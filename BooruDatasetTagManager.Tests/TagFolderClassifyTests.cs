using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagFolderClassifyPlannerTests
{
    [Fact]
    public void Plan_leavesUnmatchedImagesInPlace()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\a.png", new[] { "solo" }, "")
        };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(images, new[] { "hatsune miku" });

        Assert.Empty(moves);
    }

    [Fact]
    public void Plan_sendsImagesWithEverySelectedTagToDefaultMixFolder()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\dump\a.png", new[] { "hatsune miku", "solo" }, "dump")
        };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(images, new[] { "hatsune miku" });

        TagFolderMove move = Assert.Single(moves);
        Assert.Equal(TagFolderClassifyPlanner.DefaultFolderName, move.DestRelativeFolder);
        Assert.Equal("a.png", move.DestFileName);
    }

    [Fact]
    public void Plan_requiresEverySelectedTag()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\a.png", new[] { "miku" }, ""),
            new TagFolderClassifyItem(@"C:\ds\b.png", new[] { "miku", "luka" }, "")
        };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(images, new[] { "miku", "luka" });

        TagFolderMove move = Assert.Single(moves);
        Assert.Equal(@"C:\ds\b.png", move.SourcePath);
        Assert.Equal(TagFolderClassifyPlanner.DefaultFolderName, move.DestRelativeFolder);
    }

    [Fact]
    public void Plan_usesCustomFolderNameAndDefaultsBlankToMix()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\a.png", new[] { "miku" }, "")
        };

        TagFolderMove named = Assert.Single(
            TagFolderClassifyPlanner.Plan(images, new[] { "miku" }, destFolderName: " school "));
        Assert.Equal("school", named.DestRelativeFolder);

        TagFolderMove blank = Assert.Single(
            TagFolderClassifyPlanner.Plan(images, new[] { "miku" }, destFolderName: "   "));
        Assert.Equal(TagFolderClassifyPlanner.DefaultFolderName, blank.DestRelativeFolder);
        Assert.Equal("Mix", TagFolderClassifyPlanner.ResolveDestFolderName("..."));
    }

    [Fact]
    public void Plan_skipsImageAlreadyInDestinationFolder()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\Mix\a.png", new[] { "hatsune miku" }, "Mix")
        };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(images, new[] { "hatsune miku" });

        Assert.Empty(moves);
    }

    [Fact]
    public void Sanitize_replacesInvalidFileNameCharacters()
    {
        Assert.Equal("foo_bar", TagFolderClassifyPlanner.SanitizeFolderName("foo:bar"));
        Assert.Equal(string.Empty, TagFolderClassifyPlanner.SanitizeFolderName("..."));
        Assert.Equal("foo_bar", TagFolderClassifyPlanner.ResolveDestFolderName("foo:bar"));
    }

    [Fact]
    public void AllocateUniqueFolderName_usesMix2WhenMixExists()
    {
        Assert.Equal("Mix", TagFolderClassifyPlanner.AllocateUniqueFolderName(null, Array.Empty<string>()));
        Assert.Equal("Mix_2", TagFolderClassifyPlanner.AllocateUniqueFolderName("Mix", new[] { "Mix" }));
        Assert.Equal("Mix_3", TagFolderClassifyPlanner.AllocateUniqueFolderName("", new[] { "Mix", "Mix_2" }));
        Assert.Equal("school_2", TagFolderClassifyPlanner.AllocateUniqueFolderName("school", new[] { "school" }));
        Assert.False(TagFolderClassifyPlanner.IsFolderNameFamily("Mixer", "Mix"));
        Assert.True(TagFolderClassifyPlanner.IsFolderNameFamily("Mix_10", "Mix"));
    }

    [Fact]
    public void Plan_sendsNewMatchesToMix2AndLeavesExistingMix()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\Mix\old.png", new[] { "miku" }, "Mix"),
            new TagFolderClassifyItem(@"C:\ds\dump\new.png", new[] { "miku" }, "dump")
        };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(
            images,
            new[] { "miku" },
            existingFolders: new[] { "Mix" });

        TagFolderMove move = Assert.Single(moves);
        Assert.Equal(@"C:\ds\dump\new.png", move.SourcePath);
        Assert.Equal("Mix_2", move.DestRelativeFolder);
    }

    [Fact]
    public void Plan_allocatesUniqueNamesOnCollision()
    {
        var images = new[]
        {
            new TagFolderClassifyItem(@"C:\ds\old\a.png", new[] { "miku" }, "old")
        };
        var occupied = new[] { "Mix/a.png" };

        IReadOnlyList<TagFolderMove> moves = TagFolderClassifyPlanner.Plan(images, new[] { "miku" }, occupied);

        TagFolderMove move = Assert.Single(moves);
        Assert.Equal("Mix", move.DestRelativeFolder);
        Assert.Equal("a_2.png", move.DestFileName);
    }
}

