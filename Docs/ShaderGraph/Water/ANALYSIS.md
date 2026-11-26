# Water shader graph analysis (detective report)

Scope: `Docs/ShaderGraph/Water/` owns five ShaderGraph JSON files and one
hand-written HLSL shader. Nothing here touches engine code, importers, or builds.
Companions: `NODES.md`, `CONFLICTS.md`, and six `.slang` reference files.

## 1. Sources and graph sizes

| File | Kind | JSON objects | `GraphData.m_Nodes` | edges | properties | blocks |
|---|---|---:|---:|---:|---:|---:|
| `Shaders/water.shadergraph` | URP Lit graph | 384 | 98 | 102 | 19 | 12 |
| `Shaders/SubGraphs/Sinewave.shadersubgraph` | subgraph | 87 | 24 | 27 | 2 | 0 |
| `Shaders/SubGraphs/depthfade.shadersubgraph` | subgraph | 36 | 10 | 9 | 2 | 0 |
| `Shaders/SubGraphs/texture movement.shadersubgraph` | subgraph | 25 | 6 | 5 | 2 | 0 |
| `Shaders/SubGraphs/Normal map.shadersubgraph` | subgraph | 32 | 7 | 7 | 2 | 0 |
| `Shaders/Water/WaterClipmap.shader` (external originals folder) | hand-written HLSL, **not** a graph | — | — | — | 60 ShaderLab props | — |

Method: the JSON is *concatenated* top-level objects, so it was parsed with
`json.JSONDecoder.raw_decode` in a loop (`_tools/trace.py`). Every object's
`m_ObjectId` was indexed; every `{ "m_Id": ... }` array was resolved to edges.
Slot IDs are the slot objects' `m_Id` integers (negative values are legal),
not their array index.

## 2. `water.shadergraph`

### 2.1 Block node ID table

| Node ID (short) | Descriptor | Connected from | Confidence / note |
|---|---|---|---|
| `305a3b86` | VertexDescription.Position | `9295cd68` Transform | H — world→object, displaced Y |
| `b9202a36` | VertexDescription.Normal | *unconnected* | H — default (0,0,0) |
| `15f12986` | VertexDescription.Tangent | *unconnected* | H — default (0,0,0) |
| `b671fb55` | SurfaceDescription.BaseColor | `9551f005` Lerp | H — fog-lerped final colour |
| `0e9800bf` | SurfaceDescription.NormalTS | `e4911d87` Saturate | H — `saturate(normal * strength)` |
| `c40f9618` | SurfaceDescription.Smoothness | `c9f39eb6` Saturate | H — `saturate(step(view.y) * smotness)` |
| `ab68fae1` | SurfaceDescription.Metallic | *unconnected* | H — literal 0 |
| `9318fffb` | SurfaceDescription.Specular | *unconnected* | H — literal 0 |
| `901830a2` | SurfaceDescription.Emission | *unconnected* | H — literal 0 |
| `339bb5b7` | SurfaceDescription.Occlusion | *unconnected* | H — literal 1 |
| `e361c3a0` | SurfaceDescription.Alpha | *unconnected* | H — literal 1 |
| `dcb78918` | SurfaceDescription.AlphaClipThreshold | *unconnected* | H — literal 0.5, clipping disabled by target |

Target objects: `UniversalTarget eb73430e` (SurfaceType 1 = transparent,
AlphaMode 0, ZTest 4, ZWrite 0, RenderFace 2, AlphaClip false) and
`UniversalLitSubTarget 65e7d76c` (WorkflowMode 0). Enum meanings are M confidence.

### 2.2 Property node / property ID table

| Property ID | Name | Type | Serialized default | Property node ID → consumer |
|---|---|---|---|---|
| `ccf300d3` | Deepness | float | 0 | `2150166f` → `66ad73dd` Distance |
| `ca7bea10` | Shallow color | Color | (0.171,0.516,0.802,0) | `7a9435a4` → `b164de58` A |
| `37e550de` | deep color | Color | 0 | `9a9532d8` → `b164de58` B |
| `182828f7` | Refraction Speed | float | 1 | `6a958677` → `1729c992`, `437edb25` |
| `97696c14` | Refraction Scale | Vector2 | (1,-1) | `486bad7d` → `8ef80351` |
| `3f3dd145` | Foam Amount | float | 250 | `59fc32d0` → `5e469230` Distance |
| `3a40e46a` | Foam cutout | float | 0 | `b5cfd621` → `4fda3dbe` B |
| `d9091454` | foam scale | float | 1 | `79fafe44` → `f02cc53e` Scale |
| `28c36b44` | foam speed | float | 0 | `25b4a5d5` → `f02cc53e` Speed |
| `6e19e646` | Foam color | Color | 0 | `2db4a604` → `b7f1e9a1` A; `49910d58` → `f7a7c8df` B |
| `a6c8926e` | speed | float | 1 | `4e88d28e` → `4f2b931b` B |
| `4090c200` | length | float | 1 | `e318935b` → both Sinewave instances |
| `c8154bc2` | amplitute | float | 0 | `62fbbc81` → `25c5048e` B |
| `49b3e42c` | Strength | float | 0 | `6e6fbe73` → `66ad73dd` Strength |
| `56b900bd` | normal strength | float | 1 | `da6f6db1` → `d139571a` B |
| `6cf8a396` | smotness | float | 0 | `a33c2611` → `7f7b02cc` B |
| `c618ce83` | Refraction stength | float | 0 | `c998f259` → `79c5fa28` A |
| `04e26577` | Horizon color | Color | 0 | `159284d8` → `dffd260b` B |
| `0c2d5241` | Horizon Distance | float | 0 | `d2bd3ed3` → `2e6e4697` Power |

