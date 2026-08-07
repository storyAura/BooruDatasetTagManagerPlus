using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

/// <summary>
/// The "replace transparent background" batches run this pre-pass first, so an
/// opaque png/jpg folder is never mass-overwritten.
/// </summary>
public sealed class TransparentImageScannerTests : IDisposable
{
    private readonly string tempDir;

    public TransparentImageScannerTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-transp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.jpg", false)]
    [InlineData("a.jpeg", false)]
    [InlineData("a.bmp", false)]
    [InlineData("a.mp4", false)]
    public void SupportsAlpha_follows_the_file_format(string name, bool expected)
    {
        Assert.Equal(expected, TransparentImageScanner.SupportsAlpha(Path.Combine(tempDir, name)));
    }

    [Fact]
    public void Opaque_png_has_no_transparent_pixels()
    {
        string path = WriteImage("opaque.png", new Rgba32(10, 20, 30, 255), null, asWebp: false);
        Assert.False(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Png_with_transparent_area_is_detected()
    {
        string path = WriteImage("alpha.png", new Rgba32(10, 20, 30, 255), new Rgba32(0, 0, 0, 0), asWebp: false);
        Assert.True(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Semi_transparent_png_is_detected()
    {
        string path = WriteImage("semi.png", new Rgba32(10, 20, 30, 255), new Rgba32(10, 20, 30, 128), asWebp: false);
        Assert.True(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Webp_with_transparency_is_detected()
    {
        string path = WriteImage("alpha.webp", new Rgba32(200, 100, 50, 255), new Rgba32(0, 0, 0, 0), asWebp: true);
        Assert.True(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Opaque_webp_has_no_transparent_pixels()
    {
        string path = WriteImage("opaque.webp", new Rgba32(200, 100, 50, 255), null, asWebp: true);
        Assert.False(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Jpeg_is_never_a_candidate_even_though_it_decodes()
    {
        string path = Path.Combine(tempDir, "photo.jpg");
        using (var image = new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255)))
            image.SaveAsJpeg(path);

        Assert.False(TransparentImageScanner.HasTransparentPixels(path));
    }

    [Fact]
    public void Missing_and_corrupted_files_are_not_candidates()
    {
        string missing = Path.Combine(tempDir, "gone.png");
        string garbage = Path.Combine(tempDir, "garbage.png");
        File.WriteAllBytes(garbage, new byte[] { 1, 2, 3, 4 });

        Assert.False(TransparentImageScanner.HasTransparentPixels(missing));
        Assert.False(TransparentImageScanner.HasTransparentPixels(garbage));
    }

    [Fact]
    public void FindTransparent_keeps_input_order_and_reports_progress()
    {
        string opaque = WriteImage("1-opaque.png", new Rgba32(9, 9, 9, 255), null, asWebp: false);
        string alpha1 = WriteImage("2-alpha.png", new Rgba32(9, 9, 9, 255), new Rgba32(0, 0, 0, 0), asWebp: false);
        string jpeg = Path.Combine(tempDir, "3-photo.jpg");
        using (var image = new Image<Rgba32>(2, 2, new Rgba32(1, 1, 1, 255)))
            image.SaveAsJpeg(jpeg);
        string alpha2 = WriteImage("4-alpha.webp", new Rgba32(9, 9, 9, 255), new Rgba32(0, 0, 0, 0), asWebp: true);

        var inspected = new List<int>();
        var progress = new Progress<int>(inspected.Add);
        List<string> found = TransparentImageScanner.FindTransparent(
            new[] { opaque, alpha1, jpeg, alpha2 }, progress);

        Assert.Equal(new[] { alpha1, alpha2 }, found);
    }

    private string WriteImage(string name, Rgba32 background, Rgba32? transparentArea, bool asWebp)
    {
        string path = Path.Combine(tempDir, name);
        using var image = new Image<Rgba32>(8, 8, background);
        if (transparentArea.HasValue)
        {
            // A real transparent background is a region, not one pixel; a block
            // also survives webp's alpha handling deterministically.
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    image[x, y] = transparentArea.Value;
        }
        if (asWebp)
        {
            // Lossless: the lossy encoder may quantize a small alpha area away,
            // which would make the fixture (not the scanner) the flaky part.
            image.SaveAsWebp(path, new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
            {
                FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossless
            });
        }
        else
        {
            image.SaveAsPng(path);
        }
        return path;
    }
}
