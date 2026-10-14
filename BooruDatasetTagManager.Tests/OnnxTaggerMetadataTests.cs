using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxTaggerMetadataTests
{
    [Fact]
    public void Wd14_models_include_thirteen_entries_with_expected_default_thresholds()
    {
        Assert.Equal(13, Wd14OnnxTaggerService.Models.Count);
        Assert.Equal(0.52, Wd14OnnxTaggerService.GetModel(Wd14OnnxTaggerService.DefaultRepo).DefaultThreshold, 2);
        Assert.Equal(0.35, Wd14OnnxTaggerService.GetModel("SmilingWolf/wd-v1-4-vit-tagger-v2").DefaultThreshold, 2);
        Wd14ModelDefinition canary = Wd14OnnxTaggerService.GetModel(Wd14OnnxTaggerService.CanaryRepo);
        Assert.Equal(0.61, canary.DefaultThreshold, 2);
        Assert.Equal(0.61, canary.DefaultCharacterThreshold, 2);
    }

    [Fact]
    public void PixAi_v09_requires_five_files_and_v10_requires_onnx_sidecar_and_tags()
    {
        PixAiModelDefinition v09 = PixAiOnnxTaggerService.GetById(PixAiOnnxTaggerService.V09Id);
        PixAiModelDefinition v10 = PixAiOnnxTaggerService.GetById(PixAiOnnxTaggerService.V10Id);

        Assert.Equal(5, v09.AllFiles().Count());
        Assert.Contains("preprocess.json", v09.AllFiles());
        Assert.Contains("thresholds.csv", v09.AllFiles());
        Assert.Equal(PixAiOnnxTaggerService.RequiredFiles.ToArray(), v09.AllFiles().ToArray());

        Assert.Contains("model.onnx", v10.AllFiles());
        Assert.Contains("model.onnx.data", v10.AllFiles());
        Assert.Contains("tags.json", v10.AllFiles());
        Assert.Equal(3, v10.AllFiles().Count());
    }
}
