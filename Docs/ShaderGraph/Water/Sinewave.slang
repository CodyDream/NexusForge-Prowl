// Sinewave.slang — direct Slang reference translation of
// Shaders/SubGraphs/Sinewave.shadersubgraph.
//
// Purpose: one-dimensional multi-harmonic wave profile used by water.shadergraph
// for the two horizontal displacement axes.
// Object counts: GraphData 1, CategoryData 1, SubGraphOutputNode 1, PropertyNode 2,
//   ConstantNode 1, MultiplyNode 10, DivideNode 1, SineNode 5, AddNode 4;
//   slots: Vector1 3, DynamicVector 25, DynamicValue 30, Vector4 1.
// Confidence: HIGH for the harmonic weights (from literal m_Value on the multiply
// inputs); MEDIUM for ConstantNode.m_constant=0 meaning PI (×2 => 2π).
// Edges: Property(position) -> Multiply(×2PI) -> Divide(/wavelength) -> 5 branches.

// Inputs mirror the subgraph's two Vector1ShaderProperty nodes.
struct SinewaveInputs
{
    float position;   // property "position", default 0 (world X or Z + time*speed)
    float wavelength; // property "wavelength", default 1 (water.shadergraph "length")
};

// Harmonic 1: sin(k)                       (39239945 -> b63b23b3)
// Harmonic 11: 0.5*sin(11k)                (b03327f4 -> ba42a9bf -> a4673d98)
// Harmonic 2: 0.7*sin(2k)                  (aefa50dd -> 9cb2186e -> 626ad575)
// Harmonic 5: 0.2*sin(5k)                  (74869f8b -> c0865751 -> 98a43603)
// Harmonic 8: 0.5*sin(8k)                  (aa1a3ce0 -> 53b7c87b -> 53624021)
// Branches summed pairwise by 4f54a782, c636953a, b63b23b3, then 9d887b1a.
float sinewaveProfile(float position, float wavelength)
{
    float k = (6.28318530718 * position) / max(1e-5, wavelength);
    float upper = sin(k) + 0.5 * sin(11.0 * k);
    float lower = 0.7 * sin(2.0 * k) + 0.2 * sin(5.0 * k) + 0.5 * sin(8.0 * k);
    return upper + lower; // subgraph output slot 1 (Out_Vector4, used as scalar height)
}
