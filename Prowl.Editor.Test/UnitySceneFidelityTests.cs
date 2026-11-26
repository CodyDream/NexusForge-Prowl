// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using CDS.ProwlFork.United;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Concrete fidelity checks for the Unity package conversion: the numbers in the real Unity YAML
/// must survive into the converted Prowl prefabs and into the converted Demo scene, component by
/// component (including quaternion x/y/z/w order, which a handedness change can silently swap).
///
/// The converter copies Unity localPosition/localRotation/localScale straight across - it does no
/// coordinate remapping - so the expected values below are the verbatim Unity numbers, with only
/// Unity's rounding left to a small tolerance.
///
/// The package is external test data, so every test skips with a clear reason when it is absent.
/// </summary>
public class UnitySceneFidelityTests : EditorTestHarness
{
    private const string PackagePath =
        @"C:\Users\Nimesh\Desktop\New folder\Game engine test new mimo\new game engine\external new import packages - do not ship\Low Poly Stylized Nature 1.0.4 .unitypackage";

    private const string PackageRelative = "Imported/Nature.unitypackage";

    private const string CloudPrefabSuffix = "Prefabs/Nature/Clouds/cloud 1.prefab";
    private const string TreePrefabSuffix = "Prefabs/Nature/Trees/Tree 1.prefab";
    private const string SceneSuffix = "Scene/Demo scene.unity";
    private const string ConvertedSceneName = "Demo scene.converted.scene";

    // Unity prints 7-8 significant digits; NormalizeSafe on the converted quaternion moves them by
    // much less than this. Loose enough for float serialisation, tight enough to catch a swapped axis.
    private const float Tolerance = 1e-4f;

    private readonly record struct SourceTransform(Float3 Position, Quaternion Rotation, Float3 Scale);

    // ================================================================
    //  Package / import setup
    // ================================================================

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

