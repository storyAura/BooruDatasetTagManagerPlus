using System.Globalization;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class PromptParserCultureTests
{
    private static List<PromptParser.PromptItem> ParseUnderCulture(string culture, string prompt)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return PromptParser.ParsePrompt(prompt, fixTagsForWeight: true);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Weighted_tag_parses_with_dot_decimal_in_any_culture(string culture)
    {
        // Regression: Convert.ToDouble(CurrentCulture) threw FormatException for
        // "(tag:1.1)" on decimal-comma locales while loading a dataset.
        var items = ParseUnderCulture(culture, "(sunset:1.5), sky");

        PromptParser.PromptItem weighted = items.Single(i => i.Text.Contains("sunset"));
        Assert.Equal(1.5f, weighted.Weight, 3);
    }

    [Fact]
    public void Malformed_weight_falls_back_to_neutral_instead_of_throwing()
    {
        var items = ParseUnderCulture("en-US", "(tag:1.2.3), other");

        PromptParser.PromptItem weighted = items.Single(i => i.Text.Contains("tag"));
        Assert.Equal(1.0f, weighted.Weight, 3);
    }

    [Fact]
    public void Unbalanced_closing_bracket_does_not_throw()
    {
        var items = ParseUnderCulture("en-US", "tag one), tag two");

        Assert.NotEmpty(items);
    }
}
