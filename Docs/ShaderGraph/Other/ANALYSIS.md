# ShaderGraph reverse engineering — Cloud / Flower / Grass / Heat Distortion / TriPlane

Scope: inference from local `.shadergraph` JSON, local `.shadersubgraph` JSON, converted `.mat`
files and the original `.unitypackage` materials shipped in this project. No engine code, importers,
Git operations, or Unity-authored shader source was used as a translation source. Nothing here is
copied from Unity source; all algorithms are inferred from JSON topology and slot defaults.

| Graph | file | JSON objects | nodes | edges | props | target | notable feature |
|---|---|---:|---:|---:|---:|---|---|
| Cloud | `Cloud.shadergraph` | 119 | 37 | 26 | 6 | URP Lit | noise vertex inflation + fresnel/wrap subsurface tint |
| Flower | `Flower shader.shadergraph` | 137 | 36 | 30 | 6 | URP Lit | palette LUT + object-height vertex sway |
| Grass | `Grass.shadergraph` | 152 | 44 | 36 | 7 | URP Lit | world-XZ noise sway + height gradient + height*Smoothness |
| Heat Distortion | `Heat Distortion.shadergraph` | 104 | 28 | 26 | 4 | URP **Unlit** | billboard vertex group + screen-space scene-color refraction |
| TriPlane | `TriPlane.shadergraph` | 80 | 27 | 18 | 6 | URP Lit | two-tone step shading; no texture sampling |

Type totals across the five graphs are in `NODES.md`. Shared subgraph instances are in
`../Shared/SUBGRAPH_INDEX.md`: exactly **one** cross-graph instance occurs in this scope,
`texture movement` in Heat Distortion (`42663009`, Scale<-`_Noise_Scale`, Speed<-`_Noise_speed`).
The other subgraphs (`depthfade`, `Normal map`, `Sinewave`, `skybox uv`, `stars`) are **not**
instantiated in these five graphs.

## Material binding evidence

The converted `.mat` files under `Matereals\` contain **no** property values (all `entries` empty),
and all point at the same shader asset GUID `63d5be44-...`. Do not bind from them. The original
package `Assets/UnityImport/Low Poly Stylized Nature.unitypackage` contains the authored YAML
materials; shader GUIDs map 1:1 to the local graph paths, e.g. `16f6b6d4...` = Cloud,
`814c25c1...` = Flower, `90af4688...` = Grass, `60b284c1...` = Heat, `94ddfc76...` = TriPlane
(= `Rock.mat`). Useful overrides (graph default -> shipped value):

| Material | Authoritative override values |
|---|---|
| `Cloud.mat` | `_Noise_Scale` 10->**0.01**, `_Noise_Amplitute` 0.1->**2.3**, `_Noise_speed` 1->**0.01**, `_Fresenal_power` 0->**1.35**, `_Subsurface_scattering_color` (0,0,0,0)->**(2.996, 2.634, 0, 1)** HDR, `_Color` (0.538,0.538,0.538,1) |
| `Flowers.mat` | `_Noise_Scale` 2->**0.2**, `_Noise_amplitute` 0.5->**0.2**, `_Noise_speed` (2,0)->**(2,2)**, `_Colour_pallate` = texture guid `d56f8ee12d992fa44a396f5`, `_Blend_color` (1,1,1,0), `_Smoothness` 0->**1** (unused) |
| `Grass.mat` | `_Noise_scale` 2->**1**, `_Noise_Amplitute` 0.5->**0.1**, `_Noise_speed` (2,0)->**(1,1)**, `_Base_noise` 0->**0.09**, `_base_color` (0.670,1,0,0), `_top_color` (0.185,1,0,0), `_Smoothness` 0->**1** |
| `Heat Distortion Mat.mat` | `_noise_stregth` 0->**0.177**, `_Noise_Scale` 50 (unchanged), `_Noise_speed` (0,0)->**(0,-20)**, `_Noise_mask` built-in white (`fileID:10300`, guid `000...f000...`) |
| `Rock.mat` (TriPlane) | `_Is_sun_move` false->**true**, `_step_Edge` 0.3, `_BaseColor` (0.585,0.585,0.585), `_SecondaryColor` (0,1,0.315), `_Tri_Planer_Direction` (0,1,0) |

The shipped `Cloud.mat` alpha is effectively opaque (Alpha block = `_Color.a` = 1), even though
`_BaseColor` in the material has alpha 0.533; the block reads the `_Color` property, not `_BaseColor`.

## Pseudocode (inferred)

```text
Cloud:
  drift     = positionOS * _Noise_Scale + _Time.y * _Noise_speed
  n         = gradientNoise(drift.xy, 10)                 // Perlin-like, Scale pin unwired
  worldPos  = objectToWorld(positionOS) + n * _Noise_Amplitute
  N         = worldNormal(); L = mainLightDirection()
  t         = pow(1 - saturate(dot(N,V)), _Fresenal_power) * saturate(dot(L,N))
  color     = lerp(_Color.rgb, _Subsurface_scattering_color.rgb, t); alpha = _Color.a

Flower:
  drift     = _Time.y * _Noise_speed + objectWorldPosition.xz
  n         = gradientNoise(positionWS.xz + drift, _Noise_Scale)
  sway      = (n - 0.5) * _Noise_amplitute * 2
  swayed    = positionWS + float3(sway, 0, sway)
  pos       = lerp(positionWS, swayed, objectSpaceY)       // object-space Y from Position(Object)
  vertex    = normalize(worldToObject(pos))                // Transform normalize=true
  baseColor = paletteSample(uv = default (0,0)) * _Blend_color  // UV pin UNWIRED

Grass:
  drift     = _Time.y * _Noise_speed + positionWS.xz * _Base_noise
  n         = gradientNoise(positionWS.xz + drift, _Noise_scale)
  sway      = (n - 0.5) * _Noise_Amplitute * 2
  vertex    = lerp(positionWS, positionWS + float3(sway,0,sway), objectSpaceY)
  baseColor = lerp(_base_color, _top_color, objectSpaceY)
  smoothness= objectSpaceY * _Smoothness

HeatDistortion:
  billboard = objectWorldPos + billboardMatrix(float4(positionOS*objectScale,0))
  vertex    = normalize(worldToObject(billboard))          // group "Bill board"
  uv        = (0 + _Time.y * _Noise_speed) * _Noise_Scale  // shared "texture movement" subgraph
  n         = valueNoise(uv, scale=1) * maskTexture.a      // UV pin unwired => (0,0)
  screenUV  = lerp(screenPos.xy, screenPos.xy + n, _noise_stregth)
  color     = sceneColor(screenUV)

