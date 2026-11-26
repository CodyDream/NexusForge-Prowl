using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using CDS.ProwlFork.United;

namespace Prowl.Editor.UnityShaders;

/// <summary>
/// Editor-side glue for the United library's Unity shader transpiler. Reads Unity .shader and
/// .compute sources from a folder tree, writes the generated Slang next to a mirrored folder
/// structure, and leaves a human-readable report for the parts a C# driver still has to do.
/// <para>
/// Generated .slang files are plain text artifacts for review/import. Nothing here registers an
/// importer: Prowl already owns .shader importing, and .slang files go through Prowl's own pipeline.
/// </para>
/// </summary>
public static class UnityShaderTranspile
{
    private const string ReportFileName = "TRANSPILE_REPORT.txt";

    private static readonly Regex ShaderExtensionPattern = new(
        @"\.(shader|compute)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The transpiler's warnings are the source of truth for what a human still has to wire up from
    // C#. These keywords also catch markers the transpiler leaves inside the generated source.
    private static readonly Regex ManualWorkPattern = new(
        @"manual|driver|c#|todo|unsupported|not supported|not implemented|must be (?:written|provided|bound|set|implemented)|needs? (?:a )?(?:custom|manual)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Transpiles every .shader/.compute file below <paramref name="sourceFolder"/> into
    /// <paramref name="outputFolder"/>, keeping relative subfolders. Returns the exact lines written
    /// to TRANSPILE_REPORT.txt. A bad file never aborts the run: its error is recorded in the report.
    /// </summary>
    public static IReadOnlyList<string> TranspileFolder(string sourceFolder, string outputFolder)
    {
        var report = new List<string>();
        string sourceRoot = "";
        string outputRoot = "";

        report.Add("Unity Shader Transpile Report");
        report.Add("Source: " + sourceFolder);
        report.Add("Output: " + outputFolder);
        report.Add("");

        try
        {
            if (string.IsNullOrWhiteSpace(sourceFolder))
                throw new ArgumentException("No source folder was given.", nameof(sourceFolder));

            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("No output folder was given.", nameof(outputFolder));

            sourceRoot = Path.GetFullPath(sourceFolder);
            outputRoot = Path.GetFullPath(outputFolder);

            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("Source folder does not exist: " + sourceRoot);

            Directory.CreateDirectory(outputRoot);
        }
        catch (Exception ex)
        {
            report.Add("ERROR: Cannot start transpile: " + ex.Message);
            FinishReport(outputFolder, report);
            return report;
        }

        var files = new List<string>();
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
            };

            files = Directory.EnumerateFiles(sourceRoot, "*", options)
                .Where(path => ShaderExtensionPattern.IsMatch(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            // Still produce a report so the caller sees why nothing was converted.
            report.Add("ERROR: Could not walk '" + sourceRoot + "': " + ex.Message);
        }

        report.Add("Shader/compute files found: " + files.Count);
        report.Add("");

        var manualWork = new List<string>();
        var failures = new List<string>();
        int written = 0;

        foreach (string file in files)
        {
            string relativeRaw = Path.GetRelativePath(sourceRoot, file);
            string relative = relativeRaw.Replace('\\', '/');
            bool isCompute = Path.GetExtension(file).Equals(".compute", StringComparison.OrdinalIgnoreCase);
            report.Add("=== " + relative + " (" + (isCompute ? "compute" : "shader") + ") ===");

            try
            {
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new IOException("Source path is outside the folder being transpiled: " + file);

                string source = File.ReadAllText(file);
                string name = Path.GetFileNameWithoutExtension(file);

                var transpiled = isCompute
                    ? UnityShaderTranspiler.TranspileCompute(source, name)
                    : UnityShaderTranspiler.Transpile(source, name);

                // A null result (or any other transpiler failure) is caught per file below.
                string destination = Path.GetFullPath(Path.Combine(outputRoot, relativeRaw));
                string destinationRelative = Path.GetRelativePath(outputRoot, destination);
                if (destinationRelative == ".." || destinationRelative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(destinationRelative))
                    throw new IOException("Output path is outside the output folder: " + destination);

                string outputFile = Path.Combine(Path.GetDirectoryName(destination) ?? outputRoot, name + ".slang");
                Directory.CreateDirectory(Path.GetDirectoryName(outputFile) ?? outputRoot);
                File.WriteAllText(outputFile, transpiled.Source ?? "");

                report.Add("  Output: " + outputFile.Replace('\\', '/'));

                IReadOnlyList<ShaderProperty> properties = transpiled.Properties ?? Array.Empty<ShaderProperty>();
                report.Add("  Properties (" + properties.Count + "):");
                if (properties.Count == 0)
                    report.Add("    (none)");
                else
                {
                    foreach (ShaderProperty property in properties)
                    {
                        string description = DescribeProperty(property);
                        string flag = ManualWorkPattern.IsMatch(description) ? "  [MANUAL C# DRIVER]" : "";
                        report.Add("    - " + description + flag);
                    }
                }

                IReadOnlyList<string> warnings = transpiled.Warnings ?? Array.Empty<string>();
                report.Add("  Warnings (" + warnings.Count + "):");
                if (warnings.Count == 0)
                    report.Add("    (none)");
                else
                {
                    foreach (string warning in warnings)
                        report.Add("    - " + warning.Replace("\r\n", " ").Replace('\n', ' '));
                }

                var reasons = new List<string>();
                reasons.AddRange(warnings.Where(w => ManualWorkPattern.IsMatch(w))
                    .Select(w => w.Replace("\r\n", " ").Replace('\n', ' ').Trim()));

                foreach (string marker in FindSourceMarkers(transpiled.Source ?? ""))
                    reasons.Add(marker);

                if (reasons.Count > 0)
                {
                    report.Add("  MANUAL C# DRIVER WORK: " + string.Join("; ", reasons.Distinct()));
                    manualWork.Add("  - " + relative + ": " + string.Join("; ", reasons.Distinct()));
                }
                else
                {
                    report.Add("  MANUAL C# DRIVER WORK: none indicated by the transpiler.");
                }

                written++;
            }
            catch (Exception ex)
            {
                string failure = relative + ": " + ex.Message;
                failures.Add("  - " + failure);
                report.Add("  ERROR: " + failure);
            }

            report.Add("");
        }

        report.Add("Summary: " + written + " written, " + failures.Count + " failed, " + manualWork.Count + " needing manual C# driver work.");
        report.Add("");
        report.Add("Files needing manual C# driver work:");
        if (manualWork.Count == 0) report.Add("  (none detected)");
        else report.AddRange(manualWork);
        report.Add("");
        report.Add("Failed files:");
        if (failures.Count == 0) report.Add("  (none)");
        else report.AddRange(failures);

        FinishReport(outputRoot, report);
        return report;
    }

    private static void FinishReport(string outputFolder, List<string> report)
    {
        if (TryWriteReport(outputFolder, report)) return;
        report.Add("ERROR: Could not write " + ReportFileName + ".");
        TryWriteReport(outputFolder, report);
    }

    private static bool TryWriteReport(string outputFolder, IReadOnlyList<string> report)
    {
        try
        {
            string folder = string.IsNullOrWhiteSpace(outputFolder) ? "." : outputFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, ReportFileName), report);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Pulls short review markers out of generated source, so a transpiler that embeds its TODOs in
    /// comments rather than in Warnings still shows up in the manual-work section.
    /// </summary>
    private static IEnumerable<string> FindSourceMarkers(string source)
    {
        if (!ManualWorkPattern.IsMatch(source)) yield break;

        int shown = 0;
        foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (!ManualWorkPattern.IsMatch(line)) continue;
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Length > 180) trimmed = trimmed.Substring(0, 180) + "...";
            yield return trimmed;
            if (++shown >= 12)
            {
                yield return "(more manual-work markers omitted from the report)";
                yield break;
            }
        }
    }

