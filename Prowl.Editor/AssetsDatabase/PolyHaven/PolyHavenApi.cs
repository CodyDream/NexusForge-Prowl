// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Editor.PolyHaven;

/// <summary>The three kinds of asset the Poly Haven API serves.</summary>
public enum PolyHavenType
{
    Hdri,
    Texture,
    Model,
}

/// <summary>What one downloaded file is for, so it can be named and wired into a material.</summary>
public enum PolyHavenMap
{
    /// <summary>A file that is used as it is, such as a model or an HDRI.</summary>
    Raw,
    Colour,
    Normal,
    /// <summary>The packed occlusion / roughness / metalness image, split into the three maps on download.</summary>
    Arm,
    Occlusion,
    Roughness,
    Metalness,
    Height,
}

/// <summary>One entry of <c>GET /assets</c>, reduced to what the browser lists.</summary>
public sealed record PolyHavenAsset(
    string Id,
    string Name,
    PolyHavenType Type,
    string Category,
    IReadOnlyList<string> Tags,
    string ThumbnailUrl,
    long Downloads);

/// <summary>
/// Client for the public Poly Haven API (https://api.polyhaven.com).
/// <para>
/// No key is needed. Their terms require every request to carry a User-Agent that names this tool, and the
/// browser panel shows the "Powered by Poly Haven" credit near the results, which the terms also ask for.
/// The header is <c>ProwlEditor/1.0 (&lt;name&gt;)</c>; the name can be set in the browser's Settings section.
/// The assets themselves are CC0 and need no attribution.
/// </para>
/// </summary>
public static class PolyHavenApi
{
    public const string BaseUrl = "https://api.polyhaven.com";

    /// <summary>The credit the panel shows next to the results, as the API terms ask.</summary>
    public const string Credit = "Powered by Poly Haven";

    private static readonly HttpClient s_http = CreateClient();

