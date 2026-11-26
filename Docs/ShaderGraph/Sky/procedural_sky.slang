// ---------------------------------------------------------------------------
// procedural_sky.slang -- direct reference translation of
//   "Shaders/Skybox.shadergraph" (SurfaceDescription.BaseColor chain)
//
// Inferred purpose: stylized sky dome with day/night gradient, hard sun and
// moon disks, noise-based star field, two-layer scrolling cloud mask and fog.
//
// Source graph counts (from local JSON):
//   Main: 374 objects | 99 nodes (11 BlockNode) | 98 edges | 17 properties |
//         6 groups | 2 active targets | 1 connected fragment block.
//   skybox uv subgraph: 12 nodes | 13 edges | 0 props (instantiated twice).
//   stars subgraph:     13 nodes | 13 edges | 3 props (instantiated once).
//
// Node subsystems (group titles from JSON):
//   "Sky gradiant" (8 nodes): horizon/sky/night lerp, vertical band, offset.
//   "New Group" (16 nodes): sun + moon disk math (asin(dot), Step, Add).
//   "cloud" (16 nodes): two Simple Noise layers, pan, multiply, posterize.
//   "clouds edge" (10 nodes): smoothstep band, OneMinus, Step(0.93).
//   "fog" (6 nodes): OneMinus -> Saturate -> Pow -> Fog.Color lerp.
//   ungrouped stars (10 nodes): Time/Sine, Remap, noise, stars subgraph, lerp.
//
// CONFIDENCE: topology high. Uncertainties documented in ANALYSIS.md:
//   - ConstantNode values in skybox uv (PI vs TAU) -- see skyboxUVAlt.
//   - Noise hash bits approximated; m_HashType 1 = LPM-like, 0 = standard.
//   - _Moon_size = 7.7 from the original material exceeds asin's range,
//     so the moon disk is expected to be absent with shipped values.
// ---------------------------------------------------------------------------

#include "skybox_uv.slang"
#include "stars.slang"

struct SkyParams
{
    float3 horizonColor;   // _horizon_color
    float3 skyColor;       // _sky_color
    float3 nightColor;     // _Sky_night_color
    float2 starSine;       // _Star_sine : remap output range for Sine Time
    float  starDensity;    // _Star_density -> stars.noise scale
    float  starPower;      // _Star_Power   -> stars.star power
    float3 sunColor;       // _Sun_color
    float  sunSize;        // _Sun_size    : Step edge on asin(dot)
    float  offset;         // _offset      : vertical band shift
    float  cloudHeight;    // _cloud_highet (sic)
    float  cloudEdge;      // _clod_edge   (sic)
    float  cloudScale;     // _cloud_scale
    float  windSpeed;      // _wind_speed
    float  fogHeight;      // _fog_height
    float3 moonColor;      // _Moon_color
    float  moonSize;       // _Moon_size   : Step edge on asin(dot)
    float3 moonDirection;  // _moon_direction
};

float sgRemap(float x, float inMin, float inMax, float outMin, float outMax)
{
    return outMin + (saturate((x - inMin) / max(1e-6, inMax - inMin))) * (outMax - outMin);
}

float sgPosterize6(float x)
{
    return floor(x * 6.0) / 6.0; // 3ff3d25f, Steps = 6
}

