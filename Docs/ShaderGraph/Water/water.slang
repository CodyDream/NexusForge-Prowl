// ---------------------------------------------------------------------------
// water.slang — direct Slang reference translation of water.shadergraph
//
// Owner: Water detective. Companion: NODES.md, ANALYSIS.md, CONFLICTS.md.
// Purpose: stylized scrolling water surface (URP Lit target) with a two-axis
//          sine displacement, depth-based shallow/deep colour, horizon blend,
//          foam, screen-space refraction and four scrolling normal layers.
//
// Node ID inventory (water.shadergraph, 384 serialized objects / 98 graph nodes / 102 edges):
//   PropertyNode 20, BlockNode 12, SubGraphNode 13, MultiplyNode 9, LerpNode 6,
//   AddNode 5, SplitNode 4, TimeNode 3, NormalBlendNode 3, NegateNode 3,
//   StepNode 2, CombineNode 2, SaturateNode 2, NormalStrengthNode 1,
//   DivideNode 1, Vector2Node 1, Vector3Node 1, ViewVectorNode 1,
//   ScreenPositionNode 1, PositionNode 1, FresnelNode 1, FogNode 1,
//   GradientNoiseNode 1, SceneColorNode 1, ClampNode 1, TransformNode 1,
//   DotProductNode 1, GraphData 1, UniversalTarget 1, UniversalLitSubTarget 1,
//   CategoryData 6, GroupData 5, material slots 253 (86 Vector1, 68 DynamicVector,
//   27 DynamicValue, 23 Vector3, 23 Vector2, 13 Vector4, 3 Position, 3 ColorRGB,
//   3 Normal, 1 UV, 1 ViewDirection, 1 Tangent, 1 ScreenPosition).
//
// Confidence: HIGH for topology and slot defaults; MEDIUM for unconnected-input
// behaviour (see ANALYSIS.md). This is a reference translation, not engine code:
// Unity's time/fog/camera/scene textures are explicit uniforms here, no macros.
// ---------------------------------------------------------------------------

cbuffer WaterParams
{
    // PropertyNode values (converted material left most at their graph defaults).
    float  waveSpeed;          // "speed"           default 1
    float  waveLength;         // "length"          default 1
    float  waveAmplitude;      // "amplitute"       default 0
    float  deepness;           // "Deepness"        default 0
    float  strength;           // "Strength"        default 0
    float2 refractionScale;    // "Refraction Scale" default (1,-1)
    float  refractionSpeed;    // "Refraction Speed" default 1
    float  refractionStrength; // "Refraction stength" default 0
    float  normalStrength;     // "normal strength" default 1
    float  smoothness;         // "smotness"        default 0
    float  horizonDistance;    // "Horizon Distance" default 0
    float  foamAmount;         // "Foam Amount"     default 250
    float  foamCutout;         // "Foam cutout"     default 0
    float  foamScale;          // "foam scale"      default 1
    float  foamSpeed;          // "foam speed"      default 0
    float3 shallowColor;       // "Shallow color"   default (0.171,0.516,0.802)
    float3 deepColor;          // "deep color"      default 0
    float3 horizonColor;       // "Horizon color"   default 0
    float3 foamColor;          // "Foam color"      default 0
    float  foamAlpha;          // "Foam color".a    default 0
};

// Engine-provided state that the graph reads implicitly (no Unity globals here).
cbuffer FrameState
{
    float  time;               // TimeNode slot 0
    float3 cameraPosWS;        // ViewVectorNode / position context
    float4x4 worldToObject;    // TransformNode m_Conversion {from:2,to:0}
    float4x4 objectToWorld;    // PositionNode m_Space=2 (world position)
    float4x4 worldToClip;      // vertex -> clip for the block output
    float3 fogColor;           // FogNode Color
    float  fogDensity;         // FogNode Density
};

Texture2D<float4> opaqueScene; // SceneColorNode + depthfade SceneDepthNode
Texture2D<float4> sceneDepth;
Texture2D<float4> waterNormalMap; // Normal map subgraph default: Water Normal.png
SamplerState linearRepeat;

