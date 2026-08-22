using System.Drawing;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;

namespace BooruDatasetTagManager.Tests;

public sealed class PreBucketMathTests
{
    [Fact]
    public void MakeBucketResolutions_512_matchesKohyaPairs()
    {
        IReadOnlyList<Size> resos = PreBucketMath.MakeBucketResolutions(
            new Size(512, 512), 256, 1024, 64);

        Assert.Contains(new Size(512, 512), resos);
        Assert.Contains(new Size(256, 1024), resos);
        Assert.Contains(new Size(1024, 256), resos);
        Assert.Contains(new Size(384, 640), resos);
        Assert.Contains(new Size(640, 384), resos);
        Assert.All(resos, size =>
        {
            Assert.True(size.Width % 64 == 0);
            Assert.True(size.Height % 64 == 0);
            Assert.InRange(size.Width, 256, 1024);
            Assert.InRange(size.Height, 256, 1024);
        });
    }

    [Fact]
    public void MakeBucketResolutions_1536_includesSquareAndBothOrientations()
    {
        IReadOnlyList<Size> resos = PreBucketMath.MakeBucketResolutions(
            new Size(1536, 1536), 256, 4096, 64);

        Assert.Contains(new Size(1536, 1536), resos);
        Assert.True(resos.Count > 20);
        Assert.Contains(resos, size => size.Width > size.Height);
        Assert.Contains(resos, size => size.Height > size.Width);
    }

    [Fact]
    public void Align_minDown_maxUp()
    {
        int min = 250;
        int max = 1000;
        PreBucketMath.AdjustMinMaxBySteps(64, ref min, ref max);
        Assert.Equal(192, min);
        Assert.Equal(1024, max);
    }

    [Fact]
    public void NormalizeResolution_snapsDownToSteps()
    {
        Size size = PreBucketMath.NormalizeResolution(1590, 900, 64);
        Assert.Equal(1536, size.Width);
        Assert.Equal(896, size.Height);
    }

    [Fact]
    public void SelectClosest_prefersMatchingAspect()
    {
        var buckets = new[]
        {
            new Size(1024, 1024),
            new Size(768, 1344),
            new Size(1344, 768)
        };

        Assert.Equal(new Size(1344, 768), PreBucketMath.SelectClosest(new Size(1920, 1080), buckets));
        Assert.Equal(new Size(1024, 1024), PreBucketMath.SelectClosest(new Size(1024, 1024), buckets));
        Assert.Equal(new Size(768, 1344), PreBucketMath.SelectClosest(new Size(800, 1400), buckets));
    }

    [Fact]
    public void FitInside_letterboxesWithoutCropping()
    {
        Size fitted = PreBucketMath.FitInside(new Size(1000, 500), new Size(1024, 1024), allowUpscale: true);
        Assert.Equal(1024, fitted.Width);
        Assert.Equal(512, fitted.Height);

        Point offset = PreBucketMath.CenterOffset(fitted, new Size(1024, 1024));
        Assert.Equal(0, offset.X);
        Assert.Equal(256, offset.Y);
    }

    [Fact]
    public void FitInside_noUpscaleOnlyPads()
    {
        Size fitted = PreBucketMath.FitInside(new Size(200, 100), new Size(1024, 1024), allowUpscale: false);
        Assert.Equal(200, fitted.Width);
        Assert.Equal(100, fitted.Height);
        Assert.Equal(new Point(412, 462), PreBucketMath.CenterOffset(fitted, new Size(1024, 1024)));
    }

    [Fact]
    public void ReduceToTarget_mergesClosestAspectsFirst()
    {
        var used = new[]
        {
            new Size(1024, 1024),
            new Size(960, 1088),
            new Size(768, 1344)
        };
        var images = new[]
        {
            new Size(1024, 1024),
            new Size(960, 1088),
            new Size(768, 1344)
        };

        IReadOnlyList<Size> reduced = PreBucketMath.ReduceToTarget(used, images, 2);

        Assert.Equal(2, reduced.Count);
        Assert.Contains(new Size(768, 1344), reduced);
        Assert.True(
            reduced.Contains(new Size(1024, 1024)) || reduced.Contains(new Size(960, 1088)));
    }

