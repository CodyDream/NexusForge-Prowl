# Node library: ShaderGraph object types (Other scope)

Scope: the five graphs `Cloud`, `Flower shader`, `Grass`, `Heat Distortion`, `TriPlane`.
Columns C/F/G/H/T = instance count in that graph. Counts include ALL serialized objects
(`GraphData`, `MaterialSlot`s, targets), not only editable nodes.

ID convention: each object declares `m_ObjectId`; references are `{ "m_Id": ... }`.
`m_SlotType: 1 = output, 0 = input`. Space enum inferred as 0=Object, 2=World from
`m_Conversion: {from:2,to:0}` + World/Position usage; enum labels are NOT in the JSON.

| m_Type | total | C | F | G | H | T | inferred meaning | conf |
|---|---:|---:|---:|---:|---:|---:|---|---|
| `UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget` | 4 | 1 | 1 | 1 | 0 | 1 | URP Lit shader target (surface blocks: BaseColor/Normal/Smoothness/etc.) | H |
| `UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget` | 5 | 1 | 1 | 1 | 1 | 1 | URP target settings object (one per graph) | H |
| `UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget` | 1 | 0 | 0 | 0 | 1 | 0 | URP Unlit target (BaseColor/Alpha blocks only) | H |
| `UnityEditor.ShaderGraph.AddNode` | 11 | 2 | 3 | 3 | 2 | 1 | A+B, component-wise; dynamic type from connected slots | H |
| `UnityEditor.ShaderGraph.BlockNode` | 48 | 12 | 9 | 12 | 6 | 9 | final output stage (VertexDescription.* / SurfaceDescription.*) | H |
| `UnityEditor.ShaderGraph.BooleanMaterialSlot` | 1 | 0 | 0 | 0 | 0 | 1 | Boolean typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.CategoryData` | 5 | 1 | 1 | 1 | 1 | 1 | editor-only property category | H |
| `UnityEditor.ShaderGraph.ColorRGBMaterialSlot` | 11 | 3 | 2 | 3 | 1 | 2 | ColorRGB typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.CombineNode` | 2 | 0 | 1 | 1 | 0 | 0 | append R/G/B/A to RGBA/RGB/RG | H |
| `UnityEditor.ShaderGraph.DotProductNode` | 3 | 1 | 0 | 0 | 0 | 2 | dot(A,B) -> scalar | H |
| `UnityEditor.ShaderGraph.DynamicValueMaterialSlot` | 51 | 12 | 12 | 15 | 9 | 3 | DynamicValue typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.DynamicVectorMaterialSlot` | 89 | 15 | 18 | 23 | 13 | 20 | DynamicVector typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.FresnelNode` | 1 | 1 | 0 | 0 | 0 | 0 | pow(1 - saturate(dot(normal, viewDir)), Power) | M |
| `UnityEditor.ShaderGraph.GradientNoiseNode` | 3 | 1 | 1 | 1 | 0 | 0 | Perlin-style 2D gradient noise of UV * Scale; synonyms say "perlin noise" | H |
| `UnityEditor.ShaderGraph.GraphData` | 5 | 1 | 1 | 1 | 1 | 1 | root: m_Nodes / m_Properties / m_Edges / contexts / targets | H |
| `UnityEditor.ShaderGraph.GroupData` | 3 | 0 | 1 | 1 | 1 | 0 | editor-only visual group with m_Title | H |
| `UnityEditor.ShaderGraph.Internal.BooleanShaderProperty` | 1 | 0 | 0 | 0 | 0 | 1 | exposed bool property (keyword-like toggle in graph) | M |
| `UnityEditor.ShaderGraph.Internal.ColorShaderProperty` | 7 | 2 | 1 | 2 | 0 | 2 | exposed color; m_ColorMode 0=LDR, 1=HDR (inferred) | M |
| `UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty` | 2 | 0 | 1 | 0 | 1 | 0 | exposed Texture2D with default texture guid | H |
| `UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty` | 15 | 4 | 3 | 4 | 2 | 2 | exposed float; m_FloatType 1=slider with m_RangeValues | H |
| `UnityEditor.ShaderGraph.Internal.Vector2ShaderProperty` | 3 | 0 | 1 | 1 | 1 | 0 | exposed float2 | H |
| `UnityEditor.ShaderGraph.Internal.Vector3ShaderProperty` | 1 | 0 | 0 | 0 | 0 | 1 | exposed float3 | H |
| `UnityEditor.ShaderGraph.LerpNode` | 6 | 1 | 1 | 2 | 1 | 1 | A*(1-T)+B*T | H |
| `UnityEditor.ShaderGraph.MainLightDirectionNode` | 2 | 1 | 0 | 0 | 0 | 1 | Main directional light direction (node lives in URP namespace; host supplies the light); synonyms say "sun" | H |
| `UnityEditor.ShaderGraph.Matrix4MaterialSlot` | 1 | 0 | 0 | 0 | 1 | 0 | Matrix4 typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.MultiplyNode` | 17 | 4 | 4 | 5 | 3 | 1 | A*B; dynamic value slots allow scalar/vector/matrix (defaults in m_Value) | H |
| `UnityEditor.ShaderGraph.NegateNode` | 1 | 0 | 0 | 0 | 0 | 1 | -A | H |
| `UnityEditor.ShaderGraph.NoiseNode` | 1 | 0 | 0 | 0 | 1 | 0 | Simple/value noise of UV * Scale; m_HashType=0 | H |
| `UnityEditor.ShaderGraph.NormalMaterialSlot` | 10 | 3 | 2 | 2 | 1 | 2 | Normal typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.NormalVectorNode` | 3 | 1 | 0 | 0 | 0 | 2 | surface normal in m_Space (0 Object, 2 World inferred from defaults/use) | M |
| `UnityEditor.ShaderGraph.ObjectNode` | 2 | 0 | 1 | 0 | 1 | 0 | object transform values: Position (world translation), Scale, Bounds Min/Max, Size | M |
| `UnityEditor.ShaderGraph.PositionMaterialSlot` | 5 | 1 | 1 | 1 | 1 | 1 | Position typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.PositionNode` | 13 | 2 | 4 | 6 | 1 | 0 | vertex position in m_Space (0 Object, 2 World inferred from transforms) | M |
| `UnityEditor.ShaderGraph.PropertyNode` | 28 | 6 | 5 | 7 | 4 | 6 | reads exposed property via m_Property {m_Id} | H |
| `UnityEditor.ShaderGraph.RedirectNodeData` | 1 | 0 | 0 | 0 | 1 | 0 | passthrough reroute node | H |
| `UnityEditor.ShaderGraph.SampleTexture2DNode` | 2 | 0 | 1 | 0 | 1 | 0 | Texture2D sample; RGBA plus R/G/B/A outputs; sampler from property | H |
| `UnityEditor.ShaderGraph.SamplerStateMaterialSlot` | 2 | 0 | 1 | 0 | 1 | 0 | SamplerState typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.SaturateNode` | 3 | 1 | 0 | 0 | 0 | 2 | clamp01 | H |
| `UnityEditor.ShaderGraph.SceneColorNode` | 1 | 0 | 0 | 0 | 1 | 0 | camera opaque/scene color texture lookup by screen UV; synonyms "screen buffer" | H |
| `UnityEditor.ShaderGraph.ScreenPositionMaterialSlot` | 1 | 0 | 0 | 0 | 1 | 0 | ScreenPosition typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.ScreenPositionNode` | 1 | 0 | 0 | 0 | 1 | 0 | screen-space position; m_ScreenSpaceType=0 => Default normalized UV | M |
| `UnityEditor.ShaderGraph.SplitNode` | 7 | 1 | 2 | 3 | 1 | 0 | separate vector to R/G/B/A outputs | H |
| `UnityEditor.ShaderGraph.StepNode` | 1 | 0 | 0 | 0 | 0 | 1 | step(Edge, In) = In >= Edge ? 1 : 0 (NOT Edge>=In) | H |
| `UnityEditor.ShaderGraph.SubGraphNode` | 1 | 0 | 0 | 0 | 1 | 0 | shared subgraph instance; m_SerializedSubGraph GUID + m_PropertyIds overrides | H |
| `UnityEditor.ShaderGraph.SubtractNode` | 2 | 0 | 1 | 1 | 0 | 0 | A-B; unconnected B default (0.5,1,1,1) => 1-noise for R | H |
| `UnityEditor.ShaderGraph.TangentMaterialSlot` | 5 | 1 | 1 | 1 | 1 | 1 | Tangent typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Texture2DInputMaterialSlot` | 2 | 0 | 1 | 0 | 1 | 0 | Texture2DInput typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Texture2DMaterialSlot` | 2 | 0 | 1 | 0 | 1 | 0 | Texture2D typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.TilingAndOffsetNode` | 3 | 1 | 1 | 1 | 0 | 0 | UV*Tiling + Offset; synonyms "pan,scale" | H |
| `UnityEditor.ShaderGraph.TimeNode` | 3 | 1 | 1 | 1 | 0 | 0 | _Time: slot0 Time, 1 Sine Time, 2 Cosine Time, 3 Delta Time, 4 Smooth Delta | H |
| `UnityEditor.ShaderGraph.TransformNode` | 4 | 1 | 1 | 1 | 1 | 0 | coordinate conversion from/to m_Conversion; m_Normalize=true | H |
| `UnityEditor.ShaderGraph.TransformationMatrixNode` | 1 | 0 | 0 | 0 | 1 | 0 | matrix output; m_MatrixType=3, m_matrix=-1; exact matrix LOW confidence | L |
| `UnityEditor.ShaderGraph.UVMaterialSlot` | 9 | 2 | 3 | 2 | 2 | 0 | UV typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Vector1MaterialSlot` | 108 | 23 | 28 | 32 | 18 | 7 | Vector1 typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Vector2MaterialSlot` | 17 | 3 | 5 | 5 | 4 | 0 | Vector2 typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Vector3MaterialSlot` | 40 | 6 | 12 | 9 | 9 | 4 | Vector3 typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Vector4MaterialSlot` | 13 | 2 | 3 | 3 | 3 | 2 | Vector4 typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |
| `UnityEditor.ShaderGraph.Vector4Node` | 1 | 0 | 0 | 0 | 1 | 0 | constant float4 literal | H |
| `UnityEditor.ShaderGraph.ViewDirectionMaterialSlot` | 1 | 1 | 0 | 0 | 0 | 0 | ViewDirection typed pin: m_Id/m_SlotType/m_Value; m_Id slot id, m_SlotType 1=output/0=input, m_Value = unconnected default | H |

## Confidence key

- H = operation unambiguous from node type, synonyms, slot names and edge topology.
- M = operation clear, but exact constants/enum mapping or numeric formula must be checked.
- L = name only; no local evidence resolves the setting.

## Notes on external state observed here

- `_Time` is supplied by `TimeNode` in Cloud/Flower/Grass; Heat uses it indirectly through the
  `texture movement` subgraph (Time * Speed).
- No `VertexColorNode`, no depth-fade subgraph and no camera-depth texture in these five graphs.
- URP main-light direction is read in Cloud and TriPlane; URP scene/opaque color in Heat Distortion.
- "Wind" is not a dedicated uniform set: it is `_Time` scaled by noise-speed properties.
