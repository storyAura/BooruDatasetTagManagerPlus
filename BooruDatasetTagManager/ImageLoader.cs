using System;
using System.Drawing;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace BooruDatasetTagManager
{
    public static class ImageLoader
    {
        public static System.Drawing.Image GetImageFromFile(string imagePath)
        {
            if (!File.Exists(imagePath))
                return null;

            try
            {
                using var image = SixLabors.ImageSharp.Image.Load(imagePath);
                return ToDrawingImage(image);
            }
            catch
            {
                return null;
            }
        }

        public static System.Drawing.Image MakeThumb(string imagePath, int imageSize)
        {
            if (!File.Exists(imagePath) || imageSize <= 0)
                return null;

            try
            {
                using var image = SixLabors.ImageSharp.Image.Load(imagePath);
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new SixLabors.ImageSharp.Size(imageSize, imageSize)
                }));
                return ToDrawingImage(image);
            }
            catch
            {
                return null;
            }
        }

        private static System.Drawing.Image ToDrawingImage(SixLabors.ImageSharp.Image image)
        {
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            stream.Position = 0;
            using var bitmap = new Bitmap(stream);
            return new Bitmap(bitmap);
        }
    }
}
