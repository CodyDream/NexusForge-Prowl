// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.UnityPackages;

/// <summary> CLI surface for the Unity package registry. Commands run on the editor's main thread. </summary>
public static class UnityPackagesCli
{
    public enum Verb { List, Remove, Update }

    [CliCommand("unity_packages", "Lists imported Unity packages, removes one together with its files, or re-imports a changed source archive")]
    public static object UnityPackages(
        [CliArg("verb", "list, remove or update")] Verb verb = Verb.List,
        [CliArg("name", "Registered package name, for remove and update")] string name = "")
        => verb switch
        {
            Verb.List => ListPackages(),
            Verb.Remove => RemovePackage(name),
            Verb.Update => UpdatePackage(name),
            _ => throw new CliException($"Unknown verb '{verb}'."),
        };

    private static object ListPackages()
    {
        IReadOnlyList<UnityPackageRecord> records = UnityPackageRegistry.List();
        return new
        {
            registryPath = UnityPackageRegistry.RegistryPath,
            count = records.Count,
            packages = records.Select(r =>
            {
                bool changed = UnityPackageRegistry.IsChanged(r.Name, out string currentHash);
                return new
                {
                    name = r.Name,
                    sourcePath = r.SourcePath,
                    outputFolder = r.OutputFolder,
                    sourceHash = r.SourceHash,
                    currentHash,
                    importedAt = r.ImportedAt.ToString("o"),
                    trackedPaths = r.TrackedPaths.ToList(),
                    changed,
                };
            }).ToList(),
        };
    }

    private static object RemovePackage(string name)
    {
        UnityPackageRecord record = Require(name);
        if (Project.Current is not { } project)
            throw new CliException("No project is open.");

        (List<string> deleted, List<string> skipped) = DeleteTracked(project, record);

        // Refresh once, after every file is gone, so the database reconciles against the disk in one pass.
        if (deleted.Count > 0)
        {
            EditorAssetBackend? backend = EditorAssetBackend.Instance;
            if (backend is not null) backend.Refresh();
            else Debug.LogWarning("[UnityPackages] The asset backend was unavailable; the database was not refreshed after deletion.");
        }

        UnityPackageRegistry.Unregister(record.Name, out string error);
        return new
        {
            name = record.Name,
            removed = error.Length == 0,
            deleted,
            skipped,
            error,
        };
    }

