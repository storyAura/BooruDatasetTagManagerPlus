using System.Drawing;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager.Tests;

public sealed class BatchCropMathTests
{
    [Fact]
    public void FromDrag_free_uses_the_raw_rectangle()
    {
        Rectangle rect = BatchCropMath.FromDrag(new Point(10, 20), new Point(110, 80), new Size(200, 200), BatchCropAspect.Free);

        Assert.Equal(new Rectangle(10, 20, 100, 60), rect);
    }

    [Fact]
    public void FromDrag_square_locks_1_to_1()
    {
        Rectangle rect = BatchCropMath.FromDrag(new Point(0, 0), new Point(80, 20), new Size(200, 200), BatchCropAspect.Preset(1, 1));

        Assert.Equal(80, rect.Width);
        Assert.Equal(80, rect.Height);
        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
    }

    [Fact]
    public void FromDrag_up_left_keeps_the_start_as_the_anchor()
    {
        Rectangle rect = BatchCropMath.FromDrag(new Point(100, 100), new Point(20, 20), new Size(200, 200), BatchCropAspect.Preset(1, 1));

        Assert.Equal(new Rectangle(20, 20, 80, 80), rect);
    }

    [Fact]
    public void FromDrag_clamps_a_square_that_would_leave_the_image()
    {
        Rectangle rect = BatchCropMath.FromDrag(new Point(150, 0), new Point(250, 100), new Size(200, 200), BatchCropAspect.Preset(1, 1));

        Assert.True(rect.Right <= 200);
        Assert.True(rect.Bottom <= 200);
        Assert.Equal(rect.Width, rect.Height);
        Assert.Equal(50, rect.Width);
    }

    [Fact]
    public void ApplyAspect_full_image_to_square_centers_the_largest_fit()
    {
        Rectangle rect = BatchCropMath.ApplyAspect(new Rectangle(0, 0, 100, 200), new Size(100, 200), BatchCropAspect.Preset(1, 1));

        Assert.Equal(new Rectangle(0, 50, 100, 100), rect);
    }

    [Fact]
    public void ApplyAspect_original_keeps_the_image_ratio()
    {
        var aspect = BatchCropAspect.Original(100, 200);
        Rectangle rect = BatchCropMath.ApplyAspect(new Rectangle(10, 10, 40, 40), new Size(100, 200), aspect);

        Assert.Equal(2, rect.Height / rect.Width);
    }

    [Fact]
    public void Move_stays_inside_the_image()
    {
        Rectangle moved = BatchCropMath.Move(new Rectangle(10, 10, 40, 40), 1000, -5, new Size(100, 100));

        Assert.Equal(new Rectangle(60, 5, 40, 40), moved);
    }

    [Fact]
    public void Place_shrinks_when_the_rect_is_larger_than_the_image()
    {
        Rectangle placed = BatchCropMath.Place(new Rectangle(-10, -10, 500, 500), new Size(80, 60));

        Assert.Equal(new Rectangle(0, 0, 80, 60), placed);
    }

    [Fact]
    public void SetSize_with_aspect_updates_the_other_side()
    {
        Rectangle rect = BatchCropMath.SetSize(new Rectangle(0, 0, 50, 50), 80, 10, new Size(200, 200), BatchCropAspect.Preset(2, 1));

        Assert.Equal(80, rect.Width);
        Assert.Equal(40, rect.Height);
    }

    [Fact]
    public void FilterSameSize_keeps_only_matching_non_video_paths()
    {
        var items = new (string Path, Size? Size)[]
        {
            (@"C:\a.png", new Size(512, 768)),
            (@"C:\b.jpg", new Size(512, 768)),
            (@"C:\c.png", new Size(1024, 1024)),
            (@"C:\d.mp4", new Size(512, 768)),
            (@"C:\e.png", null),
        };

        IReadOnlyList<string> kept = BatchCropMath.FilterSameSize(items, new Size(512, 768));

        Assert.Equal(new[] { @"C:\a.png", @"C:\b.jpg" }, kept);
    }
}