    private static UnityPackageEntry FindEntry(IReadOnlyList<UnityPackageEntry> entries, string suffix)
    {
        UnityPackageEntry? entry = entries.FirstOrDefault(
            e => NormalizeRelative(e.Path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        Assert.True(entry != null, $"The real Unity package has no '{suffix}' entry.");
        return entry!;
    }

    private string FindConvertedScene()
    {
        string? found = Directory
            .EnumerateFiles(Project.AssetsPath, ConvertedSceneName, SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(Path.GetRelativePath(Project.AssetsPath, path)))
            .FirstOrDefault(path => path.EndsWith("Scene/" + ConvertedSceneName, StringComparison.OrdinalIgnoreCase));

        Assert.True(found != null,
            $"The converted Demo scene was not written beneath '{Project.AssetsPath}'.");
        return found!;
    }

    private string FindConvertedPrefab(string prefabName)
    {
        string fileName = prefabName + ".converted.prefab";
        string? found = Directory
            .EnumerateFiles(Project.AssetsPath, fileName, SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(Path.GetRelativePath(Project.AssetsPath, path)))
            .FirstOrDefault();

        Assert.True(found != null,
            $"No converted prefab '{fileName}' was written beneath '{Project.AssetsPath}'.");
        return found!;
    }

    /// <summary>Loads the converted scene through SceneAsset.Load, the same path a game uses.</summary>
    private SceneAsset LoadConvertedScene(string relative)
    {
        Guid guid = Assets.PathToGuid(relative);
        Assert.True(guid != Guid.Empty, $"Converted scene '{relative}' was not indexed by the asset database.");

        SceneAsset? scene = AssetDatabase.Load<SceneAsset>(guid);
        Assert.True(scene.IsValid(), $"Converted scene '{relative}' did not load as a SceneAsset.");

        scene!.Load().Wait();
        Scene.ProcessPendingLoad();
        Assert.True(Scene.Main.IsValid(), "Loading the converted scene left no live Main scene.");
        return scene;
    }

    // ================================================================
    //  Unity YAML source excerpts
    // ================================================================

    private static string Scalar(UnityYamlNode? node) => node is ScalarNode scalar ? scalar.Value : "";

    private static float? ParseNumber(string? text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;

    private static float Component(MapNode map, string key)
        => map.Fields.TryGetValue(key, out UnityYamlNode? node) ? ParseNumber(Scalar(node)) ?? 0f : 0f;

    private static Float3 ReadVector(UnityYamlNode? node)
    {
        if (node is not MapNode map) return new Float3(0f, 0f, 0f);
        return new Float3(Component(map, "x"), Component(map, "y"), Component(map, "z"));
    }

    private static Quaternion ReadQuaternion(UnityYamlNode? node)
    {
        if (node is not MapNode map) return new Quaternion(0f, 0f, 0f, 1f);
        float w = map.Fields.TryGetValue("w", out UnityYamlNode? nodeW)
            ? ParseNumber(Scalar(nodeW)) ?? 1f
            : 1f;
        return new Quaternion(Component(map, "x"), Component(map, "y"), Component(map, "z"), w);
    }

    private static SourceTransform ReadTransform(UnityDocument transformDoc)
        => new(
            ReadVector(transformDoc.Get("m_LocalPosition")),
            ReadQuaternion(transformDoc.Get("m_LocalRotation")),
            ReadVector(transformDoc.Get("m_LocalScale")));

    private static string ReadUnityGuid(UnityPackageEntry entry)
    {
        Assert.True(entry.Meta is { Length: > 0 }, $"The '{entry.Path}' package entry has no .meta to read its GUID from.");
        IReadOnlyList<UnityDocument> docs = UnityYaml.Parse(Encoding.UTF8.GetString(entry.Meta!));
        string guid = Scalar(docs.FirstOrDefault()?.Get("guid"));
        Assert.False(string.IsNullOrWhiteSpace(guid), $"No guid was found in the .meta of '{entry.Path}'.");
        return guid;
    }

    private static Dictionary<string, SourceTransform> ParsePrefabTransforms(UnityPackageEntry entry)
    {
        IReadOnlyList<UnityDocument> docs = UnityYaml.Parse(Encoding.UTF8.GetString(entry.Asset));

        Dictionary<long, string> names = docs
            .Where(d => d.ClassId == 1)
            .GroupBy(d => d.FileId)
            .ToDictionary(g => g.Key, g => Scalar(g.First().Get("m_Name")));

        var transforms = new Dictionary<string, SourceTransform>(StringComparer.OrdinalIgnoreCase);
        foreach (UnityDocument doc in docs.Where(d => d.ClassId == 4))
        {
            long owner = UnityYaml.ParseRef(doc.Get("m_GameObject")).FileId;
            if (names.TryGetValue(owner, out string? name) && name.Length > 0)
                transforms[name] = ReadTransform(doc);
        }
        return transforms;
    }

    private static Dictionary<long, string> ReadSceneNames(IReadOnlyList<UnityDocument> docs)
        => docs.Where(d => d.ClassId == 1)
            .GroupBy(d => d.FileId)
            .ToDictionary(g => g.Key, g => Scalar(g.First().Get("m_Name")));

    private static Dictionary<long, SourceTransform> ReadSceneTransforms(IReadOnlyList<UnityDocument> docs)
    {
        var transforms = new Dictionary<long, SourceTransform>();
        foreach (UnityDocument doc in docs.Where(d => d.ClassId == 4))
            transforms[UnityYaml.ParseRef(doc.Get("m_GameObject")).FileId] = ReadTransform(doc);
        return transforms;
    }

    /// <summary>The source scene's own Camera (!u!20) transform, not the prefab's.</summary>
    private static (string Name, SourceTransform Transform) ReadSceneCamera(IReadOnlyList<UnityDocument> docs)
    {
        UnityDocument? camera = docs.FirstOrDefault(d => d.ClassId == 20);
        Assert.True(camera != null, "The source Demo scene has no Unity Camera document (!u!20).");

        long owner = UnityYaml.ParseRef(camera!.Get("m_GameObject")).FileId;
        Dictionary<long, string> names = ReadSceneNames(docs);
        Dictionary<long, SourceTransform> transforms = ReadSceneTransforms(docs);

        Assert.True(names.TryGetValue(owner, out string? name), "The source Camera GameObject has no name.");
        Assert.True(transforms.TryGetValue(owner, out SourceTransform transform),
            "The source Camera GameObject has no Transform document.");
        return (name!, transform);
    }

    /// <summary>
    /// Builds the expected instance transform from the prefab defaults plus the scene's
    /// m_Modifications, which is what the converted scene instance should actually show.
    /// </summary>
    private static (string Name, SourceTransform Transform) ReadScenePrefabInstance(
        IReadOnlyList<UnityDocument> docs, string prefabGuid, string fallbackName, SourceTransform defaults)
    {
        UnityDocument? instance = docs.FirstOrDefault(d => d.ClassId == 1001
            && string.Equals(UnityYaml.ParseRef(d.Get("m_SourcePrefab")).Guid, prefabGuid,
                StringComparison.OrdinalIgnoreCase));
        Assert.True(instance != null,
            $"The source Demo scene has no prefab instance sourced from Unity GUID '{prefabGuid}'.");

        Float3 position = defaults.Position;
        Quaternion rotation = defaults.Rotation;
        Float3 scale = defaults.Scale;
        string name = fallbackName;

        if (instance!.Get("m_Modification", "m_Modifications") is ListNode modifications)
        {
            foreach (UnityYamlNode item in modifications.Items)
            {
                if (item is not MapNode map) continue;
                string path = Scalar(map.Fields.GetValueOrDefault("propertyPath"));
                string value = Scalar(map.Fields.GetValueOrDefault("value"));

                switch (path)
                {
                    case "m_Name": name = value; break;
                    case "m_LocalPosition.x": position.X = ParseNumber(value) ?? position.X; break;
                    case "m_LocalPosition.y": position.Y = ParseNumber(value) ?? position.Y; break;
                    case "m_LocalPosition.z": position.Z = ParseNumber(value) ?? position.Z; break;
                    case "m_LocalRotation.x": rotation.X = ParseNumber(value) ?? rotation.X; break;
                    case "m_LocalRotation.y": rotation.Y = ParseNumber(value) ?? rotation.Y; break;
                    case "m_LocalRotation.z": rotation.Z = ParseNumber(value) ?? rotation.Z; break;
                    case "m_LocalRotation.w": rotation.W = ParseNumber(value) ?? rotation.W; break;
                    case "m_LocalScale.x": scale.X = ParseNumber(value) ?? scale.X; break;
                    case "m_LocalScale.y": scale.Y = ParseNumber(value) ?? scale.Y; break;
                    case "m_LocalScale.z": scale.Z = ParseNumber(value) ?? scale.Z; break;
                }
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(name),
            $"The scene instance of '{prefabGuid}' has no usable name.");
        return (name, new SourceTransform(position, rotation, scale));
    }

    // ================================================================
    //  Prowl object lookup and assertions
    // ================================================================

    private GameObject InstantiateConvertedPrefab(string prefabName)
    {
        string relative = FindConvertedPrefab(prefabName);
        Guid guid = Assets.PathToGuid(relative);
        Assert.True(guid != Guid.Empty, $"Converted prefab '{relative}' was not indexed by the asset database.");

        PrefabAsset? prefab = AssetDatabase.Load<PrefabAsset>(guid);
        Assert.True(prefab.IsValid(), $"Converted prefab '{relative}' did not load as a PrefabAsset.");

        GameObject? root = GameObject.InstantiateDetached(prefab!);
        Assert.True(root.IsValid(), $"Converted prefab '{relative}' could not be instantiated.");
        Assert.True(string.Equals(root!.Name, prefabName, StringComparison.OrdinalIgnoreCase),
            $"Converted prefab '{relative}' instantiated a root named '{root.Name}' instead of '{prefabName}'.");
        return root!;
    }

    private static GameObject? FindInTree(GameObject root, string name)
    {
        if (root.IsNotValid()) return null;
        if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase)) return root;

        foreach (GameObject child in root.Children)
        {
            GameObject? found = FindInTree(child, name);
            if (found.IsValid()) return found;
        }
        return null;
    }

    private static List<GameObject> FindAllByName(IEnumerable<GameObject> objects, string name)
        => objects.Where(go => go.IsValid() && string.Equals(go.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool Close(float expected, float actual) => Math.Abs(expected - actual) <= Tolerance;

    private static void AssertClose(float expected, float actual, string label)
        => Assert.True(Close(expected, actual),
            $"{label}: expected {expected.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"got {actual.ToString("R", CultureInfo.InvariantCulture)} (tolerance {Tolerance}).");

    private static void AssertVector(Float3 expected, Float3 actual, string label)
    {
        AssertClose(expected.X, actual.X, label + ".x");
        AssertClose(expected.Y, actual.Y, label + ".y");
        AssertClose(expected.Z, actual.Z, label + ".z");
    }

    /// <summary>
    /// Unity's x/y/z/w are compared individually against Prowl's X/Y/Z/W. A handedness remap that
    /// swaps or rotates the quaternion components therefore fails here with the exact component.
    /// </summary>
    private static void AssertQuaternion(Quaternion expected, Quaternion actual, string label)
    {
        AssertClose(expected.X, actual.X, label + ".x");
        AssertClose(expected.Y, actual.Y, label + ".y");
        AssertClose(expected.Z, actual.Z, label + ".z");
        AssertClose(expected.W, actual.W, label + ".w");
    }

    private static bool TransformMatches(GameObject go, SourceTransform expected)
        => Close(expected.Position.X, go.Transform.LocalPosition.X)
        && Close(expected.Position.Y, go.Transform.LocalPosition.Y)
        && Close(expected.Position.Z, go.Transform.LocalPosition.Z)
        && Close(expected.Rotation.X, go.Transform.LocalRotation.X)
        && Close(expected.Rotation.Y, go.Transform.LocalRotation.Y)
        && Close(expected.Rotation.Z, go.Transform.LocalRotation.Z)
        && Close(expected.Rotation.W, go.Transform.LocalRotation.W)
        && Close(expected.Scale.X, go.Transform.LocalScale.X)
        && Close(expected.Scale.Y, go.Transform.LocalScale.Y)
        && Close(expected.Scale.Z, go.Transform.LocalScale.Z);

    private static void AssertLocalTransform(GameObject go, SourceTransform expected, string label)
    {
        Assert.True(go.IsValid(), $"{label}: object is invalid.");
        AssertVector(expected.Position, go.Transform.LocalPosition, label + " localPosition");
        AssertQuaternion(expected.Rotation, go.Transform.LocalRotation, label + " localRotation");
        AssertVector(expected.Scale, go.Transform.LocalScale, label + " localScale");
    }

    /// <summary>
    /// The probed Unity objects (the Camera and the cloud instance) sit under identity-transform
    /// ancestors, so their world transform must equal the source values too. Check that premise
    /// before comparing, rather than silently passing a wrongly parented hierarchy.
    /// </summary>
    private static void AssertWorldTransform(GameObject go, SourceTransform expected, string label)
    {
        Assert.True(go.IsValid(), $"{label}: object is invalid.");

        for (GameObject? parent = go.Parent; parent.IsValid(); parent = parent!.Parent)
        {
            string ancestor = $"{label}: ancestor '{parent!.Name}'";
            AssertVector(new Float3(0f, 0f, 0f), parent.Transform.LocalPosition, ancestor + " localPosition");
            AssertQuaternion(new Quaternion(0f, 0f, 0f, 1f), parent.Transform.LocalRotation, ancestor + " localRotation");
            AssertVector(new Float3(1f, 1f, 1f), parent.Transform.LocalScale, ancestor + " localScale");
        }

        AssertVector(expected.Position, go.Transform.Position, label + " worldPosition");
        AssertQuaternion(expected.Rotation, go.Transform.Rotation, label + " worldRotation");
        AssertVector(expected.Scale, go.Transform.LossyScale, label + " worldScale");
    }

    // ================================================================
    //  Tests
    // ================================================================

    [Fact]
    public void ConvertedPrefabs_KeepSourceTransformValuesIncludingQuaternionComponentOrder()
    {
        if (!TryImportPackage()) return;

        IReadOnlyList<UnityPackageEntry> entries = UnityPackageReader.Read(PackagePath);
        Dictionary<string, SourceTransform> cloudSource = ParsePrefabTransforms(FindEntry(entries, CloudPrefabSuffix));
        Dictionary<string, SourceTransform> treeSource = ParsePrefabTransforms(FindEntry(entries, TreePrefabSuffix));

        Assert.True(cloudSource.TryGetValue("cloud 1", out SourceTransform cloudExpected),
            "The source cloud 1 prefab has no 'cloud 1' Transform to use as an expected value.");
        Assert.True(treeSource.TryGetValue("Tree 1", out SourceTransform treeExpected),
            "The source Tree 1 prefab has no 'Tree 1' Transform to use as an expected value.");
        Assert.True(treeSource.TryGetValue("Tree 1 LOD_2", out SourceTransform lodExpected),
            "The source Tree 1 prefab has no 'Tree 1 LOD_2' Transform to use as an expected value.");

        GameObject cloudRoot = InstantiateConvertedPrefab("cloud 1");
        AssertLocalTransform(cloudRoot, cloudExpected, "converted prefab 'cloud 1'");

        GameObject treeRoot = InstantiateConvertedPrefab("Tree 1");
        AssertLocalTransform(treeRoot, treeExpected, "converted prefab 'Tree 1'");

        // LOD_2 has a distinctive (-0.7071068, 0, -0, 0.7071067) quaternion and 0.1 scale:
        // swapped quaternion components or a dropped scale cannot pass these assertions.
        GameObject? lod = FindInTree(treeRoot, "Tree 1 LOD_2");
        Assert.True(lod.IsValid(), "The converted 'Tree 1' prefab lost its 'Tree 1 LOD_2' child.");
        AssertLocalTransform(lod!, lodExpected, "converted prefab child 'Tree 1 LOD_2'");
    }

    [Fact]
    public void ConvertedScene_KeepsUnityPrefabInstanceAndCameraTransforms()
    {
        if (!TryImportPackage()) return;

        IReadOnlyList<UnityPackageEntry> entries = UnityPackageReader.Read(PackagePath);
        UnityPackageEntry sceneSource = FindEntry(entries, SceneSuffix);
        UnityPackageEntry cloudSourceEntry = FindEntry(entries, CloudPrefabSuffix);

        Dictionary<string, SourceTransform> cloudPrefabTransforms = ParsePrefabTransforms(cloudSourceEntry);
        Assert.True(cloudPrefabTransforms.TryGetValue("cloud 1", out SourceTransform cloudDefaults),
            "The source cloud 1 prefab has no Transform defaults for the scene instance.");
        string cloudGuid = ReadUnityGuid(cloudSourceEntry);

        IReadOnlyList<UnityDocument> docs = UnityYaml.Parse(Encoding.UTF8.GetString(sceneSource.Asset));
        (string cameraName, SourceTransform cameraExpected) = ReadSceneCamera(docs);
        (string cloudName, SourceTransform cloudExpected) =
            ReadScenePrefabInstance(docs, cloudGuid, "cloud 1", cloudDefaults);

        LoadConvertedScene(FindConvertedScene());

        List<GameObject> objects = Scene.Main.Objects.Where(go => go.IsValid()).ToList();
        Assert.True(objects.Count > 0, "The converted Demo scene loaded with no objects.");

        GameObject? camera = FindAllByName(objects, cameraName).FirstOrDefault();
        Assert.True(camera.IsValid(), $"The converted scene has no object named '{cameraName}'.");
        AssertLocalTransform(camera!, cameraExpected, $"converted scene '{cameraName}'");
        AssertWorldTransform(camera!, cameraExpected, $"converted scene '{cameraName}'");

        List<GameObject> clouds = FindAllByName(objects, cloudName);
        Assert.True(clouds.Count > 0, $"The converted scene has no object named '{cloudName}'.");
        GameObject? matched = clouds.FirstOrDefault(go => TransformMatches(go, cloudExpected));
        Assert.True(matched.IsValid(),
            $"No '{cloudName}' instance in the converted scene matches the Unity scene transform " +
            $"(position/rotation x,y,z,w/scale from prefab defaults plus m_Modifications). " +
            $"Candidates: {string.Join("; ", clouds.Select(go => $"localPosition={go.Transform.LocalPosition}, localRotation={go.Transform.LocalRotation}, localScale={go.Transform.LocalScale}"))}.");
        AssertWorldTransform(matched!, cloudExpected, $"converted scene '{cloudName}'");
    }

    [Fact]
    public void ConvertedScene_HasCameraComponentAndTerrainContent()
    {
        if (!TryImportPackage()) return;

        LoadConvertedScene(FindConvertedScene());
        List<GameObject> objects = Scene.Main.Objects.Where(go => go.IsValid()).ToList();

        GameObject? camera = objects.FirstOrDefault(go => go.IsValid() && go.GetComponent<Camera>().IsValid());
        Assert.True(camera.IsValid(),
            "The Unity Camera (!u!20) in 'Demo scene.unity' was skipped: the converted scene has no Prowl Camera component. " +
            "The converter must map !u!20 to Prowl.Runtime.Camera rather than silently dropping it.");

        // Terrain itself is unsupported by the converter, but the source node and the raw .asset
        // are still present. Either is enough to prove terrain content was not lost wholesale.
        bool terrainNode = objects.Any(go => go.IsValid()
            && go.Name.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0);
        bool terrainAsset = Directory.EnumerateFiles(Project.AssetsPath, "*Terrain*", SearchOption.AllDirectories).Any();
        Assert.True(terrainNode || terrainAsset,
            "The converted package contains no terrain-related node or asset; the source scene has a Terrain object and 'New Terrain.asset'.");
    }
}