float sgHashStandard(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

// LPM-like hash used by Shader Graph Simple Noise with m_HashType = 1.
float sgHashLpm(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float sgValueNoiseScaled(float2 uv, float scale, int hashType)
{
    float2 p = uv * scale;
    float2 i = floor(p);
    float2 f = frac(p);
    float2 w = f * f * (3.0 - 2.0 * f);
    float h00 = (hashType == 1) ? sgHashLpm(i) : sgHashStandard(i);
    float h10 = (hashType == 1) ? sgHashLpm(i + float2(1.0, 0.0)) : sgHashStandard(i + float2(1.0, 0.0));
    float h01 = (hashType == 1) ? sgHashLpm(i + float2(0.0, 1.0)) : sgHashStandard(i + float2(0.0, 1.0));
    float h11 = (hashType == 1) ? sgHashLpm(i + float2(1.0, 1.0)) : sgHashStandard(i + float2(1.0, 1.0));
    return lerp(lerp(h00, h10, w.x), lerp(h01, h11, w.x), w.y);
}

// All inputs correspond to graph nodes; no engine globals are hidden here.
float3 proceduralSky(float3 positionWS, float3 viewDir, float3 lightDir,
                     float time, float sineTime, float3 fogColor, SkyParams p)
{
    float2 sky = skyboxUV(positionWS);          // 1a6c53b0
    float band = sky.y + p.offset;              // 4ed48c06 -> fe6f3df2

    // ---- sky gradient -----------------------------------------------------
    float dayMix = saturate(lightDir.y + 1.0); // fc303c94 -> eff750d4 -> c0091958
    float3 dayNight = lerp(p.skyColor, p.nightColor, dayMix); // ad6357c1
    float3 baseSky = lerp(p.horizonColor, dayNight, band);    // 57be286c

    // ---- stars -------------------------------------------------------------
    float twinkle = sgRemap(sineTime, -1.0, 1.0, p.starSine.x, p.starSine.y); // 5590d433
    // 2b77ff9e has its UV input unconnected: noise is evaluated at (0,0),
    // so this term is a per-frame scalar, not a spatial pattern.
    float starNoiseConst = sgValueNoiseScaled(float2(0.0, 0.0), 100.0, 0);
    float starIntensity = twinkle * starNoiseConst;           // 0fa0c7c2
    float3 starRgb = stars(sky, p.starDensity, p.starPower, starIntensity).xyz; // 9b8bc176
    float3 skyWithStars = baseSky + lerp(float3(0.0), starRgb, band); // e27aec66

    // ---- sun / moon disks --------------------------------------------------
    float3 sunDir = normalize(lightDir + float3(0.0, -0.1, 0.1)); // db1278b4 -> 5d8de16b
    float3 v = normalize(viewDir);                                 // 6c9417c3
    float sunAngle = asin(dot(sunDir, v));                        // 9a4533e0 -> 2196d029
    float sunStep = step(p.sunSize, sunAngle);                    // aa3111b1
    float3 sunDisk = saturate(sunStep * p.sunColor);              // aac7b897 -> 07877b45
    float3 skyWithSun = lerp(skyWithStars, sunDisk, sunStep);     // 6b7ce7df

    float3 moonDir = normalize(p.moonDirection);                  // a16e9b4f -> cc93e678
    float moonAngle = asin(dot(moonDir, normalize(viewDir)));     // 21fd09f4 -> 9a34ad6a
    float moonStep = step(p.moonSize, moonAngle);                 // 8ca888e6
    float3 moonDisk = saturate(moonStep * p.moonColor);           // b63cb2a1 -> a6b8985e
    float3 skyWithMoon = skyWithSun + moonDisk;                   // de3ceb54

    // ---- clouds: two noise layers, no iteration nodes ----------------------
    float pan = time * p.windSpeed * 0.1;                         // 2d6e8fe4 -> 7d6af90a
    float2 uvFast = (sky * p.cloudScale) + float2(-2.0 * pan, 0.0);  // 15316329
    float2 uvSlow = (sky * p.cloudScale) + float2(pan, 0.0);         // 82910714
    float nFast = saturate(sgValueNoiseScaled(uvFast, 100.0, 1)); // 99741bd8 -> 5aafa6d0
    float nSlow = saturate(sgValueNoiseScaled(uvSlow, 100.0, 1)); // 39d7f17a -> e1184bc3
    float cloudProduct = saturate(nFast * nSlow);                 // 09bd9c6f -> cc29e142

    float quantized = sgPosterize6(cloudProduct);                 // 3ff3d25f
    float cloudMask = sgRemap(quantized, 0.0, 1.0, 1.0, 0.0);     // b308e6b9

    float edgeHi = saturate(p.cloudHeight + p.cloudEdge);          // dc25056d -> fffdcfe5
    float cloudEdge = smoothstep(p.cloudHeight, edgeHi, sky.y + 0.1); // da3ed51e
    float lifted = sgRemap(cloudProduct, 0.0, 1.0, 1.0, 2.0);     // c91d3563
    float special = lifted * (1.0 - cloudEdge);                   // 41b86582
    float useSpecial = step(0.93, special);                       // 0fc18d5f
    float3 skyCloud = lerp(skyWithMoon, float3(cloudMask), useSpecial); // 56e631d6

    // ---- fog and final Base Color -----------------------------------------
    float fogFactor = pow(saturate(1.0 - sky.y), p.fogHeight);    // 19c981cd
    return lerp(skyCloud, fogColor, fogFactor);                   // e43a9339 -> 5f2dd7f6
}