public sealed class BatchCropServiceTests
{
    [Fact]
    public void GetCropCopyPath_uses_crop_suffix_and_avoids_collisions()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "char.png");
            File.WriteAllText(imagePath, "x");
            File.WriteAllText(Path.Combine(root, "char_crop.png"), "x");

            string copy = BatchCropService.GetCropCopyPath(imagePath);

            Assert.Equal(Path.Combine(root, "char_crop2.png"), copy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TryGetImageSize_reads_png_header()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "src.png");
            using (var image = new Image<Rgba32>(120, 80))
                image.Save(imagePath, new PngEncoder());

            Assert.Equal(new Size(120, 80), BatchCropService.TryGetImageSize(imagePath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Overwrite_writes_the_cropped_pixels_atomically()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "src.png");
            using (var image = new Image<Rgba32>(40, 30))
            {
                image[2, 3] = new Rgba32(255, 0, 0, 255);
                image.Save(imagePath, new PngEncoder());
            }

            Assert.True(BatchCropService.TryOverwrite(imagePath, new Size(40, 30), new Rectangle(1, 2, 10, 8)));

            using var cropped = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
            Assert.Equal(10, cropped.Width);
            Assert.Equal(8, cropped.Height);
            Assert.Equal(255, cropped[1, 1].R);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SaveCopy_writes_beside_the_source_and_leaves_the_original()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "src.png");
            using (var image = new Image<Rgba32>(20, 20))
                image.Save(imagePath, new PngEncoder());

            string copy = BatchCropService.TrySaveCopy(imagePath, new Size(20, 20), new Rectangle(0, 0, 8, 8));

            Assert.Equal(Path.Combine(root, "src_crop.png"), copy);
            Assert.True(File.Exists(imagePath));
            using var cropped = SixLabors.ImageSharp.Image.Load<Rgba32>(copy);
            Assert.Equal(8, cropped.Width);
            Assert.Equal(8, cropped.Height);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Overwrite_skips_when_loaded_size_does_not_match()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "src.png");
            using (var image = new Image<Rgba32>(20, 20))
                image.Save(imagePath, new PngEncoder());

            Assert.False(BatchCropService.TryOverwrite(imagePath, new Size(40, 40), new Rectangle(0, 0, 10, 10)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Overwrite_full_image_does_not_throw_on_gdi_clone()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-batch-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "src.png");
            using (var image = new Image<Rgba32>(32, 24))
            {
                image[0, 0] = new Rgba32(10, 20, 30, 255);
                image[31, 23] = new Rgba32(40, 50, 60, 255);
                image.Save(imagePath, new PngEncoder());
            }

            Assert.True(BatchCropService.TryOverwrite(imagePath, new Size(32, 24), new Rectangle(0, 0, 32, 24)));

            using var cropped = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
            Assert.Equal(32, cropped.Width);
            Assert.Equal(24, cropped.Height);
            Assert.Equal(10, cropped[0, 0].R);
            Assert.Equal(60, cropped[31, 23].B);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CropBitmap_edge_rect_stays_inside_the_source()
    {
        using var source = new System.Drawing.Bitmap(40, 30, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Bitmap cropped = BatchCropService.CropBitmap(source, new Rectangle(32, 22, 8, 8));

        Assert.NotNull(cropped);
        Assert.Equal(8, cropped.Width);
        Assert.Equal(8, cropped.Height);
    }

    [Fact]
    public void Place_never_produces_a_rect_outside_the_image()
    {
        Rectangle placed = BatchCropMath.Place(new Rectangle(5, 5, 100, 100), new Size(40, 30));

        Assert.True(placed.X >= 0);
        Assert.True(placed.Y >= 0);
        Assert.True(placed.Right <= 40);
        Assert.True(placed.Bottom <= 30);
    }
}
