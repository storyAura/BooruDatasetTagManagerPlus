using System.Drawing;
using BooruDatasetTagManager;
using Xunit;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager.Tests;

public sealed class YoloDetectionMathTests
{
    [Fact]
    public void Letterbox_pads_landscape_to_square()
    {
        YoloLetterbox map = YoloDetectionMath.ComputeLetterbox(1920, 1080, 640);

        Assert.Equal(640, map.InputSize);
        Assert.Equal(640, map.NewWidth);
        Assert.Equal(360, map.NewHeight);
        Assert.Equal(0, map.PadX);
        Assert.Equal(140, map.PadY);
    }

    [Fact]
    public void MapLetterboxBoxToImage_undoes_padding()
    {
        YoloLetterbox map = YoloDetectionMath.ComputeLetterbox(1920, 1080, 640);
        Rectangle box = YoloDetectionMath.MapLetterboxBoxToImage(0, 140, 640, 500, map, new Size(1920, 1080));

        Assert.InRange(box.X, 0, 1920);
        Assert.InRange(box.Y, 0, 1080);
        Assert.True(box.Width > 0);
        Assert.True(box.Height > 0);
        Assert.True(box.Right <= 1920);
        Assert.True(box.Bottom <= 1080);
    }

    [Theory]
    [InlineData(1920, 1080, 16, 9)]
    [InlineData(1080, 1920, 9, 16)]
    [InlineData(1024, 1024, 1, 1)]
    [InlineData(1200, 900, 4, 3)]
    public void NearestAspectPreset_picks_the_closest_listed_ratio(
        int width, int height, int expectedW, int expectedH)
    {
        (int presetW, int presetH) = YoloDetectionMath.NearestAspectPreset(width, height);
        Assert.Equal(expectedW, presetW);
        Assert.Equal(expectedH, presetH);
    }

    [Fact]
    public void NearestAspectPreset_invalid_size_falls_back_to_first_preset()
    {
        (int width, int height) = YoloDetectionMath.NearestAspectPreset(0, 10);
        Assert.Equal(BatchCropMath.Presets[0], (width, height));
    }

    [Fact]
    public void NearestAspectPreset_equal_distance_keeps_the_earlier_preset()
    {
        // Midpoint between 2:1 (index 2) and 16:9. Equal delta keeps 2:1.
        (int width, int height) = YoloDetectionMath.NearestAspectPreset(1700, 900);
        Assert.Equal((2, 1), (width, height));
    }

    [Fact]
    public void ExpandToAspect_grows_bbox_to_square_around_the_person()
    {
        var image = new Size(1000, 1000);
        var person = new Rectangle(400, 300, 100, 200);

        Rectangle crop = YoloDetectionMath.ExpandToAspect(person, image, 1, 1);

        Assert.Equal(200, crop.Width);
        Assert.Equal(200, crop.Height);
        Assert.InRange(crop.X, 0, 800);
        Assert.True(crop.Contains(person) || Rectangle.Intersect(crop, person) == person);
    }

    [Fact]
    public void ExpandToAspect_shifts_then_shrinks_when_the_square_does_not_fit()
    {
        var image = new Size(300, 1000);
        var person = new Rectangle(0, 0, 300, 800);

        Rectangle crop = YoloDetectionMath.ExpandToAspect(person, image, 1, 1);

        Assert.Equal(300, crop.Width);
        Assert.Equal(300, crop.Height);
        Assert.InRange(crop.Y, 0, 700);
        Assert.True(crop.Right <= image.Width);
        Assert.True(crop.Bottom <= image.Height);
    }

    [Fact]
    public void NonMaxSuppression_drops_the_lower_overlap()
    {
        var boxes = new List<(Rectangle Box, float Score)>
        {
            (new Rectangle(0, 0, 100, 100), 0.9f),
            (new Rectangle(10, 10, 100, 100), 0.5f),
            (new Rectangle(200, 200, 50, 50), 0.8f)
        };

        List<(Rectangle Box, float Score)> keep = YoloDetectionMath.NonMaxSuppression(boxes, 0.3f);

        Assert.Equal(2, keep.Count);
        Assert.Equal(0.9f, keep[0].Score);
        Assert.Equal(0.8f, keep[1].Score);
    }

