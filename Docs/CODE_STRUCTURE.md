# Code Structure Audit — Unity Import / Shaders / Registry / CCS

**Status:** research + design only. No code was changed by this audit.
**Repository root:** `Prowl/` (branch `unity-import`).
**Line counts:** total physical lines (`(Get-Content <file>).Count`), taken from the current working tree, not from HEAD.
**Source of truth for "current":** untracked/modified files in the working tree are the live implementation; `HEAD` is behind on the Unity-import work.

---

## 1. Priority summary

| File (repo-relative) | Lines | Distinct responsibilities | Verdict |
|---|---:|---|---|
| `Prowl.Editor/AssetsDatabase/Importers/UnityHierarchyConverter.cs` | 1015 | conversion API, hierarchy tree, component factories, prefab-instance modifications, GUID resolution, YAML helpers | **Do later** — importer area is being edited by other agents |
| `Prowl.Editor/AssetsDatabase/Importers/UnityPackageImporter.cs` | 772 | orchestration, archive extraction, path safety, GUID bridge, material mapping, ScriptableObject conversion, terrain/prefab/scene dispatch, YAML helpers | **Do later** — importer area is being edited by other agents |
| `Prowl.Editor/GUI/Panels/PolyHavenPanel.cs` | 549 | panel layout, header controls, virtualization, card/row drawing, async list state, installed-asset scan, download/delete actions | **Do now — but check `git status` first** (a User-Agent settings agent is active nearby) |
| `Prowl.Editor/AssetsDatabase/PolyHaven/PolyHavenImport.cs` | 480 | job state machine, download ordering, ARM image splitting, import dispatch, model-material extraction, HDRI setup, texture-set setup, selection assignment | **Do now — but check `git status` first** |
| `Prowl.Editor/UnityShaders/SlangCompilePipeline.cs` | 430 | folder walk, entry-point regex parsing, Slang session/compile/diagnostics, pass pairing, ShaderDef emission, report writing, import hook | **Do later** — shader/transpiler agents active |
| `Prowl.Editor/AssetsDatabase/Importers/UnityTerrainConverter.cs` | 415 | lookups/handoff table, layer YAML decode, binary TerrainData scan, heightmap scoring, report asset | **Do later** |
| `Prowl.Editor/UnityShaders/SlangGlslRewrite.cs` | 367 | GLSL regex set, graphics shader writer, compute shader writer, stage rewriting, declaration splitting, dedupe set | **Do later** |
| `Prowl.Editor/AssetsDatabase/PolyHaven/PolyHavenApi.cs` | 348 | HTTP client, JSON asset parsing, file download, URL/file planning, naming rules, type/enum declarations | **Skip for now** — actively edited by another agent (User-Agent extraction: new `PolyHavenUserAgent.cs` / `PolyHavenUserAgentSettings.cs`); revisit after it lands |
| `Prowl.Editor/UnityPackages/UnityPackagesCli.cs` | 344 | verb dispatch, list formatting, safe deletion, update/re-import staging, path-containment/reparse-point helpers | **Do now** |
| `Prowl.Editor/UnityShaders/UnityShaderTranspile.cs` | 295 | folder walk, transpiler invocation, report writing, manual-work marker scan, reflection-based property formatting | **Do later** — transpiler path specifically owned by another agent |
| `Prowl.Editor/AssetsDatabase/UnityPackages/UnityPackageRegistry.cs` | 236 | public registry API, JSON persistence, hashing, change detection | **Later / low value** — under the ceiling |
| `Prowl.Editor/AssetsDatabase/PackageLibrary.cs` | 359 | pre-existing package library | **Leave alone** in this pass (not new Unity-import code) |
| `Prowl.Editor/EditorRegistries.cs` | 828 | pre-existing editor registry surface | **Leave alone** in this pass (separate ticket) |
| `Prowl.Editor/AssetsDatabase/Importers/TextureImporter.cs` | 1166 | legacy texture importer | **Leave alone** in this pass (separate ticket) |
| `Packages/CCS/Assets/CCS/*.cs` | 190 / 91 / 77 / 54 | one component per file | **No action** — this is the pattern to copy |

