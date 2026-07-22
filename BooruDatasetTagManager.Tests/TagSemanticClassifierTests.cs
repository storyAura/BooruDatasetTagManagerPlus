using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagSemanticClassifierTests
{
    [Theory]
    [InlineData("green eyes", TagSemanticCategory.Eyes)]
    [InlineData("closed_eyes", TagSemanticCategory.Eyes)]
    [InlineData("twintails", TagSemanticCategory.Hair)]
    [InlineData("long hair", TagSemanticCategory.Hair)]
    [InlineData("wolf cut", TagSemanticCategory.Hair)]
    [InlineData("red shirt", TagSemanticCategory.Clothing)]
    [InlineData("sleeveless dress", TagSemanticCategory.Clothing)]
    [InlineData("sleeveless", TagSemanticCategory.Clothing)]
    [InlineData("long sleeves", TagSemanticCategory.Clothing)]
    [InlineData("cat hair ornament", TagSemanticCategory.Accessory)]
    [InlineData("hairclip", TagSemanticCategory.Accessory)]
    [InlineData("backpack", TagSemanticCategory.Accessory)]
    [InlineData("black bag", TagSemanticCategory.Accessory)]
    [InlineData("2girls", TagSemanticCategory.SubjectCount)]
    [InlineData("multiple girls", TagSemanticCategory.SubjectCount)]
    [InlineData("solo", TagSemanticCategory.SubjectCount)]
    [InlineData("6+girls", TagSemanticCategory.SubjectCount)]
    [InlineData("blush", TagSemanticCategory.Expression)]
    [InlineData("open mouth", TagSemanticCategory.Expression)]
    [InlineData("mole under mouth", TagSemanticCategory.Body)]
    [InlineData("mole", TagSemanticCategory.Body)]
    [InlineData("hug from behind", TagSemanticCategory.Action)]
    [InlineData("looking at viewer", TagSemanticCategory.Action)]
    [InlineData("red background", TagSemanticCategory.Background)]
    [InlineData("highres", TagSemanticCategory.Meta)]
    [InlineData("viola", TagSemanticCategory.Object)]
    [InlineData("stuffed animal", TagSemanticCategory.Object)]
    [InlineData("holding sword", TagSemanticCategory.Object)]
    [InlineData("white cat", TagSemanticCategory.Animal)]
    [InlineData("strawberry", TagSemanticCategory.Food)]
    [InlineData("upper body", TagSemanticCategory.Composition)]
    [InlineData("from behind", TagSemanticCategory.Composition)]
    [InlineData("monochrome", TagSemanticCategory.Style)]
    [InlineData("some unknown tag", TagSemanticCategory.General)]
    public void ClassifiesGeneralTagsByHeuristics(string tag, TagSemanticCategory expected)
    {
        Assert.Equal(expected, TagSemanticClassifier.Classify(tag, -1));
    }

    [Theory]
    [InlineData(4, TagSemanticCategory.Character)]
    [InlineData(3, TagSemanticCategory.Copyright)]
    [InlineData(1, TagSemanticCategory.Artist)]
    [InlineData(5, TagSemanticCategory.Meta)]
    public void DanbooruTypeWinsOverHeuristics(int type, TagSemanticCategory expected)
    {
        Assert.Equal(expected, TagSemanticClassifier.Classify("nakamachi arale", type));
    }

    [Fact]
    public void GeneralHasNoAccentWhileOthersDo()
    {
        Assert.Null(TagSemanticClassifier.GetAccent(TagSemanticCategory.General));
        Assert.NotNull(TagSemanticClassifier.GetAccent(TagSemanticCategory.Character));
        Assert.NotNull(TagSemanticClassifier.GetAccent(TagSemanticCategory.Clothing));
    }

    [Fact]
    public void SortByCategoryOrdersByRankThenAlphabetKeepingTheFixedPrefix()
    {
        var tags = new EditableTagList(new[]
        {
            "zzz trigger", "red shirt", "twintails", "2girls", "green eyes", "black shirt"
        });

        tags.SortByCategory(1, tag => (int)TagSemanticClassifier.Classify(tag, -1));

        Assert.Equal(new[]
        {
            "zzz trigger",   // protected prefix row stays first
            "2girls",        // SubjectCount
            "twintails",     // Hair
            "green eyes",    // Eyes
            "black shirt",   // Clothing (alphabetical within rank)
            "red shirt"
        }, tags.TextTags);
    }

    [Fact]
    public void SortByCategoryClampsAnOversizedFixedPrefix()
    {
        var tags = new EditableTagList(new[] { "red shirt", "2girls" });
        tags.SortByCategory(10, tag => (int)TagSemanticClassifier.Classify(tag, -1));
        Assert.Equal(new[] { "red shirt", "2girls" }, tags.TextTags);
    }
}
