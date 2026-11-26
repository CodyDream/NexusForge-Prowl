using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.UnityPackages;

/// <summary>
/// Keeps <see cref="UnityPackageRegistry"/> in sync with imports performed by
/// <c>UnityPackageImporter</c> without requiring the importer to know about the registry.
/// </summary>
internal static class UnityPackageAutoRegister
{
    private static bool _hooked;

    [InitializeOnLoad]
    public static void Hook()
    {
        if (_hooked) return;
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null) return;
        backend.OnAssetsImported += OnAssetsImported;
        _hooked = true;
    }

    private static void OnAssetsImported(string[] paths)
    {
        if (Project.Current == null || paths == null || paths.Length == 0) return;

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) continue;

            Guid guid = EditorAssetBackend.Instance.PathToGuid(path);
            if (guid == Guid.Empty) continue;

            UnityImportReport? report = AssetDatabase.Load<UnityImportReport>(guid);
            if (report.IsNotValid()) continue;

            string source = report.SourcePath;
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) continue;

            string sourceRelative = Path.GetRelativePath(Project.Current.AssetsPath, source)
                .Replace('\\', '/');

            var tracked = new List<string> { report.OutputFolder };
            if (!string.IsNullOrEmpty(sourceRelative) &&
                !sourceRelative.StartsWith("..", StringComparison.Ordinal))
                tracked.Add(sourceRelative);

            UnityPackageRegistry.Register(new UnityPackageRecord(
                Path.GetFileNameWithoutExtension(source),
                source,
                report.OutputFolder,
                UnityPackageRegistry.ComputeHash(source),
                DateTime.UtcNow,
                tracked));
        }
    }
}
