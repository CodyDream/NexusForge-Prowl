// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Prefabs;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Importers;

/// <summary>
/// Generates a chain of simplified meshes for an imported model and saves a LOD prefab beside it.
/// The prefab's renderers carry <see cref="LODGroup"/> components, which Graphite uses to pick a level per view.
/// </summary>
public static class ModelLodGenerator
{
    public readonly record struct Result(string PrefabPath, Guid PrefabGuid, int Levels);

    /// <summary>Generates LOD meshes from the model's renderers, saves them, and writes the LOD prefab.</summary>
    public static Result Generate(AssetEntry entry, IReadOnlyList<float> ratios,
        float pixelError = 1f, float referenceHeight = 1080f)
    {
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        Project? project = Project.Current;
        if (backend == null || project == null) return default;

        if (entry.Guid == Guid.Empty || ratios.Count == 0 || ratios.Count > 7) return default;
        GameObject? root = PrefabUtility.InstantiatePrefab(entry.Guid);
        if (root.IsNotValid()) return default;

        string modelFolder = Path.GetDirectoryName(entry.Path)?.Replace('\\', '/') ?? "";
        string lodFolder = string.IsNullOrEmpty(modelFolder) ? "LODs" : $"{modelFolder}/LODs";
        string modelName = Path.GetFileNameWithoutExtension(entry.Path);
        string prefabPath = string.IsNullOrEmpty(modelFolder) ? $"{modelName}_LOD.prefab" : $"{modelFolder}/{modelName}_LOD.prefab";

        // Regenerating replaces the previous chain rather than leaving orphaned .mesh assets behind.
        backend.DeleteAssetFolder(lodFolder);

        int totalLevels = 0;
        foreach (MeshRenderer renderer in root!.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            Mesh? source = renderer.Mesh;
            if (source.IsNotValid() || !source.IsLoaded) continue;
            source.Upload();

            List<MeshLOD> chain = MeshLODGenerator.GenerateChain(source, ratios);
            if (chain.Count == 0) continue;

            var meshes = new List<Mesh>();
            var screens = new List<float>();
            // Unity-style screen-relative heights: 30%, 15%, 7.5%... The pixel-error estimate on each
            // MeshLOD is a quality report, not a switch threshold (it would put the switch at 2% of the view).
            float previous = 0.6f;
            foreach (MeshLOD level in chain)
            {
                // Unity-style screen-relative heights: 30%, 15%, 7.5%... The projected pixel error is
                // reported on each MeshLOD but must not cap the switch down to a few percent of the view.
                float screen = previous * 0.5f;
                previous = screen;

                string name = Sanitize($"{modelName}_{renderer.GameObject.Name}_LOD{meshes.Count + 1}") + ".mesh";
                backend.CreateAsset(level.Mesh, $"{lodFolder}/{name}");
                meshes.Add(level.Mesh);
                screens.Add(screen);
            }

            LODGroup group = renderer.GameObject.AddComponent<LODGroup>();
            group.Configure(meshes, screens, screens[^1] * 0.5f, 1f);
            totalLevels += meshes.Count;
        }

        PrefabUtility.SaveAsPrefabAsset(root!, prefabPath, overwrite: true);
        Guid prefabGuid = backend.ImportFile(prefabPath);
        return new Result(prefabPath, prefabGuid, totalLevels);
    }

    private static string Sanitize(string name)
        => new(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
}
