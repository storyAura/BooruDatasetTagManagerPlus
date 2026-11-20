using Xunit;

namespace BooruDatasetTagManager.Tests;

[CollectionDefinition(nameof(PromptParserBracketTagTests), DisableParallelization = true)]
public sealed class PromptParserBracketTagCollection { }

// Issue #2: tags with literal brackets were split apart, weighted 1.1 and re-wrapped on every save.
[Collection(nameof(PromptParserBracketTagTests))]
public sealed class PromptParserBracketTagTests
{
    private static List<PromptParser.PromptItem> Parse(string prompt) =>
        PromptParser.ParsePrompt(prompt, fixTagsForWeight: true);

    [Fact]
    public void Character_tag_with_series_brackets_stays_one_unweighted_tag()
    {
        var items = Parse("1girl, solo, moskva (azur lane), blue hair, olivier (brown dust)(retired legend)(series)");

        Assert.Equal(new[] { "1girl", "solo", "moskva (azur lane)", "blue hair", "olivier (brown dust)(retired legend)(series)" },
            items.Select(i => i.Text));
        Assert.All(items, i => Assert.Equal(1f, i.Weight));
    }

    [Theory]
    [InlineData("(masterpiece:1.2)", "masterpiece", 1.2f)]
    [InlineData("((solo))", "solo", 1.21f)]
    [InlineData("[bad hands]", "bad hands", 1f / 1.1f)]
    [InlineData("(moskva (azur lane):1.3)", "moskva (azur lane)", 1.3f)]
    [InlineData("moskva \\(azur lane\\)", "moskva (azur lane)", 1f)]
    [InlineData("(o)_(o)", "(o) (o)", 1f)]
    [InlineData(":)", ":)", 1f)]
    public void Weight_syntax_is_only_read_when_it_wraps_the_whole_item(string raw, string text, float weight)
    {
        var item = Assert.Single(Parse(raw));
        Assert.Equal(text, item.Text);
        Assert.Equal(weight, item.Weight, 3);
    }

    [Theory]
    [InlineData("moskva (azur lane)", 1f, "moskva (azur lane)")]
    [InlineData("moskva (azur lane)", 1.1f, null)]
    [InlineData("(o)", 1f, null)]
    [InlineData("sky", 1.1f, null)]
    [InlineData("sky", 1.21f, null)]
    public void Save_and_reload_round_trips_without_growing_brackets(string tag, float weight, string? expectedSaved)
    {
        bool original = Program.Settings.FixTagsOnSaveLoad;
        Program.Settings.FixTagsOnSaveLoad = true;
        try
        {
            var current = new EditableTag(0, tag) { Weight = weight };
            for (int pass = 0; pass < 3; pass++)
            {
                string saved = current.ToString();
                if (expectedSaved != null)
                    Assert.Equal(expectedSaved, saved);
                var reloaded = Assert.Single(Parse(saved));
                Assert.Equal(tag, reloaded.Text);
                Assert.Equal(weight, reloaded.Weight, 3);
                current = new EditableTag(0, reloaded.Text) { Weight = reloaded.Weight };
            }
        }
        finally
        {
            Program.Settings.FixTagsOnSaveLoad = original;
        }
    }
}
