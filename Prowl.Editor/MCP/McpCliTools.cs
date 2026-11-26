// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using CDS.ProwlFork.MCP;

namespace Prowl.Editor.MCP;

/// <summary> MCP handlers that map onto existing editor CLI commands. </summary>
internal static class McpCliTools
{
    public static McpToolResult Status(IReadOnlyDictionary<string, object?> args)
        => McpCli.Invoke("status");

    public static McpToolResult SceneList(IReadOnlyDictionary<string, object?> args)
        => McpCli.Invoke("scene",
            ("action", "Info"),
            ("scene", McpToolHelpers.Text(args, "scene")));

    public static McpToolResult SceneOpen(IReadOnlyDictionary<string, object?> args)
    {
        string? path = McpToolHelpers.Text(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return McpToolHelpers.Error("A scene 'path' relative to Assets is required.");
        return McpCli.Invoke("scene",
            ("action", "Open"),
            ("path", path),
            ("force", McpToolHelpers.Flag(args, "force")?.ToString()));
    }

    public static McpToolResult Tree(IReadOnlyDictionary<string, object?> args)
        => McpCli.Invoke("tree",
            ("root", McpToolHelpers.Text(args, "root")),
            ("depth", McpToolHelpers.Integer(args, "depth")?.ToString()),
            ("components", McpToolHelpers.Flag(args, "components")?.ToString()),
            ("filter", McpToolHelpers.Text(args, "filter")));

    public static McpToolResult Select(IReadOnlyDictionary<string, object?> args)
        => McpCli.Invoke("select",
            ("refs", McpToolHelpers.Text(args, "refs") ?? ""),
            ("frame", McpToolHelpers.Flag(args, "frame")?.ToString()));

    public static McpToolResult Get(IReadOnlyDictionary<string, object?> args)
    {
        string? target = McpToolHelpers.Text(args, "target");
        if (string.IsNullOrWhiteSpace(target)) return McpToolHelpers.Error("A 'target' ref is required.");
        return McpCli.Invoke("get",
            ("target", target),
            ("path", McpToolHelpers.Text(args, "path")));
    }

    public static McpToolResult Set(IReadOnlyDictionary<string, object?> args)
    {
        string? target = McpToolHelpers.Text(args, "target");
        if (string.IsNullOrWhiteSpace(target)) return McpToolHelpers.Error("A 'target' ref is required.");
        return McpCli.Invoke("set",
            ("target", target),
            ("path", McpToolHelpers.Text(args, "path")),
            ("value", McpToolHelpers.JsonText(args, "value")),
            ("values", McpToolHelpers.JsonText(args, "values")));
    }

    public static McpToolResult ComponentAdd(IReadOnlyDictionary<string, object?> args)
    {
        string? target = McpToolHelpers.Text(args, "target");
        string? type = McpToolHelpers.Text(args, "type");
        if (string.IsNullOrWhiteSpace(target)) return McpToolHelpers.Error("A GameObject 'target' ref is required.");
        if (string.IsNullOrWhiteSpace(type)) return McpToolHelpers.Error("A component 'type' name is required.");
        return McpCli.Invoke("component",
            ("action", "Add"),
            ("target", target),
            ("type", type),
            ("values", McpToolHelpers.JsonText(args, "values")));
    }

    public static McpToolResult GoCreate(IReadOnlyDictionary<string, object?> args)
    {
        int? index = McpToolHelpers.Integer(args, "index");
        return McpCli.Invoke("go",
            ("action", "Create"),
            ("name", McpToolHelpers.Text(args, "name")),
            ("parent", McpToolHelpers.Text(args, "parent")),
            ("prefab", McpToolHelpers.Text(args, "prefab")),
            ("components", McpToolHelpers.Text(args, "components")),
            ("position", McpToolHelpers.JsonText(args, "position")),
            ("rotation", McpToolHelpers.JsonText(args, "rotation")),
            ("scale", McpToolHelpers.JsonText(args, "scale")),
            ("world", McpToolHelpers.Flag(args, "world")?.ToString()),
            ("index", index >= 0 ? index.Value.ToString() : null),
            ("scene", McpToolHelpers.Text(args, "scene")));
    }

    /// <summary>Selects the refs and frames them in the scene view, using the CLI's select --frame.</summary>
    public static McpToolResult CameraFrame(IReadOnlyDictionary<string, object?> args)
    {
        string? refs = McpToolHelpers.Text(args, "refs");
        if (string.IsNullOrWhiteSpace(refs)) return McpToolHelpers.Error("A 'refs' value is required, for example /Player or @selection.");
        return McpCli.Invoke("select", ("refs", refs), ("frame", "true"));
    }
}
