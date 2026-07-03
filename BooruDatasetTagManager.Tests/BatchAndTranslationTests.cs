using System.ComponentModel;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class BatchAndTranslationTests
{
    [Fact]
    public void BatchUpdateRaisesSingleResetAndKeepsCountsCorrect()
    {
        var tags = new AllTagsList();
        tags.AddTag("shoes");
        tags.AddTag("dress");

        var events = new List<ListChangedEventArgs>();
        tags.ListChanged += (_, args) => events.Add(args);

        using (tags.BeginBatchUpdate())
        {
            tags.AddTag("shoes");
            tags.RemoveTag("dress");
            tags.AddTag("black shoes");
        }

        Assert.Single(events);
        Assert.Equal(ListChangedType.Reset, events[0].ListChangedType);
        Assert.Equal(2, tags.Cast<AllTagsItem>().Single(x => x.Tag == "shoes").Count);
        Assert.DoesNotContain(tags.Cast<AllTagsItem>(), x => x.Tag == "dress");
        Assert.Contains(tags.Cast<AllTagsItem>(), x => x.Tag == "black shoes");
    }

    [Fact]
    public void RemoveTagsDeletesDistinctTargetsInOneOperation()
    {
        var tags = new EditableTagList(new[] { "shoes", "dress", "hat", "shoes" });

        tags.RemoveTags(new[] { "shoes", "shoes", "hat" }, true);

        Assert.Equal(new[] { "dress" }, tags.TextTags);
        Assert.True(tags.CheckSyncLists());
    }

    [Fact]
    public async Task EditableTagTranslationDoesNotWriteStaleResultAfterRename()
    {
        var tags = new EditableTagList(new[] { "shoes", "dress" });
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var translating = tags.TranslateAllAsync(tag =>
            tag switch
            {
                "shoes" => release.Task,
                "black shoes" => Task.FromResult("黑鞋"),
                _ => Task.FromResult("连衣裙")
            });

        tags[0].Tag = "black shoes";
        release.SetResult("鞋子");
        await translating;

        Assert.Equal("black shoes", tags[0].Tag);
        Assert.Equal("黑鞋", tags[0].Translation);
        Assert.Equal("连衣裙", tags[1].Translation);
    }

    [Fact]
    public async Task AllTagsTranslationDoesNotWriteResultIntoReplacementItem()
    {
        var tags = new AllTagsList();
        tags.AddTag("shoes");
        tags.AddTag("dress");
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var translating = tags.TranslateAllAsync(tag =>
            tag switch
            {
                "shoes" => release.Task,
                "black shoes" => Task.FromResult("黑鞋"),
                _ => Task.FromResult("连衣裙")
            });

        tags.ChangeTag("shoes", "black shoes");
        release.SetResult("鞋子");
        await translating;

        Assert.Equal("黑鞋", tags.Cast<AllTagsItem>().Single(x => x.Tag == "black shoes").Translation);
        Assert.Equal("连衣裙", tags.Cast<AllTagsItem>().Single(x => x.Tag == "dress").Translation);
    }

    [Fact]
    public async Task TranslationManagerCoalescesConcurrentRequests()
    {
        using var temp = new TemporaryDirectory();
        var translator = new CountingTranslator();
        var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);

        var results = await Task.WhenAll(
            manager.TranslateAsync("black shoes"),
            manager.TranslateAsync("black shoes"),
            manager.TranslateAsync("black shoes"));

        Assert.All(results, result => Assert.Equal("译:black shoes", result));
        Assert.Equal(1, translator.CallCount);
    }

    [Fact]
    public void ReplacingWithExistingTagRemovesDuplicateAndKeepsListsSynchronized()
    {
        var tags = new EditableTagList(new[] { "shoes", "black shoes", "dress" });

        tags.ReplaceTag("shoes", "black shoes");

        Assert.Equal(new[] { "black shoes", "dress" }, tags.TextTags);
        Assert.True(tags.CheckSyncLists());
    }

    [Fact]
    public void QuickReplaceReplacesSameCategoryTagsBelowThresholdWithSelectedTag()
    {
        var items = new[]
        {
            new AllTagsItem("shoes") { Count = 20 },
            new AllTagsItem("black shoes") { Count = 15 },
            new AllTagsItem("brown shoes") { Count = 7 },
            new AllTagsItem("dress") { Count = 5 }
        };

        var replacements = QuickTagReplaceService.GetReplacementSourceTags(items, "black shoes", 30);

        Assert.Equal(new[] { "shoes", "brown shoes" }, replacements);
    }

    [Fact]
    public void QuickReplaceDoesNotReplaceTagsAtOrAboveThreshold()
    {
        var items = new[]
        {
            new AllTagsItem("shoes") { Count = 30 },
            new AllTagsItem("black shoes") { Count = 15 },
            new AllTagsItem("brown shoes") { Count = 7 }
        };

        var replacements = QuickTagReplaceService.GetReplacementSourceTags(items, "black shoes", 30);

        Assert.Equal(new[] { "brown shoes" }, replacements);
    }

    [Fact]
    public async Task FailedTranslationDoesNotOverwriteOtherItems()
    {
        var tags = new EditableTagList(new[] { "failed", "dress" });

        await tags.TranslateAllAsync(tag =>
            Task.FromResult(tag == "failed" ? null : "连衣裙"));

        Assert.Equal(string.Empty, tags[0].Translation);
        Assert.Equal("连衣裙", tags[1].Translation);
    }

    [Fact]
    public async Task TranslationLockIsReleasedAfterTranslatorThrows()
    {
        using var temp = new TemporaryDirectory();
        var translator = new FlakyTranslator();
        var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.TranslateAsync("first"));
        string result = await manager.TranslateAsync("second");

        Assert.Equal("译:second", result);
    }

    private sealed class CountingTranslator : AbstractTranslator
    {
        private int callCount;

        public int CallCount => callCount;

        public CountingTranslator() : base(TranslationService.GoogleTranslate)
        {
        }

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(30);
            return $"译:{text}";
        }

        public override void Dispose()
        {
        }
    }

    private sealed class FlakyTranslator : AbstractTranslator
    {
        private int callCount;

        public FlakyTranslator() : base(TranslationService.GoogleTranslate)
        {
        }

        public override Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            if (Interlocked.Increment(ref callCount) == 1)
                throw new InvalidOperationException("simulated failure");
            return Task.FromResult($"译:{text}");
        }

        public override void Dispose()
        {
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-tests-{Guid.NewGuid():N}");

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
