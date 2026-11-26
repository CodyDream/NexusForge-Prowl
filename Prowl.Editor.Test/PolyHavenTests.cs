// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Prowl.Editor.PolyHaven;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// Covers the parts of the Poly Haven integration that do not need the network: reading an /assets entry and
/// choosing which files of a /files document to download, including the local names the texture importer reads
/// and the maps that are dropped because another file already carries the same data.
/// </summary>
public class PolyHavenTests
{
    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    [Fact]
    public void ParseAsset_ReadsNameTypeCategoryAndTags()
    {
        JsonObject node = Json("""
        {
          "name": "Concrete Floor 01",
          "category": "Concrete/Cast Walls & Floors/Exposed Aggregate",
          "tags": ["rough", "concrete"],
          "thumbnail_url": "https://cdn.polyhaven.com/asset_img/thumbs/concrete_floor_01.png",
          "type": 1
        }
        """);

        PolyHavenAsset? asset = PolyHavenApi.ParseAsset("concrete_floor_01", node);

        Assert.NotNull(asset);
        Assert.Equal("Concrete Floor 01", asset!.Name);
        Assert.Equal(PolyHavenType.Texture, asset.Type);
        Assert.Equal("Concrete/Cast Walls & Floors/Exposed Aggregate", asset.Category);
        Assert.Equal(["rough", "concrete"], asset.Tags);
    }

    [Fact]
    public void ParseAsset_MapsEachTypeNumber()
    {
        Assert.Equal(PolyHavenType.Hdri, PolyHavenApi.ParseAsset("a", Json("""{"name":"a","type":0}"""))!.Type);
        Assert.Equal(PolyHavenType.Texture, PolyHavenApi.ParseAsset("b", Json("""{"name":"b","type":1}"""))!.Type);
        Assert.Equal(PolyHavenType.Model, PolyHavenApi.ParseAsset("c", Json("""{"name":"c","type":2}"""))!.Type);
        Assert.Null(PolyHavenApi.ParseAsset("d", Json("""{"name":"d","type":9}""")));
    }

    [Fact]
    public void ParseAsset_FallsBackToTheIdWhenThereIsNoName()
    {
        Assert.Equal("sunset", PolyHavenApi.ParseAsset("sunset", Json("""{"type":0}"""))!.Name);
    }

    [Fact]
    public void HdriPlan_TakesTheHdrAtTheChosenResolution()
    {
        JsonObject files = Json("""
        {
          "hdri": {
            "1k": { "hdr": { "url": "https://dl.polyhaven.org/1k/sunset_1k.hdr" } },
            "4k": { "hdr": { "url": "https://dl.polyhaven.org/4k/sunset_4k.hdr" }, "exr": { "url": "https://dl.polyhaven.org/4k/sunset_4k.exr" } }
          }
        }
        """);

        IReadOnlyList<PolyHavenFiles.PlannedFile> plan = PolyHavenFiles.Plan(PolyHavenType.Hdri, files, "4k", "sunset");

        PolyHavenFiles.PlannedFile file = Assert.Single(plan);
        Assert.Equal("sunset_4k.hdr", file.RelativePath);
        Assert.Equal("https://dl.polyhaven.org/4k/sunset_4k.hdr", file.Url);
    }

    [Fact]
    public void HdriPlan_FallsBackToExrAndIgnoresAMissingResolution()
    {
        JsonObject files = Json("""
        { "hdri": { "2k": { "exr": { "url": "https://dl.polyhaven.org/2k/x.exr" } } } }
        """);

        Assert.Equal("x_2k.exr", Assert.Single(PolyHavenFiles.Plan(PolyHavenType.Hdri, files, "2k", "x")).RelativePath);
        Assert.Empty(PolyHavenFiles.Plan(PolyHavenType.Hdri, files, "8k", "x"));
    }

    [Fact]
    public void TexturePlan_ReadsTheMapFirstDocumentAndNamesMapsSoTheImporterReadsTheirKind()
    {
        JsonObject files = Json("""
        {
          "Diffuse": { "2k": { "jpg": { "url": "https://dl.polyhaven.org/2k/brick_diff_2k.jpg" } } },
          "nor_gl": { "2k": { "jpg": { "url": "https://dl.polyhaven.org/2k/brick_nor_gl_2k.jpg" } } },
          "arm": { "2k": { "jpg": { "url": "https://dl.polyhaven.org/2k/brick_arm_2k.jpg" } } },
          "Displacement": { "2k": { "png": { "url": "https://dl.polyhaven.org/2k/brick_disp_2k.png" } } }
        }
        """);

        List<string> names = [.. PolyHavenFiles.Plan(PolyHavenType.Texture, files, "2k", "brick").Select(f => f.RelativePath)];

        // The texture importer reads "nor" as a tangent space normal map and "mask"/"displacement" as linear data.
        Assert.Equal(["brick_2k_diffuse.jpg", "brick_2k_nor_gl.jpg", "brick_2k_arm_mask.jpg", "brick_2k_displacement.png"], names);
    }

