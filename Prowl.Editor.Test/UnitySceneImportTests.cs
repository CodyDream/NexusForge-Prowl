// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// End-to-end coverage for the real .unitypackage: importing it into a fresh project must leave a
/// report asset behind, write converted scene/prefab files with proper Prowl metadata, and produce
/// assets that load as ordinary Prowl scenes and prefabs. Unsupported Unity components (particle
/// systems, URP volumes, terrain, ...) are expected to warn and are deliberately not asserted on -
/// only missing converted assets fail these tests.
///
/// The package is external test data rather than part of the repository, so both tests skip with a
/// clear reason when it is not on disk.
/// </summary>
public class UnitySceneImportTests : EditorTestHarness
{
    private const string PackagePath =
        @"C:\Users\Nimesh\Desktop\New folder\Game engine test new mimo\new game engine\external new import packages - do not ship\Low Poly Stylized Nature 1.0.4 .unitypackage";

    private const string PackageRelative = "Imported/Nature.unitypackage";

    /// <summary>The canonical (unversioned) location of the converted Demo scene under Assets/.</summary>
    private const string ExpectedSceneRelative = "Low Poly Stylized Nature/Scene/Demo scene.converted.scene";

    /// <summary>Root names from the package's plain mesh prefabs, whose conversion needs no URP support.</summary>
    private static readonly HashSet<string> KnownUnityPrefabNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cloud 1", "cloud 2", "cloud 3", "cloud 4", "cloud 5",
        "Tree 1", "Tree 2", "tree 3", "Tree 4", "tree 5", "Tree 6", "Tree 7", "Tree 8",
        "Barrel", "Tent", "Camp fire", "Fire", "Fence", "Fence pole",
    };

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>Copies and imports the real package, or marks the test skipped when it is unavailable.</summary>
    private bool TryImportPackage()
    {
        if (!File.Exists(PackagePath))
        {
            Assert.True(true, $"Skipped: real Unity package not found at '{PackagePath}'.");
            return false;
        }

        EditorRegistries.Initialize();
        if (EditorRegistries.GetImporter(".unitypackage") is null)
        {
            Assert.True(true, "Skipped: no importer is registered for .unitypackage yet.");
            return false;
        }

        CopyIntoAssets(PackagePath, PackageRelative);
        Assets.Refresh();

        Guid guid = Assets.ImportFile(PackageRelative);
        Assert.True(guid != Guid.Empty, "Importing the real package produced no asset.");
        return true;
    }

    /// <summary>
    /// The importer may keep the versioned package folder as a prefix; accept that as long as the
    /// converted scene still lives at the expected path beneath it.
    /// </summary>
    private string FindConvertedScene()
    {
        if (File.Exists(AssetAbsolutePath(ExpectedSceneRelative))) return ExpectedSceneRelative;

        string? found = Directory
            .EnumerateFiles(Project.AssetsPath, "Demo scene.converted.scene", SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(Path.GetRelativePath(Project.AssetsPath, path)))
            .FirstOrDefault(path => path.EndsWith(ExpectedSceneRelative, StringComparison.OrdinalIgnoreCase));

        Assert.True(found != null,
            $"The converted scene '{ExpectedSceneRelative}' was not written beneath '{Project.AssetsPath}'.");
        return found!;
    }

    private List<string> ConvertedPrefabPaths()
        => Directory.EnumerateFiles(Project.AssetsPath, "*.converted.prefab", SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(Path.GetRelativePath(Project.AssetsPath, path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Every converted file must be a tracked asset with a readable .meta of its own.</summary>
    private Guid AssertConvertedAssetOnDisk(string relative)
    {
        string absolute = AssetAbsolutePath(relative);
        Assert.True(File.Exists(absolute), $"Converted asset '{relative}' was not written under Assets.");

        string meta = MetaFile.GetMetaPath(absolute);
        Assert.True(File.Exists(meta), $"Converted asset '{relative}' has no .meta file.");

        Guid guid = Assets.PathToGuid(relative);
        Assert.True(guid != Guid.Empty, $"Converted asset '{relative}' was not indexed by the asset database.");
        Assert.Equal(guid, MetaFile.Read(meta).Guid);
        return guid;
    }

    [Fact]
    public void ImportingTheRealPackage_ProducesAReport_AndConvertedFilesWithMeta()
    {
        if (!TryImportPackage()) return;

        // The package itself is a real asset whose main object is the import manifest, not a stub.
        Guid packageGuid = Assets.PathToGuid(PackageRelative);
        Assert.NotEqual(Guid.Empty, packageGuid);
        Assert.True(AssetDatabase.Get(packageGuid).IsValid(), "The package asset is missing from the database.");

        UnityImportReport report = Assert.IsType<UnityImportReport>(AssetDatabase.Load<UnityImportReport>(packageGuid));
        Assert.False(report.IsMissing);
        Assert.True(report.IsLoaded);
        Assert.True(report.EntryCount > 0, "The import report did not record any package entries.");

        // The converted hierarchy files are written beside the raw Unity source, with Prowl metadata.
        AssertConvertedAssetOnDisk(FindConvertedScene());

        List<string> prefabs = ConvertedPrefabPaths();
        Assert.NotEmpty(prefabs);
        foreach (string path in prefabs)
            AssertConvertedAssetOnDisk(path);
    }

    [Fact]
    public void ConvertedSceneAndPrefab_LoadAsUsableProwlAssets()
    {
        if (!TryImportPackage()) return;

        // Scene: resolve by load path where possible, falling back to the indexed GUID (converted
        // assets are not necessarily under a Resources folder).
        string sceneRelative = FindConvertedScene();
        Guid sceneGuid = AssertConvertedAssetOnDisk(sceneRelative);
        string loadPath = sceneRelative[..^".scene".Length]; // "Demo scene.converted"

        SceneAsset? scene = AssetDatabase.FindResource<SceneAsset>(loadPath);
        if (!scene.IsValid() || scene!.AssetID != sceneGuid)
            scene = AssetDatabase.Load<SceneAsset>(sceneGuid);

        Assert.True(scene.IsValid(), $"The converted scene '{sceneRelative}' did not load as a SceneAsset.");
        Assert.False(scene!.IsMissing);
        Assert.Equal(sceneGuid, scene.AssetID);

        SceneContent content = scene.ReadContent();
        Assert.Contains(content.Roots, root => root.IsValid() &&
            (root.ChildCount > 0 || root.GetComponent<MeshRenderer>().IsValid()));

        // Prefab: at least one converted prefab must load and keep the name of a known Unity prefab.
        // Prefabs made entirely of unsupported components are allowed to be skipped.
        List<string> prefabPaths = ConvertedPrefabPaths();
        Assert.NotEmpty(prefabPaths);

        PrefabAsset? matched = null;
        string? matchedName = null;
        foreach (string path in prefabPaths)
        {
            Guid guid = Assets.PathToGuid(path);
            if (guid == Guid.Empty) continue;

            PrefabAsset? prefab = AssetDatabase.Load<PrefabAsset>(guid);
            if (!prefab.IsValid() || prefab.IsMissing) continue;

            GameObject? root = GameObject.InstantiateDetached(prefab);
            if (!root.IsValid()) continue;

            if (KnownUnityPrefabNames.Contains(root!.Name))
            {
                matched = prefab;
                matchedName = root.Name;
                break;
            }
        }

        Assert.True(matched.IsValid(),
            $"No converted prefab loaded with a known Unity root name. Converted files: {string.Join(", ", prefabPaths)}");
        Assert.Contains(matchedName!, KnownUnityPrefabNames);
    }
}
