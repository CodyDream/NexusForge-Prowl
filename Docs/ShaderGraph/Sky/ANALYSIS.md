# Sky ShaderGraph reverse-engineering (`Skybox.shadergraph` + 2 subgraphs)

Detective scope: inference only from local JSON graphs, local material YAML/JSON files, and the locally installed Shader Graph manual. No engine code, importers, Git operations, or Unity-authored shader source was used as a translation source. The hand-written `ProceduralSkyWithClouds.shader` and its generated Slang under `external new import packages - do not ship/Shaders|Slang/Sky/` are a **different, much larger shader** (many extra properties: `_DayZenithColor`, `_CloudCoverage`, 4-octave FBM, sunset grading, HDR sun). It is noted here only as provenance; it is not copied and it is not the graph under analysis.

## 1. Source inventory

| Role | Path | Observation |
|---|---|---|
| Main graph | `C:\Users\Nimesh\Documents\Prowl Projects\UnityImportTest\Assets\Low Poly Stylized Nature\Shaders\Skybox.shadergraph` | 374 flattened JSON objects. Each object has `m_ObjectId`; references use `{m_Id}`. A single `GraphData` root owns 99 nodes, 98 edges, 17 properties, 3 vertex blocks, 8 fragment blocks, 6 groups, 3 categories. |
| Subgraph A | `...\Shaders\SubGraphs\skybox uv.shadersubgraph` | 46 objects, 12 nodes, 13 edges, 0 properties, 1 output (`New`). GUID in subgraph refs: `d1f0197ddabb300418ec9d571835cb89`. |
| Subgraph B | `...\Shaders\SubGraphs\stars.shadersubgraph` | 45 objects, 13 nodes, 13 edges, 3 properties, 1 output (`Out_Vector4`). GUID: `2c5289a7933a37b42bf8fd3a0d6c4b15`. |
| Authored material (original package) | `Low Poly Stylized Nature 1.0.4 .unitypackage`, entry `fb50d0be0757d1c43b60c9936371a68b/asset` (path `Assets/Cody Dreams/Low Poly Stylized Nature/Matereals/Skybox.mat`, shader GUID `86e4c59cae06f20469333913d893acff` = main graph) | Supplies the real override values. 17 floats/colors match the graph's `m_DefaultReferenceName` list exactly. RenderType `TransparentCutout`, custom queue 2450, keyword `_ALPHATEST_ON`, shadow/motion passes disabled. A `_cloud_texture` is bound but no Texture2D node exists in the graph (dead property). |
| Converted material (current project) | `Assets\Low Poly Stylized Nature\Matereals\Skybox.mat` (Prowl JSON) | All `_colors`, `_floats`, etc. are empty. If imported as-is, the graph defaults (many of which are 0) would be used, so it is **not** a reliable source of look values. |
| Unrelated material | `external new import packages - do not ship\Shaders\Sky\Sky.mat` | Belongs to the separate hand-written HLSL shader (`ProceduralSkyWithClouds`): `_CloudCoverage`, `_SunIntensity`, `_MoonSize` etc. Names and semantics differ from the graph. Do not bind graph uniforms from it. |

### Property binding table (graph ref name → graph default → original package material value)

| Property id | Ref name | Graph default | Material override | Used by |
|---|---|---|---|---|
| `f18b0581` | `_horizon_color` | (0.176, 0.564, 0.821) | (0.4528, 0.2650, 0.0, 1) | horizon↔sky band |
| `9fd640e0` | `_sky_color` | (0, 0.313, 1) | (0, 0.8029, 1, 1) | day sky |
| `cdd9eed0` | `_Sky_night_color` | (0,0,0) | (0.3396, 0.0176, 0.3014) | night sky |
| `bf0820a5` | `_Star_sine` | (0.5, 0.89) | (50, 250) | star twinkle output range |
| `9df60816` | `_Star_density` | 5 | 1000 | `stars.noise scale` |
| `c158a2fd` | `_Star_Power` | 0 | 21.2 | `stars.star power` |
| `66b53af2` | `_Sun_color` | (0.585, 0.344, 0.241) | (1, 0.5969, 0, 1) | sun disk tint |
| `00e76efd` | `_Sun_size` | 0 | 1.36 | sun `Step` edge (radians) |
| `9280af57` | `_offset` | −0.16 | −0.04 | vertical shift of gradient band |
| `5fadb564` | `_cloud_highet` | 0 | 0.08 | cloud smoothstep lower edge |
| `ce1986a1` | `_clod_edge` | 0.36 | 0.82 | cloud smoothstep upper edge |
| `76e803df` | `_cloud_scale` | 1 | 1.83 | UV tiling of both noise layers |
| `fa79a98f` | `_wind_speed` | 0 | 0.01 | pan speed, +x and −2x layers |
| `a226d155` | `_fog_height` | 2 | 11 | exponent on horizon fog falloff |
| `25d5d788` | `_Moon_color` | (0,0,0) | (0, 1, 0.9804) | moon disk tint |
| `01b0bb2e` | `_Moon_size` | 0 | 7.7 | moon `Step` edge (radians) |
| `3f957aac` | `_moon_direction` | (0,0,0) | (0.55, −2.35, 1.71) | moon direction (normalized in graph) |

`stars` subgraph inputs, from the main instance `9b8bc176`: `noise scale ← _Star_density`, `star power ← _Star_Power`, `intensity ← remap(Sine Time, −1..1 → _Star_sine) * simpleNoise(UV, 100)`. The subgraph's own `_intensity` default (1) is overridden.

