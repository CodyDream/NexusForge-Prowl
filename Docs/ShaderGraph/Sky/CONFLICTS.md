# Cross-folder node cross-reference (Sky ownership)

Read after creation of `NODES.md`/`ANALYSIS.md`: sibling folders now contain
`Docs/ShaderGraph/Water/NODES.md` and `Docs/ShaderGraph/Other/NODES.md`.
Their `ANALYSIS.md` and `CONFLICTS.md` files were not present at check time.
Nothing in this file edits another agent's output.

## Shared node types — agreement

Checked against both sibling tables: `GraphData`, `GroupData`, `CategoryData`,
`PropertyNode`, `BlockNode`, `AddNode`, `SubtractNode`, `MultiplyNode`,
`DivideNode`, `PowerNode`, `LerpNode`, `SaturateNode`, `OneMinusNode`,
`StepNode`, `SplitNode`, `CombineNode`, `DotProductNode`, `NormalizeNode`,
`RemapNode`, `SmoothstepNode`, `TilingAndOffsetNode`, `TimeNode`,
`NoiseNode`, `GradientNoiseNode`, `MainLightDirectionNode`, `FogNode`,
`SubGraphNode`, `SubGraphOutputNode`, `ConstantNode`, all `*MaterialSlot`
types, `Vector1/2ShaderProperty`, `ColorShaderProperty`, `UniversalTarget`,
`UniversalUnlitSubTarget`. No hard inference conflicts on arithmetic or slot
semantics.

Notes where our evidence differs in strength or scope:

| Shared type | Sibling statement | Sky evidence | Outcome |
|---|---|---|---|
| `ConstantNode` | Water: `m_constant=0` read as PI, confidence **M**. | Sky has **two** instances: `3b3eaecf` (`m_constant=0`) and `c905564f` (`m_constant=1`). Reading 0=PI, 1=TAU is our default, but swapping them changes UV range from `[-0.5,0.5]×[-1,1]` to `[-1,1]×[-0.5,0.5]`; later tiling/offset can compensate either. | Not a conflict; Sky assigns **Low** and provides `skyboxUVAlt` in `skybox_uv.slang`. Water's PI reading is consistent with our default. |
| `StepNode` | Other: `step(Edge,In) = In >= Edge ? 1 : 0`, "NOT Edge>=In"; Water agrees. | Sky relies on exactly this: `step(_Sun_size, asin(dot(...)))`. With material `_Sun_size=1.36` this yields a ~12° disk; with `_Moon_size=7.7` it yields no disk at all. | Full agreement. Sky adds the out-of-range-size caveat (see `ANALYSIS.md` uncertainty #3). |
| `MainLightDirectionNode` | Other: "URP main directional light direction", confidence H. | Sky's two instances coexist with **both** a `BuiltInTarget` (`184f09cc`, Lit) and a `UniversalTarget` (`60176e3e`, Unlit) in `m_ActiveTargets`; `synonyms` only say `sun`. | Scope caveat: calling it URP-only is too narrow for this graph. Inferred meaning kept pipeline-neutral. Not a correction to Other's file. |
| `NoiseNode` (Simple Noise) | Other: `m_HashType=0`, meaning "Simple/value noise", H. | Sky has three instances with `m_HashType=1` (clouds) and one with `m_HashType=0` (stars), plus `GradientNoiseNode` h0 inside `stars`. Meaning agrees; exact hash bits are not recoverable from JSON. | Meaning agrees; Sky marks exact hash reproduction **Medium**. |
| `PositionNode` / `m_Space=2` | Water/Other: 2 = World, confidence M. | `skybox uv` uses `PositionNode(m_Space=2, m_PositionSource=0)` → normalize → `atan2/asin` equirectangular mapping; this is only coherent for a world-space direction. | Same conclusion, Sky confidence **High** for this instance. |
| `FogNode` | Water: Color + Density outputs, engine-provided, M. | Sky: `Color` connected to the final lerp; `Position` unconnected; `Density` unused. | Agreement; Sky does not model fog density at all. |
| Count convention | Both siblings count **all serialized objects** (including slots/targets). | Sky `NODES.md` also counts all serialized objects per type. The `ANALYSIS.md` "Nodes" column counts only `GraphData.m_Nodes` entries. | Convention aligned; the two columns are explicitly different metrics. |

## Types Sky has that siblings do not

`ArcsineNode`, `Arctangent2Node`, `PosterizeNode`, `RemapNode`, `ConstantNode`
(2 instances), `GradientNode`, `SampleGradient`, `GradientInputMaterialSlot`,
`GradientMaterialSlot`, `BuiltInTarget`, `BuiltInLitSubTarget`,
`ViewDirectionNode`, `Internal.Vector2ShaderProperty`. No cross-check available;
inferences are local and recorded in `NODES.md` with confidence labels.

## Types siblings have that Sky does not

`NegateNode`, `Vector1/2/3/4Node`, `ClampNode`, `FresnelNode`,
`SceneColorNode`, `SceneDepthNode`, `ScreenPositionNode`, `TransformNode`,
`SampleTexture2DNode`, `PreviewNode`, `RedirectNodeData`, etc. If another agent
claims these occur in `Skybox.shadergraph`, that is a miscount: the Sky graph's
99 nodes are enumerated in the appendix of `ANALYSIS.md`.
