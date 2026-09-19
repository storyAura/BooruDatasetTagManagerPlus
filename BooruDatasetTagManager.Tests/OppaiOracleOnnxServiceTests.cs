using System.Drawing;
using System.Drawing.Imaging;
using BooruDatasetTagManager;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OppaiOracleOnnxServiceTests
{
    [Fact]
    public void ParseLines_aligns_by_tag_id_and_keeps_underscores()
    {
        string[] lines =
        {
            "tag_id,name,category",
            "0,<PAD>,0",
            "1,<UNK>,0",
            "2,long_hair,0",
            "3,rating:explicit,0",
            "4,1girl,0"
        };

        List<string> labels = OppaiOracleSelectedTagsCsvLoader.ParseLines(lines);

        Assert.Equal(5, labels.Count);
        Assert.Equal("<PAD>", labels[0]);
        Assert.Equal("<UNK>", labels[1]);
        Assert.Equal("long_hair", labels[2]);
        Assert.Equal("rating:explicit", labels[3]);
        Assert.Equal("1girl", labels[4]);
    }

    [Fact]
    public void CollectTags_skips_empty_names_rating_and_composited_gray_background()
    {
        string[] labels = { "<PAD>", "1girl", "rating:general", "gray_background", "solo" };
        float[] probabilities = { 0.99f, 0.91f, 0.95f, 0.93f, 0.40f };

        IReadOnlyList<AutoTagProviderItem> composited = OppaiOracleOnnxService.CollectTags(
            labels, probabilities, 0.5, wasComposited: true);
        Assert.Equal(new[] { "1girl" }, composited.Select(item => item.Tag).ToArray());

        IReadOnlyList<AutoTagProviderItem> opaque = OppaiOracleOnnxService.CollectTags(
            labels, probabilities, 0.5, wasComposited: false);
        Assert.Equal(new[] { "gray_background", "1girl" }, opaque.Select(item => item.Tag).ToArray());
    }

    [Fact]
    public void ShouldEmitTag_matches_space_filters()
    {
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag(null, false));
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag(" ", false));
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag("<PAD>", false));
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag("<UNK>", false));
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag("rating:sensitive", false));
        Assert.False(OppaiOracleOnnxService.ShouldEmitTag("gray_background", true));
        Assert.True(OppaiOracleOnnxService.ShouldEmitTag("gray_background", false));
        Assert.True(OppaiOracleOnnxService.ShouldEmitTag("long_hair", true));
    }

    [Fact]
    public void ResolveIoNames_prefers_pixel_values_mask_and_probabilities()
    {
        (string pixel, string mask, string output) = OppaiOracleOnnxService.ResolveIoNames(
            new[] { "padding_mask", "pixel_values" },
            new[] { "other", "probabilities" });

        Assert.Equal("pixel_values", pixel);
        Assert.Equal("padding_mask", mask);
        Assert.Equal("probabilities", output);
    }

    [Fact]
    public void Normalize_matches_mean_half_std_half()
    {
        Assert.Equal(-1f, OppaiOracleImagePreprocessor.Normalize(0), 5);
        Assert.Equal(1f, OppaiOracleImagePreprocessor.Normalize(255), 5);
        Assert.Equal(0f, OppaiOracleImagePreprocessor.Normalize(128), 2);
    }

    [Fact]
    public void LayoutLetterbox_does_not_upscale_and_centers_pad()
    {
        (int left, int top, int width, int height) = OppaiOracleImagePreprocessor.LayoutLetterbox(4, 4, 8);
        Assert.Equal(4, width);
        Assert.Equal(4, height);
        Assert.Equal(2, left);
        Assert.Equal(2, top);

        (left, top, width, height) = OppaiOracleImagePreprocessor.LayoutLetterbox(16, 8, 8);
        Assert.Equal(8, width);
        Assert.Equal(4, height);
        Assert.Equal(0, left);
        Assert.Equal(2, top);
    }

    [Fact]
    public void Create_packs_rgb_nchw_and_marks_gray_pad_in_mask()
    {
        const int canvas = 8;
        using var source = new Bitmap(4, 4, PixelFormat.Format24bppRgb);
        using (Graphics graphics = Graphics.FromImage(source))
            graphics.Clear(Color.FromArgb(255, 0, 0));

        OppaiOraclePreprocessResult result = OppaiOracleImagePreprocessor.Create(source, canvas);

        Assert.Equal(new[] { 1, 3, canvas, canvas }, result.PixelValues.Dimensions.ToArray());
        Assert.Equal(new[] { 1, canvas, canvas }, result.PaddingMask.Dimensions.ToArray());
        Assert.False(result.WasComposited);

        Assert.False(result.PaddingMask[0, 3, 3]);
        Assert.Equal(1f, result.PixelValues[0, 0, 3, 3], 3);
        Assert.Equal(-1f, result.PixelValues[0, 1, 3, 3], 3);
        Assert.Equal(-1f, result.PixelValues[0, 2, 3, 3], 3);

        Assert.True(result.PaddingMask[0, 0, 0]);
        Assert.Equal(OppaiOracleImagePreprocessor.Normalize(114), result.PixelValues[0, 0, 0, 0], 4);
        Assert.Equal(OppaiOracleImagePreprocessor.Normalize(114), result.PixelValues[0, 1, 0, 0], 4);
        Assert.Equal(OppaiOracleImagePreprocessor.Normalize(114), result.PixelValues[0, 2, 0, 0], 4);
    }

    [Fact]
    public void HasAnyTransparency_detects_alpha_and_ignores_opaque()
    {
        using var opaque = new Bitmap(2, 2, PixelFormat.Format24bppRgb);
        Assert.False(OppaiOracleImagePreprocessor.HasAnyTransparency(opaque));

        using var transparent = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        transparent.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
        transparent.SetPixel(1, 0, Color.FromArgb(255, 0, 255, 0));
        transparent.SetPixel(0, 1, Color.FromArgb(255, 0, 0, 255));
        transparent.SetPixel(1, 1, Color.FromArgb(255, 0, 0, 0));
        Assert.True(OppaiOracleImagePreprocessor.HasAnyTransparency(transparent));
    }

    [Fact]
    public void GetById_falls_back_to_v1_1()
    {
        Assert.Equal(OppaiOracleOnnxService.V1Id, OppaiOracleOnnxService.GetById(OppaiOracleOnnxService.V1Id).Id);
        Assert.Equal(OppaiOracleOnnxService.V11Id, OppaiOracleOnnxService.GetById("missing").Id);
    }
}