## 2. Subgraph composition (instance count)

- `skybox uv`: **2 instance objects total**.
  1. `1a6c53b0` in the main graph at layout x≈−2607. Its `New` output fans out to **5** consumers: `4ed48c06` (gradient band UV), `28f016d5` & `83db947b` (splits for vertical band / fog), `15316329` & `82910714` (the two cloud UVs).
  2. `cd9f8974` inside `stars`. Its output feeds only the star `Simple Noise` (`372f9ff4`). Because `stars` itself is instantiated once, the UV subgraph is effectively evaluated twice per pixel.
- `stars`: **1 instance object**, `9b8bc176` in the main graph (x≈−1527). It is not used anywhere else. Its `Out_Vector4` is consumed by `5844750e` (lerp from black by the vertical band mask), then added to the sky at `e27aec66`.

No recursion or third subgraph is present.

## 3. Edge traces (short IDs; full ids in the appendix table)

Notation: `a→b` means slot of node `a` feeds a slot of node `b`.

### 3.1 Sky direction and equirectangular UV (`skybox uv`, high confidence)
```
e03c8062 Position (space=2, source=0) → f55955c8 Normalize → a5638aec Split
a5638aec.R → a604aa89.A ; a5638aec.B → a604aa89.B
a5638aec.G → dff1eb62.Arcsine
a604aa89 → f932fc2d.Divide by c905564f (Constant m_constant=1)
3b3eaecf (Constant m_constant=0) → e0556493.A ; e0556493.B = default 2 → b17b99ac.B
dff1eb62 → b17b99ac.A
f932fc2d → 052b64b9.R ; b17b99ac → 052b64b9.G → 18be40eb (output)
```
Inference: longitude = `atan2(dir.x, dir.z)/C1`, latitude = `asin(dir.y)/(C0/2)`, returned as `float2`. High confidence on topology; **low confidence on which math constants C0/C1 represent** (`m_constant` 0 and 1; likely enum indices PI and TAU). Both plausible assignments are discussed in §6.

### 3.2 Day/night sky band and sun elevation (high confidence)
```
cfabfd2a MainLightDirection → fc303c94 Split → eff750d4.Subtract(B=(-1,1,1,1))
eff750d4 → c0091958 Saturate → ad6357c1.T (night ⇄ day lerp)
ad6357c1: A = f6c9f4d0 sky color, B = 9fd7fe13 Sky night color
1a6c53b0 skyuv → 4ed48c06 (tiling 8,2) → b080113b Split → fe6f3df2.Add(+97eaaddd offset)
fe6f3df2 → 57be286c.T ; 57be286c: A = 09bef373 horizon color, B = ad6357c1 → e27aec66.Add
```
Read as: `dayMix = saturate(lightDir.y + 1)` (with material sun below horizon this becomes a smooth day/night fade), `band = skyUV.y + offset`, `baseSky = lerp(horizonColor, lerp(skyColor, nightColor, dayMix), band)`.

### 3.3 Sun and moon disks (high–medium confidence)
```
Sun: c89b8dfa MainLightDirection → db1278b4.Add(B=(0,-0.1,0.1,0)) → 5d8de16b Normalize → 9a4533e0.Dot with 6c9417c3 Normalize(11f225cf ViewDirection)
9a4533e0 → 2196d029 Arcsine → aa3111b1.Step(Edge=_Sun_size) → aac7b897.Multiply(_Sun_color) → 07877b45 Saturate → 6b7ce7df.Lerp.T (and step also drives T of 6b7ce7df)
Moon: 4fc43049 moon direction → a16e9b4f Normalize → 8d5f6036.Add(default 0) → cc93e678 Normalize → 21fd09f4.Dot with ae25e407 Normalize(34fad14f ViewDirection)
21fd09f4 → 9a34ad6a Arcsine → 8ca888e6.Step(Edge=_Moon_size) → b63cb2a1.Multiply(_Moon_color) → a6b8985e Saturate → de3ceb54.Add
```
Read as: `angle = asin(dot(normalize(light + tinyOffset), normalize(view)))`, `disk = step(size, angle)`. Note the sun direction is deliberately offset by `(0, −0.1, +0.1)` before normalization (a "fake sun" slightly below/tilted relative to the real light). The sun disk replaces the sky via `6b7ce7df`, the moon is **added** on top via `de3ceb54`. `Arcsine` (not `Arccosine`) means the threshold is complementary: `step(s, asin(dot)) == 1` when `dot >= sin(s)`.

### 3.4 Star field (high confidence)
```
e4af607e.Time.Sine Time → 5590d433.Remap(−1..1 → bf0820a5 Star sine) → 0fa0c7c2.Multiply
2b77ff9e Simple Noise(h0, scale=100) → 0fa0c7c2.B → 9b8bc176.stars.intensity
3600f93c Star density → 9b8bc176.noise scale ; 9c1ad884 Star Power → 9b8bc176.star power
9b8bc176 → 5844750e.Lerp(A=black, T=vertical band) → e27aec66.Add
```
Inside `stars`:
```
cd9f8974 skyuv → 372f9ff4 Simple Noise(h0) → 6a07d41d.Pow(_Star_Power) → 33bc31c0.A
noise scale → ff84650c.Divide(B=10) → bf5fee3a Gradient Noise scale
217ef3bb Gradient (6 keys) → eeacde54.SampleGradient.Time ← bf5fee3a
eeacde54 → 74805973.Multiply(intensity) → 33bc31c0.B → 99076328 output
```
So the star mask is `pow(valueNoise(uv, density), power) * ramp(perlinNoise(uv, density/10)) * intensity`, with `intensity` carrying the sine-time twinkle and a second noise. This is **not** a cell/hash star field; it is a threshold-free noise^p product.

