// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using CDS.ProwlFork.United;

using Prowl.Echo;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Importers;

/// <summary>
/// Turns the GameObject/Transform/component documents of a Unity prefab or scene into real Prowl
/// objects. It is deliberately headless: nothing is added to a world, so component setters that
/// would normally wake a renderer are harmless. Assets are resolved through the GUID map the
/// unitypackage importer built from .meta files and Prowl's own import results.
/// </summary>
internal sealed class UnityHierarchyConverter
{
    private readonly EditorAssetBackend _backend;
    private readonly Dictionary<string, Guid> _guidMap;
    private readonly Dictionary<string, string> _unityGuidToRelative;
    private readonly Action<string> _warn;
    private readonly Dictionary<string, PrefabConversion> _conversionBySourceGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, PrefabConversion> _conversionByAssetGuid = new();
    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<long> _warnedClasses = new();
    private readonly Dictionary<string, Guid?> _meshCache = new(StringComparer.Ordinal);
    private readonly UnityPrefabMeshResolver _meshResolver = new();
    private IReadOnlyDictionary<string, byte[]> _prefabBytes = new Dictionary<string, byte[]>();

    internal sealed class PrefabConversion
    {
        /// <summary>The Prowl prefab asset the Unity prefab was written to.</summary>
        public Guid AssetGuid;

        /// <summary>
        /// Unity object file ID to the identifier of the Prowl object inside the prefab asset.
        /// Transform modifications are recorded against the GameObject that owns the Transform.
        /// Hash-based IDs from Unity model prefabs are included alongside the literal file IDs.
        /// </summary>
        public Dictionary<long, UnityTarget> Targets = new();

        public readonly record struct UnityTarget(Guid Identifier, bool IsComponent);
    }

    public UnityHierarchyConverter(
        EditorAssetBackend backend,
        Dictionary<string, Guid> guidMap,
        Dictionary<string, string> unityGuidToRelative,
        Action<string> warn)
    {
        _backend = backend;
        _guidMap = guidMap;
        _unityGuidToRelative = unityGuidToRelative;
        _warn = warn;
    }

    /// <summary>Raw YAML for Unity prefabs, keyed by Unity GUID, for resolving nested prefabs.</summary>
    public void SetPrefabSources(IReadOnlyDictionary<string, byte[]> prefabBytes) => _prefabBytes = prefabBytes;

    internal sealed class ConversionResult
    {
        public Guid AssetGuid;
        public bool HasContent;
    }

    // ================================================================
    //  Prefabs
    // ================================================================

    /// <summary>Convert one Unity .prefab into a Prowl prefab at <paramref name="outputRelative"/>.</summary>
    public ConversionResult? ConvertPrefab(byte[] bytes, string unityGuid, string sourceRelative, string outputRelative)
    {
        if (!TryParse(bytes, "prefab", out IReadOnlyList<UnityDocument> docs)) return null;

        List<UnityDocument> prefabInstances = docs.Where(d => d.ClassId == 1001).ToList();
        List<UnityDocument> gameObjects = docs.Where(d => d.ClassId == 1 && !IsPrefabObject(d)).ToList();

        if (gameObjects.Count == 0 && prefabInstances.Count == 0)
        {
            _warn($"[UnityPackageImporter] '{sourceRelative}' contains no GameObject hierarchy; prefab skipped.");
            return null;
        }

        var tree = new Tree();
        BuildTree(docs, tree, sourceRelative);
        WarnUnsupported(docs, sourceRelative);

        List<GameObject> instanceRoots = InstantiatePrefabInstances(prefabInstances, tree, sourceRelative);
        foreach (GameObject instance in instanceRoots)
            tree.RegisterRootCandidate(instance);
        tree.ResolveDeferredParents(_warn);
        tree.ApplyChildOrder(docs);

        GameObject root = tree.RootForPrefab(Path.GetFileNameWithoutExtension(sourceRelative));
        if (root.IsNotValid()) return null;

        Guid guid = WritePrefab(root, outputRelative);
        if (guid == Guid.Empty) return null;

        var conversion = new PrefabConversion { AssetGuid = guid, Targets = tree.Targets };
        AddPathHashes(conversion, root);
        Register(unityGuid, sourceRelative, conversion);
        return new ConversionResult { AssetGuid = guid, HasContent = true };
    }

    /// <summary>
    /// Get the converted Prowl prefab for a Unity GUID. Raw prefabs are converted on demand so nested
    /// prefabs work whichever order they appear in the package.
    /// </summary>
    public Guid? ConvertPrefabByUnityGuid(string unityGuid)
    {
        if (string.IsNullOrWhiteSpace(unityGuid) || IsZeroGuid(unityGuid)) return null;
        if (_conversionBySourceGuid.TryGetValue(unityGuid, out PrefabConversion? existing) && existing.AssetGuid != Guid.Empty)
            return existing.AssetGuid;
        if (_inProgress.Contains(unityGuid)) return null;

        if (!_unityGuidToRelative.TryGetValue(unityGuid, out string? relative)) return null;
        if (!_prefabBytes.TryGetValue(unityGuid, out byte[]? bytes)) return null;

        _inProgress.Add(unityGuid);
        try
        {
            ConversionResult? result = ConvertPrefab(bytes, unityGuid, relative, ConvertedPath(relative, ".prefab"));
            return result?.AssetGuid;
        }
        finally
        {
            _inProgress.Remove(unityGuid);
        }
    }

