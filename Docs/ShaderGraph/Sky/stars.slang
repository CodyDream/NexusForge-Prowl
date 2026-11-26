// ---------------------------------------------------------------------------
// stars.slang -- direct reference translation of
//   "SubGraphs/stars.shadersubgraph"
//
// Inferred purpose: sparse colored star field from a value-noise mask,
// sharpened by a power, tinted by a 6-key gradient sampled with Perlin noise,
// and scaled by an externally supplied intensity (twinkle).
//
// Source graph counts (from local JSON):
//   45 JSON objects | 13 nodes | 13 edges | 3 properties | 1 output
//   ("Out_Vector4"). Properties: noise scale=100, star power=0, intensity=1.
//   Node types: Property x3, SubGraph x1 (skybox uv), Simple Noise x1,
//   Gradient Noise x1, Gradient x1, Sample Gradient x1, Power x1,
//   Divide x1, Multiply x2, SubGraphOutput x1.
//
// Edge trace (short ids):
//   cd9f8974 skybox uv -> 372f9ff4 Simple Noise(h0) -> 6a07d41d Pow
//       <- 23f6455c star power -> 33bc31c0.A
//   noise scale -> ff84650c Divide by 10 -> bf5fee3a Gradient Noise(h0)
//       -> eeacde54 Sample Gradient(217ef3bb 6-key ramp)
//       -> 74805973 Multiply by ea87f90d intensity -> 33bc31c0.B
//
// NOTE: the subgraph computes its own skybox uv (cd9f8974); the main graph
// also instantiates skybox uv separately, so skyboxUV is evaluated twice.
// Hash functions are documented approximations: exact Unity hash bits cannot
// be recovered from the graph JSON alone.
// ---------------------------------------------------------------------------

#include "skybox_uv.slang"

float sgHash21(float2 p)
{
    // Unity Simple Noise "standard" hash approximation (m_HashType = 0).
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float sgValueNoise(float2 uv, float scale)
{
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float2 w = f * f * (3.0 - 2.0 * f);
    float a = sgHash21(i);
    float b = sgHash21(i + float2(1.0, 0.0));
    float c = sgHash21(i + float2(0.0, 1.0));
    float d = sgHash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, w.x), lerp(c, d, w.x), w.y);
}

float2 sgGrad(float2 p)
{
    float a = sgHash21(p) * SG_TAU;
    return float2(cos(a), sin(a));
}

float sgGradientNoise(float2 uv, float scale)
{
    // Unity Gradient Noise "standard" / Perlin approximation.
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float n00 = dot(sgGrad(i + float2(0.0, 0.0)), f - float2(0.0, 0.0));
    float n10 = dot(sgGrad(i + float2(1.0, 0.0)), f - float2(1.0, 0.0));
    float n01 = dot(sgGrad(i + float2(0.0, 1.0)), f - float2(0.0, 1.0));
    float n11 = dot(sgGrad(i + float2(1.0, 1.0)), f - float2(1.0, 1.0));
    return lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y) * 0.5 + 0.5;
}

// 217ef3bb GradientNode.SerializableColorKeys, stored as (r,g,b,time).
// SerializableAlphaKeys are constant (1,0),(1,1); mode = 1.
static const float4 sgStarKeys[6] = {
    float4(1.000000, 0.000000, 0.000000, 0.155886),
    float4(0.000000, 0.807843, 0.021906, 0.329412),
    float4(0.000000, 0.414922, 0.627451, 0.508827),
    float4(1.000000, 0.922036, 0.000000, 0.688243),
    float4(1.000000, 1.000000, 1.000000, 0.905882),
    float4(1.000000, 0.000000, 0.628050, 1.000000)
};

float3 sampleStarGradient(float t)
{
    if (t <= sgStarKeys[0].w) return sgStarKeys[0].xyz;
    if (t >= sgStarKeys[5].w) return sgStarKeys[5].xyz;
    for (int k = 0; k < 5; ++k)
    {
        float t0 = sgStarKeys[k].w;
        float t1 = sgStarKeys[k + 1].w;
        if (t <= t1)
        {
            float w = saturate((t - t0) / max(1e-5, t1 - t0));
            return lerp(sgStarKeys[k].xyz, sgStarKeys[k + 1].xyz, w);
        }
    }
    return sgStarKeys[5].xyz;
}

// uv              : equirectangular sky UV (from skyboxUV)
// noiseScale      : main graph property _Star_density (material: 1000)
// starPower       : main graph property _Star_Power (material: 21.2)
// intensity       : twinkle * noise, computed in the main graph
float4 stars(float2 uv, float noiseScale, float starPower, float intensity)
{
    float starNoise = sgValueNoise(uv, noiseScale);           // 372f9ff4
    float sharpened = pow(starNoise, starPower);              // 6a07d41d
    float gradientTime = sgGradientNoise(uv, noiseScale / 10.0); // ff84650c -> bf5fee3a
    float3 tint = sampleStarGradient(gradientTime);           // eeacde54
    float3 outRgb = sharpened * (tint * intensity);           // 33bc31c0
    return float4(outRgb, 1.0);
}

// Wrapper matching the subgraph's internal skybox uv instance (cd9f8974).
float4 starsFromPosition(float3 positionWS, float noiseScale,
                         float starPower, float intensity)
{
    return stars(skyboxUV(positionWS), noiseScale, starPower, intensity);
}