**Note on the transpiler path from the brief:** `CDS.ProwlFork.United/CDS.ProwlFork.United/UnityShaderTranspiler.cs` does **not** exist in this checkout. The library is referenced via `using CDS.ProwlFork.United;` from `UnityShaderTranspile.cs`, `UnityPackageImporter.cs`, and `UnityHierarchyConverter.cs`. The editor-side wrapper is now `Prowl.Editor/UnityShaders/UnityShaderTranspile.cs` (295 lines), already split out from the old monolith along with `SlangCompilePipeline.cs`, `SlangGlslRewrite.cs`, and the CLI/menu shims. **Do not recreate or re-split the old CDS path.**

---

## 2. Worst offenders — detailed split plans

### 2.1 `UnityPackageImporter.cs` — 772 lines — **DO LATER**

Distinct responsibilities (line numbers from current file):

1. Importer entry point and orchestration: `Import` (50–454).
2. Archive extraction and raw-import loop (107–169), `.meta` archiving (92–100).
3. Unity GUID → Prowl GUID bridge, including `Resources/` load-path fallback (83–90, 171–196).
4. Material conversion and Unity→Lit property mapping (198–252, 713–760).
5. ScriptableObject conversion and volume-profile detection (254–304, 762–771).
6. Terrain conversion step (312–378).
7. Prefab/scene conversion dispatch (379–450).
8. Path safety and I/O: `SanitizeFolderName`, `TryMakeRelativePath`, `ToLoadPath`, `WriteBytes` (460–530).
9. Unity YAML sniffing/parsing/scalar/ref helpers (536–711) — largely duplicated in the hierarchy and terrain converters.

Proposed split (all in `Prowl.Editor/AssetsDatabase/Importers/`):

| New file | Primary type | Moves here (approx.) |
|---|---|---|
| `UnityPackageImporter.cs` | `UnityPackageImporter : AssetImporter` | Only `[ImporterFor]`, `Version`, `Source`, and a thin `Import` that calls the steps below. Target ~130 lines. Keep `RawEntry` if the steps need it, otherwise make it `internal record`. |
| `UnityPackageExtraction.cs` | `internal static UnityPackageExtraction` | Steps 1–2: `Extract(entries, packageFolder, project, report)` returning raw entries + meta archive; write bytes, folder entries, raw import loop, dependency registration. ~110 lines. |
| `UnityGuidBridge.cs` | `internal sealed UnityGuidBridge` | `ArchiveUnityMeta`, `Build(backend)`, `FindUnityGuid`, `ResolveTexture`, `ToLoadPath`, `IsZeroGuid`. Holds the two dictionaries and owns the whole GUID mapping. ~100 lines. |
| `UnityMaterialConverter.cs` | `internal static UnityMaterialConverter` | Step 3 + `ApplyTexture`, `FindTexturePath`, `ReadScaleOffset`, `ReadColor`, `ReadNamedFloat`, `ScalarAt`, plus the named Unity→Prowl property maps as `static readonly` arrays. ~150 lines. |
| `UnityScriptableObjectConverter.cs` | `internal static UnityScriptableObjectConverter` | Step 4 + `IsVolumeProfile` + scalar/ref field mapping. ~80 lines. |
| `UnityTerrainConversionStep.cs` | `internal static UnityTerrainConversionStep` | Step 4b loops over `.terrainlayer` / binary `.asset`, report creation, warning count. ~80 lines. |
| `UnityHierarchyConversionStep.cs` | `internal static UnityHierarchyConversionStep` | Step 5: prefab source map, converter construction, prefab/scene loops, dependency counts. ~90 lines. |
| `UnityPackagePaths.cs` | `internal static UnityPackagePaths` | `SanitizeFolderName`, `TryMakeRelativePath`, `ToLoadPath`, `WriteBytes` (path-safety logic isolated and testable). ~80 lines. |
| `UnityYamlReader.cs` | `internal static UnityYamlReader` | Shared `Decode`, `IsUnityYaml`, `ReadUnityGuid`, `TryParseFirstDocument`, `Get`, `ScalarText`, `TryToString`, `ParseFloat`, `ReadFloat`, `ReadInt`, `ReadUInt`, `ReadVector`, `ReadQuaternion`, `ReadColor`, `ScalarAt`, `SafeParseRef`. Consume from all three converters. ~180 lines. |

