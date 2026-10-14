using BooruDatasetTagManager;
using Xunit;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager.Tests;

public sealed class TaggerProfessionalModeTests
{
    [Fact]
    public void Professional_mode_off_drops_style_and_rating_even_at_full_confidence()
    {
        TaggerProFilter filter = TaggerProFilter.Basic(0.17, 0.27);

        Assert.False(TaggerCategoryFilter.TryAccept("official art", TaggerCategoryFilter.Style, 1f, filter, out _));
        Assert.False(TaggerCategoryFilter.TryAccept("sensitive", TaggerCategoryFilter.Rating, 1f, filter, out _));
        Assert.True(TaggerCategoryFilter.TryAccept("1girl", TaggerCategoryFilter.General, 0.2f, filter, out AutoTagProviderItem general));
        Assert.Equal("1girl", general.Tag);
        Assert.False(general.IsCharacter);
    }

    [Fact]
    public void PixAi_v1_style_follows_the_include_switch_and_meta_uses_its_threshold()
    {
        var filter = new TaggerProFilter
        {
            ProfessionalMode = true,
            GeneralThreshold = 0.17,
            CharacterThreshold = 0.27,
            StyleThreshold = 0.15,
            MetaThreshold = 0.17,
            IncludeStyle = false,
            IncludeMeta = true
        };

        Assert.False(TaggerCategoryFilter.TryAccept("official art", TaggerCategoryFilter.Style, 0.99f, filter, out _));

        filter.IncludeStyle = true;
        Assert.True(TaggerCategoryFilter.TryAccept("official art", TaggerCategoryFilter.Style, 0.2f, filter, out _));
        Assert.False(TaggerCategoryFilter.TryAccept("official art", TaggerCategoryFilter.Style, 0.1f, filter, out _));

        Assert.True(TaggerCategoryFilter.TryAccept("highres", TaggerCategoryFilter.Meta, 0.2f, filter, out _));
        Assert.False(TaggerCategoryFilter.TryAccept("highres", TaggerCategoryFilter.Meta, 0.1f, filter, out _));
    }

    [Fact]
    public void Wd_rating_names_gain_a_rating_prefix_unless_they_already_have_one()
    {
        var filter = new TaggerProFilter
        {
            ProfessionalMode = true,
            RatingThreshold = 0.41
        };

        Assert.True(TaggerCategoryFilter.TryAccept("sensitive", TaggerCategoryFilter.Rating, 0.5f, filter, out AutoTagProviderItem bare));
        Assert.Equal("rating:sensitive", bare.Tag);

        Assert.True(TaggerCategoryFilter.TryAccept("rating:g", TaggerCategoryFilter.Rating, 0.8f, filter, out AutoTagProviderItem prefixed));
        Assert.Equal("rating:g", prefixed.Tag);
    }

    [Fact]
    public void Character_tags_stay_first_after_confidence_sort()
    {
        var item = new DataItem();
        var settings = new Wd14TaggerSettings
        {
            SetMode = NetworkResultSetMode.AllWithReplacement,
            SortMode = AutoTaggerSort.Confidence,
            CharacterFirst = true
        };

        TagWriteService.ApplyTags(item, new[]
        {
            new AutoTagProviderItem { Tag = "smile", Confidence = 0.9f },
            new AutoTagProviderItem { Tag = "hatsune miku", Confidence = 0.4f, IsCharacter = true },
            new AutoTagProviderItem { Tag = "solo", Confidence = 0.8f }
        }, settings);

        Assert.Equal(new[] { "hatsune miku", "smile", "solo" }, item.Tags.TextTags);
    }

    [Fact]
    public void PixAi_v1_profile_exposes_the_extra_heads_and_wd_only_rating()
    {
        OnnxTaggerModelEntry pixai = OnnxTaggerCatalog.GetById(PixAiOnnxTaggerService.V10Id);
        TaggerProProfile pixaiProfile = TaggerProProfile.For(pixai);
        Assert.True(pixaiProfile.Style);
        Assert.True(pixaiProfile.Copyright);
        Assert.True(pixaiProfile.Meta);
        Assert.True(pixaiProfile.Rating);
        Assert.False(pixaiProfile.CreateDefaults().IncludeStyle);
        Assert.True(pixaiProfile.CreateDefaults().IncludeMeta);

        OnnxTaggerModelEntry wd = OnnxTaggerCatalog.GetById("wd:" + Wd14OnnxTaggerService.CanaryRepo);
        TaggerProProfile wdProfile = TaggerProProfile.For(wd);
        Assert.True(wdProfile.Rating);
        Assert.False(wdProfile.Style);
        Assert.False(wdProfile.Copyright);
        Assert.Equal(0.61, wdProfile.DefaultRating, 2);

        Assert.False(TaggerProProfile.For(OnnxTaggerCatalog.GetById(OppaiOracleOnnxService.V1Id)).HasExtraRows);
    }
}
