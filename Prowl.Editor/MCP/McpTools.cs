// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using CDS.ProwlFork.MCP;

namespace Prowl.Editor.MCP;

/// <summary> Builds the first MCP tool set exposed by the editor. </summary>
internal static class McpTools
{
    public static McpToolRegistry CreateRegistry()
    {
        var registry = new McpToolRegistry();

        Add(registry, "prowl_status",
            "Open project, scenes, play/compile state, selection and log sequence. Maps to the 'status' CLI command.",
            McpToolHelpers.Schema(), McpCliTools.Status);

        Add(registry, "scene_list",
            "Lists open scenes, or describes one scene with its roots. Maps to the 'scene --action Info' CLI command.",
            McpToolHelpers.Schema(("scene", "string", "Name or path of one open scene. Omit to list all.")),
            McpCliTools.SceneList);

        Add(registry, "scene_open",
            "Opens a scene from a path relative to Assets. Maps to the 'scene --action Open' CLI command.",
            McpToolHelpers.Schema(
                ("path", "string", "Scene path relative to Assets, for example Scenes/Level.scene."),
                ("force", "boolean", "Discard unsaved changes in the current scene.")),
            McpCliTools.SceneOpen);

        Add(registry, "tree",
            "Shows the scene hierarchy, or searches it by name/component. Maps to the 'tree' CLI command.",
            McpToolHelpers.Schema(
                ("root", "string", "GameObject ref to start from instead of the scene root."),
                ("depth", "integer", "Levels to descend. Defaults to 3."),
                ("components", "boolean", "Include component type names. Defaults to true."),
                ("filter", "string", "List objects whose name or component type contains this text.")),
            McpCliTools.Tree);

        Add(registry, "select",
            "Selects GameObjects so the person watching sees them. Maps to the 'select' CLI command.",
            McpToolHelpers.Schema(
                ("refs", "string", "Comma separated refs, or empty to clear the selection."),
                ("frame", "boolean", "Also frame the selection in the scene view.")),
            McpCliTools.Select);

        Add(registry, "get",
            "Reads a GameObject, component or asset and its serialized fields. Maps to the 'get' CLI command.",
            McpToolHelpers.Schema(
                ("target", "string", "A ref, for example /Player:Camera or Materials/Metal.mat."),
                ("path", "string", "Optional field path inside the target, for example settings.radius.")),
            McpCliTools.Get);

        Add(registry, "set",
            "Writes field paths on a component or asset from JSON, with undo. Maps to the 'set' CLI command.",
            McpToolHelpers.Schema(
                ("target", "string", "Component or asset ref."),
                ("path", "string", "Field path, for example speed."),
                ("value", "string", "JSON value for the field path."),
                ("values", "object", "JSON object of field path to value, to set several at once.")),
            McpCliTools.Set);

        Add(registry, "component_add",
            "Adds a component to a GameObject, with optional field values. Maps to 'component --action Add'.",
            McpToolHelpers.Schema(
                ("target", "string", "GameObject ref, for example /Player."),
                ("type", "string", "Component type name, for example Rigidbody3D."),
                ("values", "object", "Optional JSON object of field path to value applied after adding.")),
            McpCliTools.ComponentAdd);

        Add(registry, "go_create",
            "Creates a GameObject, optionally from a prefab. Maps to 'go --action Create'.",
            McpToolHelpers.Schema(
                ("name", "string", "Name of the new GameObject."),
                ("parent", "string", "Parent GameObject ref. Omit or use / for the scene root."),
                ("prefab", "string", "Prefab or model asset to instantiate instead of an empty object."),
                ("components", "string", "Comma separated component types to add."),
                ("position", "array", "Local position as [x, y, z]."),
                ("rotation", "array", "Local Euler rotation in degrees as [x, y, z]."),
                ("scale", "array", "Local scale as [x, y, z]."),
                ("world", "boolean", "Treat position and rotation as world space."),
                ("index", "integer", "Sibling index. Negative leaves it at the end."),
                ("scene", "string", "Open scene to create in. Defaults to the active scene.")),
            McpCliTools.GoCreate);

        Add(registry, "camera_frame",
            "Selects refs and frames them in the scene view. Maps to 'select --frame'.",
            McpToolHelpers.Schema(
                ("refs", "string", "Comma separated refs, or @selection for the current selection.")),
            McpCliTools.CameraFrame);

        Add(registry, "camera_projection",
            "Reads or switches the scene camera between perspective and orthographic. Uses EditorCamera.ToggleProjection.",
            McpToolHelpers.Schema(
                ("orthographic", "boolean", "Requested projection. Omit to switch to the other one.")),
            McpSceneViewTools.CameraProjection);

        Add(registry, "scene_wireframe",
            "Reads or toggles wireframe in the scene view. Uses EditorCamera.ShowWireframe.",
            McpToolHelpers.Schema(
                ("enabled", "boolean", "Requested state. Omit to toggle it.")),
            McpSceneViewTools.Wireframe);

        Add(registry, "scene_grid",
            "Reads or toggles the scene view grid. Uses EditorCamera.ShowGrid.",
            McpToolHelpers.Schema(
                ("enabled", "boolean", "Requested state. Omit to toggle it.")),
            McpSceneViewTools.Grid);

        Add(registry, "scene_gizmos",
            "Reads or toggles scene view gizmos. Uses EditorCamera.ShowGizmos.",
            McpToolHelpers.Schema(
                ("enabled", "boolean", "Requested state. Omit to toggle it.")),
            McpSceneViewTools.Gizmos);

        Add(registry, "lod_bias",
            "Reads or sets the edited scene's LOD quality bias. Uses RenderWorld.Lod.Bias, clamped to 0.1-4.",
            McpToolHelpers.Schema(
                ("value", "number", "Bias to set. Omit to read the current value.")),
            McpSceneViewTools.LodBias);

        return registry;
    }

    private static void Add(
        McpToolRegistry registry,
        string name,
        string description,
        IReadOnlyDictionary<string, object?> schema,
        ToolHandler handler)
        => registry.Register(new McpTool(name, description, schema),
            args => McpToolHelpers.Guard(() => handler(args)));
}
