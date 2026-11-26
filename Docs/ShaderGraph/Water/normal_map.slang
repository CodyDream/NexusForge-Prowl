// normal_map.slang — direct Slang reference translation of
// Shaders/SubGraphs/Normal map.shadersubgraph.
//
// Purpose: tangent-space normal lookup with an adjustable XY flattening and a
// passthrough of the texture alpha as "noise" (the noise output is unused by
// the four parent instances in water.shadergraph).
// Object counts: GraphData 1, CategoryData 1, SubGraphOutputNode 1,
//   PropertyNode 2 (UV, normal strength), TilingAndOffsetNode 1,
//   SampleTexture2DNode 1 (m_TextureType=1 normal map), NormalStrengthNode 1,
//   PreviewNode 1; slots: Vector1 7, Vector2 4, Vector3 3, Vector4 1,
//   UV 2, Texture2DInput 1, SamplerState 1, DynamicVector 2.
// Confidence: HIGH. Default texture GUID 3bd52d67… resolves to
// Textures/Water Normal.png (from the local .meta file).
// External state required: the normal texture and a repeating sampler.

struct NormalMapInputs
{
    float2 uv;             // property "UV", default (0.5,0.5); parent always connects it
    float  normalStrength; // property "normal strength", default 0.1
};

struct NormalMapOutput
{
    float3 normal; // output slot 0
    float  noise;  // output slot 1 (texture alpha; unused upstream)
};

// SampleTexture2DNode bc5a9e24: m_TextureType=1 => normal decode (rgb*2-1).
// NormalStrengthNode 737d8839: scales xy by Strength, keeps z, normalises.
// PreviewNode 66dd0c0f is editor-only passthrough for the A channel.
NormalMapOutput sampleNormalMap(Texture2D<float4> tex, SamplerState s,
                                float2 uv, float strength)
{
    float4 rgba = tex.Sample(s, uv);                 // bc5a9e24
    float3 n = rgba.xyz * 2.0 - 1.0;
    n.xy *= strength;                                // 737d8839
    NormalMapOutput o;
    o.normal = normalize(float3(n.xy, max(0.001, n.z)));
    o.noise = rgba.a;                                // 66dd0c0f -> b0a89b3b.1
    return o;
}
