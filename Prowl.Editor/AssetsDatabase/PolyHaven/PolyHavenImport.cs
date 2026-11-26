// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Rosetta;

namespace Prowl.Editor.PolyHaven;

/// <summary>
/// Downloads one Poly Haven asset into <c>Assets/PolyHaven/&lt;id&gt;/</c>, imports it, and assigns what it made:
/// an HDRI becomes the scene's panorama sky, a texture set is split into its maps, wired into a Lit material and
/// handed to every renderer on the selected object, and a model is imported as its glTF prefab.
/// <para>
/// The download runs on a worker, which is also where the packed arm image is split into occlusion, roughness
/// and metalness textures. <see cref="Poll"/> is called from the editor's UI loop, where importing touches the
/// asset database, and finishes the job. Nothing here runs on the render thread.
/// </para>
/// </summary>
public sealed class PolyHavenImport
{
    /// <summary>The folder under Assets/ that every download lands in.</summary>
    public const string FolderName = "PolyHaven";

    private readonly PolyHavenAsset _asset;
    private readonly string _resolution;
    private readonly string _relativeFolder;
    private readonly List<PolyHavenFiles.PlannedFile> _plan = [];
    private readonly List<(PolyHavenMap Map, string Name)> _unpacked = [];
    private readonly List<string> _importedPaths = [];
    private Task? _download;
    private string? _error;

    public PolyHavenImport(PolyHavenAsset asset, string resolution)
    {
        _asset = asset;
        _resolution = resolution;
        _relativeFolder = $"{FolderName}/{asset.Id}";
        Status = Loc.Get("polyhaven.starting", new { name = asset.Name });
    }

    public PolyHavenAsset Asset => _asset;

    public string Status { get; private set; }

    /// <summary>How much of the job is done, 0 to 1, for the browser's loading bar. Counts each file and the split.</summary>
    public float Progress { get; private set; }

    /// <summary>True once the job has finished, whether it succeeded or not.</summary>
    public bool IsDone { get; private set; }

    /// <summary>True when the job ended without producing an asset.</summary>
    public bool Failed => _error != null;

    public string? Error => _error;

    /// <summary>The project-relative paths that were downloaded and imported.</summary>
    public IReadOnlyList<string> ImportedPaths => _importedPaths;

    /// <summary>Starts the download. Safe to call once; the files are fetched off the UI thread.</summary>
    public void Start()
    {
        if (Project.Current is not { } project)
        {
            _error = Loc.Get("polyhaven.no_project");
            IsDone = true;
            return;
        }

        string assetsPath = project.AssetsPath;
        _download = Task.Run(() => DownloadAsync(assetsPath));
    }

    /// <summary>Finishes the job on the calling (UI) thread once the download is done. A no-op before then.</summary>
    public void Poll()
    {
        if (IsDone || _download is null || !_download.IsCompleted) return;

        if (_download.IsFaulted)
        {
            Exception cause = _download.Exception?.GetBaseException() ?? new InvalidOperationException();
            _error = cause.Message;
            Status = Loc.Get("polyhaven.failed", new { name = _asset.Name, error = cause.Message });
            IsDone = true;
            return;
        }

        try
        {
            Import();
            Progress = 1;
            Status = Loc.Get("polyhaven.imported", new { name = _asset.Name });
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Status = Loc.Get("polyhaven.failed", new { name = _asset.Name, error = ex.Message });
        }
        finally
        {
            IsDone = true;
        }
    }

