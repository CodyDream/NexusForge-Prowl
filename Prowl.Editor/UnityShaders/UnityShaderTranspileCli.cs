using System;
using System.IO;
using System.Linq;

namespace Prowl.Editor.UnityShaders;

/// <summary> CLI entry point for the Unity ShaderLab/HLSL to Slang transpiler. </summary>
public static class UnityShaderTranspileCli
{
    [CliCommand("unity_shaders", "Transpile Unity .shader and .compute sources in a folder to Slang, with a report of manual work")]
    public static object Transpile(
        [CliArg("source", "Folder containing Unity shader sources")] string source,
        [CliArg("output", "Output folder for generated .slang files; defaults to a sibling 'Slang' folder")] string output = "")
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            return new { ok = false, error = $"Source folder does not exist: {source}" };

        string outDir = output;
        if (string.IsNullOrWhiteSpace(outDir))
        {
            string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parent = Directory.GetParent(trimmed)?.FullName;
            outDir = string.IsNullOrEmpty(parent) ? Path.Combine(trimmed, "Slang") : Path.Combine(parent, "Slang");
        }

        var lines = UnityShaderTranspile.TranspileFolder(source, outDir);
        int errors = lines.Count(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
        int written = lines.Count(l => l.Contains(".slang", StringComparison.OrdinalIgnoreCase));
        return new
        {
            ok = errors == 0,
            source,
            output = outDir,
            slangFiles = written,
            errors,
            report = lines,
        };
    }
}
