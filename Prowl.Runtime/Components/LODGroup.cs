// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// The simplified meshes of one renderer. Graphite picks a level per view from the projected screen size,
/// so this component only owns the data; MeshRenderer passes the built <see cref="LodSet"/> to its renderable.
/// </summary>
[AddComponentMenu("Rendering/LOD Group")]
[DisallowMultipleComponent]
public sealed class LODGroup : Component
{
    [SerializeField] private Mesh[] levelMeshes = [];
    [SerializeField] private float[] screenSizes = [];
    [SerializeField] private float cullScreenSize;
    [SerializeField] private float bias = 1f;

    [NonSerialized, NotHeld] private LodSet? _set;

    /// <summary>Multiplies the screen size used to choose a level.</summary>
    public float Bias => bias;

    public IReadOnlyList<Mesh> LevelMeshes => levelMeshes;
    public IReadOnlyList<float> ScreenSizes => screenSizes;
    public float CullScreenSize => cullScreenSize;

    /// <summary>Sets the chain and wakes the renderers that need to pick it up.</summary>
    public void Configure(IReadOnlyList<Mesh> meshes, IReadOnlyList<float> sizes, float cull, float lodBias)
    {
        levelMeshes = [.. meshes];
        screenSizes = [.. sizes];
        cullScreenSize = cull;
        bias = lodBias;
        _set = null;

        if (GameObject.IsNotValid()) return;
        foreach (MeshRenderer renderer in GameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            renderer.MarkVisuallyDirty();
    }

    /// <summary>The LOD set Graphite needs, built once from the serialized meshes, or null while data is missing.</summary>
    public LodSet? TryGetSet()
    {
        if (_set != null) return _set;
        if (levelMeshes.Length == 0 || levelMeshes.Length != screenSizes.Length) return null;

        var levels = new LodLevel[levelMeshes.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            Mesh mesh = levelMeshes[i];
            if (mesh.IsNotValid()) return null;
            if (!mesh.IsLoaded)
            {
                if (mesh.State == AssetState.Unloaded) _ = mesh.LoadAsync();
                return null;
            }
            mesh.Upload();
            GpuMesh? gpu = mesh.GpuIfLoaded;
            if (gpu == null) return null;
            levels[i] = new LodLevel(gpu, screenSizes[i]);
        }

        try { _set = new LodSet(levels, cullScreenSize); }
        catch (ArgumentException) { return null; }
        return _set;
    }
}
