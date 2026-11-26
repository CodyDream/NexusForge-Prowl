// ---------------------------------------------------------------------------
// WaterClipmap.slang — direct Slang reference translation of the hand-written
// shader "Custom/Water_Surface" (file: Shaders/Water/WaterClipmap.shader).
//
// NOTE: there is NO WaterClipmap.shadergraph in the sources. This file therefore
// has no ShaderGraph node IDs to report. Inventory read from the HLSL source:
//   60 ShaderLab properties, 11 functions
//   (tessVert, CalcEdgeTessFactor, PatchConstantFunction, hull, domain,
//    Ocean_D_GGX, Ocean_G_Smith, Ocean_F_Schlick, SampleMicroNormal,
//    EvaluateProceduralSkyReflection, frag),
//   27 texture declarations, 53 explicit per-material scalar/vector uniforms.
// Purpose: GPU-FFT clipmap ocean surface with tessellation, cascade blending,
//          ship wake, refraction, SSS, foam and sky/cubemap reflection.
// Confidence: HIGH for the structure; MEDIUM for exact lighting constants, which
// are hand-tuned. This is an independently structured reference implementation —
// Unity macros/includes are replaced by explicit uniforms and helper functions.
// External state REQUIRED (cannot stand alone): FFT displacement/normal/foam
// cascades from OceanFFT.compute, ship-wake RT from ShipWakeSim.compute,
// scene depth + opaque textures, main-light shadow data, fog, sky probe.
// ---------------------------------------------------------------------------

cbuffer ClipmapParams
{
    // Tessellation
    float tessMax, tessMin, tessStartDist, tessEndDist;
    // FFT cascades
    float fftDomain0, fftDomain1, cascade1FadeDist, baseWaterHeight, waveAmplitude;
    // Wake
    float wakeHeightScale, wakeNormalScale, wakeFoamScale;
    float2 wakeCenterXZ; float wakeAreaSize;
    // Micro normal
    float normalTiling, normalScrollSpeed, normalStrength, normalFadeDist;
    // Shadow control
    float shadowDarkness, shadowAmbientFloor, shadowBlocksReflection, waveAOStrength;
    // Body optics
    float3 deepColor, shallowColor, extinction;
    // Caustics
    float causticsTiling, causticsSpeed, causticsIntensity;
    // Subsurface scattering
    float3 sssColor; float sssIntensity, sssPower, sssDistortion, sssBase;
    // Foam
    float3 foamAlbedo, bubbleColor; float foamScale, crestAmount, crestSpread;
    float shoreFoamDepth, foamFadeDist; float2 windDir;
    // Sky / reflection
    float3 skyZenith, skyHorizon, sunsetColor;
    float envReflectionIntensity, probeBlend, coronaIntensity, roughness;
    float refractionStrength, specularIntensity, glintIntensity;
    // Horizon fog
    float horizonFogStart, horizonFogEnd;
};

cbuffer Frame
{
    float4x4 objectToWorld, worldToClip, worldToShadow;
    float3 cameraPosWS;
    float  timeY;
    float4 zBufferParams;
    float3 sunDirection, sunColor;
    float  sunShadowAttenuation;
    float  fogFactor;
    float3 fogColor;
};

Texture2D<float4> fftDisplacement0, fftNormal0, fftFoam0;
Texture2D<float4> fftDisplacement1, fftNormal1, fftFoam1;
Texture2D<float4> shipWake, detailNormal, foamNoise;
TextureCube<float4> skyProbe;
Texture2D<float4> sceneDepth, sceneColor;
SamplerState linearRepeat, linearClamp;

// ---------------------------------------------------------------------------
// Tessellation stage
// ---------------------------------------------------------------------------