    private async Task DownloadAsync(string assetsPath)
    {
        JsonObject files = await PolyHavenApi.GetFilesAsync(_asset.Id);

        _plan.Clear();
        _plan.AddRange(PolyHavenFiles.Plan(_asset.Type, files, _resolution, _asset.Id));
        if (_plan.Count == 0)
            throw new InvalidOperationException(Loc.Get("polyhaven.no_files", new { resolution = _resolution }));

        string folder = Path.Combine(assetsPath, FolderName, _asset.Id);
        Directory.CreateDirectory(folder);

        // Each file is one step of the bar, and the split of the packed image is one more.
        int units = _plan.Count + (_plan.Any(f => f.Map == PolyHavenMap.Arm || IsArmFile(f.RelativePath)) ? 1 : 0);

        // The model file lands last, mirroring how Import() orders it: the watcher imports a file the
        // moment it arrives, and a glTF whose .bin or textures are still downloading fails with
        // "Could not resolve external buffer file" before the real import ever runs.
        PolyHavenFiles.PlannedFile[] order = [.. _plan.OrderBy(f => IsModelFile(f.RelativePath) ? 1 : 0)];

        for (int i = 0; i < order.Length; i++)
        {
            PolyHavenFiles.PlannedFile file = order[i];
            Status = Loc.Get("polyhaven.downloading",
                new { name = Path.GetFileName(file.RelativePath), index = i + 1, count = _plan.Count });

            string absolute = Path.Combine(folder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            int step = i;
            await PolyHavenApi.DownloadAsync(file.Url, absolute,
                done => Progress = (float)((step + done) / units));

            // The packed image is the one file a texture set does not keep: it becomes three textures of
            // its own. A model keeps its packed file - the glTF references it - but it is split as well
            // so the material extracted from the model can take the three greyscale maps.
            if (file.Map == PolyHavenMap.Arm)
            {
                UnpackArm(absolute, folder, keepOriginal: false);
                Progress = (float)((i + 1) / units) + (1f / units);
            }
            else if (IsArmFile(file.RelativePath))
            {
                UnpackArm(absolute, folder, keepOriginal: true);
                Progress = (float)((i + 1) / units) + (1f / units);
            }
        }
    }

    /// <summary>
    /// Splits Poly Haven's packed arm image into the maps the engine samples: occlusion from the red channel,
    /// roughness from green and metalness from blue, each written as a greyscale png so it reads the same whether
    /// a shader takes channel 0 or the whole colour. A texture set replaces the packed original; a model keeps
    /// it, because its glTF references it - the extracted material takes the splits instead.
    /// </summary>
    private void UnpackArm(string absolutePath, string assetFolder, bool keepOriginal)
    {
        Status = Loc.Get("polyhaven.unpacking", new { name = Path.GetFileName(absolutePath) });

        string outputFolder = keepOriginal ? Path.GetDirectoryName(absolutePath)! : assetFolder;

        using Aperture.Image image = Aperture.Image.Load(absolutePath,
            new Aperture.DecodeOptions { TargetPixelFormat = Aperture.PixelFormat.Rgba8 });

        int width = image.Width;
        int height = image.Height;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        image.RootFrame.CopyTo(pixels, stride);

        var channels = new (int Channel, PolyHavenMap Map)[]
        {
            (0, PolyHavenMap.Occlusion),
            (1, PolyHavenMap.Roughness),
            (2, PolyHavenMap.Metalness),
        };

        foreach ((int channel, PolyHavenMap map) in channels)
        {
            byte[] single = new byte[pixels.Length];
            for (int i = 0; i + 3 < pixels.Length; i += 4)
            {
                byte value = pixels[i + channel];
                single[i] = value;
                single[i + 1] = value;
                single[i + 2] = value;
                single[i + 3] = 255;
            }

            string name = PolyHavenFiles.FileName(_asset.Id, _resolution, map, ".png");
            using Aperture.Image split = Aperture.Image.FromPixels(single, width, height,
                Aperture.PixelFormat.Rgba8, stride, false);
            split.Save(Path.Combine(outputFolder, name), new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png });

            // _unpacked paths are relative to the asset folder, which for a texture set is the output folder itself.
            _unpacked.Add((map, Path.GetRelativePath(assetFolder, Path.Combine(outputFolder, name)).Replace('\\', '/')));
        }

        if (!keepOriginal) File.Delete(absolutePath);
    }

    private void Import()
    {
        if (EditorAssetBackend.Instance is not { } database)
            throw new InvalidOperationException(Loc.Get("polyhaven.no_project"));

        switch (_asset.Type)
        {
            case PolyHavenType.Hdri:
                ImportHdri(database);
                break;
            case PolyHavenType.Texture:
                ImportTexture(database);
                break;
            default:
                // The model file goes last: the importer resolves its material textures through the asset
                // database, so every texture the glTF references has to be a tracked asset already or the
                // materials come in with the fallback texture instead of the real maps.
                // The maps split from a packed arm image ride along: the glTF keeps referencing the packed
                // original, but the material extracted below takes the three greyscale files instead.
                string? modelPath = null;
                var scheduled = new List<string>(_plan.Count + _unpacked.Count);
                scheduled.AddRange(_plan.Select(f => f.RelativePath));
                scheduled.AddRange(_unpacked.Select(u => u.Name));
                foreach (string relative in scheduled.OrderBy(f => IsModelFile(f) ? 1 : 0))
                {
                    string imported = ImportFile(database, relative);
                    if (modelPath == null && IsModelFile(relative))
                        modelPath = imported;
                }

                // The import built the glTF's materials as sub-assets of the prefab; pull them out into
                // real .mat files so they show up in the Project panel like any other material.
                if (modelPath != null)
                    ExtractModelMaterials(database, modelPath);

                Runtime.Debug.Log($"[PolyHaven] Imported '{_asset.Name}' into Assets/{_relativeFolder}.");
                break;
        }
    }

    private static bool IsModelFile(string relativePath)
        => Path.GetExtension(relativePath).ToLowerInvariant() is ".gltf" or ".glb" or ".obj" or ".fbx";