Keep `UnityImportAssets.cs` and `UnityFileId.cs` unchanged — they are already small and single-purpose.

**Why later:** this file and the hierarchy converter are the ones other agents are currently working on. Splitting now would create churn against in-flight edits. Do the pure helper extractions (`UnityPackagePaths`, `UnityYamlReader`) first when the area is free, then the conversion steps.

---

### 2.2 `UnityHierarchyConverter.cs` — 1015 lines — **DO LATER**

Distinct responsibilities:

1. Public API: `ConvertPrefab`, `ConvertPrefabByUnityGuid`, `ConvertScene`, `ConvertedPath`, `ConversionResult` (74–205).
2. Hierarchy tree data + construction and parenting: nested `Tree`, `BuildTree`, `ResolveDeferredParents`, `ApplyChildOrder`, `RootForPrefab` (211–432).
3. Component factories: `CreateMeshRenderer`, `CreateLight`, `CreateCamera`, `CreateAudioListener`, `ResolveAudioListenerType` (434–547).
4. Prefab instances and modifications: `InstantiatePrefabInstances`, `FindOrCreateConversion`, `IndexGameObjects`, `IndexComponents`, `ApplyModifications`, `ApplyModification`, `TryApplyTransform`, `TryApplyComponent` (553–763).
5. Asset resolution and registration: `ResolvePrefabGuid`, `ResolveAssetGuid`, `ResolveAsset<T>`, `ResolveMesh`, `Register`, `WritePrefab` (769–877).
6. Hash-based fileID targets: `AddWithChildren`, `AddPathHashes`, `AddTarget` (879–920).
7. YAML helpers: `TryParse`, `Get`, `ScalarText`, `ParseRef`, `IsPrefabObject`, `IsZeroGuid`, number/vector/color readers, `WarnUnsupported` (926–1014).

Proposed split (new folder `Importers/Hierarchy/`, same namespace `Prowl.Editor.Importers` to avoid touching call sites):

| New file | Primary type | Moves here |
|---|---|---|
| `Hierarchy/UnityHierarchyConverter.cs` | `internal sealed UnityHierarchyConverter` | Public API only; orchestrates tree build → instances → write. Holds `PrefabConversion`/`ConversionResult` references and constructor. ~180 lines. |
| `Hierarchy/UnityHierarchyTypes.cs` | `PrefabConversion`, `ConversionResult` records | Move the two nested result types out for reuse by the other partial pieces. ~40 lines. |
| `Hierarchy/UnityHierarchyTree.cs` | `internal sealed UnityHierarchyTree` | `Tree` as a top-level type: dictionaries, `RegisterRootCandidate`, `CurrentRoots`, `RootForPrefab`, `ResolveDeferredParents`, `ApplyChildOrder`, `BuildTree` (GameObjects + Transforms passes). ~280 lines. |
| `Hierarchy/UnityComponentFactory.cs` | `internal static UnityComponentFactory` | All four component factories plus `ResolveAudioListenerType` and the once-only warning. ~140 lines. |
| `Hierarchy/UnityPrefabInstanceApplier.cs` | `internal sealed UnityPrefabInstanceApplier` | Prefab-instance instantiation, indexing, modification application (transform/component property paths). ~220 lines. |
| `Hierarchy/UnityAssetResolver.cs` | `internal sealed UnityAssetResolver` | GUID maps + caches, `ResolvePrefabGuid`, `ResolveAssetGuid`, `ResolveAsset<T>`, `ResolveMesh`, `Register`, `WritePrefab`, `AddPathHashes`, `AddTarget`. ~190 lines. |
| (shared) `UnityYamlReader.cs` | see 2.1 | Replace the duplicated local helpers at 926–1004. |

