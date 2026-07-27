using System.Text;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CharacterTagCatalogTests
{
    private static CharacterTagCatalog Load(params string[] rows)
    {
        using var temp = new TemporaryDirectory();
        string path = Path.Combine(temp.Path, "danbooru_character_tags.csv");
        var content = new StringBuilder("character_tag,other_names,copyright\r\n");
        foreach (string row in rows)
            content.Append(row).Append("\r\n");
        File.WriteAllText(path, content.ToString(), new UTF8Encoding(true));
        return CharacterTagCatalog.LoadFromFile(path);
    }

    [Fact]
    public void MatchesNormalizedRawFixedAndEscapedTagForms()
    {
        CharacterTagCatalog catalog = Load(
            "nakamachi_arale,中町あられ,bang_dream!",
            "alf_(silver_palace),,silver_palace");

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.Contains("nakamachi_arale"));
        Assert.True(catalog.Contains("nakamachi arale"));
        Assert.True(catalog.Contains("Nakamachi Arale"));
        Assert.True(catalog.Contains("alf \\(silver palace\\)"));
        Assert.False(catalog.Contains("unknown person"));
    }

    [Fact]
    public void DisplayTranslationUsesPrimaryNamePlusFranchise()
    {
        CharacterTagCatalog catalog = Load(
            "nakamachi_arale,中町あられ,bang_dream!",
            "hattori_hanzou_(samurai_spirits),\"服部半藏, 服部半蔵(侍魂)\",samurai_spirits",
            "no_name_character,,some_series",
            "lunatic,Lunatic_Lab,");

        Assert.Equal("中町あられ (bang dream!)", catalog.GetDisplayTranslation("nakamachi arale"));
        // First entry of the comma-separated alternative list wins.
        Assert.Equal("服部半藏 (samurai spirits)", catalog.GetDisplayTranslation("hattori hanzou (samurai spirits)"));
        // No name: classification may still hit, but no translation.
        Assert.True(catalog.Contains("no name character"));
        Assert.Null(catalog.GetDisplayTranslation("no name character"));
        Assert.Equal("Lunatic Lab", catalog.GetDisplayTranslation("lunatic"));
    }

    [Fact]
    public void ParsesParentRelationsFromTheFiveColumnFormat()
    {
        CharacterTagCatalog catalog = Load(
            "hatsune_miku,\"初音ミク, 初音未来\",vocaloid,,142418",
            "racing_miku,レーシングミク,goodsmile_racing,hatsune_miku,1734",
            "self_parent,,x,self_parent,1");

        // The two extra columns change nothing for the existing consumers.
        Assert.True(catalog.Contains("racing miku"));
        Assert.Equal("初音ミク (vocaloid)", catalog.GetDisplayTranslation("hatsune miku"));

        // Parent lookup normalizes both spellings and both directions.
        Assert.Equal("hatsune miku", catalog.GetParentTag("racing_miku"));
        Assert.Equal("hatsune miku", catalog.GetParentTag("Racing Miku"));
        Assert.Null(catalog.GetParentTag("hatsune miku"));
        // A self-referencing row must not create a loop entry.
        Assert.Null(catalog.GetParentTag("self parent"));
        // The old 3-column format keeps loading with no parents at all.
        Assert.Null(Load("plain_tag,,series").GetParentTag("plain tag"));
    }

    [Fact]
    public void ParsesQuotedQuotesAndMissingFileGracefully()
    {
        CharacterTagCatalog catalog = Load("\"\"\"a-z\"\"\",,");
        Assert.True(catalog.Contains("\"a-z\""));

        Assert.Equal(0, CharacterTagCatalog.LoadFromFile(
            Path.Combine(Path.GetTempPath(), "definitely_missing_catalog.csv")).Count);
        Assert.Equal(0, CharacterTagCatalog.LoadFromFile(null).Count);
    }

    [Fact]
    public void ParseCsvLineHandlesQuotingRules()
    {
        Assert.Equal(new[] { "a", "b", "c" }, CharacterTagCatalog.ParseCsvLine("a,b,c"));
        Assert.Equal(new[] { "\"a-z\"", "", "" }, CharacterTagCatalog.ParseCsvLine("\"\"\"a-z\"\"\",,"));
        Assert.Equal(new[] { "tag", "one, two", "ip" }, CharacterTagCatalog.ParseCsvLine("tag,\"one, two\",ip"));
    }

    [Fact]
    public void SearchAutocompleteMatchesEnglishTagsAndChineseNames()
    {
        CharacterTagCatalog catalog = Load(
            "firefly_(honkai:_star_rail),流萤,honkai:_star_rail",
            "fu_xuan_(honkai:_star_rail),符玄,honkai:_star_rail");

        TagsDB.TagItem english = Assert.Single(catalog.SearchAutocomplete("firefly"));
        Assert.False(english.IsAlias);
        Assert.Equal("firefly (honkai: star rail)", english.GetTag());
        Assert.Equal("firefly (honkai: star rail) (流萤 (honkai: star rail))", english.ToString());

        TagsDB.TagItem alias = Assert.Single(catalog.SearchAutocomplete("流萤"));
        Assert.True(alias.IsAlias);
        Assert.Equal("firefly (honkai: star rail)", alias.GetTag());
        Assert.Equal("firefly (honkai: star rail) (流萤 (honkai: star rail))", alias.ToString());

        Assert.Empty(catalog.SearchAutocomplete("missing"));
        Assert.Empty(catalog.SearchAutocomplete("   "));
    }

    [Fact]
    public void SearchAutocompleteRanksPrefixMatchesFirstAndHonorsTheLimit()
    {
        CharacterTagCatalog catalog = Load(
            "alpha_one,,x",
            "the_alpha_two,,x",
            "alpha_three,,x");

        TagsDB.TagItem[] limited = catalog.SearchAutocomplete("alpha", 2);
        Assert.Equal(2, limited.Length);
        Assert.All(limited, item => Assert.StartsWith("alpha", item.GetTag()));

        TagsDB.TagItem[] all = catalog.SearchAutocomplete("alpha");
        Assert.Equal(3, all.Length);
        Assert.Equal("the alpha two", all[2].GetTag());
    }

    [Fact]
    public void ResolveByNameMatchesPrimaryTranslatedNameExactly()
    {
        CharacterTagCatalog catalog = Load("firefly_(honkai:_star_rail),流萤,honkai:_star_rail");

        Assert.Equal("firefly (honkai: star rail)", catalog.ResolveByName("流萤"));
        Assert.Equal("firefly (honkai: star rail)", catalog.ResolveByName(" 流萤 "));
        Assert.Null(catalog.ResolveByName("符玄"));
        Assert.Null(catalog.ResolveByName(null));
        Assert.Null(catalog.ResolveByName("  "));
    }
}
