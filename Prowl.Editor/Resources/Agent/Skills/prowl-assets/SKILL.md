---
name: prowl-assets
description: How Prowl assets work for scripts. Use when a script references textures, materials, meshes, prefabs, clips, scenes or other assets, chooses between a plain field and AssetRef, loads assets by path at runtime, creates assets in code, or when assets unload, go missing, or are not in a build.
---

# Prowl assets

## The model

- Every asset derives from `Asset` (an `EngineObject`). A database asset has a GUID (`AssetID`), a path (`AssetPath`) and a `Name` taken from its file name, or the part after `#` for a sub asset.
- **One object per GUID for the whole session.** Unloading frees the data but keeps the object, and loading, reimporting or reverting refills that same object. So a reference you hold never goes stale or gets swapped for another object.
- `asset.State` is an `AssetState` (a top level enum in `Prowl.Runtime`): `Loaded`, `Unloaded`, `Loading`, `Missing` or `Failed`. An unloaded or missing asset is still `IsValid()`. Check `IsLoaded` or `IsMissing` when the content matters.
- Reading an asset's data loads it on demand, blocking. A missing or failed asset reads as empty and logs one warning.
- A runtime asset made with `new Material(shader)`, `new Mesh()` or `Clone()` has no GUID. It is always loaded, never unloaded by the engine, and belongs to whoever made it. Dispose it when done.
- Never call `Dispose()` or `Destroy()` on a database asset, it throws. Use `AssetDatabase.Unload` if you really must.

## Asset packages

A project's `Packages/` folder holds asset packages that mount into `Assets/` on demand. A package is a folder
with a `package.json` (`name`, `version`, `description`, and an optional `assets` naming the content folder,
which defaults to `Assets`) plus that content folder.

- The editor lists them in **Window → General → Packages**. A loaded
  package appears under `Assets/Packages/<name>` as a directory link to the package's content folder, so its
  files import normally and keep their own `.meta` GUIDs. Unloading deletes only the link; the package's files,
  metas and GUIDs stay, so loading it again restores the same asset identities.
- Package scripts compile with the project while the package is loaded.
- From editor code: `PackageLibrary.Discover()`, `PackageLibrary.Create(name, out error)` and
  `PackageLibrary.SetLoaded(name, loaded, out error)`.
- A mounted package's assets are assets of the project: they ship in builds, resolve in scenes, and go missing
  when the package is unloaded.

## Referencing assets from a script

| | Plain field: `public Material Mat;` | `public AssetRef<Material> Mat;` |
| --- | --- | --- |
| Saved as | the GUID | the GUID |
| Keeps the asset loaded | yes, while the object holding it is reachable | no |
| Loaded with the scene | yes | no, call `Load()` or `LoadAsync()` |
| Shipped in builds | yes | yes |

Use a plain field for almost everything. Use `AssetRef<T>` only for things needed maybe later, such as a list of levels or an optional high resolution texture.

```csharp
using Prowl.Runtime;
using Prowl.Runtime.Resources;

public class Spawner : Component
{
    public PrefabAsset Enemy;                // held and loaded with the scene
    public AssetRef<SceneAsset> NextLevel;        // only a reference, loads when asked
    [NotHeld] public Texture2D DebugTex;     // referenced, but allowed to unload

    public override void Start()
    {
        var level = NextLevel.Get();         // never loads, may be unloaded
        var loaded = NextLevel.LoadImmediate();  // blocks until loaded
    }
}
```

`AssetRef<T>`: `Get()` never loads, `LoadImmediate()` blocks, `LoadAsync()` is async, `IsEmpty`, `AssetID`, and it converts implicitly from `T`. Wrapping a runtime asset (no GUID) gives an empty ref and a warning.

## What keeps an asset loaded

A background walk starts from every live world, loads in progress, held assets, roots added with `AssetDatabase.AddRoot`, and statics marked `[HeldStatic]`. It follows instance fields (collections too). Anything it reaches stays loaded. An asset it does not reach for two walks and longer than the grace period (10 seconds by default) is unloaded.

The walk does **not** see static fields (unless `[HeldStatic]`), lambda captures, delegates or locals in async methods. An asset kept only there will unload. Put it in a field, mark the static `[HeldStatic]`, or call `AssetDatabase.Hold(asset, owner)`, which holds it until `owner` is garbage collected or you call `Release`.

If a field points at an asset that was unloaded, the next read reloads it (blocking) and logs a warning telling you to keep it in a field or hold it.

## AssetDatabase

| Call | Loads? |
| --- | --- |
| `AssetDatabase.Get(guid)`, `Get<T>(guid)` | never |
| `AssetDatabase.Load<T>(guid)` | blocks |
| `await AssetDatabase.LoadAsync<T>(guid)` | async |
| `AssetDatabase.FindResource<T>("Textures/Paint")` | blocks |
| `await AssetDatabase.FindResourceAsync<T>("Sfx/Hit")` | async |
| `AssetDatabase.FindAllResources<T>("Folder")` | blocks, and the results are not held afterwards |
| `AssetDatabase.LoadGroup(assets)`, `Preload(sceneAsset)` | returns an awaitable `AssetGroup` with `Progress` |
| `AssetDatabase.Explain(asset)` | reports state, size and what holds it, for debugging |

