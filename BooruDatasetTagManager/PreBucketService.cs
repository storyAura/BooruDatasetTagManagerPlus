using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// File-side pre-bucketing: probe sizes, assign output paths, letterbox
    /// onto a white canvas, and write via <see cref="SafeFile"/>.
    /// </summary>
    public static class PreBucketService
    {
        public static Size? TryGetImageSize(string path)
        {
            return ResolutionPrepService.TryGetImageSize(path);
        }

        public static List<(string Path, Size? Size)> ProbeSizes(IEnumerable<string> paths)
        {
            return ResolutionPrepService.ProbeSizes(paths);
        }

        public static void AssignOutputPaths(
            IEnumerable<PreBucketJob> jobs,
            string outputRoot,
            Func<string, bool> exists = null)
        {
            if (jobs == null || string.IsNullOrWhiteSpace(outputRoot))
                return;

            exists ??= File.Exists;
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PreBucketJob job in jobs)
            {
                if (job == null)
                    continue;
                job.OutputPath = PreBucketMath.AllocateOutputPath(
                    outputRoot,
                    job.BucketSize,
                    job.SourcePath,
                    reserved,
                    exists);
            }
        }

        public static string TryWrite(PreBucketJob job, IEnumerable<string> tagExtensions)
        {
            try
            {
                if (job == null || string.IsNullOrEmpty(job.SourcePath) || !File.Exists(job.SourcePath))
                    return null;
                if (string.IsNullOrEmpty(job.OutputPath)
                    || job.BucketSize.Width < 1 || job.BucketSize.Height < 1)
                {
                    return null;
                }

                byte[] original = File.ReadAllBytes(job.SourcePath);
                using var source = SixLabors.ImageSharp.Image.Load(original);
                source.Mutate(context => context.AutoOrient());

                Size fitted = job.FittedSize.Width > 0 && job.FittedSize.Height > 0
                    ? job.FittedSize
                    : PreBucketMath.FitInside(
                        new Size(source.Width, source.Height),
                        job.BucketSize,
                        allowUpscale: true);
                if (fitted.Width < 1 || fitted.Height < 1)
                    return null;

                Point offset = job.DrawOffset;
                if (fitted.Width != source.Width || fitted.Height != source.Height)
                {
                    source.Mutate(context => context.Resize(new ResizeOptions
                    {
                        Size = new SixLabors.ImageSharp.Size(fitted.Width, fitted.Height),
                        Mode = ResizeMode.Stretch,
                        Sampler = KnownResamplers.Lanczos3
                    }));
                    offset = PreBucketMath.CenterOffset(fitted, job.BucketSize);
                }
                else if (offset.X < 0 || offset.Y < 0)
                {
                    offset = PreBucketMath.CenterOffset(fitted, job.BucketSize);
                }

                using var canvas = new Image<Rgba32>(
                    job.BucketSize.Width,
                    job.BucketSize.Height,
                    new Rgba32(255, 255, 255, 255));
                canvas.Mutate(context => context.DrawImage(
                    source,
                    new SixLabors.ImageSharp.Point(offset.X, offset.Y),
                    1f));

                string directory = Path.GetDirectoryName(job.OutputPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                byte[] bytes = ImageEditorSaveService.Encode(canvas, Path.GetExtension(job.OutputPath));
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