    // ================================================================
    //  Scenes
    // ================================================================

    /// <summary>Convert a Unity .unity scene into an Echo-serialized Prowl SceneContent.</summary>
    public ConversionResult? ConvertScene(byte[] bytes, string sourceRelative, string outputRelative)
    {
        if (!TryParse(bytes, "scene", out IReadOnlyList<UnityDocument> docs)) return null;

        var tree = new Tree();
        BuildTree(docs, tree, sourceRelative);
        WarnUnsupported(docs, sourceRelative);

        List<UnityDocument> instances = docs.Where(d => d.ClassId == 1001).ToList();
        InstantiatePrefabInstances(instances, tree, sourceRelative);
        tree.ResolveDeferredParents(_warn);
        tree.ApplyChildOrder(docs);

        List<GameObject> roots = tree.CurrentRoots();
        if (roots.Count == 0)
        {
            _warn($"[UnityPackageImporter] '{sourceRelative}' has no convertible root objects; scene skipped.");
            return null;
        }

        var objects = new List<GameObject>();
        foreach (GameObject root in roots)
            AddWithChildren(root, objects);

        var content = new SceneContent { Objects = [.. objects] };
        string absolute = Path.Combine(Project.Current!.AssetsPath, outputRelative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, content.Write().WriteToString());
        }
        catch (Exception ex)
        {
            _warn($"[UnityPackageImporter] Could not write '{outputRelative}': {ex.Message}");
            return null;
        }

        Guid guid = _backend.ImportFile(outputRelative);
        if (guid == Guid.Empty) guid = _backend.PathToGuid(outputRelative);
        if (guid == Guid.Empty)
        {
            _warn($"[UnityPackageImporter] '{outputRelative}' was written but could not be imported as a scene.");
            return null;
        }

