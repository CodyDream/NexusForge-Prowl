// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Prowl.Editor.Core;
using Prowl.Editor.PolyHaven;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Browses Poly Haven's CC0 HDRIs, textures and models and downloads them straight into the project.
/// An HDRI is imported and set as the scene's panorama sky; a texture set is split into its maps and becomes a
/// material assigned to the selected model; a model is imported as its glTF prefab.
/// <para>
/// The library is large, so the list arrives in pages: only the rows the user has scrolled to are kept in the
/// layout, and a preview picture is fetched at a reduced size for the rows on screen alone.
/// </para>
/// </summary>
public class PolyHavenPanel : DockPanel
{
    [MenuItem("Window/General/Poly Haven", priority: 8, VisiblePackage = "PolyHaven")]
    static void Open() => EditorApplication.Instance?.OpenPanel<PolyHavenPanel>();

    public override string Title => Loc.Get("panel.polyhaven");
    public override string Icon => EditorIcons.Globe;

    private const int PageSize = 16;
    private const float RowHeight = 56f;
    private const float GridCellWidth = 300f;
    private const float ThumbSize = 40f;

    private static readonly (PolyHavenType Type, string Label)[] s_types =
    [
        (PolyHavenType.Hdri, "polyhaven.type_hdri"),
        (PolyHavenType.Texture, "polyhaven.type_texture"),
        (PolyHavenType.Model, "polyhaven.type_model"),
    ];

    private PolyHavenType _type = PolyHavenType.Hdri;
    private string _resolution = "2k";
    private string _search = "";
    private string _status = "";
    private bool _grid = true;

    private List<PolyHavenAsset> _assets = [];
    private Task<IReadOnlyList<PolyHavenAsset>>? _loading;
    private Task<IReadOnlyList<PolyHavenAsset>>[]? _allLoading;
    private readonly Dictionary<PolyHavenType, IReadOnlyList<PolyHavenAsset>> _byType = [];
    private bool _downloadedTab;
    private string? _loadedKey;
    private string? _loadError;
    private PolyHavenImport? _job;

    // What is already in Assets/PolyHaven/<id>/ and at which resolution, so the list can show it as installed.
    private readonly Dictionary<string, string> _downloaded = new(StringComparer.OrdinalIgnoreCase);
    private bool _scanned;