public sealed class TagFolderClassifyMoveTests
{
    [Fact]
    public void MoveImagesToFolders_movesSidecarAndRemapsInMemory()
    {
        using var temp = new TemporaryDirectory();
        string dump = Directory.CreateDirectory(Path.Combine(temp.Path, "dump")).FullName;
        string oldImage = CreateTaggedImage(dump, "one.png", "miku, solo");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
        var item = manager.DataSet[oldImage];
        bool changedBefore = manager.IsDataSetChanged();

        int moved = manager.MoveImagesToFolders(new[]
        {
            new TagFolderMove(oldImage, "miku", "one.png")
        });

        string newImage = Path.Combine(temp.Path, "miku", "one.png");
        Assert.Equal(1, moved);
        Assert.False(File.Exists(oldImage));
        Assert.True(File.Exists(newImage));
        Assert.True(File.Exists(Path.Combine(temp.Path, "miku", "one.txt")));
        Assert.False(Directory.Exists(dump));
        Assert.False(manager.DataSet.ContainsKey(oldImage));
        Assert.True(manager.DataSet.ContainsKey(newImage));
        Assert.Same(item, manager.DataSet[newImage]);
        Assert.Equal(newImage, item.ImageFilePath);
        Assert.Equal(newImage, item.Tags.OwnerImagePath);
        Assert.Equal(changedBefore, manager.IsDataSetChanged());
    }

    [Fact]
    public void MoveImagesToFolders_rejectsPathEscape()
    {
        using var temp = new TemporaryDirectory();
        string oldImage = CreateTaggedImage(temp.Path, "one.png", "miku");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Throws<ArgumentException>(() => manager.MoveImagesToFolders(new[]
        {
            new TagFolderMove(oldImage, "..", "one.png")
        }));
        Assert.True(File.Exists(oldImage));
    }

    [Fact]
    public void MoveImagesToFolders_clearsScopeWhenActiveFolderIsEmptied()
    {
        using var temp = new TemporaryDirectory();
        string dump = Directory.CreateDirectory(Path.Combine(temp.Path, "dump")).FullName;
        string oldImage = CreateTaggedImage(dump, "one.png", "miku");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
        manager.SetActiveFolder("dump");

        manager.MoveImagesToFolders(new[] { new TagFolderMove(oldImage, "miku", "one.png") });

        Assert.Null(manager.ActiveFolder);
        Assert.Equal(1, manager.GetActiveScopeCount());
    }

    [Fact]
    public void MoveImagesToFolders_rebuildsAllTagsWhenScopeFolderStillHasImages()
    {
        using var temp = new TemporaryDirectory();
        string dump = Directory.CreateDirectory(Path.Combine(temp.Path, "dump")).FullName;
        string stay = CreateTaggedImage(dump, "keep.png", "solo");
        string leave = CreateTaggedImage(dump, "go.png", "miku, solo");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
        manager.SetActiveFolder("dump");
        Assert.Equal(2, manager.AllTags.Cast<AllTagsItem>().Single(item => item.Tag == "solo").Count);

        manager.MoveImagesToFolders(new[] { new TagFolderMove(leave, "miku", "go.png") });

        Assert.Equal("dump", manager.ActiveFolder);
        Assert.Equal(1, manager.GetActiveScopeCount());
        Assert.Equal(stay, manager.GetScopedItems().Single().ImageFilePath);
        Assert.Equal(1, manager.AllTags.Cast<AllTagsItem>().Single(item => item.Tag == "solo").Count);
        Assert.DoesNotContain(manager.AllTags.Cast<AllTagsItem>(), item => item.Tag == "miku");
    }

    private static string CreateTaggedImage(string directory, string fileName, string tags)
    {
        string imagePath = Path.Combine(directory, fileName);
        using (var image = new Image<Rgba32>(4, 4))
        {
            image.Save(imagePath, new PngEncoder());
        }
        File.WriteAllText(Path.Combine(
            directory, Path.GetFileNameWithoutExtension(fileName) + ".txt"), tags);
        return imagePath;
    }
}
