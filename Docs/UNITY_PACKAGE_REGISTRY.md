# Unity Package Registry — Design Document

**Status:** Design proposal. No implementation code ships with this document.
**Applies to:** `Prowl.Editor` (asset database, packages panel, CLI).

This document specifies a first-class registry for `.unitypackage` archives extracted by `UnityPackageImporter`. Today the importer writes files under `Assets/<sanitized name>` and produces a `UnityImportReport`, but nothing records which files belong to which source archive, so they cannot be re-imported or removed safely. The registry closes that gap: it lets the editor list imported archives, detect a changed source, re-import in place, and remove every file the import created — without ever touching the original `.unitypackage`.

It is deliberately **separate** from `PackageLibrary`. `LibraryPackage` entries are mountable folders under `Packages/<name>` that are linked into `Assets/Packages/<name>` and keep their own files; a Unity import is a one-way extraction of real files directly under `Assets/<name>`. Mixing the two would make `PackageLibrary.Discover()` (which requires a `package.json` and content folder) misreport imports, and would make unmount delete the wrong files. The registry mirrors the `PackageLibrary` API style but owns its own data and persistence.

---

## 1. Data model

### 1.1 `ImportedUnityPackage` record (one entry per extracted archive)

| Field | Type | Meaning / rule |
|---|---|---|
| `name` | string | Unique key, case-insensitive. Default: `SanitizeFolderName(Path.GetFileNameWithoutExtension(sourcePath))` — the same folder name the importer already uses. |
| `sourcePath` | string | **Absolute** path of the original `.unitypackage`. Kept for re-import. Never deleted or modified. |
| `sourceSha256` | string | 64-char lowercase hex digest of the source file bytes, computed once at import/re-import (streaming `SHA256`, never load the whole archive). Changed digest = "source changed". |
| `importedAtUtc` | string | ISO-8601 UTC timestamp of the last successful registration. |
| `outputFolder` | string | Folder under `Assets`, relative, forward slashes, no trailing slash (e.g. `Low Poly Stylized Nature 1.0.4`). Absolute output = `Path.Combine(Project.Current.AssetsPath, outputFolder)`. |
| `trackedPaths` | array of `{ path, kind }` | Every file and folder the import created, relative to the **Assets root** with forward slashes, `kind` = `"file"` or `"folder"`. Includes `outputFolder` itself. Relative-to-Assets makes the safety check in §5 trivial. |
| `version` | string \| null | Best-effort version from package metadata; see §1.3. Empty string when unknown. |
| `reportGuid` | string \| null | Optional GUID of the `UnityImportReport` Asset produced by the import, for diagnostics. |
| `partial` | bool | True when the importer reported skipped entries or caught an exception. Import still succeeded; files are tracked and removable. |

Derived at list time (never persisted): `sourceExists = File.Exists(sourcePath)`, `sourceChanged` (only computed on explicit `update`/`check`), and a display `status`: one of `Complete`, `Partial`, `SourceChanged`, `SourceMissing`.

### 1.2 How `trackedPaths` is captured

Do **not** duplicate the importer's internal bookkeeping. Immediately after a successful import, snapshot the output tree:

1. Start with `root = Assets/<outputFolder>`; push it onto a stack as a folder entry.
2. Walk with an explicit stack (not `SearchOption.AllDirectories` so reparse points can be skipped, see §5). For each entry:
   - skip anything with `FileAttributes.ReparsePoint` (warn);
   - record `path = Path.GetRelativePath(assetsRoot, full).Replace('\\', '/')` and `kind`.
3. This automatically includes raw extracted files, archived `UnityMeta/**`, generated Prowl `.meta` files, and converted prefab/scene assets, even if the importer's conversion steps change later.

### 1.3 Version extraction rule (in priority order)

1. If the archive contains a `package.json` entry (path ending in `/package.json`), parse its `version` field with `System.Text.Json`.
2. Else match a version-like token in the source filename (`\d+(?:\.\d+){1,3}`, e.g. `1.0.4` in `Low Poly Stylized Nature 1.0.4 .unitypackage`).
3. Else `""`; the UI shows `—`.

