using System;
using System.Drawing;
using System.Drawing.Imaging;
using BooruDatasetTagManager;
using Microsoft.ML.OnnxRuntime.Tensors;
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

    [Fact]
    public void Parse_tags_json_keeps_general_and_character_and_drops_other_categories()
    {
        const string json = """
            {
              "num_classes": 4,
              "categories": [
                { "name": "general", "offset": 0, "count": 2, "tags": ["1girl", "solo"] },
                { "name": "character", "offset": 2, "count": 1, "tags": ["hatsune_miku"] },
                { "name": "rating", "offset": 3, "count": 1, "tags": ["rating:g"] }
              ]
            }
            """;

        var labels = PixAiTagsJsonLoader.Parse(json);

        Assert.Equal(4, labels.Count);
        Assert.Equal(("1girl", 0), labels[0]);
        Assert.Equal(("solo", 0), labels[1]);
        Assert.Equal(("hatsune_miku", 4), labels[2]);
        Assert.Equal(("rating:g", TaggerCategoryFilter.Rating), labels[3]);
    }

    [Fact]
    public void V1_letterbox_pads_black_and_stores_rgb()
    {
        const int size = 8;
        using var source = new Bitmap(size, size / 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.FromArgb(255, 255, 0, 0));
        }

        DenseTensor<float> tensor = PixAiV1OnnxImagePreprocessor.CreateInputTensor(source, size);

        Assert.Equal(new[] { 1, 3, size, size }, tensor.Dimensions.ToArray());
        // Top row is black padding, which normalizes to -1. A stretch would have painted it red.
        Assert.Equal(-1f, tensor[0, 0, 0, 0], 3);
        Assert.Equal(-1f, tensor[0, 1, 0, 0], 3);
        Assert.Equal(-1f, tensor[0, 2, 0, 0], 3);
        // Pasted red sits in channel 0 (RGB), not the BGR order used by v0.9.
        Assert.Equal(1f, tensor[0, 0, 3, 3], 2);
        Assert.Equal(-1f, tensor[0, 1, 3, 3], 2);
        Assert.Equal(-1f, tensor[0, 2, 3, 3], 2);
    }

    [Fact]
    public void Legacy_pixai_thresholds_migrate_onto_v09_and_v10_uses_catalog_defaults()
    {
        var settings = new PixAiTaggerSettings
        {
            GeneralThreshold = 0.42,
            CharacterThreshold = 0.77
        };

        (double general, double character) = settings.GetThresholdsForModel(PixAiOnnxTaggerService.V09Id);
        Assert.Equal(0.42, general, 2);
        Assert.Equal(0.77, character, 2);

        (double v10General, double v10Character) = settings.GetThresholdsForModel(PixAiOnnxTaggerService.V10Id);
        Assert.Equal(0.17, v10General, 2);
        Assert.Equal(0.27, v10Character, 2);

        settings.SetThresholdsForModel(PixAiOnnxTaggerService.V10Id, 0.2, 0.3);
        Assert.Equal(0.42, settings.GeneralThreshold, 2);
        (double savedGeneral, double savedCharacter) = settings.GetThresholdsForModel(PixAiOnnxTaggerService.V10Id);
        Assert.Equal(0.2, savedGeneral, 2);
        Assert.Equal(0.3, savedCharacter, 2);
    }
}
