using System.Drawing;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager.Tests;

public sealed class ResolutionPrepMathTests
{
    [Fact]
    public void AlignDown_snaps_to_64_and_rejects_below_minimum()
    {
        Assert.Equal(0, ResolutionPrepMath.AlignDown(63));
        Assert.Equal(64, ResolutionPrepMath.AlignDown(64));
        Assert.Equal(960, ResolutionPrepMath.AlignDown(1000));
        Assert.Equal(1024, ResolutionPrepMath.AlignDown(1024));
    }

    [Fact]
    public void TryNormalizeGear_rejects_out_of_range_and_aligns()
    {
        Assert.Null(ResolutionPrepMath.TryNormalizeGear(50));
        Assert.Null(ResolutionPrepMath.TryNormalizeGear(9000));
        Assert.Equal(64, ResolutionPrepMath.TryNormalizeGear(70));
        Assert.Equal(960, ResolutionPrepMath.TryNormalizeGear(1000));
    }

    [Fact]
    public void MergeGears_keeps_defaults_and_dedups_custom()
    {
        IReadOnlyList<int> gears = ResolutionPrepMath.MergeGears(new[] { 1000, 1024, 512, 70 });

        Assert.Equal(new[] { 64, 512, 768, 896, 960, 1024, 1280, 1536 }, gears);
    }

    [Fact]
    public void ScaleToLongEdge_1920x1080_to_1024()
    {
        Size? size = ResolutionPrepMath.ScaleToLongEdge(new Size(1920, 1080), 1024);

        Assert.Equal(new Size(1024, 576), size);
    }

    [Fact]
    public void ScaleToLongEdge_does_not_upscale()
    {
        Assert.Null(ResolutionPrepMath.ScaleToLongEdge(new Size(800, 600), 1024));
    }

    [Fact]
    public void ScaleToLongEdge_identity_when_already_at_gear()
    {
        Size? size = ResolutionPrepMath.ScaleToLongEdge(new Size(1024, 1024), 1024);

        Assert.Equal(new Size(1024, 1024), size);
    }

    [Fact]
    public void CenterCrop_square_from_landscape()
    {
        Rectangle? crop = ResolutionPrepMath.CenterCrop(new Size(1920, 1080), 1, 1);

        Assert.Equal(new Rectangle(420, 0, 1080, 1080), crop);
    }

    [Fact]
    public void TileSize_16_9_at_1024()
    {
        Size? tile = ResolutionPrepMath.TileSize(1024, 16, 9);

        Assert.Equal(new Size(1024, 576), tile);
    }

    [Fact]
    public void PlaceTiles_exact_grid()
    {
        IReadOnlyList<Rectangle> tiles = ResolutionPrepMath.PlaceTiles(new Size(4096, 4096), new Size(1024, 1024));

        Assert.Equal(16, tiles.Count);
        Assert.Equal(new Rectangle(0, 0, 1024, 1024), tiles[0]);
        Assert.Equal(new Rectangle(3072, 3072, 1024, 1024), tiles[15]);
    }

    [Fact]
    public void PlaceTiles_last_row_and_column_cover_the_edge()
    {
        IReadOnlyList<Rectangle> tiles = ResolutionPrepMath.PlaceTiles(new Size(4000, 2000), new Size(1024, 1024));

        Assert.Contains(tiles, t => t.X == 0 && t.Y == 0);
        Assert.Contains(tiles, t => t.Right == 4000);
        Assert.Contains(tiles, t => t.Bottom == 2000);
        Assert.All(tiles, t =>
        {
            Assert.Equal(1024, t.Width);
            Assert.Equal(1024, t.Height);
            Assert.InRange(t.X, 0, 4000 - 1024);
            Assert.InRange(t.Y, 0, 2000 - 1024);
        });
    }

    [Fact]
    public void PlaceTiles_skips_when_image_is_smaller_than_tile()
    {
        IReadOnlyList<Rectangle> tiles = ResolutionPrepMath.PlaceTiles(new Size(512, 512), new Size(1024, 1024));

        Assert.Empty(tiles);
    }

    [Fact]
    public void Plan_scale_only_emits_one_job_per_gear()
    {
        var items = new (string Path, Size? Size)[] { (@"C:\a.png", new Size(2048, 1536)) };
        var plan = ResolutionPrepMath.Plan(items, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.ScaleOnly,
            Gears = new[] { 768, 1024 }
        });

