using System;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class PixAiOnnxTaggerServiceTests
{
    [Fact]
    public void ParseLines_reads_pixai_v09_csv_format()
    {
        var lines = new[]
        {
            "id,tag_id,name,category,count,ips",
            "0,12345,1girl,0,1000,0.5",
            "1,67890,hatsune_miku,4,500,0.9"
        };

        var labels = PixAiSelectedTagsCsvLoader.ParseLines(lines);

        Assert.Equal(2, labels.Count);
        Assert.Equal(("1girl", 0), labels[0]);
        Assert.Equal(("hatsune_miku", 4), labels[1]);
    }

    [Fact]
    public void ParseLines_falls_back_to_legacy_wd14_format()
    {
        var lines = new[]
        {
            "1girl,0",
            "hatsune_miku,4"
        };

        var labels = PixAiSelectedTagsCsvLoader.ParseLines(lines);

        Assert.Equal(2, labels.Count);
        Assert.Equal(("1girl", 0), labels[0]);
        Assert.Equal(("hatsune_miku", 4), labels[1]);
    }

    [Fact]
    public void ApplySigmoid_maps_logits_to_probabilities()
    {
        float[] output = PixAiOnnxTaggerService.ApplySigmoid(new[] { 0f, 10f, -10f });

        Assert.Equal(0.5f, output[0], 3);
        Assert.True(output[1] > 0.99f);
        Assert.True(output[2] < 0.01f);
    }

    [Fact]
    public void ResolveSessionMetadata_prefers_prediction_over_logits()
    {
        (string inputName, string outputName, bool requiresSigmoid) =
            PixAiOnnxTaggerService.ResolveSessionMetadata(
                new[] { "input" },
                new[] { "embedding", "logits", "prediction" });

        Assert.Equal("input", inputName);
        Assert.Equal("prediction", outputName);
        Assert.False(requiresSigmoid);
    }

    [Fact]
    public void ResolveSessionMetadata_uses_logits_when_prediction_missing()
    {
        (_, string outputName, bool requiresSigmoid) =
            PixAiOnnxTaggerService.ResolveSessionMetadata(
                new[] { "input" },
                new[] { "embedding", "logits" });

        Assert.Equal("logits", outputName);
        Assert.True(requiresSigmoid);
    }

    [Fact]
    public void ResolveSessionMetadata_throws_when_no_supported_output_exists()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            PixAiOnnxTaggerService.ResolveSessionMetadata(
                new[] { "input" },
                new[] { "embedding" }));

        Assert.Contains("embedding", ex.Message, StringComparison.Ordinal);
    }
}
