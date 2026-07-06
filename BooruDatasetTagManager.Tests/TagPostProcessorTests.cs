using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagPostProcessorTests
{
    [Fact]
    public void Process_replaces_underscores_for_wd14_settings()
    {
        var settings = new Wd14TaggerSettings { ReplaceUnderscoresWithSpaces = true };

        List<string> result = TagPostProcessor.Process(new[] { "long_hair", "blue_eyes" }, settings);

        Assert.Equal(new[] { "long hair", "blue eyes" }, result);
    }

    [Fact]
    public void Process_preserves_kaomoji_tags()
    {
        var settings = new Wd14TaggerSettings { ReplaceUnderscoresWithSpaces = true };

        List<string> result = TagPostProcessor.Process(new[] { "0_0", "long_hair" }, settings);

        Assert.Equal(new[] { "0_0", "long hair" }, result);
    }

    [Fact]
    public void Process_does_not_replace_underscores_for_non_onnx_settings()
    {
        var settings = new InterragatorSettings();

        List<string> result = TagPostProcessor.Process(new[] { "long_hair" }, settings);

        Assert.Equal(new[] { "long_hair" }, result);
    }

    [Fact]
    public void Process_adds_prefix_and_suffix_tags()
    {
        var settings = new Wd14TaggerSettings
        {
            ReplaceUnderscoresWithSpaces = false,
            TagPrefix = "masterpiece, best quality",
            TagSuffix = "artist:test",
        };

        List<string> result = TagPostProcessor.Process(new[] { "1girl", "solo" }, settings);

        Assert.Equal(new[] { "masterpiece", "best quality", "1girl", "solo", "artist:test" }, result);
    }

    [Fact]
    public void Process_returns_prefix_and_suffix_when_inference_is_empty()
    {
        var settings = new PixAiTaggerSettings
        {
            TagPrefix = "prefix_tag",
            TagSuffix = "suffix_tag",
        };

        List<string> result = TagPostProcessor.Process(Array.Empty<string>(), settings);

        Assert.Equal(new[] { "prefix_tag", "suffix_tag" }, result);
    }

    [Fact]
    public void Process_skips_underscore_conversion_when_disabled()
    {
        var settings = new PixAiTaggerSettings { ReplaceUnderscoresWithSpaces = false };

        List<string> result = TagPostProcessor.Process(new[] { "long_hair" }, settings);

        Assert.Equal(new[] { "long_hair" }, result);
    }
}
