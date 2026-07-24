using System.Drawing;
using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Size = System.Drawing.Size;
using PointF = System.Drawing.PointF;

namespace BooruDatasetTagManager.Tests;

public sealed class PreviewCanvasMathTests
{
    [Fact]
    public void FitZoomUsesTheSmallerRatioAndSurvivesDegenerateSizes()
    {
        Assert.Equal(0.5f, PreviewCanvasMath.FitZoom(new Size(100, 200), new Size(200, 100)));
        Assert.Equal(2f, PreviewCanvasMath.FitZoom(new Size(200, 200), new Size(100, 50)));
        Assert.Equal(1f, PreviewCanvasMath.FitZoom(new Size(0, 0), new Size(100, 100)));
        Assert.Equal(1f, PreviewCanvasMath.FitZoom(new Size(100, 100), Size.Empty));
    }

    [Fact]
    public void CenterOffsetCentersTheScaledImage()
    {
        PointF offset = PreviewCanvasMath.CenterOffset(new Size(200, 100), new Size(100, 100), 0.5f);
        Assert.Equal(75f, offset.X);
        Assert.Equal(25f, offset.Y);
    }

    [Fact]
    public void ZoomAroundPointKeepsTheImagePointUnderTheCursor()
    {
        var cursor = new PointF(120f, 80f);
        var offset = new PointF(10f, 20f);
        const float oldZoom = 1f;
        const float newZoom = 2.5f;
        float imageX = (cursor.X - offset.X) / oldZoom;
        float imageY = (cursor.Y - offset.Y) / oldZoom;

        PointF moved = PreviewCanvasMath.ZoomAroundPoint(cursor, offset, oldZoom, newZoom);

        Assert.Equal(imageX, (cursor.X - moved.X) / newZoom, 3);
        Assert.Equal(imageY, (cursor.Y - moved.Y) / newZoom, 3);
    }

    [Fact]
    public void ClampOffsetCentersSmallAxesAndForbidsGapsOnLargeAxes()
    {
        var viewport = new Size(100, 100);
        var image = new Size(400, 40);

        PointF clamped = PreviewCanvasMath.ClampOffset(viewport, image, 1f, new PointF(50f, -30f));

        Assert.Equal(0f, clamped.X);
        Assert.Equal(30f, clamped.Y);

        clamped = PreviewCanvasMath.ClampOffset(viewport, image, 1f, new PointF(-500f, 0f));
        Assert.Equal(-300f, clamped.X);
    }

    [Fact]
    public void ClampZoomHonorsBounds()
    {
        Assert.Equal(PreviewCanvasMath.MinZoom, PreviewCanvasMath.ClampZoom(0.001f));
        Assert.Equal(PreviewCanvasMath.MaxZoom, PreviewCanvasMath.ClampZoom(100f));
        Assert.Equal(1.5f, PreviewCanvasMath.ClampZoom(1.5f));
    }
}

public sealed class DanbooruWikiExamplePostTests
{
    [Fact]
    public void ExtractExamplePostIdsParsesInOrderDedupesAndCaps()
    {
        const string body = "Examples\n\n"
            + "* Post #9616575\n"
            + "* post #7467939: Hime cut\n"
            + "Also see post #9616575 again and Post #7525572.\n";

        Assert.Equal(new[] { 9616575, 7467939, 7525572 },
            DanbooruWikiClient.ExtractExamplePostIds(body, 4));
        Assert.Equal(new[] { 9616575, 7467939 },
            DanbooruWikiClient.ExtractExamplePostIds(body, 2));
    }

    [Fact]
    public void ExtractExamplePostIdsHandlesEmptyAndPostFreeBodies()
    {
        Assert.Empty(DanbooruWikiClient.ExtractExamplePostIds(null, 4));
        Assert.Empty(DanbooruWikiClient.ExtractExamplePostIds("no examples here", 4));
        Assert.Empty(DanbooruWikiClient.ExtractExamplePostIds("post #123", 0));
    }

    [Fact]
    public void TrimToIntroSectionKeepsOnlyTextBeforeTheFirstHeader()
    {
        const string body = "Hair around shoulder length.\n\nThe wolf cut is common.\n\n"
            + "h4. Examples\n\n* post #7272717\n\nh4#length. Hair lengths\n\n* bald";

        Assert.Equal("Hair around shoulder length.\n\nThe wolf cut is common.",
            DanbooruWikiClient.TrimToIntroSection(body));
    }

    [Fact]
    public void TrimToIntroSectionFallsBackForHeaderlessAndHeaderFirstBodies()
    {
        Assert.Equal("plain text only", DanbooruWikiClient.TrimToIntroSection("plain text only"));
        const string headerFirst = "h4. Examples\n\n* post #1";
        Assert.Equal(headerFirst, DanbooruWikiClient.TrimToIntroSection(headerFirst));
        Assert.Equal(string.Empty, DanbooruWikiClient.TrimToIntroSection(""));
    }

