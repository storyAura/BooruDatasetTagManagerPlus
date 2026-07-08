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

            var exportedPaths = new List<string>();
            foreach (CropRegion region in regions)
            {
                Rectangle bounds = CropCanvasHelper.ClampToImage(region.Bounds, source.Size);
                if (bounds.Width < 2 || bounds.Height < 2)
                    continue;

                string outputPath = GetOutputPath(imagePath, region.Index);
                using (Bitmap cropped = source.Clone(bounds, source.PixelFormat))
                {
                    cropped.Save(outputPath);
                }
                exportedPaths.Add(outputPath);
            }
            return exportedPaths;
        }
    }
}