    private static HttpClient CreateClient() => new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// All requests go through here so each one carries the currently configured User-Agent. It is applied to
    /// the request, not the shared client, because the name can be edited while other requests are in flight.
    /// </summary>
    private static async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        PolyHavenUserAgent.Apply(request.Headers);
        return await s_http.SendAsync(request, completion, ct);
    }

    /// <summary>Every asset of one kind, with its name, category and tags. Throws when the API cannot be reached.</summary>
    public static async Task<IReadOnlyList<PolyHavenAsset>> GetAssetsAsync(PolyHavenType type, CancellationToken ct = default)
    {
        string query = type switch
        {
            PolyHavenType.Hdri => "hdris",
            PolyHavenType.Texture => "textures",
            _ => "models",
        };

        JsonNode root = await GetJsonAsync($"{BaseUrl}/assets?type={query}", ct);
        var assets = new List<PolyHavenAsset>();
        if (root is not JsonObject map) return assets;

        foreach (KeyValuePair<string, JsonNode?> entry in map)
        {
            if (entry.Value is not JsonObject node) continue;
            if (ParseAsset(entry.Key, node) is { } asset && asset.Type == type)
                assets.Add(asset);
        }
        return [.. assets.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The download links of one asset, as the raw <c>GET /files/{id}</c> document.</summary>
    public static async Task<JsonObject> GetFilesAsync(string id, CancellationToken ct = default)
        => await GetJsonAsync($"{BaseUrl}/files/{id}", ct) as JsonObject
           ?? throw new InvalidDataException($"Poly Haven returned no file list for '{id}'.");

    /// <summary>Reads one <c>/assets</c> entry. Null when the document is not an asset.</summary>
    public static PolyHavenAsset? ParseAsset(string id, JsonObject node)
    {
        PolyHavenType? type = Int(node, "type") switch
        {
            0 => PolyHavenType.Hdri,
            1 => PolyHavenType.Texture,
            2 => PolyHavenType.Model,
            _ => null,
        };
        if (type is null) return null;

        var tags = new List<string>();
        if (node["tags"] is JsonArray array)
            foreach (JsonNode? tag in array)
                if (tag is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
                    tags.Add(text);

        return new PolyHavenAsset(
            id,
            Text(node, "name") is { Length: > 0 } name ? name : id,
            type.Value,
            Text(node, "category"),
            tags,
            Text(node, "thumbnail_url"),
            Int(node, "download_count"));
    }

    /// <summary>
    /// Downloads a file to an absolute path. Half-written files are kept as .part and moved into place.
    /// <paramref name="onProgress"/> is handed the fraction done so far, 0 to 1, as the bytes arrive.
    /// </summary>
    public static async Task DownloadAsync(string url, string absolutePath, Action<double>? onProgress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        string temp = absolutePath + ".part";

        using (HttpResponseMessage response = await GetAsync(url, ct, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? 0;
            await using Stream input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

            byte[] buffer = new byte[1 << 16];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                if (onProgress != null && total > 0) onProgress((double)copied / total);
            }
        }

        File.Move(temp, absolutePath, overwrite: true);
        onProgress?.Invoke(1);
    }

    /// <summary>Downloads a small file into memory, used for the browser's preview images.</summary>
    public static async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private static async Task<JsonNode> GetJsonAsync(string url, CancellationToken ct)
    {
        using HttpResponseMessage response = await GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(text) ?? throw new InvalidDataException($"Poly Haven returned no JSON for '{url}'.");
    }

    private static string Text(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue(out string? text) ? text : "";

    private static long Int(JsonObject node, string key)
    {
        if (node[key] is not JsonValue value) return 0;
        if (value.TryGetValue(out long number)) return number;
        if (value.TryGetValue(out int small)) return small;
        if (value.TryGetValue(out double real)) return (long)real;
        return 0;
    }
}

/// <summary>
/// Works out which files of a <c>/files/{id}</c> document to download for a chosen kind, resolution and
/// destination name. Kept free of I/O so it can be tested against recorded documents.
/// <para>
/// The documents are keyed by map: <c>files["Diffuse"]["2k"]["jpg"].url</c>. An HDRI is keyed by kind
/// (<c>files["hdri"]["2k"]["hdr"]</c>) and a model by format (<c>files["gltf"]["2k"]["gltf"]</c>).
/// </para>
/// <para>
/// The local file names matter: the texture importer decides a map's processing from its file name, so normal
/// maps are saved with "nor" in the name (read as a tangent space normal map) and data maps with "mask",
/// "roughness", "metallic", "occlusion" or "displacement" (read as linear data), while the colour map keeps
/// none of those words and stays sRGB.
/// </para>
/// </summary>
public static class PolyHavenFiles
{
    /// <summary>The resolutions the panel offers, windowed down to what each document actually has.</summary>
    public static readonly string[] Resolutions = ["1k", "2k", "4k", "8k"];

    /// <summary>One file to fetch or produce: where it goes under the asset's folder, from where, and what it is.</summary>
    public readonly record struct PlannedFile(string RelativePath, string Url, PolyHavenMap Map);

    /// <summary>The local file name of one map at a resolution. Unpacked maps are always png.</summary>
    public static string FileName(string id, string resolution, PolyHavenMap map, string extension)
        => map switch
        {
            PolyHavenMap.Colour => $"{id}_{resolution}_diffuse{extension}",
            PolyHavenMap.Normal => $"{id}_{resolution}_nor_gl{extension}",
            PolyHavenMap.Arm => $"{id}_{resolution}_arm_mask{extension}",
            PolyHavenMap.Occlusion => $"{id}_{resolution}_occlusion.png",
            PolyHavenMap.Roughness => $"{id}_{resolution}_roughness.png",
            PolyHavenMap.Metalness => $"{id}_{resolution}_metallic.png",
            _ => $"{id}_{resolution}_displacement{extension}",
        };

    /// <summary>
    /// The maps worth downloading from a texture document, in the order they are fetched. Poly Haven ships the
    /// same surface data several ways, so this keeps one source per map: the packed arm image when there is one
    /// (AO, roughness and metalness in R, G and B) and the separate maps only when it is missing. The DirectX
    /// normal map is never taken since the OpenGL one is the same data and the engine expects it.
    /// </summary>
    public static IReadOnlyList<(string Source, PolyHavenMap Map)> TextureMaps(JsonObject files)
    {
        var maps = new List<(string Source, PolyHavenMap Map)>();

        if (ColourSource(files) is { } colour)
            maps.Add((colour, PolyHavenMap.Colour));

        if (files.ContainsKey("nor_gl"))
            maps.Add(("nor_gl", PolyHavenMap.Normal));

        if (files.ContainsKey("arm"))
        {
            maps.Add(("arm", PolyHavenMap.Arm));
        }
        else
        {
            if (files.ContainsKey("AO")) maps.Add(("AO", PolyHavenMap.Occlusion));
            if (files.ContainsKey("Rough")) maps.Add(("Rough", PolyHavenMap.Roughness));
            if (files.ContainsKey("Metal")) maps.Add(("Metal", PolyHavenMap.Metalness));
        }

        if (files.ContainsKey("Displacement"))
            maps.Add(("Displacement", PolyHavenMap.Height));

        return maps;
    }

    /// <summary>Every file a download needs, or an empty list when the document has nothing at that resolution.</summary>
    public static IReadOnlyList<PlannedFile> Plan(PolyHavenType type, JsonObject files, string resolution, string id)
    {
        var plan = new List<PlannedFile>();
        switch (type)
        {
            case PolyHavenType.Hdri:
                if (HdriUrl(files, resolution) is { } hdri)
                    plan.Add(new PlannedFile($"{id}_{resolution}{ExtensionOf(hdri, ".hdr")}", hdri, PolyHavenMap.Raw));
                break;

            case PolyHavenType.Texture:
                foreach ((string source, PolyHavenMap map) in TextureMaps(files))
                {
                    if (TextureUrl(files, resolution, source) is not { } url) continue;
                    string extension = ExtensionOf(url, ".jpg");
                    plan.Add(new PlannedFile(FileName(id, resolution, map, extension), url, map));
                }
                break;

            case PolyHavenType.Model:
                foreach ((string relative, string url) in ModelFiles(files, resolution, id))
                    plan.Add(new PlannedFile(relative, url, PolyHavenMap.Raw));
                break;
        }
        return plan;
    }

    /// <summary>The equirectangular HDRI at a resolution, preferring the .hdr file the engine reads natively.</summary>
    public static string? HdriUrl(JsonObject files, string resolution)
    {
        if (files["hdri"] is not JsonObject byResolution) return null;
        if (byResolution[resolution] is not JsonObject atResolution) return null;
        return Url(atResolution, "hdr") ?? Url(atResolution, "exr");
    }

    /// <summary>One map of a texture set at a resolution, as a jpg when there is one and a png otherwise.</summary>
    public static string? TextureUrl(JsonObject files, string resolution, string map)
    {
        if (files[map] is not JsonObject byResolution) return null;
        if (byResolution[resolution] is not JsonObject atResolution) return null;
        return Url(atResolution, "jpg") ?? Url(atResolution, "png") ?? Url(atResolution, "exr");
    }

    /// <summary>The glTF and everything it references, as paths relative to the asset's folder.</summary>
    public static IReadOnlyList<(string RelativePath, string Url)> ModelFiles(JsonObject files, string resolution, string id)
    {
        var list = new List<(string, string)>();
        if (files["gltf"] is not JsonObject byResolution) return list;
        if (byResolution[resolution] is not JsonObject atResolution) return list;
        if (atResolution["gltf"] is not JsonObject gltf) return list;

        // Unlike an HDRI or texture map, the glTF entry holds its own url rather than a file object.
        if (Str(gltf, "url") is { Length: > 0 } model)
            list.Add(($"{id}_{resolution}{ExtensionOf(model, ".gltf")}", model));

        // The model's textures and .bin files arrive as a map of relative path -> file, which is kept as is so
        // the glTF's own relative references still resolve next to it.
        if (gltf["include"] is JsonObject includes)
            foreach (KeyValuePair<string, JsonNode?> entry in includes)
                if (entry.Value is JsonObject file && Str(file, "url") is { Length: > 0 } url)
                    list.Add((entry.Key.Replace('\\', '/'), url));

        return list;
    }

    /// <summary>The albedo map of a set: Poly Haven names it Diffuse, or numbers the colour layers of a few assets.</summary>
    private static string? ColourSource(JsonObject files)
    {
        if (files.ContainsKey("Diffuse")) return "Diffuse";
        if (files.ContainsKey("col_1")) return "col_1";
        return files.Select(kv => kv.Key).FirstOrDefault(k =>
            k.StartsWith("col", StringComparison.OrdinalIgnoreCase) && !k.Equals("col", StringComparison.OrdinalIgnoreCase));
    }

    private static string Str(JsonObject node, string key)
        => node[key] is JsonValue value && value.TryGetValue(out string? text) ? text : "";

    private static string? Url(JsonObject node, string key)
        => node[key] is JsonObject file && file["url"] is JsonValue value && value.TryGetValue(out string? url)
            && !string.IsNullOrWhiteSpace(url) ? url : null;

    private static string ExtensionOf(string url, string fallback)
    {
        string path = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsolutePath : url;
        string extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ? fallback : extension;
    }
}
