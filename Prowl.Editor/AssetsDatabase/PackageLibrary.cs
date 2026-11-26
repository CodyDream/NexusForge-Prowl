// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;

using RuntimeDebug = Prowl.Runtime.Debug;

namespace Prowl.Editor;

/// <summary>
/// A folder under the project's Packages/ directory that can be mounted into Assets/ on demand.
/// <para>
/// The package keeps its own files, .meta files and GUIDs. Mounting only makes the asset database, and so
/// the project panel, scenes and builds, see them: the link lives at Assets/Packages/&lt;name&gt; and points
/// at the package's content folder. Unmounting removes the link and leaves the package's files alone.
/// </para>
/// </summary>
public sealed class LibraryPackage
{
    /// <summary>The folder name under Packages/, which is also the mount folder's name.</summary>
    public string Name { get; init; } = "";

    /// <summary>The version the package's package.json declares.</summary>
    public string Version { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>Absolute path of the package folder, Packages/&lt;name&gt;.</summary>
    public string FolderPath { get; init; } = "";

    /// <summary>Absolute path of the content folder the package mounts, usually Packages/&lt;name&gt;/Assets.</summary>
    public string ContentPath { get; init; } = "";

    /// <summary>Absolute path of the link under Assets/, Assets/Packages/&lt;name&gt;.</summary>
    public string MountPath { get; init; } = "";

    /// <summary>Whether the package is mounted, i.e. its content is part of the asset database.</summary>
    public bool IsMounted { get; init; }

    /// <summary>Whether the package ships C# scripts, which need a recompile when it is loaded or unloaded.</summary>
    public bool HasScripts { get; init; }
}

/// <summary>
/// Discovers the packages in a project's Packages/ folder and mounts or unmounts them from Assets/.
/// <para>
/// A package is a folder with a package.json and a content folder (Assets/ by default). Mounting creates a
/// directory link, not a copy, so the same files are imported either way and edits inside the editor land in
/// the package. The link is a directory junction on Windows and a symbolic link elsewhere, which both import
/// like an ordinary folder without needing administrator rights.
/// </para>
/// </summary>
public static class PackageLibrary
{
    /// <summary>Raised when a package is mounted or unmounted, with its name and new enabled state.</summary>
    public static event Action<string, bool>? StateChanged;

    /// <summary>Whether a package exists in the project and is currently mounted (enabled).</summary>
    public static bool IsEnabled(string name) => Find(name)?.IsMounted ?? false;

    /// <summary>
    /// Seeds an engine-provided package in the open project and mounts it the first time only. After that the
    /// user's enabled/disabled choice is the package link itself and survives editor restarts.
    /// </summary>
    public static void EnsureBuiltIn(string name, string description)
    {
        if (Project.Current is not { } project) return;
        string folder = Path.Combine(project.PackagesPath, name);
        if (!Directory.Exists(Path.Combine(folder, DefaultContentFolder)))
        {
            Directory.CreateDirectory(Path.Combine(folder, DefaultContentFolder));
            WriteManifest(folder, name);
        }

        string seeded = Path.Combine(folder, ".seeded");
        if (File.Exists(seeded)) return;
        File.WriteAllText(seeded, "1");
        SetLoaded(name, true, out _);
    }

    public const string ManifestFileName = "package.json";
    public const string DefaultContentFolder = "Assets";
    public const string MountFolderName = "Packages";

    /// <summary>The manifest a new package gets, written with a name, a version and its content folder.</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    /// <summary>Every package in the open project's Packages/ folder, in folder-name order.</summary>
    public static IReadOnlyList<LibraryPackage> Discover()
    {
        if (Project.Current is not { } project) return [];

        string root = project.PackagesPath;
        if (!Directory.Exists(root))
        {
            try { Directory.CreateDirectory(root); }
            catch (Exception ex)
            {
                RuntimeDebug.LogWarning($"Could not create the packages folder '{root}': {ex.Message}");
                return [];
            }
        }

        List<string> folders;
        try
        {
            folders = [.. Directory.EnumerateDirectories(root)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            RuntimeDebug.LogWarning($"Could not read the packages folder '{root}': {ex.Message}");
            return [];
        }

        var packages = new List<LibraryPackage>(folders.Count);
        foreach (string folder in folders)
        {
            string name = Path.GetFileName(folder);
            try
            {
                if (Read(project, folder, name) is { } package)
                    packages.Add(package);
            }
            catch (Exception ex)
            {
                RuntimeDebug.LogWarning($"Could not read package '{name}': {ex.Message}");
            }
        }
        return packages;
    }

    /// <summary>The package with the given folder name, or null when Packages/ has none by that name.</summary>
    public static LibraryPackage? Find(string name)
        => Discover().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Creates Packages/&lt;name&gt;/ with a package.json and an empty content folder, then loads it. An existing
    /// package folder of that name is loaded instead of failing, so Create works as "new or open".
    /// </summary>
    public static bool Create(string name, out string error)
    {
        error = "";
        if (Project.Current is not { } project)
        {
            error = "No project is open.";
            return false;
        }

        name = (name ?? "").Trim();
        if (name.Length == 0)
        {
            error = "Give the package a name.";
            return false;
        }
        if (name.StartsWith('.'))
        {
            error = "Package names cannot start with a dot.";
            return false;
        }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            error = $"'{name}' is not a valid folder name.";
            return false;
        }

        string folder = Path.Combine(project.PackagesPath, name);
        if (Directory.Exists(folder) || File.Exists(folder))
            return SetLoaded(name, true, out error);

        try
        {
            Directory.CreateDirectory(Path.Combine(folder, DefaultContentFolder));
            WriteManifest(folder, name);
            RuntimeDebug.Log($"[Packages] Created package '{name}' in Packages/{name}.");
        }
        catch (Exception ex)
        {
            error = $"Could not create the package: {ex.Message}";
            return false;
        }

        return SetLoaded(name, true, out error);
    }