        Assert.Equal(2, plan.Jobs.Count);
        Assert.Equal("_768", plan.Jobs[0].Suffix);
        Assert.Equal("_1024", plan.Jobs[1].Suffix);
        Assert.Equal(new Size(1024, 768), plan.Jobs[1].OutputSize);
        Assert.Equal(0, plan.SkippedImages);
    }

    [Fact]
    public void Plan_center_crop_uses_aspect_in_suffix()
    {
        var items = new (string Path, Size? Size)[] { (@"C:\a.png", new Size(1920, 1080)) };
        var plan = ResolutionPrepMath.Plan(items, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.CenterCrop,
            AspectWidth = 1,
            AspectHeight = 1,
            Gears = new[] { 1024 }
        });

        Assert.Single(plan.Jobs);
        Assert.Equal("_1-1_1024", plan.Jobs[0].Suffix);
        Assert.Equal(new Rectangle(420, 0, 1080, 1080), plan.Jobs[0].SourceRect);
        Assert.Equal(new Size(1024, 1024), plan.Jobs[0].OutputSize);
    }

    [Fact]
    public void Plan_split_indexes_tiles_and_skips_undersized_gears()
    {
        var items = new (string Path, Size? Size)[] { (@"C:\a.png", new Size(2048, 2048)) };
        var plan = ResolutionPrepMath.Plan(items, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.SplitTiles,
            AspectWidth = 1,
            AspectHeight = 1,
            Gears = new[] { 1024, 4096 }
        });

        Assert.Equal(4, plan.Jobs.Count);
        Assert.Equal("_1-1_1024_01", plan.Jobs[0].Suffix);
        Assert.Equal("_1-1_1024_04", plan.Jobs[3].Suffix);
        Assert.Equal(1, plan.SkippedGears);
        Assert.Equal(0, plan.SkippedImages);
    }

    [Fact]
    public void Plan_skips_images_that_would_upscale()
    {
        var items = new (string Path, Size? Size)[] { (@"C:\small.png", new Size(512, 512)) };
        var plan = ResolutionPrepMath.Plan(items, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.ScaleOnly,
            Gears = new[] { 1024 }
        });

        Assert.Empty(plan.Jobs);
        Assert.Equal(1, plan.SkippedImages);
    }

    [Fact]
    public void AllocateOutputPath_avoids_existing_and_reserved_names()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\out\a_1024.png"
        };

        string first = ResolutionPrepMath.AllocateOutputPath(@"C:\out\a.png", "_1024", reserved, existing.Contains);
        string second = ResolutionPrepMath.AllocateOutputPath(@"C:\out\a.png", "_1024", reserved, existing.Contains);

        Assert.Equal(@"C:\out\a_10242.png", first);
        Assert.Equal(@"C:\out\a_10243.png", second);
    }

    [Fact]
    public void RandomCrop_stays_in_bounds_and_matches_center_size()
    {
        var image = new Size(1920, 1080);
        Rectangle? center = ResolutionPrepMath.CenterCrop(image, 1, 1);
        Rectangle? crop = ResolutionPrepMath.RandomCrop(image, 1, 1, new Random(42));

        Assert.NotNull(center);
        Assert.NotNull(crop);
        Assert.Equal(center.Value.Size, crop.Value.Size);
        Assert.InRange(crop.Value.X, 0, image.Width - crop.Value.Width);
        Assert.InRange(crop.Value.Y, 0, image.Height - crop.Value.Height);
        Assert.Equal(crop.Value.Width, crop.Value.Height);
    }

    [Fact]
    public void RandomCrop_only_one_position_when_aspect_already_fills()
    {
        Rectangle? crop = ResolutionPrepMath.RandomCrop(new Size(1080, 1080), 1, 1, new Random(7));

        Assert.Equal(new Rectangle(0, 0, 1080, 1080), crop);
    }

    [Fact]
    public void Plan_random_emits_n_independent_crops_with_suffix()
    {
        var items = new (string Path, Size? Size)[] { (@"C:\a.png", new Size(1920, 1080)) };
        var plan = ResolutionPrepMath.Plan(items, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.RandomCrop,
            AspectWidth = 1,
            AspectHeight = 1,
            Gears = new[] { 1024 },
            RandomCount = 3,
            Random = new Random(1)
        });

        Assert.Equal(3, plan.Jobs.Count);
        Assert.Equal("_1-1_rand1_1024", plan.Jobs[0].Suffix);
        Assert.Equal("_1-1_rand3_1024", plan.Jobs[2].Suffix);
        Assert.Equal(plan.Jobs[0].SourceRect.Size, plan.Jobs[1].SourceRect.Size);
    }

    [Fact]
    public void ResolveSourcePaths_all_images_when_nothing_is_selected()
    {
        string[] all = { @"C:\a.png", @"C:\b.png" };
        IReadOnlyList<string> paths = ResolutionPrepMath.ResolveSourcePaths(
            ResolutionPrepSource.AllImages,
            Array.Empty<string>(),
            new[] { @"C:\folder.png" },
            all);

        Assert.Equal(all, paths);
    }

    [Fact]
    public void Plan_yolo_mode_without_crops_skips_the_image()
    {
        var plan = ResolutionPrepMath.Plan(
            new (string Path, Size? Size)[] { (@"C:\a.png", new Size(1920, 1080)) },
            new ResolutionPrepRequest
            {
                Mode = ResolutionPrepMode.YoloPerson,
                Gears = new[] { 1024 }
            });

        Assert.Empty(plan.Jobs);
        Assert.Equal(1, plan.SkippedImages);
    }

    [Fact]
    public void PlanFromCrops_writes_yolo_suffix_per_box()
    {
        var crops = new (string Path, Size Size, IReadOnlyList<Rectangle> Crops)[]
        {
            (@"C:\a.png", new Size(1920, 1080), new[]
            {
                new Rectangle(100, 0, 1080, 1080),
                new Rectangle(400, 0, 1080, 1080)
            })
        };
        var plan = ResolutionPrepMath.PlanFromCrops(crops, new ResolutionPrepRequest
        {
            Mode = ResolutionPrepMode.YoloPerson,
            AspectWidth = 1,
            AspectHeight = 1,
            Gears = new[] { 1024 }
        });

        Assert.Equal(2, plan.Jobs.Count);
        Assert.Equal("_1-1_yolo1_1024", plan.Jobs[0].Suffix);
        Assert.Equal("_1-1_yolo2_1024", plan.Jobs[1].Suffix);
        Assert.Equal(new Size(1024, 1024), plan.Jobs[0].OutputSize);
    }
}

