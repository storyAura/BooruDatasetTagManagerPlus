using System.Drawing;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class ImageEditorTests
{
    [Fact]
    public void DocumentTracksDirtyThroughUndoRedo()
    {
        using var document = new ImageEditorDocument(new Bitmap(10, 10));
        Assert.False(document.IsDirty);

        document.BeginStroke();
        document.DrawStrokeSegment(new Point(1, 1), new Point(8, 8), Color.Red, 2f, erase: false, eraseToTransparent: false);
        Assert.True(document.IsDirty);
        Assert.True(document.CanUndo);

        Assert.True(document.Undo());
        Assert.False(document.IsDirty);
        Assert.True(document.CanRedo);

        Assert.True(document.Redo());
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void CropRejectsFullImageAndAppliesPartialRectangles()
    {
        using var document = new ImageEditorDocument(new Bitmap(20, 10));
        Assert.False(document.ApplyCrop(new Rectangle(0, 0, 20, 10)));
        Assert.False(document.IsDirty);

        Assert.True(document.ApplyCrop(new Rectangle(5, 2, 10, 6)));
        Assert.Equal(new Size(10, 6), document.Image.Size);
        Assert.True(document.IsDirty);

        Assert.True(document.Undo());
        Assert.Equal(new Size(20, 10), document.Image.Size);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void RotateSwapsDimensionsAndUndoRestores()
    {
        using var document = new ImageEditorDocument(new Bitmap(30, 10));
        document.RotateFlip(RotateFlipType.Rotate90FlipNone);
        Assert.Equal(new Size(10, 30), document.Image.Size);
        Assert.True(document.Undo());
        Assert.Equal(new Size(30, 10), document.Image.Size);
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".webp", true)]
    [InlineData(".jpg", false)]
    [InlineData(".bmp", false)]
    [InlineData("", false)]
    public void TransparencySupportFollowsExtension(string extension, bool expected)
    {
        Assert.Equal(expected, ImageEditorSaveService.SupportsTransparency(extension));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    public void EncodeProducesDecodableImageOfSameSize(string extension)
    {
        using var bitmap = new Bitmap(12, 7);
        byte[] bytes = ImageEditorSaveService.Encode(bitmap, extension);
        using var decoded = SixLabors.ImageSharp.Image.Load(bytes);
        Assert.Equal(12, decoded.Width);
        Assert.Equal(7, decoded.Height);
    }

    [Fact]
    public void ScreenRectMapsToExactPixelSpanAtIntegralZoom()
    {
        // Regression: dragging screen x=10..20 at 100% zoom must select the
        // 10 image columns 10..19, not 11 — the exclusive Right/Bottom bounds
        // used to be fed through the inclusive floor mapping directly.
        var imageSize = new Size(100, 100);
        Rectangle drag = Rectangle.FromLTRB(10, 10, 20, 20);

        Rectangle mapped = ImageEditorCanvasMath.ScreenRectToImageRect(drag, 1f, PointF.Empty, imageSize);

        Assert.Equal(new Rectangle(10, 10, 10, 10), mapped);
    }

    [Fact]
    public void ScreenRectMappingHonorsZoomAndPanOffset()
    {
        var imageSize = new Size(100, 100);

        // 4x zoom: 40 screen pixels cover exactly 10 image pixels.
        Assert.Equal(
            new Rectangle(0, 0, 10, 10),
            ImageEditorCanvasMath.ScreenRectToImageRect(Rectangle.FromLTRB(0, 0, 40, 40), 4f, PointF.Empty, imageSize));

        // Panned view: the offset shifts the origin, not the size.
        Assert.Equal(
            new Rectangle(10, 10, 10, 10),
            ImageEditorCanvasMath.ScreenRectToImageRect(Rectangle.FromLTRB(110, 60, 120, 70), 1f, new PointF(100, 50), imageSize));
    }

    [Fact]
    public void ScreenRectMappingClampsToImageAndRejectsEmptyDrags()
    {
        var imageSize = new Size(20, 20);

        // Selection past the image edge is trimmed to the image.
        Assert.Equal(
            new Rectangle(15, 15, 5, 5),
            ImageEditorCanvasMath.ScreenRectToImageRect(Rectangle.FromLTRB(15, 15, 60, 60), 1f, PointF.Empty, imageSize));

        // A click without a drag selects nothing.
        Assert.Equal(
            Rectangle.Empty,
            ImageEditorCanvasMath.ScreenRectToImageRect(Rectangle.FromLTRB(5, 5, 5, 5), 1f, PointF.Empty, imageSize));
    }

    [Fact]
    public void ScreenPointMapsToContainingPixelAndClamps()
    {
        var imageSize = new Size(10, 10);

        // At 4x zoom, screen x=7 lies inside image pixel 1.
        Assert.Equal(
            new Point(1, 1),
            ImageEditorCanvasMath.ScreenPointToImagePixel(new Point(7, 7), 4f, PointF.Empty, imageSize));

        // Points outside the image clamp to the nearest edge pixel.
        Assert.Equal(
            new Point(9, 0),
            ImageEditorCanvasMath.ScreenPointToImagePixel(new Point(500, -3), 1f, PointF.Empty, imageSize));
    }

    [Fact]
    public void CreateNewFilePathSkipsExistingNames()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "image.png");
        File.WriteAllBytes(source, new byte[] { 1 });
        string first = ImageEditorSaveService.CreateNewFilePath(source);
        Assert.Equal(Path.Combine(temp.Path, "image_edit.png"), first);

        File.WriteAllBytes(first, new byte[] { 1 });
        string second = ImageEditorSaveService.CreateNewFilePath(source);
        Assert.Equal(Path.Combine(temp.Path, "image_edit2.png"), second);
    }

    [Fact]
    public void CloneCaptionCopiesTagFileNextToNewImage()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "image.png");
        File.WriteAllBytes(source, new byte[] { 1 });
        File.WriteAllText(Path.Combine(temp.Path, "image.txt"), "1girl, smile");
        string target = Path.Combine(temp.Path, "image_edit.png");

        string caption = ImageEditorSaveService.CloneCaption(source, target, new[] { "txt", "caption" });

        Assert.Equal(Path.Combine(temp.Path, "image_edit.txt"), caption);
        Assert.Equal("1girl, smile", File.ReadAllText(caption!));
    }

    [Fact]
    public void CloneCaptionReturnsNullWithoutSourceCaption()
    {
        using var temp = new TemporaryDirectory();
        string source = Path.Combine(temp.Path, "image.png");
        File.WriteAllBytes(source, new byte[] { 1 });

        Assert.Null(ImageEditorSaveService.CloneCaption(
            source, Path.Combine(temp.Path, "image_edit.png"), new[] { "txt" }));
    }
}
