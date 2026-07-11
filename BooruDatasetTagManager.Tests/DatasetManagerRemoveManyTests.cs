using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class DatasetManagerRemoveManyTests
{
    [Fact]
    public void RemoveMany_uses_execute_bulk_mutation()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "BooruDatasetTagManager",
            "DatasetManager.cs"));

        int methodStart = source.IndexOf("public void RemoveMany(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0);
        int methodEnd = source.IndexOf("public Image GetImageFromFileWithCache", methodStart, StringComparison.Ordinal);
        Assert.True(methodEnd > methodStart);
        string methodBody = source.Substring(methodStart, methodEnd - methodStart);
        Assert.Contains("ExecuteBulkMutation", methodBody);
    }

    [Fact]
    public void RemoveMany_updates_dataset_and_all_tags_counts()
    {
        using var temp = new TemporaryDirectory();
        string image1 = CreateTaggedImage(temp.Path, "one.png", "tag1, tag2");
        string image2 = CreateTaggedImage(temp.Path, "two.png", "tag2, tag3");
        string image3 = CreateTaggedImage(temp.Path, "three.png", "tag1");

        var manager = new DatasetManager();
        manager.AddImages(new[] { image1, image2, image3 }, loadPreviewImages: false, readMetadata: false);

        Assert.Equal(3, manager.DataSet.Count);
        Assert.Equal(2, GetTagCount(manager.AllTags, "tag1"));
        Assert.Equal(2, GetTagCount(manager.AllTags, "tag2"));
        Assert.Equal(1, GetTagCount(manager.AllTags, "tag3"));

        manager.RemoveMany(new[] { image1, image2 });

        Assert.Single(manager.DataSet);
        Assert.True(manager.DataSet.ContainsKey(image3));
        Assert.Equal(1, GetTagCount(manager.AllTags, "tag1"));
        Assert.Equal(0, GetTagCount(manager.AllTags, "tag2"));
        Assert.Equal(0, GetTagCount(manager.AllTags, "tag3"));
    }

    [Fact]
    public void ClearWithoutTagNotifications_does_not_raise_tags_list_changed()
    {
        var tags = new EditableTagList(new[] { "shoes", "dress" });
        int changeCount = 0;
        tags.TagsListChanged += (_, _, _, _) => changeCount++;

        tags.ClearWithoutTagNotifications();

        Assert.Equal(0, changeCount);
        Assert.Empty(tags.TextTags);
    }

    private static string CreateTaggedImage(string root, string fileName, string prompt)
    {
        string imagePath = Path.Combine(root, fileName);
        using (var image = new Image<Rgba32>(8, 8))
        {
            image.Save(imagePath, new PngEncoder());
        }

        File.WriteAllText(Path.ChangeExtension(imagePath, ".txt"), prompt);
        return imagePath;
    }

    private static int GetTagCount(AllTagsList allTags, string tag)
    {
        return allTags.Cast<AllTagsItem>().FirstOrDefault(item => item.Tag == tag)?.Count ?? 0;
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BooruDatasetTagManager.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