The converted material `Matereals/Water shader.mat` has **empty** property maps
(no float/colour/texture overrides), so the graph defaults above are what ships.

### 2.3 SubGraphNode ID table

| Node ID | Name | `m_SerializedSubGraph.guid` | Resolved file | Notable edges |
|---|---|---|---|---|
| `0131bb1a` | Normal map | `a809a161…` | `Normal map` | UV ← `6c54bdc7`; normal → `b0808911` B |
| `14a6acdb` | Normal map | same | `Normal map` | UV ← `bee82d22`; normal → `88ab288c` B |
| `5378309f` | Normal map | same | `Normal map` | UV ← `8ff6f95c`; normal → `b0808911` A |
| `a3e40612` | Normal map | same | `Normal map` | UV ← `6ec45782`; normal → `88ab288c` A |
| `129d00de` | Sinewave | `eb89f1ec…` | `Sinewave` | position ← `8f06e6fd` (world X + phase); wavelength ← `length` |
| `ca5da90f` | Sinewave | same | `Sinewave` | position ← `9c6400eb` (world Z + phase); wavelength ← `length` |
| `5e469230` | depthfade | `8046a77d…` | `depthfade` | Distance ← Foam Amount; result → `4fda3dbe` |
| `66ad73dd` | depthfade | same | `depthfade` | Strength/Deepness → `8ad52966` → colour + normal strength |
| `6c54bdc7`, `6ec45782`, `8ff6f95c`, `bee82d22`, `f02cc53e` | texture movement | `f29aaf50…` | `texture movement` | feed the four normals and the foam noise UV |

The parent instances also carry stale property GUIDs `c2c894f2` and `15ab56ef`
which have no matching slot or property in the subgraph file — consistent with
the shared `Docs/ShaderGraph/Shared/SUBGRAPH_INDEX.md` finding.

### 2.4 Edge traces (short form)

**Vertex position (`305a3b86`):**
```
PositionNode 61d1b431 (space 2 = world)
  -> Split b96d23e3
     .R -> Add 8f06e6fd(+Time*speed) -> Sinewave 129d00de
     .B -> Add 9c6400eb(+Time*speed) -> Sinewave ca5da90f
  -> Add f8d367f5 -> Divide 12380a21.B=2 -> Multiply 25c5048e(amplitute)
  -> Add 9b272b11(+ original Y) -> Vector3 e44923c7(X=R, Y=wave, Z=B)
  -> Transform 9295cd68 {from:2,to:0} -> Block 305a3b86
```
Pseudocode: `p.y += amplitude * (wave(p.x + t*speed, length) + wave(p.z + t*speed, length)) / 2`.

**NormalTS (`0e9800bf`):**
```
four (texture movement -> Normal map) pairs
 -> NormalBlend 88ab288c / b0808911 -> NormalBlend cb318b8b
 -> NormalStrength 02e7c306 (Strength = Lerp df1a31e3(0, normal strength,
    saturate(depthfade(Strength, Deepness))))
 -> Saturate e4911d87 -> Block 0e9800bf
```
Scrolling directions built from `Refraction Speed = s`: `(0,s)`, `(s,-s)`,
`(0,-s)`, `(-s,s)` via `Vector2 1729c992`, `Split 437edb25`,
`Negate d14c9d55/e7fa1f30/f5b77941`, `Combine bd5ff6e8/33577ead`.

