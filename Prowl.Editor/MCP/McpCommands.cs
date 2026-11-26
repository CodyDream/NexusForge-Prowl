// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using CDS.ProwlFork.MCP;

namespace Prowl.Editor.MCP;

public static class McpCommands
{
    [CliCommand("mcp_dispatch", "Handle one MCP JSON-RPC line (used by the CLI mcp verb)")]
    public static string Dispatch([CliArg("line")] string line)
        => McpStdioServer.HandleLine(line, McpTools.CreateRegistry(), "prowl-editor", "1.0") ?? "";

    /// <summary>
    /// Runs an MCP stdio server that exposes editor tools to a client. CLI commands already run on the editor main
    /// thread, so the server and every tool handler stay there. It serves until stdin closes, which means a caller
    /// invoking it through the CLI HTTP endpoint should pass a long --timeout (for example --timeout 86400), or run
    /// it in a terminal where it can block for as long as the client needs.
    /// </summary>
    [CliCommand("mcp_serve", "Run an MCP stdio server for editor control")]
    public static void Serve()
    {
        McpToolRegistry registry = McpTools.CreateRegistry();
        McpStdioServer.Run(registry, "prowl-editor", "1.0");
    }
}
