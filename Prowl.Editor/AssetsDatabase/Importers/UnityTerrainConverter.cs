// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using CDS.ProwlFork.United;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>
/// The terrain-specific half of a <c>.unitypackage</c> import. Unity keeps a terrain's layers in
/// readable YAML (<c>.terrainlayer</c>) but its heightmap in a binary <c>TerrainData</c> asset;
/// this converter recovers what it can of both and writes real Prowl <see cref="TerrainData"/> assets.
/// </summary>
/// <remarks>
/// Prowl's terrain model (<c>Prowl.Runtime/Resources/TerrainData.cs</c>) is:
/// <list type="bullet">
/// <item><c>HeightmapResolution</c> (line 158) - samples on a side, 33..8193;</item>
/// <item><c>Size</c> / <c>Height</c> (lines 161, 168) - metres across and metres of vertical range;</item>
/// <item><c>Layers</c> (line 177) - a list of <see cref="TerrainLayer"/> with <c>Albedo</c>,
/// <c>NormalMap</c>, <c>Tiling</c>, <c>Roughness</c>, <c>HeightBlend</c>, <c>HexTiling</c> and
/// <c>MacroVariation</c> (lines 22-40);</item>
/// <item><c>SetHeights(float[])</c> (line 308) - row-major samples from 0 at the base to 1 at the top.</item>
/// </list>
/// <para>
/// Terrains are placed in scenes by <c>Prowl.Runtime.Terrain.TerrainComponent</c>
/// (<c>Prowl.Runtime/Components/Terrain/TerrainComponent.cs</c> line 28): its <c>Data</c> field
/// (line 30) holds the <see cref="TerrainData"/> asset and the GameObject's transform places it.
/// <c>Prowl.Runtime.TerrainCollider</c>
/// (<c>Prowl.Runtime/Components/Physics/TerrainCollider.cs</c> line 21) samples that same data for
/// collision, but Unity's physics-material settings have no direct Prowl equivalent.
/// </para>
/// This converter only makes data assets. Placing a <c>!u!218 Terrain</c> component is left to
/// <see cref="UnityHierarchyConverter"/>, which can resolve a Unity TerrainData GUID through
/// <see cref="FindConvertedTerrain"/> or <see cref="TerrainGuidForUnityGuid"/>.
/// </remarks>
public sealed class UnityTerrainConverter
{
    // The class id Unity uses for a .terrainlayer document.
    private const long TerrainLayerClassId = 1953259897;

    // Unity writes height arrays as an int32 sample count followed by 16-bit samples. These are the
    // resolutions a Unity terrain can have; trying all of them lets us find a real heightmap without
    // writing a full Unity binary type-tree reader.
    private static readonly int[] CandidateResolutions = [33, 65, 129, 257, 513, 1025, 2049, 4097];

    // Below this smoothness score a sample looks like a heightmap rather than texture/image bytes.
    private const double HeightmapSmoothnessLimit = 0.02;

    // Unity terrain data does not carry a size Prowl can read without its full binary type tree. The
    // common Unity default (500m across, 600m tall) is used instead and logged as an approximation.
    private const float AssumedTerrainSize = 500f;
    private const float AssumedTerrainHeight = 600f;

    private readonly EditorAssetBackend _backend;
    private readonly IReadOnlyDictionary<string, Guid> _prowlGuidByUnityGuid;
    private readonly IReadOnlyDictionary<string, string> _unityGuidToRelative;
    private readonly Action<string> _warn;
    private readonly Dictionary<string, TerrainLayer> _layersByUnityGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _terrainGuidByUnityGuid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _terrainGuidByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _colliderWarned;
    private bool _sizeWarned;

    // A small hand-off table: the hierarchy converter is created after this one and can look up the
    // Prowl asset for a !u!218 Terrain's m_TerrainData reference without changing its constructor.
    private static readonly Dictionary<string, Guid> LatestConvertedTerrains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Look up a terrain data asset converted during the current package import.</summary>
    public static Guid? FindConvertedTerrain(string unityGuid)
        => LatestConvertedTerrains.TryGetValue(unityGuid, out Guid guid) && guid != Guid.Empty ? guid : null;

    /// <summary>Clear the hand-off table at the start of a package import.</summary>
    public static void BeginPackageImport() => LatestConvertedTerrains.Clear();

    /// <summary>Converted terrain layers, keyed by the Unity GUID of the <c>.terrainlayer</c> asset.</summary>
    public IReadOnlyDictionary<string, TerrainLayer> Layers => _layersByUnityGuid;

