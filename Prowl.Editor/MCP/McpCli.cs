// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using CDS.ProwlFork.MCP;

using Prowl.Cli;
using Prowl.Editor;

namespace Prowl.Editor.MCP;

/// <summary> Runs an existing editor CLI command and returns its result as MCP text. Main thread only. </summary>
internal static class McpCli
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

    /// <summary> Invokes a CLI command with named arguments. Null values are left at their defaults. </summary>
    public static McpToolResult Invoke(string command, params (string Name, string? Value)[] args)
    {
        var request = new CliRunRequest { Command = command, TimeoutSeconds = (int)s_timeout.TotalSeconds };
        foreach ((string name, string? value) in args)
            if (value != null) request.Args[name] = value;

        // Called directly rather than through CliServer.Execute: the stdio server already holds the editor
        // main thread, so re-dispatching through the main-thread loop would wait on ourselves.
        object? result = CliCommands.Invoke(request);
        if (result is Task task)
        {
            try
            {
                if (!task.Wait(s_timeout))
                    throw new CliException($"Command '{command}' did not finish within {s_timeout.TotalSeconds:0} seconds.");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                throw ex.InnerException!;
            }
            result = TaskResult(task);
        }

        return McpToolHelpers.Json(CliCommands.ToJson(result));
    }

    private static object? TaskResult(Task task)
    {
        Type type = task.GetType();
        if (!type.IsGenericType || type.GetGenericArguments()[0].Name == "VoidTaskResult") return null;
        return type.GetProperty("Result")!.GetValue(task);
    }
}
