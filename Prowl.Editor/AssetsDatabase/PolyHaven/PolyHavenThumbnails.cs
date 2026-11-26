// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.PolyHaven;

/// <summary>
/// The small preview pictures the browser shows next to each asset. Poly Haven serves a thumbnail per asset,
/// so the panel asks for one only when its row is on screen and at a reduced size, and the decoded textures
/// live in a small cache so scrolling back does not download them again.
/// <para>
/// Downloads run on the thread pool; <see cref="Poll"/> decodes the finished ones on the UI thread, where a
/// texture can be created.
/// </para>
/// </summary>
public static class PolyHavenThumbnails
{
    private const int MaxCached = 96;
    private const int Width = 256;
    private const int Height = 256;

    private sealed class Entry
    {
        public Task<byte[]>? Download;
        public Texture2D? Texture;
        public bool Failed;
    }

    private static readonly Dictionary<string, Entry> s_entries = new(StringComparer.Ordinal);
    private static readonly List<string> s_order = [];

    /// <summary>Asks for the preview of <paramref name="asset"/> unless it is already here or on its way.</summary>
    public static void Request(PolyHavenAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.ThumbnailUrl)) return;

        if (s_entries.TryGetValue(asset.Id, out Entry? known))
        {
            if (known.Failed) return;
            if (known.Texture.IsValid()) return;
            if (known.Download is { IsCompleted: false }) return;
        }

        var entry = new Entry { Download = Task.Run(() => PolyHavenApi.DownloadBytesAsync(SmallUrl(asset.ThumbnailUrl))) };
        s_entries[asset.Id] = entry;
        s_order.Add(asset.Id);
        Evict();
    }

    /// <summary>The preview of <paramref name="asset"/> once it has arrived, or null while it is still coming.</summary>
    public static Texture2D? Get(PolyHavenAsset asset)
        => s_entries.TryGetValue(asset.Id, out Entry? entry) && entry.Texture.IsValid() ? entry.Texture : null;

    /// <summary>Turns every finished download into a texture. Call from the UI thread, once a frame.</summary>
    public static void Poll()
    {
        foreach (Entry entry in s_entries.Values)
        {
            if (entry.Texture.IsValid()) continue;
            Task<byte[]>? download = entry.Download;
            if (download is not { IsCompleted: true }) continue;
            entry.Download = null;

            if (download.IsFaulted || download.IsCanceled)
            {
                entry.Failed = true;
                continue;
            }

            try
            {
                // Decoded with the image's own top row first, not the bottom-first layout a texture is uploaded
                // with: the browser hands the pixels to a 2D canvas, which starts at the top.
                using Aperture.Image image = Aperture.Image.Load(download.Result,
                    new Aperture.DecodeOptions { TargetPixelFormat = Aperture.PixelFormat.Rgba8 });
                entry.Texture = Texture2D.FromImage(image, mipmaps: false, srgb: true);
            }
            catch (Exception)
            {
                // A thumbnail that will not decode is left out rather than shown as a broken image.
                entry.Failed = true;
            }
        }
    }

    /// <summary>
    /// The same preview at a smaller size: the CDN builds it from the query, so the browser fetches a 256 pixel
    /// image instead of the larger one the asset document points at. The grid shows these as cover images.
    /// </summary>
    public static string SmallUrl(string url)
    {
        int query = url.IndexOf('?');
        if (query < 0) return url;

        string head = url[..query];
        var parts = new List<string>();
        foreach (string part in url[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string key = part.Split('=', 2)[0];
            if (key.Equals("width", StringComparison.OrdinalIgnoreCase)) parts.Add($"width={Width}");
            else if (key.Equals("height", StringComparison.OrdinalIgnoreCase)) parts.Add($"height={Height}");
            else if (key.Equals("format", StringComparison.OrdinalIgnoreCase)) parts.Add("format=jpg");
            else parts.Add(part);
        }

        return $"{head}?{string.Join('&', parts)}";
    }

    private static void Evict()
    {
        while (s_order.Count > MaxCached)
        {
            string oldest = s_order[0];
            s_order.RemoveAt(0);
            if (!s_entries.Remove(oldest, out Entry? entry)) continue;
            if (entry.Texture.IsValid()) entry.Texture!.Dispose();
        }
    }
}
