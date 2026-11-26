# Shader Graph node library — Sky folder contribution

Scope: the three sky-related JSON graphs only.

| Graph | File | JSON objects | Nodes | Edges | Properties |
|---|---|---:|---:|---:|---:|
| `main` | `Assets/Low Poly Stylized Nature/Shaders/Skybox.shadergraph` | 374 | 99 | 98 | 17 |
| `skyuv` | `Assets/Low Poly Stylized Nature/Shaders/SubGraphs/skybox uv.shadersubgraph` | 46 | 12 | 13 | 0 |
| `stars` | `Assets/Low Poly Stylized Nature/Shaders/SubGraphs/stars.shadersubgraph` | 45 | 13 | 13 | 3 |

Counts include the `GraphData` object itself; "Nodes" counts entries in `GraphData.m_Nodes`.

## `m_Type | instance count | inferred meaning | confidence | which graph`

Counts are `main / skyuv / stars` in that order. `synonyms` were read from the JSON where present; layout (`m_DrawState.m_Position.x`) was used to order chains.

| m_Type (short) | count (M/SU/ST) | Inferred meaning | Conf. | Graph(s) |
|---|---:|---|---|---|
| `DynamicVectorMaterialSlot` | 115/17/6 | Untyped 1–4 component slot; the actual width is decided by the connected graph. | High | all |
| `Vector1MaterialSlot` | 57/10/8 | Scalar input/output slot (`m_Value` is a bare float). | High | all |
| `DynamicValueMaterialSlot` | 27/0/6 | Untyped scalar-or-matrix slot; matrix defaults are identity, so a connected scalar collapses to scalar multiply. | High | main, stars |
| `PropertyNode` | 17/0/3 | Reads a graph property; the referenced property id is in `m_Property.m_Id`. | High | main, stars |
| `Vector2MaterialSlot` | 17/1/0 | `float2` slot. Used for Tiling/Offset, Remap ranges, `Star sine`. | High | main, skyuv |
| `BlockNode` | 11/0/0 | Shader output block (3 vertex, 8 fragment); only `SurfaceDescription.BaseColor` is connected. | High | main |
| `Vector1ShaderProperty` | 10/0/3 | Scalar material property (`m_DefaultReferenceName` gives the real uniform name). | High | main, stars |
| `MultiplyNode` | 9/0/2 | Component-wise multiply. For matrix-typed slots it still resolves to scalar multiply here. | High | main, stars |
| `Vector4MaterialSlot` | 9/2/3 | `float4` slot; colors are stored as RGBA with A frequently unused. | High | all |
| `SaturateNode` | 8/0/0 | `clamp(x,0,1)`; synonyms say `clamp`. | High | main |
| `AddNode` | 7/0/0 | Component-wise add; used for gradient, wind offsets, sun/moon additive composition. | High | main |
| `GroupData` | 6/0/0 | Editor-only group. Titles found: `Sky gradiant`, `New Group` (x2), `cloud`, `clouds edge`, `fog`. | High | main |
| `Vector3MaterialSlot` | 6/1/0 | `float3` slot (directions, positions). | High | main, skyuv |
| `UVMaterialSlot` | 6/0/2 | `float2` UV slot; explicitly tagged coordinate input of noise/tiling nodes. | High | main, stars |
| `LerpNode` | 6/0/0 | `lerp(A,B,T)`; synonyms `mix, blend, linear interpolate`. | High | main |
| `ColorShaderProperty` | 5/0/0 | Color property; all 5 are in main. | High | main |
| `NormalizeNode` | 5/1/0 | Normalizes a vector; used on view direction, light direction, moon direction. | High | main, skyuv |
| `SplitNode` | 4/1/0 | Component extract (`.R/.G/.B/.A`); synonyms `separate`. | High | main, skyuv |
| `StepNode` | 3/0/0 | `step(Edge, In)` → 1 where `In >= Edge`. Used for sun/moon disks and a cloud highlight threshold. | High | main |
| `TilingAndOffsetNode` | 3/0/0 | `UV * Tiling + Offset`; used for sky gradient band, cloud panning. | High | main |
| `NoiseNode` | 3/0/1 | Simple (value) noise; `m_HashType` 0 = standard, 1 = LPM-like. | Medium | main, stars |
| `RemapNode` | 3/0/0 | `(In - InMin) / (InMax - InMin)` then lerp to `OutMin..OutMax`. | High | main |
| `CategoryData` | 3/1/1 | Editor-only property categories. Main names: `Sky colors`, `""`, `Stars`. | High | all |
| `ViewDirectionNode` | 2/0/0 | Camera-to-surface direction; `m_Space:2` = world space. | High | main |
| `DotProductNode` | 2/0/0 | Dot of sun/moon direction with view direction → angular distance input. | High | main |
| `ArcsineNode` | 2/1/0 | `asin`, used on `dot()` to obtain an angular measure in radians. | High | main, skyuv |
| `TimeNode` | 2/0/0 | Time output; `Time` drives wind, `Sine Time` drives star twinkle. | High | main |
| `OneMinusNode` | 2/0/0 | `1 - x`; used for horizon falloff and cloud edge mask. | High | main |
| `SubGraphNode` | 2/1/1 | Instance of `skybox uv` (main + nested inside `stars`) and `stars` (main only). | High | all |
| `MainLightDirectionNode` | 2/0/0 | Main directional light direction; synonyms explicitly say `sun`. | High | main |
| `NormalMaterialSlot` | 2/0/0 | Normal-typed block slot (vertex normal, tangent-space normal). | High | main |
| `PositionMaterialSlot` | 2/0/0 | Position-typed block slot (`VertexDescription.Position`). | High | main |
| `ColorRGBMaterialSlot` | 2/0/0 | Color block slot (`Base Color`, `Emission`). | High | main |
| `GraphData` | 1/1/1 | Root object; holds `m_Nodes`, `m_Edges`, `m_Properties`, contexts and active target ids. | High | all |
| `BuiltInTarget` | 1/0/0 | Built-in render pipeline target, surface type 0 (opaque), render face 2 (both). | High | main |
| `PowerNode` | 1/0/1 | `pow(A,B)`; fog-height falloff in main, star sharpening in `stars`. | High | main, stars |
| `Vector3ShaderProperty` | 1/0/0 | `moon direction` property. | High | main |
| `PosterizeNode` | 1/0/0 | `floor(x * Steps) / Steps`; Steps default 6 → banded cloud mask. | High | main |
| `BuiltInLitSubTarget` | 1/0/0 | Lit subtarget (workflow mode 1 = specular); graph only writes Base Color. | Medium | main |
| `UniversalTarget` | 1/0/0 | URP target; alpha-clip enabled but alpha block is unused. | High | main |
| `TangentMaterialSlot` | 1/0/0 | Tangent block slot. | High | main |
| `FogNode` | 1/0/0 | Built-in fog color/density; only `Color` is connected (into a final horizon blend). | Medium | main |
| `Internal.Vector2ShaderProperty` | 1/0/0 | `Star sine` property (output range of sine remap). | High | main |
| `SmoothstepNode` | 1/0/0 | `smoothstep(Edge1, Edge2, In)`; cloud edge band. | High | main |
| `SubtractNode` | 1/0/0 | `A - B`; sun elevation (light.y − (−1)). | High | main |
| `UniversalUnlitSubTarget` | 1/0/0 | URP unlit subtarget (coexists with the URP target entry). | Medium | main |
| `DivideNode` | 0/3/1 | Component-wise divide. In `skybox uv` it turns `atan2`/`asin` into UV; in `stars` it scales gradient noise. | High | skyuv, stars |
| `SubGraphOutputNode` | 0/1/1 | Subgraph output (`New` in skyuv, `Out_Vector4` in stars). | High | skyuv, stars |
| `ConstantNode` | 0/2/0 | Math constant; `m_constant` is 0 and 1 → likely PI and TAU (enum index). | **Low** | skyuv |
| `Arctangent2Node` | 0/1/0 | `atan2(A,B)` for equirectangular longitude. | High | skyuv |
| `PositionNode` | 0/1/0 | `m_Space:2`, `m_PositionSource:0` → world-space position (used as sky direction). | High | skyuv |
| `GradientNode` | 0/0/1 | 6-key color ramp for star tint; synonyms `ramp`. | High | stars |
| `GradientInputMaterialSlot` | 0/0/1 | Gradient-valued slot feeding `Sample Gradient`. | High | stars |
| `GradientMaterialSlot` | 0/0/1 | Gradient output slot of `GradientNode`. | High | stars |
| `GradientNoiseNode` | 0/0/1 | Perlin-style gradient noise; synonyms `perlin noise`. | Medium | stars |
| `SampleGradient` | 0/0/1 | Samples the star ramp at a Perlin-noise driven time. | High | stars |

## Cross-check notes for sibling detectives

- No `NegateNode`, `Vector1/2/3/4Node`, or `OneMinusNode` beyond the two listed above; negation is done by multiplying by a negative constant (`0fada497`, B = −2). If another folder reports a `NegateNode`, that is a different graph, not this one.
- `StepNode` is *not* a smooth disk: `step(Edge, In)` returns a hard 0/1; the disk edges here are `asin(dot(...))` in radians.
- `FogNode` here is a color source, not a post-process; its `Position` input is unconnected and its `Color` feeds only the final horizon lerp.
- `PropertyNode` names are typos in the asset (`clod edge`, `cloud highet`, `Sky night color`); do not "correct" them when binding uniforms — `m_DefaultReferenceName` is authoritative.
