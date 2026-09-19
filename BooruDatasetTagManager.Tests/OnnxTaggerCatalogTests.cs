using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxTaggerCatalogTests
{
    [Fact]
    public void AllModels_contains_twelve_wd14_one_pixai_three_cl_and_two_oppai_entries()
    {
        Assert.Equal(18, OnnxTaggerCatalog.AllModels.Count);
        Assert.Equal(12, OnnxTaggerCatalog.AllModels.Count(model => model.Kind == OnnxTaggerModelKind.Wd14));
        Assert.Single(OnnxTaggerCatalog.AllModels, model => model.Kind == OnnxTaggerModelKind.PixAi);
        Assert.Equal(3, OnnxTaggerCatalog.AllModels.Count(model => model.Kind == OnnxTaggerModelKind.ClTagger));
        Assert.Equal(2, OnnxTaggerCatalog.AllModels.Count(model => model.Kind == OnnxTaggerModelKind.OppaiOracle));
    }

    [Fact]
    public void OppaiOracle_entries_use_expected_repo_thresholds_and_no_character_threshold()
    {
        OnnxTaggerModelEntry v1 = OnnxTaggerCatalog.GetById(OppaiOracleOnnxService.V1Id);
        OnnxTaggerModelEntry v11 = OnnxTaggerCatalog.GetById(OppaiOracleOnnxService.V11Id);

        Assert.Equal(OnnxTaggerModelKind.OppaiOracle, v1.Kind);
        Assert.Equal(OppaiOracleOnnxService.ModelRepo, v1.Repo);
        Assert.Equal(0.55, v1.DefaultThreshold, 2);
        Assert.False(v1.DefaultCharacterThreshold.HasValue);
        Assert.Equal("V1_onnx/model.onnx", v1.OppaiModel.ModelFile);
        Assert.Equal(320, v1.OppaiModel.ImageSize);

        Assert.Equal(OnnxTaggerModelKind.OppaiOracle, v11.Kind);
        Assert.Equal(0.65, v11.DefaultThreshold, 2);
        Assert.False(v11.DefaultCharacterThreshold.HasValue);
        Assert.Equal("V1.1_onnx/model.onnx", v11.OppaiModel.ModelFile);
        Assert.Equal(448, v11.OppaiModel.ImageSize);
    }

    [Fact]
    public void DisplayNames_include_full_model_name_after_family_prefix()
    {
        Assert.Equal("[PixAI] pixai-tagger v0.9",
            OnnxTaggerCatalog.GetById(OnnxTaggerCatalog.PixAiModelId).DisplayName);
        Assert.Equal("[CL] cl_tagger v1.02",
            OnnxTaggerCatalog.GetById("cl:Nonene/cl_tagger:1_02").DisplayName);
        Assert.Equal("[CL] cl_tagger_v2 v2.00 🔒",
            OnnxTaggerCatalog.GetById("cl:cella110n/cl_tagger_v2:v2_00").DisplayName);
        Assert.Equal("[CL] cl_tagger_v2 v2.01a 🔒",
            OnnxTaggerCatalog.GetById("cl:cella110n/cl_tagger_v2:v2_01a").DisplayName);
        Assert.Equal("[OO] OppaiOracle V1 (320)",
            OnnxTaggerCatalog.GetById(OppaiOracleOnnxService.V1Id).DisplayName);
        Assert.Equal("[OO] OppaiOracle V1.1 (448)",
            OnnxTaggerCatalog.GetById(OppaiOracleOnnxService.V11Id).DisplayName);
    }

    [Fact]
    public void PixAi_entry_uses_expected_repo_and_thresholds()
    {
        OnnxTaggerModelEntry pixAi = OnnxTaggerCatalog.AllModels.Single(model => model.Id == OnnxTaggerCatalog.PixAiModelId);

        Assert.Equal(PixAiOnnxTaggerService.ModelRepo, pixAi.Repo);
        Assert.Equal(0.3, pixAi.DefaultThreshold, 2);
        Assert.Equal(0.85, pixAi.DefaultCharacterThreshold.GetValueOrDefault(), 2);
    }

    [Fact]
    public void Wd14_default_thresholds_match_service_definitions()
    {
        foreach (Wd14ModelDefinition definition in Wd14OnnxTaggerService.Models)
        {
            OnnxTaggerModelEntry entry = OnnxTaggerCatalog.AllModels.Single(model =>
                model.Kind == OnnxTaggerModelKind.Wd14
                && string.Equals(model.Repo, definition.Repo, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(definition.DefaultThreshold, entry.DefaultThreshold, 2);
        }
    }

    [Fact]
    public void GetById_returns_matching_entry_or_first_model()
    {
        OnnxTaggerModelEntry pixAi = OnnxTaggerCatalog.GetById(OnnxTaggerCatalog.PixAiModelId);
        Assert.Equal(OnnxTaggerModelKind.PixAi, pixAi.Kind);

        OnnxTaggerModelEntry fallback = OnnxTaggerCatalog.GetById("missing-model-id");
        Assert.Equal(OnnxTaggerCatalog.AllModels[0].Id, fallback.Id);
    }
}
