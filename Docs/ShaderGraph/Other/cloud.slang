// Cloud.shadergraph - direct Slang reference translation (inference only, no Unity source copied)
// Graph: 119 serialized objects / 37 nodes / 26 edges. Node instances: BlockNode 12, PropertyNode 6,
// MultiplyNode 4, PositionNode 2, AddNode 2, and one each of Fresnel, TilingAndOffset, Lerp,
// DotProduct, GradientNoise, Transform, Split, NormalVector, MainLightDirection, Time, Saturate.
// Inferred purpose: animated Perlin-noise vertex inflation in world space; base color blends
// _Color with a subsurface color by main-light wrap term times Fresnel; alpha = _Color.a.
// Confidence: topology HIGH; noise/fresnel formulas and Transform(normalize=true) MEDIUM.

cbuffer CloudParams
{
    float4 _Color;                     // default (0,0,0,0) in graph
    float  _Noise_Scale;               // default 10 - feeds UV multiplier, NOT noise Scale pin
    float  _Noise_Amplitute;           // default 0.1 (typo preserved)
    float  _Noise_speed;               // default 1
    float4 _Subsurface_scattering_color;
    float  _Fresenal_power;            // slider 0..3, default 0 (typo preserved)
    float  _TimeY;                     // external: Unity _Time.y
};

float4 objectToWorldPos(float3 p);
float3 worldToObjectPos(float3 p);     // Transform from=2(to World) to=0(Object); normalize flag uncertain
float4x4 getViewProj();                // external camera state
float3 worldNormal();                  // NormalVectorNode m_Space=2
float3 mainLightDirection();           // URP MainLightDirectionNode
float3 viewDirectionWS(float3 worldPos);

// Placeholder: match Unity GradientNoiseNode (synonyms: "perlin noise") numerically before shipping.
float gradientNoise2(float2 uv, float scale) { return fract(sin(dot(uv * scale, float2(12.9898, 78.233))) * 43758.5453); }

float cloudDisplacement(float3 positionOS)
{
    // 02e4d09c(Position Object) * fc6e9386(_Noise_Scale) + 9fa9c4f6(Time)*97173621(_Noise_speed)
    float3 drift = positionOS * _Noise_Scale + _TimeY * _Noise_speed;
    // 0af40223 TilingAndOffset: UV pin unconnected => (0,0); Tiling=(1,1); Offset=drift.xy (float3->float2)
    float2 noiseUV = drift.xy;
    // 4fa12012 GradientNoise: Scale pin unconnected => 10; UV from TilingAndOffset
    return gradientNoise2(noiseUV, 10.0) * _Noise_Amplitute; // 5a4d33ab Multiply
}

struct VSOut { float4 positionCS : SV_POSITION; float3 worldPos : TEXCOORD0; };

[shader("vertex")]
VSOut vsMain(float3 positionOS : POSITION)
{
    float3 worldPos = objectToWorldPos(positionOS).xyz;          // 2e243342, m_Space=2
    float3 displaced = worldPos + cloudDisplacement(positionOS); // c9d46319 Add (scalar broadcast)
    VSOut o;
    o.positionCS = mul(getViewProj(), float4(worldToObjectPos(displaced), 1.0)); // 58a4c0f7 -> block
    o.worldPos = displaced;
    return o;
}

[shader("fragment")]
float4 fsMain(VSOut i) : SV_Target
{
    float3 n = normalize(worldNormal());
    float ndv = saturate(dot(n, viewDirectionWS(i.worldPos)));
    float fresnel = pow(1.0 - ndv, max(_Fresenal_power, 0.0)); // 01c720c9
    float wrap = saturate(dot(mainLightDirection(), n));       // 9be05dc6 -> 4b9a49e9 -> be3d7dd7
    float t = fresnel * wrap;                                   // f5bfde03
    float3 rgb = lerp(_Color.rgb, _Subsurface_scattering_color.rgb, t); // 11ad037f -> BaseColor
    return float4(rgb, _Color.a);                               // 608b64f1.A -> Alpha
}
