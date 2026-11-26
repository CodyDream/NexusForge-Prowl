// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using CDS.ProwlFork.United;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Coverage for the United library's Unity ShaderLab/HLSL to Slang transpiler. These tests call the
/// library directly and hold no editor state, so they do not need the editor test harness.
/// The two real-file tests use external shader sources that are not part of the repository and skip
/// with a clear reason when those files are not on disk.
/// </summary>
public class UnityShaderTranspilerTests
{
    private const string ShaderFolder =
        @"C:\Users\Nimesh\Desktop\New folder\Game engine test new mimo\new game engine\external new import packages - do not ship\Shaders";

    // The transpiler appends the original source as line comments at the end of the generated
    // module. Cut that suffix off before asserting that a construct was actually translated;
    // otherwise the verbatim input would satisfy (or contradict) the assertion.
    private const string OriginalSourceHeader = "// ==== Original ShaderLab/HLSL source";

    private static string GeneratedPart(string source)
    {
        int index = source.IndexOf(OriginalSourceHeader, StringComparison.Ordinal);
        return index >= 0 ? source.Substring(0, index) : source;
    }

    [Fact]
    public void Transpile_LegacyShaderLab_ExtractsPropertiesAndTranslatesCgBlock()
    {
        const string shader = """
            Shader "Legacy/Simple"
            {
                Properties
                {
                    _Color ("Color", Color) = (1,1,1,1)
                    _MainTex ("Texture", 2D) = "white" {}
                    _Cutoff ("Cutoff", Range(0,1)) = 0.5
                }
                SubShader
                {
                    Pass
                    {
                        CGPROGRAM
                        #pragma vertex vert
                        #pragma fragment frag
                        #include "UnityCG.cginc"

                        sampler2D _MainTex;
                        fixed4 _Color;

                        struct appdata
                        {
                            float4 vertex : POSITION;
                            float2 uv : TEXCOORD0;
                        };

                        struct v2f
                        {
                            float4 vertex : SV_POSITION;
                            float2 uv : TEXCOORD0;
                        };

                        v2f vert(appdata v)
                        {
                            v2f o;
                            o.vertex = v.vertex;
                            o.uv = v.uv;
                            return o;
                        }

                        fixed4 frag(v2f i) : SV_Target
                        {
                            fixed4 c = tex2D(_MainTex, i.uv);
                            return c * _Color;
                        }
                        ENDCG
                    }
                }
            }
            """;

        TranspiledShader result = UnityShaderTranspiler.Transpile(shader, "Legacy/Simple");

        Assert.False(string.IsNullOrWhiteSpace(result.Source));

        Assert.True(result.Properties.Count >= 3,
            $"Expected at least three ShaderLab properties, got {result.Properties.Count}.");
        Assert.Contains(result.Properties, p => p.Name == "_Color" && p.Type == "Color");
        Assert.Contains(result.Properties, p => p.Name == "_MainTex" && p.Type == "2D");
        Assert.Contains(result.Properties, p => p.Name == "_Cutoff" && p.Type == "Range");

        // Both entry points from #pragma get Slang [shader(...)] attributes.
        Assert.Contains("[shader(\"vertex\")]", result.Source);
        Assert.Contains("[shader(\"fragment\")]", result.Source);

        // Cg 'fixed' precision types are mapped to 'float' in the generated code. Check only the
        // translated part: the transpiler includes the verbatim input as comments at the end.
        string generated = GeneratedPart(result.Source);
        Assert.Contains("float4 frag(", generated);
        Assert.Contains("float4 c = ", generated);
        Assert.DoesNotContain("fixed4 frag(", generated);
        Assert.DoesNotContain("fixed4 c = ", generated);

        // Unity's CG includes cannot be mapped to Slang and must be reported.
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w =>
            w.IndexOf("UnityCG.cginc", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(result.Warnings, w =>
            w.IndexOf("Unity-specific", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void TranspileCompute_RawComputeSource_KeepsEntryPointAndThreadGroupSize()
    {
        const string compute = """
            #pragma kernel CSMain

            RWTexture2D<float4> Output;

            [numthreads(8,8,1)]
            void CSMain(uint3 id : SV_DispatchThreadID)
            {
                Output[id.xy] = float4(1, 0, 0, 1);
            }
            """;

        TranspiledShader result = UnityShaderTranspiler.TranspileCompute(compute, "TestCompute");

        Assert.False(string.IsNullOrWhiteSpace(result.Source));
        Assert.Contains("[shader(\"compute\")]", result.Source);
        Assert.Contains("[numthreads(8,8,1)]", result.Source);

        string generated = GeneratedPart(result.Source);
        Assert.Contains("[shader(\"compute\")]", generated);
        Assert.Contains("[numthreads(8,8,1)]", generated);
        Assert.Contains("void CSMain(", generated);
    }

    [Fact]
    public void Transpile_RealCiwsShader_ProducesSourceAndUnitySpecificWarnings()
    {
        string path = Path.Combine(ShaderFolder, "ciws line", "CIWS_TracerStream.shader");
        if (!File.Exists(path))
        {
            Assert.True(true, $"Skipped: real Unity shader not found at '{path}'.");
            return;
        }

        TranspiledShader result = UnityShaderTranspiler.Transpile(File.ReadAllText(path), "CIWS_TracerStream");

        Assert.False(string.IsNullOrWhiteSpace(result.Source));
        Assert.NotEmpty(result.Warnings);

        // This shader includes UnityCG.cginc and uses Cg 'fixed' types: both are flagged.
        Assert.Contains(result.Warnings, w =>
            w.IndexOf("UnityCG", StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.IndexOf("Unity-specific", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void TranspileCompute_RealOceanCompute_ProducesSourceAndWarnings()
    {
        string path = Path.Combine(ShaderFolder, "Water", "OceanFFT.compute");
        if (!File.Exists(path))
        {
            Assert.True(true, $"Skipped: real Unity compute shader not found at '{path}'.");
            return;
        }

        TranspiledShader result = UnityShaderTranspiler.TranspileCompute(File.ReadAllText(path), "OceanFFT");

        Assert.False(string.IsNullOrWhiteSpace(result.Source));
        Assert.NotEmpty(result.Warnings);

        // Raw .compute HLSL is not ShaderLab, which the transpiler reports as a Unity-specific
        // construct needing manual attention.
        Assert.Contains(result.Warnings, w =>
            w.IndexOf("ShaderLab", StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
