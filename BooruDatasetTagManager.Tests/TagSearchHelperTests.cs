using System.Collections.Generic;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagSearchHelperTests
{
    private static List<TagSearchItem> CreateSampleItems()
    {
        return new List<TagSearchItem>
        {
            new("1girl", "1个女孩"),
            new("solo", "单人"),
            new("blue hair", "蓝色头发"),
            new("long hair", "长发"),
            new("blue eyes", "蓝色眼睛"),
            new("smile", "微笑"),
            new("open mouth", "张嘴")
        };
    }

    [Fact]
    public void SequentialSearchVisitsCompoundAndExactMatchesInOrder()
    {
        var items = new List<TagSearchItem>
        {
            new("1girl"),
            new("thighband pantyhose"),
            new("pantyhose"),
            new("torn pantyhose")
        };

        // Searching for "pantyhose" finds first occurrence "thighband pantyhose" (index 1)
        int first = TagSearchHelper.FindBestMatch(items, "pantyhose", 0);
        Assert.Equal(1, first);

        // Next forward search finds exact "pantyhose" (index 2)
        int second = TagSearchHelper.FindBestMatch(items, "pantyhose", first + 1);
        Assert.Equal(2, second);

        // Next forward search finds "torn pantyhose" (index 3)
        int third = TagSearchHelper.FindBestMatch(items, "pantyhose", second + 1);
        Assert.Equal(3, third);

        // Wrapping around finds index 1 again
        int fourth = TagSearchHelper.FindBestMatch(items, "pantyhose", third + 1);
        Assert.Equal(1, fourth);
    }

    [Fact]
    public void WholeWordSearchMatchesExactTagOnly()
    {
        var items = new List<TagSearchItem>
        {
            new("1girl"),
            new("thighband pantyhose"),
            new("pantyhose"),
            new("torn pantyhose")
        };

        // With wholeWord = true, compound tags are skipped and only exact "pantyhose" matches
        int match = TagSearchHelper.FindBestMatch(items, "pantyhose", 0, wholeWord: true);
        Assert.Equal(2, match);
    }

    [Fact]
    public void ForwardAndBackwardNavigationWorks()
    {
        var items = CreateSampleItems();

        // Forward search for "blue" starting at 0: should find "blue hair" (index 2)
        int first = TagSearchHelper.FindBestMatch(items, "blue", 0, forward: true);
        Assert.Equal(2, first);

        // Next forward search starting from next row (3): should find "blue eyes" (index 4)
        int second = TagSearchHelper.FindBestMatch(items, "blue", first + 1, forward: true);
        Assert.Equal(4, second);

        // Backward search starting from index 3: should go back to "blue hair" (index 2)
        int prev = TagSearchHelper.FindBestMatch(items, "blue", 3, forward: false);
        Assert.Equal(2, prev);

        // Backward search starting from index 1: should wrap around and find "blue eyes" (index 4)
        int wrapPrev = TagSearchHelper.FindBestMatch(items, "blue", 1, forward: false);
        Assert.Equal(4, wrapPrev);
    }

    [Fact]
    public void WholeWordMatchingRespectsBoundaries()
    {
        var items = new List<TagSearchItem>
        {
            new("hair ornament"),
            new("hair"),
            new("long hair")
        };

        // Non-whole-word finds prefix "hair ornament" (index 0)
        int nonWhole = TagSearchHelper.FindBestMatch(items, "hair", 0, wholeWord: false);
        Assert.Equal(0, nonWhole);

        // Whole-word finds exact "hair" (index 1)
        int whole = TagSearchHelper.FindBestMatch(items, "hair", 0, wholeWord: true);
        Assert.Equal(1, whole);

        // Whole-word with no exact match returns -1
        int noMatch = TagSearchHelper.FindBestMatch(items, "hai", 0, wholeWord: true);
        Assert.Equal(-1, noMatch);
    }

    [Fact]
    public void MatchCaseRespected()
    {
        var items = new List<TagSearchItem>
        {
            new("Blue Hair"),
            new("blue eyes")
        };

        // Case-insensitive finds index 0
        Assert.Equal(0, TagSearchHelper.FindBestMatch(items, "blue", 0, matchCase: false));

        // Case-sensitive for "blue" skips "Blue Hair" and finds "blue eyes" (index 1)
        Assert.Equal(1, TagSearchHelper.FindBestMatch(items, "blue", 0, matchCase: true));

        // Case-sensitive for "Blue" finds "Blue Hair" (index 0)
        Assert.Equal(0, TagSearchHelper.FindBestMatch(items, "Blue", 0, matchCase: true));
    }

    [Fact]
    public void ChineseTranslationAndAliasMatchingWorks()
    {
        var items = CreateSampleItems();

        // Matches by translation substring: "微笑" -> "smile" (index 5)
        int transMatch = TagSearchHelper.FindBestMatch(items, "微", 0);
        Assert.Equal(5, transMatch);

        // Matches by alias set
        var aliases = new HashSet<string> { "solo" };
        int aliasMatch = TagSearchHelper.FindBestMatch(items, "单独一人", 0, aliasTags: aliases);
        Assert.Equal(1, aliasMatch);
    }

    [Fact]
    public void EmptyAndNotFoundHandledGracefully()
    {
        var items = CreateSampleItems();

        Assert.Equal(-1, TagSearchHelper.FindBestMatch(items, "", 0));
        Assert.Equal(-1, TagSearchHelper.FindBestMatch(items, "   ", 0));
        Assert.Equal(-1, TagSearchHelper.FindBestMatch(items, "non_existent_tag_xyz", 0));
        Assert.Equal(-1, TagSearchHelper.FindBestMatch((List<TagSearchItem>)null, "blue", 0));
        Assert.Equal(-1, TagSearchHelper.FindBestMatch(new List<TagSearchItem>(), "blue", 0));
    }
}