    /// <summary>
    /// Mounts or unmounts a package. Loading makes its assets part of the project; unloading takes them out
    /// of the asset database again without touching the package's files. Refreshes the asset database and,
    /// when the package has scripts, asks for a recompile.
    /// </summary>
    public static bool SetLoaded(string name, bool loaded, out string error)
    {
        error = "";
        if (Project.Current is not { } project)
        {
            error = "No project is open.";
            return false;
        }

        LibraryPackage? package = Discover().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            error = $"Packages/ has no package named '{name}'.";
            return false;
        }
        if (loaded == package.IsMounted) return true;

        if (loaded)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(project.AssetsPath, MountFolderName));
                if (Directory.Exists(package.MountPath))
                {
                    error = $"Assets/{MountFolderName}/{package.Name} is a real folder, not a package link. Move it out of the way first.";
                    return false;
                }
                CreateMount(package.MountPath, package.ContentPath);
            }
            catch (Exception ex)
            {
                error = $"Could not load the package: {ex.Message}";
                return false;
            }
            RuntimeDebug.Log($"[Packages] Loaded '{package.Name}' at Assets/{MountFolderName}/{package.Name}.");
        }
        else
        {
            if (!package.IsMounted)
                return true;

            try { RemoveMount(package.MountPath); }
            catch (Exception ex)
            {
                error = $"Could not unload the package: {ex.Message}";
                return false;
            }
            RuntimeDebug.Log($"[Packages] Unloaded '{package.Name}'. Its files stay in Packages/{package.Name}.");
        }

        RefreshDatabase(package.HasScripts);
        StateChanged?.Invoke(package.Name, loaded);
        return true;
    }

    private static LibraryPackage? Read(Project project, string folder, string name)
    {
        string version = "1.0.0";
        string description = "";
        string contentFolder = DefaultContentFolder;

        string manifestPath = Path.Combine(folder, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            if (JsonNode.Parse(File.ReadAllText(manifestPath)) is JsonObject manifest)
            {
                version = manifest["version"]?.ToString() ?? version;
                description = manifest["description"]?.ToString() ?? description;
                contentFolder = manifest["assets"]?.ToString() ?? contentFolder;
            }
        }

        string contentPath = Path.Combine(folder, contentFolder);
        if (!Directory.Exists(contentPath)) return null;

        string mountPath = Path.Combine(project.AssetsPath, MountFolderName, name);
        return new LibraryPackage
        {
            Name = name,
            Version = version,
            Description = description,
            FolderPath = folder,
            ContentPath = contentPath,
            MountPath = mountPath,
            IsMounted = IsMount(mountPath),
            HasScripts = HasScripts(contentPath),
        };
    }

    private static void WriteManifest(string folder, string name)
    {
        JsonObject manifest = new()
        {
            ["name"] = name,
            ["version"] = "1.0.0",
            ["description"] = "",
            ["assets"] = DefaultContentFolder,
        };
        File.WriteAllText(Path.Combine(folder, ManifestFileName), manifest.ToJsonString(s_jsonOptions));
    }

    /// <summary>Whether the path is a directory link (junction or symbolic link) rather than a real folder.</summary>
    public static bool IsMount(string path)
    {
        try { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

    private static bool HasScripts(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Any(); }
        catch { return false; }
    }

    private static void CreateMount(string linkPath, string targetPath)
    {
        if (OperatingSystem.IsWindows())
        {
            // mklink /J makes a junction, which works without administrator rights and does not need
            // Developer Mode the way a symbolic link does.
            var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process? process = Process.Start(start);
            if (process is null) throw new IOException("cmd.exe could not be started.");
            string error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || !IsMount(linkPath))
                throw new IOException(string.IsNullOrWhiteSpace(error) ? "mklink /J failed." : error.Trim());
            return;
        }

        Directory.CreateSymbolicLink(linkPath, targetPath);
        if (!Directory.Exists(linkPath)) throw new IOException("The symbolic link could not be created.");
    }

    private static void RemoveMount(string mountPath)
    {
        // Not recursive: on Windows this removes the reparse point and never walks into the package,
        // so the package's files survive an unload.
        if (!IsMount(mountPath)) throw new IOException($"'{mountPath}' is not a package link.");
        Directory.Delete(mountPath, recursive: false);
    }

    private static void RefreshDatabase(bool hasScripts)
    {
        try { EditorAssetBackend.Instance?.Refresh(); }
        catch (Exception ex) { RuntimeDebug.LogWarning($"Asset database refresh after a package change failed: {ex.Message}"); }

        if (hasScripts) ScriptAssemblyManager.RequestRecompile();
    }
}
