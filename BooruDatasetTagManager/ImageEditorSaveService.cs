using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace BooruDatasetTagManager
{
    /// <summary>Result of a completed image-editor save, consumed by MainForm
    /// to refresh caches, thumbnails, and (for new files) the dataset.</summary>
    public sealed class ImageEditorSaveOutcome
    {
        public ImageEditorSaveMode Mode { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ClonedCaptionPath { get; set; }
    }

    /// <summary>
    /// File-side half of the image editor: encodes the edited bitmap in the
    /// format matching the file extension (via ImageSharp, so webp works too),
    /// picks collision-free "_edit" names, and clones the caption file when the
    /// user saves an edited copy instead of overwriting.
    /// </summary>
    public static class ImageEditorSaveService
    {
        private static readonly HashSet<string> TransparencyCapableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".webp", ".gif"
        };

        /// <summary>True when the target format keeps an alpha channel, so the
        /// eraser can erase to transparent instead of white.</summary>
        public static bool SupportsTransparency(string extension)
        {
            return !string.IsNullOrEmpty(extension) && TransparencyCapableExtensions.Contains(extension);
        }

        public static byte[] Encode(System.Drawing.Bitmap bitmap, string extension)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            using Image<Bgra32> image = ToImageSharp(bitmap);
            using var stream = new MemoryStream();
            image.Save(stream, CreateEncoder(extension));
            return stream.ToArray();
        }

        private static IImageEncoder CreateEncoder(string extension)
        {
            switch (extension?.ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".jfif":
                    return new JpegEncoder { Quality = 95 };
                case ".bmp":
                    return new BmpEncoder();
                case ".gif":
                    return new GifEncoder();
                case ".webp":
                    return new WebpEncoder();
                default:
                    return new PngEncoder();
            }
        }

        /// <summary>Inverse of ImageLoader.ToDrawingImage: direct pixel copy,
        /// no lossy intermediate encode.</summary>
        private static Image<Bgra32> ToImageSharp(System.Drawing.Bitmap bitmap)
        {
            var bounds = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                byte[] pixels = new byte[rowBytes * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * rowBytes, rowBytes);
                return SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(pixels, bitmap.Width, bitmap.Height);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Returns "&lt;name&gt;_edit&lt;ext&gt;" next to the source,
        /// appending a counter until the name is free.</summary>
        public static string CreateNewFilePath(string imagePath)
        {
            string fullPath = Path.GetFullPath(imagePath);
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            for (int index = 1; ; index++)
            {
                string suffix = index == 1 ? "_edit" : "_edit" + index;
                string candidate = Path.Combine(directory, baseName + suffix + extension);
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        /// <summary>Copies the source image's caption file (if any) so the edited
        /// copy keeps its tags. Returns the new caption path or null.</summary>
        public static string CloneCaption(string sourceImagePath, string targetImagePath, IEnumerable<string> tagExtensions)
        {
            string sourceCaption = FindExistingCaptionPath(sourceImagePath, tagExtensions);
            if (sourceCaption == null)
                return null;
            string directory = Path.GetDirectoryName(Path.GetFullPath(targetImagePath)) ?? string.Empty;
            string targetCaption = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(targetImagePath) + Path.GetExtension(sourceCaption));
            if (File.Exists(targetCaption))
                return targetCaption;
            File.Copy(sourceCaption, targetCaption);
            return targetCaption;
        }

        public static string FindExistingCaptionPath(string imagePath, IEnumerable<string> tagExtensions)
        {
            if (tagExtensions == null)
                return null;
            string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath)) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(imagePath);
            foreach (string extension in tagExtensions)
            {
                if (string.IsNullOrWhiteSpace(extension))
                    continue;
                string candidate = Path.Combine(directory, baseName + "." + extension.TrimStart('.'));
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }
    }
}
