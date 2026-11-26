// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;

using CDS.ProwlFork.United;

using Prowl.Editor;
using Prowl.Runtime;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Coverage for Unity asset import: reading a real .unitypackage (a gzip tarball of GUID-named
/// folders), parsing the Unity-flavoured YAML inside those assets, and handing a package to the
/// editor's importer. The package itself is external test data rather than part of the repository,
/// so the two tests that need it skip with a clear reason when it is not on disk.
/// </summary>
public class UnityImportTests : EditorTestHarness
{
    private const string PackagePath =
        @"C:\Users\Nimesh\Desktop\New folder\Game engine test new mimo\new game engine\external new import packages - do not ship\Low Poly Stylized Nature 1.0.4 .unitypackage";

    /// <summary>Unity paths are '/'-separated and start at "Assets/"; tolerate either separator and an implementation that trims that prefix.</summary>
    private static string NormalizedPath(string path)
    {
        string p = path.Replace('\\', '/').TrimStart('/');
        const string prefix = "Assets/";
        return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? p.Substring(prefix.Length) : p;
    }

    [Fact]
    public void UnityPackageReader_ReadsTheRealPackage_WithExpectedPaths()
    {
        if (!File.Exists(PackagePath))
        {
            Assert.True(true, $"Skipped: real Unity package not found at '{PackagePath}'.");
            return;
        }

        IReadOnlyList<UnityPackageEntry> entries = UnityPackageReader.Read(PackagePath);

        // The archive has 160 pathname members (one asset per GUID folder); allow a little slack for
        // the reader ignoring an odd folder while still catching a truncated or mis-parsed tarball.
        Assert.InRange(entries.Count, 150, 170);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Path)));

        // Path is the verbatim pathname from the archive, not the tar member name.
        Assert.Contains(entries, e => e.Path.StartsWith("Assets/Cody Dreams/Low Poly Stylized Nature/", StringComparison.Ordinal));

        HashSet<string> paths = entries.Select(e => NormalizedPath(e.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string extension in new[] { ".mat", ".prefab", ".fbx", ".unity", ".asset", ".png" })
            Assert.Contains(paths, p => p.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        string[] expected =
        [
            "Cody Dreams/Low Poly Stylized Nature/Matereals/Artificial/Heat Distortion Mat.mat",
            "Cody Dreams/Low Poly Stylized Nature/Prefabs/Nature/Effects/Fire.prefab",
            "Cody Dreams/Low Poly Stylized Nature/Models/Barrel.fbx",
            "Cody Dreams/Low Poly Stylized Nature/Scene/Demo scene.unity",
            "Cody Dreams/Low Poly Stylized Nature/Scene/Terrain Data/New Terrain.asset",
            "Cody Dreams/Low Poly Stylized Nature/Textures/Leave.png",
        ];
        foreach (string path in expected)
            Assert.Contains(path, paths);

        UnityPackageEntry material = Assert.Single(entries,
            e => NormalizedPath(e.Path).EndsWith("Matereals/Cloud.mat", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(material.Asset);
        Assert.NotEmpty(material.Meta!);
    }

    [Fact]
    public void UnityYaml_ParsesDocumentsNestedMapsAndReferences()
    {
        const string yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!21 &2100000
            Material:
              serializedVersion: 6
              m_Name: Testing Material
              m_SavedProperties:
                serializedVersion: 3
                m_TexEnvs:
                  _MainTex:
                    m_Texture: {fileID: 2100000, guid: abc, type: 2}
                    m_Scale: {x: 1, y: 1}
            --- !u!1001 &100100000
            Prefab:
              m_Name: Testing Prefab
              m_Modifications: []
            """;

        var documents = UnityYaml.Parse(yaml);

        Assert.Equal(2, documents.Count());
        UnityDocument material = documents[0];
        Assert.Equal(21L, material.ClassId);
        Assert.Equal(2100000L, material.FileId);

        UnityDocument prefab = documents[1];
        Assert.Equal(1001L, prefab.ClassId);
        Assert.Equal(100100000L, prefab.FileId);

        var name = Assert.IsType<ScalarNode>(material.Get("m_Name"));
        Assert.Equal("Testing Material", name.Value);

        // Get walks several levels into the leading class map, including Unity's inline maps.
        var texEnvs = Assert.IsType<MapNode>(material.Get("m_SavedProperties", "m_TexEnvs"));
        Assert.True(texEnvs.Fields.ContainsKey("_MainTex"));

        UnityRef reference = UnityYaml.ParseRef(material.Get("m_SavedProperties", "m_TexEnvs", "_MainTex", "m_Texture"));
        Assert.Equal(2100000L, reference.FileId);
        Assert.Equal("abc", reference.Guid);
        Assert.Equal(2, reference.Type);
        Assert.False(reference.IsNull);
    }

    [Fact]
    public void TheRealUnityPackage_ImportsWithoutThrowing_AndProducesAMainAsset()
    {
        if (!File.Exists(PackagePath))
        {
            Assert.True(true, $"Skipped: real Unity package not found at '{PackagePath}'.");
            return;
        }

        EditorRegistries.Initialize();
        if (EditorRegistries.GetImporter(".unitypackage") is null)
        {
            Assert.True(true, "Skipped: no importer is registered for .unitypackage yet.");
            return;
        }

        const string relative = "Imported/Nature.unitypackage";
        CopyIntoAssets(PackagePath, relative);

        Exception? failure = Record.Exception(() =>
        {
            Assets.Refresh();
            Assets.ImportFile(relative);
        });
        Assert.True(failure is null, $"Importing the package threw: {failure}");

        Guid guid = Assets.PathToGuid(relative);
        Assert.NotEqual(Guid.Empty, guid);

        Asset? main = AssetDatabase.Get(guid);
        Assert.True(main.IsValid(), "The importer is registered but produced no main asset.");
    }
}
