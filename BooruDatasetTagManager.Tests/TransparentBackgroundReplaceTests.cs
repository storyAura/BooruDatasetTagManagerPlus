using System.Drawing;
using System.Windows.Forms;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using DrawingColor = System.Drawing.Color;

namespace BooruDatasetTagManager.Tests;

public sealed class TransparentBackgroundReplaceTests : IDisposable
{
    private readonly string tempDir = Path.Combine(
        Path.GetTempPath(), "BDTM-transp-replace-" + Guid.NewGuid().ToString("N"));

    public TransparentBackgroundReplaceTests()
    {
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

    [Fact]
    public void Flatten_fills_transparent_pixels_and_keeps_opaque_ones()
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(10, 20, 30, 255));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                image[x, y] = new Rgba32(0, 0, 0, 0);

        TransparentBackgroundReplacer.Flatten(image, DrawingColor.FromArgb(255, 20, 180, 40));

        Assert.Equal(new Rgba32(20, 180, 40, 255), image[1, 1]);
        Assert.Equal(new Rgba32(10, 20, 30, 255), image[6, 6]);
    }

    [Fact]
    public void Flatten_composites_semi_transparent_pixels()
    {
        using var image = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 255, 128));

        TransparentBackgroundReplacer.Flatten(image, DrawingColor.FromArgb(255, 255, 0, 0));

        Rgba32 pixel = image[0, 0];
        Assert.Equal(255, pixel.A);
        Assert.True(pixel.R > 100);
        Assert.Equal(0, pixel.G);
        Assert.True(pixel.B > 100);
    }

    [Fact]
    public void Replace_png_round_trip_removes_transparency()
    {
        string path = WriteTransparent("alpha.png", asWebp: false);
        Assert.True(TransparentImageScanner.HasTransparentPixels(path));

        byte[] encoded = TransparentBackgroundReplacer.Replace(path, DrawingColor.FromArgb(255, 255, 0, 0));
        File.WriteAllBytes(path, encoded);

        Assert.False(TransparentImageScanner.HasTransparentPixels(path));
        using var check = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        Assert.Equal(new Rgba32(255, 0, 0, 255), check[1, 1]);
        Assert.Equal(10, check[6, 6].R);
    }

    [Fact]
    public void Replace_webp_round_trip_removes_transparency()
    {
        string path = WriteTransparent("alpha.webp", asWebp: true);
        Assert.True(TransparentImageScanner.HasTransparentPixels(path));

        byte[] encoded = TransparentBackgroundReplacer.Replace(path, DrawingColor.White);
        File.WriteAllBytes(path, encoded);

        Assert.False(TransparentImageScanner.HasTransparentPixels(path));
        using var check = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        Assert.Equal(255, check[1, 1].A);
        Assert.Equal(255, check[1, 1].R);
    }

    [Fact]
    public void Tools_category_header_is_a_label_so_it_cannot_steal_menu_clicks()
    {
        using var header = new ToolsCategoryHeaderItem("menuToolsProcessingHeader", DrawingColor.SteelBlue);
        Assert.IsAssignableFrom<ToolStripLabel>(header);
        Assert.Equal(typeof(ToolStripLabel), header.GetType().BaseType);
        Assert.False(header.Enabled);
    }

    private string WriteTransparent(string name, bool asWebp)
    {
        string path = Path.Combine(tempDir, name);
        using var image = new Image<Rgba32>(8, 8, new Rgba32(10, 20, 30, 255));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                image[x, y] = new Rgba32(0, 0, 0, 0);
        if (asWebp)
        {
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