    /// <summary>Converted Prowl terrain data assets, keyed by the Unity GUID of the source asset.</summary>
    public IReadOnlyDictionary<string, Guid> TerrainGuids => _terrainGuidByUnityGuid;

    public UnityTerrainConverter(
        EditorAssetBackend backend,
        IReadOnlyDictionary<string, Guid> prowlGuidByUnityGuid,
        IReadOnlyDictionary<string, string> unityGuidToRelative,
        Action<string> warn)
    {
        _backend = backend;
        _prowlGuidByUnityGuid = prowlGuidByUnityGuid;
        _unityGuidToRelative = unityGuidToRelative;
        _warn = warn;
    }

    /// <summary>
    /// The Prowl terrain data asset made from a Unity TerrainData asset, for a scene/prefab converter
    /// resolving a <c>!u!218 Terrain</c> component's <c>m_TerrainData</c> reference.
    /// </summary>
    public Guid? TerrainGuidForUnityGuid(string unityGuid)
        => !string.IsNullOrWhiteSpace(unityGuid) && _terrainGuidByUnityGuid.TryGetValue(unityGuid, out Guid guid)
            && guid != Guid.Empty ? guid : null;

    /// <summary>The same lookup by the package-relative path of the source <c>.asset</c>.</summary>
    public Guid? TerrainGuidForPath(string relativePath)
        => _terrainGuidByPath.TryGetValue(relativePath, out Guid guid) && guid != Guid.Empty ? guid : null;

    /// <summary>
    /// Read one Unity <c>.terrainlayer</c> YAML document. Its diffuse and normal textures are resolved
    /// through the importer's Unity GUID map; tiling and smoothness become Prowl layer properties.
    /// </summary>
    public TerrainLayer? ConvertTerrainLayer(byte[] bytes, string sourceRelative, string unityGuid)
    {
        IReadOnlyList<UnityDocument> documents;
        try
        {
            documents = UnityYaml.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        }
        catch (Exception ex)
        {
            _warn($"[UnityPackageImporter] Could not parse terrain layer '{sourceRelative}': {ex.Message}");
            return null;
        }

        UnityDocument? doc = documents.FirstOrDefault(d => d.ClassId == TerrainLayerClassId)
                             ?? documents.FirstOrDefault(d => d.Fields.ContainsKey("m_DiffuseTexture"));
        if (doc == null) return null;

        var layer = new TerrainLayer
        {
            Albedo = ResolveTexture(GetNode(doc, "m_DiffuseTexture")),
            NormalMap = ResolveTexture(GetNode(doc, "m_NormalMapTexture")),
            Tiling = ReadVectorMember(doc, "m_TileSize", "x") ?? 10f,
            Roughness = 1f - Math.Clamp(ReadFloat(GetNode(doc, "m_Smoothness")) ?? 0f, 0f, 1f),
        };
        // Unity's mask-map alpha is a height blend; Prowl reads the height from the albedo's alpha.
        // There is no direct equivalent, so the default blend is kept.

        if (!string.IsNullOrEmpty(unityGuid))
            _layersByUnityGuid[unityGuid] = layer;

        return layer;
    }

    /// <summary>
    /// Convert one binary Unity TerrainData asset into a Prowl <see cref="TerrainData"/>. Heights are
    /// recovered from the binary sample array when the scan finds a plausible one; otherwise a flat,
    /// correctly sized heightmap is written and a warning is logged.
    /// </summary>
    public Guid ConvertTerrainData(byte[] bytes, string unityGuid, string sourceRelative, string outputRelative)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(sourceRelative);

        if (!TryExtractHeightmap(bytes, out int resolution, out float[]? heights))
        {
            _warn($"[UnityPackageImporter] Could not find a readable heightmap in '{sourceRelative}'. " +
                  $"Writing a flat {resolution}x{resolution} placeholder; the terrain's shape was not recovered.");
            heights = new float[resolution * resolution];
            Array.Fill(heights, 0.5f);
        }

        var data = new TerrainData { Name = name };
        data.ResizeHeightmap(resolution);
        data.Size = AssumedTerrainSize;
        data.Height = AssumedTerrainHeight;
        if (!_sizeWarned)
        {
            _sizeWarned = true;
            _warn($"[UnityPackageImporter] Unity TerrainData size and height are not readable without its full " +
                  $"binary type tree; using {AssumedTerrainSize:g}m across and {AssumedTerrainHeight:g}m tall for " +
                  "converted terrains.");
        }