    /// <summary>
    /// Describes a library ShaderProperty without assuming which fields the library version has.
    /// The library owns the property shape; this report only needs a readable representation.
    /// </summary>
    private static string DescribeProperty(object? property)
    {
        if (property == null) return "null";

        string text = property.ToString() ?? "";
        Type type = property.GetType();
        if (!string.IsNullOrEmpty(text) && text != type.FullName && text != type.Name) return text;

        var parts = new List<string>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        try
        {
            foreach (FieldInfo field in type.GetFields(flags))
                parts.Add(field.Name + "=" + FormatValue(field.GetValue(property)));

            foreach (PropertyInfo info in type.GetProperties(flags))
            {
                if (info.GetIndexParameters().Length > 0) continue;
                parts.Add(info.Name + "=" + FormatValue(info.GetValue(property)));
            }
        }
        catch (Exception ex)
        {
            parts.Add("<unreadable: " + ex.Message + ">");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : text;
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is string s) return "\"" + s + "\"";

        if (value is IEnumerable items)
        {
            var values = new List<string>();
            foreach (object? item in items) values.Add(FormatValue(item));
            return "[" + string.Join(", ", values) + "]";
        }

        string text = value.ToString() ?? "";
        return text.Length > 120 ? text.Substring(0, 120) + "..." : text;
    }
}