TriPlane:
  facing    = saturate(dot(_Tri_Planer_Direction, worldNormal()))
  sun       = saturate(dot(-mainLightDirection(), worldNormal()))
  m         = facing * sun + float(_Is_sun_move)
  color     = lerp(_BaseColor, _SecondaryColor, step(_step_Edge, m))
```

## Uncertain nodes / operations (ranked)

1. **`TransformationMatrixNode` `adf5323e` (Heat Distortion), LOW.** `m_MatrixType: 3`,
   `m_matrix: -1`. The vertex billboard group multiplies `(positionOS * objectScale, 0)` by this
   matrix, then adds object position. The exact matrix (view rotation? camera-right/up?) is not
   serialized; only its role as a camera-facing transform is inferable.
2. **`SampleTexture2DNode` UV inputs (Flower `45a49aa9`, Heat `c8ae43d7`), LOW.** Neither node has
   an edge on its UV slot (`m_SlotId: 2`); the serialized default is `(0,0)`. Consequently the
   palette/alpha mask is sampled at one constant texel. This may be intentional (a 1-texel LUT /
   built-in white) or the graph may be incomplete. Do not invent a UV source when porting.
3. **`StepNode` `8f61d20a` + bool add `5a75911a` (TriPlane), MEDIUM.** `Rock.mat` ships
   `_Is_sun_move = true`; with `A = bool-as-float` added to `dot*dot`, `step(0.3, m)` is always 1,
   making the rock permanently secondary-green. Either the bool casts to something else, or the
   shipped value intentionally disables the primary color. The edge topology itself is certain.

Secondary uncertainties: gradient/simple noise bit-exactness (`m_HashType`: Cloud
`GradientNoiseNode 4fa12012` = 1, Flower `a9e23f2c` and Grass `dc3beb6f` = 0, Heat
`NoiseNode 7c959616` = 0; sibling folders read 0 as standard and 1 as LPM-like);
`TransformNode m_Normalize=true` applied to a
**position** (Fragment/Vertex conversions in Cloud, Flower, Grass, Heat); `m_Space` and
`m_Conversion` enums are read as 0=Object / 2=World from context, not from serialized labels;
`SubtractNode` B default `(0.5,1,1,1)` interpreted as `n - 0.5`; URP `MainLightDirection` and
`SceneColor` require engine state that the converted project does not yet expose.

## External state a port must supply

| Requirement | Where needed | Notes |
|---|---|---|
| `_Time.y` | Cloud `9fa9c4f6`, Flower `b67933e2`, Grass `bd959200`; indirectly Heat through `texture movement` | Motion speed comes from `_Noise_speed` properties, not from dedicated wind uniforms. |
| Main light direction | Cloud `9be05dc6`, TriPlane `472f9c9e` | `MainLightDirectionNode` (URP-namespaced node; host supplies the light), synonyms `sun`. Sky's graph has both BuiltIn and URP targets, so treat it as pipeline-neutral state. |
| Camera/view state | Cloud Fresnel (view dir), Heat `ScreenPositionNode` + `SceneColorNode` + billboard matrix | Scene-color/opaque texture and view-projection are engine-managed. |
| World/object transforms | all five except TriPlane | `PositionNode m_Space=0/2`, `ObjectNode`, `TransformNode from=2,to=0`. |
| Object-space position / vertex colors | Flower/Grass use object-space Y from `Position(Object)`; **no** `VertexColorNode` exists in any of the five | External state is geometry attributes, not vertex colors. |
| Camera depth texture | **not needed** here; `depthfade.shadersubgraph` is only used by `water.shadergraph` | Cross-reference only. |
| Palette / mask textures | Flower `_Colour_pallate`, Heat `_Noise_mask` | Both UV pins unwired (see uncertainty #2). |

## Portability assessment

- **Fully portable from JSON (algorithm-level):** `TriPlane` (pure math, 18 edges) and `Cloud`
  (self-contained noise/fresnel; requires only engine light/view uniforms). Both need bit-exact
  noise/fresnel matching only if pixel-identical output is required.
- **Portable with a documented caveat:** `Grass` and `Flower` — same sway skeleton as Cloud, but
  the object-space height and unwired palette UV must be decided by the port author; `Flower`'s
  palette sample is effectively a constant color as authored.
- **Needs manual work:** `Heat Distortion` — the unlit refraction path is straightforward, but the
  billboard `TransformationMatrixNode` and the URP scene-color texture (plus its capture timing)
  cannot be reconstructed from JSON alone.

## Cross-reference with sibling folders

`Sky/NODES.md`, `Water/NODES.md` and `Other/NODES.md` share the same conventions (counts include
`GraphData`/slots/targets; slot type 1=output; space enum 0=Object,2=World). All shared node
semantics checked out: arithmetic nodes, `StepNode` (`In>=Edge`), `NoiseNode` hash flag,
`GradientNoiseNode` (perlin), `FresnelNode`, `SceneColorNode`, `ScreenPositionNode`,
`TransformNode {from:2,to:0}`, `SubGraphNode` identity, `TimeNode` slot order, and the material-slot
types. Sky's `CONFLICTS.md` was also read: its only caveat affecting this folder is that
`MainLightDirectionNode` should be described pipeline-neutrally (Sky's graph registers both BuiltIn
and URP targets), which is reflected above and in `NODES.md`. Water's `texture_movement.slang`
agrees with this folder's Heat Distortion reading: the subgraph reduces to `time*speed` because its
UV input is unwired and multiplied by zero. No disagreements remain, so no `CONFLICTS.md` was needed.

## Appendices: complete property/node ID maps and edge traces

Short IDs are the first 8 hex characters of each `m_ObjectId`; slot names come from the referenced
material slot objects. `m_SlotType: 1=output, 0=input`.


### Cloud

**Properties (m_ObjectId → ref name, default):**

| full ID | name | ref | default / flags |
|---|---|---|---|
| `58cc14cb95814ffe956731c334bfd302` | Color | `_Color` | {r:0,g:0,b:0,a:0} m_ColorMode=1 |
| `c57dcec76e144cd8adf3bf48ddec8680` | Noise Scale | `_Noise_Scale` | 10 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `4c151227987a47afa6b26a1903c72a8d` | Noise Amplitute | `_Noise_Amplitute` | 0.1 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `47bf4ba5cea44d26b67b5ff0851f933c` | Noise speed | `_Noise_speed` | 1 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `9f5f5d235b364ffb973795f1445247f2` | Subsurface scattering color | `_Subsurface_scattering_color` | {r:0,g:0,b:0,a:0} m_ColorMode=1 |
| `2604f9ee7c374740ad9cd4700dd496f9` | Fresenal power | `_Fresenal_power` | 0 m_FloatType=1 m_RangeValues={x:0,y:3} |

**Node ID map (8-char prefix; full IDs are unique in-file):**

| id | type | name | slots (#id:name=t default) / config |
|---|---|---|---|
| `3260b929` | BlockNode | VertexDescription.Position | #0:Position={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Position" |
| `191412bf` | BlockNode | VertexDescription.Normal | #0:Normal={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Normal" |
| `10fbd35d` | BlockNode | VertexDescription.Tangent | #0:Tangent={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Tangent" |
| `3ce90ef3` | BlockNode | SurfaceDescription.BaseColor | #0:Base Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.BaseColor" |
| `df0ad3ee` | BlockNode | SurfaceDescription.Alpha | #0:Alpha=1 m_SerializedDescriptor="SurfaceDescription.Alpha" |
| `ce1c25c3` | BlockNode | SurfaceDescription.AlphaClipThreshold | #0:Alpha Clip Threshold=0.5 m_SerializedDescriptor="SurfaceDescription.AlphaClipThreshold" |
| `6355f41e` | PropertyNode | Property | #0:Color={x:0,y:0,z:0,w:0} m_Property={m_Id:"58cc14cb95814ffe956731c334bfd302"} |
| `608b64f1` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `2e243342` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `5a4d33ab` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `f15c5cb5` | PropertyNode | Property | #0:Noise Amplitute=0 m_Property={m_Id:"4c151227987a47afa6b26a1903c72a8d"} |
| `c9d46319` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `58a4c0f7` | TransformNode | Transform | #0:In={x:0,y:0,z:0} #1:Out={x:0,y:0,z:0} m_Conversion={from:2,to:0} m_ConversionType=0 m_Normalize=True m_Conversion={from:2,to:0} |
| `4fa12012` | GradientNoiseNode | Gradient Noise | #0:UV={x:0,y:0} #1:Scale=10 #2:Out=0 m_HashType=1 |
| `0af40223` | TilingAndOffsetNode | Tiling And Offset | #0:UV={x:0,y:0} #1:Tiling={x:1,y:1} #2:Offset={x:0,y:0} #3:Out={x:0,y:0}  |
| `9fa9c4f6` | TimeNode | Time | #0:Time=0 #1:Sine Time=0 #2:Cosine Time=0 #3:Delta Time=0 #4:Smooth Delta=0  |
| `f145a5aa` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `97173621` | PropertyNode | Property | #0:Noise speed=0 m_Property={m_Id:"47bf4ba5cea44d26b67b5ff0851f933c"} |
| `02e4d09c` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=0 m_PositionSource=0 |
| `289a2533` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `7e024b3c` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `fc6e9386` | PropertyNode | Property | #0:Noise Scale=0 m_Property={m_Id:"c57dcec76e144cd8adf3bf48ddec8680"} |
| `5e5b23de` | PropertyNode | Property | #0:Subsurface scattering color={x:0,y:0,z:0,w:0} m_Property={m_Id:"9f5f5d235b364ffb973795f1445247f2"} |
| `11ad037f` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `01c720c9` | FresnelNode | Fresnel Effect | #0:Normal={x:0,y:0,z:0} #1:View Dir={x:0,y:0,z:0} #2:Power=1 #3:Out=0  |
| `4b57b54e` | PropertyNode | Property | #0:Fresenal power=0 m_Property={m_Id:"2604f9ee7c374740ad9cd4700dd496f9"} |
| `9be05dc6` | MainLightDirectionNode | Main Light Direction | #0:Direction={x:0,y:0,z:0}  |
| `6dac4e13` | NormalVectorNode | Normal Vector | #0:Out={x:0,y:0,z:1} m_Space=2 |
| `4b9a49e9` | DotProductNode | Dot Product | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:1,z:0,w:0} #2:Out=0  |
| `be3d7dd7` | SaturateNode | Saturate | #0:In={x:0,y:0,z:0,w:0} #1:Out={x:0,y:0,z:0,w:0}  |
| `f5bfde03` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `47584180` | BlockNode | SurfaceDescription.Smoothness | #0:Smoothness=0.5 m_SerializedDescriptor="SurfaceDescription.Smoothness" |
| `924ec0d3` | BlockNode | SurfaceDescription.NormalTS | #0:Normal (Tangent Space)={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.NormalTS" |
| `4ceafe4a` | BlockNode | SurfaceDescription.Emission | #0:Emission={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.Emission" |
| `7b52be58` | BlockNode | SurfaceDescription.Occlusion | #0:Ambient Occlusion=1 m_SerializedDescriptor="SurfaceDescription.Occlusion" |
| `06465aad` | BlockNode | SurfaceDescription.Specular | #0:Specular Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.Specular" |
| `cb1acea4` | BlockNode | SurfaceDescription.Metallic | #0:Metallic=0 m_SerializedDescriptor="SurfaceDescription.Metallic" |


**Edge trace (output → input; slot names from material slots):**

```
01c720c9.3:Out -> f5bfde03.1:B  (Fresnel Effect => Multiply)
02e4d09c.0:Out -> 7e024b3c.0:A  (Position => Multiply)
0af40223.3:Out -> 4fa12012.0:UV  (Tiling And Offset => Gradient Noise)
11ad037f.3:Out -> 3ce90ef3.0:Base Color  (Lerp => SurfaceDescription.BaseColor)
289a2533.2:Out -> 0af40223.2:Offset  (Add => Tiling And Offset)
2e243342.0:Out -> c9d46319.0:A  (Position => Add)
4b57b54e.0:Fresenal power -> 01c720c9.2:Power  (Property => Fresnel Effect)
4b9a49e9.2:Out -> be3d7dd7.0:In  (Dot Product => Saturate)
4fa12012.2:Out -> 5a4d33ab.0:A  (Gradient Noise => Multiply)
58a4c0f7.1:Out -> 3260b929.0:Position  (Transform => VertexDescription.Position)
5a4d33ab.2:Out -> c9d46319.1:B  (Multiply => Add)
5e5b23de.0:Subsurface scattering color -> 11ad037f.1:B  (Property => Lerp)
608b64f1.4:A -> df0ad3ee.0:Alpha  (Split => SurfaceDescription.Alpha)
6355f41e.0:Color -> 11ad037f.0:A  (Property => Lerp)
6355f41e.0:Color -> 608b64f1.0:In  (Property => Split)
6dac4e13.0:Out -> 4b9a49e9.1:B  (Normal Vector => Dot Product)
7e024b3c.2:Out -> 289a2533.0:A  (Multiply => Add)
97173621.0:Noise speed -> f145a5aa.1:B  (Property => Multiply)
9be05dc6.0:Direction -> 4b9a49e9.0:A  (Main Light Direction => Dot Product)
9fa9c4f6.0:Time -> f145a5aa.0:A  (Time => Multiply)
be3d7dd7.1:Out -> f5bfde03.0:A  (Saturate => Multiply)
c9d46319.2:Out -> 58a4c0f7.0:In  (Add => Transform)
f145a5aa.2:Out -> 289a2533.1:B  (Multiply => Add)
f15c5cb5.0:Noise Amplitute -> 5a4d33ab.1:B  (Property => Multiply)
f5bfde03.2:Out -> 11ad037f.2:T  (Multiply => Lerp)
fc6e9386.0:Noise Scale -> 7e024b3c.1:B  (Property => Multiply)
```


### Flower

**Properties (m_ObjectId → ref name, default):**

| full ID | name | ref | default / flags |
|---|---|---|---|
| `d918d2af7b60442e8881d25d85733007` | Noise Scale | `_Noise_Scale` | 2 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `967f965508ab425d9fff4e34214030de` | Noise amplitute | `_Noise_amplitute` | 0.5 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `79c2d0b8fe064b269d26fdf0039cbca6` | Noise speed | `_Noise_speed` | {x:2,y:0,z:0,w:0}  |
| `8baa1f22f4b54604854bc92e55d9644c` | Colour pallate | `_Colour_pallate` | {m_SerializedTexture:"{"texture":{"fileID":2800000,"guid":"d56f8ee12d992",m_Guid:""} m_DefaultType=0 |
| `d560585c5d184152b49255fa033a1f17` | Blend color | `_Blend_color` | {r:1,g:1,b:1,a:0} m_ColorMode=0 |
| `c3f15e3e2ef349f0a8652252c72ecf6e` | Smoothness | `_Smoothness` | 0 m_FloatType=1 m_RangeValues={x:0,y:1} |

**Node ID map (8-char prefix; full IDs are unique in-file):**

| id | type | name | slots (#id:name=t default) / config |
|---|---|---|---|
| `c5d9f77e` | BlockNode | VertexDescription.Position | #0:Position={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Position" |
| `bddd875d` | BlockNode | VertexDescription.Normal | #0:Normal={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Normal" |
| `7dabc771` | BlockNode | VertexDescription.Tangent | #0:Tangent={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Tangent" |
| `0f900ab7` | BlockNode | SurfaceDescription.BaseColor | #0:Base Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.BaseColor" |
| `d7f9b712` | BlockNode | SurfaceDescription.NormalTS | #0:Normal (Tangent Space)={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.NormalTS" |
| `d9c4860f` | BlockNode | SurfaceDescription.Metallic | #0:Metallic=0 m_SerializedDescriptor="SurfaceDescription.Metallic" |
| `046ea15e` | BlockNode | SurfaceDescription.Smoothness | #0:Smoothness=0.5 m_SerializedDescriptor="SurfaceDescription.Smoothness" |
| `61f9c06a` | BlockNode | SurfaceDescription.Emission | #0:Emission={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.Emission" |
| `86603f0f` | BlockNode | SurfaceDescription.Occlusion | #0:Ambient Occlusion=1 m_SerializedDescriptor="SurfaceDescription.Occlusion" |
| `913e6298` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `d6bf2a94` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `600989ca` | TilingAndOffsetNode | Tiling And Offset | #0:UV={x:0,y:0} #1:Tiling={x:1,y:1} #2:Offset={x:0,y:0} #3:Out={x:0,y:0}  |
| `b67933e2` | TimeNode | Time | #0:Time=0 #1:Sine Time=0 #2:Cosine Time=0 #3:Delta Time=0 #4:Smooth Delta=0  |
| `e4fadfa4` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `a9e23f2c` | GradientNoiseNode | Gradient Noise | #0:UV={x:0,y:0} #1:Scale=10 #2:Out=0 m_HashType=0 |
| `bf4a1862` | SubtractNode | Subtract | #0:A={x:1,y:1,z:1,w:1} #1:B={x:0.5,y:1,z:1,w:1} #2:Out={x:0,y:0,z:0,w:0}  |
| `baea78d6` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `842fff86` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `122a65ef` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `e8e91cda` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `5192ca3a` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `8a549c88` | CombineNode | Combine | #0:R=0 #1:G=0 #2:B=0 #3:A=0 #4:RGBA={x:0,y:0,z:0,w:0} #5:RGB={x:0,y:0,z:0} #6:RG={x:0,y:0}  |
| `f2cf77b6` | TransformNode | Transform | #0:In={x:0,y:0,z:0} #1:Out={x:0,y:0,z:0} m_Conversion={from:2,to:0} m_ConversionType=0 m_Normalize=True m_Conversion={from:2,to:0} |
| `cea532b8` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `7c4b8da2` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `ecdefdd6` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `d708c7f9` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=0 m_PositionSource=0 |
| `77504e96` | PropertyNode | Property | #0:Noise Scale=0 m_Property={m_Id:"d918d2af7b60442e8881d25d85733007"} |
| `bd315dee` | PropertyNode | Property | #0:Noise amplitute=0 m_Property={m_Id:"967f965508ab425d9fff4e34214030de"} |
| `f1b65521` | PropertyNode | Property | #0:Noise speed={x:0,y:0} m_Property={m_Id:"79c2d0b8fe064b269d26fdf0039cbca6"} |
| `5f3993e6` | PropertyNode | Property | #0:Colour pallate=None m_Property={m_Id:"8baa1f22f4b54604854bc92e55d9644c"} |
| `45a49aa9` | SampleTexture2DNode | Sample Texture 2D | #0:RGBA={x:0,y:0,z:0,w:0} #4:R=0 #5:G=0 #6:B=0 #7:A=0 #1:Texture=None #2:UV={x:0,y:0} #3:Sampler=None m_TextureType=0 |
| `f50cc44a` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `d2d83666` | PropertyNode | Property | #0:Blend color={x:0,y:0,z:0,w:0} m_Property={m_Id:"d560585c5d184152b49255fa033a1f17"} |
| `5d6aa48c` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `73732f4d` | ObjectNode | Object | #0:Position={x:0,y:0,z:0} #1:Scale={x:0,y:0,z:0} #2:World Bounds Min={x:0,y:0,z:0} #3:World Bounds Max={x:0,y:0,z:0} #4:Bounds Size={x:0,y:0,z:0}  |


**Edge trace (output → input; slot names from material slots):**

```
122a65ef.2:Out -> 8a549c88.0:R  (Add => Combine)
45a49aa9.0:RGBA -> f50cc44a.0:A  (Sample Texture 2D => Multiply)
5192ca3a.2:Out -> 8a549c88.2:B  (Add => Combine)
5d6aa48c.2:Out -> 600989ca.2:Offset  (Add => Tiling And Offset)
5f3993e6.0:Colour pallate -> 45a49aa9.1:Texture  (Property => Sample Texture 2D)
600989ca.3:Out -> a9e23f2c.0:UV  (Tiling And Offset => Gradient Noise)
73732f4d.0:Position -> 5d6aa48c.1:B  (Object => Add)
77504e96.0:Noise Scale -> a9e23f2c.1:Scale  (Property => Gradient Noise)
7c4b8da2.3:Out -> f2cf77b6.0:In  (Lerp => Transform)
842fff86.0:Out -> e8e91cda.0:In  (Position => Split)
8a549c88.5:RGB -> 7c4b8da2.1:B  (Combine => Lerp)
913e6298.2:G -> 7c4b8da2.2:T  (Split => Lerp)
a9e23f2c.2:Out -> bf4a1862.0:A  (Gradient Noise => Subtract)
b67933e2.0:Time -> e4fadfa4.0:A  (Time => Multiply)
baea78d6.2:Out -> cea532b8.0:A  (Multiply => Multiply)
bd315dee.0:Noise amplitute -> baea78d6.1:B  (Property => Multiply)
bf4a1862.2:Out -> baea78d6.0:A  (Subtract => Multiply)
cea532b8.2:Out -> 122a65ef.1:B  (Multiply => Add)
cea532b8.2:Out -> 5192ca3a.1:B  (Multiply => Add)
d2d83666.0:Blend color -> f50cc44a.1:B  (Property => Multiply)
d6bf2a94.0:Out -> 600989ca.0:UV  (Position => Tiling And Offset)
d708c7f9.0:Out -> 913e6298.0:In  (Position => Split)
e4fadfa4.2:Out -> 5d6aa48c.0:A  (Multiply => Add)
e8e91cda.1:R -> 122a65ef.0:A  (Split => Add)
e8e91cda.2:G -> 8a549c88.1:G  (Split => Combine)
e8e91cda.3:B -> 5192ca3a.0:A  (Split => Add)
ecdefdd6.0:Out -> 7c4b8da2.0:A  (Position => Lerp)
f1b65521.0:Noise speed -> e4fadfa4.1:B  (Property => Multiply)
f2cf77b6.1:Out -> c5d9f77e.0:Position  (Transform => VertexDescription.Position)
f50cc44a.2:Out -> 0f900ab7.0:Base Color  (Multiply => SurfaceDescription.BaseColor)
```


### Grass

**Properties (m_ObjectId → ref name, default):**

| full ID | name | ref | default / flags |
|---|---|---|---|
| `1c8309aaa6884d4fbc376c0b16d6006d` | Noise scale | `_Noise_scale` | 2 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `87bd04a5fe5247c6b23b1d128074d476` | Noise Amplitute | `_Noise_Amplitute` | 0.5 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `70f2226266aa4a0b87297330c17a5041` | Smoothness | `_Smoothness` | 0 m_FloatType=1 m_RangeValues={x:0,y:1} |
| `3bd181a51b214dee938826689a6037f3` | Noise speed | `_Noise_speed` | {x:2,y:0,z:0,w:0}  |
| `a6d68edfdaaa4dc4baff79e35b5d1d53` | top color | `_top_color` | {r:0,g:0,b:0,a:0} m_ColorMode=0 |
| `737c92a2f69e44f7a5a7b7298d5b47de` | base color | `_base_color` | {r:0,g:0,b:0,a:0} m_ColorMode=0 |
| `a8f09216085240759556ea3dfca692cd` | Base noise | `_Base_noise` | 0 m_FloatType=1 m_RangeValues={x:0,y:1} |

**Node ID map (8-char prefix; full IDs are unique in-file):**

| id | type | name | slots (#id:name=t default) / config |
|---|---|---|---|
| `d45b179d` | BlockNode | VertexDescription.Position | #0:Position={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Position" |
| `ea4af778` | BlockNode | VertexDescription.Normal | #0:Normal={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Normal" |
| `0d86761f` | BlockNode | VertexDescription.Tangent | #0:Tangent={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Tangent" |
| `0fdb41fa` | BlockNode | SurfaceDescription.BaseColor | #0:Base Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.BaseColor" |
| `68fcfd30` | BlockNode | SurfaceDescription.NormalTS | #0:Normal (Tangent Space)={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.NormalTS" |
| `93054f23` | BlockNode | SurfaceDescription.Metallic | #0:Metallic=0 m_SerializedDescriptor="SurfaceDescription.Metallic" |
| `6fe6bb70` | BlockNode | SurfaceDescription.Smoothness | #0:Smoothness=0 m_SerializedDescriptor="SurfaceDescription.Smoothness" |
| `25316d0a` | BlockNode | SurfaceDescription.Emission | #0:Emission={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.Emission" |
| `70f324a6` | BlockNode | SurfaceDescription.Occlusion | #0:Ambient Occlusion=1 m_SerializedDescriptor="SurfaceDescription.Occlusion" |
| `af92527e` | PropertyNode | Property | #0:Smoothness=0 m_Property={m_Id:"70f2226266aa4a0b87297330c17a5041"} |
| `6862eb5d` | BlockNode | SurfaceDescription.Specular | #0:Specular Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.Specular" |
| `315c5e63` | BlockNode | SurfaceDescription.Alpha | #0:Alpha=1 m_SerializedDescriptor="SurfaceDescription.Alpha" |
| `0acbfd2d` | BlockNode | SurfaceDescription.AlphaClipThreshold | #0:Alpha Clip Threshold=0.5 m_SerializedDescriptor="SurfaceDescription.AlphaClipThreshold" |
| `3f4f3c56` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=0 m_PositionSource=0 |
| `6a85a986` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `c95fb042` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `10b0e56e` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `375e7619` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `bd959200` | TimeNode | Time | #0:Time=0 #1:Sine Time=0 #2:Cosine Time=0 #3:Delta Time=0 #4:Smooth Delta=0  |
| `a732e52b` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `3c1138d9` | PropertyNode | Property | #0:Noise scale=0 m_Property={m_Id:"1c8309aaa6884d4fbc376c0b16d6006d"} |
| `416c154b` | PropertyNode | Property | #0:Noise speed={x:0,y:0} m_Property={m_Id:"3bd181a51b214dee938826689a6037f3"} |
| `63d6f8d6` | SubtractNode | Subtract | #0:A={x:1,y:1,z:1,w:1} #1:B={x:0.5,y:1,z:1,w:1} #2:Out={x:0,y:0,z:0,w:0}  |
| `a10f3f76` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `2694daf8` | PropertyNode | Property | #0:Noise Amplitute=0 m_Property={m_Id:"87bd04a5fe5247c6b23b1d128074d476"} |
| `5b63c695` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `19cabf22` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `01bd9234` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `4d2b488b` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `2be22b54` | CombineNode | Combine | #0:R=0 #1:G=0 #2:B=0 #3:A=0 #4:RGBA={x:0,y:0,z:0,w:0} #5:RGB={x:0,y:0,z:0} #6:RG={x:0,y:0}  |
| `37baef8e` | TransformNode | Transform | #0:In={x:0,y:0,z:0} #1:Out={x:0,y:0,z:0} m_Conversion={from:2,to:0} m_ConversionType=0 m_Normalize=True m_Conversion={from:2,to:0} |
| `cf20e7ea` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `a554c0e9` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `753f80fd` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `ea03506f` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=0 m_PositionSource=0 |
| `b22fc214` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `3aec412d` | PropertyNode | Property | #0:top color={x:0,y:0,z:0,w:0} m_Property={m_Id:"a6d68edfdaaa4dc4baff79e35b5d1d53"} |
| `853fde68` | PropertyNode | Property | #0:base color={x:0,y:0,z:0,w:0} m_Property={m_Id:"737c92a2f69e44f7a5a7b7298d5b47de"} |
| `dc3beb6f` | GradientNoiseNode | Gradient Noise | #0:UV={x:0,y:0} #1:Scale=10 #2:Out=0 m_HashType=0 |
| `88dc4292` | TilingAndOffsetNode | Tiling And Offset | #0:UV={x:0,y:0} #1:Tiling={x:1,y:1} #2:Offset={x:0,y:0} #3:Out={x:0,y:0}  |
| `05c313f6` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `e3141778` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=2 m_PositionSource=0 |
| `54b73a70` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `912ce3ea` | PropertyNode | Property | #0:Base noise=0 m_Property={m_Id:"a8f09216085240759556ea3dfca692cd"} |


**Edge trace (output → input; slot names from material slots):**

```
01bd9234.1:R -> 19cabf22.0:A  (Split => Add)
01bd9234.2:G -> 2be22b54.1:G  (Split => Combine)
01bd9234.3:B -> 4d2b488b.0:A  (Split => Add)
05c313f6.2:Out -> 88dc4292.2:Offset  (Add => Tiling And Offset)
10b0e56e.2:G -> a554c0e9.2:T  (Split => Lerp)
10b0e56e.2:G -> b22fc214.2:T  (Split => Lerp)
19cabf22.2:Out -> 2be22b54.0:R  (Add => Combine)
2694daf8.0:Noise Amplitute -> a10f3f76.1:B  (Property => Multiply)
2be22b54.5:RGB -> a554c0e9.1:B  (Combine => Lerp)
375e7619.0:Out -> 88dc4292.0:UV  (Position => Tiling And Offset)
37baef8e.1:Out -> d45b179d.0:Position  (Transform => VertexDescription.Position)
3aec412d.0:top color -> b22fc214.1:B  (Property => Lerp)
3c1138d9.0:Noise scale -> dc3beb6f.1:Scale  (Property => Gradient Noise)
3f4f3c56.0:Out -> c95fb042.0:In  (Position => Split)
416c154b.0:Noise speed -> a732e52b.1:B  (Property => Multiply)
4d2b488b.2:Out -> 2be22b54.2:B  (Add => Combine)
54b73a70.2:Out -> 05c313f6.1:B  (Multiply => Add)
5b63c695.0:Out -> 01bd9234.0:In  (Position => Split)
63d6f8d6.2:Out -> a10f3f76.0:A  (Subtract => Multiply)
6a85a986.2:Out -> 6fe6bb70.0:Smoothness  (Multiply => SurfaceDescription.Smoothness)
753f80fd.0:Out -> a554c0e9.0:A  (Position => Lerp)
853fde68.0:base color -> b22fc214.0:A  (Property => Lerp)
88dc4292.3:Out -> dc3beb6f.0:UV  (Tiling And Offset => Gradient Noise)
912ce3ea.0:Base noise -> 54b73a70.1:B  (Property => Multiply)
a10f3f76.2:Out -> cf20e7ea.0:A  (Multiply => Multiply)
a554c0e9.3:Out -> 37baef8e.0:In  (Lerp => Transform)
a732e52b.2:Out -> 05c313f6.0:A  (Multiply => Add)
af92527e.0:Smoothness -> 6a85a986.1:B  (Property => Multiply)
b22fc214.3:Out -> 0fdb41fa.0:Base Color  (Lerp => SurfaceDescription.BaseColor)
bd959200.0:Time -> a732e52b.0:A  (Time => Multiply)
c95fb042.2:G -> 6a85a986.0:A  (Split => Multiply)
cf20e7ea.2:Out -> 19cabf22.1:B  (Multiply => Add)
cf20e7ea.2:Out -> 4d2b488b.1:B  (Multiply => Add)
dc3beb6f.2:Out -> 63d6f8d6.0:A  (Gradient Noise => Subtract)
e3141778.0:Out -> 54b73a70.0:A  (Position => Multiply)
ea03506f.0:Out -> 10b0e56e.0:In  (Position => Split)
```


### Heat Distortion

**Properties (m_ObjectId → ref name, default):**

| full ID | name | ref | default / flags |
|---|---|---|---|
| `4a99b86105f245449c50bd73c0fbe346` | noise stregth | `_noise_stregth` | 0 m_FloatType=1 m_RangeValues={x:0,y:1} |
| `527eddb23a6747d59eaaa3f072a559cf` | Noise mask | `_Noise_mask` | {m_SerializedTexture:"{"texture":{"fileID":10300,"guid":"000000000000000",m_Guid:""} m_DefaultType=0 |
| `ca291e8422944af49d96321b0b88a79c` | Noise Scale | `_Noise_Scale` | 50 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `ff36d86751e2411397a41214a02daac2` | Noise speed | `_Noise_speed` | {x:0,y:0,z:0,w:0}  |

**Node ID map (8-char prefix; full IDs are unique in-file):**

| id | type | name | slots (#id:name=t default) / config |
|---|---|---|---|
| `ba14f4c0` | BlockNode | VertexDescription.Position | #0:Position={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Position" |
| `7e422952` | BlockNode | VertexDescription.Normal | #0:Normal={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Normal" |
| `11456a68` | BlockNode | VertexDescription.Tangent | #0:Tangent={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Tangent" |
| `51990020` | BlockNode | SurfaceDescription.BaseColor | #0:Base Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.BaseColor" |
| `dca0abb9` | ObjectNode | Object | #0:Position={x:0,y:0,z:0} #1:Scale={x:0,y:0,z:0} #2:World Bounds Min={x:0,y:0,z:0} #3:World Bounds Max={x:0,y:0,z:0} #4:Bounds Size={x:0,y:0,z:0}  |
| `8943b1b1` | PositionNode | Position | #0:Out={x:0,y:0,z:0} m_Space=0 m_PositionSource=0 |
| `6e98849e` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `adf5323e` | TransformationMatrixNode | Transformation Matrix | #0:Out=matrix m_matrix=-1 m_MatrixType=3 |
| `9d6577b6` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `0e574d6c` | SplitNode | Split | #0:In={x:0,y:0,z:0,w:0} #1:R=0 #2:G=0 #3:B=0 #4:A=0  |
| `559c64fc` | Vector4Node | Vector 4 | #1:X=0 #2:Y=0 #3:Z=0 #4:W=0 #0:Out={x:0,y:0,z:0,w:0} m_Value={x:0,y:0,z:0,w:0} |
| `3fa0fc29` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `33d9e4da` | RedirectNodeData | Redirect Node | #0:={x:0,y:0,z:0,w:0} #1:={x:0,y:0,z:0,w:0}  |
| `1b754f00` | TransformNode | Transform | #0:In={x:0,y:0,z:0} #1:Out={x:0,y:0,z:0} m_Conversion={from:2,to:0} m_ConversionType=0 m_Normalize=True m_Conversion={from:2,to:0} |
| `f74f5523` | SceneColorNode | Scene Color | #0:UV={x:0,y:0,z:0,w:0} #1:Out={x:0,y:0,z:0}  |
| `a80c84a2` | BlockNode | SurfaceDescription.Alpha | #0:Alpha=1 m_SerializedDescriptor="SurfaceDescription.Alpha" |
| `cbe4e427` | BlockNode | SurfaceDescription.AlphaClipThreshold | #0:Alpha Clip Threshold=0.5 m_SerializedDescriptor="SurfaceDescription.AlphaClipThreshold" |
| `eab70534` | ScreenPositionNode | Screen Position | #0:Out={x:0,y:0,z:0,w:0} m_ScreenSpaceType=0 |
| `9996650e` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `1fd3d6bd` | PropertyNode | Property | #0:noise stregth=0 m_Property={m_Id:"4a99b86105f245449c50bd73c0fbe346"} |
| `7c959616` | NoiseNode | Simple Noise | #0:UV={x:0,y:0} #1:Scale=1 #2:Out=0 m_HashType=0 |
| `690e2d41` | PropertyNode | Property | #0:Noise mask=None m_Property={m_Id:"527eddb23a6747d59eaaa3f072a559cf"} |
| `c8ae43d7` | SampleTexture2DNode | Sample Texture 2D | #0:RGBA={x:0,y:0,z:0,w:0} #4:R=0 #5:G=0 #6:B=0 #7:A=0 #1:Texture=None #2:UV={x:0,y:0} #3:Sampler=None m_TextureType=0 |
| `60bbd4ce` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `04b53701` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `4841775e` | PropertyNode | Property | #0:Noise Scale=0 m_Property={m_Id:"ca291e8422944af49d96321b0b88a79c"} |
| `42663009` | SubGraphNode | texture movement | #-1991933760:Scale={x:1,y:1} #-223675099:Speed={x:0,y:0} #1:Out_Vector4={x:0,y:0}  |
| `1deaa18f` | PropertyNode | Property | #0:Noise speed={x:0,y:0} m_Property={m_Id:"ff36d86751e2411397a41214a02daac2"} |


**Edge trace (output → input; slot names from material slots):**

```
04b53701.2:Out -> 9996650e.1:B  (Add => Lerp)
0e574d6c.1:R -> 559c64fc.1:X  (Split => Vector 4)
0e574d6c.2:G -> 559c64fc.2:Y  (Split => Vector 4)
0e574d6c.3:B -> 559c64fc.3:Z  (Split => Vector 4)
1b754f00.1:Out -> ba14f4c0.0:Position  (Transform => VertexDescription.Position)
1deaa18f.0:Noise speed -> 42663009.-223675099:Speed  (Property => texture movement)
1fd3d6bd.0:noise stregth -> 9996650e.2:T  (Property => Lerp)
33d9e4da.1: -> 3fa0fc29.1:B  (Redirect Node => Add)
3fa0fc29.2:Out -> 1b754f00.0:In  (Add => Transform)
42663009.1:Out_Vector4 -> 7c959616.0:UV  (texture movement => Simple Noise)
4841775e.0:Noise Scale -> 42663009.-1991933760:Scale  (Property => texture movement)
559c64fc.0:Out -> 9d6577b6.1:B  (Vector 4 => Multiply)
60bbd4ce.2:Out -> 04b53701.1:B  (Multiply => Add)
690e2d41.0:Noise mask -> c8ae43d7.1:Texture  (Property => Sample Texture 2D)
6e98849e.2:Out -> 0e574d6c.0:In  (Multiply => Split)
7c959616.2:Out -> 60bbd4ce.1:B  (Simple Noise => Multiply)
8943b1b1.0:Out -> 6e98849e.0:A  (Position => Multiply)
9996650e.3:Out -> f74f5523.0:UV  (Lerp => Scene Color)
9d6577b6.2:Out -> 3fa0fc29.0:A  (Multiply => Add)
adf5323e.0:Out -> 9d6577b6.0:A  (Transformation Matrix => Multiply)
c8ae43d7.7:A -> 60bbd4ce.0:A  (Sample Texture 2D => Multiply)
dca0abb9.0:Position -> 33d9e4da.0:  (Object => Redirect Node)
dca0abb9.1:Scale -> 6e98849e.1:B  (Object => Multiply)
eab70534.0:Out -> 04b53701.0:A  (Screen Position => Add)
eab70534.0:Out -> 9996650e.0:A  (Screen Position => Lerp)
f74f5523.1:Out -> 51990020.0:Base Color  (Scene Color => SurfaceDescription.BaseColor)
```


### TriPlane

**Properties (m_ObjectId → ref name, default):**

| full ID | name | ref | default / flags |
|---|---|---|---|
| `406864064ec049e2b3687dc735e3d87b` | BaseColor | `_BaseColor` | {r:0.4811,g:0.4811,b:0.4811,a:0} m_ColorMode=0 |
| `fda83dcce8d34a7ba5985e6692a9ef11` | SecondaryColor | `_SecondaryColor` | {r:0,g:0.8679,b:0.1323,a:0} m_ColorMode=0 |
| `2ee6f701b01c4d10ad2c0207f749a421` | Tri Planer Direction | `_Tri_Planer_Direction` | {x:0,y:1,z:0,w:0}  |
| `566440713837483eb14fbdcb481fc04a` | step Edge | `_step_Edge` | 0.3 m_FloatType=0 m_RangeValues={x:0,y:1} |
| `27a5ffc3a8924a5987d595686e1c5c39` | Is sun move | `_Is_sun_move` | False  |
| `26253042da9a4ef78a93e643adf19734` | Smoothness | `_Smoothness` | 0 m_FloatType=1 m_RangeValues={x:0,y:1} |

**Node ID map (8-char prefix; full IDs are unique in-file):**

| id | type | name | slots (#id:name=t default) / config |
|---|---|---|---|
| `8a69f160` | BlockNode | VertexDescription.Position | #0:Position={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Position" |
| `4b2e7fec` | BlockNode | VertexDescription.Normal | #0:Normal={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Normal" |
| `d0d1edff` | BlockNode | VertexDescription.Tangent | #0:Tangent={x:0,y:0,z:0} m_SerializedDescriptor="VertexDescription.Tangent" |
| `61c71239` | BlockNode | SurfaceDescription.BaseColor | #0:Base Color={x:0.5,y:0.5,z:0.5} m_SerializedDescriptor="SurfaceDescription.BaseColor" |
| `f19a4b6a` | BlockNode | SurfaceDescription.NormalTS | #0:Normal (Tangent Space)={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.NormalTS" |
| `3e199d4a` | BlockNode | SurfaceDescription.Metallic | #0:Metallic=0 m_SerializedDescriptor="SurfaceDescription.Metallic" |
| `f0efbd7b` | BlockNode | SurfaceDescription.Smoothness | #0:Smoothness=0.5 m_SerializedDescriptor="SurfaceDescription.Smoothness" |
| `557d6829` | BlockNode | SurfaceDescription.Emission | #0:Emission={x:0,y:0,z:0} m_SerializedDescriptor="SurfaceDescription.Emission" |
| `02d4b625` | BlockNode | SurfaceDescription.Occlusion | #0:Ambient Occlusion=1 m_SerializedDescriptor="SurfaceDescription.Occlusion" |
| `731c561c` | NormalVectorNode | Normal Vector | #0:Out={x:0,y:0,z:1} m_Space=2 |
| `607b2245` | DotProductNode | Dot Product | #0:A={x:0,y:1,z:0,w:0} #1:B={x:0,y:1,z:0,w:0} #2:Out=0  |
| `82ec9549` | SaturateNode | Saturate | #0:In={x:0,y:0,z:0,w:0} #1:Out={x:0,y:0,z:0,w:0}  |
| `89a4cb8a` | PropertyNode | Property | #0:Tri Planer Direction={x:0,y:0,z:0} m_Property={m_Id:"2ee6f701b01c4d10ad2c0207f749a421"} |
| `8f61d20a` | StepNode | Step | #0:Edge={x:0.57,y:1,z:1,w:1} #1:In={x:0.22,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `5df8b08c` | PropertyNode | Property | #0:step Edge=0 m_Property={m_Id:"566440713837483eb14fbdcb481fc04a"} |
| `294b711f` | LerpNode | Lerp | #0:A={x:0,y:0,z:0,w:0} #1:B={x:1,y:1,z:1,w:1} #2:T={x:0,y:0,z:0,w:0} #3:Out={x:0,y:0,z:0,w:0}  |
| `2e280c56` | PropertyNode | Property | #0:BaseColor={x:0,y:0,z:0,w:0} m_Property={m_Id:"406864064ec049e2b3687dc735e3d87b"} |
| `05088f80` | PropertyNode | Property | #0:SecondaryColor={x:0,y:0,z:0,w:0} m_Property={m_Id:"fda83dcce8d34a7ba5985e6692a9ef11"} |
| `472f9c9e` | MainLightDirectionNode | Main Light Direction | #0:Direction={x:0,y:0,z:0}  |
| `3c9c9179` | NormalVectorNode | Normal Vector | #0:Out={x:0,y:0,z:1} m_Space=2 |
| `44c157c4` | DotProductNode | Dot Product | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:1,z:0,w:0} #2:Out=0  |
| `24e6c641` | NegateNode | Negate | #0:In={x:0,y:0,z:0,w:0} #1:Out={x:0,y:0,z:0,w:0}  |
| `31d0cb50` | SaturateNode | Saturate | #0:In={x:0,y:0,z:0,w:0} #1:Out={x:0,y:0,z:0,w:0}  |
| `c1f6a389` | MultiplyNode | Multiply | #0:A=matrix #1:B=matrix #2:Out=matrix  |
| `f31ec76f` | PropertyNode | Property | #0:Is sun move=False m_Property={m_Id:"27a5ffc3a8924a5987d595686e1c5c39"} |
| `5a75911a` | AddNode | Add | #0:A={x:0,y:0,z:0,w:0} #1:B={x:0,y:0,z:0,w:0} #2:Out={x:0,y:0,z:0,w:0}  |
| `9113dfc2` | PropertyNode | Property | #0:Smoothness=0 m_Property={m_Id:"26253042da9a4ef78a93e643adf19734"} |


**Edge trace (output → input; slot names from material slots):**

```
05088f80.0:SecondaryColor -> 294b711f.1:B  (Property => Lerp)
24e6c641.1:Out -> 44c157c4.0:A  (Negate => Dot Product)
294b711f.3:Out -> 61c71239.0:Base Color  (Lerp => SurfaceDescription.BaseColor)
2e280c56.0:BaseColor -> 294b711f.0:A  (Property => Lerp)
31d0cb50.1:Out -> 5a75911a.1:B  (Saturate => Add)
3c9c9179.0:Out -> 44c157c4.1:B  (Normal Vector => Dot Product)
44c157c4.2:Out -> 31d0cb50.0:In  (Dot Product => Saturate)
472f9c9e.0:Direction -> 24e6c641.0:In  (Main Light Direction => Negate)
5a75911a.2:Out -> c1f6a389.0:A  (Add => Multiply)
5df8b08c.0:step Edge -> 8f61d20a.0:Edge  (Property => Step)
607b2245.2:Out -> 82ec9549.0:In  (Dot Product => Saturate)
731c561c.0:Out -> 607b2245.1:B  (Normal Vector => Dot Product)
82ec9549.1:Out -> c1f6a389.1:B  (Saturate => Multiply)
89a4cb8a.0:Tri Planer Direction -> 607b2245.0:A  (Property => Dot Product)
8f61d20a.2:Out -> 294b711f.2:T  (Step => Lerp)
9113dfc2.0:Smoothness -> f0efbd7b.0:Smoothness  (Property => SurfaceDescription.Smoothness)
c1f6a389.2:Out -> 8f61d20a.1:In  (Multiply => Step)
f31ec76f.0:Is sun move -> 5a75911a.0:A  (Property => Add)
```

