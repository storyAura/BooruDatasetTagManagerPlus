using BooruDatasetTagManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class ImageCropExporterTests
{
    [Fact]
    public void GetOutputPath_uses_same_folder_and_region_suffix()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "folder", "sample.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);

            string output = ImageCropExporter.GetOutputPath(imagePath, 2);

            Assert.Equal(Path.Combine(root, "folder", "sample_r2.png"), output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ExportRegions_writes_one_file_per_region_in_source_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "bdtm-crop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string imagePath = Path.Combine(root, "source.png");
            using (var image = new Image<Rgba32>(120, 80))
            {
                image.Save(imagePath, new PngEncoder());
            }

            using (var bitmap = new System.Drawing.Bitmap(imagePath))
            {
                var regions = new List<CropRegion>
                {
                    new CropRegion { Index = 1, Bounds = new System.Drawing.Rectangle(0, 0, 40, 40) },
                    new CropRegion { Index = 2, Bounds = new System.Drawing.Rectangle(50, 10, 30, 30) },
                };

                var exportedPaths = ImageCropExporter.ExportRegions(imagePath, bitmap, regions);
                Assert.Equal(2, exportedPaths.Count);
                Assert.True(File.Exists(ImageCropExporter.GetOutputPath(imagePath, 1)));
                Assert.True(File.Exists(ImageCropExporter.GetOutputPath(imagePath, 2)));
                Assert.Equal(root, ImageCropExporter.GetOutputDirectory(imagePath));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
