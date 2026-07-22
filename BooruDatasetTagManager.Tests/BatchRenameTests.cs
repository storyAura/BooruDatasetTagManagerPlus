using System.Text;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class BatchRenamePlannerTests
{
    [Fact]
    public void NumericNamesArePaddedAndCombineWithPrefixSuffix()
    {
        Assert.Equal(new[] { "img_001_v2", "img_002_v2", "img_003_v2" },
            BatchRenamePlanner.BuildNames(3, "img_", "_v2", BatchRenameNumbering.Numeric, 1, 3));
        Assert.Equal(new[] { "9", "10" },
            BatchRenamePlanner.BuildNames(2, "", "", BatchRenameNumbering.Numeric, 9, 1));
    }

    [Fact]
    public void LetterNamesRunExcelStyle()
    {
        Assert.Equal("a", BatchRenamePlanner.ToLetters(0));
        Assert.Equal("z", BatchRenamePlanner.ToLetters(25));
        Assert.Equal("aa", BatchRenamePlanner.ToLetters(26));
        Assert.Equal("ab", BatchRenamePlanner.ToLetters(27));
        Assert.Equal(new[] { "p_a", "p_b" },
            BatchRenamePlanner.BuildNames(2, "p_", "", BatchRenameNumbering.Letters, 1, 3));
    }

    [Fact]
    public void NoneModeKeepsOriginalNamesBetweenPrefixAndSuffix()
    {
        Assert.Equal(new[] { "x_one_y", "x_two_y" },
            BatchRenamePlanner.BuildNames(2, "x_", "_y", BatchRenameNumbering.None, 1, 3,
                new[] { "one", "two" }));
    }

    [Fact]
    public void InvalidPartsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            BatchRenamePlanner.BuildNames(1, "bad/", "", BatchRenameNumbering.Numeric, 1, 3));
        Assert.Throws<ArgumentException>(() =>
            BatchRenamePlanner.BuildNames(1, "", "", BatchRenameNumbering.None, 1, 3, new[] { "" }));
    }
}

public sealed class DatasetRenameImagesTests
{
    [Fact]
    public void RenamesImagesWithCaptionsAndRemapsMemoryEvenWhenNamesSwap()
    {
        using var temp = new TemporaryDirectory();
        string a = CreateTaggedImage(temp.Path, "one.png", "tag1");
        string b = CreateTaggedImage(temp.Path, "two.png", "tag2");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        // Swap the two names: only the two-phase temp dance makes this work.
        int renamed = manager.RenameImages(new[]
        {
            new KeyValuePair<string, string>(a, "two"),
            new KeyValuePair<string, string>(b, "one")
        });

        Assert.Equal(2, renamed);
        string newA = Path.Combine(temp.Path, "two.png");
        string newB = Path.Combine(temp.Path, "one.png");
        Assert.True(File.Exists(newA));
        Assert.True(File.Exists(newB));
        Assert.Equal("tag1", File.ReadAllText(Path.Combine(temp.Path, "two.txt")).Trim());
        Assert.Equal("tag2", File.ReadAllText(Path.Combine(temp.Path, "one.txt")).Trim());
        Assert.True(manager.DataSet.ContainsKey(newA));
        Assert.True(manager.DataSet.ContainsKey(newB));
        var item = manager.DataSet[newA];
        Assert.Equal("two", item.Name);
        Assert.Equal(newA, item.Tags.OwnerImagePath);
        Assert.EndsWith("two.txt", item.TextFilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsConflictsWithFilesOutsideTheRenameSetBeforeMovingAnything()
    {
        using var temp = new TemporaryDirectory();
        string a = CreateTaggedImage(temp.Path, "one.png", "tag1");
        CreateTaggedImage(temp.Path, "taken.png", "tag2");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Throws<ArgumentException>(() => manager.RenameImages(new[]
        {
            new KeyValuePair<string, string>(a, "taken")
        }));
        Assert.True(File.Exists(a));
        Assert.True(manager.DataSet.ContainsKey(a));
    }

    [Fact]
    public void RejectsDirectoryCollisionsOnTargetsUpFront()
    {
        using var temp = new TemporaryDirectory();
        string a = CreateTaggedImage(temp.Path, "one.png", "tag1");
        // DATA-01 trigger: File.Exists is false for directories, so a folder
        // named like the caption target used to pass the pre-check and fail
        // halfway through the move, splitting disk from memory.
        Directory.CreateDirectory(Path.Combine(temp.Path, "b.txt"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "c.png"));
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Throws<ArgumentException>(() => manager.RenameImages(new[]
        {
            new KeyValuePair<string, string>(a, "b")
        }));
        Assert.Throws<ArgumentException>(() => manager.RenameImages(new[]
        {
            new KeyValuePair<string, string>(a, "c")
        }));

        Assert.True(File.Exists(a));
        Assert.True(File.Exists(Path.Combine(temp.Path, "one.txt")));
        Assert.True(manager.DataSet.ContainsKey(a));
    }

    [Fact]
    public void RenamingAnImageWithoutCaptionRetargetsTheFutureSavePath()
    {
        using var temp = new TemporaryDirectory();
        string imagePath = Path.Combine(temp.Path, "one.png");
        using (var image = new Image<Rgba32>(4, 4))
        {
            image.Save(imagePath, new PngEncoder());
        }
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Equal(1, manager.RenameImages(new[]
        {
            new KeyValuePair<string, string>(imagePath, "two")
        }));

        string newImage = Path.Combine(temp.Path, "two.png");
        var item = manager.DataSet[newImage];
        // DATA-02: without this retarget a later save resurrected one.txt.
        Assert.EndsWith("two.txt", item.TextFilePath, StringComparison.OrdinalIgnoreCase);

        item.Tags.AddTag("new_tag", false);
        manager.SaveAll();
        Assert.True(File.Exists(Path.Combine(temp.Path, "two.txt")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "one.txt")));
    }

    private static string CreateTaggedImage(string directory, string fileName, string tags)
    {
        string imagePath = Path.Combine(directory, fileName);
        using (var image = new Image<Rgba32>(4, 4))
        {
            image.Save(imagePath, new PngEncoder());
        }
        File.WriteAllText(Path.Combine(
            directory, Path.GetFileNameWithoutExtension(fileName) + ".txt"), tags, new UTF8Encoding(false));
        return imagePath;
    }
}
