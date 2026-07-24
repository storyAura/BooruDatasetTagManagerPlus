using Xunit;

namespace BooruDatasetTagManager.Tests;

public class ChineseTagLookupTests
{
    [Fact]
    public void ResolveInputReturnsEnglishTagForSimplifiedChinese()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "long_hair,长发",
            "smile,微笑|笑容"
        });

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Equal("long hair", lookup.ResolveInput("长发", "zh-CN"));
        Assert.Equal("smile", lookup.ResolveInput("笑容", "zh-CN"));
    }

    [Fact]
    public void ResolveInputIgnoresChineseLookupOutsideSimplifiedChinese()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "long_hair,长发");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Equal("长发", lookup.ResolveInput("长发", "en-US"));
    }

    [Fact]
    public void CreateAutocompleteValuesAddsChineseAliasesThatResolveToEnglishTags()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "long_hair,长发",
            "black_shoes,黑鞋|黑色鞋子"
        });

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
        var baseValues = new[]
        {
            ChineseTagLookupService.CreateTagItem("long hair", 120)
        };

        var values = lookup.CreateAutocompleteValues(baseValues, "zh-CN");
        var chineseAlias = values.Single(item => item.Tag == "长发");
        var newChineseAlias = values.Single(item => item.Tag == "黑色鞋子");

        Assert.True(chineseAlias.IsAlias);
        Assert.Equal("long hair", chineseAlias.GetTag());
        Assert.Equal(120, chineseAlias.Count);
        Assert.Equal("black shoes", newChineseAlias.GetTag());
    }

    [Fact]
    public void CreateAutocompleteValuesBackfillsEnglishTagsMissingFromBaseValues()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "long_hair,长发",
            "black_shoes,黑鞋"
        });

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        // Empty base values = the user has no Tags/*.csv autocomplete files;
        // English typing must still complete from the dictionary entries.
        var values = lookup.CreateAutocompleteValues(Array.Empty<TagsDB.TagItem>(), "zh-CN");

        var english = values.Single(item => item.Tag == "long hair");
        Assert.False(english.IsAlias);
        Assert.Equal("long hair (长发)", english.ToString());
        Assert.Contains(values, item => item.Tag == "black shoes" && !item.IsAlias);
    }

    [Fact]
    public void CreateAutocompleteValuesDoesNotDuplicateEnglishTagsAlreadyInBaseValues()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "long_hair,长发");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
        var baseValues = new[] { ChineseTagLookupService.CreateTagItem("long hair", 42) };

        var values = lookup.CreateAutocompleteValues(baseValues, "zh-CN");

        var english = values.Single(item => item.Tag == "long hair");
        Assert.Equal(42, english.Count);
    }

    [Fact]
    public void CreateAutocompleteValuesAddsChineseDisplayTextToEnglishTags()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "red_theme,\u7ea2\u8272\u4e3b\u9898");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
        var baseValue = ChineseTagLookupService.CreateTagItem("red theme", 0);

        var values = lookup.CreateAutocompleteValues(new[] { baseValue }, "zh-CN");

        Assert.Equal("red theme", values[0].GetTag());
        Assert.Equal("red theme (\u7ea2\u8272\u4e3b\u9898)", values[0].ToString());
    }

    [Fact]
    public void CreateAutocompleteValuesDoesNotAddChineseDisplayOutsideSimplifiedChinese()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "red_theme,\u7ea2\u8272\u4e3b\u9898");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
        var baseValue = ChineseTagLookupService.CreateTagItem("red theme", 0);

        var values = lookup.CreateAutocompleteValues(new[] { baseValue }, "en-US");

        Assert.Equal("red theme", values[0].GetTag());
        Assert.True(string.IsNullOrEmpty(values[0].AutocompleteDisplayText));
    }

    [Fact]
    public void GetChineseNameForEnglishTagReturnsFirstChineseName()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "red_theme,\u7ea2\u8272\u4e3b\u9898|\u7ea2\u8272\u98ce\u683c");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Equal("\u7ea2\u8272\u4e3b\u9898", lookup.GetChineseNameForEnglishTag("red theme", "zh-CN"));
        Assert.Equal(string.Empty, lookup.GetChineseNameForEnglishTag("red theme", "en-US"));
    }

    [Fact]
    public void SearchAutocompleteValuesReturnsChineseContainsMatchesWithEnglishDisplay()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "holding_food,\u62ff\u7740\u98df\u7269",
            "holding_object,\u62ff\u7740\u7269\u4f53",
            "food_on_table,\u684c\u4e0a\u7684\u98df\u7269"
        });

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
        var baseValues = new[]
        {
            ChineseTagLookupService.CreateTagItem("holding food", 30),
            ChineseTagLookupService.CreateTagItem("holding object", 15)
        };

        var matches = lookup.SearchAutocompleteValues("\u62ff\u7740", baseValues, "zh-CN");

        Assert.Contains(matches, item => item.GetTag() == "holding food" && item.ToString() == "holding food (\u62ff\u7740\u98df\u7269)");
        Assert.Contains(matches, item => item.GetTag() == "holding object" && item.ToString() == "holding object (\u62ff\u7740\u7269\u4f53)");
        Assert.DoesNotContain(matches, item => item.GetTag() == "food on table");
    }

    [Fact]
    public void SearchAutocompleteValuesSupportsChineseSubstringButResolveInputRequiresExactMatch()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "holding_food,\u62ff\u7740\u98df\u7269");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Single(lookup.SearchAutocompleteValues("\u98df\u7269", Array.Empty<TagsDB.TagItem>(), "zh-CN"));
        Assert.Equal("\u62ff\u7740", lookup.ResolveInput("\u62ff\u7740", "zh-CN"));
        Assert.Equal("holding food", lookup.ResolveInput("\u62ff\u7740\u98df\u7269", "zh-CN"));
    }

    [Fact]
    public void SearchAutocompleteValuesIgnoresChineseLookupOutsideSimplifiedChinese()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "holding_food,\u62ff\u7740\u98df\u7269");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Empty(lookup.SearchAutocompleteValues("\u62ff\u7740", Array.Empty<TagsDB.TagItem>(), "en-US"));
    }

    [Fact]
    public void FindEnglishTagsByChineseNameMatchesSynonymSubstrings()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllLines(csvPath, new[]
        {
            "long_hair,长发|长头发",
            "black_hair,黑发|黑色头发",
            "smile,微笑"
        });

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        // "头发" only appears in synonym names, never as the primary name.
        var matches = lookup.FindEnglishTagsByChineseName("头发", "zh-CN");

        Assert.Contains("long hair", matches);
        Assert.Contains("black hair", matches);
        Assert.DoesNotContain("smile", matches);
    }

    [Fact]
    public void FindEnglishTagsByChineseNameIsEmptyOutsideSimplifiedChinese()
    {
        using var temp = new TemporaryDirectory();
        string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
        File.WriteAllText(csvPath, "long_hair,长发");

        var lookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);

        Assert.Empty(lookup.FindEnglishTagsByChineseName("长发", "en-US"));
        Assert.Empty(lookup.FindEnglishTagsByChineseName("  ", "zh-CN"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-chinese-tag-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
