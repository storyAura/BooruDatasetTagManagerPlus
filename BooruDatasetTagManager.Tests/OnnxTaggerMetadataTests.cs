using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxTaggerMetadataTests
{
    [Fact]
    public void Wd14_models_include_twelve_entries_with_expected_default_thresholds()
    {
        Assert.Equal(12, Wd14OnnxTaggerService.Models.Count);
        Assert.Equal(0.52, Wd14OnnxTaggerService.GetModel("SmilingWolf/wd-eva02-large-tagger-v3").DefaultThreshold, 2);
        Assert.Equal(0.35, Wd14OnnxTaggerService.GetModel("SmilingWolf/wd-v1-4-vit-tagger-v2").DefaultThreshold, 2);
    }

    [Fact]
    public void PixAi_requires_five_model_files()
    {
        Assert.Equal(5, PixAiOnnxTaggerService.RequiredFiles.Count);
        Assert.Contains("preprocess.json", PixAiOnnxTaggerService.RequiredFiles);
        Assert.Contains("thresholds.csv", PixAiOnnxTaggerService.RequiredFiles);
    }
}