    [Theory]
    [InlineData("https://cdn.donmai.us/preview/ab/cd/abcd.jpg", true)]
    [InlineData("https://danbooru.donmai.us/data/x.png", true)]
    [InlineData("https://donmai.us/x.png", true)]
    [InlineData("http://cdn.donmai.us/x.png", false)]
    [InlineData("https://evil.example.com/x.png", false)]
    [InlineData("https://donmai.us.evil.com/x.png", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void PreviewDownloadsOnlyAllowHttpsDanbooruHosts(string url, bool expected)
    {
        // NET-02: preview URLs come from API JSON and must not be able to
        // point the client at arbitrary hosts or plain HTTP.
        Assert.Equal(expected, DanbooruWikiClient.IsAllowedPreviewUrl(url));
    }
}

public sealed class DatasetFolderRenameTests
{
    [Fact]
    public void RenameFolderMovesDirectoryAndRemapsInMemoryPaths()
    {
        using var temp = new TemporaryDirectory();
        string dirA = Directory.CreateDirectory(Path.Combine(temp.Path, "1_alpha")).FullName;
        Directory.CreateDirectory(Path.Combine(temp.Path, "1_beta"));
        string oldImage = CreateTaggedImage(dirA, "one.png", "tag1, tag2");
        CreateTaggedImage(Path.Combine(temp.Path, "1_beta"), "two.png", "tag2");

        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
        Assert.Equal(2, manager.DataSet.Count);
        var item = manager.DataSet[oldImage];
        bool changedBefore = manager.IsDataSetChanged();
        manager.SetActiveFolder("1_alpha");

        string newRelative = manager.RenameFolder("1_alpha", "1_renamed");

        string newDir = Path.Combine(temp.Path, "1_renamed");
        string newImage = Path.Combine(newDir, "one.png");
        Assert.Equal("1_renamed", newRelative);
        Assert.False(Directory.Exists(dirA));
        Assert.True(File.Exists(newImage));
        Assert.True(File.Exists(Path.Combine(newDir, "one.txt")));
        Assert.False(manager.DataSet.ContainsKey(oldImage));
        Assert.True(manager.DataSet.ContainsKey(newImage));
        Assert.Same(item, manager.DataSet[newImage]);
        Assert.Equal(newImage, item.ImageFilePath);
        Assert.Equal(newImage, item.Tags.OwnerImagePath);
        Assert.StartsWith(newDir, item.TextFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1_renamed", manager.ActiveFolder);
        Assert.Equal(changedBefore, manager.IsDataSetChanged());
    }

    [Fact]
    public void SetActiveFoldersScopesTheUnionOfSelectedFolders()
    {
        using var temp = new TemporaryDirectory();
        CreateTaggedImage(Directory.CreateDirectory(Path.Combine(temp.Path, "1_alpha")).FullName, "a.png", "tag_a");
        CreateTaggedImage(Directory.CreateDirectory(Path.Combine(temp.Path, "1_beta")).FullName, "b.png", "tag_b");
        CreateTaggedImage(Directory.CreateDirectory(Path.Combine(temp.Path, "1_gamma")).FullName, "c.png", "tag_c");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        // Browser Ctrl/Shift folder multi-select: scope = union of the set,
        // so the AllTags counts can follow the selection.
        manager.SetActiveFolders(new[] { "1_alpha", "1_beta" });

        Assert.Equal(2, manager.GetActiveScopeCount());
        Assert.Equal("1_alpha", manager.ActiveFolder);
        Assert.Equal(new[] { "1_alpha", "1_beta" }, manager.ActiveFolders);
        Assert.Equal(
            new[] { "a.png", "b.png" },
            manager.GetScopedItems()
                .Select(item => Path.GetFileName(item.ImageFilePath))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        manager.SetActiveFolders(null);
        Assert.Equal(3, manager.GetActiveScopeCount());
        Assert.Null(manager.ActiveFolder);
    }

    [Fact]
    public void RenameFolderRejectsInvalidAndConflictingNames()
    {
        using var temp = new TemporaryDirectory();
        CreateTaggedImage(Directory.CreateDirectory(Path.Combine(temp.Path, "1_alpha")).FullName, "one.png", "tag1");
        CreateTaggedImage(Directory.CreateDirectory(Path.Combine(temp.Path, "1_beta")).FullName, "two.png", "tag2");
        var manager = new DatasetManager();
        Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));

        Assert.Throws<ArgumentException>(() => manager.RenameFolder("1_alpha", "  "));
        Assert.Throws<ArgumentException>(() => manager.RenameFolder("1_alpha", "bad/name"));
        Assert.Throws<ArgumentException>(() => manager.RenameFolder("1_alpha", "1_beta"));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "1_alpha")));
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