    /// <summary>Whether a downloaded file is Poly Haven's packed arm image, which has to be split into its three maps.</summary>
    private static bool IsArmFile(string relativePath)
        => !IsModelFile(relativePath)
            && Path.GetFileNameWithoutExtension(relativePath).Contains("_arm_", StringComparison.OrdinalIgnoreCase);

    private string ImportFile(EditorAssetBackend database, string fileName)
    {
        string relativePath = $"{_relativeFolder}/{fileName}";
        database.ImportFile(relativePath);
        _importedPaths.Add(relativePath);
        return relativePath;
    }

    /// <summary>
    /// Turns the materials the model imported as sub-assets into standalone .mat files under a
    /// Materials folder beside it, and points the model's import settings at them, so they are
    /// ordinary Project assets the user can edit and move and the model keeps referencing across
    /// every future reimport.
    /// </summary>
    private void ExtractModelMaterials(EditorAssetBackend database, string modelRelativePath)
    {
        foreach (string path in ExtractMaterials(database, modelRelativePath, _relativeFolder, _unpacked))
            _importedPaths.Add(path);
    }

    /// <summary>
    /// Turns the materials the model imported as sub-assets into standalone .mat files under a
    /// Materials folder beside it, and points the model's import settings at them, so they are
    /// ordinary Project assets the user can edit and move and the model keeps referencing across
    /// every future reimport. Maps split from a packed arm image are wired into the copy so each
    /// slot holds its own greyscale file rather than the packed channels the glTF points at.
    /// </summary>
    public static List<string> ExtractMaterials(EditorAssetBackend database, string modelRelativePath, string assetFolder, IReadOnlyList<(PolyHavenMap Map, string Name)> unpacked)
    {
        var extracted = new List<string>();
        try
        {
            if (database.GetEntry(modelRelativePath) is not { } entry || entry.SubAssets.Length == 0)
                return extracted;
            if (Project.Current is not { } project)
                return extracted;

            string metaPath = MetaFile.GetMetaPath(Path.Combine(project.AssetsPath, modelRelativePath));
            if (!File.Exists(metaPath))
                return extracted;

            MetaFileData meta = MetaFile.Read(metaPath);
            EchoObject settings = meta.Settings ?? EchoObject.NewCompound();

            foreach (SubAssetEntry sub in entry.SubAssets)
            {
                if (sub.Type == null || !typeof(Material).IsAssignableFrom(sub.Type)) continue;

                // Load, not Get: a sub-asset nothing has touched yet stands in the database as an unloaded
                // shell - no shader, no properties - and serializing that would write an empty material.
                Material? material = AssetDatabase.Load<Material>(sub.Guid);
                if (material.IsNotValid()) continue;

                WireUnpackedMaps(database, material, assetFolder, unpacked);

                Guid guid = Importers.ModelImportOverrides.ExtractMaterial(material, modelRelativePath, sub.Name);
                if (guid == Guid.Empty) continue;

                Importers.ModelImportOverrides.SetExtractedMaterial(settings, sub.Name, guid);
                if (database.GuidToPath(guid) is { } materialPath)
                    extracted.Add(materialPath);
            }

            if (extracted.Count == 0) return extracted;

            meta.Settings = settings;
            MetaFile.Write(metaPath, meta);

            // The inspector may hold a cached copy of the old settings; drop it, then rebuild the model
            // against the new ones so its slots resolve to the extracted .mat files instead of embedded copies.
            Inspector.ImportSettingsEditor.Forget(entry.Guid);
            Inspector.AssetImporterEditor.ForgetBaseline(entry.Guid);
            database.Reimport(entry.Guid);

            Runtime.Debug.Log($"[PolyHaven] Extracted {extracted.Count} material(s) from '{modelRelativePath}' into Assets/{assetFolder}/Materials/.");
        }
        catch (Exception ex)
        {
            // The model itself is already imported; a failure here must not fail the whole download.
            Runtime.Debug.LogError($"[PolyHaven] Could not extract materials from '{modelRelativePath}': {ex.Message}");
        }
        return extracted;
    }

    /// <summary>
    /// Points a model material's occlusion, roughness and metalness slots at the greyscale maps split from
    /// Poly Haven's packed arm image, in place of the packed channels the glTF wired in for the sub-asset.
    /// </summary>
    private static void WireUnpackedMaps(EditorAssetBackend database, Material material, string assetFolder, IReadOnlyList<(PolyHavenMap Map, string Name)> unpacked)
    {
        foreach ((PolyHavenMap map, string name) in unpacked)
        {
            string? slot = map switch
            {
                PolyHavenMap.Occlusion => "ambient_occlusion_map",
                PolyHavenMap.Roughness => "specular_roughness_map",
                PolyHavenMap.Metalness => "base_metalness_map",
                _ => null,
            };
            if (slot == null) continue;

            string texturePath = $"{assetFolder}/{name}";
            if (AssetDatabase.Load<Texture2D>(database.GetEntry(texturePath)?.Guid ?? Guid.Empty) is { } texture)
            {
                material.SetTexture(slot, texture);
                material.SetTextureChannel(slot, 0);
            }
        }
    }

