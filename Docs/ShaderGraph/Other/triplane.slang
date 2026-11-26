// TriPlane.shadergraph - direct Slang reference translation (inference only, no Unity source copied)
// Graph: 80 serialized objects / 27 nodes / 18 edges. Node instances: BlockNode 9, PropertyNode 6,
// and one each of NormalVectorNode(×2), DotProduct(×2), Saturate(×2), MainLightDirection,
// Negate, Add, Multiply, Step, Lerp.
// Inferred purpose: stylized two-tone shading. A triplanar-facing term dot(_Tri_Planer_Direction,
// worldNormal) is multiplied by a sun-facing term dot(-mainLightDirection, worldNormal); adding
// _Is_sun_move (bool->float) forces the result high; step(_step_Edge, value) mixes BaseColor and
// SecondaryColor. There are NO texture samples or triplanar UV blends despite the graph's name.
// Confidence: topology HIGH; Step argument order and bool->float semantics MEDIUM.

cbuffer TriPlaneParams
{
    float3 _BaseColor;            // default (0.481,0.481,0.481)
    float3 _SecondaryColor;       // default (0,0.868,0.132)
    float3 _Tri_Planer_Direction; // default (0,1,0)
    float  _step_Edge;            // default 0.3
    bool   _Is_sun_move;          // default false
    float  _Smoothness;           // default 0
};

float3 worldNormal();            // NormalVectorNode m_Space=2 (731c561c and 3c9c9179)
float3 mainLightDirection();     // URP MainLightDirectionNode, synonym "sun"

[shader("fragment")]
float4 fsMain() : SV_Target
{
    float3 n = normalize(worldNormal());
    float facing = saturate(dot(_Tri_Planer_Direction, n));              // 89a4cb8a -> 607b2245 -> 82ec9549
    float sunFacing = saturate(dot(-mainLightDirection(), n));           // 472f9c9e -> 24e6c641 -> 44c157c4 -> 31d0cb50
    float mixer = facing * sunFacing;                                    // c1f6a389 Multiply
    mixer += _Is_sun_move ? 1.0 : 0.0;                                   // f31ec76f -> 5a75911a Add (bool cast uncertain)
    float t = step(_step_Edge, mixer);                                   // 8f61d20a Step: Out = In >= Edge ? 1 : 0
    float3 baseColor = lerp(_BaseColor, _SecondaryColor, t);              // 294b711f -> BaseColor
    // 9113dfc2 -> SurfaceDescription.Smoothness = _Smoothness (not a color output)
    return float4(baseColor, 1.0);
}