    // How many rows are in the layout, and what the list was filtered by when that was decided. Rows are only
    // added as the user comes near the end, so a library of hundreds costs a page at most until it is scrolled.
    private int _shown = PageSize;
    private string _shownFor = "";

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (Project.Current is null)
        {
            paper.Box("ph_noproject").Height(30)
                .Text(Loc.Get("polyhaven.no_project"), font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        Poll();

        Origami.ScrollView(paper, "ph_scroll", width, height).SmoothScroll(false).Body(view =>
        {
            using (paper.Column("ph_col").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
            {
                EditorGUI.SectionHeader(paper, "ph_head", Loc.Get("polyhaven.heading"), first: true);

                paper.Box("ph_credit").Height(UnitValue.Auto)
                    .Text(PolyHavenApi.Credit, font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                    .Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 8);

                using (paper.Row("ph_types").Height(28).Gap(6).Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 8).Enter())
                {
                    foreach ((PolyHavenType type, string label) in s_types)
                        Origami.Button(paper, $"ph_type_{type}", Loc.Get(label), () => SetType(type))
                            .Height(26).Show();

                    Origami.Button(paper, "ph_tab_downloaded", Loc.Get("polyhaven.tab_downloaded"),
                        () => SetDownloadedTab()).Height(26).Show();
                }

                EditorGUI.SettingsRow(paper, "ph_search", Loc.Get("polyhaven.search"), () =>
                    Origami.TextField(paper, "ph_search_v", _search, v => _search = v).Show());

                using (paper.Row("ph_res").Height(28).Gap(6).Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 8).Enter())
                    foreach (string resolution in PolyHavenFiles.Resolutions)
                        Origami.Button(paper, $"ph_res_{resolution}", resolution.ToUpperInvariant(),
                            () => _resolution = resolution).Height(26).Show();

                EditorGUI.SettingsRow(paper, "ph_refresh", Loc.Get("polyhaven.refresh"), () =>
                    Origami.Button(paper, "ph_refresh_btn", $"{EditorIcons.ArrowsRotate}  {Loc.Get("polyhaven.rescan")}",
                        ForceReload).Small().Show());

                EditorGUI.SettingsRow(paper, "ph_layout", Loc.Get("polyhaven.layout"), () =>
                    Origami.Button(paper, "ph_layout_btn", _grid ? Loc.Get("polyhaven.list") : Loc.Get("polyhaven.grid"),
                        () => _grid = !_grid).Small().Show());

                // The running import reports itself at the top, where it stays in view however far the list
                // is scrolled. The bar fills as the bytes of each file arrive.
                if (_job is { IsDone: false } job)
                {
                    Hint(paper, "ph_job", $"{job.Status}  ({job.Progress * 100f:F0}%)", font);
                    using (paper.Row("ph_job_bar_row").Height(16)
                        .Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 8).Enter())
                        Origami.ProgressBar(paper, "ph_job_bar", job.Progress).Show();
                }
                else if (_status.Length > 0)
                {
                    Hint(paper, "ph_status", _status, font);
                }

                if (_loadError is { Length: > 0 })
                    Hint(paper, "ph_error", _loadError, font);
                else if (_loading is { IsCompleted: false })
                    Hint(paper, "ph_loading", Loc.Get("polyhaven.loading"), font);

                List<PolyHavenAsset> list = [.. Filtered()];
                // Covers are at least 256px tall, or a quarter of the panel, whichever is larger.
                float coverHeight = Math.Max(256f, view.Height * 0.25f);
                float gridRowHeight = coverHeight + 78f;
                int shown = RowsFor(list, view.ScrollY, view.Height, _grid ? gridRowHeight : RowHeight);

                if (list.Count == 0)
                    Hint(paper, "ph_empty", Loc.Get("polyhaven.empty"), font);

                // Only the assets on screen ask for a preview, so scrolling does not pull the whole library.
                float lineHeight = _grid ? gridRowHeight : RowHeight;
                int firstVisible = Math.Max(0, (int)(view.ScrollY / lineHeight) - 1);
                int lastVisible = (int)((view.ScrollY + view.Height) / lineHeight) + 1;
                if (_grid)
                {
                    int columns = Math.Max(1, (int)(view.Width / GridCellWidth));
                    int firstAsset = firstVisible * columns;
                    int lastAsset = lastVisible * columns + columns;
                    for (int i = Math.Max(0, firstAsset); i < Math.Min(shown, list.Count) && i < lastAsset; i++)
                        PolyHavenThumbnails.Request(list[i]);
                }
                else
                {
                    for (int i = Math.Max(0, firstVisible); i < Math.Min(shown, list.Count) && i <= lastVisible; i++)
                        PolyHavenThumbnails.Request(list[i]);
                }

                if (_grid)
                {
                    int columns = Math.Max(1, (int)(view.Width / GridCellWidth));
                    float cellWidth = view.Width / columns;
                    for (int i = 0; i < shown && i < list.Count; i += columns)
                        DrawGridRow(paper, font, list, i, columns, cellWidth, coverHeight, gridRowHeight);
                }
                else
                {
                    for (int i = 0; i < shown && i < list.Count; i++)
                        DrawRow(paper, font, list[i]);
                }

                if (shown < list.Count)
                    Hint(paper, "ph_more", Loc.Get("polyhaven.scroll_more", new { count = list.Count - shown }), font);
            }
        });
    }

    /// <summary>The rows to lay out: a page, grown as the end of the list comes into view.</summary>
    private int RowsFor(List<PolyHavenAsset> list, float scrollY, float viewHeight, float lineHeight)
    {
        string key = $"{_downloadedTab}|{_type}|{_search}";
        if (key != _shownFor)
        {
            _shownFor = key;
            _shown = PageSize;
        }

        int reached = (int)((scrollY + viewHeight) / lineHeight) + PageSize;
        if (reached > _shown) _shown = reached;

        return Math.Min(_shown, Math.Max(list.Count, 0));
    }

    /// <summary>One grid row of asset cards: up to <c>columns</c> of them side by side.</summary>
    private void DrawGridRow(Paper paper, Scribe.FontFile font, IReadOnlyList<PolyHavenAsset> list, int start, int columns, float cellWidth, float coverHeight, float rowHeight)
    {
        using (paper.Row($"ph_grow_{start}").Height(rowHeight).Gap(8).Enter())
            for (int c = 0; c < columns; c++)
            {
                int index = start + c;
                if (index >= list.Count) break;
                DrawCard(paper, font, list[index], cellWidth, coverHeight);
            }
    }

    /// <summary>One asset card in the grid: preview, name and download button, in a column of the given width.</summary>
    private void DrawCard(Paper paper, Scribe.FontFile font, PolyHavenAsset asset, float width, float coverHeight)
    {
        string id = asset.Id;
        Texture2D? preview = PolyHavenThumbnails.Get(asset);
        bool installed = _downloaded.ContainsKey(id);

        Origami.RightClickMenu(paper, $"ph_cell_{id}", b => BuildAssetMenu(b, asset));

        using (paper.Column($"ph_cell_{id}").Width(width).Height(UnitValue.Auto).Gap(4).Enter())
        {
            ElementBuilder thumb = paper.Box($"ph_thumb_{id}").Width(width).Height(coverHeight)
                .Rounded(EditorTheme.Roundness)
                .BackgroundColor(Color.FromArgb(40, 0, 0, 0))
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1);

            if (preview.IsValid())
                thumb.OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    // Fit the cover to its own aspect ratio inside the fixed-height area, centred.
                    float scale = Math.Min((float)r.Size.X / preview.Width, (float)r.Size.Y / preview.Height);
                    float drawW = preview.Width * scale;
                    float drawH = preview.Height * scale;
                    canvas.DrawImageRounded(preview,
                        (float)r.Min.X + ((float)r.Size.X - drawW) * 0.5f,
                        (float)r.Min.Y + ((float)r.Size.Y - drawH) * 0.5f,
                        drawW, drawH, EditorTheme.Roundness);
                }));
            else
                thumb.Icon(paper, EditorIcons.Image_I, EditorTheme.Ink300, size: 16f);

            paper.Box($"ph_name_{id}").Height(16).Clip()
                .Text(asset.Name, font).TextColor(installed ? EditorTheme.Ink300 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            string meta = asset.Category.Length > 0
                ? asset.Category
                : asset.Tags.Count > 0 ? string.Join(", ", asset.Tags.Take(4)) : asset.Id;
            if (installed && _downloaded.TryGetValue(id, out string? res) && res.Length > 0)
                meta = $"{meta}  ·  {res.ToUpperInvariant()}";

            paper.Box($"ph_meta_{id}").Height(14).Clip()
                .Text(meta, font).TextColor(installed ? EditorTheme.Ink300 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.Button(paper, $"ph_dl_{id}",
                    installed ? $"{EditorIcons.Check}  {Loc.Get("polyhaven.installed")}" : $"{EditorIcons.Download}  {Loc.Get("polyhaven.download")}",
                    () => Download(asset)).Small().Width(84).Height(24).Show();
        }
    }

    /// <summary>One asset: its preview, name, category and the button that imports it.</summary>
    private void DrawRow(Paper paper, Scribe.FontFile font, PolyHavenAsset asset)
    {
        string id = asset.Id;
        Texture2D? preview = PolyHavenThumbnails.Get(asset);
        bool installed = _downloaded.ContainsKey(id);

        Origami.RightClickMenu(paper, $"ph_row_{id}", b => BuildAssetMenu(b, asset));

        using (paper.Row($"ph_row_{id}").Height(RowHeight).Gap(8).AlignItems(LayoutAlignment.Center)
            .Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 2).Enter())
        {
            ElementBuilder thumb = paper.Box($"ph_thumb_{id}").Width(ThumbSize).Height(ThumbSize)
                .Rounded(EditorTheme.Roundness)
                .BackgroundColor(Color.FromArgb(40, 0, 0, 0))
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1);

            if (preview.IsValid())
                thumb.OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float scale = Math.Min((float)r.Size.X / preview.Width, (float)r.Size.Y / preview.Height);
                    float drawW = preview.Width * scale;
                    float drawH = preview.Height * scale;
                    canvas.DrawImageRounded(preview,
                        (float)r.Min.X + ((float)r.Size.X - drawW) * 0.5f,
                        (float)r.Min.Y + ((float)r.Size.Y - drawH) * 0.5f,
                        drawW, drawH, EditorTheme.Roundness);
                }));
            else
                thumb.Icon(paper, EditorIcons.Image_I, EditorTheme.Ink300, size: 16f);

            using (paper.Column($"ph_info_{id}").Width(UnitValue.StretchOne).Height(RowHeight)
                .AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box($"ph_name_{id}").Height(18).Clip()
                    .Text(asset.Name, font).TextColor(installed ? EditorTheme.Ink300 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                string meta = asset.Category.Length > 0
                    ? asset.Category
                    : asset.Tags.Count > 0 ? string.Join(", ", asset.Tags.Take(4)) : asset.Id;
                if (installed && _downloaded.TryGetValue(id, out string? res) && res.Length > 0)
                    meta = $"{meta}  ·  {res.ToUpperInvariant()}";

                paper.Box($"ph_meta_{id}").Height(16).Clip()
                    .Text(meta, font).TextColor(installed ? EditorTheme.Ink300 : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            Origami.Button(paper, $"ph_dl_{id}",
                    installed ? $"{EditorIcons.Check}  {Loc.Get("polyhaven.installed")}" : $"{EditorIcons.Download}  {Loc.Get("polyhaven.download")}",
                    () => Download(asset)).Small().Width(84).Height(24).Show();
        }
    }

    private IEnumerable<PolyHavenAsset> Filtered()
    {
        string search = _search.Trim();
        IEnumerable<PolyHavenAsset> assets = _assets;
        if (search.Length > 0)
            assets = assets.Where(a =>
                a.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || a.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || a.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                || a.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)));
        return assets;
    }

    /// <summary>Loads the current kind's asset list once, and collects it when the request is done.</summary>
    private void Poll()
    {
        PolyHavenThumbnails.Poll();

        if (!_scanned)
        {
            _scanned = true;
            ScanDownloaded();
        }

        if (_job is { IsDone: true })
        {
            _status = _job.Status;
            _job = null;
            _scanned = false; // a finished job may have created a new folder
        }

        _job?.Poll();

        if (_downloadedTab)
        {
            // The library pages are cached per kind, so this tab can show installed assets of any type.
            if (_byType.Count < s_types.Length && _allLoading is null)
                _allLoading = [.. s_types.Select(t => Task.Run(() => PolyHavenApi.GetAssetsAsync(t.Type)))];

            if (_allLoading is { } tasks && tasks.All(t => t.IsCompleted))
            {
                for (int i = 0; i < tasks.Length; i++)
                {
                    try { _byType[s_types[i].Type] = tasks[i].Result; }
                    catch (Exception ex)
                    {
                        _loadError = Loc.Get("polyhaven.load_failed", new { error = (ex.InnerException ?? ex).Message });
                    }
                }
                _allLoading = null;
            }

            var installed = _byType.Values.SelectMany(a => a)
                .Where(a => _downloaded.ContainsKey(a.Id)).ToList();
            foreach (string id in _downloaded.Keys)
                if (!installed.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    installed.Add(new PolyHavenAsset(id, id, PolyHavenType.Texture, "", [],
                        $"https://cdn.polyhaven.com/asset_img/thumbs/{id}.png?width=256&height=256", 0));
            _assets = installed;
        }
        else
        {
            string key = _type.ToString();
            if (_loading is null && _loadedKey != key)
            {
                _loadedKey = key;
                _loadError = null;
                _assets = [];
                _shown = PageSize;
                _loading = Task.Run(() => PolyHavenApi.GetAssetsAsync(_type));
            }

            if (_loading is { IsCompleted: true })
            {
                try
                {
                    _assets = [.. _loading.Result];
                    _byType[_type] = _assets;
                }
                catch (Exception ex)
                {
                    _loadError = Loc.Get("polyhaven.load_failed", new { error = (ex.InnerException ?? ex).Message });
                }
                _loading = null;
            }
        }
    }

    private void SetType(PolyHavenType type)
    {
        if (_type == type && !_downloadedTab) return;
        _type = type;
        _downloadedTab = false;
        _search = "";
        _status = "";
        _shown = PageSize;
        _loadedKey = null;
    }

    private void SetDownloadedTab()
    {
        if (_downloadedTab) return;
        _downloadedTab = true;
        _search = "";
        _status = "";
        _shown = PageSize;
        _scanned = false;
        _loadedKey = null;
    }

    private void ForceReload()
    {
        _loadedKey = null;
        _loadError = null;
        _assets = [];
        _shown = PageSize;
        _scanned = false;
        _byType.Clear();
        _allLoading = null;
    }

    /// <summary>Reads which Poly Haven assets are already in the project, and at what resolution.</summary>
    private void ScanDownloaded()
    {
        _downloaded.Clear();
        if (Project.Current is not { } project) return;
        string root = Path.Combine(project.AssetsPath, PolyHavenImport.FolderName);
        if (!Directory.Exists(root)) return;

        foreach (string folder in Directory.GetDirectories(root))
            _downloaded[Path.GetFileName(folder)] = ResolutionOf(folder);
    }

    /// <summary>The resolution encoded in a downloaded file name, e.g. &lt;id&gt;_1k_diffuse.jpg.</summary>
    private static string ResolutionOf(string folder)
    {
        foreach (string file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            int last = name.LastIndexOf('_');
            if (last < 0) continue;
            string tail = name[(last + 1)..];
            if (PolyHavenFiles.Resolutions.Contains(tail, StringComparer.OrdinalIgnoreCase))
                return tail.ToLowerInvariant();
        }
        return "";
    }

    /// <summary>Right-click menu of one asset: installed ones can be replaced at another resolution or deleted.</summary>
    private void BuildAssetMenu(ContextBuilder menu, PolyHavenAsset asset)
    {
        menu.Title(asset.Name, icon: EditorIcons.Globe);

        if (!_downloaded.ContainsKey(asset.Id))
        {
            menu.Item(Loc.Get("polyhaven.download"), () => Download(asset), icon: EditorIcons.Download);
            return;
        }

        menu.Header($"{Loc.Get("polyhaven.installed")}: {_downloaded[asset.Id].ToUpperInvariant()}");
        menu.Submenu(Loc.Get("polyhaven.redownload"), sub =>
        {
            foreach (string resolution in PolyHavenFiles.Resolutions)
            {
                string res = resolution;
                sub.Item(res.ToUpperInvariant(), () => ReDownload(asset, res), icon: EditorIcons.Download);
            }
        }, icon: EditorIcons.Download);
        menu.Item(Loc.Get("polyhaven.delete"), () => Origami.Confirm(
            Loc.Get("polyhaven.delete_title"), Loc.Get("polyhaven.delete_body", new { name = asset.Name }),
            () => DeleteDownloaded(asset)), icon: EditorIcons.Trash);
    }

    /// <summary>Deletes the downloaded files first, then starts a fresh job at the chosen resolution.</summary>
    private void ReDownload(PolyHavenAsset asset, string resolution)
    {
        if (_job is { IsDone: false })
        {
            _status = Loc.Get("polyhaven.busy");
            return;
        }

        EditorAssetBackend.Instance?.DeleteAssetFolder($"{PolyHavenImport.FolderName}/{asset.Id}");
        _resolution = resolution;
        _scanned = false;
        Download(asset);
    }

    /// <summary>Removes the downloaded folder and everything the database knows about it.</summary>
    private void DeleteDownloaded(PolyHavenAsset asset)
    {
        if (_job is { IsDone: false })
        {
            _status = Loc.Get("polyhaven.busy");
            return;
        }

        EditorAssetBackend.Instance?.DeleteAssetFolder($"{PolyHavenImport.FolderName}/{asset.Id}");
        _scanned = false;
        _status = Loc.Get("polyhaven.deleted", new { name = asset.Name });
    }

    private void Download(PolyHavenAsset asset)
    {
        if (_job is { IsDone: false })
        {
            _status = Loc.Get("polyhaven.busy");
            return;
        }

        _job = new PolyHavenImport(asset, _resolution);
        _job.Start();
        _status = _job.Status;
    }

    private static void Hint(Paper paper, string id, string text, Scribe.FontFile font)
        => paper.Box(id).Height(18)
            .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable()
            .Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 6);
}