**Preserve exactly:** `ConvertedPath(string, string)`, `SetPrefabSources`, `ConvertPrefab`, `ConvertScene`, and `ConversionResult.AssetGuid` — `UnityPackageImporter` calls all of them. Also keep the cross-converter lookup `UnityTerrainConverter.FindConvertedTerrain` reachable from the prefab/scene path.

---

### 2.3 `PolyHavenPanel.cs` — 549 lines — **DO NOW**

Distinct responsibilities:

1. Panel shell, title/icon/menu entry, state fields, `OnGUI` composition (34–193).
2. Virtualization: `RowsFor` and the visible-range/thumbnail-request math (149–174, 195–209).
3. Rendering: `DrawGridRow`, `DrawCard`, `DrawRow`, `Hint` (211–327, 544–549).
4. Async list state: `Poll`, `Filtered`, `SetType`, `SetDownloadedTab`, `ForceReload`, `_loading`/`_allLoading`/`_byType` (329–448).
5. Installed-asset scan: `ScanDownloaded`, `ResolutionOf` (450–475).
6. Asset actions: `BuildAssetMenu`, `ReDownload`, `DeleteDownloaded`, `Download`, busy handling (477–542).

Proposed split — **use `public partial class PolyHavenPanel`** in every file so fields and private methods stay accessible and no call site, element ID, or menu registration changes:

| New file | Moves here |
|---|---|
| `GUI/Panels/PolyHavenPanel.cs` | Menu item, `Title`, `Icon`, constants, state fields, `OnGUI` as composition only. Target ≤200 lines. |
| `GUI/Panels/PolyHaven/PolyHavenPanel.Header.cs` | Type tabs, downloaded tab, search field, resolution buttons, refresh, layout toggle, job/status/error/loading hints, `Hint`. |
| `GUI/Panels/PolyHaven/PolyHavenPanel.List.cs` | `RowsFor`, visible range computation, grid/list iteration, thumbnail-request logic. Pure layout math, easy to test. |
| `GUI/Panels/PolyHaven/PolyHavenPanel.Cards.cs` | `DrawGridRow`, `DrawCard`, `DrawRow`, preview fit/draw, installed metadata string formatting. |
| `GUI/Panels/PolyHaven/PolyHavenPanel.Browser.cs` | `Poll`, `Filtered`, `SetType`, `SetDownloadedTab`, `ForceReload`, `ScanDownloaded`, `ResolutionOf`, task caches. Non-UI state; no `Paper` dependency. |
| `GUI/Panels/PolyHaven/PolyHavenPanel.Actions.cs` | `BuildAssetMenu`, `ReDownload`, `DeleteDownloaded`, `Download`, `_job` lifecycle. |

Keep element IDs (`ph_*`) unchanged when moving drawing code; Origami/Paper keys are layout identity.

---

### 2.4 `PolyHavenImport.cs` — 480 lines — **DO NOW**

Distinct responsibilities:

1. Public job state machine: `Status`, `Progress`, `IsDone`, `Failed`, `Error`, `ImportedPaths`, `Start`, `Poll` (32–115).
2. Download orchestration: `DownloadAsync`, model-last ordering, progress units (117–162).
3. ARM image splitting: `UnpackArm`, channel→map table (164–214).
4. Import dispatch and file-kind predicates: `Import`, `IsModelFile`, `IsArmFile`, `ImportFile` (216–270).
5. Model material extraction: `ExtractModelMaterials`, public static `ExtractMaterials`, `WireUnpackedMaps` (272–372).
6. HDRI setup: `ImportHdri`, sky material, scene assignment (374–397).
7. Texture-set setup and selection assignment: `ImportTexture`, `ApplyToSelection` (399–479).