`name` and `version` are metadata only — they never affect file paths.

### 1.4 Example entry

```json
{
  "schemaVersion": 1,
  "packages": [
    {
      "name": "Low Poly Stylized Nature 1.0.4",
      "sourcePath": "C:/Projects/Demo/External/Low Poly Stylized Nature 1.0.4 .unitypackage",
      "sourceSha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "importedAtUtc": "2026-11-26T10:14:03Z",
      "outputFolder": "Low Poly Stylized Nature 1.0.4",
      "version": "1.0.4",
      "partial": false,
      "reportGuid": "3f2a...c19e",
      "trackedPaths": [
        { "path": "Low Poly Stylized Nature 1.0.4", "kind": "folder" },
        { "path": "Low Poly Stylized Nature 1.0.4/Models/Tree.fbx", "kind": "file" },
        { "path": "Low Poly Stylized Nature 1.0.4/UnityMeta/Models/Tree.fbx.meta", "kind": "file" },
        { "path": "Low Poly Stylized Nature 1.0.4/Models/Tree.prefab", "kind": "file" }
      ]
    }
  ]
}
```

---

## 2. Persistence

**Location:** `<project root>/Library/UnityPackages.json` → `Path.Combine(Project.Current.LibraryPath, "UnityPackages.json")`.

**Format:** UTF-8 JSON with `schemaVersion` and `packages[]`, written with `System.Text.Json` and `JsonSerializerOptions { WriteIndented = true }` (the same options `PackageLibrary.s_jsonOptions` already uses). No new dependency.

**Why `Library/`:**

- `Project.LibraryPath` already exists and is created at project open; it holds derived, machine-local data (`metadata.db`, `EditorState.json`, `cache`, `thumbnails`). This registry is derived data: absolute local source paths, hashes and a manifest of extracted files. It is not authored content.
- Putting it in `Assets/` would make the JSON itself an asset, create a self-tracking problem, and copy it into future package snapshots. Putting it in `ProjectSettings/` would imply it should be shared/committed, but its absolute paths and hashes are machine-specific. `Packages/` is reserved for `LibraryPackage` mounts with their own `package.json`.
- Keeping it in one file makes list/update/remove atomic at the record level and easy to inspect or repair.

**I/O rules:**

- `Load()` on startup/first use: missing file → empty registry (no error); corrupt file → log a warning, rename to `UnityPackages.json.corrupt` (never silently delete) and start empty. Unknown future `schemaVersion` → treat as read-only and warn.
- `Save()` writes to `UnityPackages.json.tmp` in the same directory then `File.Move(tmp, path, overwrite: true)` — atomic, so a crash cannot leave a truncated registry.
- All mutation is editor-main-thread only, matching `CliCommandAttribute` and asset database rules. Name lookups are case-insensitive.

---

## 3. Workflows

### 3.1 Register after import

At the end of `UnityPackageImporter.Import` (after conversion, alongside `ctx.SetMainAsset(report)`), call `UnityPackageRegistry.Register(...)` with the source path, folder, computed hash, extracted version, report GUID, `partial` flag, and the snapshot from §1.2. Registration happens only when the import returned success — including partial success, so a partial import remains removable. Re-registering an existing name replaces its record (the importer already overwrote the same files); `Register` raises `Changed`.

### 3.2 List in the UI

`PackagesPanel` gains a second section below the existing mount list, "Imported Unity Packages", populated from `UnityPackageRegistry.List()`. Each row shows: name, version, source filename, imported date, a `partial` marker, and a status badge (`Up to date` / `Source changed` / `Source missing` / `Partial`). Buttons: **Update**, **Remove** (with confirmation), **Reveal source**. Missing/changed source is computed only on panel rescan or an explicit check, not on every frame (hashing large archives is expensive). New locale keys follow the existing `packages.*` convention, e.g. `unitypkg.heading`, `unitypkg.update`, `unitypkg.source_missing`. Rescan is triggered by the registry `Changed` event, mirroring `PackageLibrary.StateChanged`.