    [Fact]
    public void Plan_disableBucket_usesSingleResolutionFolder()
    {
        var plan = PreBucketMath.Plan(
            new (string Path, Size? Size)[]
            {
                (@"C:\ds\a.png", new Size(1920, 1080)),
                (@"C:\ds\b.png", new Size(800, 1200))
            },
            new PreBucketSettings
            {
                ResolutionWidth = 1536,
                ResolutionHeight = 1536,
                EnableBucket = false
            });

        Assert.Equal(2, plan.Jobs.Count);
        Assert.All(plan.Jobs, job => Assert.Equal(new Size(1536, 1536), job.BucketSize));
        Assert.Equal("1536x1536", Assert.Single(plan.Groups).FolderName);
        Assert.Equal(1, plan.KohyaUsedCount);
    }

    [Fact]
    public void Plan_enableBucket_assignsClosestAndCanReduce()
    {
        var plan = PreBucketMath.Plan(
            new (string Path, Size? Size)[]
            {
                (@"C:\ds\wide.png", new Size(1920, 1080)),
                (@"C:\ds\tall.png", new Size(800, 1400)),
                (@"C:\ds\square.png", new Size(1024, 1024))
            },
            new PreBucketSettings
            {
                ResolutionWidth = 1024,
                ResolutionHeight = 1024,
                EnableBucket = true,
                MinBucketReso = 256,
                MaxBucketReso = 2048,
                BucketResoSteps = 64,
                TargetBucketCount = 2
            });

        Assert.Equal(3, plan.Jobs.Count);
        Assert.Equal(2, plan.Groups.Count);
        Assert.True(plan.KohyaUsedCount >= 2);
        Assert.All(plan.Jobs, job =>
        {
            Assert.True(job.FittedSize.Width <= job.BucketSize.Width);
            Assert.True(job.FittedSize.Height <= job.BucketSize.Height);
        });
    }

    [Fact]
    public void Plan_skipsVideosAndBadSizes()
    {
        var plan = PreBucketMath.Plan(
            new (string Path, Size? Size)[]
            {
                (@"C:\ds\clip.mp4", new Size(1920, 1080)),
                (@"C:\ds\bad.png", new Size(0, 10)),
                (@"C:\ds\ok.png", new Size(512, 512))
            },
            new PreBucketSettings { EnableBucket = false, ResolutionWidth = 512, ResolutionHeight = 512 });

        Assert.Equal(3, plan.ImageCount);
        Assert.Equal(2, plan.SkippedImages);
        Assert.Single(plan.Jobs);
    }

    [Fact]
    public void StepEstimate_usesCeilSoSmallSetsAreNotZero()
    {
        Assert.Equal(936, PreBucketMath.TheoreticalSteps(156, 1, 4, 24));
        Assert.Equal(1, PreBucketMath.TheoreticalSteps(23, 1, 26, 1));
        Assert.Equal(12, PreBucketMath.BucketedSteps(new[] { 1, 1, 1 }, 1, 4, 4));
        Assert.Equal(4, PreBucketMath.BucketedSteps(new[] { 10, 5, 5, 3 }, 1, 26, 1));
    }

