// Heat Distortion.shadergraph - direct Slang reference translation (inference only, no Unity source copied)
// Graph: 104 serialized objects / 28 nodes / 26 edges. Node instances: BlockNode 6, PropertyNode 4,
// MultiplyNode 3, AddNode 2, and one each of Split, Transform, TransformationMatrix, Vector4,
// Redirect, SceneColor, ScreenPosition, Lerp, Simple Noise, SampleTexture2D, Object, Position,
// SubGraphNode ("texture movement"). URP UNLIT target (only BaseColor/Alpha blocks wired).
// Inferred purpose: billboard vertex placement (group title "Bill board") and screen-space
// refraction: scene-color UV is lerped toward UV + masked noise*_Noise_Scale motion by _noise_stregth.
// Confidence: fragment chain HIGH; the vertex billboard matrix is LOW (m_matrix=-1, m_MatrixType=3).

cbuffer HeatParams
{
    float  _noise_stregth;    // slider 0..1, default 0
    Texture2D _Noise_mask;    // default texture guid 0000...f000 (Unity built-in white)
    float  _Noise_Scale;      // default 50 - passed to texture-movement subgraph Scale (xy)
    float2 _Noise_speed;      // default (0,0) - passed to subgraph Speed
    float  _TimeY;            // external Unity _Time.y
};

float3 objectPosition();                // ObjectNode slot 0 (group "Bill board")
float3 objectScale();                   // ObjectNode slot 1
float4x4 getWorldToObjectMatrix();      // TransformNode from=2(World) to=0(Object), normalize=true
float4x4 getBillboardMatrix();          // TransformationMatrixNode m_MatrixType=3, m_matrix=-1 (ambiguous)
float4x4 getViewProj();
float2 textureMovement(float scale, float2 speed); // shared subgraph: (UV0=0)*scale + Time*speed = Time*speed
float valueNoise(float2 uv);            // Simple NoiseNode m_HashType=0, Scale pin unconnected (=1)
float3 sampleSceneColor(float2 uv);     // SceneColorNode (camera opaque texture)

struct VSOut { float4 positionCS : SV_POSITION; };

[shader("vertex")]
VSOut vsMain(float3 positionOS : POSITION)
{
    float3 scaled = positionOS * objectScale();             // 8943b1b1 Position(Object) * Object.Scale
    float4 homogeneous = float4(scaled, 0.0);               // 0e574d6c Split -> 559c64fc Vector4(X,Y,Z,W=0)
    float3 billboardOffset = mul(getBillboardMatrix(), homogeneous).xyz; // adf5323e -> 9d6577b6
    float3 worldPos = objectPosition() + billboardOffset;    // 33d9e4da Redirect -> 3fa0fc29 Add
    float3 posOS = normalize(mul(getWorldToObjectMatrix(), float4(worldPos, 0.0)).xyz); // 1b754f00
    VSOut o;
    o.positionCS = mul(getViewProj(), float4(posOS, 1.0));
    return o;
}

[shader("fragment")]
float4 fsMain(float4 screenPos : SCREEN_POSITION) : SV_Target
{
    float2 movedUV = textureMovement(_Noise_Scale, _Noise_speed); // 42663009 "texture movement" subgraph
    float mask = 1.0; // c8ae43d7 SampleTexture2D(_Noise_mask, uv=(0,0)); A pin used; default white tex
    float n = valueNoise(movedUV) * mask;                          // 7c959616 -> 60bbd4ce
    float2 distortedUV = screenPos.xy + n;                         // 04b53701 Add
    float2 uv = lerp(screenPos.xy, distortedUV, saturate(_noise_stregth)); // 9996650e
    return float4(sampleSceneColor(uv), 1.0);                      // f74f5523 -> BaseColor; Alpha block = 1
}
