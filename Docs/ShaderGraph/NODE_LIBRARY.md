# Unified ShaderGraph node library

Merged from the three independent detective passes:

- `Sky/NODES.md` — `Skybox.shadergraph`, `skybox uv`, `stars` (scope M/SU/ST)
- `Water/NODES.md` — `water.shadergraph`, `Sinewave`, `depthfade`, `texture movement`, `Normal map`
- `Other/NODES.md` — `Cloud`, `Flower shader`, `Grass`, `Heat Distortion`, `TriPlane`

Read the per-folder `ANALYSIS.md` files for edge traces and pseudocode; this file is the shared
cross-reference so the same `m_Type` is interpreted consistently across all three contributions.
`Shared/SUBGRAPH_INDEX.md` resolves subgraph GUID → file → instance count.

## Conventions used by all three

- Every serialized object has `m_ObjectId`; references are `{ "m_Id": ... }` (node-local slot ids)
  or `{ "m_GuidSerialized": ... }` (property identity).
- `m_SlotType`: `0` = input, `1` = output. `m_DrawState.m_Position` gives graph layout (left = earlier).
- `synonyms` arrays often name the operation directly (`multiply`, `times`, `mix`, `blend`, `remap`).
- Confidence: **H** = directly evidenced by type/synonyms/topology, **M** = topology certain but
  meaning inferred, **L** = guess (flagged in the per-folder `CONFLICTS.md` files).

## Unified node type meanings

| `m_Type` (short) | Meaning | Conf. | Seen in |
|---|---|---|---|
| `DynamicVectorMaterialSlot` | Untyped 1–4 component pin; width from the connection. | H | Sky, Water, Other |
| `DynamicValueMaterialSlot` | Untyped scalar-or-matrix pin; identity matrix default. | H | Sky, Other |
| `Vector1/2/3/4MaterialSlot` | Typed float pin of width 1/2/3/4. | H | all |
| `BooleanMaterialSlot` | Typed bool pin (rare; TriPlane sun toggle). | H | Other |
| `ColorRGBMaterialSlot` | RGB color pin with `m_DefaultColor`. | H | Sky, Other |
| `NormalMaterialSlot` | Normal pin; `m_Space` marks tangent/world/object. | M | Sky, Other |
| `PropertyNode` | Reads a graph property via `m_Property.m_Id`. | H | all |
| `BlockNode` | Final VertexDescription/SurfaceDescription output stage. | H | all |
| `GroupData` | Editor-only grouping label (`m_Title`), e.g. "Foam", "Refraction". | H | all |
| `AddNode` | `A+B` component-wise. | H | all |
| `SubtractNode` | `A-B`. | H | Sky, Water, Other |
| `MultiplyNode` | `A*B` (matrix/scalar/vector by connection). | H | all |
| `DivideNode` | `A/B`. | H | Sky, Other |
| `PowerNode` | `pow(A,B)`. | H | Sky, Water |
| `LerpNode` | `lerp(A,B,T)` (`synonyms`: mix, blend). | H | all |
| `SaturateNode` | `clamp01(x)` (`synonyms`: clamp). | H | all |
| `ClampNode` | `clamp(x,min,max)` with explicit bounds. | H | Water, Other |
| `NegateNode` | `-x` (`synonyms`: invert, opposite). | H | all |
| `SplitNode` | Component split (R/G/B/A). | H | all |
| `CombineNode` | Build a vector from components. | H | Sky, Water |
| `StepNode` | `step(edge,x)`; binary select. | H | Sky, Other |
| `Vector1/2/3/4Node` | Literal constant vector of that width. | H | all |
| `ConstantNode` | Scalar constant (`m_constant`; 1 vs 0 ambiguity in `skybox uv`). | M/L | Sky |
| `TimeNode` | Time outputs (Time, Sine Time, Cosine Time, Delta Time). | H | Sky, Water, Other |
| `SampleTexture2DNode` | Texture sample; unwired UV slot defaults to `(0,0)` (watch Flower/Heat). | H | Water, Other |
| `SubGraphNode` | Nested graph instance; `m_SerializedSubGraph.guid` + `m_PropertyGuids`. | H | all |
| `PositionNode` | Position in a space (`m_Space`), e.g. Object/World. | H | Water, Other |
| `NormalVectorNode` | Surface normal in a space. | H | Other |
| `ViewDirectionNode` | Camera-to-surface direction. | H | Other |
| `TransformationMatrixNode` | Matrix conversion (`m_MatrixType`; billboard role in Heat Distortion). | L | Other |
| `SimpleNoiseNode` | Value-noise FBM (scale/octaves in slots). | H | Sky, Water |
| `GradientNoiseNode` | Gradient noise. | H | Water |
| `PosterizeNode` | Quantize/step remap (cloud density chain in Sky). | M | Sky |
| `RemapNode` | Linear remap from input range to output range. | H | Water, Other |
| `OneMinusNode` | `1-x`. | H | Sky, Water |
| `DotProductNode` | Dot product. | H | Other |
| `CrossProductNode` | Cross product. | H | Other |
| `FresnelEffectNode` | View-dependent rim term. | H | Water, Other |
| `MainLightDirectionNode` | Pipeline-neutral "main light direction" read (not URP-exclusive here). | M | Sky |
| `UniversalTarget` / `UniversalLitSubTarget` / `UniversalUnlitSubTarget` | URP target settings: render face, blend, alpha clip, surface blocks. | H | Other, Sky, Water |
| `Vector1ShaderProperty` etc. (`Internal.*ShaderProperty`) | Graph property definition with `m_DefaultReferenceName` and `m_Value`. | H | all |

## Cross-reference notes

- **Agreed by all three**: `MultiplyNode`, `LerpNode`, `SaturateNode`, `NegateNode`, `SplitNode`,
  `PropertyNode`, `BlockNode`, `TimeNode`, `SubGraphNode` — confidence High.
- **Adopted Sky's pipeline-neutral reading** of `MainLightDirectionNode` in Other and Water.
- **`texture movement` subgraph**: Water and Other independently agree the `UV` pin is unwired in
  these instances, so the result reduces to `time * speed`.
- **Open low-confidence items** (see per-folder `CONFLICTS.md` and `ANALYSIS.md`):
  `ConstantNode` PI-vs-TAU in `skybox uv`; cloud-density Multiply/Posterize "FBM" naming;
  moon `Step` with shipped `_Moon_size = 7.7` outside `asin` range; Heat Distortion
  `TransformationMatrixNode` billboard matrix; unwired sample UVs in Flower/Heat; TriPlane
  bool-to-float in `step(0.3, facing*sun + 1)`.
