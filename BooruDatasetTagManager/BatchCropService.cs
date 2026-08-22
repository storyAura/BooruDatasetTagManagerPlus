using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// File-side batch crop: identify sizes, write cropped bytes through
    /// <see cref="SafeFile"/>, and pick collision-free <c>_crop</c> names.
    /// </summary>
    public static class BatchCropService
    {
        public static Size? TryGetImageSize(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || VideoProcessingService.IsVideoFile(path))
                return null;
            try
            {
                SixLabors.ImageSharp.ImageInfo info = SixLabors.ImageSharp.Image.Identify(path);
                if (info == null || info.Width <= 0 || info.Height <= 0)
                    return null;
                return new Size(info.Width, info.Height);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetCropCopyPath(string imagePath)
        {
            string fullPath = Path.GetFullPath(imagePath);
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";

            string candidate = Path.Combine(directory, baseName + "_crop" + extension);
            if (!File.Exists(candidate))
                return candidate;
            for (int index = 2; ; index++)
            {
                candidate = Path.Combine(directory, baseName + "_crop" + index + extension);
                if (!File.Exists(candidate))
                    return candidate;
            }
        }

        public static bool TryOverwrite(string imagePath, Size expectedSize, Rectangle crop)
        {
            return TryWrite(imagePath, expectedSize, crop, imagePath) != null;
        }

        public static string TrySaveCopy(string imagePath, Size expectedSize, Rectangle crop)
        {
            return TryWrite(imagePath, expectedSize, crop, GetCropCopyPath(imagePath));
        }

        private static string TryWrite(string imagePath, Size expectedSize, Rectangle crop, string outputPath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;

                using Image loaded = ImageLoader.GetImageFromFile(imagePath);
                if (loaded is not Bitmap bitmap || bitmap.Size != expectedSize)
                    return null;

                Rectangle bounds = BatchCropMath.Place(crop, bitmap.Size);
                if (bounds.Width < BatchCropMath.MinSize || bounds.Height < BatchCropMath.MinSize)
                    return null;

                byte[] bytes;
                using (Bitmap cropped = CropBitmap(bitmap, bounds))
                {
                    if (cropped == null)
                        return null;
                    bytes = ImageEditorSaveService.Encode(cropped, Path.GetExtension(outputPath));
                }

                SafeFile.WriteAllBytes(outputPath, bytes);
                return outputPath;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Copy <paramref name="bounds"/> into a new 32bppArgb bitmap.
        /// GDI+ <see cref="Bitmap.Clone(Rectangle, PixelFormat)"/> throws
        /// "Parameter is not valid" for some source formats and for a crop
        /// that covers the entire image, so this uses DrawImage instead.
        /// </summary>
        internal static Bitmap CropBitmap(Bitmap source, Rectangle bounds)
        {
            if (source == null)
                return null;
            bounds = Rectangle.Intersect(bounds, new Rectangle(0, 0, source.Width, source.Height));
            if (bounds.Width < BatchCropMath.MinSize || bounds.Height < BatchCropMath.MinSize)
                return null;

            var dest = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(dest))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    graphics.DrawImage(
                        source,
                        new Rectangle(0, 0, bounds.Width, bounds.Height),
                        bounds,
                        GraphicsUnit.Pixel);
                }
                return dest;
            }
            catch
            {
                dest.Dispose();
                throw;
            }
        }
    }
}
