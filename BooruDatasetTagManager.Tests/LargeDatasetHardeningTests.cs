using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager.Tests;

public sealed class LargeDatasetHardeningTests
{
    [Fact]
    public void LoadFromFolder_accepts_five_hundred_images()
    {
        using var temp = new TemporaryDirectory();
        WritePngBatch(temp.Path, 500);

        var manager = new DatasetManager();
        try
        {
            Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
            Assert.Equal(500, manager.DataSet.Count);
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void TagWriteService_applies_append_to_five_hundred_items()
    {
        using var temp = new TemporaryDirectory();
        WritePngBatch(temp.Path, 500);
        var manager = new DatasetManager();
        try
        {
            Assert.True(manager.LoadFromFolder(temp.Path, loadPreviewImages: false, readMetadata: false));
            var settings = new Wd14TaggerSettings
            {
                SetMode = NetworkResultSetMode.OnlyNewWithAddition
            };

            manager.ExecuteBulkMutation(() =>
            {
                foreach (DataItem item in manager.DataSet.Values)
                    TagWriteService.ApplyTagNames(item, new[] { "1girl", "solo" }, settings);
            });

            Assert.All(manager.DataSet.Values, item =>
            {
                Assert.Equal(new[] { "1girl", "solo" }, item.Tags.TextTags);
            });
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static void WritePngBatch(string folder, int count)
    {
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(folder, $"img-{i:D4}.png");
            using var image = new Image<Rgba32>(8, 8, new Rgba32(20, 40, 80, 255));
            image.Save(path);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-large-ds-{Guid.NewGuid():N}");

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
