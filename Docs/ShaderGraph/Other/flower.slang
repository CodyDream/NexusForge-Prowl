// Flower shader.shadergraph - direct Slang reference translation (inference only, no Unity source copied)
// Graph: 137 serialized objects / 36 nodes / 30 edges. Node instances: BlockNode 9, PropertyNode 5,
// PositionNode 4, MultiplyNode 4, AddNode 3, SplitNode 2, and one each of SampleTexture2D,
// TilingAndOffset, GradientNoise, Subtract, Combine, Lerp, Transform, Object, Time.
// Inferred purpose: wind-like vertex sway from Perlin noise driven by world position, object
// translation and Time*_Noise_speed; base color = palette texture * _Blend_color. The lerp that
// drives vertex position blends world position toward the swayed position by object-space Y.
// Confidence: topology HIGH; exact noise, Transform(normalize), and palette UV (pin unwired) MEDIUM/LOW.

cbuffer FlowerParams
{
    float  _Noise_Scale;      // default 2 - GradientNoise Scale pin
    float  _Noise_amplitute;  // default 0.5 (typo preserved)
    float2 _Noise_speed;      // default (2,0)
    Texture2D _Colour_pallate;// palette LUT (default texture guid in graph)
    float4 _Blend_color;      // default (1,1,1,0)
    float  _Smoothness;       // declared but never connected in this graph
    float  _TimeY;            // external Unity _Time.y
};

float3 objectPosition();                // ObjectNode slot 0 (world translation)
float4x4 getViewProj();                 // external camera state
float gradientNoise2(float2 uv, float scale); // GradientNoiseNode (synonym "perlin noise")
float4x4 worldToObjectMatrix();         // TransformNode from=2(World) to=0(Object)
float4 samplePalette(float2 uv);        // SampleTexture2DNode

struct VSOut { float4 positionCS : SV_POSITION; };

[shader("vertex")]
VSOut vsMain(float3 positionWS : POSITION) // d6bf2a94/842fff86/ecdefdd6 Position m_Space=2 (World)
{
    float2 drift = _TimeY * _Noise_speed + objectPosition().xz; // e4fadfa4 -> 5d6aa48c
    float2 noiseUV = positionWS.xz + drift;   // 600989ca TilingAndOffset: Tiling=(1,1), Offset=drift.xy
    float n = gradientNoise2(noiseUV, _Noise_Scale); // a9e23f2c
    float sway = (n - 0.5) * _Noise_amplitute; // bf4a1862 Subtract (B.x default 0.5) -> baea78d6 Multiply
    float sway2 = sway * 2.0;                  // cea532b8 Multiply (B default (2,2,2,2) in matrix slot)

    // 122a65ef Add(posW.x, sway2); 5192ca3a Add(posW.z, sway2); 8a549c88 Combine(R,G,B) with G=posW.y
    float3 swayed = float3(positionWS.x + sway2, positionWS.y, positionWS.z + sway2);
    // 913e6298 Split(d708c7f9 Position Object).G = object-space height used as lerp T
    float t = saturate(positionWS.y - objectPosition().y); // approximation: object-space Y via world minus translation
    float3 p = lerp(positionWS, swayed, t);                 // 7c4b8da2 Lerp A=posW, B=swayed, T=objectY
    float3 posOS = normalize(mul(worldToObjectMatrix(), float4(p, 0.0)).xyz); // f2cf77b6, m_Normalize=true
    VSOut o;
    o.positionCS = mul(getViewProj(), float4(posOS, 1.0));
    return o;
}

[shader("fragment")]
float4 fsMain() : SV_Target
{
    // SampleTexture2DNode 45a49aa9: UV pin has NO incoming edge -> literal (0,0).
    // This samples one texel of the palette; verify against the imported texture before relying on it.
    float4 pal = samplePalette(float2(0.0, 0.0));
    float3 baseColor = pal.rgb * _Blend_color.rgb; // f50cc44a Multiply; B matrix-slot default e03=29.5 ignored (wired)
    return float4(baseColor, 1.0);
}
