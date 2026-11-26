// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.Graphite.Rendering;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="LODGroup"/>: move the scene camera to where a stage is picked, or hold a stage
/// while the camera stays put, so the levels can be compared side by side.
/// </summary>
[CustomEditor(typeof(LODGroup))]
public sealed class LODGroupEditor : CustomEditor
{
    private int _stage;
    private int _forced = -1;

    public override void OnGUI(Paper paper, string id, object target)
    {
        LODGroup group = (LODGroup)target;
        DrawDefaultInspector(paper, $"{id}_default", target);

        int last = group.LevelMeshes.Count; // Graphite stage 0 is the renderer's own mesh, stages 1..N the LOD meshes.
        _stage = Math.Clamp(_stage, 0, last);
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorGUI.SectionHeader(paper, $"{id}_h_debug", "LOD preview", first: true);

        using (paper.Row($"{id}_stageRow").Height(26).Gap(6).Enter())
        {
            Origami.Button(paper, $"{id}_prev", "<", () => _stage = Math.Max(0, _stage - 1)).Small().Show();
            paper.Box($"{id}_stageLabel").Height(24)
                .Text($"Stage {_stage} / {last}", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            Origami.Button(paper, $"{id}_next", ">", () => _stage = Math.Min(last, _stage + 1)).Small().Show();
        }

        Origami.Button(paper, $"{id}_moveCamera", "Move scene camera to this stage",
            () => MoveCameraToStage(group, _stage)).Small().Show();

        Origami.Button(paper, $"{id}_force",
            _forced < 0 ? "Hold this stage (camera stays put)" : $"Holding stage {_forced} - click for Auto",
            () => HoldStage(group, _forced < 0 ? _stage : -1)).Small().Show();

        paper.Box($"{id}_hint").Height(UnitValue.Auto)
            .Text("Stage 0 is the full mesh. Move the camera to a stage to see the size at which Graphite switches to it, or hold a stage without touching the camera to compare the levels.", font)
            .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>Frames the group from its current angle at the distance where the chosen stage becomes active.</summary>
    private static void MoveCameraToStage(LODGroup group, int stage)
    {
        EditorCamera? camera = SceneViewPanel.ActiveCamera;
        if (camera == null || group.LevelMeshes.Count == 0) return;

        Mesh mesh = group.LevelMeshes[0];
        if (mesh.IsNotValid() || !mesh.IsLoaded) return;
        Float3 size = mesh.bounds.Size;
        float radius = 0.5f * MathF.Sqrt(size.X * size.X + size.Y * size.Y + size.Z * size.Z);
        if (radius <= 0f) return;

        float screen = stage <= 0 || stage > group.ScreenSizes.Count
            ? 1f : MathF.Max(group.ScreenSizes[stage - 1], 0.0001f);

        // Graphite picks a level from size = radius * cot(fov/2) * bias / distance. Solve that for
        // distance instead of calling Frame(radius / screen), which added a large extra padding term.
        float fovRad = camera.FieldOfView * MathF.PI / 180f;
        float bias = group.Bias * (EditorSceneManager.EditedWorld.Rendering.World?.Lod.Bias ?? 1f);
        float distance = radius / MathF.Tan(fovRad * 0.5f) * bias / screen;
        distance = Math.Max(radius * 2f, Math.Min(distance, radius * 20f));

        Float3 center = group.GameObject.Transform.TransformPoint(mesh.bounds.Center);
        camera.SetPosition(center - camera.Forward * distance);
    }

    /// <summary>Forces the chosen stage on this group's renderables, or returns them to automatic selection.</summary>
    private void HoldStage(LODGroup group, int stage)
    {
        RenderWorld? world = EditorSceneManager.EditedWorld.Rendering.World;
        if (world == null) return;

        foreach (MeshRenderer renderer in group.GameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
        {
            RenderableHandle handle = renderer.Renderable;
            if (handle.IsValid && world.IsAlive(handle))
                world.ForceLod(handle, stage);
        }
        _forced = stage;
    }
}
