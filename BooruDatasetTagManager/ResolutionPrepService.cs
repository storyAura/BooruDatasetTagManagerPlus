using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// File-side resolution prep: probe sizes, assign output paths, crop /
    /// Lanczos-downscale through ImageSharp, and write via <see cref="SafeFile"/>.
    /// </summary>
    public static class ResolutionPrepService
    {
        public static Size? TryGetImageSize(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || VideoProcessingService.IsVideoFile(path))
                return null;
            try
            {
                ImageInfo info = SixLabors.ImageSharp.Image.Identify(path);
                if (info == null || info.Width <= 0 || info.Height <= 0)
                    return null;
                return new Size(info.Width, info.Height);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<(string Path, Size? Size)> ProbeSizes(IEnumerable<string> paths)
        {
            var items = new List<(string Path, Size? Size)>();
            if (paths == null)
                return items;
            foreach (string path in paths)
                items.Add((path, TryGetImageSize(path)));
            return items;
        }

        public static void AssignOutputPaths(IEnumerable<ResolutionPrepJob> jobs, Func<string, bool> exists = null)
        {
            if (jobs == null)
                return;
            exists ??= File.Exists;
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ResolutionPrepJob job in jobs)
            {
                if (job == null)
                    continue;
                job.OutputPath = ResolutionPrepMath.AllocateOutputPath(
                    job.SourcePath,
                    job.Suffix,
                    reserved,
                    exists);
            }
        }

        public static string TryWrite(ResolutionPrepJob job, bool sharpen, IEnumerable<string> tagExtensions)
        {
            try
            {
                if (job == null || string.IsNullOrEmpty(job.SourcePath) || !File.Exists(job.SourcePath))
                    return null;
                if (string.IsNullOrEmpty(job.OutputPath) || job.OutputSize.Width < 1 || job.OutputSize.Height < 1)
                    return null;

                byte[] original = File.ReadAllBytes(job.SourcePath);
                using var image = SixLabors.ImageSharp.Image.Load(original);
                image.Mutate(context => context.AutoOrient());
                if (image.Width != job.SourceSize.Width || image.Height != job.SourceSize.Height)
                    return null;

                var bounds = SixLabors.ImageSharp.Rectangle.Intersect(
                    new SixLabors.ImageSharp.Rectangle(
                        job.SourceRect.X,
                        job.SourceRect.Y,
                        job.SourceRect.Width,
                        job.SourceRect.Height),
                    new SixLabors.ImageSharp.Rectangle(0, 0, image.Width, image.Height));
                if (bounds.Width < 1 || bounds.Height < 1)
                    return null;

                bool cropped = bounds.X != 0 || bounds.Y != 0
                    || bounds.Width != image.Width || bounds.Height != image.Height;
                if (cropped)
                    image.Mutate(context => context.Crop(bounds));

                bool resized = image.Width != job.OutputSize.Width || image.Height != job.OutputSize.Height;
                if (resized)
                {
                    if (job.OutputSize.Width > image.Width || job.OutputSize.Height > image.Height)
                        return null;
                    image.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(job.OutputSize.Width, job.OutputSize.Height),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                    if (sharpen)
                        image.Mutate(context => context.GaussianSharpen(ResolutionPrepMath.SharpenSigma));
                }

                byte[] bytes;
                if (!cropped && !resized)
                    bytes = original;
                else
                    bytes = ImageEditorSaveService.Encode(image, Path.GetExtension(job.OutputPath));

                SafeFile.WriteAllBytes(job.OutputPath, bytes);
                if (tagExtensions != null)
                    ImageEditorSaveService.CloneCaption(job.SourcePath, job.OutputPath, tagExtensions);
                return job.OutputPath;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