Proposed split (`Prowl.Editor/AssetsDatabase/PolyHaven/`):

| New file | Primary type | Moves here |
|---|---|---|
| `PolyHavenImport.cs` | `public sealed PolyHavenImport` | Constructor + public properties + `Start`/`Poll` + `Import` dispatch. ~130 lines. |
| `PolyHavenDownloader.cs` | `internal sealed PolyHavenDownloader` | `DownloadAsync`, ordering and progress accounting, `Status` updates via callback. ~90 lines. |
| `PolyHavenArmSplitter.cs` | `internal static PolyHavenArmSplitter` | `UnpackArm` + channel table + naming via `PolyHavenFiles.FileName`. Pure image processing. ~80 lines. |
| `PolyHavenFileKinds.cs` | `internal static PolyHavenFileKinds` | `IsModelFile`, `IsArmFile`, extension sets. ~30 lines. |
| `PolyHavenModelMaterialExtractor.cs` | `internal static PolyHavenModelMaterialExtractor` | `ExtractMaterials` (keep public static signature on a forwarding method on `PolyHavenImport` if external callers appear), `WireUnpackedMaps`, meta settings write, cache invalidation, reimport. ~120 lines. |
| `PolyHavenHdriImporter.cs` | `internal static PolyHavenHdriImporter` | `ImportHdri` + panorama sky creation/scene dirtying. ~50 lines. |
| `PolyHavenTextureSetImporter.cs` | `internal static PolyHavenTextureSetImporter` | `ImportTexture`, map→slot mapping, `ApplyToSelection`. ~100 lines. |

**Preserve exactly:** `FolderName`, `Asset`, `Status`, `Progress`, `IsDone`, `Failed`, `Error`, `ImportedPaths`, `Start()`, `Poll()`, and the public static `ExtractMaterials(...)` signature (grep confirms it is currently called only internally at line 280, but tests may use it). No threading changes: download stays on the worker, import stays in `Poll` on the UI thread.

---

### 2.5 `PolyHavenApi.cs` — 348 lines — **SKIP FOR NOW**

At audit time another agent was mid-edit on this file: the fixed `UserAgent` constant had been replaced with `PolyHavenUserAgent.Apply(request.Headers)` and two new files (`PolyHavenUserAgent.cs`, `PolyHavenUserAgentSettings.cs`) had appeared. Do not split this file until that work is committed. The plan below is for later.

Two unrelated types already share one file: `PolyHavenApi` (HTTP + JSON parsing, 57–190) and `PolyHavenFiles` (I/O-free planning/naming, 206–348), plus three public declarations at 16–47.

| New file | Moves here |
|---|---|
| `PolyHaven/PolyHavenTypes.cs` | `PolyHavenType`, `PolyHavenMap`, `PolyHavenAsset` record. |
| `PolyHaven/PolyHavenApi.cs` | HTTP client, `GetAssetsAsync`, `GetFilesAsync`, `ParseAsset`, `DownloadAsync`, `DownloadBytesAsync`, `GetJsonAsync`, `Text`, `Int`. |
| `PolyHaven/PolyHavenFiles.cs` | `Resolutions`, `PlannedFile`, `FileName`, `TextureMaps`, `Plan`, `HdriUrl`, `TextureUrl`, `ModelFiles`, `ColourSource`, `Str`, `Url`, `ExtensionOf`. |

Low urgency — it is under the 400-line ceiling. Do it only while already touching Poly Haven code.

---

### 2.6 `SlangCompilePipeline.cs` — 430 lines — **DO LATER**

Distinct responsibilities: report format (48–56, 201–207, 391–411), folder walk/path validation (62–109, 122–145), entry-point regex parsing (251–286), Slang session/compile/diagnostics (288–361), pass pairing and stubs (211–249), ShaderDef emission via `SlangGlslRewrite` (152–189), import hook (363–384).

