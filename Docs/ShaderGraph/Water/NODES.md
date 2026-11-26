# Node library: ShaderGraph object types (Water scope)

Scope: `water.shadergraph` plus the four subgraphs it instantiates: `Sinewave`,
`depthfade`, `texture movement`, `Normal map` (all under `Shaders/SubGraphs/`).
`WaterClipmap` has **no** `.shadergraph` — it is a hand-written HLSL `.shader`,
so it contributes zero ShaderGraph objects. Its function inventory lives in
`ANALYSIS.md` instead.

Columns: W = water, S = Sinewave, D = depthfade, T = texture movement,
N = Normal map. Counts include **all** serialized objects (`GraphData`, targets,
material slots), matching the sibling `Other/NODES.md` convention.

ID convention: each object declares `m_ObjectId`; all cross-links are
`{ "m_Id": ... }` references (not GUIDs). `m_SlotType`: 0 = input, 1 = output.
`m_Slots` on a node points at slot objects; each slot has its own `m_Id` used by
`GraphData.m_Edges[].m_OutputSlot/m_InputSlot.m_SlotId`. Space enum inferred as
0 = Object, 2 = World (same reading as `Other/NODES.md`).

| m_Type | total | W | S | D | T | N | inferred meaning | conf |
|---|---:|---:|---:|---:|---:|---:|---|---|
| `UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget` | 1 | 1 | 0 | 0 | 0 | 0 | URP Lit target; explains the SurfaceDescription blocks (BaseColor/NormalTS/Smoothness/Metallic/Specular/Emission/Occlusion/Alpha/AlphaClipThreshold) | H |
| `UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget` | 1 | 1 | 0 | 0 | 0 | 0 | URP target settings: SurfaceType=1 (transparent), ZTest=4, ZWrite=0, AlphaMode=0, AlphaClip=false | M (enum values inferred) |
| `UnityEditor.ShaderGraph.AddNode` | 9 | 5 | 4 | 0 | 0 | 0 | A+B component-wise; dynamic operands, defaults visible in `m_Value` | H |
| `UnityEditor.ShaderGraph.BlockNode` | 12 | 12 | 0 | 0 | 0 | 0 | final stage outputs; `m_SerializedDescriptor` names it (VertexDescription.* / SurfaceDescription.*) | H |
| `UnityEditor.ShaderGraph.CategoryData` | 10 | 6 | 1 | 1 | 1 | 1 | editor-only property grouping; `m_Title` = Waves/Water color/Refraction/Normals/Foam | H |
| `UnityEditor.ShaderGraph.ClampNode` | 1 | 1 | 0 | 0 | 0 | 0 | clamp(In, Min, Max); Min/Max unconnected here (defaults 0/1) → clamp01 | M (bounds from defaults) |
| `UnityEditor.ShaderGraph.ColorRGBMaterialSlot` | 3 | 3 | 0 | 0 | 0 | 0 | float3 color pin used by the block nodes | H |
| `UnityEditor.ShaderGraph.CombineNode` | 2 | 2 | 0 | 0 | 0 | 0 | assemble RG/RGB/RGBA from scalar inputs | H |
| `UnityEditor.ShaderGraph.ConstantNode` | 1 | 0 | 1 | 0 | 0 | 0 | `synonyms: pi,tau,phi`; `m_constant = 0` read as PI (×2 later → 2π) | M |
| `UnityEditor.ShaderGraph.DivideNode` | 3 | 1 | 1 | 1 | 0 | 0 | A/B component-wise; unconnected B defaults (water: 2, Sinewave: wavelength, depthfade: Distance) | H |
| `UnityEditor.ShaderGraph.DotProductNode` | 1 | 1 | 0 | 0 | 0 | 0 | dot(A,B)→scalar; B default `(0,1,0,0)` ⇒ view-vector.y | H |
| `UnityEditor.ShaderGraph.DynamicValueMaterialSlot` | 63 | 27 | 30 | 3 | 3 | 0 | scalar/vector/matrix pin; `m_Value` holds an `e00..e33` matrix for unconnected defaults | H |
| `UnityEditor.ShaderGraph.DynamicVectorMaterialSlot` | 104 | 68 | 25 | 9 | 0 | 2 | dynamically typed vector pin | H |
| `UnityEditor.ShaderGraph.FogNode` | 1 | 1 | 0 | 0 | 0 | 0 | URP fog Color + Density outputs (engine-provided) | M |
| `UnityEditor.ShaderGraph.FresnelNode` | 1 | 1 | 0 | 0 | 0 | 0 | pow(1 − saturate(dot(N,V)), Power); both N and View Dir inputs unconnected here | M (see ANALYSIS uncertainty #2) |
| `UnityEditor.ShaderGraph.GradientNoiseNode` | 1 | 1 | 0 | 0 | 0 | 0 | `synonyms: perlin noise`; noise(UV × Scale), Scale=10 | H |
| `UnityEditor.ShaderGraph.GraphData` | 5 | 1 | 1 | 1 | 1 | 1 | root object: m_Properties/m_Nodes/m_Edges/m_VertexContext/m_FragmentContext/m_ActiveTargets | H |
| `UnityEditor.ShaderGraph.GroupData` | 5 | 5 | 0 | 0 | 0 | 0 | editor-only frame; `m_Title` = waves, Water color, Refraction, Normals, Foam | H |
| `UnityEditor.ShaderGraph.Internal.ColorShaderProperty` | 4 | 4 | 0 | 0 | 0 | 0 | exposed float4 color (Shallow/deep/Horizon/Foam) | H |
| `UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty` | 19 | 14 | 2 | 2 | 0 | 1 | exposed float; several defaults are 0 in the converted material | H |
| `UnityEditor.ShaderGraph.Internal.Vector2ShaderProperty` | 4 | 1 | 0 | 0 | 2 | 1 | exposed float2 (Refraction Scale, Speed, Scale, UV) | H |
| `UnityEditor.ShaderGraph.LerpNode` | 6 | 6 | 0 | 0 | 0 | 0 | A·(1−T)+B·T | H |
| `UnityEditor.ShaderGraph.MultiplyNode` | 21 | 9 | 10 | 1 | 1 | 0 | A·B; DynamicValue slots allow scalar×vector×matrix | H |
| `UnityEditor.ShaderGraph.NegateNode` | 3 | 3 | 0 | 0 | 0 | 0 | −A; used to build the four scrolling directions | H |
| `UnityEditor.ShaderGraph.NormalBlendNode` | 3 | 3 | 0 | 0 | 0 | 0 | blend two tangent-space normals (whiteout-style); three instances form a binary tree | M (exact blend formula not in JSON) |
| `UnityEditor.ShaderGraph.NormalMaterialSlot` | 3 | 3 | 0 | 0 | 0 | 0 | float3 normal pin | H |
| `UnityEditor.ShaderGraph.NormalStrengthNode` | 2 | 1 | 0 | 0 | 0 | 1 | flatten tangent-space normal XY by Strength; `synonyms` absent but slot names conclusive | H |
| `UnityEditor.ShaderGraph.PositionMaterialSlot` | 3 | 3 | 0 | 0 | 0 | 0 | position pin | H |
| `UnityEditor.ShaderGraph.PositionNode` | 1 | 1 | 0 | 0 | 0 | 0 | `synonyms: location`; `m_Space=2` ⇒ world position | M (enum mapping) |
| `UnityEditor.ShaderGraph.PreviewNode` | 1 | 0 | 0 | 0 | 0 | 1 | editor-only preview passthrough (Normal map noise output) | H |
| `UnityEditor.ShaderGraph.PropertyNode` | 28 | 20 | 2 | 2 | 2 | 2 | reads exposed property via `m_Property.m_Id` | H |
| `UnityEditor.ShaderGraph.SampleTexture2DNode` | 1 | 0 | 0 | 0 | 0 | 1 | `synonyms: tex2d`; `m_TextureType=1` (normal map); default texture GUID `3bd52d67…` = Water Normal.png | H |
| `UnityEditor.ShaderGraph.SamplerStateMaterialSlot` | 1 | 0 | 0 | 0 | 0 | 1 | sampler pin on SampleTexture2D | H |
| `UnityEditor.ShaderGraph.SaturateNode` | 3 | 2 | 0 | 1 | 0 | 0 | clamp01 | H |
| `UnityEditor.ShaderGraph.SceneColorNode` | 1 | 1 | 0 | 0 | 0 | 0 | `synonyms: screen buffer`; opaque/scene color lookup by screen UV | H |
| `UnityEditor.ShaderGraph.SceneDepthNode` | 1 | 0 | 0 | 1 | 0 | 0 | scene depth lookup at screen UV (eye depth path) | H |
| `UnityEditor.ShaderGraph.ScreenPositionMaterialSlot` | 2 | 1 | 0 | 1 | 0 | 0 | float4 screen position pin | H |
| `UnityEditor.ShaderGraph.ScreenPositionNode` | 2 | 1 | 0 | 1 | 0 | 0 | current fragment screen position (used for depth/color UVs) | H |
| `UnityEditor.ShaderGraph.SineNode` | 5 | 0 | 5 | 0 | 0 | 0 | sin(input); five harmonics 1/2/5/8/11 | H |
| `UnityEditor.ShaderGraph.SplitNode` | 5 | 4 | 0 | 1 | 0 | 0 | vector → R/G/B/A outputs | H |
| `UnityEditor.ShaderGraph.StepNode` | 2 | 2 | 0 | 0 | 0 | 0 | step(Edge,In) = In≥Edge?1:0; Edge/In defaults read from `m_Value` | H |
| `UnityEditor.ShaderGraph.SubGraphNode` | 13 | 13 | 0 | 0 | 0 | 0 | `m_SerializedSubGraph` GUID selects the subgraph: Normal map ×4, Sinewave ×2, depthfade ×2, texture movement ×5 | H |
| `UnityEditor.ShaderGraph.SubGraphOutputNode` | 4 | 0 | 1 | 1 | 1 | 1 | subgraph output pins (`Out_Vector4`, `normal`, `noise`) | H |
| `UnityEditor.ShaderGraph.SubtractNode` | 1 | 0 | 0 | 1 | 0 | 0 | A−B in depthfade: sceneDepth − screenPos.a | H |
| `UnityEditor.ShaderGraph.TangentMaterialSlot` | 1 | 1 | 0 | 0 | 0 | 0 | tangent pin (VertexDescription.Tangent, unconnected) | H |
| `UnityEditor.ShaderGraph.Texture2DInputMaterialSlot` | 1 | 0 | 0 | 0 | 0 | 1 | texture pin with serialized default texture guid | H |
| `UnityEditor.ShaderGraph.TilingAndOffsetNode` | 2 | 0 | 0 | 0 | 1 | 1 | `synonyms: pan,scale`; UV·Tiling + Offset | H |
| `UnityEditor.ShaderGraph.TimeNode` | 4 | 3 | 0 | 0 | 1 | 0 | `_Time`: slot0 Time, 1 Sine, 2 Cosine, 3 Delta, 4 Smooth Delta | H |
| `UnityEditor.ShaderGraph.TransformNode` | 1 | 1 | 0 | 0 | 0 | 0 | `synonyms: world,tangent,object,view,screen,convert`; `m_Conversion {from:2,to:0}` = world→object, `m_Normalize=true` | M |
| `UnityEditor.ShaderGraph.UVMaterialSlot` | 4 | 1 | 0 | 0 | 1 | 2 | float2 UV pin | H |
| `UnityEditor.ShaderGraph.Vector1MaterialSlot` | 108 | 86 | 3 | 7 | 5 | 7 | float pin; unconnected defaults in `m_Value` (e.g. Divide B=2, DotProduct B=(0,1,0,0)) | H |
| `UnityEditor.ShaderGraph.Vector2MaterialSlot` | 33 | 23 | 0 | 0 | 6 | 4 | float2 pin | H |
| `UnityEditor.ShaderGraph.Vector2Node` | 1 | 1 | 0 | 0 | 0 | 0 | literal float2; X unconnected (0), Y from Refraction Speed | H |
| `UnityEditor.ShaderGraph.Vector3MaterialSlot` | 26 | 23 | 0 | 0 | 0 | 3 | float3 pin | H |
| `UnityEditor.ShaderGraph.Vector3Node` | 1 | 1 | 0 | 0 | 0 | 0 | literal float3 assembled from split world-position components | H |
| `UnityEditor.ShaderGraph.Vector4MaterialSlot` | 17 | 13 | 1 | 2 | 0 | 1 | float4 pin (subgraph `Out_Vector4` is really a float2/float4 depending on instance) | H |
| `UnityEditor.ShaderGraph.ViewDirectionMaterialSlot` | 1 | 1 | 0 | 0 | 0 | 0 | view-direction pin | H |
| `UnityEditor.ShaderGraph.ViewVectorNode` | 1 | 1 | 0 | 0 | 0 | 0 | `synonyms: eye vector`; `m_Space=2` ⇒ world-space view vector | M |

## Confidence key

- **H** — unambiguous from node type, `synonyms`, slot names, `m_Value`, and edge topology.
- **M** — operation clear, but an enum mapping, unconnected-input behavior, or exact formula needs checking.
- **L** — name only; local evidence insufficient.

## Cross-reference with sibling folders

Shared types were checked against `Docs/ShaderGraph/Other/NODES.md`. No inference
conflicts on the arithmetic/slot types; the notable extra detail here is that
`ConstantNode.m_constant=0` is read as PI (Other has no instance). Sky's
`NODES.md` did not exist at the time of writing. See `CONFLICTS.md` for the
items where Water has stronger/different evidence or where the sibling's claim
needs a caveat for this graph.
