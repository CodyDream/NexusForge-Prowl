// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Editor.Projects;
using Prowl.Slang;

namespace Prowl.Editor.UnityShaders;

/// <summary>
/// Second stage of the Unity shader import pipeline. Compiles the <c>.slang</c> files produced by
/// <see cref="UnityShaderTranspile"/> to GLSL with Slang, then rewrites that GLSL into the ShaderDef
/// files Prowl imports: a <c>.shader</c> with one pass per vertex/fragment pair, and a <c>.compute</c>
/// with a <c>#pragma kernel</c> per compute entry point.
/// <para>
/// A broken file never aborts the folder: errors and warnings are recorded in SLANG_COMPILE_REPORT.txt
/// next to the generated files.
/// </para>
/// </summary>
public static class SlangCompilePipeline
{
    private const string ReportFileName = "SLANG_COMPILE_REPORT.txt";
    private const string ProfileName = "glsl_450";

    private static readonly Regex s_slangExtension = new(@"\.slang$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex s_shaderAttribute = new(
        @"\[\s*shader\s*\(\s*""(?<stage>vertex|fragment|compute)""\s*\)\s*\]\s*" +
        @"(?<attrs>(?:\[[^\]]*\]\s*)*)" +
        @"(?<decl>[^;{}]*?\b(?<name>[A-Za-z_]\w*)\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex s_numThreads = new(
        @"\[\s*numthreads\s*\(\s*(?<x>\d+)\s*,\s*(?<y>\d+)\s*,\s*(?<z>\d+)\s*\)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex s_moduleName = new(@"^\s*module\s+(?<name>[A-Za-z_]\w*)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex s_identifier = new(@"[^A-Za-z0-9_]", RegexOptions.Compiled);

    /// <summary>
    /// Compiles every <c>.slang</c> file below <paramref name="slangFolder"/> into
    /// <paramref name="outputFolder"/>, keeping relative subfolders. Returns the exact lines written
    /// to SLANG_COMPILE_REPORT.txt.
    /// </summary>
    public static IReadOnlyList<string> CompileFolder(string slangFolder, string outputFolder)
    {
        var report = new List<string>
        {
            "Slang Compile Report",
            "Source: " + slangFolder,
            "Output: " + outputFolder,
            "",
        };

        string sourceRoot = "";
        string outputRoot = "";
        bool slangAvailable = false;

        try
        {
            if (string.IsNullOrWhiteSpace(slangFolder))
                throw new ArgumentException("No source folder was given.", nameof(slangFolder));
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentException("No output folder was given.", nameof(outputFolder));

            sourceRoot = Path.GetFullPath(slangFolder);
            outputRoot = Path.GetFullPath(outputFolder);

            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("Source folder does not exist: " + sourceRoot);

            Directory.CreateDirectory(outputRoot);

            // Touching GlobalSession is what loads the native Slang library. Do it once, up front, so
            // a missing DLL produces one clear report entry instead of a crash for every file.
            slangAvailable = GlobalSession.FindProfile(ProfileName) != ProfileID.Unknown;
            if (!slangAvailable)
                report.Add("ERROR: Slang does not know the profile '" + ProfileName + "'.");
        }
        catch (Exception ex)
        {
            report.Add("ERROR: Slang compiler is unavailable: " + Describe(ex));
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
                .Where(path => s_slangExtension.IsMatch(Path.GetExtension(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            report.Add("ERROR: Could not walk '" + sourceRoot + "': " + ex.Message);
        }

        report.Add("Slang files found: " + files.Count);
        report.Add("");

        var failures = new List<string>();
        int shaders = 0, computes = 0;

        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            report.Add("=== " + relative + " ===");

            try
            {
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    throw new IOException("Source path is outside the folder being compiled: " + file);
                if (!slangAvailable)
                    throw new InvalidOperationException("Slang native compiler is unavailable; no output was generated.");

                string source = File.ReadAllText(file);
                List<EntryPoint> entries = FindEntryPoints(source);
                if (entries.Count == 0)
                    throw new InvalidOperationException("No [shader(\"vertex\")], [shader(\"fragment\")] or [shader(\"compute\")] entry points were found.");

                report.Add("  Entry points: " + string.Join(", ", entries.Select(e => e.Stage + ":" + e.Name)));
                foreach (EntryPoint entry in entries.Where(e => e.Stage == "compute" && !e.HasThreadSize))
                    report.Add("  WARNING: Compute kernel '" + entry.Name + "' has no [numthreads(...)] attribute; using 1,1,1.");

                var warnings = new List<string>();
                List<string> code = Compile(file, source, entries, warnings);
                foreach (string warning in warnings)
                    report.Add("  WARNING: " + warning.Replace("\r\n", " ").Replace('\n', ' '));

                string basePath = Path.Combine(outputRoot, Path.GetRelativePath(sourceRoot, file));
                string destination = Path.GetFullPath(Path.ChangeExtension(basePath, null));
                string destinationRelative = Path.GetRelativePath(outputRoot, destination);
                if (destinationRelative == ".." || destinationRelative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(destinationRelative))
                    throw new IOException("Output path is outside the output folder: " + destination);

                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? outputRoot);

                List<EntryPoint> graphics = entries.Where(e => e.Stage != "compute").ToList();
                List<EntryPoint> kernels = entries.Where(e => e.Stage == "compute").ToList();

                if (graphics.Count > 0)
                {
                    List<GraphicsPass> passes = MakePasses(graphics, report);
                    var sources = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 0; i < graphics.Count; i++)
                        sources[graphics[i].Name] = code[i];

                    var built = new List<GraphicsPass>(passes.Count);
                    foreach (GraphicsPass pass in passes)
                        built.Add(new GraphicsPass(pass.Name, GetSource(sources, pass.Vertex), GetSource(sources, pass.Fragment)));

                    string shaderName = "Slang/" + Path.ChangeExtension(relative, null).Replace('\\', '/');
                    string shaderFile = destination + ".shader";
                    File.WriteAllText(shaderFile, SlangGlslRewrite.BuildGraphics(shaderName, built));
                    report.Add("  Output: " + shaderFile.Replace('\\', '/'));
                    TryImport(shaderFile, report);
                    shaders++;
                }

                if (kernels.Count > 0)
                {
                    var kernelSources = new List<ComputeKernelSource>(kernels.Count);
                    foreach (EntryPoint kernel in kernels)
                    {
                        int index = entries.IndexOf(kernel);
                        kernelSources.Add(new ComputeKernelSource(
                            kernel.Name, kernel.X, kernel.Y, kernel.Z, code[index]));
                    }

                    string computeFile = destination + ".compute";
                    string text = SlangGlslRewrite.BuildCompute(kernelSources, out IReadOnlyList<string> rewriteWarnings);
                    foreach (string warning in rewriteWarnings)
                        report.Add("  WARNING: " + warning);
                    File.WriteAllText(computeFile, text);
                    report.Add("  Output: " + computeFile.Replace('\\', '/'));
                    TryImport(computeFile, report);
                    computes++;
                }
            }
            catch (Exception ex)
            {
                string failure = relative + ": " + Describe(ex);
                failures.Add("  - " + failure);
                report.Add("  ERROR: " + failure);
            }

            report.Add("");
        }

        report.Add($"Summary: {shaders} .shader file(s), {computes} .compute file(s) written, {failures.Count} failed.");
        report.Add("");
        report.Add("Failed files:");
        if (failures.Count == 0) report.Add("  (none)");
        else report.AddRange(failures);

        FinishReport(outputRoot, report);
        return report;
    }

    private static string GetSource(IReadOnlyDictionary<string, string> sources, string name)
    {
        if (name.Length > 0 && sources.TryGetValue(name, out string? source))
            return source;

        return name.Length == 0
            ? "void main() { gl_Position = vec4(0.0, 0.0, 0.0, 1.0); }"
            : "layout(location = 0) out vec4 fragColor;\nvoid main() { fragColor = vec4(1.0); }";
    }

    /// <summary>Pairs each vertex entry with a fragment entry; unpaired stages get a trivial stub.</summary>
    private static List<GraphicsPass> MakePasses(List<EntryPoint> graphics, List<string> report)
    {
        var passes = new List<GraphicsPass>();
        var used = new HashSet<EntryPoint>();

        foreach (EntryPoint vertex in graphics.Where(e => e.Stage == "vertex"))
        {
            EntryPoint? fragment = graphics.FirstOrDefault(e => e.Stage == "fragment" && !used.Contains(e));
            if (fragment == null)
                report.Add("  WARNING: Vertex entry '" + vertex.Name + "' has no fragment entry; an empty fragment stage was generated.");
            else
                used.Add(fragment);

            passes.Add(new GraphicsPass(
                vertex.Name + (fragment != null ? "+" + fragment.Name : ""),
                vertex.Name,
                fragment?.Name ?? ""));
            used.Add(vertex);
        }

        foreach (EntryPoint fragment in graphics.Where(e => e.Stage == "fragment" && !used.Contains(e)))
        {
            report.Add("  WARNING: Fragment entry '" + fragment.Name + "' has no vertex entry; a trivial vertex stage was generated.");
            passes.Add(new GraphicsPass(fragment.Name, "", fragment.Name));
        }

        return passes;
    }

    private static List<EntryPoint> FindEntryPoints(string source)
    {
        var entries = new List<EntryPoint>();
        foreach (Match match in s_shaderAttribute.Matches(source))
        {
            // The transpiler leaves the original ShaderLab below the Slang as line comments, and
            // those comments describe the very attributes we are looking for. Skip them.
            int lineStart = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            if (source.IndexOf("//", lineStart, Math.Max(0, match.Index - lineStart), StringComparison.Ordinal) >= 0)
                continue;

            string name = match.Groups["name"].Value;
            if (entries.Any(e => e.Name == name)) continue;

            string stage = match.Groups["stage"].Value;
            int x = 1, y = 1, z = 1;
            bool hasThreadSize = false;

            int windowStart = Math.Max(0, match.Index - 160);
            string window = source[windowStart..Math.Min(source.Length, match.Index + match.Length)];
            Match threads = s_numThreads.Match(window);
            if (threads.Success && stage == "compute")
            {
                x = ParseSize(threads.Groups["x"].Value);
                y = ParseSize(threads.Groups["y"].Value);
                z = ParseSize(threads.Groups["z"].Value);
                hasThreadSize = true;
            }

            entries.Add(new EntryPoint(stage, name, x, y, z, hasThreadSize));
        }
        return entries;
    }

    private static int ParseSize(string text)
        => int.TryParse(text, out int value) && value > 0 ? value : 1;

    private static List<string> Compile(string file, string source, List<EntryPoint> entries, List<string> warnings)
    {
        string dir = Path.GetDirectoryName(file) ?? ".";
        ProfileID profile = GlobalSession.FindProfile(ProfileName);
        if (profile == ProfileID.Unknown)
            throw new InvalidOperationException("Slang does not know the profile '" + ProfileName + "'.");

        var target = new TargetDescription
        {
            Format = CompileTarget.Glsl,
            Profile = profile,
        };
        var description = new SessionDescription
        {
            Targets = [target],
            SearchPaths = [dir],
            FileProvider = new FolderFileProvider(dir),
        };

        Session session = GlobalSession.CreateSession(description);
        Match declared = s_moduleName.Match(source);
        string moduleName = declared.Success ? declared.Groups["name"].Value : ModuleName(file);

        Module module = session.LoadModuleFromSourceString(moduleName, file, source, out DiagnosticInfo diag);
        Check(diag, warnings);

        var parts = new List<ComponentType> { module };
        foreach (EntryPoint entry in entries)
            parts.Add(module.FindEntryPointByName(entry.Name));

        ComponentType composite = session.CreateCompositeComponentType([.. parts], out diag);
        Check(diag, warnings);

        ComponentType linked = composite.Link(out diag);
        Check(diag, warnings);

        var code = new List<string>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Memory<byte> bytes = linked.GetEntryPointCode(i, 0, out diag);
            Check(diag, warnings);
            code.Add(Encoding.UTF8.GetString(bytes.Span));
        }
        return code;
    }