        List<TerrainLayer> layers = ReadLayerOrder(bytes);
        if (layers.Count == 0 && _layersByUnityGuid.Count > 0)
        {
            _warn($"[UnityPackageImporter] Could not recover the layer order in '{sourceRelative}'; " +
                  "using all converted terrain layers in import order.");
            layers = _layersByUnityGuid.Values.Select(CopyLayer).ToList();
        }

        // The fresh asset starts with four empty layers. Drop the extras through the proper API so
        // the package's weight maps are rebuilt to match, then add the Unity layers in their order.
        while (data.Layers.Count > 1)
            data.RemoveLayer(data.Layers.Count - 1);
        data.Layers[0] = layers.Count > 0 ? CopyLayer(layers[0]) : new();
        for (int i = 1; i < layers.Count; i++)
            data.AddLayer(CopyLayer(layers[i]));

        data.SetHeights(heights);

        _backend.CreateAsset(data, outputRelative);
        Guid guid = data.AssetID;
        if (guid != Guid.Empty)
        {
            if (!string.IsNullOrEmpty(unityGuid))
            {
                _terrainGuidByUnityGuid[unityGuid] = guid;
                LatestConvertedTerrains[unityGuid] = guid;
            }
            _terrainGuidByPath[sourceRelative] = guid;
        }

