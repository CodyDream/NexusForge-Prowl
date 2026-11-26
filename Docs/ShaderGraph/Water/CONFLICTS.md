# Cross-folder node cross-reference (Water ownership)

Read after writing `NODES.md`/`ANALYSIS.md`: sibling folders now contain
`Docs/ShaderGraph/Other/NODES.md`, `Docs/ShaderGraph/Sky/NODES.md`,
`Docs/ShaderGraph/Sky/CONFLICTS.md`, and `Docs/ShaderGraph/Shared/SUBGRAPH_INDEX.md`.
Nothing here edits another agent's files. No SDK or build work was performed.

## Shared node types — agreement

Every Water node type was checked against the Other and Sky tables. Shared with
both: `GraphData`, `CategoryData`, `GroupData`, `PropertyNode`, `BlockNode`,
`AddNode`, `MultiplyNode`, `DivideNode`, `LerpNode`, `SaturateNode`, `NegateNode`,
`SplitNode`, `CombineNode`, `DotProductNode`, `StepNode`, `SubtractNode`,
`TilingAndOffsetNode`, `TimeNode`, `GradientNoiseNode`, `FogNode`, `SubGraphNode`,
`SubGraphOutputNode`, `ConstantNode`, `PositionNode`, `ScreenPositionNode`,
all `*MaterialSlot` types, `Vector1/2ShaderProperty`, `ColorShaderProperty`,
`UniversalTarget`. No hard inference conflict on arithmetic or slot semantics.

Types Water shares with Other but not Sky: `FresnelNode`, `SceneColorNode`,
`SceneDepthNode`, `TransformNode`, `SampleTexture2DNode`, `PreviewNode`,
`NormalStrengthNode`, `NormalBlendNode`, `Vector2Node`, `Vector3Node`,
`ViewVectorNode`, `ClampNode`, `UniversalLitSubTarget`. Sky's statement that
these do not occur in `Skybox.shadergraph` is consistent with Water's own
counts (all are in `water.shadergraph` / its subgraphs only).

Types Sky has that Water does not: `ArcsineNode`, `Arctangent2Node`,
`PosterizeNode`, `RemapNode`, `NormalizeNode`, `OneMinusNode`, `PowerNode`,
`NoiseNode`, `GradientNode`, `SampleGradient`, `BuiltInTarget`, etc. No
cross-check available; Sky's local inferences were accepted as-is.
Water has no `RedirectNodeData` (that single instance belongs to Other).

## Evidence-strength differences and water-specific caveats

| Shared type | Sibling statement | Water evidence | Outcome |
|---|---|---|---|
| `ConstantNode` | Sky: `m_constant` 0 and 1 → likely PI/TAU, assigns **Low**; notes swapping changes UV ranges. | Water's only instance `9b3b8ffc` has `m_constant=0`; a following `MultiplyNode a392a9f8` has B literal **2**, giving 2π per cycle, which is coherent with PI=0. | **Agreement**, Water keeps **Medium** (Sky Low). No conflict; recorded as Water uncertainty #3. |
| `StepNode` | Other: `step(Edge,In) = In>=Edge ? 1 : 0` (**H**, "NOT Edge>=In"). Sky: relies on `step(_Sun_size, asin(dot(...)))`, full agreement. | Water uses the same reading twice: `0e028674` builds the foam threshold from the gradient noise (`step(edge, noise)`), and `cab8dd8e` uses Edge default 0 with `dot(view,(0,1,0))`. | Full agreement. Sky's out-of-range-size caveat does not apply here. |
| `PositionNode` / `m_Space=2` | Other **M** (0=Object, 2=World inferred). Sky **High** for `skybox uv` because the equirectangular mapping only makes sense in world space. | `61d1b431` (`m_PositionSource=0`) feeds a world-space wave displacement and a world→object `TransformNode 9295cd68` (`m_Conversion {from:2,to:0}`). | Same conclusion; Water keeps **M** for the enum mapping but **H** for this instance's role. |
| `FogNode` | Other: does not occur. Sky: Color connected to final horizon lerp; Density unused; assigns **Medium**. | `6a069dc4` connects **both** Color and Density into the final `LerpNode 9551f005` feeding BaseColor. | Meaning agrees. Scope caveat: Sky's "Density unused" is true only for Sky; do not generalise it. |
| `FresnelNode` | Other: `pow(1 - saturate(dot(N,V)), Power)`, **M**. | Water's `2e6e4697` has N and View Dir inputs **unconnected with (0,0,0) defaults**, so it degenerates to 1 for any Power. | Formula agrees; Water's evaluation is degenerate. Flagged as uncertainty #2, not corrected in the translation. |
| `TilingAndOffsetNode` | Other: `UV*Tiling + Offset`, **H**. Sky: used for sky-band/cloud panning. | Operation agrees, but the UV input of `4bc1fbd8` (`texture movement`) is unconnected with default `(0,0)`, so `Scale` has no effect and all five parent instances are spatially uniform. | Operation agreement; Water-specific caveat is uncertainty #1. Sky's cloud panning does not share the defect. |
| `SceneColorNode` | Other: "camera opaque/scene color texture lookup by screen UV; synonyms screen buffer", **H**. | Water's `8400e357` receives `ScreenPosition + normal*RefractionStrength` — a refraction lookup. | Agreement; Water adds the refraction-use case. |
| `GradientNoiseNode` | Other **H** (`synonyms: perlin noise`). Sky **Medium** for the stars instance. | Water's `8259c3ec` has the same synonym and a literal Scale=10. | No conflict; exact hash bits unrecoverable in all three folders. |
| `SampleTexture2DNode` | Other **H** for plain colour/normal sampling. | `bc5a9e24` has `m_TextureType=1` (normal decode) and a serialized default texture GUID resolving to `Water Normal.png`. | Agreement; Water's normal-decode detail is additional, not contradictory. |
| `SubGraphNode` | Other: `m_SerializedSubGraph` GUID + `m_PropertyIds` overrides, **H**. | All 13 instances resolve by GUID; the five `texture movement` instances also carry stale GUIDs `c2c894f2` and `15ab56ef` with no matching slot/property (matches `Shared/SUBGRAPH_INDEX.md`). | Agreement. The shared index's warning that `.meta` GUIDs ≠ serialized GUIDs was essential and is confirmed here. |
| Count convention | Other: all serialized objects incl. slots/targets. Sky: same for `NODES.md`, separate "Nodes" column = `m_Nodes` entries. | Water uses the same two metrics (`NODES.md` counts objects; `ANALYSIS.md` table separates objects / m_Nodes / edges). | Aligned. |
| `PropertyNode` names | Sky: asset contains typos (`clod edge`, `Sky night color`); `m_DefaultReferenceName` is authoritative for uniform binding. | Water has the same class of typos: `amplitute`, `smotness`, `Refraction stength`, `deep color`. | Agreement — do not "fix" names when binding; Water tables quote `m_Name` for traceability only. |

## Top three Water uncertainties (recorded, not resolved)

1. `TilingAndOffsetNode 4bc1fbd8` inside `texture movement` — UV default `(0,0)`
   makes the whole layer spatially uniform (LOW confidence it is intentional).
2. `FresnelNode 2e6e4697` — both vector inputs unconnected at `(0,0,0)`, so the
   horizon lerp always selects Horizon color (LOW).
3. `ConstantNode 9b3b8ffc` — PI-vs-TAU enum reading (MEDIUM/LOW; Sky also Low).

`NormalBlendNode` ×3 is a runner-up: operation clear, exact blend formula not
recoverable from JSON; the reference uses a simplified whiteout blend.
