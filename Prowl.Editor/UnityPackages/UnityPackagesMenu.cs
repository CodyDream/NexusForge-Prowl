// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.UnityPackages;

/// <summary> Menu entry points for inspecting and removing imported Unity packages. </summary>
public static class UnityPackagesMenu
{
    [MenuItem("Tools/Unity Packages/Refresh List")]
    public static void RefreshList()
    {
        IReadOnlyList<UnityPackageRecord> records = UnityPackageRegistry.List();
        Debug.Log($"[UnityPackages] Registry: {UnityPackageRegistry.RegistryPath} ({records.Count} package(s)).");
        if (records.Count == 0) return;

        foreach (UnityPackageRecord record in records)
        {
            bool changed = UnityPackageRegistry.IsChanged(record.Name, out string hash);
            Debug.Log($"[UnityPackages] {record.Name}: source={record.SourcePath}, output={record.OutputFolder}, "
                      + $"imported={record.ImportedAt:o}, changed={changed}, hash={(hash.Length > 0 ? hash : record.SourceHash)}, "
                      + $"tracked=[{string.Join(", ", record.TrackedPaths)}]");
        }
    }

    [MenuItem("Tools/Unity Packages/Remove Selected...", true)]
    public static bool RemoveSelectedEnabled() => UnityPackageRegistry.List().Count > 0;

    /// <summary>
    /// There is no dedicated Unity-package list panel yet, so "selected" is resolved by choosing the
    /// registered package's source archive. The delete itself goes through the same guarded CLI path.
    /// </summary>
    [MenuItem("Tools/Unity Packages/Remove Selected...")]
    public static void RemoveSelected()
    {
        IReadOnlyList<UnityPackageRecord> records = UnityPackageRegistry.List();
        if (records.Count == 0)
        {
            Debug.LogWarning("[UnityPackages] No packages are registered; nothing to remove.");
            return;
        }

        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            UnityPackageRecord? match = records.FirstOrDefault(r => IsSource(r, path));
            if (match is null)
            {
                Debug.LogWarning($"[UnityPackages] '{path}' is not the source of a registered package.");
                return;
            }

            Origami.Confirm($"Remove {match.Name}?",
                $"Delete every tracked file and folder for '{match.Name}' from Assets, then unregister it?",
                () =>
                {
                    if (Project.Current is not { } project)
                    {
                        Debug.LogWarning("[UnityPackages] No project is open; nothing was removed.");
                        return;
                    }

                    (List<string> deleted, List<string> skipped) = UnityPackagesCli.DeleteTracked(project, match);
                    if (deleted.Count > 0) EditorAssetBackend.Instance?.Refresh();
                    UnityPackageRegistry.Unregister(match.Name, out string error);

                    Debug.Log($"[UnityPackages] Removed '{match.Name}': {deleted.Count} deleted, {skipped.Count} skipped."
                              + (error.Length > 0 ? $" Registry error: {error}" : ""));
                });
        },
        startPath: Project.Current?.PackagesPath,
        filters: ["*.unitypackage"],
        filterLabels: ["Unity package sources"]);
    }

    private static bool IsSource(UnityPackageRecord record, string path)
    {
        string root = Project.Current?.RootPath ?? "";
        string expected = Path.IsPathRooted(record.SourcePath)
            ? record.SourcePath
            : Path.Combine(root, record.SourcePath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        try
        {
            return string.Equals(Path.GetFullPath(expected), Path.GetFullPath(path), comparison);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