### 3.3 Update / re-import when the source hash changes

`Update(name, force)`:

1. Find the entry; if `sourcePath` no longer exists → error `Source file is missing`, do nothing.
2. Recompute SHA-256. If it equals `sourceSha256` and `force == false` → return `up-to-date` (no file changes).
3. **Delete then re-import** (not an overlay): call `Remove(name)` (§3.4) to clear tracked outputs and the entry, then re-run the normal import pipeline on the original `.unitypackage` (`UnityPackageImporter`), which registers a fresh entry with the new hash, timestamp, version, and snapshot.
4. If the re-import fails partway, still register whatever was written, with `partial = true`, so `Remove` can clean it; report the failure. The original archive is intact, so a retry is always possible.
5. Refresh the asset database (see §4).

`UpdateAll()` (CLI `--all`) recomputes every hash and updates only entries whose digest changed; it skips missing sources and reports them. Optional `update --source <path>` re-points a moved archive before re-importing.

### 3.4 Remove

`Remove(name, confirm)`:

1. Resolve the entry and refresh the on-disk snapshot if desired.
2. Delete tracked paths **deepest-first** (`path` length descending): files with `File.Delete`, folders only when empty with `Directory.Delete(path, recursive: false)`.
3. For each path: reject anything not under the Assets root, and skip/ warn on reparse points (§5). Missing paths are ignored — removal is idempotent.
4. Remove the output folder itself if it still exists and is empty and is not a reparse point.
5. Remove the registry entry, `Save()`, refresh the asset database, raise `Changed`.
6. If any deletion failed (locked file), keep the entry so the user can retry and report the failures. Never delete the `.unitypackage`.

### 3.5 Missing source files and partial imports

- **Missing source:** `list` and UI show `Source missing`. `Update` refuses; `Remove` still works because it depends only on tracked paths. No automatic deletion when a source disappears.
- **Partial import:** recorded with `partial = true`; shown in the UI and CLI. The entry is fully removable and updatable. The warning count from `UnityImportReport` explains what was skipped.
- **Orphaned files:** files a user adds under the output folder after import are not in `trackedPaths` and are left alone; `Remove` deletes only what the registry knows about.

---

## 4. Integration points

### 4.1 New `UnityPackageRegistry` (static class, `Prowl.Editor` namespace)

Placed next to `PackageLibrary.cs` (e.g. `AssetsDatabase/UnityPackageRegistry.cs`). API mirrors `PackageLibrary`'s style (`Find`, `out string error`, `Debug.Log*`, a `Changed` event):

```
event Action Changed
IReadOnlyList<ImportedUnityPackage> List()
ImportedUnityPackage? Find(string name)
bool Register(UnityImportReport report, string sourcePath, string outputFolder, string version, bool partial, out string error)
bool Update(string name, bool force, out string error)
bool Remove(string name, out string error)
void Save() / void Load()
```

It calls `EditorAssetBackend.Instance?.Refresh()` after mutations, and `ScriptAssemblyManager.RequestRecompile()` if tracked paths include a `.cs` file — the same refresh policy as `PackageLibrary.RefreshDatabase`.

### 4.2 `UnityPackageImporter`