public sealed class ResolutionPrepServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "bdtm-res-prep-" + Guid.NewGuid().ToString("N"));

    public ResolutionPrepServiceTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        catch
        {
            // temp cleanup is best-effort
        }
    }

    [Fact]
    public void TryWrite_scale_only_matches_planned_size()
    {
        string source = Path.Combine(root, "wide.png");
        WritePng(source, 1920, 1080);

        var plan = ResolutionPrepMath.Plan(
            new[] { (source, ResolutionPrepService.TryGetImageSize(source)) },
            new ResolutionPrepRequest { Mode = ResolutionPrepMode.ScaleOnly, Gears = new[] { 1024 } });
        ResolutionPrepService.AssignOutputPaths(plan.Jobs);

        string written = ResolutionPrepService.TryWrite(plan.Jobs[0], sharpen: false, tagExtensions: null);

        Assert.Equal(plan.Jobs[0].OutputPath, written);
        Assert.True(File.Exists(written));
        ImageInfo info = SixLabors.ImageSharp.Image.Identify(written);
        Assert.Equal(1024, info.Width);
        Assert.Equal(576, info.Height);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void TryWrite_center_crop_is_square_and_clones_caption()
    {
        string source = Path.Combine(root, "photo.png");
        WritePng(source, 1920, 1080);
        File.WriteAllText(Path.Combine(root, "photo.txt"), "1girl, solo");

        var plan = ResolutionPrepMath.Plan(
            new (string Path, Size? Size)[] { (source, new Size(1920, 1080)) },
            new ResolutionPrepRequest
            {
                Mode = ResolutionPrepMode.CenterCrop,
                AspectWidth = 1,
                AspectHeight = 1,
                Gears = new[] { 1024 }
            });
        ResolutionPrepService.AssignOutputPaths(plan.Jobs);

        string written = ResolutionPrepService.TryWrite(plan.Jobs[0], sharpen: false, new[] { "txt" });

        Assert.NotNull(written);
        ImageInfo info = SixLabors.ImageSharp.Image.Identify(written);
        Assert.Equal(1024, info.Width);
        Assert.Equal(1024, info.Height);
        string caption = Path.Combine(root, Path.GetFileNameWithoutExtension(written) + ".txt");
        Assert.Equal("1girl, solo", File.ReadAllText(caption));
    }

    [Fact]
    public void TryWrite_split_keeps_tile_pixel_size()
    {
        string source = Path.Combine(root, "big.png");
        WritePng(source, 2048, 2048);

        var plan = ResolutionPrepMath.Plan(
            new (string Path, Size? Size)[] { (source, new Size(2048, 2048)) },
            new ResolutionPrepRequest
            {
                Mode = ResolutionPrepMode.SplitTiles,
                AspectWidth = 1,
                AspectHeight = 1,
                Gears = new[] { 1024 }
            });
        ResolutionPrepService.AssignOutputPaths(plan.Jobs);

        Assert.Equal(4, plan.Jobs.Count);
        foreach (ResolutionPrepJob job in plan.Jobs)
        {
            string written = ResolutionPrepService.TryWrite(job, sharpen: false, tagExtensions: null);
            Assert.NotNull(written);
            ImageInfo info = SixLabors.ImageSharp.Image.Identify(written);
            Assert.Equal(1024, info.Width);
            Assert.Equal(1024, info.Height);
        }
        Assert.True(File.Exists(source));
    }

    private static void WritePng(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 80, 120, 255));
        image.SaveAsPng(path);
    }
}
