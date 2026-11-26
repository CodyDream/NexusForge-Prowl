// depthfade.slang — direct Slang reference translation of
// Shaders/SubGraphs/depthfade.shadersubgraph.
//
// Purpose: soft depth-based mask, used in water.shadergraph for water colour,
// normal strength, and foam cut-out.
// Object counts: GraphData 1, CategoryData 1, SubGraphOutputNode 1,
//   PropertyNode 2, ScreenPositionNode 1, SceneDepthNode 1, SplitNode 1,
//   SubtractNode 1, DivideNode 1, SaturateNode 1, MultiplyNode 1;
//   slots: Vector1 7, DynamicVector 9, DynamicValue 3, ScreenPosition 2, Vector4 2.
// Confidence: HIGH. Edge trace:
//   ScreenPosition -> Split.A -> Subtract(sceneDepth - screenA)
//     -> Divide(/Distance) -> Saturate -> Multiply(*Strength) -> Out.
// External state required: scene depth texture and the current fragment's
// screen-space depth (screenPos.a). Not standalone.

struct DepthFadeInputs
{
    float strength;       // property "Strength", default 1
    float distance;       // property "Distance", default 2
    float sceneDepthRaw;  // SceneDepthNode at the screen UV
    float screenDepthA;   // ScreenPositionNode.a (Split A output)
};

float depthFade(float strength, float distance, float sceneEyeDepth, float surfaceEyeDepth)
{
    float delta = sceneEyeDepth - surfaceEyeDepth;             // f5cb8819 (A-B, A default 1 but connected)
    return saturate(delta / max(1e-5, distance)) * strength;   // 9133af43 -> d2e26331 -> 21bc79c5
}