## Loading by path at runtime

A load path is the path below the nearest `Resources` folder, without the extension: `Assets/Enemies/Resources/Textures/Paint.png` loads as `Textures/Paint`. Sub assets add `#Name`.

Builds only ship the scenes in the build, everything they reference (plain fields and `AssetRef`), everything inside `Resources` folders, and what those reference. Assets under `Editor` folders are left out. **An asset you only load by path or GUID must be in a `Resources` folder**, or a player build reports it is not in the asset manifest.

## Sub assets, prefabs and scenes

- A model file imports as a `PrefabAsset` with sub assets: meshes, materials, the avatar and animation clips. Address one as `Models/Hero.fbx#Run`. `Library/prowl command asset info --path Models/Hero.fbx` lists them.
- Spawn a prefab with `GameObject.Instantiate(prefab)`, with overloads for position, rotation, parent and scene.
- Prefabs do not nest. A prefab instance inside another prefab is flattened into plain objects when the outer prefab is saved.
- The prefab a model imports as is read only. To change it, instantiate it, edit the instance, and save a new prefab with `prefab create`.
- `SceneAsset` is the saved scene asset. `Scene` is a live world. `level.Load()` replaces Main's content with the level, `level.LoadAdditive()` adds it as another section, `level.Open()` makes a new dormant world from it. Each returns a `SceneLoad`: await it for the loaded `SceneSection`, read `Phase` and `Progress`, or call `Wait()` to block until it is ready. Loads apply at the end of the frame. `AssetRef<SceneAsset>` has the same three methods.

## Creating and saving assets (editor code only)

`EditorAssetBackend.Instance.CreateAsset(asset, "Materials/New.mat")` writes the file, imports it and turns that same object into the database asset. `SaveAsset(asset)` writes changes back. Files like `.fbx` and `.png` are imported, not saved, so they can not be written this way. From the CLI use `asset create`, `set`, `material` and `importer` instead.

## Poly Haven (online library)

**Window → General → Poly Haven** browses Poly Haven's CC0 HDRIs, textures and models through their public API and imports the picked one into `Assets/PolyHaven/<id>/`. The API needs no key but requires a naming User-Agent, which the client sends, and the panel shows the "Powered by Poly Haven" credit the terms ask for.

- **An HDRI** downloads at the chosen resolution (1k-8k `.hdr`), imports, and becomes a panorama material that is set as the edited scene's `SkyMaterial`.
- **A texture set** keeps one source per map: the colour map, the OpenGL normal map, the displacement map, and the packed `arm` image. The packed image is split on the download thread into `_occlusion`, `_roughness` and `_metallic` greyscale pngs and the packed original is deleted, so the project never holds the same data twice. Assets without an `arm` image fall back to their separate `AO`, `Rough` and `Metal` maps. The DirectX normal map and the `rough_ao`, `spec`, `Bump` and `blend` files are never imported.
- The maps become a Lit material with `base_color_map`, `geometry_normal_map`, `ambient_occlusion_map`, `specular_roughness_map`, `base_metalness_map` and `geometry_height_map`, and that material is given to every slot of every renderer under the selected object.
- **A model** downloads its glTF, `.bin` and textures keeping the glTF's relative paths, and imports as its model prefab. The packed `arm` image the glTF references stays on disk, but it is split into `_occlusion`, `_roughness` and `_metallic` greyscale pngs next to the other textures. Every material the model imports then becomes a standalone `.mat` under `Assets/PolyHaven/<id>/Materials/`, wired to the base colour, normal, occlusion and the three unpacked maps, so models show their textures like texture sets do and remain editable, movable Project assets.

The panel lists a page at a time and adds rows as it is scrolled, and a 128 pixel preview is fetched only for the rows on screen (`PolyHavenThumbnails`), so browsing the library costs a couple of small images rather than the whole catalogue. Assets already present under `Assets/PolyHaven/<id>/` are shown greyed out as installed; right-click one to re-download it at another resolution (its folder is deleted first) or to delete it.

File names carry meaning: `nor` in a name imports as a normal map and `mask`/`roughness`/`metallic`/`occlusion`/`displacement` as linear data, which is why the downloaded maps are named that way. `PolyHavenFiles.Plan` decides what to fetch and is covered by `Prowl.Editor.Test/PolyHavenTests.cs`.

## Gotchas

- Never use `?.`, `??`, `??=`, `== null`, `!= null`, `is null` or `is not null` on an asset or any `EngineObject`. They are compile errors (PROWLEO001, PROWLEO002, PROWLEO003). Use `x.IsValid()` and `x.IsNotValid()`.
- A scene field on a component is `AssetRef<SceneAsset>`. A plain `SceneAsset` field loads the scene and everything it uses with the component (warning PROWLSC002).
- Never block on a task in a script with `.Result`, `.Wait()` or `GetAwaiter().GetResult()` (PROWLTH001). Loads finish on the main thread, so the wait never returns. `await` instead, or call the blocking `Load()`.
- Blocking loads hitch the frame. Use the async calls or `LoadGroup` for large content.
- `Material.LoadDefault(...)` returns a new copy you own. The other built in `LoadDefault` calls return a shared asset.
