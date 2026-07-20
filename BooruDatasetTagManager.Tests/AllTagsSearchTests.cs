using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class AllTagsSearchTests
{
    // AddTag keeps the list alphabetically sorted (matching the grid), so
    // expected indexes below refer to the sorted order.
    private static AllTagsList CreateList(params string[] tags)
    {
        var list = new AllTagsList();
        foreach (string tag in tags)
            list.AddTag(tag);
        return list;
    }

    [Fact]
    public void PrefixMatchBeatsEarlierSubstringMatch()
    {
        // Sorted: ["black hair", "hair ornament", "smile"]. "hair" appears
        // inside "black hair" (index 0) first, but the prefix match
        // "hair ornament" (index 1) must win.
        AllTagsList list = CreateList("smile", "black hair", "hair ornament");

        Assert.Equal(1, list.FindTagBestMatch("hair", 0));
    }

    [Fact]
    public void SearchIsCaseInsensitiveAndFallsBackToContains()
    {
        // Sorted: ["long hair", "open mouth", "smile"].
        AllTagsList list = CreateList("long hair", "smile", "open mouth");

        Assert.Equal(2, list.FindTagBestMatch("SMI", 0));
        Assert.Equal(1, list.FindTagBestMatch("mouth", 0));
        Assert.Equal(-1, list.FindTagBestMatch("missing", 0));
    }

    [Fact]
    public void SearchWrapsAroundFromStartIndex()
    {
        AllTagsList list = CreateList("smile", "standing", "solo");

        // Starting after the last row wraps back to the first match.
        Assert.Equal(0, list.FindTagBestMatch("s", 3));
        // Starting mid-list finds the next match, not the first one.
        Assert.Equal(1, list.FindTagBestMatch("s", 1));
        // Empty input never matches.
        Assert.Equal(-1, list.FindTagBestMatch("", 0));
    }

    [Fact]
    public void AliasSetLocatesEnglishTagForChineseQuery()
    {
        // Sorted: ["long hair", "smile"]. The Chinese query matches no tag
        // text, so the row is found through the CSV alias set.
        AllTagsList list = CreateList("smile", "long hair");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "long hair" };

        Assert.Equal(0, list.FindTagBestMatch("头发", 0, aliases));
        Assert.Equal(-1, list.FindTagBestMatch("头发", 0, null));
    }

    [Fact]
    public void TranslationSubstringMatchBeatsAliasMatch()
    {
        // Sorted: ["black hair", "smile"].
        AllTagsList list = CreateList("smile", "black hair");
        list[1].SetTranslation("微笑");
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "black hair" };

        // "微" hits the translation of "smile" (index 1); the alias hit on
        // "black hair" (index 0) only wins when nothing else matches.
        Assert.Equal(1, list.FindTagBestMatch("微", 0, aliases));
    }

    [Fact]
    public void TagSubstringMatchBeatsTranslationMatch()
    {
        // Sorted: ["black hair", "smile"]; translation of "black hair"
        // contains "mile" too, but the tag substring on "smile" must win.
        AllTagsList list = CreateList("smile", "black hair");
        list[0].SetTranslation("mile marker");

        Assert.Equal(1, list.FindTagBestMatch("mile", 0, null));
    }
}