    [Fact]
    public void ParseYoloOutput_reads_channels_first_layout()
    {
        // One detection: cx=320, cy=320, w=100, h=100, score=0.9 in [1,5,1]
        float[] data = { 320f, 320f, 100f, 100f, 0.9f };
        var map = new YoloLetterbox
        {
            InputSize = 640,
            Scale = 1f,
            PadX = 0,
            PadY = 0,
            NewWidth = 640,
            NewHeight = 640
        };

        List<(Rectangle Box, float Score)> boxes = YoloDetectionMath.ParseYoloOutput(
            data,
            new[] { 1, 5, 1 },
            0.3f,
            map,
            new Size(640, 640));

        Assert.Single(boxes);
        Assert.Equal(0.9f, boxes[0].Score, 3);
        Assert.InRange(boxes[0].Box.Width, 90, 110);
        Assert.InRange(boxes[0].Box.Height, 90, 110);
    }

    [Fact]
    public void ParseYoloOutput_reads_channels_last_layout()
    {
        float[] data = { 320f, 320f, 80f, 80f, 0.7f };
        var map = new YoloLetterbox
        {
            InputSize = 640,
            Scale = 1f,
            NewWidth = 640,
            NewHeight = 640
        };

        List<(Rectangle Box, float Score)> boxes = YoloDetectionMath.ParseYoloOutput(
            data,
            new[] { 1, 1, 5 },
            0.3f,
            map,
            new Size(640, 640));

        Assert.Single(boxes);
        Assert.Equal(0.7f, boxes[0].Score, 3);
    }

    [Fact]
    public void ParseYoloOutput_filters_by_confidence()
    {
        float[] data = { 320f, 320f, 40f, 40f, 0.1f };
        List<(Rectangle Box, float Score)> boxes = YoloDetectionMath.ParseYoloOutput(
            data,
            new[] { 1, 5, 1 },
            0.3f,
            new YoloLetterbox { InputSize = 640, Scale = 1f, NewWidth = 640, NewHeight = 640 },
            new Size(640, 640));

        Assert.Empty(boxes);
    }
}

public sealed class YoloPersonDetectorServiceTests : IDisposable
{
    private readonly string previousAppPath = Program.AppPath;
    private readonly string root = Path.Combine(Path.GetTempPath(), "bdtm-yolo-" + Guid.NewGuid().ToString("N"));

    public YoloPersonDetectorServiceTests()
    {
        Directory.CreateDirectory(root);
        Program.AppPath = root;
    }