### 3.5 Cloud layers — the "FBM loops" (high topology, medium semantics)
There are **no loop/iteration nodes**. The apparent FBM is two simple-noise layers multiplied together, plus a posterized band:
```
4eca3e46.Time → 2d6e8fe4.Multiply(f9490647 wind speed * 7d6af90a constant 0.1)
2d6e8fe4 → d3484fbf.Multiply(+1) → c09265b7.Combine.R → 82910714.Offset (pan +t)
2d6e8fe4 → 0fada497.Multiply(−2) → 15316329.Offset (pan −2t)
1a6c53b0 skyuv → 15316329 / 82910714, both tiled by f1d8e45c cloud scale
15316329 → 99741bd8 Simple Noise(h1) → 5aafa6d0 Saturate
82910714 → 39d7f17a Simple Noise(h1) → e1184bc3 Saturate
5aafa6d0 * e1184bc3 → 09bd9c6f → cc29e142 Saturate → 3ff3d25f Posterize(Steps=6) → b308e6b9 Remap(0..1 → 1..0) → 56e631d6.A
09bd9c6f → c91d3563 Remap(0..1 → 1..2) → 41b86582.Multiply
1a6c53b0 → 28f016d5.Split.G → e5daada6.Add(+0.1)
53ebbf89 cloud highet & cbfe79f1 clod edge → dc25056d.Add → fffdcfe5 Saturate → da3ed51e.Edge2 ; da3ed51e.In = e5daada6
da3ed51e → 46eb2cca OneMinus → 41b86582.B → 0fc18d5f.Step(Edge=0.93) → 56e631d6.T
de3ceb54 (moon-composited sky) → 56e631d6.A ; b308e6b9 → 56e631d6.B
```
`56e631d6` blends the moon-composited sky toward the 6-step quantized cloud mask, gated by the cloud-edge step. It drives the final `e43a9339` fog lerp. Two noise layers at opposite pan speeds is the only "octave" structure present; there is no rotation matrix, no `abs(fbm*2−1)` billow, no 4-iteration loop in this graph.

### 3.6 Fog and final output (high confidence)
```
1a6c53b0 → 83db947b.Split.G → 2ce69d94.OneMinus → 09c33e8c.Saturate → 19c981cd.Pow(_fog_height)
19c981cd → e43a9339.T ; e43a9339.A = 56e631d6.Lerp(cloud-composited sky) ; e43a9339.B = b08d1cb5.Fog.Color (Position unconnected)
e43a9339 → 5f2dd7f6 SurfaceDescription.BaseColor
```
All other fragment blocks are left at defaults (Alpha=1, Metallic=0, Smoothness=0.5, Emission=0, AO=1, NormalTS=0). Only Base Color is authored. Active targets: BuiltIn (`184f09cc`, Lit subtarget) **and** URP (`60176e3e`, Unlit subtarget) — both registered simultaneously; the emitted shader depends on which pipeline is active.

## 4. Plain pseudocode (inferred, not copied)

```
skyuv(dir):                       // dir = normalize(world position)
    u = atan2(dir.x, dir.z) / C1
    v = asin(dir.y) / (C0 / 2)
    return (u, v)                 // C0/C1 are the two ConstantNodes; see §6

sky = skyuv(dir)
band = sky.y + _offset
dayMix = saturate(lightDir.y + 1)
base = lerp(_horizon_color, lerp(_sky_color, _Sky_night_color, dayMix), band)
base += lerp(0, stars(sky, _Star_density, _Star_Power, twinkleNoise), band)

sunAngle  = asin(dot(normalize(light + (0,-0.1,0.1)), normalize(view)))
moonAngle = asin(dot(normalize(normalize(_moon_direction)), normalize(view)))
sunDisk  = saturate(step(_Sun_size,  sunAngle) * _Sun_color)
moonDisk = saturate(step(_Moon_size, moonAngle) * _Moon_color)
sky = base blended to sunDisk by sunDiskMask, then + moonDisk

t    = _Time * _wind_speed * 0.1
uv1  = (sky * _cloud_scale) + (t, 0)
uv2  = (sky * _cloud_scale) + (-2t, 0)
n1   = saturate(simpleNoise(uv1, 100, hash=LPM))
n2   = saturate(simpleNoise(uv2, 100, hash=LPM))
prod = saturate(n1 * n2)
mask = lerp(1, 0, posterize(prod, 6))          // remap 0..1 -> 1..0
edge = smoothstep(_cloud_highet, saturate(_cloud_highet + _clod_edge), sky.y + 0.1)
cloudSpecial = remap(prod, 0..1, 1..2) * (1 - edge)
cloudStep = step(0.93, cloudSpecial)
skyCloud = lerp(skyWithMoon, mask, cloudStep)   // scalar mask broadcast to RGB
fogFactor = pow(saturate(1 - sky.y), _fog_height)
color = lerp(skyCloud, fogColor, fogFactor)

stars(uv, density, power, intensity):
    return pow(simpleNoise(uv, density, hash=standard), power)
         * sampleGradient(gradientNoise(uv, density/10, hash=standard))
         * intensity
```

