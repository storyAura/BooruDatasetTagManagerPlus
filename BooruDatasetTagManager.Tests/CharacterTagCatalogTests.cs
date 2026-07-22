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
}