    public void Dispose()
    {
        Program.AppPath = previousAppPath;
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Default_model_path_stays_under_Models()
    {
        string path = HuggingFaceModelDownloader.GetLocalPath(
            YoloPersonDetectorService.DefaultRepo,
            YoloPersonDetectorService.DefaultFileName);

        string models = Path.Combine(root, "Models") + Path.DirectorySeparatorChar;
        Assert.StartsWith(models, path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deepghs", path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("person_detect_v1.1_s", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("model.onnx", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetLocalPath_rejects_parent_segments_that_escape_Models()
    {
        Assert.Throws<InvalidOperationException>(() =>
            HuggingFaceModelDownloader.GetLocalPath("yolo-import", Path.Combine("..", "..", "secret.onnx")));
    }

    [Fact]
    public void ImportOnnx_copies_into_yolo_import_under_Models()
    {
        string source = Path.Combine(root, "custom.onnx");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });

        string dest = YoloPersonDetectorService.ImportOnnx(source);

        Assert.True(File.Exists(dest));
        string models = Path.Combine(root, "Models") + Path.DirectorySeparatorChar;
        Assert.StartsWith(models, dest, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yolo-import", dest, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("custom.onnx", Path.GetFileName(dest));
    }
}

public sealed class YoloDetectorCatalogTests
{
    [Fact]
    public void Catalog_lists_person_face_head_and_import()
    {
        Assert.Equal(12, YoloDetectorCatalog.AllModels.Count);
        Assert.Equal(5, YoloDetectorCatalog.AllModels.Count(model => model.Kind == YoloDetectorKind.Person));
        Assert.Equal(3, YoloDetectorCatalog.AllModels.Count(model => model.Kind == YoloDetectorKind.Face));
        Assert.Equal(3, YoloDetectorCatalog.AllModels.Count(model => model.Kind == YoloDetectorKind.Head));
        Assert.Single(YoloDetectorCatalog.AllModels, model => model.Kind == YoloDetectorKind.Import);
        Assert.Equal(YoloDetectorCatalog.DefaultId, YoloPersonDetectorService.DefaultModelId);
        Assert.Equal("[Person] v1.1 small", YoloDetectorCatalog.Default.DisplayName);
    }

    [Fact]
    public void GetById_falls_back_to_default_person_v11_small()
    {
        YoloDetectorModelEntry face = YoloDetectorCatalog.GetById("deepghs:face_detect_v1.4_s");
        Assert.Equal(YoloDetectorKind.Face, face.Kind);
        Assert.Equal("deepghs/anime_face_detection", face.Repo);
        Assert.Equal("face_detect_v1.4_s/model.onnx", face.FileName);

        YoloDetectorModelEntry missing = YoloDetectorCatalog.GetById("missing-model-id");
        Assert.Equal(YoloDetectorCatalog.DefaultId, missing.Id);

        YoloDetectorModelEntry empty = YoloDetectorCatalog.GetById(null);
        Assert.Equal(YoloDetectorCatalog.DefaultId, empty.Id);
    }

    [Fact]
    public void Nested_catalog_paths_stay_under_Models()
    {
        string previous = Program.AppPath;
        string root = Path.Combine(Path.GetTempPath(), "bdtm-yolo-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Program.AppPath = root;
            YoloDetectorModelEntry head = YoloDetectorCatalog.GetById("deepghs:head_detect_v2.0_s");
            string path = YoloDetectorCatalog.GetLocalPath(head);

            string models = Path.Combine(root, "Models") + Path.DirectorySeparatorChar;
            Assert.StartsWith(models, path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("anime_head_detection", path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("head_detect_v2.0_s", path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("model.onnx", path, StringComparison.OrdinalIgnoreCase);
            Assert.Same(head, YoloDetectorCatalog.FindByLocalPath(path));
        }
        finally
        {
            Program.AppPath = previous;
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ResolveInitial_uses_saved_id_then_import_then_default()
    {
        string previous = Program.AppPath;
        string root = Path.Combine(Path.GetTempPath(), "bdtm-yolo-init-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Program.AppPath = root;
            string import = Path.Combine(root, "imported.onnx");
            File.WriteAllBytes(import, new byte[] { 1, 2, 3, 4 });

            YoloDetectorModelEntry saved = YoloDetectorCatalog.ResolveInitial(
                "deepghs:face_detect_v1.4_n",
                import);
            Assert.Equal("deepghs:face_detect_v1.4_n", saved.Id);

            YoloDetectorModelEntry legacy = YoloDetectorCatalog.ResolveInitial(null, import);
            Assert.Equal(YoloDetectorCatalog.ImportId, legacy.Id);

            YoloDetectorModelEntry unset = YoloDetectorCatalog.ResolveInitial("  ", null);
            Assert.Equal(YoloDetectorCatalog.DefaultId, unset.Id);
        }
        finally
        {
            Program.AppPath = previous;
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ResolveModelPath_uses_import_only_for_import_entry()
    {
        string previous = Program.AppPath;
        string root = Path.Combine(Path.GetTempPath(), "bdtm-yolo-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Program.AppPath = root;
            string import = Path.Combine(root, "custom.onnx");
            File.WriteAllBytes(import, new byte[] { 1, 2, 3, 4 });
            using var service = new YoloPersonDetectorService();

            string catalogPath = service.ResolveModelPath(YoloDetectorCatalog.Default, import);
            Assert.Contains("person_detect_v1.1_s", catalogPath, StringComparison.OrdinalIgnoreCase);

            string importPath = service.ResolveModelPath(
                YoloDetectorCatalog.GetById(YoloDetectorCatalog.ImportId),
                import);
            Assert.Equal(Path.GetFullPath(import), importPath);
            Assert.True(service.IsModelReady(YoloDetectorCatalog.GetById(YoloDetectorCatalog.ImportId), import));
            Assert.False(service.IsModelReady(YoloDetectorCatalog.Default, importPath: null));
        }
        finally
        {
            Program.AppPath = previous;
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
            }
        }
    }
}