Direct Slang translations are in `skybox_uv.slang`, `stars.slang`, and `procedural_sky.slang`; each file carries its own node-count header. The Slang uses explicit `float2/float3/float4`, so the dynamic-slot ambiguity of the JSON is resolved to scalars/vectors as the graph connections imply.

## 5. External state the runtime must provide

| Input | Source in graph | Notes |
|---|---|---|
| `_Time` / `Sine Time` | `TimeNode` ×2 | Wind pan and star twinkle. `Sine Time` is remapped through `_Star_sine`. |
| Main light direction | `MainLightDirectionNode` ×2 (`cfabfd2a`, `c89b8dfa`) | synonyms `sun`. One copy drives day/night elevation, the other the disk. A hard-coded `(0,-0.1,0.1)` offset is applied to the disk copy. |
| World position / view direction | `PositionNode` (skyuv), `ViewDirectionNode` ×2 | Both `m_Space:2` (world). The `skybox uv` subgraph normalizes world position as the sky direction. |
| Fog color | `FogNode.Color` (`b08d1cb5`) | Engine fog uniform; `Position` input is unconnected. |
| 17 graph properties | `m_DefaultReferenceName` column above | Use the original package material values, not the empty converted `Skybox.mat`, and not the unrelated hand-written shader's material. |
| `_cloud_texture` | Present in original material only | No matching node; safe to ignore. |

## 6. Confidence notes and open questions

**Top three uncertainties**