| New file | Moves here |
|---|---|
| `UnityShaders/SlangCompilePipeline.cs` | `CompileFolder` only, ~110 lines. |
| `UnityShaders/SlangEntryPointParser.cs` | `FindEntryPoints`, `ParseSize`, `s_shaderAttribute`, `s_numThreads`, `EntryPoint` record, "//-commented attribute" skip rule. ~90 lines. |
| `UnityShaders/SlangSessionCompiler.cs` | `Compile`, `Check`, `ModuleName`, `FolderFileProvider`, `s_moduleName`, `s_identifier`, profile/target/session setup. ~140 lines. |
| `UnityShaders/SlangPassBuilder.cs` | `MakePasses`, `GetSource`, `GraphicsPass` type, vertex/fragment stubbing. ~70 lines. |
| `UnityShaders/CompileReportWriter.cs` | `FinishReport`, `TryWriteReport`, `Describe`, report header/summary formatting — shared with `UnityShaderTranspile` (which has the same three methods today). ~60 lines. |

Do this only after the transpiler agents are done; the report writer is the one piece worth extracting even earlier because it is duplicated.

---

### 2.7 `SlangGlslRewrite.cs` — 367 lines — **DO LATER**

Distinct responsibilities: regex set (21–36), graphics ShaderDef writer (39–83), compute ShaderDef writer (89–137), section/escape helpers (139–148, 285), stage rewriting (150–224), uniform-block rewriting (226–258), layout stripping/normalization (260–283), top-level declaration splitting (288–334), `StageResult`/`OrderedSet` and records (336–367).

| New file | Moves here |
|---|---|
| `UnityShaders/Glsl/GlslShaderDefWriter.cs` | `BuildGraphics`, `BuildCompute`, `Section`, `Escape`, `GraphicsPass`, `ComputeKernelSource` records. ~140 lines. |
| `UnityShaders/Glsl/GlslStageRewriter.cs` | `RewriteStage`, `RewriteBlock`, `StripBindings`, `NormalizeMatrix`, `StageResult`. ~150 lines. |
| `UnityShaders/Glsl/GlslDeclSplitter.cs` | `Split` (directive/declaration tokenizer). ~60 lines. |
| `UnityShaders/Glsl/GlslPatterns.cs` | All static regexes and `OrderedSet`. ~50 lines. |

Keep the internal API (`BuildGraphics`, `BuildCompute`, the two records) unchanged so `SlangCompilePipeline` needs no edits beyond `using` directives.

---

### 2.8 `UnityShaderTranspile.cs` — 295 lines — **DO LATER**

Distinct responsibilities: folder walk/transpile loop (40–198), report writing (200–220), manual-work marker scan (222–244), reflection-based `ShaderProperty` description (246–294).

| New file | Moves here |
|---|---|
| `UnityShaders/UnityShaderTranspile.cs` | `TranspileFolder` only (~120 lines), still calling `UnityShaderTranspiler.Transpile`/`TranspileCompute` from `CDS.ProwlFork.United`. |
| `UnityShaders/CompileReportWriter.cs` | Shared with `SlangCompilePipeline` (`FinishReport`, `TryWriteReport`, `Describe`). |
| `UnityShaders/TranspileManualWorkScanner.cs` | `ManualWorkPattern`, `FindSourceMarkers`, `DescribeProperty`, `FormatValue`. ~90 lines. |

**Do not touch** the `CDS.ProwlFork.United` library or reintroduce `CDS.ProwlFork.United/UnityShaderTranspiler.cs`; another agent owns that area.

---

### 2.9 `UnityTerrainConverter.cs` — 415 lines — **DO LATER**

Distinct responsibilities: public lookups and static handoff table (36–102), layer YAML decode (108–139), binary TerrainData assembly (146–190), layer texture resolution/copy/scalar helpers (213–255), binary layer-order scan (262–296), heightmap scan/scoring (308–399), report asset (402–415).

