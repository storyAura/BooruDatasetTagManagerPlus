using System.Drawing;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class SimilarImageFinderTests
{
    /// <summary>Vertical black/white stripes, one stripe per hash column.</summary>
    private static Bitmap MakeStripes(int width, int height, Color dark, Color bright)
    {
        var bmp = new Bitmap(width, height);
        int stripe = width / 9;
        using var g = Graphics.FromImage(bmp);
        for (int i = 0; i < 9; i++)
        {
            using var brush = new SolidBrush(i % 2 == 0 ? dark : bright);
            g.FillRectangle(brush, i * stripe, 0, stripe, height);
        }
        return bmp;
    }

    /// <summary>Smooth left-to-right brightness gradient (no sharp edges).</summary>
    private static Bitmap MakeGradient(int width, int height)
    {
        var bmp = new Bitmap(width, height);
        for (int x = 0; x < width; x++)
        {
            int v = x * 255 / (width - 1);
            for (int y = 0; y < height; y++)
                bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
        return bmp;
    }

    [Fact]
    public void SameImageHashesIdentically()
    {
        using var image = MakeStripes(90, 80, Color.Black, Color.White);
        Assert.Equal(SimilarImageFinder.ComputeDHash(image), SimilarImageFinder.ComputeDHash(image));
    }

    [Fact]
    public void ResizedImageStaysSimilar()
    {
        using var large = MakeStripes(90, 80, Color.Black, Color.White);
        using var small = MakeStripes(45, 40, Color.Black, Color.White);
        int distance = SimilarImageFinder.HammingDistance(
            SimilarImageFinder.ComputeDHash(large), SimilarImageFinder.ComputeDHash(small));
        Assert.True(distance <= 6, $"distance {distance}");
    }

    [Fact]
    public void BrightnessShiftStaysSimilar()
    {
        using var normal = MakeStripes(90, 80, Color.FromArgb(80, 80, 80), Color.FromArgb(170, 170, 170));
        using var brighter = MakeStripes(90, 80, Color.FromArgb(140, 140, 140), Color.FromArgb(230, 230, 230));
        int distance = SimilarImageFinder.HammingDistance(
            SimilarImageFinder.ComputeDHash(normal), SimilarImageFinder.ComputeDHash(brighter));
        Assert.True(distance <= 4, $"distance {distance}");
    }

    [Fact]
    public void DifferentImagesAreFarApart()
    {
        using var stripes = MakeStripes(90, 80, Color.Black, Color.White);
        using var gradient = MakeGradient(90, 80);
        int distance = SimilarImageFinder.HammingDistance(
            SimilarImageFinder.ComputeDHash(stripes), SimilarImageFinder.ComputeDHash(gradient));
        Assert.True(distance > 16, $"distance {distance}");
    }

    [Fact]
    public void HammingDistanceCountsDifferingBits()
    {
        Assert.Equal(0, SimilarImageFinder.HammingDistance(0UL, 0UL));
        Assert.Equal(2, SimilarImageFinder.HammingDistance(0b1011UL, 0b0001UL));
        Assert.Equal(64, SimilarImageFinder.HammingDistance(0UL, ulong.MaxValue));
    }

    [Fact]
    public void GroupBySimilarityClustersAndDropsSingletons()
    {
        string[] items = { "a", "b", "far", "c", "d" };
        ulong[] hashes = { 0UL, 1UL, 0x00FF00FF00FF00FFUL, ulong.MaxValue, ulong.MaxValue ^ 2UL };

        var groups = SimilarImageFinder.GroupBySimilarity(items, hashes, maxDistance: 1);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "a", "b" }, groups[0]);
        Assert.Equal(new[] { "c", "d" }, groups[1]);
    }

    [Fact]
    public void GroupBySimilarityValidatesArguments()
    {
        Assert.Throws<System.ArgumentException>(
            () => SimilarImageFinder.GroupBySimilarity(new[] { "a" }, new ulong[] { 1, 2 }, 5));
        Assert.Throws<System.ArgumentOutOfRangeException>(
            () => SimilarImageFinder.GroupBySimilarity(new[] { "a" }, new ulong[] { 1 }, -1));
    }
}
