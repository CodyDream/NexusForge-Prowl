// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using Prowl.Editor.Projects;

using RuntimeDebug = Prowl.Runtime.Debug;

namespace Prowl.Editor.UnityPackages;

/// <summary>
/// Tracks the Unity packages imported into the open project in Library/UnityPackages.json. The registry
/// only records metadata: removing an entry never deletes the files an import produced.
/// </summary>
public static class UnityPackageRegistry
{
    private static readonly object s_gate = new();
    private static readonly JsonSerializerOptions s_readOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>Absolute path of the registry file, or an empty string when no project is open.</summary>
    public static string RegistryPath
    {
        get
        {
            string? root = Project.Current?.RootPath;
            return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "Library", "UnityPackages.json");
        }
    }

    /// <summary>Every registered package, or an empty list when no project is open or the file is missing or unreadable.</summary>
    public static IReadOnlyList<UnityPackageRecord> List()
    {
        lock (s_gate)
        {
            string path = RegistryPath;
            return path.Length == 0 ? [] : Read(path);
        }
    }

    /// <summary>Finds a registered package by name, ignoring case. Returns null when there is no match.</summary>
    public static UnityPackageRecord? Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return List().FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds a package, replacing any existing entry with the same name (ignoring case).</summary>
    public static void Register(UnityPackageRecord record)
    {
        if (record is null) return;

        lock (s_gate)
        {
            string path = RegistryPath;
            if (path.Length == 0)
            {
                RuntimeDebug.LogWarning("[UnityPackages] No project is open; the import was not registered.");
                return;
            }

            List<UnityPackageRecord> records = Read(path);
            int index = records.FindIndex(r => string.Equals(r.Name, record.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) records[index] = record;
            else records.Add(record);

            Write(path, records, out _);
        }
    }

    /// <summary>Removes the registry entry for a package (ignoring case). No files on disk are deleted.</summary>
    public static bool Unregister(string name, out string error)
    {
        error = "";

        lock (s_gate)
        {
            string path = RegistryPath;
            if (path.Length == 0)
            {
                error = "No project is open.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = $"'{name}' is not registered.";
                return false;
            }

            List<UnityPackageRecord> records = Read(path);
            int index = string.IsNullOrEmpty(name)
                ? -1
                : records.FindIndex(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                error = $"'{name}' is not registered.";
                return false;
            }

            records.RemoveAt(index);
            return Write(path, records, out error);
        }
    }

    /// <summary>Computes the SHA-256 of a file as lowercase hex, or an empty string if it cannot be read.</summary>
    public static string ComputeHash(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return "";

        try
        {
            using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Whether the package's source file has changed since import. A missing source file reports an
    /// empty <paramref name="currentHash"/> and a warning, and counts as unchanged.
    /// </summary>
    public static bool IsChanged(string name, out string currentHash)
    {
        currentHash = "";

        UnityPackageRecord? record = Find(name);
        if (record is null)
        {
            RuntimeDebug.LogWarning($"[UnityPackages] '{name}' is not registered; cannot check for changes.");
            return false;
        }

        string source = ResolveSourcePath(record.SourcePath);
        if (!File.Exists(source))
        {
            RuntimeDebug.LogWarning($"[UnityPackages] Source file '{record.SourcePath}' for '{record.Name}' is missing.");
            return false;
        }

        currentHash = ComputeHash(source);
        if (currentHash.Length == 0) return false;

        return !string.Equals(record.SourceHash, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSourcePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return "";
        if (Path.IsPathRooted(sourcePath)) return sourcePath;

        string? root = Project.Current?.RootPath;
        return string.IsNullOrEmpty(root) ? sourcePath : Path.Combine(root, sourcePath);
    }

    private static List<UnityPackageRecord> Read(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            List<UnityPackageEntry>? entries = JsonSerializer.Deserialize<List<UnityPackageEntry>>(
                File.ReadAllText(path), s_readOptions);
            if (entries is null) return [];

            var records = new List<UnityPackageRecord>(entries.Count);
            foreach (UnityPackageEntry entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Name)) continue;
                records.Add(new UnityPackageRecord(
                    entry.Name,
                    entry.SourcePath ?? "",
                    entry.OutputFolder ?? "",
                    entry.SourceHash ?? "",
                    entry.ImportedAt ?? default,
                    entry.TrackedPaths is null ? [] : entry.TrackedPaths.Where(p => !string.IsNullOrEmpty(p)).ToList()));
            }
            return records;
        }
        catch (Exception ex)
        {
            RuntimeDebug.LogWarning($"[UnityPackages] Could not read the registry '{path}': {ex.Message}");
            return [];
        }
    }

    private static bool Write(string path, List<UnityPackageRecord> records, out string error)
    {
        error = "";

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            List<UnityPackageEntry> entries = records.Select(r => new UnityPackageEntry
            {
                Name = r.Name,
                SourcePath = r.SourcePath,
                OutputFolder = r.OutputFolder,
                SourceHash = r.SourceHash,
                ImportedAt = r.ImportedAt,
                TrackedPaths = r.TrackedPaths is null ? [] : [.. r.TrackedPaths],
            }).ToList();

            File.WriteAllText(path, JsonSerializer.Serialize(entries, s_writeOptions));
            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not write the Unity package registry: {ex.Message}";
            RuntimeDebug.LogWarning($"[UnityPackages] {error}");
            return false;
        }
    }
}

/// <summary>JSON shape of one registry entry. Nullable members keep a partially corrupt file from losing every entry.</summary>
internal sealed class UnityPackageEntry
{
    public string? Name { get; set; }
    public string? SourcePath { get; set; }
    public string? OutputFolder { get; set; }
    public string? SourceHash { get; set; }
    public DateTime? ImportedAt { get; set; }
    public List<string>? TrackedPaths { get; set; }
}
