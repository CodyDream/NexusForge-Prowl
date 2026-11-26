// texture_movement.slang — direct Slang reference translation of
// Shaders/SubGraphs/texture movement.shadersubgraph.
//
// Purpose: scrolling UV generator for the four refraction normal layers and
// for the foam noise lookup in water.shadergraph.
// Object counts: GraphData 1, CategoryData 1, SubGraphOutputNode 1,
//   PropertyNode 2 (Speed, Scale), TimeNode 1, MultiplyNode 1,
//   TilingAndOffsetNode 1; slots: Vector1 5, Vector2 6, DynamicValue 3, UV 1.
// Confidence: HIGH for the arithmetic. IMPORTANT caveat: the TilingAndOffset
// UV input is UNCONNECTED and its serialized default is (0,0), so UV*Scale
// vanishes; only the time-driven Offset survives. See ANALYSIS.md #1.
// External state required: the engine's _Time uniform.

struct TextureMovementInputs
{
    float2 scale; // property "Scale", default (1,1)
    float2 speed; // property "Speed", default (0,0)
    float  time;  // TimeNode slot 0
};

float2 scrollingUV(float2 scale, float2 speed, float time)
{
    float2 offset = time * speed;               // 36a5f094 -> 6fc870c0
    return float2(0.0, 0.0) * scale + offset;   // 4bc1fbd8: UV default (0,0), Tiling=Scale
}
