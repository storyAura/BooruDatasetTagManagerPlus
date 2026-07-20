using Newtonsoft.Json.Linq;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class ClTaggerServiceTests
{
    [Fact]
    public void ParseV1TagMappingAlignsTagsToModelOutputIndex()
    {
        // Real cl_tagger v1 format: { "idx": { "tag", "category" } }.
        JObject root = JObject.Parse("""
            {
              "0": { "tag": "general", "category": "Rating" },
              "1": { "tag": "1girl", "category": "General" },
              "3": { "tag": "hatsune_miku", "category": "Character" }
            }
            """);

        var labels = ClTaggerOnnxService.ParseV1TagMapping(root);

        Assert.Equal(4, labels.Count);
        Assert.Equal(("general", "Rating"), labels[0]);
        Assert.Equal(("1girl", "General"), labels[1]);
        Assert.Null(labels[2].Name); // gap stays empty instead of shifting indexes
        Assert.Equal(("hatsune_miku", "Character"), labels[3]);
    }

    [Fact]
    public void ParseV2VocabularyReadsIdxToTagAndCategories()
    {
        // Real cl_tagger_v2 format: idx_to_tag + tag_to_category maps.
        JObject root = JObject.Parse("""
            {
              "idx_to_tag": { "0": "1girl", "1": "hatsune_miku", "2": "explicit" },
              "tag_to_category": { "1girl": "General", "hatsune_miku": "Character", "explicit": "Rating" }
            }
            """);

        var labels = ClTaggerOnnxService.ParseV2Vocabulary(root);

        Assert.Equal(3, labels.Count);
        Assert.Equal(("1girl", "General"), labels[0]);
        Assert.Equal(("hatsune_miku", "Character"), labels[1]);
        Assert.Equal(("explicit", "Rating"), labels[2]);
    }

    [Fact]
    public void SigmoidIsStableForExtremeLogits()
    {
        Assert.Equal(0.5, ClTaggerOnnxService.Sigmoid(0), 6);
        Assert.True(ClTaggerOnnxService.Sigmoid(100) > 0.999);
        Assert.True(ClTaggerOnnxService.Sigmoid(-100) < 0.001);
        // Large magnitudes must not overflow to NaN/Infinity.
        Assert.False(double.IsNaN(ClTaggerOnnxService.Sigmoid(-10000)));
        Assert.False(double.IsNaN(ClTaggerOnnxService.Sigmoid(10000)));
    }

    [Fact]
    public void NormalizeMapsBytesToMeanHalfStdHalf()
    {
        // (x/255 - 0.5) / 0.5 per the author's preprocessing.
        Assert.Equal(-1f, ClTaggerImagePreprocessor.Normalize(0), 5);
        Assert.Equal(1f, ClTaggerImagePreprocessor.Normalize(255), 5);
        Assert.Equal(0f, ClTaggerImagePreprocessor.Normalize(128), 2);
    }

    [Fact]
    public void CatalogExposesClModelsAndMarksTheGatedRepo()
    {
        var v1 = OnnxTaggerCatalog.GetById("cl:Nonene/cl_tagger:1_02");
        var v200 = OnnxTaggerCatalog.GetById("cl:cella110n/cl_tagger_v2:v2_00");
        var v2 = OnnxTaggerCatalog.GetById("cl:cella110n/cl_tagger_v2:v2_01a");

        Assert.Equal(OnnxTaggerModelKind.ClTagger, v1.Kind);
        Assert.False(v1.ClModel.IsGated);
        Assert.Equal("cl_tagger_1_02/tag_mapping.json", v1.ClModel.LabelsFile);

        // Both v2 variants ship from the same gated repo, each from its own folder.
        Assert.True(v200.ClModel.IsGated);
        Assert.Equal("v2_00/model_vocabulary.json", v200.ClModel.LabelsFile);
        Assert.Contains("v2_00/model.onnx.data", v200.ClModel.AllFiles());

        Assert.Equal(OnnxTaggerModelKind.ClTagger, v2.Kind);
        Assert.True(v2.ClModel.IsGated);
        // The multi-GB external-weights sidecar must be part of the download set.
        Assert.Contains("v2_01a/model.onnx.data", v2.ClModel.AllFiles());
        Assert.Equal(0.55, v2.DefaultThreshold);
    }

    [Fact]
    public void ValidateCachedFileAppliesOnnxRuleToNestedFileNames()
    {
        using var temp = new TempDir();
        string path = Path.Combine(temp.Path, "model.onnx");
        File.WriteAllBytes(path, new byte[16]); // far below the 1 MB minimum

        // The rule must match on the file NAME even when the repo-relative
        // path contains a subfolder (e.g. "v2_01a/model.onnx").
        Assert.False(HuggingFaceModelDownloader.ValidateCachedFile(path, "v2_01a/model.onnx"));
        Assert.True(HuggingFaceModelDownloader.ValidateCachedFile(path, "v2_01a/model_vocabulary.json"));
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-cl-tagger-tests-{Guid.NewGuid():N}");

        public TempDir()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
