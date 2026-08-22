using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// One planned write: crop <see cref="SourceRect"/> from the source, then
    /// (if needed) Lanczos-downscale to <see cref="OutputSize"/>.
    /// </summary>
    public sealed class ResolutionPrepJob
    {
        public string SourcePath { get; init; }
        public Size SourceSize { get; init; }
        public Rectangle SourceRect { get; init; }
        public Size OutputSize { get; init; }
        public string Suffix { get; init; }
        public string OutputPath { get; set; }

        public bool NeedsResize =>
            SourceRect.Width != OutputSize.Width || SourceRect.Height != OutputSize.Height;

        public bool IsFullImage =>
            SourceRect.X == 0
            && SourceRect.Y == 0
            && SourceRect.Width == SourceSize.Width
            && SourceRect.Height == SourceSize.Height;
    }

    public sealed class ResolutionPrepRequest
    {
        public ResolutionPrepMode Mode { get; init; }
        public int AspectWidth { get; init; } = 1;
        public int AspectHeight { get; init; } = 1;
        public IReadOnlyList<int> Gears { get; init; } = Array.Empty<int>();
        public int RandomCount { get; init; } = 1;
        public Random Random { get; init; }
    }

    public sealed class ResolutionPrepPlan
    {
        public IReadOnlyList<ResolutionPrepJob> Jobs { get; init; } = Array.Empty<ResolutionPrepJob>();
        public int ImageCount { get; init; }
        public int SkippedImages { get; init; }
        public int SkippedGears { get; init; }
    }

    /// <summary>
    /// Gear / aspect / tile geometry for the resolution-prep window. Kept
    /// UI-free so the form can stay unlinked from the test project.
    /// </summary>
    public static class ResolutionPrepMath
    {
        public static readonly int[] DefaultGears = { 512, 768, 896, 1024, 1280, 1536 };

        public const int MinGear = 64;
        public const int MaxGear = 8192;
        public const int Align = 64;
        public const float SharpenSigma = 0.6f;
        public const int MinRandomCount = 1;
        public const int MaxRandomCount = 32;

        public static int ClampRandomCount(int value)
        {
            if (value < MinRandomCount)
                return MinRandomCount;
            if (value > MaxRandomCount)
                return MaxRandomCount;
            return value;
        }

        public static IReadOnlyList<string> ResolveSourcePaths(
            ResolutionPrepSource source,
            IReadOnlyList<string> selected,
            IReadOnlyList<string> folder,
            IReadOnlyList<string> all)
        {
            switch (source)
            {
                case ResolutionPrepSource.Folder:
                    return folder ?? Array.Empty<string>();
                case ResolutionPrepSource.AllImages:
                    return all ?? Array.Empty<string>();
                default:
                    return selected ?? Array.Empty<string>();
            }
        }

        public static int AlignDown(int value)
        {
            if (value < MinGear)
                return 0;
            return value - (value % Align);
        }

        /// <summary>
        /// Returns a 64-aligned gear in range, or null when the value is unusable.
        /// </summary>
        public static int? TryNormalizeGear(int value)
        {
            if (value < MinGear || value > MaxGear)
                return null;
            int aligned = AlignDown(value);
            return aligned >= MinGear ? aligned : (int?)null;
        }

        public static IReadOnlyList<int> MergeGears(IEnumerable<int> custom)
        {
            var set = new SortedSet<int>(DefaultGears);
            if (custom != null)
            {
                foreach (int gear in custom)
                {
                    int? normalized = TryNormalizeGear(gear);
                    if (normalized.HasValue)
                        set.Add(normalized.Value);
                }
            }
            return new List<int>(set);
        }

        public static IReadOnlyList<int> NormalizeSelectedGears(IEnumerable<int> selected)
        {
            var set = new SortedSet<int>();
            if (selected != null)
            {
                foreach (int gear in selected)
                {
                    int? normalized = TryNormalizeGear(gear);
                    if (normalized.HasValue)
                        set.Add(normalized.Value);
                }
            }
            return new List<int>(set);
        }

        public static Size? ScaleToLongEdge(Size source, int gear)
        {
            int target = AlignDown(gear);
            if (target < MinGear || source.Width < 1 || source.Height < 1)
                return null;

            int longEdge = Math.Max(source.Width, source.Height);
            if (longEdge < target)
                return null;

            if (longEdge == target)
            {
                int alignedWidth = AlignDown(source.Width);
                int alignedHeight = AlignDown(source.Height);
                if (alignedWidth < MinGear || alignedHeight < MinGear)
                    return null;
                return new Size(alignedWidth, alignedHeight);
            }

            double scale = (double)target / longEdge;
            int outWidth = AlignDown((int)Math.Round(source.Width * scale));
            int outHeight = AlignDown((int)Math.Round(source.Height * scale));
            if (outWidth < MinGear || outHeight < MinGear)
                return null;
            if (outWidth > target)
                outWidth = target;
            if (outHeight > target)
                outHeight = target;
            return new Size(outWidth, outHeight);
        }

        public static Rectangle? CenterCrop(Size image, int aspectWidth, int aspectHeight)
        {
            if (image.Width < 1 || image.Height < 1 || aspectWidth < 1 || aspectHeight < 1)
                return null;
            Rectangle rect = BatchCropMath.ApplyAspect(
                new Rectangle(0, 0, image.Width, image.Height),
                image,
                BatchCropAspect.Preset(aspectWidth, aspectHeight));
            if (rect.Width < 1 || rect.Height < 1)
                return null;
            return rect;
        }

        /// <summary>
        /// Largest in-image rectangle of the given aspect (same size as
        /// <see cref="CenterCrop"/>) whose origin is uniform-random in the
        /// remaining slide range. Pass a seeded <see cref="Random"/> in tests.
        /// </summary>
        public static Rectangle? RandomCrop(Size image, int aspectWidth, int aspectHeight, Random random)
        {
            Rectangle? crop = CenterCrop(image, aspectWidth, aspectHeight);
            if (crop == null)
                return null;

            random ??= new Random();
            int maxX = image.Width - crop.Value.Width;
            int maxY = image.Height - crop.Value.Height;
            int x = maxX <= 0 ? 0 : random.Next(0, maxX + 1);
            int y = maxY <= 0 ? 0 : random.Next(0, maxY + 1);
            return new Rectangle(x, y, crop.Value.Width, crop.Value.Height);
        }

        public static Size? TileSize(int gear, int aspectWidth, int aspectHeight)
        {
            int target = AlignDown(gear);
            if (target < MinGear || aspectWidth < 1 || aspectHeight < 1)
                return null;

            int width;
            int height;
            if (aspectWidth >= aspectHeight)
            {
                width = target;
                height = AlignDown((int)Math.Round((double)target * aspectHeight / aspectWidth));
            }
            else
            {
                height = target;
                width = AlignDown((int)Math.Round((double)target * aspectWidth / aspectHeight));
            }

            if (width < MinGear || height < MinGear)
                return null;
            return new Size(width, height);
        }

        public static IReadOnlyList<Rectangle> PlaceTiles(Size image, Size tile)
        {
            var tiles = new List<Rectangle>();
            if (tile.Width < 1 || tile.Height < 1
                || image.Width < tile.Width || image.Height < tile.Height)
            {
                return tiles;
            }

            foreach (int y in AxisPositions(image.Height, tile.Height))
            {
                foreach (int x in AxisPositions(image.Width, tile.Width))
                    tiles.Add(new Rectangle(x, y, tile.Width, tile.Height));
            }
            return tiles;
        }

        public static string BuildSuffix(ResolutionPrepMode mode, int aspectWidth, int aspectHeight, int gear, int? tileIndex)
        {
            int target = AlignDown(gear);
            switch (mode)
            {
                case ResolutionPrepMode.CenterCrop:
                    return "_" + aspectWidth + "-" + aspectHeight + "_" + target;
                case ResolutionPrepMode.SplitTiles:
                    return "_" + aspectWidth + "-" + aspectHeight + "_" + target + "_" + (tileIndex ?? 1).ToString("00");
                case ResolutionPrepMode.RandomCrop:
                    return "_" + aspectWidth + "-" + aspectHeight + "_rand" + (tileIndex ?? 1) + "_" + target;
                case ResolutionPrepMode.YoloPerson:
                    return "_" + aspectWidth + "-" + aspectHeight + "_yolo" + (tileIndex ?? 1) + "_" + target;
                default:
                    return "_" + target;
            }
        }

        /// <summary>
        /// Collision-free path next to the source: <c>name{suffix}.ext</c>, then
        /// <c>name{suffix}2.ext</c>, …
        /// </summary>
        public static string AllocateOutputPath(
            string sourcePath,
            string suffix,
            ISet<string> reserved,
            Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(sourcePath))
                return null;

            string fullPath = Path.GetFullPath(sourcePath);
            string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(fullPath);
            string extension = Path.GetExtension(fullPath);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            if (string.IsNullOrEmpty(suffix))
                suffix = "_prep";

            exists ??= (_ => false);
            reserved ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 1; ; index++)
            {
                string extra = index == 1 ? string.Empty : index.ToString();
                string candidate = Path.Combine(directory, baseName + suffix + extra + extension);
                if (reserved.Contains(candidate) || exists(candidate))
                    continue;
                reserved.Add(candidate);
                return candidate;
            }
        }

        public static ResolutionPrepPlan Plan(
            IEnumerable<(string Path, Size? Size)> items,
            ResolutionPrepRequest request)
        {
            var jobs = new List<ResolutionPrepJob>();
            int imageCount = 0;
            int skippedImages = 0;
            int skippedGears = 0;
            var gears = NormalizeSelectedGears(request?.Gears);

            if (items == null)
                return new ResolutionPrepPlan();

            int aspectWidth = request == null ? 1 : Math.Max(1, request.AspectWidth);
            int aspectHeight = request == null ? 1 : Math.Max(1, request.AspectHeight);
            ResolutionPrepMode mode = request?.Mode ?? ResolutionPrepMode.ScaleOnly;

            foreach ((string path, Size? size) in items)
            {
                imageCount++;
                if (string.IsNullOrEmpty(path) || VideoProcessingService.IsVideoFile(path))
                {
                    skippedImages++;
                    continue;
                }
                if (size is not Size imageSize || imageSize.Width < 1 || imageSize.Height < 1)
                {
                    skippedImages++;
                    continue;
                }

                int jobsBefore = jobs.Count;
                switch (mode)
                {
                    case ResolutionPrepMode.CenterCrop:
                        PlanCenterCrop(jobs, path, imageSize, aspectWidth, aspectHeight, gears, ref skippedGears);
                        break;
                    case ResolutionPrepMode.SplitTiles:
                        PlanSplit(jobs, path, imageSize, aspectWidth, aspectHeight, gears, ref skippedGears);
                        break;
                    case ResolutionPrepMode.RandomCrop:
                        PlanRandom(jobs, path, imageSize, aspectWidth, aspectHeight, gears,
                            request.RandomCount, request.Random, ref skippedGears);
                        break;
                    case ResolutionPrepMode.YoloPerson:
                        // Detections are supplied via PlanFromCrops after YOLO runs.
                        break;
                    default:
                        PlanScaleOnly(jobs, path, imageSize, gears, ref skippedGears);
                        break;
                }

                if (jobs.Count == jobsBefore)
                    skippedImages++;
            }

            return new ResolutionPrepPlan
            {
                Jobs = jobs,
                ImageCount = imageCount,
                SkippedImages = skippedImages,
                SkippedGears = skippedGears
            };
        }

        /// <summary>
        /// Plan writes from already-chosen crop rectangles (YOLO boxes after
        /// aspect expansion, or any other precomputed windows).
        /// </summary>
        public static ResolutionPrepPlan PlanFromCrops(
            IEnumerable<(string Path, Size Size, IReadOnlyList<Rectangle> Crops)> items,
            ResolutionPrepRequest request)
        {
            var jobs = new List<ResolutionPrepJob>();
            int imageCount = 0;
            int skippedImages = 0;
            int skippedGears = 0;
            var gears = NormalizeSelectedGears(request?.Gears);
            int aspectWidth = request == null ? 1 : Math.Max(1, request.AspectWidth);
            int aspectHeight = request == null ? 1 : Math.Max(1, request.AspectHeight);
            ResolutionPrepMode mode = request?.Mode ?? ResolutionPrepMode.YoloPerson;

            if (items == null)
                return new ResolutionPrepPlan();

            foreach ((string path, Size imageSize, IReadOnlyList<Rectangle> crops) in items)
            {
                imageCount++;
                if (string.IsNullOrEmpty(path) || VideoProcessingService.IsVideoFile(path)
                    || imageSize.Width < 1 || imageSize.Height < 1
                    || crops == null || crops.Count == 0)
                {
                    skippedImages++;
                    continue;
                }

                int jobsBefore = jobs.Count;
                for (int i = 0; i < crops.Count; i++)
                {
                    Rectangle crop = BatchCropMath.Place(crops[i], imageSize);
                    if (crop.Width < 1 || crop.Height < 1)
                        continue;

                    bool anyGear = false;
                    foreach (int gear in gears)
                    {
                        Size? output = ScaleToLongEdge(crop.Size, gear);
                        if (output == null)
                        {
                            skippedGears++;
                            continue;
                        }

                        anyGear = true;
                        jobs.Add(new ResolutionPrepJob
                        {
                            SourcePath = path,
                            SourceSize = imageSize,
                            SourceRect = crop,
                            OutputSize = output.Value,
                            Suffix = BuildSuffix(mode, aspectWidth, aspectHeight, gear, i + 1)
                        });
                    }

                    if (!anyGear && gears.Count == 0)
                        break;
                }

                if (jobs.Count == jobsBefore)
                    skippedImages++;
            }

            return new ResolutionPrepPlan
            {
                Jobs = jobs,
                ImageCount = imageCount,
                SkippedImages = skippedImages,
                SkippedGears = skippedGears
            };
        }

        private static void PlanScaleOnly(
            List<ResolutionPrepJob> jobs,
            string path,
            Size imageSize,
            IReadOnlyList<int> gears,
            ref int skippedGears)
        {
            foreach (int gear in gears)
            {
                Size? output = ScaleToLongEdge(imageSize, gear);
                if (output == null)
                {
                    skippedGears++;
                    continue;
                }
                jobs.Add(new ResolutionPrepJob
                {
                    SourcePath = path,
                    SourceSize = imageSize,
                    SourceRect = new Rectangle(0, 0, imageSize.Width, imageSize.Height),
                    OutputSize = output.Value,
                    Suffix = BuildSuffix(ResolutionPrepMode.ScaleOnly, 1, 1, gear, null)
                });
            }
        }

        private static void PlanCenterCrop(
            List<ResolutionPrepJob> jobs,
            string path,
            Size imageSize,
            int aspectWidth,
            int aspectHeight,
            IReadOnlyList<int> gears,
            ref int skippedGears)
        {
            Rectangle? crop = CenterCrop(imageSize, aspectWidth, aspectHeight);
            if (crop == null)
            {
                skippedGears += gears.Count;
                return;
            }

            foreach (int gear in gears)
            {
                Size? output = ScaleToLongEdge(crop.Value.Size, gear);
                if (output == null)
                {
                    skippedGears++;
                    continue;
                }
                jobs.Add(new ResolutionPrepJob
                {
                    SourcePath = path,
                    SourceSize = imageSize,
                    SourceRect = crop.Value,
                    OutputSize = output.Value,
                    Suffix = BuildSuffix(ResolutionPrepMode.CenterCrop, aspectWidth, aspectHeight, gear, null)
                });
            }
        }

        private static void PlanRandom(
            List<ResolutionPrepJob> jobs,
            string path,
            Size imageSize,
            int aspectWidth,
            int aspectHeight,
            IReadOnlyList<int> gears,
            int randomCount,
            Random random,
            ref int skippedGears)
        {
            int count = ClampRandomCount(randomCount);
            random ??= new Random();
            var crops = new List<Rectangle>(count);
            for (int i = 0; i < count; i++)
            {
                Rectangle? crop = RandomCrop(imageSize, aspectWidth, aspectHeight, random);
                if (crop != null)
                    crops.Add(crop.Value);
            }

            if (crops.Count == 0)
            {
                skippedGears += gears.Count;
                return;
            }

            for (int i = 0; i < crops.Count; i++)
            {
                foreach (int gear in gears)
                {
                    Size? output = ScaleToLongEdge(crops[i].Size, gear);
                    if (output == null)
                    {
                        skippedGears++;
                        continue;
                    }
                    jobs.Add(new ResolutionPrepJob
                    {
                        SourcePath = path,
                        SourceSize = imageSize,
                        SourceRect = crops[i],
                        OutputSize = output.Value,
                        Suffix = BuildSuffix(ResolutionPrepMode.RandomCrop, aspectWidth, aspectHeight, gear, i + 1)
                    });
                }
            }
        }

        private static void PlanSplit(
            List<ResolutionPrepJob> jobs,
            string path,
            Size imageSize,
            int aspectWidth,
            int aspectHeight,
            IReadOnlyList<int> gears,
            ref int skippedGears)
        {
            foreach (int gear in gears)
            {
                Size? tile = TileSize(gear, aspectWidth, aspectHeight);
                if (tile == null)
                {
                    skippedGears++;
                    continue;
                }

                IReadOnlyList<Rectangle> windows = PlaceTiles(imageSize, tile.Value);
                if (windows.Count == 0)
                {
                    skippedGears++;
                    continue;
                }

                for (int i = 0; i < windows.Count; i++)
                {
                    Rectangle window = windows[i];
                    Size output = tile.Value;
                    Size? scaled = ScaleToLongEdge(window.Size, gear);
                    if (scaled != null)
                        output = scaled.Value;
                    jobs.Add(new ResolutionPrepJob
                    {
                        SourcePath = path,
                        SourceSize = imageSize,
                        SourceRect = window,
                        OutputSize = output,
                        Suffix = BuildSuffix(ResolutionPrepMode.SplitTiles, aspectWidth, aspectHeight, gear, i + 1)
                    });
                }
            }
        }

        private static IReadOnlyList<int> AxisPositions(int length, int tile)
        {
            if (length < tile || tile < 1)
                return Array.Empty<int>();

            var positions = new List<int>();
            int pos = 0;
            while (pos + tile <= length)
            {
                positions.Add(pos);
                pos += tile;
            }

            int last = length - tile;
            if (positions.Count == 0)
                return Array.Empty<int>();
            if (positions[positions.Count - 1] != last)
                positions.Add(last);
            return positions;
        }
    }
}
