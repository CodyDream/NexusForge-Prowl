// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// Resolves a Unity MeshFilter reference to the Mesh Prowl imported from the same model.
///
/// Unity identifies a model sub-object by an XXH64 hash of its FBX node name (for example
/// "Type:Mesh-&gt;cloud 10"), but the hash is only written into the prefab, never into the model's
/// .meta. Prowl's model importer keeps the FBX node names on the GameObjects in the model prefab
/// and names the mesh sub-assets after the FBX geometry ("Mesh", "Mesh.002", ...), so comparing
/// hashes against sub-asset names cannot work. Walking the imported model prefab by node name is
/// both exact and independent of how the model importer names its sub-assets.
/// </summary>
internal sealed class UnityPrefabMeshResolver
{
    private readonly Dictionary<Guid, GameObject?> _models = new();
    private readonly Dictionary<string, Mesh?> _meshes = new(StringComparer.Ordinal);

    /// <summary>
    /// Find the mesh attached to the FBX node <paramref name="nodeName"/> in <paramref name="modelGuid"/>.
    /// Returns null when the model or node cannot be found; callers can then fall back to other
    /// resolution strategies.
    /// </summary>
    public Mesh? FindMesh(Guid modelGuid, string nodeName)
    {
        if (modelGuid == Guid.Empty || string.IsNullOrWhiteSpace(nodeName)) return null;

        string key = modelGuid.ToString("N") + "/" + nodeName;
        if (_meshes.TryGetValue(key, out Mesh? cached)) return cached;

        Mesh? result = null;
        GameObject? root = FindModel(modelGuid);
        if (root.IsValid())
        {
            GameObject? node = FindNode(root!, nodeName);
            if (node.IsValid())
            {
                MeshRenderer? renderer = node!.GetComponent<MeshRenderer>();
                if (renderer.IsValid() && renderer!.Mesh.IsValid()) result = renderer.Mesh;
            }
        }

        _meshes[key] = result;
        return result;
    }

    private GameObject? FindModel(Guid guid)
    {
        if (_models.TryGetValue(guid, out GameObject? cached)) return cached;

        GameObject? instance = null;
        // A GUID can also name a standalone Mesh asset; loading that as a prefab would only log a
        // wrong-type warning.
        if (AssetDatabase.Get(guid) is PrefabAsset prefab)
            instance = GameObject.InstantiateDetached(prefab);

        _models[guid] = instance;
        return instance;
    }

    private static GameObject? FindNode(GameObject root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;

        // Breadth-first: a node name is unique in an FBX, and this keeps the search shallow for
        // the deeply nested model hierarchies that LOD groups produce.
        var pending = new Queue<GameObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            GameObject current = pending.Dequeue();
            if (current.IsNotValid()) continue;
            foreach (GameObject child in current.Children)
            {
                if (child.IsNotValid()) continue;
                if (string.Equals(child.Name, name, StringComparison.Ordinal)) return child;
                pending.Enqueue(child);
            }
        }
        return null;
    }
}
