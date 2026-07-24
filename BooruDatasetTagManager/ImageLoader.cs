using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
                // Bake the EXIF orientation into the pixels: GDI+ ignores the
                // tag, so camera photos showed (and saved) sideways otherwise.
                image.Mutate(context => context.AutoOrient());
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
                image.Mutate(context => context.AutoOrient().Resize(new ResizeOptions
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

        public static System.Drawing.Image LoadPreview(string imagePath, int maximumDimension)
        {
            if (!File.Exists(imagePath) || maximumDimension <= 0)
                return null;

            try
            {
                using var image = SixLabors.ImageSharp.Image.Load(imagePath);
                image.Mutate(context => context.AutoOrient());
                if (Math.Max(image.Width, image.Height) > maximumDimension)
                {
                    image.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new SixLabors.ImageSharp.Size(maximumDimension, maximumDimension)
                    }));
                }
                return ToDrawingImage(image);
            }
            catch
            {
                return null;
            }
        }

        private static System.Drawing.Image ToDrawingImage(SixLabors.ImageSharp.Image image)
        {
            // Direct pixel copy. The previous PNG-encode -> GDI+-decode -> clone
            // round-trip made 4 transient copies per image and dominated dataset
            // loading CPU. Bgra32 rows match GDI+ Format32bppArgb memory layout.
            using var clone = image.CloneAs<Bgra32>();
            var bitmap = new Bitmap(clone.Width, clone.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
                BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = clone.Width * 4;
                    byte[] buffer = new byte[rowBytes];
                    clone.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            MemoryMarshal.AsBytes(accessor.GetRowSpan(y)).CopyTo(buffer);
                            Marshal.Copy(buffer, 0, data.Scan0 + y * data.Stride, rowBytes);
                        }
                    });
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
