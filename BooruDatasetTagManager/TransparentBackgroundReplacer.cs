using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Fills transparent / semi-transparent pixels with a solid color and
    /// encodes the result using the source extension. ImageSharp-only so the
    /// Tools-menu batch does not depend on GDI+ compositing (and can run on
    /// a worker thread).
    /// </summary>
    public static class TransparentBackgroundReplacer
    {
        public static byte[] Replace(string imagePath, System.Drawing.Color fillColor)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("Image path is empty.", nameof(imagePath));
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image not found.", imagePath);

            using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
            image.Mutate(context => context.AutoOrient());
            Flatten(image, fillColor);
            return ImageEditorSaveService.Encode(image, Path.GetExtension(imagePath));
        }

        public static void Flatten(Image<Rgba32> image, System.Drawing.Color fillColor)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            var background = new Rgba32(fillColor.R, fillColor.G, fillColor.B, 255);
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        ref Rgba32 pixel = ref row[x];
                        if (pixel.A == 255)
                            continue;
                        if (pixel.A == 0)
                        {
                            pixel = background;
                            continue;
                        }

                        int inverse = 255 - pixel.A;
                        pixel.R = (byte)((pixel.R * pixel.A + background.R * inverse + 127) / 255);
                        pixel.G = (byte)((pixel.G * pixel.A + background.G * inverse + 127) / 255);
                        pixel.B = (byte)((pixel.B * pixel.A + background.B * inverse + 127) / 255);
                        pixel.A = 255;
                    }
                }
            });
        }
    }
}
