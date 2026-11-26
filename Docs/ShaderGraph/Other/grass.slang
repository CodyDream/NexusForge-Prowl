// Grass.shadergraph - direct Slang reference translation (inference only, no Unity source copied)
// Graph: 152 serialized objects / 44 nodes / 36 edges. Node instances: BlockNode 12, PropertyNode 7,
// PositionNode 6, MultiplyNode 5, SplitNode 3, AddNode 3, LerpNode 2, and one each of Combine,
// Transform, Subtract, TilingAndOffset, Time, GradientNoise.
// Inferred purpose: wind sway from Perlin noise (UV = world XZ + Time*speed + worldPos*_Base_noise),
// bending vertex X/Z; base color lerps _base_color -> _top_color by object-space Y; smoothness is
// object-space Y multiplied by _Smoothness. Same skeleton as Flower shader without the palette texture.
// Confidence: topology HIGH; noise and Transform(normalize) formulas MEDIUM.

cbuffer GrassParams
{
    float  _Noise_scale;     // default 2
    float  _Noise_Amplitute; // default 0.5 (typo preserved)
    float  _Smoothness;      // slider default 0
    float2 _Noise_speed;     // default (2,0)
    float4 _top_color;       // default (0,0,0,0)
    float4 _base_color;      // default (0,0,0,0)
    float  _Base_noise;      // slider default 0 - multiplies world position in UV offset
    float  _TimeY;           // external Unity _Time.y
};

float4x4 getViewProj();                      // external camera state
float4x4 worldToObjectMatrix();              // TransformNode from=2(World) to=0(Object)
float gradientNoise2(float2 uv, float scale);// GradientNoiseNode (synonym "perlin noise")
float objectSpaceY(float3 positionOS);       // external: object-space vertex Y (Position m_Space=0)

struct VSOut
{
    float4 positionCS : SV_POSITION;
    float  height     : TEXCOORD0;           // 10b0e56e.Split.G and c95fb042.Split.G
};

[shader("vertex")]
VSOut vsMain(float3 positionOS : POSITION, float3 positionWS : TEXCOORD0)
{
    // 375e7619/88dc4292: UV = world XZ, Tiling=(1,1); 05c313f6: Offset = Time*speed + worldXZ*_Base_noise
    float2 drift = _TimeY * _Noise_speed + positionWS.xz * _Base_noise; // a732e52b, 54b73a70, 05c313f6
    float n = gradientNoise2(positionWS.xz + drift, _Noise_scale);      // dc3beb6f
    float sway = (n - 0.5) * _Noise_Amplitute;                           // 63d6f8d6 -> a10f3f76 (B default .5)
    float sway2 = sway * 2.0;                                            // cf20e7ea (B default 2)

    // 01bd9234 Split(worldPos): R+sway2, G, B+sway2 -> 2be22b54 Combine RGB
    float3 swayed = float3(positionWS.x + sway2, positionWS.y, positionWS.z + sway2);
    float height = objectSpaceY(positionOS); // ea03506f/10b0e56e Split.G - object-space height
    float3 p = lerp(positionWS, swayed, saturate(height)); // a554c0e9
    float3 posOS = normalize(mul(worldToObjectMatrix(), float4(p, 0.0)).xyz); // 37baef8e, m_Normalize=true

    VSOut o;
    o.positionCS = mul(getViewProj(), float4(posOS, 1.0));
    o.height = height;
    return o;
}

[shader("fragment")]
float4 fsMain(float height : TEXCOORD0) : SV_Target
{
    // b22fc214 Lerp: A=_base_color, B=_top_color, T=object-space Y (10b0e56e.Split.G)
    float3 baseColor = lerp(_base_color.rgb, _top_color.rgb, saturate(height));
    return float4(baseColor, 1.0);
}

// Note: c95fb042.Split(3f4f3c56 Position Object).G -> 6a85a986 Multiply by _Smoothness ->
// SurfaceDescription.Smoothness. Emit as a second target / material channel in a real port.
