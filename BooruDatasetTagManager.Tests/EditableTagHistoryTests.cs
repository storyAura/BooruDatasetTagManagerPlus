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
    public void ReplaceTagCommitsDanglingEditTransactionSoTextMirrorStaysInSync()
    {
        var tags = new EditableTagList(new[] { "old_tag", "other" });

        // Simulate a cell edit abandoned by a grid rebind: the transaction
        // stays open, so a later programmatic Tag set would skip the mirror.
        tags[0].BeginEdit();
        tags.ReplaceTag("old_tag", "new tag");

        Assert.True(tags.CheckSyncLists());
        Assert.Equal("new tag", tags.TextTags[0]);
    }

    [Fact]
    public void RemoveTagsCommitsDanglingEditTransactionBeforeRemoving()
    {
        var tags = new EditableTagList(new[] { "aaa", "bbb", "ccc" });

        tags[1].BeginEdit();
        tags[1].Tag = "changed"; // swallowed by the open transaction
        tags.RemoveTags(new[] { "ccc" }, storeHistory: false);

        Assert.True(tags.CheckSyncLists());
        Assert.Equal(new[] { "aaa", "changed" }, tags.TextTags);
    }

    [Fact]
    public void DesynchronizedMirrorSelfHealsInsteadOfThrowingAndCorruptingFurther()
    {
        var tags = new EditableTagList(new[] { "aaa", "bbb", "ccc" });

        // Historical bug: an open transaction swallowed the mirror update, and
        // the next mutation THREW from inside CollectionBase, whose rollback
        // re-inserted the removed item into the object list only (lists then
        // differed in length and every later edit crashed).
        tags[1].BeginEdit();
        tags[1].Tag = "changed";
        tags.RemoveAt(2);

        Assert.True(tags.CheckSyncLists());
        Assert.Equal(2, tags.Count);
        Assert.Equal(new[] { "aaa", "changed" }, tags.TextTags);
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