| New file (folder `Importers/Terrain/`) | Moves here |
|---|---|
| `Terrain/UnityTerrainConverter.cs` | Public API, static `LatestConvertedTerrains` handoff, `ConvertTerrainData` orchestration, `BuildReport`. ~130 lines. |
| `Terrain/UnityTerrainLayerReader.cs` | `ConvertTerrainLayer`, `ResolveTexture`, `CopyLayer`, `GetNode`, `ScalarText`, `ReadFloat`, `ReadVectorMember`, `IsZeroGuid` (or use shared `UnityYamlReader`). ~90 lines. |
| `Terrain/UnityTerrainDataReader.cs` | `TryExtractHeightmap`, `ScoreCandidate`, `ReadSample`, `ReadLayerOrder`, `UnityGuidBytes`, `HexDigit`, all binary constants. ~160 lines. |
| `Terrain/UnityTerrainImportReport.cs` | `UnityTerrainImportReport : Asset`. ~20 lines. |

---

### 2.10 `UnityPackagesCli.cs` — 344 lines — **DO NOW**

Distinct responsibilities: verb dispatch (18–30), list output (32–55), remove + safe deletion (57–158), update/re-import (160–244), path containment/reparse-point safety (246–343).

| New file | Moves here |
|---|---|
| `UnityPackages/UnityPackagesCli.cs` | `[CliCommand]` entry, `Verb`, dispatch. ~50 lines. |
| `UnityPackages/Commands/PackageListCommand.cs` | `ListPackages` projection. ~40 lines. |
| `UnityPackages/Commands/PackageRemoveCommand.cs` | `RemovePackage`, `DeleteTracked`, refresh + unregister. ~110 lines. |
| `UnityPackages/Commands/PackageUpdateCommand.cs` | `UpdatePackage`, staging, hash check, reimport, registry update. ~100 lines. |
| `UnityPackages/UnityPackagePaths.cs` | `Require`, `IsUsablePackagePath`, `DefaultStagingPath`, `PathComparison`, `ResolveInsideAssets`, `IsInside`, `PathsEqual`, `NormalizeRelative`, `IsReparsePoint`, `CrossesReparsePoint`, `ContainsReparsePoint`. ~120 lines. Security-sensitive; isolate so it can be unit-tested on its own. |

Keep the CLI command name, verb enum, and anonymous result shapes stable — tests and scripts consume them.

---

### 2.11 `UnityPackageRegistry.cs` — 236 lines — **LOW PRIORITY**

Only worth splitting if the file is edited for another reason:

- `UnityPackageRegistry.cs` — public API (`List`, `Find`, `Register`, `Unregister`, `IsChanged`, `RegistryPath`). ~130 lines.
- `UnityPackageRegistryStore.cs` — `ComputeHash`, `ResolveSourcePath`, `Read`, `Write`, `UnityPackageEntry` JSON DTO. ~110 lines.

Otherwise leave it alone; it is already single-purpose and under the ceiling.

---

### 2.12 CCS — **NO ACTION, USE AS THE REFERENCE PATTERN**

`Packages/CCS/Assets/CCS/` already follows the target structure:

- `CinematicCameraBrain.cs` — 190 lines (orchestrator)
- `CinematicVirtualCamera.cs` — 91 lines (pose authoring)
- `CinematicImpulse.cs` — 77 lines
- `CinematicNoise.cs` — 54 lines
- (plus `CinematicFollow`/`CinematicLookAt`/`CinematicEase` planned in `Docs/CCS_DESIGN.md` §4 as separate types)

Rule for CCS implementation agents: when `CinematicCameraBrain.cs` passes 400 lines, split blend evaluation, damping/noise composition, and impulse consumption into `CinematicBlendEvaluator`, `CinematicNoiseComposer`, and `CinematicImpulseTracker` helpers, one file each. Do not fold the follow/look-at/noise/impulse behaviours back into the brain file. `Docs/CCS_DESIGN.md` (439 lines) is documentation, not code, and is fine as-is.

---

## 3. General rules for future agents

