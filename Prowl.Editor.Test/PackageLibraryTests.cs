// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The package library: a package folder is mounted into Assets/ so the asset database imports it, and
/// unmounted again without touching the package's own files.
/// </summary>
public class PackageLibraryTests : EditorTestHarness
{
    private string PackageFolder(string name) => Path.Combine(Project.PackagesPath, name);
    private string ContentFolder(string name) => Path.Combine(PackageFolder(name), "Assets");

    private void WriteMaterial(string packageName, string relative)
    {
        string path = Path.Combine(ContentFolder(packageName), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Serializer.Serialize(typeof(object), new Material()).WriteToString());
    }

    [Fact]
    public void Create_WritesManifestAndMountsThePackage()
    {
        Assert.True(PackageLibrary.Create("demo", out string error), error);

        Assert.True(File.Exists(Path.Combine(PackageFolder("demo"), PackageLibrary.ManifestFileName)));

        LibraryPackage package = PackageLibrary.Find("demo")!;
        Assert.True(package.IsMounted);
        Assert.True(PackageLibrary.IsMount(package.MountPath));
        Assert.Equal(Path.GetFullPath(ContentFolder("demo")), Path.GetFullPath(package.ContentPath));
        Assert.True(Directory.Exists(package.MountPath), "the link must read as a folder through Assets");

        Assert.True(PackageLibrary.SetLoaded("demo", false, out error), error);
    }

    [Fact]
    public void Loading_ImportsThePackageAssets_AndUnloadingTakesThemOut()
    {
        // A package as it would be shipped: a manifest and an Assets folder.
        Directory.CreateDirectory(ContentFolder("props"));
        File.WriteAllText(Path.Combine(PackageFolder("props"), PackageLibrary.ManifestFileName),
            "{\n  \"name\": \"props\",\n  \"version\": \"2.1.0\",\n  \"assets\": \"Assets\"\n}");
        WriteMaterial("props", "Art/Crate.mat");

        Assert.True(PackageLibrary.SetLoaded("props", true, out string error), error);

        string mounted = "Packages/props/Art/Crate.mat";
        Assert.NotNull(Assets.GetEntry(mounted));
        Assert.True(File.Exists(AssetAbsolutePath(mounted)), "the asset should be readable through the mount");

        Assert.True(PackageLibrary.SetLoaded("props", false, out error), error);

        Assert.Null(Assets.GetEntry(mounted));
        Assert.False(File.Exists(AssetAbsolutePath(mounted)), "unloading removes the link, not the package");
        Assert.True(File.Exists(Path.Combine(ContentFolder("props"), "Art/Crate.mat")),
            "the package's own files must survive an unload");
        Assert.Equal("2.1.0", PackageLibrary.Find("props")!.Version);
    }

    [Fact]
    public void AFolderWithoutAContentFolderIsNotAPackage()
    {
        Directory.CreateDirectory(Path.Combine(PackageFolder("empty"), "Other"));
        Assert.DoesNotContain(PackageLibrary.Discover(), p => p.Name == "empty");
    }

    [Fact]
    public void ARealFolderInTheWayIsNotReplacedOrDeleted()
    {
        Directory.CreateDirectory(ContentFolder("blocked"));
        WriteMaterial("blocked", "Blocked.mat");
        string blocked = Path.Combine(Project.AssetsPath, PackageLibrary.MountFolderName, "blocked");
        Directory.CreateDirectory(blocked);

        Assert.False(PackageLibrary.SetLoaded("blocked", true, out string error));
        Assert.Contains("real folder", error);
        Assert.True(Directory.Exists(blocked));
    }
}