        return guid;
    }

    /// <summary>
    /// Unity's TerrainCollider settings (physics material, friction) have no Prowl mapping. This
    /// converter only makes data assets, so component placement belongs to the hierarchy converter;
    /// log that once per import rather than once per scene.
    /// </summary>
    public void WarnTerrainColliderUnsupported()
    {
        if (_colliderWarned) return;
        _colliderWarned = true;
        _warn("[UnityPackageImporter] Unity TerrainCollider components were skipped: this conversion produces terrain data only. " +
              "Prowl's TerrainCollider samples the TerrainData directly but has no mapping for Unity's physics material/friction settings; " +
              "attach it to the terrain GameObject if collision is needed.");
    }

    /// <summary>Counts for the terrain part of the import, written as its own manifest asset.</summary>
    public UnityTerrainImportReport BuildReport() => new()
    {
        ConvertedTerrainLayers = _layersByUnityGuid.Count,
        ConvertedTerrains = _terrainGuidByUnityGuid.Count,
    };

    // ================================================================
    //  Terrain layers
    // ================================================================

    private Texture2D? ResolveTexture(UnityYamlNode? node)
    {
        UnityRef reference;
        try { reference = UnityYaml.ParseRef(node); }
        catch { return null; }

        if (reference.IsNull || string.IsNullOrWhiteSpace(reference.Guid) || IsZeroGuid(reference.Guid)) return null;
        if (!_prowlGuidByUnityGuid.TryGetValue(reference.Guid, out Guid prowlGuid)) return null;

        Texture2D? texture = AssetDatabase.Load<Texture2D>(prowlGuid);
        return texture.IsValid() ? texture : null;
    }

    private static TerrainLayer CopyLayer(TerrainLayer source) => new()
    {
        Albedo = source.Albedo,
        NormalMap = source.NormalMap,
        Tiling = source.Tiling,
        Roughness = source.Roughness,
        HeightBlend = source.HeightBlend,
        HexTiling = source.HexTiling,
        MacroVariation = source.MacroVariation,
    };

    private static UnityYamlNode? GetNode(UnityDocument document, string key)
        => document.Fields.TryGetValue(key, out UnityYamlNode? node) ? node : null;

    private static string? ScalarText(UnityYamlNode? node) => node switch
    {
        ScalarNode scalar => scalar.Value,
        null => null,
        _ => null,
    };

    private static float? ReadFloat(UnityYamlNode? node)
        => float.TryParse(ScalarText(node), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : null;

    private static float? ReadVectorMember(UnityDocument document, string vector, string member)
        => ReadFloat(GetNode(document, vector) is MapNode map
            && map.Fields.TryGetValue(member, out UnityYamlNode? node) ? node : null);

    private static bool IsZeroGuid(string guid) => guid.Length == 32 && guid.All(c => c == '0');

    /// <summary>
    /// A binary TerrainData lists its referenced TerrainLayer GUIDs in its external-reference table.
    /// Unity stores those GUID bytes with each textual hex byte's nibbles swapped; their order in the
    /// file is the terrain's layer order, so a simple scan recovers it without a type-tree parser.
    /// </summary>
    private List<TerrainLayer> ReadLayerOrder(byte[] bytes)
    {
        var found = new List<(int Offset, TerrainLayer Layer)>();
        foreach ((string unityGuid, TerrainLayer layer) in _layersByUnityGuid)
        {
            if (unityGuid.Length != 32) continue;
            byte[] pattern = UnityGuidBytes(unityGuid);
            int from = 0;
            while (from <= bytes.Length - pattern.Length)
            {
                int index = bytes.AsSpan(from).IndexOf(pattern);
                if (index < 0) break;
                int offset = from + index;
                found.Add((offset, layer));
                from = offset + pattern.Length;
            }
        }

        return found.OrderBy(pair => pair.Offset).Select(pair => pair.Layer).ToList();
    }

    private static byte[] UnityGuidBytes(string hex)
    {
        byte[] bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i++)
        {
            int hi = HexDigit(hex[i * 2]);
            int lo = HexDigit(hex[i * 2 + 1]);
            byte value = (byte)((hi << 4) | lo);
            bytes[i] = (byte)((value << 4) | (value >> 4));
        }
        return bytes;
    }

    private static int HexDigit(char c) => c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);

    // ================================================================
    //  Heightmap extraction
    // ================================================================

    /// <summary>
    /// Find a Unity height array by looking for a plausible sample count (a Unity heightmap resolution
    /// squared) followed by smooth 16-bit samples. Real heightmaps vary by only a few units between
    /// neighbours; the splat-alpha textures stored in the same file vary by thousands, so a smoothness
    /// score separates them reliably.
    /// </summary>
    private static bool TryExtractHeightmap(byte[] bytes, out int resolution, out float[]? heights)
    {
        resolution = 513;
        heights = null;

        double bestScore = double.MaxValue;
        int bestResolution = 0;
        int bestOffset = 0;
        bool bestLittleEndian = true;
        Span<byte> pattern = stackalloc byte[4];

        foreach (int res in CandidateResolutions)
        {
            int count = res * res;
            if (count * 2 + 4 > bytes.Length) continue;

            for (int endian = 0; endian < 2; endian++)
            {
                bool little = endian == 0;
                if (little) BinaryPrimitives.WriteInt32LittleEndian(pattern, count);
                else BinaryPrimitives.WriteInt32BigEndian(pattern, count);

                int from = 0;
                while (from <= bytes.Length - pattern.Length)
                {
                    int index = bytes.AsSpan(from).IndexOf(pattern);
                    if (index < 0) break;
                    int offset = from + index;
                    from = offset + 1;
                    if (offset + 4 + count * 2 > bytes.Length) continue;

                    double score = ScoreCandidate(bytes, offset + 4, count, little);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestResolution = res;
                        bestOffset = offset + 4;
                        bestLittleEndian = little;
                    }
                }
            }
        }

        if (bestResolution == 0 || bestScore > HeightmapSmoothnessLimit)
            return false;

        int samples = bestResolution * bestResolution;
        float[] values = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            ushort sample = bestLittleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bestOffset + i * 2, 2))
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(bestOffset + i * 2, 2));
            values[i] = sample / 65535f;
        }

        resolution = bestResolution;
        heights = values;
        return true;
    }

    private static double ScoreCandidate(byte[] bytes, int offset, int count, bool little)
    {
        // The range is sampled across the whole array; the difference must be between *neighbouring*
        // samples, so striding here would measure terrain slope instead of noise.
        int step = Math.Max(1, count / 4096);
        int min = ushort.MaxValue, max = 0;
        for (int i = 0; i < count; i += step)
        {
            ushort value = ReadSample(bytes, offset + i * 2, little);
            if (value < min) min = value;
            if (value > max) max = value;
        }
        if (max == min) return double.MaxValue;

        int neighbours = Math.Min(count - 1, 4096);
        ushort previous = ReadSample(bytes, offset, little);
        long totalDifference = 0;
        for (int i = 1; i <= neighbours; i++)
        {
            ushort value = ReadSample(bytes, offset + i * 2, little);
            totalDifference += Math.Abs(value - previous);
            previous = value;
        }

        return (double)totalDifference / neighbours / (max - min + 1);
    }

    private static ushort ReadSample(byte[] bytes, int offset, bool little)
        => little
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
}

/// <summary>
/// Counts for the terrain conversion, written next to the converted terrain assets. The main
/// <see cref="UnityImportReport"/> lives in another file and is left untouched by this converter.
/// </summary>
public sealed class UnityTerrainImportReport : Asset
{
    /// <summary>Unity .terrainlayer files whose textures were resolved.</summary>
    public int ConvertedTerrainLayers;

    /// <summary>Binary TerrainData assets turned into Prowl TerrainData assets.</summary>
    public int ConvertedTerrains;

    public UnityTerrainImportReport() : base("Unity Terrain Import Report") { }
}
