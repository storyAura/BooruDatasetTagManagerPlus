using System.Drawing;
using System.Drawing.Imaging;
using BooruDatasetTagManager;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class Wd14OnnxImagePreprocessorTests
{
    [Theory]
    [InlineData(255, 0, 0, 0f, 0f, 255f)]   // pure red → BGR [0, 0, 255]
    [InlineData(0, 0, 255, 255f, 0f, 0f)]     // pure blue → BGR [255, 0, 0]
    [InlineData(0, 255, 0, 0f, 255f, 0f)]     // pure green → BGR [0, 255, 0]
    public void CreateInputTensor_packs_gdi_bytes_as_bgr_nhwc(
        int r, int g, int b,
        float expectB, float expectG, float expectR)
    {
        const int size = 8;
        using var source = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.FromArgb(255, r, g, b));
        }

        DenseTensor<float> tensor = Wd14OnnxImagePreprocessor.CreateInputTensor(source, size);

        Assert.Equal(new[] { 1, size, size, 3 }, tensor.Dimensions.ToArray());
        // Center pixel (pad is white for smaller images; here source fills the canvas).
        int x = size / 2;
        int y = size / 2;
        Assert.Equal(expectB, tensor[0, y, x, 0]);
        Assert.Equal(expectG, tensor[0, y, x, 1]);
        Assert.Equal(expectR, tensor[0, y, x, 2]);
    }
}
