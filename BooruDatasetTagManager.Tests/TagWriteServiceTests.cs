using BooruDatasetTagManager;
using Xunit;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager.Tests;

/// <summary>
/// Locks the write-mode semantics behind the ONNX/LLM taggers. The P0 trap:
/// SkipExistTagList must never touch an already-tagged image (callers now
/// pre-filter those and report them) but must still fill untagged ones.
/// </summary>
public class TagWriteServiceTests
{
    private static DataItem MakeItem(params string[] tags)
    {
        var item = new DataItem();
        if (tags.Length > 0)
            item.Tags.AddRange(tags, false);
        return item;
    }

    private static Wd14TaggerSettings SettingsWith(NetworkResultSetMode mode)
    {
        return new Wd14TaggerSettings { SetMode = mode };
    }

    [Fact]
    public void SkipExistingModeLeavesTaggedImagesUntouched()
    {
        DataItem item = MakeItem("1girl", "purple hair");

        TagWriteService.ApplyTagNames(item, new[] { "smile", "solo" },
            SettingsWith(NetworkResultSetMode.SkipExistTagList));

        Assert.Equal(new[] { "1girl", "purple hair" }, item.Tags.TextTags);
    }

    [Fact]
    public void SkipExistingModeFillsUntaggedImages()
    {
        DataItem item = MakeItem();

        TagWriteService.ApplyTagNames(item, new[] { "smile", "solo" },
            SettingsWith(NetworkResultSetMode.SkipExistTagList));

        Assert.Equal(new[] { "smile", "solo" }, item.Tags.TextTags);
    }

    [Fact]
    public void ReplacementModeOverwritesExistingTags()
    {
        DataItem item = MakeItem("old tag");

        TagWriteService.ApplyTagNames(item, new[] { "new tag" },
            SettingsWith(NetworkResultSetMode.AllWithReplacement));

        Assert.Equal(new[] { "new tag" }, item.Tags.TextTags);
    }

    [Fact]
    public void AdditionModeAppendsWithoutDuplicates()
    {
        DataItem item = MakeItem("1girl");

        TagWriteService.ApplyTagNames(item, new[] { "1girl", "smile" },
            SettingsWith(NetworkResultSetMode.OnlyNewWithAddition));

        Assert.Equal(new[] { "1girl", "smile" }, item.Tags.TextTags);
    }
}
