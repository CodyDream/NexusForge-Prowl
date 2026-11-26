// ---------------------------------------------------------------------------
// skybox_uv.slang -- direct reference translation of
//   "SubGraphs/skybox uv.shadersubgraph"
//
// Inferred purpose: convert a world-space sky direction into an
// equirectangular-style (longitude, latitude) UV pair.
//
// Source graph counts (from local JSON, not Unity source):
//   46 JSON objects | 12 nodes | 13 edges | 0 properties | 1 output ("New")
//   Node types: Position x1, Normalize x1, Split x1, Arcsine x1,
//               Arctangent2 x1, Constant x2, Divide x3, Combine x1,
//               SubGraphOutput x1
//
// Node id map (short ids):
//   e03c8062 Position(m_Space=2, source=0) -> f55955c8 Normalize
//   -> a5638aec Split
//   a5638aec.R/B -> a604aa89 Arctangent2 -> f932fc2d Divide by c905564f
//   a5638aec.G -> dff1eb62 Arcsine -> b17b99ac Divide by (3b3eaecf / 2)
//   f932fc2d/b17b99ac -> 052b64b9 Combine -> 18be40eb output
//
// CONFIDENCE: topology high; numeric constants LOW. The two ConstantNodes
// serialize m_constant = 1 and m_constant = 0 with synonyms
// "pi, tau, phi". We assume enum index 0 = PI, 1 = TAU. `skyboxUVAlt`
// exposes the swapped reading for A/B verification against a generated shader.
// ---------------------------------------------------------------------------

#ifndef SKYBOX_UV_SLANG_INCLUDED
#define SKYBOX_UV_SLANG_INCLUDED

#define SG_PI  3.141592653589793
#define SG_TAU 6.283185307179586

float2 skyboxUV(float3 positionWS)
{
    float3 d = normalize(positionWS);
    float longitude = atan2(d.x, d.z); // a604aa89
    float latitude  = asin(d.y);       // dff1eb62

    // c905564f (m_constant=1) assumed TAU; 3b3eaecf (m_constant=0)
    // assumed PI, divided by the Divide node's default B = 2.
    float u = longitude / SG_TAU;      // f932fc2d
    float v = latitude / (SG_PI * 0.5); // e0556493 -> b17b99ac

    return float2(u, v);               // 052b64b9 -> 18be40eb
}

// Alternative if m_constant 0 = TAU and 1 = PI.
float2 skyboxUVAlt(float3 positionWS)
{
    float3 d = normalize(positionWS);
    return float2(atan2(d.x, d.z) / SG_PI, asin(d.y) / SG_TAU);
}

#endif // SKYBOX_UV_SLANG_INCLUDED
