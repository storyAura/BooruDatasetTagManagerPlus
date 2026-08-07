using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CorruptedImageScannerTests : IDisposable
{
    private readonly string tempDir;

    public CorruptedImageScannerTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "BDTM-corrupt-" + Guid.NewGuid().ToString("N"));
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
    public void Inspect_returns_missing_for_absent_file()
    {
        CorruptedImageFinding finding = CorruptedImageScanner.Inspect(Path.Combine(tempDir, "nope.png"));
        Assert.NotNull(finding);
        Assert.Equal(CorruptedImageScanner.ReasonMissing, finding!.ReasonCode);
    }

    [Fact]
    public void Inspect_returns_empty_for_zero_byte_file()
    {
        string path = Path.Combine(tempDir, "empty.png");
        File.WriteAllBytes(path, Array.Empty<byte>());

        CorruptedImageFinding finding = CorruptedImageScanner.Inspect(path);

        Assert.NotNull(finding);
        Assert.Equal(CorruptedImageScanner.ReasonEmpty, finding!.ReasonCode);
    }

    [Fact]
    public void Inspect_returns_decode_for_garbage_bytes()
    {
        string path = Path.Combine(tempDir, "garbage.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        CorruptedImageFinding finding = CorruptedImageScanner.Inspect(path);

        Assert.NotNull(finding);
        Assert.Equal(CorruptedImageScanner.ReasonDecode, finding!.ReasonCode);
    }

    [Fact]
    public void Inspect_returns_null_for_valid_png()
    {
        string path = Path.Combine(tempDir, "ok.png");
        using (var image = new Image<Rgba32>(8, 8))
            image.SaveAsPng(path);

        Assert.Null(CorruptedImageScanner.Inspect(path));
    }

    [Fact]
    public void Scan_collects_only_corrupted_paths_in_order()
    {
        string good = Path.Combine(tempDir, "good.png");
        using (var image = new Image<Rgba32>(4, 4))
            image.SaveAsPng(good);
        string bad = Path.Combine(tempDir, "bad.jpg");
        File.WriteAllText(bad, "not an image");
        string missing = Path.Combine(tempDir, "gone.webp");

        List<CorruptedImageFinding> findings = CorruptedImageScanner.Scan(new[] { good, bad, missing });

        Assert.Equal(2, findings.Count);
        Assert.Equal(bad, findings[0].Path);
        Assert.Equal(CorruptedImageScanner.ReasonDecode, findings[0].ReasonCode);
        Assert.Equal(missing, findings[1].Path);
        Assert.Equal(CorruptedImageScanner.ReasonMissing, findings[1].ReasonCode);
    }
}
