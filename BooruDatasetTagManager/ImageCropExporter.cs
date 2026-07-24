using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace BooruDatasetTagManager
{
    public static class ImageCropExporter
    {
        public static string GetOutputPath(string imagePath, int regionIndex)
        {
            string fullImage = Path.GetFullPath(imagePath);
            string directory = Path.GetDirectoryName(fullImage) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(fullImage);
            string ext = Path.GetExtension(fullImage);
            if (string.IsNullOrEmpty(ext))
                ext = ".png";
            return Path.Combine(directory, baseName + "_r" + regionIndex + ext);
        }

        public static string GetOutputDirectory(string imagePath)
        {
            return Path.GetDirectoryName(Path.GetFullPath(imagePath)) ?? string.Empty;
        }

        public static IReadOnlyList<string> ExportRegions(string imagePath, Bitmap source, IReadOnlyList<CropRegion> regions)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (regions == null || regions.Count == 0)
                return Array.Empty<string>();

            // Encode every region in memory first, then write: an encode
            // failure aborts before any file lands (no half-exported batch),
            // and each write is atomic via SafeFile. Encoding goes through
            // ImageEditorSaveService so the bytes actually match the extension
            // (bare Bitmap.Save always produced PNG bytes, lying for .jpg/.webp).
            var staged = new List<(string Path, byte[] Bytes)>();
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CropRegion region in regions)
            {
                Rectangle bounds = CropCanvasHelper.ClampToImage(region.Bounds, source.Size);
                if (bounds.Width < 2 || bounds.Height < 2)
                    continue;

                string outputPath = EnsureUniquePath(GetOutputPath(imagePath, region.Index), taken);
                using (Bitmap cropped = source.Clone(bounds, source.PixelFormat))
                {
                    staged.Add((outputPath, ImageEditorSaveService.Encode(cropped, Path.GetExtension(outputPath))));
                }
            }

            var exportedPaths = new List<string>();
            foreach ((string path, byte[] bytes) in staged)
            {
                SafeFile.WriteAllBytes(path, bytes);
                exportedPaths.Add(path);
            }
            return exportedPaths;
        }

        /// <summary>Appends " (2)", " (3)"… until the name collides neither
        /// with an existing file nor with an earlier region of this batch, so
        /// re-exporting never silently overwrites previous crops.</summary>
        private static string EnsureUniquePath(string desiredPath, HashSet<string> taken)
        {
            string directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(desiredPath);
            string ext = Path.GetExtension(desiredPath);
            string candidate = desiredPath;
            for (int counter = 2; File.Exists(candidate) || !taken.Add(candidate); counter++)
                candidate = Path.Combine(directory, baseName + " (" + counter + ")" + ext);
            return candidate;
        }
    }
}