1. **ConstantNode numeric values (`3b3eaecf`, `c905564f`, skyuv).** `m_constant` is 0 and 1; the node's `synonyms` only say `pi, tau, phi`, and the Shader Graph manual lists PI/TAU/PHI/E/SQRT2 without exposing the enum index mapping. Topology is certain; the scale of the returned UV is not. Two self-consistent readings:
   - A: `C0 = PI`, `C1 = TAU` → `u ∈ [−0.5,0.5]`, `v ∈ [−1,1]` (the translation's default).
   - B: `C0 = TAU`, `C1 = PI` → `u ∈ [−1,1]`, `v ∈ [−0.5,0.5]`.
   The subsequent tiling `(8,2)` and `+0.1` bias mean either can be compensated by material values, so geometry alone cannot disambiguate. Flagged **Low**; verify against a Unity-generated shader if one becomes available locally.

2. **"Cloud FBM loops" are not loops in this graph.** The only fractal-ish structure is two `Simple Noise` layers (`99741bd8`, `39d7f17a`) with opposite pan directions, multiplied at `09bd9c6f`, then posterized to 6 steps and remapped. There is no octave counter, rotation matrix, or `billow = abs(2n−1)` node. The 4-octave FBM exists only in the separate hand-written shader. Flagged **Medium** for the *intent* (cloud density/billow estimate), **High** for the topology.

3. **`_Moon_size = 7.7` is outside the `asin` output range.** `asin()` returns `[−π/2, π/2] ≈ [−1.571, 1.571]`; `step(7.7, angle)` is always 0, so the shipped material makes the moon disk invisible. `_Sun_size = 1.36` gives `dot ≥ sin(1.36) ≈ 0.978` (≈12° disk), which is plausible. Either the material was authored for an older `acos`-based version, or the moon is intentionally disabled. The graph semantics themselves are **High** confidence (`step(edge, asin(dot))`); the *interpretation of the size property* is **Low**.

**Other lower-confidence items**

- `NoiseNode.m_HashType` 0/1 maps to Unity's standard/LPM hash variants; exact bit-for-bit noise is not reproducible from the JSON alone, so the Slang supplies documented approximations.
- `GradientNode.m_SerializableMode = 1` (gradient mode) is honored as piecewise-linear by time; the exact key interpolation mode is not serialized more precisely.
- `FogNode.Position` is unconnected; only its `Color` output is used, so fog density from the graph is zero/irrelevant.
- Two active targets (BuiltIn Lit and URP Unlit) are registered; which one is compiled depends on the host pipeline. The graph writes only Base Color, so the Lit target's specular/smoothness blocks stay at defaults.
- `m_DrawState.m_Position.x` is used as execution-order evidence; it is editor metadata, not a semantic guarantee. Group titles (`Sky gradiant`, `cloud`, `clouds edge`, `fog`, `New Group`) corroborate the subsystem splits.
- Property names are misspelled in the asset (`clod edge`, `cloud highet`, `highet`), and `m_DisplayName` is empty; `m_Name` + `m_DefaultReferenceName` are authoritative.

## 7. Appendix — complete ID tables

The following tables were generated directly from `m_ObjectId`, `m_Type`, `m_Name`, group id and slot defaults; they are the raw index used for the traces above.
### `main` â€” complete node index (layout order)

| Short ID | Full m_ObjectId | m_Type | m_Name | Group | Key slot defaults |
|---|---|---|---|---|---|
| `cfabfd2a` | `cfabfd2aba704e85b3e88b48c8c7976a` | `MainLightDirectionNode` | Main Light Direction | â€” | â€” |
| `4ed48c06` | `4ed48c06cb5a4b47abc86ebbb39bfd56` | `TilingAndOffsetNode` | Tiling And Offset | â€” | Tiling=(8,2,0,0) |
| `4eca3e46` | `4eca3e46cd124c6f97117d40f3c8a303` | `TimeNode` | Time | cloud | â€” |
| `f9490647` | `f94906476ccc4d8385f9d918d49dd8f5` | `PropertyNode` | Property â†’ wind speed | cloud | â€” |
| `fc303c94` | `fc303c946ba443a7b9c3c1b4fffabe40` | `SplitNode` | Split | â€” | â€” |
| `1a6c53b0` | `1a6c53b053b54057bfb0cf60c9731232` | `SubGraphNode` | skybox uv | â€” | â€” |
| `3600f93c` | `3600f93cb54246d2a590b2d1e68178b1` | `PropertyNode` | Property â†’ Star density | â€” | â€” |
| `eff750d4` | `eff750d4fbc54d5e93300aa09a90bfba` | `SubtractNode` | Subtract | â€” | A=(1,1,1,1), B=(-1,1,1,1) |
| `2d6e8fe4` | `2d6e8fe4abff4e918b6b0e7d95315728` | `MultiplyNode` | Multiply | cloud | A=mat, B=mat |
| `e4af607e` | `e4af607eab204fd8a095679089c7864a` | `TimeNode` | Time | â€” | â€” |
| `cbfe79f1` | `cbfe79f1f67649ec97e882a96afd7fc5` | `PropertyNode` | Property â†’ clod edge | clouds edge | â€” |
| `53ebbf89` | `53ebbf89979c441f98a2571cf2c6546a` | `PropertyNode` | Property â†’ cloud highet | clouds edge | â€” |
| `4fc43049` | `4fc4304937934fd392b810266ddfed3c` | `PropertyNode` | Property â†’ moon direction | New Group | â€” |
| `a6992057` | `a69920578d8545e78b63279333dab919` | `PropertyNode` | Property â†’ Star sine | â€” | â€” |
| `c89b8dfa` | `c89b8dfa3a8f45d1a23eaacd4dc64c09` | `MainLightDirectionNode` | Main Light Direction | New Group | â€” |
| `34fad14f` | `34fad14febe440abbddbab3dfeb6cc64` | `ViewDirectionNode` | View Direction | New Group | â€” |
| `11f225cf` | `11f225cf562a4874ba18cedd09ab1f94` | `ViewDirectionNode` | View Direction | New Group | â€” |
| `c0091958` | `c0091958e8de4b46994f7c3c07b2d093` | `SaturateNode` | Saturate | â€” | â€” |
| `28f016d5` | `28f016d5205f4217926888defd92afbb` | `SplitNode` | Split | clouds edge | â€” |
| `9c1ad884` | `9c1ad884d41c4f4ea8fa07200f2faf1f` | `PropertyNode` | Property â†’ Star Power | â€” | â€” |
| `5590d433` | `5590d433cd624139ae5c696df61cb207` | `RemapNode` | Remap | â€” | In=(-1,-1,-1,-1), In Min Max=(-1,1,0,0), Out Min Max=(0,1,0,0) |
| `d3484fbf` | `d3484fbfc5134a4cb6b72033f41be32c` | `MultiplyNode` | Multiply | cloud | A=mat, B=mat |
| `0fada497` | `0fada4974f8a4432b9ac4b43a46ac2e0` | `MultiplyNode` | Multiply | cloud | A=mat, B=mat |
| `7d6af90a` | `7d6af90a7ea1453399580d7bef51eb87` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `dc25056d` | `dc25056ddd47453985984757eafe3230` | `AddNode` | Add | clouds edge | â€” |
| `db1278b4` | `db1278b44a354b0a84ce413532c1849f` | `AddNode` | Add | New Group | B=(0,-0.1,0.1,0) |
| `a16e9b4f` | `a16e9b4f051b4ac693daf352a35d0101` | `NormalizeNode` | Normalize | New Group | â€” |
| `8d5f6036` | `8d5f6036e83a493f8fc2e083a8061475` | `AddNode` | Add | New Group | â€” |
| `2b77ff9e` | `2b77ff9ed36c45ddb82f95408348fdb0` | `NoiseNode` | Simple Noise | â€” | Scale=100 |
| `ae25e407` | `ae25e4071c4849c6868d725b67385990` | `NormalizeNode` | Normalize | New Group | â€” |
| `5d8de16b` | `5d8de16b2aad439a9820e0c153ca7f36` | `NormalizeNode` | Normalize | â€” | â€” |
| `e5daada6` | `e5daada69c2d499ba6e842272000da39` | `AddNode` | Add | clouds edge | B=(0.1,0,0,0) |
| `6c9417c3` | `6c9417c35bac4695a2acc10f3e0a18b9` | `NormalizeNode` | Normalize | New Group | â€” |
| `c09265b7` | `c09265b748ad441e9686736ad6f7f993` | `CombineNode` | Combine | cloud | â€” |
| `0fa0c7c2` | `0fa0c7c2a6c742949793338def4289e1` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `cc93e678` | `cc93e6788bc0498bac71bee10bbbdc37` | `NormalizeNode` | Normalize | New Group | â€” |
| `fffdcfe5` | `fffdcfe5d45d4f6999d29ea98ffb385e` | `SaturateNode` | Saturate | clouds edge | â€” |
| `9b8bc176` | `9b8bc17654cb4e0f8d4f024fd99e10e2` | `SubGraphNode` | stars | â€” | noise scale=100, star power=7.1 |
| `21fd09f4` | `21fd09f4d8e44438aca41249bc65b98d` | `DotProductNode` | Dot Product | New Group | B=(0,1,0,0) |
| `9fd7fe13` | `9fd7fe13e540406a8a970cfb9b04c388` | `PropertyNode` | Property â†’ Sky night color | Sky gradiant | â€” |
| `09bef373` | `09bef373ed244df8833cad24589c586a` | `PropertyNode` | Property â†’ horizon color | Sky gradiant | â€” |
| `f6c9f4d0` | `f6c9f4d0bc2d4668b03a8eeca182b30a` | `PropertyNode` | Property â†’ sky color | Sky gradiant | â€” |
| `9a4533e0` | `9a4533e0c83a47a7a124c0e29199f960` | `DotProductNode` | Dot Product | New Group | B=(0,1,0,0) |
| `15316329` | `153163295efb4c348b651212a9560376` | `TilingAndOffsetNode` | Tiling And Offset | cloud | Tiling=(1,1,0,0) |
| `82910714` | `82910714d3f546b7acf48b796f4a94aa` | `TilingAndOffsetNode` | Tiling And Offset | cloud | Tiling=(1,1,0,0) |
| `dff4109d` | `dff4109d121a4d85b9cab14c284093c8` | `PropertyNode` | Property â†’ Moon size | â€” | â€” |
| `0fcafe20` | `0fcafe208dc64a3db0802c16ccb1093c` | `PropertyNode` | Property â†’ Sun size | â€” | â€” |
| `b080113b` | `b080113b90e546ed9cabad41181821ee` | `SplitNode` | Split | Sky gradiant | â€” |
| `97eaaddd` | `97eaaddddfc046c0b5519717fb079e00` | `PropertyNode` | Property â†’ offset | Sky gradiant | â€” |
| `da3ed51e` | `da3ed51e203e40bc84918207711444ef` | `SmoothstepNode` | Smoothstep | clouds edge | Edge2=(1,1,1,1) |
| `9a34ad6a` | `9a34ad6a26544e7b902a6f973b031f74` | `ArcsineNode` | Arcsine | New Group | â€” |
| `2196d029` | `2196d029c73f46fc943c9b50059d0b67` | `ArcsineNode` | Arcsine | New Group | â€” |
| `ad6357c1` | `ad6357c130b64377b550ba0dc401742e` | `LerpNode` | Lerp | Sky gradiant | B=(1,1,1,1) |
| `fe6f3df2` | `fe6f3df2999145d4b8d006b001bcee35` | `AddNode` | Add | Sky gradiant | â€” |
| `39d7f17a` | `39d7f17a33f047faa600259e89580df2` | `NoiseNode` | Simple Noise | cloud | Scale=100 |
| `99741bd8` | `99741bd8e61b42f4b0090ae8b797aa7e` | `NoiseNode` | Simple Noise | cloud | Scale=100 |
| `f1d8e45c` | `f1d8e45cf7f747618ab96fce94b4e40c` | `PropertyNode` | Property â†’ cloud scale | â€” | â€” |
| `57be286c` | `57be286c7b024ddc9d1ea42a4888cfe7` | `LerpNode` | Lerp | Sky gradiant | B=(1,1,1,1) |
| `46eb2cca` | `46eb2ccaf7be407491d825cfab9a4fc1` | `OneMinusNode` | One Minus | clouds edge | In=(1,1,1,1) |
| `8ca888e6` | `8ca888e6083947c9b36578dd0e915432` | `StepNode` | Step | New Group | Edge=(1,1,1,1) |
| `aa3111b1` | `aa3111b13ad54e3b8067f85b6adbb6ec` | `StepNode` | Step | â€” | Edge=(1,1,1,1) |
| `f55f070b` | `f55f070bfd174965a6e4617dda3d39f7` | `PropertyNode` | Property â†’ Moon color | â€” | â€” |
| `e1184bc3` | `e1184bc3d90a4e1f83033dbddbfe023f` | `SaturateNode` | Saturate | cloud | â€” |
| `5aafa6d0` | `5aafa6d0b929451684f1e8e7c6dde78d` | `SaturateNode` | Saturate | cloud | â€” |
| `b61283a8` | `b61283a80e0d4dff85fa14d83cae86df` | `PropertyNode` | Property â†’ Sun color | New Group | â€” |
| `5844750e` | `5844750ec69e4d698894fcfb56fddcbf` | `LerpNode` | Lerp | â€” | B=(1,1,1,1), T=(1,0,0,0) |
| `aac7b897` | `aac7b8972a694f5b9321efd785123981` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `41b86582` | `41b865822ebe49229d45d884cbbda8fd` | `MultiplyNode` | Multiply | clouds edge | A=mat, B=mat |
| `09bd9c6f` | `09bd9c6fee8e40f288e44fccfb829556` | `MultiplyNode` | Multiply | cloud | A=mat, B=mat |
| `b63cb2a1` | `b63cb2a17cd0406e82ee84d55b34cb99` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `0fc18d5f` | `0fc18d5fa90f4af1883531277c00543f` | `StepNode` | Step | clouds edge | Edge=(0.93,1,1,1) |
| `cc29e142` | `cc29e142586e4016996f7ba90ac012b8` | `SaturateNode` | Saturate | cloud | â€” |
| `e27aec66` | `e27aec6690604df5916fe57986c894c2` | `AddNode` | Add | â€” | â€” |
| `a6b8985e` | `a6b8985e8fc945eeb4531cf452d8a1e4` | `SaturateNode` | Saturate | â€” | â€” |
| `07877b45` | `07877b459d764552be81b8e050d28cae` | `SaturateNode` | Saturate | â€” | â€” |
| `c91d3563` | `c91d35631a054a9286cd8ec01dc04ba6` | `RemapNode` | Remap | â€” | In=(-1,-1,-1,-1), In Min Max=(0,1,0,0), Out Min Max=(1,2,0,0) |
| `78516262` | `78516262375f4024a0535fa623457f05` | `BlockNode` | VertexDescription.Position | â€” | â€” |
| `42a7bfe4` | `42a7bfe4a65847ddaf6d04c02fabc411` | `BlockNode` | VertexDescription.Normal | â€” | â€” |
| `25a2f399` | `25a2f3998d0e41e99968471313785abf` | `BlockNode` | VertexDescription.Tangent | â€” | â€” |
| `5f2dd7f6` | `5f2dd7f612d741b089500e97416ebbaa` | `BlockNode` | SurfaceDescription.BaseColor | â€” | Base Color=(0.5,0.5,0.5,0) |
| `e9f2344b` | `e9f2344b4ee5430ab8a693fc127f2bb7` | `BlockNode` | SurfaceDescription.Alpha | â€” | Alpha=1 |
| `df962430` | `df962430152848ccbfae1ad1e0e193b6` | `BlockNode` | SurfaceDescription.AlphaClipThreshold | â€” | Alpha Clip Threshold=0.5 |
| `c4d37dbf` | `c4d37dbfc43e49abb32853a4643085b6` | `BlockNode` | SurfaceDescription.Smoothness | â€” | Smoothness=0.5 |
| `415a3fec` | `415a3fecd8604128b3fa5bc33e1584cc` | `BlockNode` | SurfaceDescription.NormalTS | â€” | â€” |
| `6f26ec24` | `6f26ec245acb4843a8d6ab1984e32d6c` | `BlockNode` | SurfaceDescription.Emission | â€” | â€” |
| `f82f8ca8` | `f82f8ca85e044f81a77557ac4f8231a0` | `BlockNode` | SurfaceDescription.Occlusion | â€” | Ambient Occlusion=1 |
| `0d5747c9` | `0d5747c984f74a1fbff92040f9af1769` | `BlockNode` | SurfaceDescription.Metallic | â€” | â€” |
| `3ff3d25f` | `3ff3d25f31c44837a96266649eaef6c8` | `PosterizeNode` | Posterize | cloud | Steps=(6,6,6,6) |
| `b308e6b9` | `b308e6b910894b60ab98d1a3143468fe` | `RemapNode` | Remap | cloud | In=(-1,-1,-1,-1), In Min Max=(0,1,0,0), Out Min Max=(1,0,0,0) |
| `6b7ce7df` | `6b7ce7dfec6c4700bc8b079071b4dc6b` | `LerpNode` | Lerp | â€” | B=(1,1,1,1) |
| `83db947b` | `83db947b978c49b49ccd0921af404628` | `SplitNode` | Split | fog | â€” |
| `de3ceb54` | `de3ceb5431384ab29921edf5915e067b` | `AddNode` | Add | â€” | â€” |
| `2ce69d94` | `2ce69d94b8fd402b97f4a6d70b82f686` | `OneMinusNode` | One Minus | fog | In=(1,1,1,1) |
| `c18e1207` | `c18e1207befa4a6aa97c0856284883e0` | `PropertyNode` | Property â†’ fog height | fog | â€” |
| `09c33e8c` | `09c33e8c159d43569509eb89960ea243` | `SaturateNode` | Saturate | fog | â€” |
| `b08d1cb5` | `b08d1cb571304532be4dbcc22cc8c131` | `FogNode` | Fog | fog | â€” |
| `19c981cd` | `19c981cdb9764921a3a326ff940d9982` | `PowerNode` | Power | fog | B=(2,2,2,2) |
| `56e631d6` | `56e631d61c16416d85cb50958418b8ed` | `LerpNode` | Lerp | â€” | B=(1,1,1,1) |
| `e43a9339` | `e43a9339434e4596a74f6df021b1a667` | `LerpNode` | Lerp | â€” | B=(1,1,1,1) |

### `main` â€” properties

| Short ID | Property name | Ref name | Type | Graph default |
|---|---|---|---|---|
| `25d5d788` | Moon color | `_Moon_color` | ColorShaderProperty | {'r': 0.0, 'g': 0.0, 'b': 0.0, 'a': 0.0} |
| `01b0bb2e` | Moon size | `_Moon_size` | Vector1ShaderProperty | 0 |
| `cdd9eed0` | Sky night color | `_Sky_night_color` | ColorShaderProperty | {'r': 0.0, 'g': 0.0, 'b': 0.0, 'a': 0.0} |
| `c158a2fd` | Star Power | `_Star_Power` | Vector1ShaderProperty | 0 |
| `9df60816` | Star density | `_Star_density` | Vector1ShaderProperty | 5 |
| `bf0820a5` | Star sine | `_Star_sine` | Vector2ShaderProperty | (0.5,0.89,0,0) |
| `66b53af2` | Sun color | `_Sun_color` | ColorShaderProperty | {'r': 0.5849056243896484, 'g': 0.3441349 |
| `00e76efd` | Sun size | `_Sun_size` | Vector1ShaderProperty | 0 |
| `ce1986a1` | clod edge | `_clod_edge` | Vector1ShaderProperty | 0.36 |
| `5fadb564` | cloud highet | `_cloud_highet` | Vector1ShaderProperty | 0 |
| `76e803df` | cloud scale | `_cloud_scale` | Vector1ShaderProperty | 1 |
| `a226d155` | fog height | `_fog_height` | Vector1ShaderProperty | 2 |
| `f18b0581` | horizon color | `_horizon_color` | ColorShaderProperty | {'r': 0.17550720274448395, 'g': 0.564088 |
| `3f957aac` | moon direction | `_moon_direction` | Vector3ShaderProperty | (0,0,0,0) |
| `9280af57` | offset | `_offset` | Vector1ShaderProperty | -0.16 |
| `9fd640e0` | sky color | `_sky_color` | ColorShaderProperty | {'r': 0.0, 'g': 0.3132779598236084, 'b': |
| `fa79a98f` | wind speed | `_wind_speed` | Vector1ShaderProperty | 0 |

### `skyuv` â€” complete node index (layout order)

| Short ID | Full m_ObjectId | m_Type | m_Name | Group | Key slot defaults |
|---|---|---|---|---|---|
| `e03c8062` | `e03c80625e7a457fa2e45ae7df6a250f` | `PositionNode` | Position | â€” | â€” |
| `f55955c8` | `f55955c8367d484e85162c346e39fbfc` | `NormalizeNode` | Normalize | â€” | â€” |
| `a5638aec` | `a5638aec677b4b798184fc3ace213a5a` | `SplitNode` | Split | â€” | â€” |
| `3b3eaecf` | `3b3eaecf5ee54c54884d9a1a67f1eb2d` | `ConstantNode` | Constant | â€” | â€” |
| `dff1eb62` | `dff1eb62bff048bb9230d370be9d2643` | `ArcsineNode` | Arcsine | â€” | â€” |
| `a604aa89` | `a604aa899e79461d8dca4073711c9e6a` | `Arctangent2Node` | Arctangent2 | â€” | â€” |
| `c905564f` | `c905564f8f1e4407a95bb27667592bc3` | `ConstantNode` | Constant | â€” | â€” |
| `e0556493` | `e055649353c5430aad9b22a1f5ec2bd4` | `DivideNode` | Divide | â€” | B=(2,2,2,2) |
| `b17b99ac` | `b17b99ac7b504a78bb11024812bb09b9` | `DivideNode` | Divide | â€” | B=(2,2,2,2) |
| `f932fc2d` | `f932fc2d13794d2082bd298a20ee46ff` | `DivideNode` | Divide | â€” | B=(2,2,2,2) |
| `052b64b9` | `052b64b966a04b5c8b49e3627cd712a2` | `CombineNode` | Combine | â€” | â€” |
| `18be40eb` | `18be40eb1d154c16a1bad27bf6115ba8` | `SubGraphOutputNode` | Output | â€” | â€” |

### `skyuv` â€” properties

| Short ID | Property name | Ref name | Type | Graph default |
|---|---|---|---|---|

### `stars` â€” complete node index (layout order)

| Short ID | Full m_ObjectId | m_Type | m_Name | Group | Key slot defaults |
|---|---|---|---|---|---|
| `cd9f8974` | `cd9f8974ea05448680f9719fc646417d` | `SubGraphNode` | skybox uv | â€” | â€” |
| `17657154` | `17657154c0f84795926ead10e5ab6bc4` | `PropertyNode` | Property â†’ noise scale | â€” | â€” |
| `372f9ff4` | `372f9ff4ed78444caaed87e51c0d7680` | `NoiseNode` | Simple Noise | â€” | Scale=500 |
| `ff84650c` | `ff84650c920f49608e541df5de2c288a` | `DivideNode` | Divide | â€” | B=(10,2,2,2) |
| `217ef3bb` | `217ef3bb049d46eaa1710b36fec73ae0` | `GradientNode` | Gradient | â€” | â€” |
| `bf5fee3a` | `bf5fee3a78794ccd85bd78709af29642` | `GradientNoiseNode` | Gradient Noise | â€” | Scale=10 |
| `23f6455c` | `23f6455c2a6b47539aed1ef131e6e864` | `PropertyNode` | Property â†’ star power | â€” | â€” |
| `eeacde54` | `eeacde54128a4521961815f9d5909f33` | `SampleGradient` | Sample Gradient | â€” | Gradient={'serializedVersion': '2', 'key0': {'r':, Time=0.75 |
| `ea87f90d` | `ea87f90d2e994e1cb12e4b99c24e501e` | `PropertyNode` | Property â†’ intensity | â€” | â€” |
| `6a07d41d` | `6a07d41d147b44e7ab7d7f5b29a5ddb5` | `PowerNode` | Power | â€” | B=(2,2,2,2) |
| `74805973` | `748059734bde4181b4cea67f130b9997` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `33bc31c0` | `33bc31c0ba1c42c1a60e5fc38ad34a2d` | `MultiplyNode` | Multiply | â€” | A=mat, B=mat |
| `99076328` | `99076328a1e74a6f9ff5566102b2e198` | `SubGraphOutputNode` | Output | â€” | â€” |

### `stars` â€” properties

| Short ID | Property name | Ref name | Type | Graph default |
|---|---|---|---|---|
| `f3ab6d48` | intensity | `_intensity` | Vector1ShaderProperty | 1 |
| `107e9757` | noise scale | `_noise_scale` | Vector1ShaderProperty | 100 |
| `b8336c64` | star power | `_star_power` | Vector1ShaderProperty | 0 |

