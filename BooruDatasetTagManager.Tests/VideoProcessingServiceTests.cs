using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class VideoProcessingServiceTests
{
    [Theory]
    [InlineData(".mp4", true)]
    [InlineData(".MKV", true)]
    [InlineData(".webm", true)]
    [InlineData(".jpg", false)]
    public void IsVideoFile_detects_supported_extensions(string extension, bool expected)
    {
        bool actual = VideoProcessingService.IsVideoFile("sample" + extension);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("30000/1001", 29.97)]
    [InlineData("24/1", 24)]
    [InlineData("30", 30)]
    [InlineData("0/0", 0)]
    public void ParseFpsString_parses_fraction_and_decimal_values(string input, double expected)
    {
        double actual = VideoProcessingService.ParseFpsString(input);
        Assert.Equal(expected, actual, 2);
    }

    [Fact]
    public void ParseFrameSelection_accepts_frame_numbers_and_timestamps()
    {
        ParsedFrameSelection selection = VideoProcessingService.ParseFrameSelection("0, 30, 00:00:01, 00:00:05.500");

        Assert.Empty(selection.InvalidTokens);
        Assert.Equal(new[] { 0, 30 }, selection.FrameNumbers);
        Assert.Equal(2, selection.Timestamps.Count);
        Assert.True(selection.IsValid);
    }

    [Fact]
    public void ParseFrameSelection_reports_invalid_tokens()
    {
        ParsedFrameSelection selection = VideoProcessingService.ParseFrameSelection("abc, 10");

        Assert.Single(selection.InvalidTokens);
        Assert.Equal("abc", selection.InvalidTokens[0]);
        Assert.Single(selection.FrameNumbers);
    }

    [Fact]
    public void FfmpegLocator_prefers_bundled_path_when_present()
    {
        string appDir = AppContext.BaseDirectory;
        var locator = new FfmpegLocator(appDir, string.Empty);

        string bundled = Path.Combine(appDir, "ThirdParty", "ffmpeg", "win-x64", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            Assert.Equal(bundled, locator.FfmpegExe);
            Assert.True(locator.IsAvailable);
        }
        else
        {
            Assert.EndsWith("ffmpeg.exe", locator.FfmpegExe, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("frame=  123 fps= 24 q=-0.0 size=    1024kB time=00:00:05.00 bitrate=1677.7kbits/s speed=1.02x", "frame=123")]
    [InlineData("Press [q] to stop, [?] for help", "")]
    public void FilterProgressLine_keeps_useful_progress_and_drops_prompts(string input, string expected)
    {
        Assert.Equal(expected, VideoProcessingService.FilterProgressLine(input));
    }

    [Fact]
    public void GetConvertOutputPath_uses_suffix_when_not_replacing()
    {
        var service = VideoProcessingService.CreateDefault();
        string input = @"C:\videos\clip.mp4";

        string output = service.GetConvertOutputPath(input, "mkv", replaceOriginal: false);

        Assert.Equal(@"C:\videos\clip_converted.mkv", output);
    }

    [Fact]
    public void GetConvertOutputPath_when_replacing_targets_original_name_with_new_extension()
    {
        // Regression: replaceOriginal used to return the input path itself, which
        // handed ffmpeg the same file as input and output ("-y" truncates the
        // output before reading -> source destroyed on old ffmpeg builds).
        var service = VideoProcessingService.CreateDefault();

        string output = service.GetConvertOutputPath(@"C:\videos\clip.mp4", "mkv", replaceOriginal: true);

        Assert.Equal(@"C:\videos\clip.mkv", output);
    }

    [Fact]
    public void GetConvertTempOutputPath_is_a_sibling_temp_file_distinct_from_input()
    {
        var service = VideoProcessingService.CreateDefault();
        string input = @"C:\videos\clip.mp4";

        string temp = service.GetConvertTempOutputPath(input, "mp4");

        Assert.Equal(@"C:\videos\clip_convert_tmp.mp4", temp);
        Assert.NotEqual(input, temp, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetFlatExtractOutputPattern_uses_flat_frame_naming()
    {
        var service = VideoProcessingService.CreateDefault();
        string pattern = service.GetFlatExtractOutputPattern(@"D:\set\clip.mp4", "png");

        Assert.Equal(@"D:\set\clip_frame_%06d.png", pattern);
    }

    [Fact]
    public void FinalizeReplaceOriginal_refuses_to_overwrite_an_unrelated_sibling()
    {
        // VIDEO-01: clip.mkv -> mp4 with an independent clip.mp4 already present
        // must fail instead of silently clobbering the sibling and deleting mkv.
        using var temp = new TemporaryDirectory();
        string input = Path.Combine(temp.Path, "clip.mkv");
        string tempOut = Path.Combine(temp.Path, "clip_convert_tmp.mp4");
        string final = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(input, "source");
        File.WriteAllText(tempOut, "converted");
        File.WriteAllText(final, "unrelated");
        var service = VideoProcessingService.CreateDefault();

        Assert.Throws<IOException>(() => service.FinalizeReplaceOriginal(input, tempOut, final));

        Assert.Equal("unrelated", File.ReadAllText(final));
        Assert.True(File.Exists(input));
    }

    [Fact]
    public void FinalizeReplaceOriginal_replaces_same_extension_and_free_cross_format_targets()
    {
        using var temp = new TemporaryDirectory();
        var service = VideoProcessingService.CreateDefault();

        string sameExt = Path.Combine(temp.Path, "a.mp4");
        string sameExtTemp = Path.Combine(temp.Path, "a_convert_tmp.mp4");
        File.WriteAllText(sameExt, "old");
        File.WriteAllText(sameExtTemp, "new");
        service.FinalizeReplaceOriginal(sameExt, sameExtTemp, sameExt);
        Assert.Equal("new", File.ReadAllText(sameExt));

        string crossInput = Path.Combine(temp.Path, "b.mkv");
        string crossTemp = Path.Combine(temp.Path, "b_convert_tmp.mp4");
        string crossFinal = Path.Combine(temp.Path, "b.mp4");
        File.WriteAllText(crossInput, "src");
        File.WriteAllText(crossTemp, "conv");
        service.FinalizeReplaceOriginal(crossInput, crossTemp, crossFinal);
        Assert.Equal("conv", File.ReadAllText(crossFinal));
        Assert.False(File.Exists(crossInput));
    }
}
