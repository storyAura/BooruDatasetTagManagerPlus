using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class LocalizationAndImageLoaderTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("ru-RU")]
    [InlineData("pt-BR")]
    public void TranslationContainsEveryEnglishKey(string language)
    {
        var en = LoadLanguage("en-US");
        var translation = LoadLanguage(language);

        var missing = en.Keys.Except(translation.Keys).OrderBy(x => x).ToArray();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("GridImage")]
    [InlineData("GridName")]
    [InlineData("BtnAutoGetTagsDefSet")]
    [InlineData("FormLoadingSettings")]
    [InlineData("MenuTools")]
    [InlineData("TipLoadingComplete")]
    [InlineData("LlmT2NlInvalidSettings")]
    [InlineData("LlmT2NlProgressTitle")]
    [InlineData("HuggingFace")]
    [InlineData("HfMirror")]
    [InlineData("MenuOnnxTagger")]
    [InlineData("MenuContextDSRetagOnnx")]
    [InlineData("TaggerModelCorrupt")]
    public void ChineseLanguageFileLocalizesCriticalUiText(string key)
    {
        var en = LoadLanguage("en-US");
        var zh = LoadLanguage("zh-CN");

        Assert.True(en.ContainsKey(key), $"Missing en-US key: {key}");
        Assert.True(zh.ContainsKey(key), $"Missing zh-CN key: {key}");
        Assert.NotEqual(en[key], zh[key]);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("webp")]
    public void MakeThumbLoadsStaticImageFormats(string extension)
    {
        using var temp = new TemporaryDirectory();
        string imagePath = Path.Combine(temp.Path, $"sample.{extension}");
        SaveSampleImage(imagePath);

        using var thumb = ImageLoader.MakeThumb(imagePath, 32);

        Assert.NotNull(thumb);
        Assert.True(thumb.Width <= 32);
        Assert.True(thumb.Height <= 32);
    }

    [Fact]
    public void MakeThumbReturnsNullForInvalidImage()
    {
        using var temp = new TemporaryDirectory();
        string imagePath = Path.Combine(temp.Path, "not-an-image.webp");
        File.WriteAllText(imagePath, "not image data");

        using var thumb = ImageLoader.MakeThumb(imagePath, 32);

        Assert.Null(thumb);
    }

    [Fact]
    public void LoadPreviewDoesNotUpscaleImageBelowMaximumDimension()
    {
        using var temp = new TemporaryDirectory();
        string imagePath = Path.Combine(temp.Path, "small.png");
        SaveSampleImage(imagePath, 80, 40);

        using var preview = ImageLoader.LoadPreview(imagePath, 2048);

        Assert.NotNull(preview);
        Assert.Equal(80, preview.Width);
        Assert.Equal(40, preview.Height);
    }

    [Theory]
    [InlineData(320, 160, 128, 128, 64)]
    [InlineData(160, 320, 128, 64, 128)]
    public void LoadPreviewLimitsMaximumDimensionAndPreservesAspectRatio(
        int width,
        int height,
        int maximumDimension,
        int expectedWidth,
        int expectedHeight)
    {
        using var temp = new TemporaryDirectory();
        string imagePath = Path.Combine(temp.Path, "large.png");
        SaveSampleImage(imagePath, width, height);

        using var preview = ImageLoader.LoadPreview(imagePath, maximumDimension);

        Assert.NotNull(preview);
        Assert.Equal(expectedWidth, preview.Width);
        Assert.Equal(expectedHeight, preview.Height);
    }

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "BooruDatasetTagManager", "Languages", $"{language}.txt");
        return File.ReadAllLines(path)
            .Select(line => new { Line = line, Separator = line.IndexOf('=') })
            .Where(item => item.Separator >= 0)
            .ToDictionary(
                item => item.Line.Substring(0, item.Separator).Trim(),
                item => item.Line.Substring(item.Separator + 1));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "BooruDatasetTagManager", "Languages", "en-US.txt");
            if (File.Exists(candidate))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static void SaveSampleImage(string path, int width = 80, int height = 40)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(30, 120, 220, 255));
        image.Save(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-image-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
