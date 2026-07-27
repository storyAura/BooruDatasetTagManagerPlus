using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class AllTagsCategoryFilterTests
{
    private static AllTagsList CreateList(params string[] tags)
    {
        var list = new AllTagsList();
        foreach (string tag in tags)
            list.AddTag(tag);
        return list;
    }

    [Fact]
    public void CategoryFilterHidesNonMatchingAndClearingRestores()
    {
        AllTagsList list = CreateList("long hair", "smile", "blue eyes");

        list.SetCategoryFilter(tag => tag.EndsWith("hair"));

        Assert.Equal(1, list.Count);
        Assert.Equal("long hair", list[0].Tag);

        list.SetCategoryFilter(null);

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void AddTagWhileCategoryFilterActiveOnlyShowsMatches()
    {
        AllTagsList list = CreateList("long hair");
        list.SetCategoryFilter(tag => tag.EndsWith("hair"));

        list.AddTag("short hair");
        list.AddTag("open mouth");

        Assert.Equal(2, list.Count);
        Assert.Equal("long hair", list[0].Tag);
        Assert.Equal("short hair", list[1].Tag);

        list.SetCategoryFilter(null);

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void CategoryFilterCombinesWithTextFilter()
    {
        AllTagsList list = CreateList("long hair", "hair ornament", "smile");
        list.SetCategoryFilter(tag => tag != "hair ornament");

        list.Filter = "hair";

        Assert.Equal(1, list.Count);
        Assert.Equal("long hair", list[0].Tag);
    }

    [Fact]
    public void CategoryFilterCombinesWithCountFilter()
    {
        var list = new AllTagsList();
        list.AddTag("solo");
        list.AddTag("solo");
        list.AddTag("smile");

        list.SetCategoryFilter(tag => tag == "smile");
        list.SetFilterByCount(1);

        Assert.Equal(1, list.Count);
        Assert.Equal("smile", list[0].Tag);

        // "solo" matches the count but not the category: both must hold.
        list.SetFilterByCount(2);

        Assert.Equal(0, list.Count);
    }
}