    /// <summary>
    /// Deletes every tracked file or folder that is inside the current project's Assets directory.
    /// Missing paths are skipped, and any path that is (or contains) a reparse point is refused so
    /// junctions and symlinks are never traversed.
    /// </summary>
    internal static (List<string> Deleted, List<string> Skipped) DeleteTracked(Project project, UnityPackageRecord record)
    {
        var deleted = new List<string>();
        var skipped = new List<string>();

        string assetsRoot = Path.GetFullPath(project.AssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = PathComparison();

        foreach (string tracked in record.TrackedPaths)
        {
            if (string.IsNullOrWhiteSpace(tracked)) continue;

            string? absolute = ResolveInsideAssets(assetsRoot, tracked, comparison);
            if (absolute == null)
            {
                skipped.Add(tracked);
                Debug.LogWarning($"[UnityPackages] Refused to delete '{tracked}' from '{record.Name}': it is not inside Assets.");
                continue;
            }

            string relative = NormalizeRelative(assetsRoot, absolute);
            if (!Directory.Exists(absolute) && !File.Exists(absolute))
            {
                skipped.Add(relative);
                continue;
            }

            if (CrossesReparsePoint(assetsRoot, absolute, comparison)
                || (Directory.Exists(absolute) && ContainsReparsePoint(absolute)))
            {
                skipped.Add(relative);
                Debug.LogWarning($"[UnityPackages] Refused to delete '{relative}' from '{record.Name}': it is, or contains, a junction or symlink.");
                continue;
            }

            try
            {
                if (Directory.Exists(absolute))
                {
                    Directory.Delete(absolute, recursive: true);
                    string folderMeta = MetaFile.GetMetaPath(absolute);
                    if (File.Exists(folderMeta)) File.Delete(folderMeta);
                }
                else if (File.Exists(absolute))
                {
                    File.Delete(absolute);
                    string meta = MetaFile.GetMetaPath(absolute);
                    if (File.Exists(meta)) File.Delete(meta);
                }
                else
                {
                    // An earlier import step, or the user, may already have removed it.
                    skipped.Add(relative);
                    continue;
                }
            }
            catch (Exception ex)
            {
                skipped.Add(relative);
                Debug.LogWarning($"[UnityPackages] Could not delete '{relative}' from '{record.Name}': {ex.Message}");
                continue;
            }

            deleted.Add(relative);
            Debug.Log($"[UnityPackages] Deleted '{relative}' while removing '{record.Name}'.");
        }

        return (deleted, skipped);
    }

    private static object UpdatePackage(string name)
    {
        UnityPackageRecord record = Require(name);
        if (Project.Current is not { } project)
            throw new CliException("No project is open.");

        if (!UnityPackageRegistry.IsChanged(record.Name, out string hash))
            return new
            {
                name = record.Name,
                updated = false,
                reason = hash.Length == 0 ? "Source file is missing or unreadable." : "Source file has not changed.",
                hash = hash.Length == 0 ? record.SourceHash : hash,
            };

        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend is null)
            return new { name = record.Name, updated = false, reason = "The asset importer is not available." };

        string source = Path.IsPathRooted(record.SourcePath)
            ? record.SourcePath
            : Path.Combine(project.RootPath, record.SourcePath);
        if (!File.Exists(source))
            return new { name = record.Name, updated = false, reason = $"Source file '{source}' does not exist." };

        string assetsRoot = Path.GetFullPath(project.AssetsPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = PathComparison();

        // ImportFile only accepts paths under Assets. Reuse the package's original location when the
        // registry recorded one, so the importer extracts into the same output folder as before.
        string relative = record.TrackedPaths.FirstOrDefault(p => IsUsablePackagePath(assetsRoot, p, comparison))
                          ?? DefaultStagingPath(source);
        string absolute = Path.GetFullPath(Path.Combine(assetsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            if (!PathsEqual(source, absolute, comparison))
            {
                string? directory = Path.GetDirectoryName(absolute);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, absolute, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityPackages] Could not stage '{source}' at '{relative}' for re-import: {ex.Message}");
            return new { name = record.Name, updated = false, reason = ex.Message };
        }

        Guid guid = backend.ImportFile(EditorAssetBackend.NormalizePath(relative));
        if (guid == Guid.Empty)
        {
            Debug.LogError($"[UnityPackages] Re-import of '{record.Name}' from '{relative}' failed. See the logs.");
            return new { name = record.Name, updated = false, reason = "The importer did not produce an asset." };
        }

        backend.Refresh();

        string outputFolder = record.OutputFolder;
        UnityImportReport? report = AssetDatabase.Load<UnityImportReport>(guid);
        if (report.IsValid() && !string.IsNullOrWhiteSpace(report.OutputFolder))
            outputFolder = report.OutputFolder.Replace('\\', '/').Trim('/');

        var tracked = new List<string>(record.TrackedPaths);
        foreach (string path in new[] { relative, outputFolder })
            if (!string.IsNullOrWhiteSpace(path) && !tracked.Contains(path, StringComparer.OrdinalIgnoreCase))
                tracked.Add(path);

        var updated = new UnityPackageRecord(record.Name, source, outputFolder, hash, DateTime.UtcNow, tracked);
        UnityPackageRegistry.Register(updated);
        Debug.LogSuccess($"[UnityPackages] Re-imported '{record.Name}' from '{source}'.");

        return new
        {
            name = updated.Name,
            updated = true,
            previousHash = record.SourceHash,
            hash = updated.SourceHash,
            outputFolder = updated.OutputFolder,
            importedAt = updated.ImportedAt.ToString("o"),
            packageGuid = guid.ToString(),
            trackedPaths = tracked,
            registryPath = UnityPackageRegistry.RegistryPath,
        };
    }

    private static UnityPackageRecord Require(string name)
        => UnityPackageRegistry.Find(name)
           ?? throw new CliException($"Package '{name}' is not registered. Run 'unity_packages --verb list' to see what is.");

    private static bool IsUsablePackagePath(string assetsRoot, string tracked, StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(tracked)) return false;
        if (!tracked.TrimEnd('/', '\\').EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return false;
        string? full = ResolveInsideAssets(assetsRoot, tracked, comparison);
        return full != null && !CrossesReparsePoint(assetsRoot, full, comparison);
    }

    private static string DefaultStagingPath(string source)
    {
        string fileName = Path.GetFileName(source);
        foreach (char invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "UnityPackage.unitypackage";
        return "UnityPackages/" + fileName;
    }

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Resolves a tracked path, accepting either an absolute path or one relative to Assets.</summary>
    private static string? ResolveInsideAssets(string assetsRoot, string tracked, StringComparison comparison)
    {
        string candidate = tracked.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(candidate) ? candidate : Path.Combine(assetsRoot, candidate));
        }
        catch (Exception)
        {
            return null;
        }

        return IsInside(assetsRoot, full, comparison) ? full : null;
    }

    private static bool IsInside(string assetsRoot, string full, StringComparison comparison)
        => !PathsEqual(assetsRoot, full, comparison)
           && full.StartsWith(assetsRoot + Path.DirectorySeparatorChar, comparison);

    private static bool PathsEqual(string a, string b, StringComparison comparison)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                         Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                         comparison);

    private static string NormalizeRelative(string assetsRoot, string absolute)
        => EditorAssetBackend.NormalizePath(Path.GetRelativePath(assetsRoot, absolute));

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception) { return true; } // Missing, broken or unreadable links are refused too.
    }

    /// <summary>Refuses a path nested below a junction, not just a junction itself.</summary>
    private static bool CrossesReparsePoint(string assetsRoot, string path, StringComparison comparison)
    {
        string current = path;
        while (IsInside(assetsRoot, current, comparison))
        {
            if (IsReparsePoint(current)) return true;
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current, comparison)) break;
            current = parent;
        }
        return false;
    }

    /// <summary>Checks every descendant before a recursive delete, without ever entering a junction.</summary>
    private static bool ContainsReparsePoint(string folder)
    {
        var pending = new Stack<string>();
        pending.Push(folder);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
            {
                if (IsReparsePoint(entry)) return true;
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }

        return false;
    }
}