One call to `Register` at the end of `Import`, using data already available (`ctx.AbsolutePath`, `report.OutputFolder`, `report.SourcePath`, the report asset's GUID). No change to extraction/conversion behavior.

### 4.3 `PackagesPanel`

Add the registry section described in §3.2; keep the existing mount list unchanged. `RescanIfNeeded()` also reads `UnityPackageRegistry.List()`, subscribed to `Changed`. Status text reuses the existing `_status` area.

### 4.4 CLI command surface

New static class `UnityPackageCli` (pattern of `UnityShaderTranspileCli`), discovered automatically by `EditorRegistries` via `CliCommands.Scan`. One command with a verb enum, following the `asset` command's verb pattern:

```
[CliCommand("unity_packages", "... list / update / remove imported .unitypackage archives")]
public static object UnityPackages(
    [CliArg("verb")] UnityPackageVerb verb,       // List | Update | Remove
    [CliArg("name", "...")] string name = "",     // required for update/remove unless --all
    [CliArg("all", "...")] bool all = false,      // update all changed / list all (default)
    [CliArg("force", "...")] bool force = false,  // re-import even when hash matches
    [CliArg("confirm", "...")] bool confirm = false); // required for remove, as with 'asset'
```

Return anonymous objects/arrays so `CliCommands.ToJson` serializes them directly. Examples:

- `unity_packages list` → `{ count, packages: [{ name, version, sourcePath, sourceSha256, importedAtUtc, outputFolder, sourceExists, status, trackedCount, partial }] }`
- `unity_packages update --name "Low Poly Stylized Nature 1.0.4"` → `{ name, changed, status, error }`
- `unity_packages update --all` → `{ results: [...] }`
- `unity_packages remove --name "..." --confirm` → `{ name, removed, deletedFiles, failedPaths }`

CLI runs on the editor main thread, so it can safely call registry methods and refresh the asset database.

---

## 5. Failure modes and safety rules

1. **Never delete outside `Assets`.** Store only normalized relative paths. Before any delete, `Path.GetFullPath(Path.Combine(assetsRoot, rel))` must equal or start with `assetsRoot + Path.DirectorySeparatorChar`; reject rooted paths, `..`, drive letters (`:`), and UNC paths. Treat a bad entry as corrupt, skip it with a warning, and keep the rest of the record.
2. **Never follow directory junctions.** Check `FileAttributes.ReparsePoint` (the same test as `PackageLibrary.IsMount`) before enumerating or deleting. Reparse points are skipped during snapshot, skipped during deletion, and the output folder itself is only removed with a non-recursive delete if it is not a link. This covers Windows junctions and symlinks.
3. **Keep the original `.unitypackage`.** The source path is read-only. Removal targets tracked paths under `Assets` only, and the source path is explicitly excluded from the tracked set if the user placed the archive inside the output folder.
4. **No path collisions:** refuse to register when the output folder is `Assets/Packages` or when two entries resolve to the same output folder; never overwrite a real folder that belongs to another package.
5. **Atomic persistence and recovery:** temp-file + move on save; corrupt file preserved as `.corrupt`; unknown schema version is read-only.
6. **Locked/missing files:** skip with a warning, keep the registry entry, report failures; next `Remove` retries. Partial failure never makes the registry lie about what remains on disk.
7. **Expensive hashing:** SHA-256 is streamed and cached; recomputed only on import/re-import/explicit check, never during every UI frame or `list` of many packages.
8. **Source moved:** reported as `Source missing`; user re-points with `update --source`. No guessing, no auto-delete.

---

## 6. Suggested tests (design checklist)

- Import → entry has correct name/version/hash and a tracked set covering raw files, `UnityMeta/**`, converted assets and the output folder.
- `list` shows `SourceChanged` after the archive bytes change; `update` re-imports and changes the hash/timestamp.
- `update --force` re-imports with an unchanged hash; update on a missing source fails without deleting files.
- `remove` deletes only tracked files/folders, leaves the `.unitypackage` and unrelated sibling folders, refreshes the database, and is idempotent on a second call.
- A junction inside the output folder is neither traversed nor deleted through; a junction as the output folder is unlinked, not followed.
- A tracked path with `..`/absolute path injected into the JSON is refused.
- Corrupt registry file is preserved as `.corrupt` and yields an empty registry; save is atomic.
- Partial import registers with `partial = true` and remains removable/updatable.

---

## 7. Files this design would touch (implementation phase, none changed here)

- **Add** `Prowl/Prowl.Editor/AssetsDatabase/UnityPackageRegistry.cs` and `.../UnityPackageCli.cs`.
- **Edit** `UnityPackageImporter.cs` (register call), `PackagesPanel.cs` (new section + rescan subscription), `Prowl.Editor/Resources/Locale/*.json` (new keys), and `Prowl.Editor.Test/` (new registry tests).
- **No change** to `PackageLibrary` behavior or to `Library/UnityPackages.json` at design time; the file is created at runtime on first registration.