**BaseColor (`b671fb55`):**
```
depthT  = saturate(depthfade(Strength, Deepness))              [8ad52966]
water   = lerp(Shallow, Deep, depthT)                          [b164de58]
horizon = lerp(water, HorizonColor, Fresnel(HorizonDistance))  [dffd260b]
foamEdge= depthfade(FoamAmount, Strength=0) * FoamCutout       [5e469230, 4fda3dbe]
noise   = GradientNoise(foam texture movement, Scale=10)       [8259c3ec]
foamT   = step(foamEdge, noise) * FoamColor.a                  [0e028674, f16ede3a]
mixed   = lerp(horizon, FoamColor, foamT)                      [f7a7c8df]
sceneUV = ScreenPosition + normalBlend * RefractionStrength    [58c95e68, ccae03e1, 1f336bd5]
refract = SceneColor(sceneUV)                                  [8400e357]
A       = lerp(refract, mixed, mixed.a)                        [13b8fd8b, ec3f54df.A]
final   = lerp(A, FogColor, FogDensity)                        [9551f005] -> Block
```
Fog node `6a069dc4` supplies both Color and Density (engine state).

**Smoothness (`c40f9618`):**
```
dot(ViewVector, (0,1,0)) -> Step(Edge=0, In=dot) -> * smotness -> saturate
```
`DotProduct b1366f3b` B default is `(0,1,0,0)` — read from `m_Value`, not a connection.

## 3. Subgraphs

### 3.1 `Sinewave`

IDs: `b5071bed` Property(position), `5ff753d4` Property(wavelength),
`9b3b8ffc` Constant(m_constant=0), `a392a9f8` Multiply(×2),
`4c34d00a` Multiply(×position), `3537158a` Divide(/wavelength) — call this `k`.
Five branches: `39239945` Sine(k), `aefa50dd`×2→`9cb2186e`, `74869f8b`×5→`c0865751`,
`aa1a3ce0`×8→`53b7c87b`, `b03327f4`×11→`ba42a9bf`; weighted by
`626ad575` (0.7), `98a43603` (0.2), `53624021` (0.5), `a4673d98` (0.5);
summed by `4f54a782`, `c636953a`, `b63b23b3`, `9d887b1a` ->
`SubGraphOutputNode 1a1ba0a7`. Weight literals come from input-slot `m_Value`.

Pseudocode: `profile = sin(k) + 0.5 sin(11k) + 0.7 sin(2k) + 0.2 sin(5k) + 0.5 sin(8k)`.

### 3.2 `depthfade`

IDs: `65204d11` ScreenPosition -> `98abc38d` Split(.A) -> `f5cb8819`
Subtract(sceneDepth − screenA); `14851417` SceneDepth -> Subtract.A;
`9c549d5e` Property(Distance) -> `9133af43` Divide -> `d2e26331` Saturate ->
`21bc79c5` Multiply by `02d73a9e` Property(Strength) -> `9c2f8d92` Output.

Pseudocode: `out = saturate((sceneDepth − surfaceDepth)/Distance) * Strength`.
Requires the engine's scene-depth texture and the fragment's screen-space depth.

### 3.3 `texture movement`

IDs: `36a5f094` Time -> `6fc870c0` Multiply by `1afb16b5` Property(Speed) ->
`4bc1fbd8` TilingAndOffset.Offset (Tiling ← `9eb0d33c` Property(Scale)) ->
`aeda6788` Output. The TilingAndOffset UV input is **unconnected**, default `(0,0)`.

Pseudocode (faithful): `uv = float2(0,0) * Scale + time * Speed = time * Speed`.

### 3.4 `Normal map`

IDs: `1db50259` Property(UV) -> `5a833088` TilingAndOffset -> `bc5a9e24`
SampleTexture2D (`m_TextureType=1`, serialised texture GUID
`3bd52d67…` = `Textures/Water Normal.png`) -> `737d8839` NormalStrength(Strength ←
`70bbcbb0` Property(normal strength), default 0.1) -> `b0a89b3b` Output.normal;
texture `.A` -> `66dd0c0f` Preview -> Output.noise (**unused** by water.shadergraph).

Pseudocode: `normal = normalize(float3(decodeRGB(tex(uv)).xy * strength, z))`.

## 4. `WaterClipmap` (HLSL, not a graph)

No ShaderGraph IDs exist; the invariant is the 11-function pipeline:

| Function | Role |
|---|---|
| `tessVert` / `hull` / `PatchConstantFunction` | world-space control points; per-edge tessellation from camera distance |
| `domain` | barycentric interpolation; FFT cascade 0 + faded cascade 1 displacement; wake height; world/clip/screen/fog varyings |
| `Ocean_D_GGX`, `Ocean_G_Smith`, `Ocean_F_Schlick` | micro-facet BRDF terms |
| `SampleMicroNormal` | two scrolling taps of the detail normal, distance-faded |
| `EvaluateProceduralSkyReflection` | analytic sky gradient + sunset band + sun corona in reflection |
| `frag` | shadow split, normals, refraction/depth, caustics, transmittance, upwelling, SSS, specular/glints, sky/cubemap reflection, three foam sources, horizon + engine fog |

