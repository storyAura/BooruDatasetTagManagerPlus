using Xunit;

namespace BooruDatasetTagManager.Tests;

public class EditableTagHistoryTests
{
    [Fact]
    public void PrevStateRestoresDeletedTag()
    {
        var tags = new EditableTagList(new[] { "holding food", "smile" });

        tags.RemoveAt(0);
        tags.PrevState();

        Assert.Equal(new[] { "holding food", "smile" }, tags.TextTags);
    }

    [Fact]
    public void PrevStateRestoresModifiedTag()
    {
        var tags = new EditableTagList(new[] { "holding food" });

        tags[0].BeginEdit();
        tags[0].Tag = "holding object";
        tags[0].EndEdit();
        tags.PrevState();

        Assert.Equal("holding food", tags.TextTags[0]);
    }

    [Fact]
    public void PrevStateRestoresAddedTag()
    {
        var tags = new EditableTagList(new[] { "holding food" });

        tags.AddTag("smile", true, DatasetManager.AddingType.Down);
        tags.PrevState();

        Assert.Equal(new[] { "holding food" }, tags.TextTags);
    }

    [Fact]
    public void PrevStateRestoresMovedTagOrder()
    {
        var tags = new EditableTagList(new[] { "holding food", "smile", "solo" });

        tags.Move(0, 2);
        tags.PrevState();

        Assert.Equal(new[] { "holding food", "smile", "solo" }, tags.TextTags);
    }

    [Fact]
    public void NextStateRestoresChangeAfterUndo()
    {
        var tags = new EditableTagList(new[] { "holding food", "smile" });

        tags.RemoveAt(0);
        tags.PrevState();
        tags.NextState();

        Assert.Equal(new[] { "smile" }, tags.TextTags);
    }
}
