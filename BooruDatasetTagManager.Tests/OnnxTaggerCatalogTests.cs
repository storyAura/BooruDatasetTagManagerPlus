using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxTaggerCatalogTests
{
    [Fact]
    public void AllModels_contains_twelve_wd14_and_one_pixai_entry()
    {
        Assert.Equal(13, OnnxTaggerCatalog.AllModels.Count);
        Assert.Equal(12, OnnxTaggerCatalog.AllModels.Count(model => model.Kind == OnnxTaggerModelKind.Wd14));
        Assert.Single(OnnxTaggerCatalog.AllModels, model => model.Kind == OnnxTaggerModelKind.PixAi);
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
