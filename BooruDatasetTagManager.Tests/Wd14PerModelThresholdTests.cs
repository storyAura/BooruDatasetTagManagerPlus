using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class Wd14PerModelThresholdTests
{
    [Fact]
    public void GetThresholdsForRepo_uses_model_default_when_repo_has_no_saved_entry()
    {
        var settings = new Wd14TaggerSettings
        {
            SelectedModelRepo = "SmilingWolf/wd-eva02-large-tagger-v3",
            Threshold = 0.52,
            CharacterThreshold = 0.85
        };

        (double threshold, double characterThreshold) =
            settings.GetThresholdsForRepo("SmilingWolf/wd-vit-large-tagger-v3");

        Assert.Equal(0.26, threshold, 2);
        Assert.Equal(0.85, characterThreshold, 2);
    }

    [Fact]
    public void GetThresholdsForRepo_uses_legacy_global_values_for_selected_repo()
    {
        var settings = new Wd14TaggerSettings
        {
            SelectedModelRepo = "SmilingWolf/wd-eva02-large-tagger-v3",
            Threshold = 0.52,
            CharacterThreshold = 0.85
        };

        (double threshold, double characterThreshold) =
            settings.GetThresholdsForRepo("SmilingWolf/wd-eva02-large-tagger-v3");

        Assert.Equal(0.52, threshold, 2);
        Assert.Equal(0.85, characterThreshold, 2);
    }

    [Fact]
    public void SetThresholdsForRepo_persists_independent_values_per_repo()
    {
        var settings = new Wd14TaggerSettings();
        settings.SetThresholdsForRepo("SmilingWolf/wd-eva02-large-tagger-v3", 0.52, 0.85);
        settings.SetThresholdsForRepo("SmilingWolf/wd-vit-large-tagger-v3", 0.26, 0.80);

        (double evaThreshold, _) = settings.GetThresholdsForRepo("SmilingWolf/wd-eva02-large-tagger-v3");
        (double vitThreshold, double vitCharacterThreshold) =
            settings.GetThresholdsForRepo("SmilingWolf/wd-vit-large-tagger-v3");

        Assert.Equal(0.52, evaThreshold, 2);
        Assert.Equal(0.26, vitThreshold, 2);
        Assert.Equal(0.80, vitCharacterThreshold, 2);
    }

    [Fact]
    public void EnsureLegacyThresholdMigrated_seeds_selected_repo_entry()
    {
        var settings = new Wd14TaggerSettings
        {
            SelectedModelRepo = "SmilingWolf/wd-eva02-large-tagger-v3",
            Threshold = 0.52,
            CharacterThreshold = 0.85
        };

        settings.EnsureLegacyThresholdMigrated();

        Assert.True(settings.HasThresholdsForRepo("SmilingWolf/wd-eva02-large-tagger-v3"));
        (double threshold, double characterThreshold) =
            settings.GetThresholdsForRepo("SmilingWolf/wd-eva02-large-tagger-v3");
        Assert.Equal(0.52, threshold, 2);
        Assert.Equal(0.85, characterThreshold, 2);
    }
}
