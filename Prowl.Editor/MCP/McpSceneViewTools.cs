// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

using CDS.ProwlFork.MCP;

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.MCP;

/// <summary> MCP handlers that drive scene-view state directly, where no CLI command exists. </summary>
internal static class McpSceneViewTools
{
    public static McpToolResult CameraProjection(IReadOnlyDictionary<string, object?> args)
    {
        if (SceneViewPanel.ActiveCamera is not { } camera)
            return NoCamera();

        bool? orthographic = McpToolHelpers.Flag(args, "orthographic");
        if (orthographic is bool wanted && wanted != camera.IsOrthographic)
            camera.ToggleProjection();

        return McpToolHelpers.Json(new JsonObject
        {
            ["orthographic"] = camera.IsOrthographic,
            ["fieldOfView"] = camera.FieldOfView,
        });
    }

    public static McpToolResult Wireframe(IReadOnlyDictionary<string, object?> args)
        => Toggle(args, "wireframe", camera => camera.ShowWireframe, (camera, value) => camera.ShowWireframe = value);

    public static McpToolResult Grid(IReadOnlyDictionary<string, object?> args)
        => Toggle(args, "grid", camera => camera.ShowGrid, (camera, value) => camera.ShowGrid = value);

    public static McpToolResult Gizmos(IReadOnlyDictionary<string, object?> args)
        => Toggle(args, "gizmos", camera => camera.ShowGizmos, (camera, value) => camera.ShowGizmos = value);

    public static McpToolResult LodBias(IReadOnlyDictionary<string, object?> args)
    {
        Scene scene = EditorSceneManager.EditedWorld;
        if (!scene.IsValid()) return McpToolHelpers.Error("No scene is open.");
        if (scene.Rendering.World is not { } world) return McpToolHelpers.Error("The edited scene has no render world.");

        if (McpToolHelpers.Float(args, "value") is float bias)
            world.Lod.Bias = Math.Clamp(bias, 0.1f, 4f);

        return McpToolHelpers.Json(new JsonObject { ["bias"] = world.Lod.Bias });
    }

    private static McpToolResult Toggle(
        IReadOnlyDictionary<string, object?> args,
        string key,
        Func<EditorCamera, bool> get,
        Action<EditorCamera, bool> set)
    {
        if (SceneViewPanel.ActiveCamera is not { } camera)
            return NoCamera();

        // With no 'enabled' argument this toggles, matching the scene view's own menu items.
        set(camera, McpToolHelpers.Flag(args, "enabled") ?? !get(camera));
        return McpToolHelpers.Json(new JsonObject { [key] = get(camera) });
    }

    private static McpToolResult NoCamera()
        => McpToolHelpers.Error("No scene view is open. Open Window/General/Scene first.");
}