        return new ConversionResult { AssetGuid = guid, HasContent = true };
    }

    public static string ConvertedPath(string relative, string extension)
    {
        string withoutExtension = relative.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? relative[..^extension.Length]
            : Path.ChangeExtension(relative, null) ?? relative;
        return withoutExtension + ".converted" + extension;
    }

    // ================================================================
    //  GameObject tree construction
    // ================================================================

    private sealed class Tree
    {
        public readonly Dictionary<long, UnityDocument> TransformDocs = new();
        public readonly Dictionary<long, GameObject> GameObjects = new();
        public readonly Dictionary<long, GameObject> ByTransformFileId = new();
        public readonly Dictionary<long, PrefabConversion.UnityTarget> Targets = new();
        public readonly List<(GameObject Child, long Father)> DeferredParents = new();
        public readonly List<GameObject> RootCandidates = new();
        public readonly Dictionary<long, (long PrefabInstance, long SourceTransform)> StrippedTransforms = new();

        public void RegisterRootCandidate(GameObject go)
        {
            if (go.IsValid() && !RootCandidates.Contains(go)) RootCandidates.Add(go);
        }

        public List<GameObject> CurrentRoots()
            => GameObjects.Values.Concat(RootCandidates)
                .Where(go => go.IsValid() && go.Parent.IsNotValid())
                .Distinct(ReferenceEqualityComparer.Instance)
                .Cast<GameObject>()
                .ToList();

        public GameObject RootForPrefab(string fallbackName)
        {
            List<GameObject> roots = CurrentRoots();
            if (roots.Count == 0) return null!;
            if (roots.Count == 1) return roots[0];

            var synthetic = new GameObject(fallbackName);
            foreach (GameObject root in roots)
                root.SetParent(synthetic, worldPositionStays: false);
            return synthetic;
        }

        public void ResolveDeferredParents(Action<string> warn)
        {
            foreach ((GameObject child, long father) in DeferredParents)
            {
                if (child.IsNotValid() || child.Parent.IsValid()) continue;
                if (ByTransformFileId.TryGetValue(father, out GameObject? parent) && parent.IsValid() && !ReferenceEquals(parent, child))
                {
                    child.SetParent(parent, worldPositionStays: false);
                    continue;
                }
                if (StrippedTransforms.TryGetValue(father, out (long PrefabInstance, long SourceTransform) stripped))
                {
                    if (_instanceTargets.TryGetValue(stripped.PrefabInstance, out PrefabConversion? conversion)
                        && conversion.Targets.TryGetValue(stripped.SourceTransform, out PrefabConversion.UnityTarget target)
                        && TryFindInInstances(stripped.PrefabInstance, target, out GameObject? instanceParent))
                    {
                        child.SetParent(instanceParent!, worldPositionStays: false);
                        continue;
                    }
                }
                warn($"[UnityPackageImporter] Could not resolve parent {father} for '{child.Name}'; kept as a root.");
            }
            DeferredParents.Clear();

            foreach ((GameObject instance, long father) in _instanceParents)
            {
                if (instance.IsNotValid() || instance.Parent.IsValid()) continue;
                if (ByTransformFileId.TryGetValue(father, out GameObject? parentGo) && parentGo.IsValid())
                {
                    instance.SetParent(parentGo, worldPositionStays: false);
                    continue;
                }
                if (StrippedTransforms.TryGetValue(father, out (long PrefabInstance, long SourceTransform) strippedParent)
                    && _instanceTargets.TryGetValue(strippedParent.PrefabInstance, out PrefabConversion? conversion)
                    && conversion.Targets.TryGetValue(strippedParent.SourceTransform, out PrefabConversion.UnityTarget target)
                    && TryFindInInstances(strippedParent.PrefabInstance, target, out GameObject? targetParent))
                {
                    instance.SetParent(targetParent!, worldPositionStays: false);
                    continue;
                }
                warn($"[UnityPackageImporter] Could not resolve prefab parent {father} for '{instance.Name}'; kept as a root.");
            }
            _instanceParents.Clear();
        }

        /// <summary>
        /// Unity stores sibling order in the parent's m_Children list, not in document order. Apply it
        /// after every parent link exists so Prowl's child order matches Unity's.
        /// </summary>
        public void ApplyChildOrder(IReadOnlyList<UnityDocument> docs)
        {
            foreach (UnityDocument doc in docs)
            {
                if (doc.ClassId != 4
                    || !ByTransformFileId.TryGetValue(doc.FileId, out GameObject? parent)
                    || parent.IsNotValid()) continue;
                if (Get(doc, "m_Children") is not ListNode children) continue;

                int index = 0;
                foreach (UnityYamlNode? item in children.Items)
                {
                    UnityRef childRef = ParseRef(item);
                    if (childRef.IsNull
                        || !ByTransformFileId.TryGetValue(childRef.FileId, out GameObject? child)
                        || child.IsNotValid()
                        || !ReferenceEquals(child.Parent, parent.Transform)) continue;
                    child.SetSiblingIndex(index++);
                }
            }
        }

        // Set by the converter while creating prefab instances; kept per Unity PrefabInstance file ID.
        internal readonly Dictionary<long, PrefabConversion> _instanceTargets = new();
        internal readonly Dictionary<long, Dictionary<Guid, GameObject>> _instanceGameObjects = new();
        internal readonly Dictionary<long, Dictionary<Guid, Component>> _instanceComponents = new();
        internal readonly List<(GameObject Instance, long Father)> _instanceParents = new();

        internal void SetInstanceParent(GameObject instance, long prefabInstanceId, long father)
            => _instanceParents.Add((instance, father));

        internal bool TryFindInInstances(long prefabInstanceId, PrefabConversion.UnityTarget target, out GameObject? go)
        {
            go = null;
            if (!_instanceGameObjects.TryGetValue(prefabInstanceId, out Dictionary<Guid, GameObject>? map)) return false;
            return map.TryGetValue(target.Identifier, out go) && go.IsValid();
        }
    }

    private void BuildTree(IReadOnlyList<UnityDocument> docs, Tree tree, string sourceName)
    {
        foreach (UnityDocument doc in docs)
        {
            if (doc.ClassId == 1 && !IsPrefabObject(doc))
            {
                string name = ScalarText(Get(doc, "m_Name")) ?? "GameObject";
                var go = new GameObject(name);
                go.Enabled = (ScalarText(Get(doc, "m_IsActive")) ?? "1") != "0";
                string flags = ScalarText(Get(doc, "m_StaticEditorFlags")) ?? "0";
                go.IsStatic = flags != "0" && flags != "0L";
                string layer = ScalarText(Get(doc, "m_Layer")) ?? "0";
                if (int.TryParse(layer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layerIndex))
                    go.LayerIndex = layerIndex;

                tree.GameObjects[doc.FileId] = go;
                tree.Targets[doc.FileId] = new PrefabConversion.UnityTarget(go.Identifier, false);
            }
        }

        foreach (UnityDocument doc in docs)
        {
            if (doc.ClassId != 4) continue;

            // Stripped Transform documents carry no m_GameObject: they stand for a Transform
            // inside a prefab instance, and their file ID is exactly what Unity writes into a
            // child's m_Father or a nested instance's m_TransformParent. They must be recorded
            // even though there is no GameObject to build here, or those links become roots.
            UnityRef prefabInstance = ParseRef(Get(doc, "m_PrefabInstance"));
            UnityRef corresponding = ParseRef(Get(doc, "m_CorrespondingSourceObject"));
            if (!prefabInstance.IsNull && !corresponding.IsNull)
                tree.StrippedTransforms[doc.FileId] = (prefabInstance.FileId, corresponding.FileId);

            UnityRef owner = ParseRef(Get(doc, "m_GameObject"));
            long ownerId = owner.FileId;
            if (owner.IsNull || !tree.GameObjects.TryGetValue(ownerId, out GameObject? go) || go.IsNotValid()) continue;

            // Unity stores local TRS directly, with the same X/Y/Z/W component order Prowl uses.
            // A normalisation here would introduce float error on already-normalised values, so
            // keep the source components verbatim (including -0, which matters for path hashes).
            Transform transform = go.Transform;
            if (ReadVector(Get(doc, "m_LocalPosition")) is Float3 position) transform.LocalPosition = position;
            if (ReadVector(Get(doc, "m_LocalScale")) is Float3 scale) transform.LocalScale = scale;
            if (ReadQuaternion(Get(doc, "m_LocalRotation")) is Quaternion rotation)
                transform.LocalRotation = rotation;

            tree.TransformDocs[doc.FileId] = doc;
            tree.ByTransformFileId[doc.FileId] = go;
            tree.Targets[doc.FileId] = new PrefabConversion.UnityTarget(go.Identifier, false);

            UnityRef father = ParseRef(Get(doc, "m_Father"));
            if (father.IsNull || father.FileId == 0) tree.RegisterRootCandidate(go);
            else if (tree.StrippedTransforms.ContainsKey(father.FileId) || !tree.TransformDocs.ContainsKey(father.FileId))
                tree.DeferredParents.Add((go, father.FileId));
            else if (tree.ByTransformFileId.TryGetValue(father.FileId, out GameObject? parent)
                && parent.IsValid() && !ReferenceEquals(parent, go))
                go.SetParent(parent, worldPositionStays: false);
        }

        // Parent links are resolved after every transform has been read, since Unity documents are
        // not ordered by hierarchy.
        foreach ((GameObject child, long father) in tree.DeferredParents.ToArray())
        {
            if (tree.ByTransformFileId.TryGetValue(father, out GameObject? parent)
                && parent.IsValid() && !ReferenceEquals(parent, child))
                child.SetParent(parent, worldPositionStays: false);
        }

        var meshFilters = new Dictionary<long, UnityRef>();
        foreach (UnityDocument doc in docs)
            if (doc.ClassId == 33)
            {
                UnityRef owner = ParseRef(Get(doc, "m_GameObject"));
                if (!owner.IsNull) meshFilters[owner.FileId] = ParseRef(Get(doc, "m_Mesh"));
            }

        foreach (UnityDocument doc in docs)
        {
            if (doc.ClassId is not (23 or 108 or 20 or 81)) continue;

            UnityRef owner = ParseRef(Get(doc, "m_GameObject"));
            if (owner.IsNull || !tree.GameObjects.TryGetValue(owner.FileId, out GameObject? go) || go.IsNotValid()) continue;

            Component? component = doc.ClassId switch
            {
                23 => CreateMeshRenderer(doc, meshFilters.GetValueOrDefault(owner.FileId), go.Name),
                108 => CreateLight(doc),
                20 => CreateCamera(doc),
                81 => new AudioListener(),
                _ => null,
            };
            if (component.IsNotValid()) continue;

            go.AddComponent(component!);
            tree.Targets[doc.FileId] = new PrefabConversion.UnityTarget(component!.Identifier, true);

            string enabled = ScalarText(Get(doc, "m_Enabled")) ?? "1";
            if (enabled == "0") component!.Enabled = false;
        }
    }

    private MeshRenderer? CreateMeshRenderer(UnityDocument doc, UnityRef meshRef, string nodeName)
    {
        var renderer = new MeshRenderer();
        if (!meshRef.IsNull && ResolveMesh(meshRef, nodeName) is Guid meshGuid && meshGuid != Guid.Empty)
        {
            Mesh? mesh = AssetDatabase.Load<Mesh>(meshGuid);
            if (mesh.IsValid()) renderer.Mesh = mesh;
        }

        var materials = new List<Material>();
        if (Get(doc, "m_Materials") is ListNode materialList)
        {
            foreach (UnityYamlNode? item in materialList.Items)
            {
                UnityRef reference = ParseRef(item);
                Material? material = ResolveAsset<Material>(reference);
                if (material.IsValid()) materials.Add(material!);
            }
        }
        if (materials.Count > 0) renderer.Materials = materials;

        string cast = ScalarText(Get(doc, "m_CastShadows")) ?? "1";
        renderer.ShadowCasting = cast == "0" ? ShadowCasting.Off : ShadowCasting.On;
        renderer.ReceiveShadows = (ScalarText(Get(doc, "m_ReceiveShadows")) ?? "1") != "0";
        return renderer;
    }

    private Light? CreateLight(UnityDocument doc)
    {
        // Unity: 0 Spot, 1 Directional, 2 Point, 3 Rectangle (area), 4 Disc (area).
        int type = ScalarText(Get(doc, "m_Type")) is { } raw
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 2;
        Light light = type switch
        {
            0 => new SpotLight(),
            1 => new DirectionalLight(),
            _ => new PointLight(), // area lights have no equivalent among Prowl's punctual lights
        };

        light.UseTemperature = false;
        if (ReadColor(Get(doc, "m_Color")) is Color color) light.Color = color;
        if (ReadFloat(Get(doc, "m_Intensity")) is float intensity) light.Intensity = intensity;
        if (light is LocalLight local && ReadFloat(Get(doc, "m_Range")) is float range) local.Range = range;
        if (light is SpotLight spot)
        {
            if (ReadFloat(Get(doc, "m_SpotAngle")) is float angle) spot.SpotAngle = angle;
            float outer = ReadFloat(Get(doc, "m_SpotAngle")) ?? 0f;
            float inner = ReadFloat(Get(doc, "m_InnerSpotAngle")) ?? outer;
            if (outer > 0f) spot.SpotSoftness = 1f - Math.Clamp(inner / outer, 0f, 1f);
        }

        string shadows = ScalarText(Get(doc, "m_Shadows", "m_Type")) ?? "1";
        light.CastShadows = shadows != "0";
        return light;
    }

    private static Camera CreateCamera(UnityDocument doc)
    {
        var camera = new Camera();

        // Unity CameraClearFlags: 1 Skybox, 2 SolidColor, 3 Depth, 4 Nothing.
        int clearFlags = ReadInt(Get(doc, "m_ClearFlags")) ?? 1;
        camera.Background = clearFlags switch
        {
            2 => CameraBackground.Color,
            3 or 4 => CameraBackground.None,
            _ => CameraBackground.Skybox,
        };
        if (ReadColor(Get(doc, "m_BackGroundColor")) is Color background) camera.BackgroundColor = background;
        if (ReadFloat(Get(doc, "near clip plane")) is float near) camera.NearClipPlane = near;
        if (ReadFloat(Get(doc, "far clip plane")) is float far) camera.FarClipPlane = far;
        if (ReadFloat(Get(doc, "field of view")) is float fov) camera.FieldOfView = fov;
        if (ReadFloat(Get(doc, "orthographic size")) is float size) camera.OrthographicSize = size;
        camera.ProjectionMode = (ReadInt(Get(doc, "orthographic")) ?? 0) == 0
            ? Camera.ProjectionType.Perspective
            : Camera.ProjectionType.Orthographic;
        if (ReadInt(Get(doc, "m_Depth")) is int depth) camera.Depth = depth;
        if (ScalarText(Get(doc, "m_HDR")) is string hdr) camera.HDR = hdr != "0";
        if (ReadUInt(Get(doc, "m_CullingMask", "m_Bits")) is uint mask)
            camera.CullingMask = LayerMask.FromMask(mask);
        if (Get(doc, "m_NormalizedViewPortRect") is MapNode viewport)
            camera.ViewportRect = new Float4(
                ReadFloat(GetIn(viewport, "x")) ?? 0f,
                ReadFloat(GetIn(viewport, "y")) ?? 0f,
                ReadFloat(GetIn(viewport, "width")) ?? 1f,
                ReadFloat(GetIn(viewport, "height")) ?? 1f);
        return camera;
    }

    // !u!81 is Prowl's AudioListener. It exists in Prowl.Runtime, so it is constructed directly;
    // no reflection or "component missing" warning is necessary.

    // ================================================================
    //  Prefab instances and modifications
    // ================================================================

    private List<GameObject> InstantiatePrefabInstances(IReadOnlyList<UnityDocument> instances, Tree tree, string sourceName)
    {
        var result = new List<GameObject>();
        foreach (UnityDocument doc in instances)
        {
            UnityRef sourceRef = ParseRef(Get(doc, "m_SourcePrefab"));
            Guid prefabGuid = ResolvePrefabGuid(sourceRef);
            PrefabAsset? prefab = AssetDatabase.Load<PrefabAsset>(prefabGuid);
            if (prefab.IsNotValid())
            {
                _warn($"[UnityPackageImporter] Prefab instance '{doc.FileId}' in '{sourceName}' has no usable source prefab; skipped.");
                continue;
            }

            GameObject? instance = GameObject.InstantiateDetached(prefab!);
            if (instance.IsNotValid()) continue;

            PrefabConversion conversion = FindOrCreateConversion(prefabGuid, prefab!, instance!);
            tree._instanceTargets[doc.FileId] = conversion;
            tree._instanceGameObjects[doc.FileId] = IndexGameObjects(instance!);
            tree._instanceComponents[doc.FileId] = IndexComponents(instance!);

            ApplyModifications(doc, instance!, conversion, tree, sourceName);

            // m_TransformParent is a bare {fileID: n} map, not a full guid/type reference.
            long parentFileId = 0;
            if (Get(doc, "m_TransformParent") is MapNode parentMap
                && ScalarText(parentMap.Fields.GetValueOrDefault("fileID") as UnityYamlNode) is { } parentText)
                long.TryParse(parentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parentFileId);

            if (parentFileId == 0)
                tree.RegisterRootCandidate(instance!);
            else
                tree.SetInstanceParent(instance!, doc.FileId, parentFileId);

            result.Add(instance!);
        }
        return result;
    }

    private PrefabConversion FindOrCreateConversion(Guid prefabGuid, PrefabAsset prefab, GameObject instance)
    {
        if (_conversionByAssetGuid.TryGetValue(prefabGuid, out PrefabConversion? found)) return found;

        // Model prefabs have no Unity YAML prefab to read file IDs from. Index the Prowl tree with
        // Unity's hash-based IDs so their transform overrides still land on the right object when
        // the FBX node names match.
        var conversion = new PrefabConversion { AssetGuid = prefabGuid };
        AddPathHashes(conversion, instance);
        _conversionByAssetGuid[prefabGuid] = conversion;
        return conversion;
    }

    private static Dictionary<Guid, GameObject> IndexGameObjects(GameObject root)
    {
        var map = new Dictionary<Guid, GameObject>();
        void Walk(GameObject go)
        {
            if (go.IsNotValid()) return;
            Guid id = go.SourceIdentifier != Guid.Empty ? go.SourceIdentifier : go.Identifier;
            map.TryAdd(id, go);
            foreach (GameObject child in go.Children) Walk(child);
        }
        Walk(root);
        return map;
    }

    private static Dictionary<Guid, Component> IndexComponents(GameObject root)
    {
        var map = new Dictionary<Guid, Component>();
        void Walk(GameObject go)
        {
            if (go.IsNotValid()) return;
            foreach (Component component in go.GetComponents<Component>())
            {
                if (component.IsNotValid()) continue;
                Guid id = component.SourceIdentifier != Guid.Empty ? component.SourceIdentifier : component.Identifier;
                map.TryAdd(id, component);
            }
            foreach (GameObject child in go.Children) Walk(child);
        }
        Walk(root);
        return map;
    }

    private void ApplyModifications(UnityDocument instance, GameObject root, PrefabConversion conversion, Tree tree, string sourceName)
    {
        if (Get(instance, "m_Modification", "m_Modifications") is not ListNode modifications) return;

        Dictionary<Guid, GameObject> gameObjects = tree._instanceGameObjects.GetValueOrDefault(instance.FileId) ?? IndexGameObjects(root);
        Dictionary<Guid, Component> components = tree._instanceComponents.GetValueOrDefault(instance.FileId) ?? IndexComponents(root);

        // Unity writes one m_Modifications entry per quaternion component, in its own order (w, x, y, z
        // in these packages). Normalising after each entry would throw away a partial w before the
        // other components arrive, which is why an instance could end up visibly rotated away from
        // the scene value. Collect the touched objects and normalise once, after every entry landed.
        var touchedRotations = new HashSet<GameObject>();

        foreach (UnityYamlNode? item in modifications.Items)
        {
            if (item is not MapNode map) continue;
            string propertyPath = ScalarText(map.Fields.GetValueOrDefault("propertyPath") as UnityYamlNode) ?? "";
            if (propertyPath.Length == 0) continue;

            UnityRef target = ParseRef(map.Fields.GetValueOrDefault("target") as UnityYamlNode);
            if (!conversion.Targets.TryGetValue(target.FileId, out PrefabConversion.UnityTarget targetInfo)) continue;

            GameObject? go = null;
            Component? component = null;
            if (targetInfo.IsComponent)
                components.TryGetValue(targetInfo.Identifier, out component);
            else
                gameObjects.TryGetValue(targetInfo.Identifier, out go);
            if (go.IsNotValid() && component.IsNotValid()) continue;

            string value = ScalarText(map.Fields.GetValueOrDefault("value") as UnityYamlNode) ?? "";
            UnityRef objectRef = ParseRef(map.Fields.GetValueOrDefault("objectReference") as UnityYamlNode);
            ApplyModification(propertyPath, value, objectRef, go, component, sourceName, touchedRotations);
        }

        foreach (GameObject touched in touchedRotations)
            if (touched.IsValid())
                touched.Transform.LocalRotation = Quaternion.NormalizeSafe(touched.Transform.LocalRotation);
    }

    private void ApplyModification(string path, string value, UnityRef objectRef, GameObject? go, Component? component,
        string sourceName, HashSet<GameObject> touchedRotations)
    {
        if (go.IsValid() && TryApplyTransform(path, value, go!, touchedRotations)) return;

        if (go.IsValid())
        {
            switch (path)
            {
                case "m_Name":
                    go!.Name = value;
                    return;
                case "m_IsActive":
                    go!.Enabled = value != "0";
                    return;
            }
        }

        if (component.IsValid() && TryApplyComponent(path, value, objectRef, component!)) return;

        // Unity editor-only flags and hint values have no Prowl equivalent; they are intentionally
        // ignored rather than reported for every object in the scene.
    }

    private static bool TryApplyTransform(string path, string value, GameObject go, HashSet<GameObject> touchedRotations)
    {
        if (!path.StartsWith("m_LocalPosition.", StringComparison.Ordinal)
            && !path.StartsWith("m_LocalScale.", StringComparison.Ordinal)
            && !path.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) return false;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
            return false;

        string axis = path[(path.LastIndexOf('.') + 1)..];
        if (path.StartsWith("m_LocalPosition.", StringComparison.Ordinal))
        {
            Float3 v = go.Transform.LocalPosition;
            switch (axis) { case "x": v.X = number; break; case "y": v.Y = number; break; case "z": v.Z = number; break; default: return false; }
            go.Transform.LocalPosition = v;
            return true;
        }
        if (path.StartsWith("m_LocalScale.", StringComparison.Ordinal))
        {
            Float3 v = go.Transform.LocalScale;
            switch (axis) { case "x": v.X = number; break; case "y": v.Y = number; break; case "z": v.Z = number; break; default: return false; }
            go.Transform.LocalScale = v;
            return true;
        }

        Quaternion q = go.Transform.LocalRotation;
        switch (axis)
        {
            case "x": q.X = number; break;
            case "y": q.Y = number; break;
            case "z": q.Z = number; break;
            case "w": q.W = number; break;
            default: return false;
        }
        go.Transform.LocalRotation = q;
        touchedRotations.Add(go);
        return true;
    }

    private bool TryApplyComponent(string path, string value, UnityRef objectRef, Component component)
    {
        if (path == "m_Enabled")
        {
            component.Enabled = value != "0";
            return true;
        }

        if (path.StartsWith("m_Materials.Array.data[", StringComparison.Ordinal)
            && component is MeshRenderer renderer)
        {
            int open = path.IndexOf('[');
            int close = path.IndexOf(']', open + 1);
            if (open < 0 || close < 0
                || !int.TryParse(path[(open + 1)..close], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                || index < 0) return true;

            Material? material = ResolveAsset<Material>(objectRef);
            if (material.IsNotValid()) return true;

            var materials = renderer.Materials.ToList();
            while (materials.Count <= index) materials.Add(null!);
            materials[index] = material!;
            renderer.Materials = materials;
            return true;
        }

        if (component is Light light)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) return true;
            if (path == "m_Intensity") { light.Intensity = number; return true; }
            if (path == "m_Range" && light is LocalLight local) { local.Range = number; return true; }
        }

        return false;
    }

    // ================================================================
    //  Asset resolution
    // ================================================================

    private Guid ResolvePrefabGuid(UnityRef source)
    {
        if (source.IsNull || string.IsNullOrWhiteSpace(source.Guid)) return Guid.Empty;

        Guid? converted = ConvertPrefabByUnityGuid(source.Guid);
        if (converted.HasValue && converted.Value != Guid.Empty) return converted.Value;

        return _guidMap.TryGetValue(source.Guid, out Guid guid) ? guid : Guid.Empty;
    }

    /// <summary>
    /// Resolve a Unity asset GUID through the path bridge. The importer writes .mat/.asset Prowl
    /// assets after it has read the old GUID mappings, which leaves the map stale for those files;
    /// the asset path is stable, so ask the backend for its current GUID first.
    /// </summary>
    private Guid ResolveAssetGuid(UnityRef reference)
    {
        if (reference.IsNull || string.IsNullOrWhiteSpace(reference.Guid)) return Guid.Empty;

        if (_unityGuidToRelative.TryGetValue(reference.Guid, out string? relative)
            && !string.IsNullOrWhiteSpace(relative))
        {
            Guid byPath = _backend.PathToGuid(relative);
            if (byPath != Guid.Empty) return byPath;
        }

        return _guidMap.TryGetValue(reference.Guid, out Guid guid) ? guid : Guid.Empty;
    }

    private T? ResolveAsset<T>(UnityRef reference) where T : Asset
    {
        Guid guid = ResolveAssetGuid(reference);
        if (guid == Guid.Empty) return null;
        // A Unity reference can name a model prefab (for example a material override that still
        // points at the FBX). Loading that as a Material/Mesh only produces a wrong-type warning.
        if (AssetDatabase.Get(guid) is not T) return null;
        T? asset = AssetDatabase.Load<T>(guid);
        return asset.IsValid() ? asset : null;
    }

    private Guid? ResolveMesh(UnityRef reference, string nodeName)
    {
        if (reference.IsNull || string.IsNullOrWhiteSpace(reference.Guid)) return null;

        string key = reference.Guid + "/" + reference.FileId.ToString(CultureInfo.InvariantCulture);
        if (_meshCache.TryGetValue(key, out Guid? cached)) return cached;

        Guid? resolved = null;
        Guid parentGuid = ResolveAssetGuid(reference);

        // Unity's file ID hashes the FBX node name, while Prowl names the mesh sub-assets after
        // the FBX geometry. The imported model prefab still carries the node names, so use that
        // as the primary mapping instead of guessing at sub-asset names.
        if (parentGuid != Guid.Empty && _meshResolver.FindMesh(parentGuid, nodeName) is { } namedMesh)
            resolved = namedMesh.AssetID;

        if (resolved == null && parentGuid != Guid.Empty)
        {
            SubAssetEntry[] subs = _backend.GetSubAssets(parentGuid);
            List<SubAssetEntry> meshes = subs.Where(s => s.TypeName.Contains(".Mesh,", StringComparison.Ordinal)).ToList();

            foreach (SubAssetEntry subMesh in meshes)
                if (UnityFileId.Matches(reference.FileId, subMesh.Name))
                {
                    resolved = subMesh.Guid;
                    break;
                }

            // Unity's FBX file-ID hash uses the source mesh name, which is not always the Prowl
            // sub-asset name. A single-mesh model is unambiguous even when the names differ.
            // Guessing "first mesh" is wrong whenever a model contains several meshes: it attaches
            // one leaf's geometry to every renderer, so trees and other multi-part props appear
            // stretched or duplicated. Only fall back when the model has exactly one mesh.
            if (resolved == null && meshes.Count == 1) resolved = meshes[0].Guid;
            if (resolved == null && meshes.Count > 1)
                _warn($"[UnityPackageImporter] Could not map mesh {reference.FileId} of '{reference.Guid}' to an imported sub-asset; renderer left without a mesh.");
        }

        // A model's main asset is a PrefabAsset, not a Mesh; loading it as one only produces a
        // wrong-type warning. Standalone mesh assets have no model prefab to search, so they go
        // through the normal typed load.
        if (resolved == null && (parentGuid == Guid.Empty || AssetDatabase.Get(parentGuid) is not PrefabAsset))
        {
            Mesh? directMesh = ResolveAsset<Mesh>(reference);
            if (directMesh.IsValid()) resolved = directMesh!.AssetID;
        }
        _meshCache[key] = resolved;
        return resolved;
    }

    private void Register(string unityGuid, string sourceRelative, PrefabConversion conversion)
    {
        _conversionByAssetGuid[conversion.AssetGuid] = conversion;
        if (!string.IsNullOrWhiteSpace(unityGuid) && !IsZeroGuid(unityGuid))
        {
            _conversionBySourceGuid[unityGuid] = conversion;
            _guidMap[unityGuid] = conversion.AssetGuid;
        }
        else if (!string.IsNullOrWhiteSpace(sourceRelative))
        {
            foreach ((string unity, string relative) in _unityGuidToRelative)
                if (string.Equals(relative, sourceRelative, StringComparison.OrdinalIgnoreCase))
                    _conversionBySourceGuid[unity] = conversion;
        }
    }

    private Guid WritePrefab(GameObject root, string outputRelative)
    {
        try
        {
            if (!PrefabUtility.SaveAsPrefabAsset(root, outputRelative, overwrite: true)) return Guid.Empty;
        }
        catch (Exception ex)
        {
            _warn($"[UnityPackageImporter] Could not save prefab '{outputRelative}': {ex.Message}");
            return Guid.Empty;
        }

        Guid guid = _backend.ImportFile(outputRelative);
        if (guid == Guid.Empty) guid = _backend.PathToGuid(outputRelative);
        if (guid == Guid.Empty)
            _warn($"[UnityPackageImporter] '{outputRelative}' was written but could not be imported as a prefab.");
        return guid;
    }

    private static void AddWithChildren(GameObject go, List<GameObject> list)
    {
        if (go.IsNotValid() || list.Contains(go)) return;
        list.Add(go);
        foreach (GameObject child in go.Children) AddWithChildren(child, list);
    }

    /// <summary>Unity's fileIDs for model sub-objects are XXH64 hashes of their type and path.</summary>
    private void AddPathHashes(PrefabConversion conversion, GameObject root)
    {
        void Walk(GameObject go, string path)
        {
            if (go.IsNotValid()) return;

            var variants = new HashSet<string>(StringComparer.Ordinal)
            {
                path,
                "//RootNode/" + path,
                "//RootNode/root/" + path,
            };
            if (path.Contains('/')) variants.Add("//RootNode/" + path[(path.IndexOf('/') + 1)..]);

            foreach (string variant in variants)
            {
                foreach (string type in new[] { "Transform", "GameObject" })
                    AddTarget(conversion, UnityFileId.Hash($"Type:{type}->{variant}0"), go.Identifier, false);
                foreach (Component component in go.GetComponents<Component>())
                    if (component.IsValid())
                        AddTarget(conversion, UnityFileId.Hash($"Type:{component.GetType().Name}->{variant}0"), component.Identifier, true);
            }

            foreach (GameObject child in go.Children) Walk(child, path + "/" + child.Name);
        }

        Walk(root, root.Name);
    }

    private static void AddTarget(PrefabConversion conversion, long fileId, Guid identifier, bool isComponent)
    {
        if (fileId == 0 || identifier == Guid.Empty) return;
        conversion.Targets.TryAdd(fileId, new PrefabConversion.UnityTarget(identifier, isComponent));
    }

    // ================================================================
    //  Unity YAML helpers
    // ================================================================

    private static bool TryParse(byte[] bytes, string kind, out IReadOnlyList<UnityDocument> docs)
    {
        docs = [];
        try
        {
            docs = UnityYaml.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
            return docs.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityPackageImporter] Could not parse Unity {kind} YAML: {ex.Message}");
            return false;
        }
    }

    private static UnityYamlNode? Get(UnityDocument document, params string[] path)
    {
        try { return UnityYaml.Get(document, path); }
        catch { return null; }
    }

    private static string? ScalarText(UnityYamlNode? node)
        => node is ScalarNode scalar ? scalar.Value : null;

    private static UnityRef ParseRef(UnityYamlNode? node)
    {
        try { return UnityYaml.ParseRef(node); }
        catch { return new UnityRef(0, "", 0); }
    }

    private static bool IsPrefabObject(UnityDocument document)
    {
        UnityRef prefab = ParseRef(Get(document, "m_PrefabInstance"));
        return !prefab.IsNull;
    }

    private static bool IsZeroGuid(string guid)
        => guid.Length == 32 && guid.All(c => c == '0');

    private static float? ReadFloat(UnityYamlNode? node)
        => float.TryParse(ScalarText(node), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;

    private static int? ReadInt(UnityYamlNode? node)
        => int.TryParse(ScalarText(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static uint? ReadUInt(UnityYamlNode? node)
        => uint.TryParse(ScalarText(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : null;

    private static Float3? ReadVector(UnityYamlNode? node)
    {
        if (node is not MapNode) return null;
        return new Float3(
            ReadFloat(GetIn(node, "x")) ?? 0f,
            ReadFloat(GetIn(node, "y")) ?? 0f,
            ReadFloat(GetIn(node, "z")) ?? 0f);
    }

    private static Quaternion? ReadQuaternion(UnityYamlNode? node)
    {
        if (node is not MapNode) return null;
        return new Quaternion(
            ReadFloat(GetIn(node, "x")) ?? 0f,
            ReadFloat(GetIn(node, "y")) ?? 0f,
            ReadFloat(GetIn(node, "z")) ?? 0f,
            ReadFloat(GetIn(node, "w")) ?? 1f);
    }

    private static Color? ReadColor(UnityYamlNode? node)
    {
        if (node is not MapNode) return null;
        return new Color(
            ReadFloat(GetIn(node, "r")) ?? 0f,
            ReadFloat(GetIn(node, "g")) ?? 0f,
            ReadFloat(GetIn(node, "b")) ?? 0f,
            ReadFloat(GetIn(node, "a")) ?? 1f);
    }

    private static UnityYamlNode? GetIn(UnityYamlNode node, string key)
        => node is MapNode map && map.Fields.TryGetValue(key, out UnityYamlNode? value) ? value : null;

    private void WarnUnsupported(IReadOnlyList<UnityDocument> docs, string sourceName)
    {
        foreach (long classId in docs.Select(d => d.ClassId).Distinct())
        {
            if (classId is 1 or 4 or 20 or 23 or 33 or 81 or 108 or 1001) continue;
            if (_warnedClasses.Add(classId))
                _warn($"[UnityPackageImporter] Unsupported Unity component class !u!{classId} in '{sourceName}' (and possibly other files) was skipped.");
        }
    }
}
