// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Lists the packages in the project's Packages/ folder and mounts or unmounts them from Assets/.
/// A mounted package's assets appear in the project panel like any other asset; unmounting removes them
/// from the asset database and leaves the package's files untouched.
/// </summary>
public class PackagesPanel : DockPanel
{
    [MenuItem("Window/General/Packages", priority: 7)]
    static void Open() => EditorApplication.Instance?.OpenPanel<PackagesPanel>();

    public override string Title => Loc.Get("panel.packages");
    public override string Icon => EditorIcons.BoxArchive;

    private List<LibraryPackage> _packages = [];
    private string? _scannedFor;
    private bool _reload;
    private string _newName = "";
    private string _status = "";

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (Project.Current is null)
        {
            paper.Box("pkg_noproject").Height(30)
                .Text(Loc.Get("packages.no_project"), font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        RescanIfNeeded();

        Origami.ScrollView(paper, "pkg_scroll", width, height).Body(() =>
        {
            using (paper.Column("pkg_col").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
            {
                EditorGUI.SectionHeader(paper, "pkg_head", Loc.Get("packages.heading"), first: true);

                paper.Box("pkg_hint").Height(UnitValue.Auto)
                    .Text(Loc.Get("packages.hint"), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                    .Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 10);

                EditorGUI.SettingsRow(paper, "pkg_folder", Loc.Get("packages.folder"), () =>
                    Origami.Button(paper, "pkg_folder_btn", $"{EditorIcons.ArrowsRotate}  {Loc.Get("packages.refresh")}",
                        ForceRescan).Small().Show());

                if (_packages.Count == 0)
                {
                    paper.Box("pkg_empty").Height(UnitValue.Auto)
                        .Text(Loc.Get("packages.empty"), font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                        .Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 4, 12);
                }

                foreach (LibraryPackage package in _packages)
                    DrawPackage(paper, font, package);

                EditorGUI.SectionHeader(paper, "pkg_new_head", Loc.Get("packages.new"));

                EditorGUI.SettingsRow(paper, "pkg_new_name", Loc.Get("packages.name"), () =>
                    Origami.TextField(paper, "pkg_new_name_v", _newName, v => _newName = v).Show());

                using (paper.Row("pkg_new_row").Height(38).Margin(Origami.Current.Metrics.PaddingLarge, 0, 4, 0).Enter())
                {
                    Origami.Button(paper, "pkg_new_create", $"{EditorIcons.Plus}  {Loc.Get("packages.create")}", Create)
                        .Height(28).Show();
                }

                if (_status.Length > 0)
                {
                    paper.Box("pkg_status").Height(UnitValue.Auto)
                        .Text(_status, font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                        .Margin(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 2, 8);
                }
            }
        });
    }

    private void DrawPackage(Paper paper, Scribe.FontFile font, LibraryPackage package)
    {
        EditorGUI.SettingsToggle(paper, $"pkg_{package.Name}", package.Name, package.IsMounted,
            loaded => SetLoaded(package, loaded));

        string info = $"{package.Version}  ·  Packages/{package.Name}" + (package.HasScripts ? "  ·  scripts" : "");
        paper.Box($"pkg_{package.Name}_info").Height(18)
            .Text(info, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable()
            .Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 8);
    }

    private void SetLoaded(LibraryPackage package, bool loaded)
    {
        bool ok = PackageLibrary.SetLoaded(package.Name, loaded, out string error);
        _status = ok
            ? Loc.Get(loaded ? "packages.loaded_msg" : "packages.unloaded_msg", new { name = package.Name })
            : error;
        _reload = true;
    }

    private void Create()
    {
        bool ok = PackageLibrary.Create(_newName, out string error);
        _status = ok ? Loc.Get("packages.created_msg", new { name = _newName.Trim() }) : error;
        if (ok) _newName = "";
        _reload = true;
    }

    private void RescanIfNeeded()
    {
        string? root = Project.Current?.RootPath;
        if (_reload || _scannedFor != root)
        {
            _reload = false;
            _scannedFor = root;
            _packages = [.. PackageLibrary.Discover()];
        }
    }

    private void ForceRescan()
    {
        _status = "";
        _reload = true;
    }
}
