// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Net.Http.Headers;
using System.Text;

namespace Prowl.Editor.PolyHaven;

/// <summary>
/// Builds the User-Agent Poly Haven asks every request to carry. The product/version token names and versions
/// this integration, as their API terms require; the comment is the configurable project/application name:
/// <c>ProwlEditor/1.0 (&lt;name&gt;)</c>. Left unchanged it reproduces the original header exactly.
/// </summary>
public static class PolyHavenUserAgent
{
    /// <summary>The product token kept in front of the header, unchanged from the original client.</summary>
    public const string Product = "ProwlEditor";

    /// <summary>The version token kept in front of the header, unchanged from the original client.</summary>
    public const string Version = "1.0";

    /// <summary>The name used when the user has not set one: the comment of the original header.</summary>
    public const string DefaultName = "Prowl Game Engine Poly Haven integration";

    /// <summary>Longest custom name kept in the header, so a stray paste cannot produce an enormous header.</summary>
    public const int MaxNameLength = 128;

    /// <summary>The header used when no custom name has been stored.</summary>
    public static string DefaultHeader => $"{Product}/{Version} ({DefaultName})";

    /// <summary>The header to send right now, from the persisted setting.</summary>
    public static string Header => $"{Product}/{Version} ({Sanitize(CurrentName)})";

    private static string CurrentName
    {
        get
        {
            try
            {
                return EditorRegistries.GetSettings<PolyHavenUserAgentSettings>().ApplicationName;
            }
            catch (Exception)
            {
                // The client is also used before the editor has opened a project. A bad or missing registry
                // must never stop a request: fall back to the well-known name instead.
                return DefaultName;
            }
        }
    }

    /// <summary>
    /// Cleans a user-supplied name for use inside the header: trims it, drops control characters (including
    /// CR/LF, which could otherwise inject extra header lines) and caps its length. Empty input uses the
    /// default name so the header always identifies this editor.
    /// </summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DefaultName;

        var clean = new StringBuilder(name.Length);
        bool space = false;
        foreach (char c in name)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                space = clean.Length > 0;
                continue;
            }
            if (space)
            {
                clean.Append(' ');
                space = false;
            }
            clean.Append(c);
        }

        string result = clean.ToString();
        if (result.Length == 0) return DefaultName;
        if (result.Length > MaxNameLength) result = result[..MaxNameLength].TrimEnd();
        return result.Length == 0 ? DefaultName : result;
    }

    /// <summary>Sets the current User-Agent on one request, rather than on the shared client: the name can be
    /// edited while downloads are running, and mutating a client's default headers during a request is unsafe.
    /// </summary>
    public static void Apply(HttpRequestHeaders headers)
    {
        headers.UserAgent.Clear();
        // Added without parsing so a name with ordinary punctuation is sent exactly as typed. Sanitize has
        // already removed the characters that could break the header.
        headers.TryAddWithoutValidation("User-Agent", Header);
    }
}
