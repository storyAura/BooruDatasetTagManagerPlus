using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class Wd14SessionMetadataTests
{
    [Fact]
    public void ResolveSessionMetadata_prefers_known_output_names()
    {
        (string inputName, string outputName) = Wd14OnnxTaggerService.ResolveSessionMetadata(
            new[] { "input" },
            new[] { "embedding", "output" });

        Assert.Equal("input", inputName);
        Assert.Equal("output", outputName);
    }

    [Fact]
    public void ResolveSessionMetadata_falls_back_to_first_output()
    {
        (_, string outputName) = Wd14OnnxTaggerService.ResolveSessionMetadata(
            new[] { "images" },
            new[] { "scores" });

        Assert.Equal("scores", outputName);
    }

    [Theory]
    [InlineData(new[] { 1, 448, 448, 3 }, 448)]
    [InlineData(new[] { 1, 512, 512, 3 }, 512)]
    [InlineData(new[] { 1, 3, 448, 448 }, 448)]
    [InlineData(new[] { 448, 448, 3 }, 448)]
    public void ResolveInputSize_supports_nhwc_and_nchw_shapes(int[] dims, int expected)
    {
        int size = Wd14OnnxTaggerService.ResolveInputSize(dims);
        Assert.Equal(expected, size);
    }

    [Fact]
    public void ResolveInputSize_defaults_to_448_for_dynamic_shapes()
    {
        int size = Wd14OnnxTaggerService.ResolveInputSize(new[] { -1, -1, -1, 3 });
        Assert.Equal(448, size);
    }

    [Theory]
    [InlineData(new[] { 1, 448, 448, 3 }, false)]
    [InlineData(new[] { 1, 3, 448, 448 }, true)]
    [InlineData(new[] { -1, -1, -1, 3 }, false)]
    public void IsNchwLayout_follows_channel_axis(int[] dims, bool expected)
    {
        Assert.Equal(expected, Wd14OnnxTaggerService.IsNchwLayout(dims));
    }

    [Fact]
    public void ValuesLookLikeLogits_is_false_for_probabilities_and_true_outside_unit_interval()
    {
        Assert.False(Wd14OnnxTaggerService.ValuesLookLikeLogits(new[] { 0f, 0.61f, 1f }));
        Assert.True(Wd14OnnxTaggerService.ValuesLookLikeLogits(new[] { 0.2f, -0.4f }));

        float[] sigmoid = Wd14OnnxTaggerService.ApplySigmoid(new[] { 0f });
        Assert.Equal(0.5f, sigmoid[0], 3);
    }
}