float hash21(float2 p)
{
    return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

// ---------------------------------------------------------------------------
// Subgraph translations (one function each; see the dedicated .slang files)
// ---------------------------------------------------------------------------

// Sinewave.shadersubgraph: 5-harmonic profile. ConstantNode m_constant=0 -> PI,
// multiplied by 2, so k = 2*PI*position/wavelength (confidence: medium).
float sinewaveProfile(float position, float wavelength)
{
    float k = 6.28318530718 * position / max(1e-5, wavelength);
    return sin(k)                       // 39239945
         + 0.5  * sin(11.0 * k)         // ba42a9bf -> a4673d98
         + 0.7  * sin(2.0  * k)         // 9cb2186e -> 626ad575
         + 0.2  * sin(5.0  * k)         // c0865751 -> 98a43603
         + 0.5  * sin(8.0  * k);        // 53b7c87b -> 53624021
}

// texture movement.shadersubgraph. The subgraph's UV input is UNCONNECTED and
// its serialized default is (0,0), so the result is time-driven only — kept
// faithful here; see ANALYSIS.md uncertainty #1.
float2 scrollingUV(float2 speed)
{
    return time * speed;               // 36a5f094 -> 6fc870c0 -> 4bc1fbd8
}

// depthfade.shadersubgraph: saturate((sceneDepth - surfaceDepth)/Distance)*Strength.
float depthFade(float strength, float distance, float sceneEyeDepth, float surfaceEyeDepth)
{
    float delta = sceneEyeDepth - surfaceEyeDepth; // f5cb8819
    return saturate(delta / max(1e-5, distance)) * strength; // 9133af43, d2e26331, 21bc79c5
}

// Normal map.shadersubgraph: sample, flatten XY, normalise; noise output is the
// alpha channel but is unused by the parent graph.
float3 sampleWaterNormal(float2 uv, float strength)
{
    float3 n = waterNormalMap.Sample(linearRepeat, uv).xyz * 2.0 - 1.0; // bc5a9e24
    n.xy *= strength;                                                   // 737d8839
    return normalize(float3(n.xy, max(0.001, n.z)));
}

// ---------------------------------------------------------------------------
// Vertex stage — BlockNodes VertexDescription.Position / Normal / Tangent.
// Only Position is connected (305a3b86 <- 9295cd68). Normal/Tangent keep their
// slot defaults, so no tangent-space work happens in the vertex stage.
// ---------------------------------------------------------------------------

struct WaterVaryings
{
    float4 positionCS : SV_POSITION;
    float3 worldPos   : TEXCOORD0;
    float4 screenPos  : TEXCOORD1;
    float3 normalWS   : TEXCOORD2; // default (0,0,0): VertexDescription.Normal unconnected
    float3 tangentWS  : TEXCOORD3; // default (0,0,0): VertexDescription.Tangent unconnected
};

float waveHeight(float3 worldPos)
{
    float phase  = time * waveSpeed;                       // 4f2b931b
    float waveX  = sinewaveProfile(worldPos.x + phase, waveLength); // 8f06e6fd -> 129d00de
    float waveZ  = sinewaveProfile(worldPos.z + phase, waveLength); // 9c6400eb -> ca5da90f
    return waveAmplitude * (waveX + waveZ) * 0.5;          // f8d367f5 / 12380a21(B=2) / 25c5048e
}

[shader("vertex")]
WaterVaryings vs_main(float3 positionOS : POSITION)
{
    WaterVaryings v;
    float3 worldPos = mul(objectToWorld, float4(positionOS, 1.0)).xyz; // PositionNode 61d1b431 (m_Space=2)
    worldPos.y += waveHeight(worldPos);                               // 9b272b11 -> e44923c7
    float3 objectPos = mul(worldToObject, float4(worldPos, 1.0)).xyz; // TransformNode 9295cd68 (world->object)

    v.positionCS = mul(worldToClip, float4(objectPos, 1.0));          // 305a3b86
    v.worldPos   = worldPos;
    v.screenPos  = float4(v.positionCS.xy, v.positionCS.zw);
    v.normalWS   = float3(0.0, 0.0, 0.0);
    v.tangentWS  = float3(0.0, 0.0, 0.0);
    return v;
}

// ---------------------------------------------------------------------------
// Fragment stage — SurfaceDescription blocks. Connected: BaseColor (b671fb55),
// NormalTS (0e9800bf), Smoothness (c40f9618). The other six blocks are
// unconnected and therefore use their serialized defaults (0/1 as appropriate).
// ---------------------------------------------------------------------------

// Four scrolling directions built from Split/Combine/Negate of Refraction Speed:
//   (0,s), (s,-s), (0,-s), (-s,s)  — nodes 1729c992, bd5ff6e8, f5b77941, 33577ead
float3 blendedRefractionNormal()
{
    float s = refractionSpeed;
    float2 dirA = float2(0.0, s);
    float2 dirB = float2(s, -s);
    float2 dirC = -dirA;
    float2 dirD = float2(-s, s);

    float2 scale = refractionScale; // 486bad7d -> 8ef80351 (also scales foam/refraction UVs)
    float3 nA = sampleWaterNormal(scrollingUV(dirA) * scale, 0.1); // 6ec45782 -> a3e40612
    float3 nB = sampleWaterNormal(scrollingUV(dirB) * scale, 0.1); // bee82d22 -> 14a6acdb
    float3 nC = sampleWaterNormal(scrollingUV(dirC) * scale, 0.1); // 8ff6f95c -> 5378309f
    float3 nD = sampleWaterNormal(scrollingUV(dirD) * scale, 0.1); // 6c54bdc7 -> 0131bb1a

    // NormalBlend binary tree: 88ab288c, b0808911, cb318b8b.
    // Simplified whiteout blend (exact ShaderGraph blend formula not in JSON).
    float3 upper = normalize(float3(nA.xy + nB.xy, nA.z * nB.z));
    float3 lower = normalize(float3(nC.xy + nD.xy, nC.z * nD.z));
    return normalize(float3(upper.xy + lower.xy, upper.z * lower.z));
}

float3 surfaceNormalTS(float sceneEyeDepth, float surfaceEyeDepth)
{
    float3 rawNormal = blendedRefractionNormal();                     // cb318b8b
    float fade = saturate(depthFade(strength, deepness, sceneEyeDepth, surfaceEyeDepth)); // 66ad73dd -> 8ad52966
    float scaled = lerp(0.0, normalStrength, fade);                   // df1a31e3 (A unconnected = 0)
    return saturate(rawNormal * scaled);                              // 02e7c306 -> e4911d87 -> 0e9800bf
}

float surfaceSmoothness(float3 viewDirWS)
{
    // DotProductNode b1366f3b has B default (0,1,0,0) -> dot(view, up) = view.y.
    float facing = step(0.0, dot(viewDirWS, float3(0.0, 1.0, 0.0)));  // cab8dd8e
    return saturate(facing * smoothness);                             // 7f7b02cc -> c9f39eb6 -> c40f9618
}

float3 waterBodyColor(float sceneEyeDepth, float surfaceEyeDepth)
{
    float depthMix = saturate(depthFade(strength, deepness, sceneEyeDepth, surfaceEyeDepth)); // 8ad52966.3
    float3 baseWater = lerp(shallowColor, deepColor, depthMix);        // b164de58

    // FresnelNode 2e6e4697: Normal and View Dir are unconnected; their serialized
    // defaults are zero, so this evaluates to pow(1,power) = 1 (uncertainty #2).
    float horizonT = pow(1.0 - saturate(dot(float3(0,0,0), float3(0,0,0))), horizonDistance);
    return lerp(baseWater, horizonColor, horizonT);                    // dffd260b
}

float foamMask(float sceneEyeDepth, float surfaceEyeDepth)
{
    // depthfade(Foam Amount, 0 Strength) * Foam cutout -> step(edge, noise).
    float edge = depthFade(0.0, foamAmount, sceneEyeDepth, surfaceEyeDepth) * foamCutout; // 5e469230 -> 4fda3dbe
    // GradientNoiseNode 8259c3ec (synonyms: perlin noise), Scale = 10.
    float2 noiseUV = scrollingUV(float2(foamSpeed, foamSpeed)) * 10.0;
    float2 cell = floor(noiseUV), f = fract(noiseUV);
    float2 u = f * f * (3.0 - 2.0 * f);
    float h00 = hash21(cell), h10 = hash21(cell + float2(1, 0));
    float h01 = hash21(cell + float2(0, 1)), h11 = hash21(cell + float2(1, 1));
    float noise = lerp(lerp(h00, h10, u.x), lerp(h01, h11, u.x), u.y);
    float stepMask = step(edge, noise);                                 // 0e028674
    return saturate(stepMask * foamAlpha);                              // f16ede3a (Foam color.a)
}

[shader("fragment")]
float4 fs_main(WaterVaryings v)
{
    float2 screenUV = v.screenPos.xy / v.screenPos.w;                   // 58c95e68
    float  surfaceEyeDepth = v.screenPos.w;
    float  sceneEyeDepth = sceneDepth.Sample(linearRepeat, screenUV).r; // depthfade subgraph
    float3 viewDir = normalize(cameraPosWS - v.worldPos);               // 1a0d2800

    // --- refraction: perturb the opaque-scene lookup with the blended normal ---
    float2 refractOffset = blendedRefractionNormal().xz * refractionStrength;
    float3 refracted = opaqueScene.Sample(linearRepeat, saturate(screenUV + refractOffset)).rgb; // 8400e357

    float3 color = waterBodyColor(sceneEyeDepth, surfaceEyeDepth);
    float mask = foamMask(sceneEyeDepth, surfaceEyeDepth);
    color = lerp(color, foamColor, mask);                               // f7a7c8df

    // Alpha of the foam-blended colour drives the refraction/water mix.
    // Horizon color alpha defaults to 0, hence T=0 in the converted material.
    float waterAlpha = foamAlpha * mask;
    color = lerp(refracted, color, waterAlpha);                         // 13b8fd8b

    color = lerp(color, fogColor, fogDensity);                          // 9551f005 -> b671fb55

    // SurfaceDescription.NormalTS and .Smoothness are consumed by the URP Lit
    // target (lighting is done by the host, not by this reference function).
    float3 normalTS = surfaceNormalTS(sceneEyeDepth, surfaceEyeDepth);  // 0e9800bf
    float smoothness = surfaceSmoothness(viewDir);                      // c40f9618
    // Host would feed normalTS/smoothness into its BRDF here.
    return float4(color * (0.75 + 0.25 * normalTS.z) * (0.5 + 0.5 * smoothness), 1.0);
}
