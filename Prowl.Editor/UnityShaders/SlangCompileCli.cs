using System;
using System.IO;
using System.Linq;

namespace Prowl.Editor.UnityShaders;

/// <summary> CLI entry point for compiling generated Slang shaders into Prowl .shader/.compute assets. </summary>
public static class SlangCompileCli
{
    [CliCommand("unity_shaders_compile", "Compile generated .slang files to Prowl .shader and .compute assets, with a report")]
    public static object Compile(
        [CliArg("source", "Folder containing generated .slang files")] string source,
        [CliArg("output", "Output folder for generated .shader and .compute files; defaults to a sibling 'CompiledShaders' folder")] string output = "")
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            return new { ok = false, error = $"Source folder does not exist: {source}", output = "", report = Array.Empty<string>() };

        string outDir = output;
        if (string.IsNullOrWhiteSpace(outDir))
        {
            string trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parent = Directory.GetParent(trimmed)?.FullName;
            outDir = string.IsNullOrEmpty(parent) ? Path.Combine(trimmed, "CompiledShaders") : Path.Combine(parent, "CompiledShaders");
        }

        var lines = SlangCompilePipeline.CompileFolder(source, outDir);
        int errors = lines.Count(l => l.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase));
        int written = lines.Count(l => l.Contains("  Output:", StringComparison.OrdinalIgnoreCase));

        return new
        {
            ok = errors == 0,
            source,
            output = outDir,
            filesWritten = written,
            errors,
            report = lines,
        };
    }
}
