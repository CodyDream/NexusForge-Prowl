// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using CDS.ProwlFork.MCP;

using Prowl.Cli;

namespace Prowl.Editor.MCP;

/// <summary> Converts tool arguments and formats tool results. Shared by every MCP handler. </summary>
internal static class McpToolHelpers
{
    /// <summary> An argument as text, or null when absent. JSON strings are unwrapped, other JSON values become raw text. </summary>
    public static string? Text(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out object? value) || value == null) return null;
        return value switch
        {
            string text => text,
            JsonElement element => element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText(),
            _ => System.Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary> An argument as JSON text, for CLI arguments that expect an object or array. </summary>
    public static string? JsonText(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out object? value) || value == null) return null;
        if (value is string text) return text;
        if (value is JsonElement element) return element.GetRawText();
        return JsonSerializer.Serialize(value, CliProtocol.Json);
    }

    public static bool? Flag(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out object? value) || value == null) return null;
        switch (value)
        {
            case bool flag: return flag;
            case JsonElement element:
                if (element.ValueKind == JsonValueKind.True) return true;
                if (element.ValueKind == JsonValueKind.False) return false;
                if (element.ValueKind == JsonValueKind.Number) return element.GetDouble() != 0;
                if (element.ValueKind == JsonValueKind.String) return ParseFlag(element.GetString());
                return null;
            case string text: return ParseFlag(text);
            default:
                try { return System.Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
                catch (Exception ex) when (ex is FormatException or InvalidCastException) { return null; }
        }
    }

    public static int? Integer(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out object? value) || value == null) return null;
        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int number) ? number : null;
        if (value is int integer) return integer;
        if (value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
        try { return System.Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    public static float? Float(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (!args.TryGetValue(name, out object? value) || value == null) return null;
        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out float number) ? number : null;
        if (value is float single) return single;
        if (value is string text && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return parsed;
        try { return System.Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static bool? ParseFlag(string? text)
    {
        if (bool.TryParse(text, out bool flag)) return flag;
        if (text is "1" or "yes" or "on") return true;
        if (text is "0" or "no" or "off") return false;
        return null;
    }

    /// <summary>A simple JSON Schema object for a tool's arguments.</summary>
    public static Dictionary<string, object?> Schema(params (string Name, string Type, string Description)[] properties)
    {
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, string type, string description) in properties)
            fields[name] = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["description"] = description,
            };
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = fields,
            ["additionalProperties"] = false,
        };
    }

    public static McpToolResult Ok(string text) => new(false, text);
    public static McpToolResult Error(string message) => new(true, message);
    public static McpToolResult Json(JsonNode? node) => new(false, node?.ToJsonString(CliProtocol.Json) ?? "null");

    /// <summary>Turns a CLI error into a readable MCP error instead of leaking a stack trace.</summary>
    public static McpToolResult Guard(Func<McpToolResult> work)
    {
        try { return work(); }
        catch (CliException ex) { return Error(ex.Message); }
        catch (Exception ex) { return Error(ex.Message); }
    }
}
