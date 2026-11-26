// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.UnityPackages;

/// <summary>
/// Project-panel action for importing (or re-importing) the selected .unitypackage file.
/// The entries are discovered automatically by EditorRegistries through MenuItemAttribute.Scan;
/// "Assets/Create" is the one menu root ProjectPanel builds into its right-click menu without
/// requiring an edit to the panel itself.
/// </summary>
public static class UnityPackageContextMenu
{
    /// <summary>Menu path used by both the action and its validator.</summary>
    public const string MenuPath = "Assets/Create/Import Unity Package";

    private const string Extension = ".unitypackage";

    /// <summary>True when the active Project-panel selection is a .unitypackage file.</summary>
    public static bool IsUnityPackage(ContentItem? item)
        => item is { IsFolder: false, IsSubAsset: false }
           && item.RelativePath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adds the action directly to a context menu, but only when a .unitypackage is selected.
    /// Exposed so a panel can place it as a top-level item with a single call.
    /// </summary>
    public static void AddToMenu(ContextBuilder builder)
    {
        if (!IsUnityPackage(Selection.GetActiveAs<ContentItem>())) return;
        builder.Item("Import Unity Package", ImportUnityPackage, icon: EditorIcons.FileZipper);
    }

    public static void ImportUnityPackage()
    {
        ContentItem? item = Selection.GetActiveAs<ContentItem>();
        if (!IsUnityPackage(item))
        {
            Debug.LogWarning("[UnityPackages] Select a .unitypackage file in the Project panel first.");
            return;
        }

        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null)
        {
            Debug.LogError("[UnityPackages] The asset backend is not available; nothing was imported.");
            return;
        }

        string relativePath = EditorAssetBackend.NormalizePath(item!.RelativePath);
        try
        {
            // Re-imports in place when the path is already tracked, so this also handles updates.
            Guid guid = backend.ImportFile(relativePath);
            if (guid == Guid.Empty)
            {
                Debug.LogError($"[UnityPackages] Failed to import '{relativePath}'. See the logs for details.");
                return;
            }

            Debug.LogSuccess($"[UnityPackages] Imported '{item.Name}' from '{relativePath}'.");
            TryRegister(relativePath, guid);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityPackages] Failed to import '{relativePath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Records the import in the package registry. UnityPackageAutoRegister normally does this from
    /// the backend's import event; this best-effort fallback covers imports that do not raise it.
    /// A failure here must never turn a successful import into an error.
    /// </summary>
    private static void TryRegister(string relativePath, Guid guid)
    {
        try
        {
            string source = Project.Current is { } project
                ? Path.GetFullPath(Path.Combine(project.AssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                : relativePath;
            string outputFolder = "";

            UnityImportReport? report = AssetDatabase.Load<UnityImportReport>(guid);
            if (report.IsValid())
            {
                if (!string.IsNullOrWhiteSpace(report.SourcePath)) source = report.SourcePath;
                outputFolder = report.OutputFolder;
            }

            var tracked = new List<string>();
            if (!string.IsNullOrWhiteSpace(outputFolder)) tracked.Add(outputFolder);
            tracked.Add(relativePath);

            UnityPackageRegistry.Register(new UnityPackageRecord(
                Path.GetFileNameWithoutExtension(relativePath),
                source,
                outputFolder,
                UnityPackageRegistry.ComputeHash(source),
                DateTime.UtcNow,
                tracked));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityPackages] Import completed but could not be registered: {ex.Message}");
        }
    }
}