Algorithm summary: `displacedWS = p + disp0 + c1Weight*disp1 + (baseHeight + wakeY)ŷ`;
`normal = combine(fftCascadeNormals, microNormal, wakeGradient)`; refracted seabed
is sampled with an occluder-aware offset; `color = lerp(body + SSS, reflection,
Fresnel) + sunSpecular + glints`, then crest/shore/wake foam and horizon fog.

`WaterClipmap.slang` is an independently structured reference translation of
this pipeline; it deliberately does **not** reproduce Unity's macros/includes.
It is not standalone: see §5.

## 5. What requires external C# / engine state (cannot be standalone)

1. **`water.shadergraph`**
   - `TimeNode` ×3 (and inside `texture movement`) read `_Time` (float time, sine/cosine variants).
   - `PositionNode 61d1b431` (`m_Space=2`) needs world position; `TransformNode 9295cd68`
     needs object↔world matrices.
   - `ViewVectorNode 1a0d2800` needs camera world position.
   - `ScreenPositionNode 58c95e68` and `depthfade`'s `ScreenPositionNode`/`SceneDepthNode`
     need the frame's screen-space position and a **scene depth texture**.
   - `SceneColorNode 8400e357` needs the **opaque/scene colour texture**.
   - `FogNode 6a069dc4` needs Unity/URP fog Color and Density.
   - `Normal map` gets its texture from a serialised default GUID, **not** an exposed
     material property; the host must bind `Water Normal.png`.
   - The converted material has no overrides, so all values come from graph defaults.
2. **`WaterClipmap.shader`**
   - `_OceanDisplacement0/1`, `_OceanNormal0/1`, `_OceanFoam0/1` are written by
     `OceanFFT.compute` (two cascades, domain sizes 256 m and 64 m).
   - `_ShipWakeMap`, `_WakeCenterXZ`, `_WakeAreaSize` are written/bound by C# from
     `ShipWakeSim.compute` (`_WakeCenterXZ`/`_WakeAreaSize` sit *outside* the
     per-material CBUFFER, i.e. globals).
   - `_SkyReflectionCubemap` probe, main-light shadow coordinates/attenuation,
     URP fog, `_ZBufferParams`, `LinearEyeDepth`, `GlossyEnvironmentReflection`
     and `MixFog` all come from engine state/includes.
   - Tessellation needs a hull/domain-capable target; variant keywords
     (`_MAIN_LIGHT_SHADOWS*`, `_SHADOWS_SOFT*`, `_ADDITIONAL_LIGHTS`) are C#-driven.

## 6. Top three uncertain nodes

1. **`TilingAndOffsetNode 4bc1fbd8` inside `texture movement`** — confidence **LOW**
   that the spatially-uniform result is intentional. Its UV input is unconnected
   with default `(0,0)`, so all five parent instances (`6c54bdc7`, `6ec45782`,
   `8ff6f95c`, `bee82d22`, `f02cc53e`) produce coordinates derived only from
   `time * Speed`; `Scale` has no effect and the normal map is uniform across the
   surface. A missing `Position`/UV connection in the original graph is the most
   likely explanation. The reference translation stays faithful to the JSON.
2. **`FresnelNode 2e6e4697`** — confidence **LOW** in the intended inputs. Both
   `Normal` and `View Dir` are unconnected with serialized defaults `(0,0,0)`, so
   `pow(1 − saturate(dot(0,0)), HorizonDistance) = 1` for any exponent: the
   horizon colour always wins the `dffd260b` lerp. Likely missing connections.
3. **`ConstantNode 9b3b8ffc` (`Sinewave`)** — confidence **MEDIUM/LOW**.
   `m_constant = 0` is read as π (the ×2 node `a392a9f8` then yields 2π per
   wave cycle). Sky's `CONFLICTS.md` independently assigns **Low** to the same
   enum-index reading; if 0 were τ instead, the wave frequency would double.

Runner-up: `NormalBlendNode` ×3 (`88ab288c`, `b0808911`, `cb318b8b`) — the
operation is clear but the exact whiteout/RNM formula is not recoverable from
JSON; the reference uses a simplified whiteout blend.

## 7. Confidence summary

- **High**: node identities, counts, slot defaults, all edge topology, the
  vertex wave formula, the depthfade formula, the Sinewave harmonic weights,
  the texture decode chain, and the WaterClipmap stage ordering.
- **Medium**: space/target enum mappings, `NormalBlend` details, Fresnel semantics,
  and any claim about what unconnected inputs were *meant* to be.
- **Low**: the three nodes in §6. They are flagged rather than silently
  "corrected" in the `.slang` files.