    [Fact]
    public void TexturePlan_PrefersThePackedArmOverTheSeparateMapsAndNeverTakesTheDirectXNormal()
    {
        JsonObject files = Json("""
        {
          "Diffuse": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_diff_1k.jpg" } } },
          "nor_gl": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_nor_gl_1k.jpg" } } },
          "nor_dx": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_nor_dx_1k.jpg" } } },
          "arm": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_arm_1k.jpg" } } },
          "AO": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_ao_1k.jpg" } } },
          "Rough": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_rough_1k.jpg" } } },
          "rough_ao": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_rough_ao_1k.jpg" } } },
          "Metal": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_metal_1k.jpg" } } },
          "Bump": { "1k": { "jpg": { "url": "https://dl.polyhaven.org/1k/rock_bump_1k.jpg" } } },
          "blend": { "1k": { "blend": { "url": "https://dl.polyhaven.org/1k/rock_1k.blend" } } }
        }
        """);

        List<string> names = [.. PolyHavenFiles.Plan(PolyHavenType.Texture, files, "1k", "rock").Select(f => f.RelativePath)];

        Assert.Equal(["rock_1k_diffuse.jpg", "rock_1k_nor_gl.jpg", "rock_1k_arm_mask.jpg"], names);
    }

    [Fact]
    public void TexturePlan_FallsBackToSeparateSurfaceMapsWhenThereIsNoArm()
    {
        JsonObject files = Json("""
        {
          "Diffuse": { "1k": { "png": { "url": "https://dl.polyhaven.org/1k/tile_diff_1k.png" } } },
          "AO": { "1k": { "png": { "url": "https://dl.polyhaven.org/1k/tile_ao_1k.png" } } },
          "Rough": { "1k": { "png": { "url": "https://dl.polyhaven.org/1k/tile_rough_1k.png" } } },
          "Metal": { "1k": { "png": { "url": "https://dl.polyhaven.org/1k/tile_metal_1k.png" } } }
        }
        """);

        List<string> names = [.. PolyHavenFiles.Plan(PolyHavenType.Texture, files, "1k", "tile").Select(f => f.RelativePath)];

        Assert.Equal(["tile_1k_diffuse.png", "tile_1k_occlusion.png", "tile_1k_roughness.png", "tile_1k_metallic.png"], names);
    }

    [Fact]
    public void TexturePlan_UnpackedArmMapsAreAlwaysPngWhateverTheSourceFormatWas()
    {
        JsonObject files = Json("""
        {
          "arm": { "4k": { "jpg": { "url": "https://dl.polyhaven.org/4k/wood_arm_4k.jpg" } } }
        }
        """);

        PolyHavenFiles.PlannedFile file = Assert.Single(PolyHavenFiles.Plan(PolyHavenType.Texture, files, "4k", "wood"));

        Assert.Equal(PolyHavenMap.Arm, file.Map);
        Assert.Equal("wood_4k_arm_mask.jpg", file.RelativePath);
        Assert.Equal("wood_4k_occlusion.png", PolyHavenFiles.FileName("wood", "4k", PolyHavenMap.Occlusion, ".jpg"));
        Assert.Equal("wood_4k_metallic.png", PolyHavenFiles.FileName("wood", "4k", PolyHavenMap.Metalness, ".jpg"));
    }

    [Fact]
    public void ModelPlan_KeepsTheGltfAndThePathsItReferences()
    {
        JsonObject files = Json("""
        {
          "gltf": {
            "1k": {
              "gltf": {
                "url": "https://dl.polyhaven.org/1k/ball_1k.gltf",
                "include": {
                  "textures/ball_diffuse.jpg": { "url": "https://dl.polyhaven.org/1k/textures/ball_diffuse.jpg" },
                  "ball.bin": { "url": "https://dl.polyhaven.org/1k/ball.bin" }
                }
              }
            }
          }
        }
        """);

        List<string> names = [.. PolyHavenFiles.Plan(PolyHavenType.Model, files, "1k", "ball").Select(f => f.RelativePath)];

        Assert.Equal(["ball_1k.gltf", "textures/ball_diffuse.jpg", "ball.bin"], names);
    }

    /// <summary>
    /// The packed arm image is read channel by channel and written back out as png, so this walks the same
    /// Aperture calls the import makes: pixels in, png out, pixels back again.
    /// </summary>
    [Fact]
    public void ArmSplit_RoundTripsEachChannelThroughAPng()
    {
        byte[] pixels = [10, 20, 30, 255, 200, 150, 100, 255];

        using Aperture.Image packed = Aperture.Image.FromPixels(pixels, 2, 1,
            Aperture.PixelFormat.Rgba8, 8, false);
        byte[] png = packed.Encode(new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png });

        using Aperture.Image split = Aperture.Image.Load(png,
            new Aperture.DecodeOptions { TargetPixelFormat = Aperture.PixelFormat.Rgba8 });
        byte[] read = new byte[8];
        split.RootFrame.CopyTo(read, 8);

        Assert.Equal(2, split.Width);
        Assert.Equal(1, split.Height);
        Assert.Equal([10, 20, 30, 255, 200, 150, 100, 255], read);
    }

    [Fact]
    public void ThumbnailUrl_AsksForASmallJpegPreview()
    {
        const string url = "https://cdn.polyhaven.com/asset_img/thumbs/ArmChair_01.png?width=256&height=256&format=webp&v=abc";

        string small = PolyHavenThumbnails.SmallUrl(url);

        Assert.Equal("https://cdn.polyhaven.com/asset_img/thumbs/ArmChair_01.png?width=256&height=256&format=jpg&v=abc", small);
    }
}