1. **Size target:** new/edited `.cs` files ≤ **400 lines hard ceiling**, **250 lines soft target**. At 400, split before adding more code. Test files may go to 500. Generated code, vendor code, and `obj/` artifacts are exempt.
2. **One responsibility per file.** A file should have one reason to change. If you cannot describe it in one sentence without "and", split it.
3. **Importers orchestrate only.** `IAssetImporter.Import` may sequence work and register dependencies; it must not contain YAML parsing, path sanitization, texture-channel math, or large format-specific mapping tables. Those belong in `Importers/<Feature>/...` helpers.
4. **Panels render only.** A `DockPanel` file owns layout composition and state fields. Data fetching/polling, list virtualization math, card/row drawing, and asset actions go into `partial class` sibling files (`Panel.Header.cs`, `Panel.List.cs`, `Panel.Cards.cs`, `Panel.Browser.cs`, `Panel.Actions.cs`). Never dump download logic into a panel file.
5. **Extract helpers by kind:**
   - Parsing → `internal static` reader classes (`UnityYamlReader`, `SlangEntryPointParser`, `UnityTerrainDataReader`).
   - Formatting/reporting → `internal static` writer classes (`CompileReportWriter`, `GlslShaderDefWriter`).
   - UI pieces → `partial class` files keyed by concern, not one giant `OnGUI`.
   - Security/path logic → dedicated `UnityPackagePaths` with its own tests.
6. **No duplicated utility code.** The Unity YAML scalar/ref/number helpers currently exist three times (package importer, hierarchy converter, terrain converter); report-writing exists twice (`SlangCompilePipeline`, `UnityShaderTranspile`). Consolidate rather than copy when touching any of them.
7. **Namespaces mirror folders; file name matches the primary type.** At most one public type per file; small private/internal records may be co-located with their user.
8. **Preserve public API when splitting.** Before moving a method, grep for callers (including tests and CLI), keep the original signature as a thin forwarder if uncertain, and make no behaviour changes in a structure-only commit. For Origami/Paper UI, keep element IDs identical.
9. **Coordination:** check `git status` and recent diffs immediately before editing — the tree changes between agents. Files owned by in-flight agents — everything under `AssetsDatabase/Importers/`, `UnityShaders/`, the `CDS.ProwlFork.United` transpiler, and currently `PolyHavenApi.cs` / the User-Agent settings work — are **do later**. The Poly Haven panel/import and package CLI are the best **do now** candidates, provided the pre-edit status check shows no fresh changes to them.
10. **Verify after each split:** build `Prowl.Editor`, then run `Prowl.Editor.Test` (Unity import, shader transpile, Poly Haven, package registry suites). A structural split must leave all tests green with identical output files.

---

## 4. Suggested execution order

1. **Now, independent:** `PolyHavenPanel.cs` partial split (6 files) → `PolyHavenImport.cs` split (7 files) → `UnityPackagesCli.cs` split (5 files). Each is self-contained and does not touch importer/transpiler code. Re-run `git status` before starting each one; if the User-Agent agent has moved into the panel, let it finish first.
2. **After the User-Agent agent lands:** `PolyHavenApi.cs` 3-way split. `CompileReportWriter` extraction only if the shader agents agree it will not clash.
3. **Later, after importer agents finish:** `UnityPackagePaths` + `UnityYamlReader` extraction (these two remove the most duplication), then `UnityPackageImporter` step helpers, then `UnityHierarchyConverter` hierarchy folder, then `UnityTerrainConverter`.
4. **Later, after transpiler agents finish:** `UnityShaderTranspile` manual-work scanner + shared report writer, `SlangCompilePipeline` parser/compiler/pass split, `SlangGlslRewrite` GLSL folder.
5. **Separate tickets, not this pass:** `TextureImporter.cs` (1166), `EditorRegistries.cs` (828), `PackageLibrary.cs` (359).

---

*Report written by the structure-audit subagent. Research/design only — no source files were modified.*