struct ControlPoint { float3 worldPos : TEXCOORD0; };
struct PatchFactors { float edge[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };

struct OceanVaryings
{
    float4 positionCS : SV_POSITION;
    float3 worldPos : TEXCOORD0;
    float4 screenPos : TEXCOORD1;
    float2 undisplacedXZ : TEXCOORD2;
    float  distToCam : TEXCOORD3;
    float3 displacement : TEXCOORD4;
    float  fog : TEXCOORD5;
};

float edgeTessFactor(float3 a, float3 b)
{
    float midDist = distance((a + b) * 0.5, cameraPosWS);
    float t = saturate((midDist - tessStartDist) / max(0.001, tessEndDist - tessStartDist));
    return lerp(tessMax, tessMin, t); // near = fine, far = coarse
}

PatchFactors patchConstants(InputPatch<ControlPoint, 3> patch)
{
    PatchFactors f;
    f.edge[0] = edgeTessFactor(patch[1].worldPos, patch[2].worldPos);
    f.edge[1] = edgeTessFactor(patch[2].worldPos, patch[0].worldPos);
    f.edge[2] = edgeTessFactor(patch[0].worldPos, patch[1].worldPos);
    f.inside = (f.edge[0] + f.edge[1] + f.edge[2]) / 3.0;
    return f;
}

float wakeHeightAt(float2 xz, out float edgeFade)
{
    float2 uv = (xz - wakeCenterXZ) / max(1.0, wakeAreaSize) + 0.5;
    edgeFade = 0.0;
    if (any(uv < 0.002) || any(uv > 0.998)) return 0.0;
    edgeFade = saturate(min(uv.x, 1.0 - uv.x) * 10.0) * saturate(min(uv.y, 1.0 - uv.y) * 10.0);
    return shipWake.SampleLevel(linearClamp, uv, 0).r * wakeHeightScale * edgeFade;
}

[shader("vertex")]
ControlPoint vs_control(float3 positionOS : POSITION)
{
    ControlPoint cp;
    cp.worldPos = mul(objectToWorld, float4(positionOS, 1.0)).xyz;
    return cp;
}

[shader("hull")]
[domain("tri")]
[partitioning("fractional_odd")]
[outputtopology("triangle_cw")]
[patchconstantfunc("patchConstants")]
[outputcontrolpoints(3)]
ControlPoint hs_pass(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

[shader("domain")]
[domain("tri")]
OceanVaryings ds_sample(PatchFactors f, OutputPatch<ControlPoint, 3> patch,
                        float3 bary : SV_DomainLocation)
{
    float3 p = patch[0].worldPos * bary.x + patch[1].worldPos * bary.y + patch[2].worldPos * bary.z;
    OceanVaryings o;
    o.undisplacedXZ = p.xz;
    o.distToCam = length(cameraPosWS - p);

    float2 uv0 = p.xz / max(1.0, fftDomain0);
    float2 uv1 = p.xz / max(1.0, fftDomain1);
    float c1 = saturate((cascade1FadeDist - o.distToCam) / max(1e-5, cascade1FadeDist * 0.4));

    float3 d0 = fftDisplacement0.SampleLevel(linearRepeat, uv0, 0).xyz;
    float3 d1 = fftDisplacement1.SampleLevel(linearRepeat, uv1, 0).xyz;
    o.displacement = d0 + d1 * c1;

    float fade;
    float wakeY = wakeHeightAt(o.undisplacedXZ, fade);
    o.worldPos = p + o.displacement;
    o.worldPos.y += baseWaterHeight + wakeY;

    o.positionCS = mul(worldToClip, float4(o.worldPos, 1.0));
    o.screenPos  = float4(o.positionCS.x, o.positionCS.y, o.positionCS.z, o.positionCS.w);
    o.fog = saturate(1.0 / exp2(o.positionCS.z * 0.0001)); // host fog factor supplied externally
    return o;
}

// ---------------------------------------------------------------------------
// BRDF helpers
// ---------------------------------------------------------------------------

float ggxDistribution(float nDotH, float rough)
{
    float a = rough * rough, a2 = a * a;
    float d = nDotH * (a2 * nDotH - nDotH) + 1.0;
    return a2 / max(1e-5, 3.14159265 * d * d);
}

float smithVisibility(float nDotV, float nDotL, float rough)
{
    float a = rough * rough;
    float gv = nDotL * sqrt((nDotV - a * nDotV) * nDotV + a);
    float gl = nDotV * sqrt((nDotL - a * nDotL) * nDotL + a);
    return 0.5 / max(1e-5, gv + gl);
}

float schlickFresnel(float vDotH, float f0)
{
    return f0 + (1.0 - f0) * pow(saturate(1.0 - vDotH), 5.0);
}

// Two scrolling taps of the detail normal; faded out with distance.
float3 microNormalWS(float2 worldXZ, float dist, float t)
{
    float2 a = worldXZ * normalTiling + float2(t * normalScrollSpeed * 0.04, t * normalScrollSpeed * 0.025);
    float2 b = worldXZ * (normalTiling * 1.83) + float2(-t * normalScrollSpeed * 0.03, t * normalScrollSpeed * 0.042);
    float3 n1 = detailNormal.Sample(linearRepeat, a).xyz * 2.0 - 1.0;
    float3 n2 = detailNormal.Sample(linearRepeat, b).xyz * 2.0 - 1.0;
    float3 n = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
    n.xy *= normalStrength * saturate(1.0 - dist / max(1.0, normalFadeDist));
    return normalize(n);
}

float3 proceduralSky(float3 r, float3 l, float3 lCol, float rough, float shadow)
{
    float elevation = saturate(r.y);
    float3 grad = lerp(skyHorizon, skyZenith, pow(elevation, 0.65));
    float2 rx = normalize(r.xz + 1e-5), lx = normalize(l.xz + 1e-5);
    float warm = pow(saturate(dot(rx, lx)), 2.5)
               * pow(saturate(1.0 - elevation), 1.5)
               * saturate(1.2 - abs(l.y) * 1.5);
    grad = lerp(grad, sunsetColor * lCol, warm * 0.85 * shadow);
    float corona = pow(saturate(dot(r, l)), lerp(32.0, 10.0, saturate(rough * 2.0)))
                 * coronaIntensity * saturate(l.y * 1.5 + 0.2);
    return grad + lCol * corona * shadow;
}

// Host-supplied depth conversion, written as a plain helper (not a Unity include).
float LinearEyeDepth(float raw, float4 zParams)
{
    return 1.0 / (zParams.z * raw + zParams.w);
}

// ---------------------------------------------------------------------------
// Fragment stage
// ---------------------------------------------------------------------------

[shader("fragment")]
float4 fs_surface(OceanVaryings i) : SV_Target
{
    float2 screenUV = i.screenPos.xy / i.screenPos.w;
    float t = timeY;
    float3 V = normalize(cameraPosWS - i.worldPos);

    // --- shadow interpretation: diffuse floor, direct kill, reflection dim ---
    float rawShadow = sunShadowAttenuation;
    float sceneShadow = lerp(shadowAmbientFloor, 1.0, lerp(1.0, rawShadow, shadowDarkness));
    float directShadow = lerp(1.0, rawShadow, shadowDarkness);
    float reflectionShadow = lerp(1.0, rawShadow, shadowBlocksReflection * shadowDarkness);

    // --- cascade normals ---
    float2 uv0 = i.undisplacedXZ / max(1.0, fftDomain0);
    float2 uv1 = i.undisplacedXZ / max(1.0, fftDomain1);
    float c1 = saturate((cascade1FadeDist - i.distToCam) / max(1e-5, cascade1FadeDist * 0.4));
    float3 n0 = fftNormal0.Sample(linearRepeat, uv0).xyz;
    float3 n1 = lerp(float3(0, 1, 0), fftNormal1.Sample(linearRepeat, uv1).xyz, c1);
    float3 fftN = normalize(float3(n0.x + n1.x, max(0.2, n0.y + n1.y - 1.0), n0.z + n1.z));
    float3 micro = microNormalWS(i.worldPos.xz, i.distToCam, t);

    // --- wake normal from central differences of the wake height field ---
    float2 wuv = (i.worldPos.xz - wakeCenterXZ) / max(1.0, wakeAreaSize) + 0.5;
    float3 wakeN = float3(0, 0, 0);
    float4 wakeSample = float4(0, 0, 0, 0);
    if (all(wuv > 0.002) && all(wuv < 0.998))
    {
        float fade = saturate(min(wuv.x, 1.0 - wuv.x) * 10.0) * saturate(min(wuv.y, 1.0 - wuv.y) * 10.0);
        wakeSample = shipWake.Sample(linearClamp, wuv) * fade;
        float texel = 1.0 / 1024.0, metersPerTexel = max(0.1, wakeAreaSize / 1024.0);
        float l = shipWake.Sample(linearClamp, wuv - float2(texel, 0)).r;
        float r = shipWake.Sample(linearClamp, wuv + float2(texel, 0)).r;
        float b = shipWake.Sample(linearClamp, wuv - float2(0, texel)).r;
        float u = shipWake.Sample(linearClamp, wuv + float2(0, texel)).r;
        wakeN = float3(-((r - l) / (2.0 * metersPerTexel)) * wakeNormalScale * fade, 0.0,
                        -((u - b) / (2.0 * metersPerTexel)) * wakeNormalScale * fade);
    }

    float3 N = normalize(float3(fftN.x + micro.x + wakeN.x,
                                max(0.15, fftN.y * micro.z),
                                fftN.z + micro.y + wakeN.z));
    float rawNV = dot(N, V);
    if (rawNV < 0.05) N = normalize(N + V * (0.05 - rawNV));
    float NV = max(0.02, dot(N, V));

    // --- refracted seabed with occluder-aware depth selection ---
    float surfaceEye = i.screenPos.w;
    float sceneEye = LinearEyeDepth(sceneDepth.Sample(linearClamp, screenUV).r, zBufferParams);
    float2 offset = refractionStrength * N.xz * min(1.0, 0.5 * max(0.0, sceneEye - surfaceEye)) / max(1.0, surfaceEye);
    float2 candidateUV = clamp(screenUV + offset, 0.001, 0.999);
    float candidateEye = LinearEyeDepth(sceneDepth.Sample(linearClamp, candidateUV).r, zBufferParams);
    float occluderWeight = smoothstep(-0.05, 0.15, candidateEye - surfaceEye);
    float2 finalUV = lerp(screenUV, candidateUV, occluderWeight);
    float waterDepth = max(0.0, lerp(sceneEye, candidateEye, occluderWeight) - surfaceEye);
    float3 seabed = sceneColor.Sample(linearClamp, finalUV).rgb;

    // --- wave AO, caustics, transmittance, upwelling ---
    float waveRel = i.worldPos.y - baseWaterHeight;
    float normHeight = saturate((waveRel + waveAmplitude) / max(0.1, waveAmplitude * 2.0));
    float ao = lerp(1.0, lerp(1.0 - waveAOStrength, 1.0, smoothstep(0.05, 0.85, normHeight)), shadowDarkness);
    float2 causticUV = (i.worldPos.xz + offset * 25.0) * causticsTiling;
    float c1s = detailNormal.Sample(linearRepeat, causticUV + t * causticsSpeed * 0.04).r;
    float c2s = detailNormal.Sample(linearRepeat, causticUV * 1.35 - t * causticsSpeed * 0.03).g;
    float causticMask = saturate(1.0 - waterDepth / 6.0) * saturate(1.0 - i.distToCam / 180.0) * saturate(sunDirection.y);
    seabed += seabed * pow(min(c1s, c2s), 2.5) * causticsIntensity * causticMask * sunColor * directShadow;

    float3 transmittance = exp(-extinction * waterDepth);
    float sunUp = saturate(dot(N, sunDirection));
    float wrapped = saturate(dot(N, sunDirection) * 0.5 + 0.5);
    float3 irradiance = lerp(skyHorizon, skyZenith, 0.5) * ao * 0.5
                      + sunColor * saturate(sunDirection.y) * lerp(1.0, wrapped, shadowDarkness)
                        * sceneShadow * ao * 0.7;
    float crest = saturate(waveRel * 0.15 + 0.5);
    float3 bodyTint = lerp(deepColor, shallowColor, crest);
    float3 interior = seabed * transmittance
                    + bodyTint * irradiance * (1.0 - transmittance) + shallowColor * 0.12;

    // --- subsurface scattering ---
    float4 foam0 = fftFoam0.Sample(linearRepeat, uv0);
    float4 foam1 = fftFoam1.Sample(linearRepeat, uv1);
    float persistent = max(foam0.r, foam1.r * c1);
    float whitecaps = max(foam0.g, foam1.g * c1);
    float thinness = max(foam0.b, foam1.b * c1);
    float3 lightRay = normalize(sunDirection + N * sssDistortion);
    float forward = pow(saturate(dot(V, -lightRay)), sssPower);
    float sssTerm = (forward * 4.5 + sssBase) * thinness * saturate(1.3 - abs(sunDirection.y) * 0.8);
    float3 sssGlow = sssColor * sunColor * (sssTerm * sssIntensity) * sceneShadow;

    // --- sun specular and sparkle ---
    float rough = clamp(roughness + saturate(i.distToCam / 900.0) * 0.18, 0.02, 0.45);
    float3 H = normalize(V + sunDirection);
    float nDotH = saturate(dot(N, H)), vDotH = saturate(dot(V, H));
    float3 specular = ggxDistribution(nDotH, rough) * smithVisibility(NV, sunUp, rough)
                    * schlickFresnel(vDotH, 0.0204) * sunColor * specularIntensity * directShadow;
    float3 glintN = normalize(N + micro * 2.5);
    float sparkle = pow(saturate(dot(glintN, H)), 800.0) * glintIntensity * saturate(1.0 - i.distToCam / 120.0);
    float corridor = pow(saturate(dot(fftN, H)), 8.0);
    specular += sparkle * corridor * schlickFresnel(vDotH, 0.0204) * sunColor * directShadow;

    // --- sky / cubemap reflection, dimmed in shadow ---
    float fresnel = schlickFresnel(NV, 0.0204);
    float3 R = reflect(-V, N);
    R.y = max(R.y, 0.03);
    R = normalize(R);
    float3 sky = proceduralSky(R, sunDirection, sunColor, rough, directShadow);
    float3 cube = skyProbe.SampleLevel(linearRepeat, R, rough * 6.0).rgb;
    float3 probe = (cube.r + cube.g + cube.b > 0.001) ? cube : lerp(skyHorizon, skyZenith, 0.5);
    float3 reflection = lerp(sky, probe, probeBlend) * envReflectionIntensity;
    reflection = lerp(deepColor * shadowAmbientFloor * 1.5, reflection, reflectionShadow);

    float3 color = lerp(interior + sssGlow * saturate(waterDepth / 0.15), reflection, fresnel) + specular;

    // --- foam: crest / shoreline / ship wake ---
    float fadeFoam = saturate((foamFadeDist - i.distToCam) / (foamFadeDist * 0.35));
    if (fadeFoam > 0.001)
    {
        float2 stable = i.worldPos.xz - i.displacement.xz * 0.3;
        float nA = foamNoise.Sample(linearRepeat, stable * foamScale).r;
        float nB = foamNoise.Sample(linearRepeat, stable * (foamScale * 1.85) + float2(t * 0.02, -t * 0.015)).r;
        float cellular = (nA + nB) * 0.5;

        float energy = saturate(whitecaps * crestAmount + persistent * crestSpread);
        float threshold = lerp(0.85, 0.12, energy);
        float crestMask = smoothstep(threshold - 0.25, threshold + 0.25, cellular) * saturate(energy * 1.8);
        float shoreEnergy = smoothstep(max(0.01, shoreFoamDepth), 0.0, waterDepth);
        float shoreMask = shoreEnergy * smoothstep(0.25, 0.75, cellular) * 0.65;
        float wakeEnergy = saturate(wakeSample.g * wakeFoamScale);
        float wakeMask = wakeEnergy * smoothstep(0.15, 0.85, cellular + wakeEnergy * 0.4);
        float totalFoam = saturate(max(max(crestMask, shoreMask), wakeMask) * fadeFoam);

        if (totalFoam > 0.001)
        {
            float foamNL = lerp(1.0, saturate(dot(N, sunDirection) * 0.45 + 0.55), shadowDarkness);
            float3 surfaceFoam = foamAlbedo * (sunColor * foamNL * sceneShadow + skyHorizon * 0.55);
            float3 bubbles = bubbleColor * (skyHorizon * 0.65 + 0.35);
            float3 foamRGB = lerp(bubbles, surfaceFoam, smoothstep(0.10, 0.60, totalFoam));
            float alpha = saturate(pow(totalFoam, 1.2) * smoothstep(0.01, 0.35, totalFoam) * 0.95);
            color = lerp(color, foamRGB, alpha);
        }
    }

    float horizonFog = pow(saturate((i.distToCam - horizonFogStart) / max(1.0, horizonFogEnd - horizonFogStart)), 1.8);
    color = lerp(color, skyHorizon, horizonFog);
    color = lerp(color, fogColor, fogFactor);
    return float4(color, 1.0);
}