    /// <summary>Imports the HDRI, builds a panorama sky material from it and gives it to the edited scene.</summary>
    private void ImportHdri(EditorAssetBackend database)
    {
        string relativePath = ImportFile(database, _plan[0].RelativePath);
        Texture2D? loaded = AssetDatabase.Load<Texture2D>(database.GetEntry(relativePath)?.Guid ?? Guid.Empty);
        if (loaded.IsNotValid())
            throw new InvalidOperationException(Loc.Get("polyhaven.failed_import", new { file = relativePath }));
        Texture2D texture = loaded!;

        Material sky = SceneEnvironment.CreateSkyMaterial(BuiltInSky.Panorama);
        sky.Name = $"{_asset.Name} ({_resolution})";
        sky.SetTexture("_Panorama", texture);

        string materialPath = $"{_relativeFolder}/{_asset.Id}_{_resolution}_sky.mat";
        database.CreateAsset(sky, materialPath);
        _importedPaths.Add(materialPath);

        Scene scene = EditorSceneManager.EditedWorld;
        if (scene.IsValid())
        {
            scene.Environment.SkyMaterial = sky;
            EditorSceneManager.MarkDirty(EditorSceneManager.SceneOf(scene.ActiveSection));
        }
    }

    /// <summary>
    /// Imports the maps of the texture set, wires each into a Lit material and gives that material to every
    /// renderer on the selected object, so a whole model takes it in one go.
    /// </summary>
    private void ImportTexture(EditorAssetBackend database)
    {
        var textures = new Dictionary<PolyHavenMap, Texture2D>();
        var files = new List<(PolyHavenMap Map, string Name)>();

        // The packed image was already split on the worker and is not imported itself.
        foreach (PolyHavenFiles.PlannedFile file in _plan)
            if (file.Map != PolyHavenMap.Arm) files.Add((file.Map, file.RelativePath));
        files.AddRange(_unpacked);

        foreach ((PolyHavenMap map, string name) in files)
        {
            string relativePath = ImportFile(database, name);
            if (AssetDatabase.Load<Texture2D>(database.GetEntry(relativePath)?.Guid ?? Guid.Empty) is { } texture)
                textures[map] = texture;
        }

        Material material = new(Shader.LoadDefault(DefaultShader.Lit))
        {
            Name = $"{_asset.Name} ({_resolution})",
        };

        Texture2D? Map(PolyHavenMap map) => textures.TryGetValue(map, out Texture2D? texture) ? texture : null;

        if (Map(PolyHavenMap.Colour) is { } baseColor)
            material.SetTexture("base_color_map", baseColor);

        if (Map(PolyHavenMap.Normal) is { } normal)
            material.SetTexture("geometry_normal_map", normal);

        if (Map(PolyHavenMap.Occlusion) is { } occlusion)
            material.SetTexture("ambient_occlusion_map", occlusion);

        if (Map(PolyHavenMap.Roughness) is { } roughness)
            material.SetTexture("specular_roughness_map", roughness);

        if (Map(PolyHavenMap.Metalness) is { } metalness)
            material.SetTexture("base_metalness_map", metalness);

        if (Map(PolyHavenMap.Height) is { } displacement)
            material.SetTexture("geometry_height_map", displacement);

        string materialPath = $"{_relativeFolder}/{_asset.Id}_{_resolution}.mat";
        database.CreateAsset(material, materialPath);
        _importedPaths.Add(materialPath);

        int assigned = ApplyToSelection(material);
        Runtime.Debug.Log(assigned > 0
            ? $"[PolyHaven] Created '{materialPath}' with {textures.Count} map(s) and gave it to {assigned} material slot(s)."
            : $"[PolyHaven] Created '{materialPath}' with {textures.Count} map(s). Select a model to have it assigned automatically.");
    }

    /// <summary>Gives <paramref name="material"/> to every slot of every renderer under the selected object.</summary>
    private static int ApplyToSelection(Material material)
    {
        if (Selection.GetActiveAs<GameObject>() is not { } root) return 0;

        int slots = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer is not IMaterialRenderer target) continue;
            for (int slot = 0; slot < target.MaterialSlotCount; slot++)
            {
                target.SetMaterial(slot, material);
                slots++;
            }
        }

        if (slots > 0)
        {
            Scene scene = EditorSceneManager.EditedWorld;
            if (scene.IsValid())
                EditorSceneManager.MarkDirty(EditorSceneManager.SceneOf(scene.ActiveSection));
        }

        return slots;
    }
}
