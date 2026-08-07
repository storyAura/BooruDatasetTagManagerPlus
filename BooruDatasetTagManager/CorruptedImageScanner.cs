using System;
using System.Collections.Generic;
using System.IO;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Outcome of inspecting one image file for decodeability.
    /// </summary>
    public sealed class CorruptedImageFinding
    {
        public string Path { get; init; } = string.Empty;
        /// <summary>Stable reason code: missing / empty / decode / invalid_size.</summary>
        public string ReasonCode { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
    }

    /// <summary>
    /// Pure disk inspect for broken / unreadable image files. Linked into
    /// tests; no Program.* or WinForms. Video paths are out of scope — callers
    /// should skip them the same way the similar-image finder does.
    /// </summary>
    public static class CorruptedImageScanner
    {
        public const string ReasonMissing = "missing";
        public const string ReasonEmpty = "empty";
        public const string ReasonDecode = "decode";
        public const string ReasonInvalidSize = "invalid_size";

        /// <summary>
        /// Returns a finding when <paramref name="imagePath"/> cannot be
        /// opened as an image; otherwise null.
        /// </summary>
        public static CorruptedImageFinding Inspect(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return new CorruptedImageFinding
                {
                    Path = imagePath ?? string.Empty,
                    ReasonCode = ReasonMissing
                };
            }

            long length;
            try
            {
                length = new FileInfo(imagePath).Length;
            }
            catch (Exception ex)
            {
                return new CorruptedImageFinding
                {
                    Path = imagePath,
                    ReasonCode = ReasonDecode,
                    Detail = ex.GetType().Name
                };
            }

            if (length <= 0)
            {
                return new CorruptedImageFinding
                {
                    Path = imagePath,
                    ReasonCode = ReasonEmpty
                };
            }

            try
            {
                using ImageSharpImage image = ImageSharpImage.Load(imagePath);
                if (image.Width <= 0 || image.Height <= 0)
                {
                    return new CorruptedImageFinding
                    {
                        Path = imagePath,
                        ReasonCode = ReasonInvalidSize,
                        Detail = image.Width + "x" + image.Height
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                return new CorruptedImageFinding
                {
                    Path = imagePath,
                    ReasonCode = ReasonDecode,
                    Detail = ex.GetType().Name
                };
            }
        }

        /// <summary>
        /// Scans <paramref name="imagePaths"/> in order; reports progress after
        /// each file. Cancellation stops further inspects; already-found items
        /// are still returned.
        /// </summary>
        public static List<CorruptedImageFinding> Scan(
            IEnumerable<string> imagePaths,
            IProgress<int> progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var findings = new List<CorruptedImageFinding>();
            if (imagePaths == null)
                return findings;

            int done = 0;
            foreach (string path in imagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CorruptedImageFinding finding = Inspect(path);
                if (finding != null)
                    findings.Add(finding);
                done++;
                progress?.Report(done);
            }
            return findings;
        }
    }
}