    [Fact]
    public void AllocateOutputPath_staysUnderRootAndAvoidsCollisions()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\out\1024x1536\a.png"
        };

        string first = PreBucketMath.AllocateOutputPath(
            @"C:\out", new Size(1024, 1536), @"C:\ds\a.png", reserved, existing.Contains);
        string second = PreBucketMath.AllocateOutputPath(
            @"C:\out", new Size(1024, 1536), @"C:\ds\a.png", reserved, existing.Contains);

        Assert.Equal(@"C:\out\1024x1536\a2.png", first);
        Assert.Equal(@"C:\out\1024x1536\a3.png", second);
        Assert.StartsWith(@"C:\out\1024x1536\", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllocateOutputPath_rejectsEmptyFileName()
    {
        Assert.Null(PreBucketMath.AllocateOutputPath(
            @"C:\out", new Size(1024, 1024), @"C:\ds\.", new HashSet<string>(), _ => false));
    }

    [Fact]
    public void FolderName_isWidthXHeight()
    {
        Assert.Equal("1024x1536", PreBucketMath.FolderName(new Size(1024, 1536)));
    }

    [Fact]
    public void ResolveSourcePaths_reusesPrepSourceEnum()
    {
        IReadOnlyList<string> paths = PreBucketMath.ResolveSourcePaths(
            ResolutionPrepSource.AllImages,
            new[] { "sel" },
            new[] { "folder" },
            new[] { "all" });
        Assert.Equal(new[] { "all" }, paths);
    }

    [Fact]
    public void CollectRemovableSources_skipsUnwrittenAndSamePath()
    {
        string root = Path.GetFullPath(@"C:\ds");
        var jobs = new[]
        {
            new PreBucketJob
            {
                SourcePath = Path.Combine(root, "old", "a.png"),
                OutputPath = Path.Combine(root, "1024x1024", "a.png")
            },
            new PreBucketJob
            {
                SourcePath = Path.Combine(root, "old", "fail.png"),
                OutputPath = Path.Combine(root, "1024x1024", "fail.png")
            },
            new PreBucketJob
            {
                SourcePath = Path.Combine(root, "1024x1024", "same.png"),
                OutputPath = Path.Combine(root, "1024x1024", "same.png")
            }
        };

        IReadOnlyList<string> removable = PreBucketMath.CollectRemovableSources(
            jobs,
            new[] { jobs[0].OutputPath, jobs[2].OutputPath });

        Assert.Equal(
            new[] { Path.GetFullPath(jobs[0].SourcePath) },
            removable);
    }

    [Fact]
    public void CollectSourceDirectories_uniqueParents()
    {
        string root = Path.GetFullPath(@"C:\ds");
        IReadOnlyList<string> dirs = PreBucketMath.CollectSourceDirectories(new[]
        {
            Path.Combine(root, "10_char", "a.png"),
            Path.Combine(root, "10_char", "b.png"),
            Path.Combine(root, "other", "c.png")
        });

        Assert.Equal(2, dirs.Count);
        Assert.Contains(Path.Combine(root, "10_char"), dirs);
        Assert.Contains(Path.Combine(root, "other"), dirs);
    }
}

public sealed class PreBucketServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "bdtm-prebucket-" + Guid.NewGuid().ToString("N"));

    public PreBucketServiceTests()
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
    public void TryWrite_letterboxesToExactBucketAndClonesCaption()
    {
        string source = Path.Combine(root, "wide.png");
        WritePng(source, 200, 100, new Rgba32(200, 30, 30, 255));
        File.WriteAllText(Path.Combine(root, "wide.txt"), "1girl, solo");

        var job = new PreBucketJob
        {
            SourcePath = source,
            SourceSize = new Size(200, 100),
            BucketSize = new Size(256, 256),
            FittedSize = new Size(256, 128),
            DrawOffset = new Point(0, 64)
        };
        PreBucketService.AssignOutputPaths(new[] { job }, root);

        string written = PreBucketService.TryWrite(job, new[] { "txt" });

        Assert.Equal(job.OutputPath, written);
        Assert.True(File.Exists(written));
        Assert.True(File.Exists(source));
        ImageInfo info = SixLabors.ImageSharp.Image.Identify(written);
        Assert.Equal(256, info.Width);
        Assert.Equal(256, info.Height);
        Assert.Equal("256x256", Path.GetFileName(Path.GetDirectoryName(written)));
        string caption = Path.Combine(Path.GetDirectoryName(written)!, "wide.txt");
        Assert.Equal("1girl, solo", File.ReadAllText(caption));

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(written);
        Assert.Equal(255, image[8, 8].R);
        Assert.Equal(255, image[8, 8].G);
        Assert.True(image[128, 128].R > 150);
    }

    [Fact]
    public void DeleteEmptyFoldersUnderRoot_deletesEmptyChildButNotRoot()
    {
        string oldDir = Directory.CreateDirectory(Path.Combine(root, "10_char")).FullName;
        string stayDir = Directory.CreateDirectory(Path.Combine(root, "keep")).FullName;
        string oldImage = Path.Combine(oldDir, "one.png");
        string stayImage = Path.Combine(stayDir, "two.png");
        WritePng(oldImage, 8, 8, new Rgba32(10, 10, 10, 255));
        WritePng(stayImage, 8, 8, new Rgba32(10, 10, 10, 255));
        File.WriteAllText(Path.ChangeExtension(oldImage, ".txt"), "solo");
        File.WriteAllText(Path.ChangeExtension(stayImage, ".txt"), "solo");

        using var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(root, loadPreviewImages: false, readMetadata: false));

        File.Delete(oldImage);
        File.Delete(Path.ChangeExtension(oldImage, ".txt"));
        manager.RemoveMany(new[] { oldImage });
        manager.DeleteEmptyFoldersUnderRoot(new[] { oldDir, root });

        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(root));
        Assert.True(Directory.Exists(stayDir));
    }

    private static void WritePng(string path, int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.SaveAsPng(path);
    }
}
