using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.UnityShaders;

/// <summary> Menu entry points for the Unity shader transpiler. </summary>
public static class UnityShaderTranspileMenu
{
    // Convenience path for this import task only. The regular menu command picks its own folder.
    private const string KnownShaderFolder =
        @"C:\Users\Nimesh\Desktop\New folder\Game engine test new mimo\new game engine\external new import packages - do not ship\Shaders";

    [MenuItem("Tools/Unity Shader Transpiler/Transpile Shader Folder...")]
    public static void TranspileShaderFolder()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, source =>
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            string defaultOutput = DefaultOutputFolder(source);
            EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, output =>
            {
                // Cancelling the second picker uses the sibling Slang folder as the default.
                Run(source, string.IsNullOrWhiteSpace(output) ? defaultOutput : output);
            }, defaultOutput);
        });
    }

    [MenuItem("Tools/Unity Shader Transpiler/Transpile Known External Folder")]
    public static void TranspileKnownExternalFolder()
    {
        if (!Directory.Exists(KnownShaderFolder))
        {
            Debug.LogWarning("[UnityShaderTranspile] Known shader folder does not exist: " + KnownShaderFolder);
            return;
        }

        Run(KnownShaderFolder, DefaultOutputFolder(KnownShaderFolder));
    }

    private static string DefaultOutputFolder(string sourceFolder)
    {
        string full = Path.GetFullPath(sourceFolder);
        string parent = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? full;
        return Path.Combine(parent, "Slang");
    }

    private static void Run(string sourceFolder, string outputFolder)
    {
        IReadOnlyList<string> report = UnityShaderTranspile.TranspileFolder(sourceFolder, outputFolder);
        foreach (string line in report) Debug.Log(line);

        string reportPath = outputFolder;
        try { reportPath = Path.Combine(Path.GetFullPath(outputFolder), "TRANSPILE_REPORT.txt"); }
        catch { /* TranspileFolder already reported an invalid output folder. */ }

        Debug.LogSuccess("[UnityShaderTranspile] Report: " + reportPath.Replace('\\', '/'));
    }
}
