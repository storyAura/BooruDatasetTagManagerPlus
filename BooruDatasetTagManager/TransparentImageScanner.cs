using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Pre-pass for the "replace transparent background" batches: keeps only
    /// the files that can actually be affected — a format carrying an alpha
    /// channel (png / webp / gif) that really contains non-opaque pixels.
    /// Without it a whole folder of opaque jpg/png images would be re-encoded
    /// for nothing (lossy for jpg, and a pointless overwrite everywhere else).
    /// Pure disk/decode logic, no Program.* or WinForms, so tests link it.
    /// </summary>
    public static class TransparentImageScanner
    {
        /// <summary>
        /// True when the file extension can carry alpha at all. Videos and
        /// alpha-less formats (jpg, bmp, …) are rejected without a decode.
        /// </summary>
        public static bool SupportsAlpha(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                return false;
            if (VideoProcessingService.IsVideoFile(imagePath))
                return false;
            return ImageEditorSaveService.SupportsTransparency(Path.GetExtension(imagePath));
        }

        /// <summary>
        /// True when at least one pixel is not fully opaque. Scanning stops at
        /// the first such pixel. Missing or undecodable files return false —
        /// they are simply not candidates (the corrupted-image scanner is the
        /// tool that reports them).
        /// </summary>
        public static bool HasTransparentPixels(string imagePath, byte opaqueAlpha = 255)
        {
            if (!SupportsAlpha(imagePath) || !File.Exists(imagePath))
                return false;
            try
            {
                using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(imagePath);
                bool found = false;
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            if (row[x].A < opaqueAlpha)
                            {
                                found = true;
                                return;
                            }
                        }
                    }
                });
                return found;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the subset of <paramref name="imagePaths"/> that has a
        /// transparent background to replace, in input order. Progress is
        /// reported as "files inspected" after each file.
        /// </summary>
        public static List<string> FindTransparent(
            IEnumerable<string> imagePaths,
            IProgress<int> progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new List<string>();
            if (imagePaths == null)
                return result;

            int done = 0;
            foreach (string path in imagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasTransparentPixels(path))
                    result.Add(path);
                done++;
                progress?.Report(done);
            }
            return result;
        }
    }
}