    private static void Check(DiagnosticInfo diagnostics, List<string> warnings)
    {
        string? message = diagnostics.Message;
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            foreach (Diagnostic diagnostic in diagnostics.GetDiagnostics())
            {
                if (diagnostic.Severity is Severity.Error or Severity.Fatal)
                    throw new InvalidOperationException("Slang: " + diagnostic.Message.Trim());
                warnings.Add("Slang: " + diagnostic.Message.Trim());
            }
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception)
        {
            if (message.Contains("error", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Slang: " + message.Trim());
            warnings.Add("Slang: " + message.Trim());
        }
    }

    private static string ModuleName(string file)
    {
        string name = s_identifier.Replace(Path.GetFileNameWithoutExtension(file), "_");
        return char.IsLetter(name[0]) || name[0] == '_' ? name : "_" + name;
    }

    private static void TryImport(string outputFile, List<string> report)
    {
        try
        {
            Project? project = Project.Current;
            EditorAssetBackend? backend = EditorAssetBackend.Instance;
            if (project == null || backend == null) return;

            string assetsRoot = Path.GetFullPath(project.AssetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = assetsRoot + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(outputFile);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

            string relative = backend.ToRelativePath(full);
            backend.ImportFile(relative);
            report.Add("  Imported: " + relative);
        }
        catch (Exception ex)
        {
            report.Add("  WARNING: Generated file was written but could not be imported: " + Describe(ex));
        }
    }

    private static string Describe(Exception exception)
        => exception is TypeInitializationException && exception.InnerException != null
            ? exception.InnerException.Message
            : exception.Message;

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

    private sealed class FolderFileProvider(string root) : IFileProvider
    {
        public Memory<byte>? LoadFile(string path)
        {
            try
            {
                string full = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
                return File.Exists(full) ? File.ReadAllBytes(full) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed record EntryPoint(string Stage, string Name, int X, int Y, int Z, bool HasThreadSize);
}
