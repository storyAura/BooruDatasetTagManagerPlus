using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace BooruDatasetTagManager
{
    public sealed class PreBucketSettings
    {
        public int ResolutionWidth { get; set; } = PreBucketMath.DefaultResolution;
        public int ResolutionHeight { get; set; } = PreBucketMath.DefaultResolution;
        public bool EnableBucket { get; set; } = true;
        public int MinBucketReso { get; set; } = PreBucketMath.DefaultMinReso;
        public int MaxBucketReso { get; set; } = PreBucketMath.DefaultMaxReso;
        public int BucketResoSteps { get; set; } = PreBucketMath.DefaultSteps;
        public int TargetBucketCount { get; set; }
        public bool AllowUpscale { get; set; }
        public int Repeats { get; set; } = 1;
        public int BatchSize { get; set; } = 4;
        public int Epochs { get; set; } = 1;
        public string OutputRoot { get; set; } = string.Empty;
    }

    public sealed class PreBucketJob
    {
        public string SourcePath { get; init; }
        public Size SourceSize { get; init; }
        public Size BucketSize { get; init; }
        public Size FittedSize { get; init; }
        public Point DrawOffset { get; init; }
        public string OutputPath { get; set; }

        public string FolderName => PreBucketMath.FolderName(BucketSize);
    }

    public sealed class PreBucketGroup
    {
        public Size Size { get; init; }
        public int Count { get; init; }
        public double AspectRatio => Size.Height == 0 ? 0 : (double)Size.Width / Size.Height;
        public string FolderName => PreBucketMath.FolderName(Size);
    }

    public sealed class PreBucketPlan
    {
        public IReadOnlyList<PreBucketJob> Jobs { get; init; } = Array.Empty<PreBucketJob>();
        public IReadOnlyList<PreBucketGroup> Groups { get; init; } = Array.Empty<PreBucketGroup>();
        public IReadOnlyList<Size> PredefinedResolutions { get; init; } = Array.Empty<Size>();
        public IReadOnlyList<Size> SelectedResolutions { get; init; } = Array.Empty<Size>();
        public int ImageCount { get; init; }
        public int SkippedImages { get; init; }
        public int KohyaUsedCount { get; init; }
        public int TheoreticalSteps { get; init; }
        public int BucketedSteps { get; init; }
    }

    /// <summary>
    /// Kohya / sd-scripts ARB math, letterbox geometry, and target-count
    /// reduction. Kept UI-free so the form can stay out of the test project.
    /// </summary>
    public static class PreBucketMath
    {
        public const int DefaultResolution = 1536;
        public const int DefaultMinReso = 256;
        public const int DefaultMaxReso = 4096;
        public const int DefaultSteps = 64;
        public const int MinDimension = 64;
        public const int MaxDimension = 8192;
        public const int MinSteps = 8;
        public const int MaxSteps = 512;
        public const int MinTarget = 0;
        public const int MaxTarget = 256;
        public const int MinRepeats = 1;
        public const int MaxRepeats = 1000;
        public const int MinBatch = 1;
        public const int MaxBatch = 256;
        public const int MinEpochs = 1;
        public const int MaxEpochs = 10000;

        public static IReadOnlyList<string> ResolveSourcePaths(
            ResolutionPrepSource source,
            IReadOnlyList<string> selected,
            IReadOnlyList<string> folder,
            IReadOnlyList<string> all)
        {
            return ResolutionPrepMath.ResolveSourcePaths(source, selected, folder, all);
        }

        public static int ClampSteps(int steps)
        {
            if (steps < MinSteps)
                return MinSteps;
            if (steps > MaxSteps)
                return MaxSteps;
            return steps;
        }

        public static int AlignDown(int value, int steps)
        {
            int unit = ClampSteps(steps);
            if (value < unit)
                return 0;
            return value - (value % unit);
        }

        public static int AlignUp(int value, int steps)
        {
            int unit = ClampSteps(steps);
            if (value <= 0)
                return unit;
            int rem = value % unit;
            if (rem == 0)
                return value;
            long aligned = (long)value + unit - rem;
            return aligned > MaxDimension ? AlignDown(MaxDimension, unit) : (int)aligned;
        }

        /// <summary>
        /// Kohya <c>adjust_min_max_bucket_reso_by_steps</c>: min snaps down,
        /// max snaps up, both stay inside the usable range.
        /// </summary>
        public static void AdjustMinMaxBySteps(int steps, ref int minReso, ref int maxReso)
        {
            int unit = ClampSteps(steps);
            minReso = AlignDown(minReso, unit);
            if (minReso < MinDimension)
                minReso = AlignUp(MinDimension, unit);
            maxReso = AlignUp(maxReso, unit);
            if (maxReso > MaxDimension)
                maxReso = AlignDown(MaxDimension, unit);
            if (maxReso < minReso)
                maxReso = minReso;
        }

        public static Size NormalizeResolution(int width, int height, int steps)
        {
            int unit = ClampSteps(steps);
            int w = AlignDown(width, unit);
            int h = AlignDown(height, unit);
            if (w < MinDimension)
                w = AlignDown(DefaultResolution, unit);
            if (h < MinDimension)
                h = AlignDown(DefaultResolution, unit);
            if (w < MinDimension)
                w = unit;
            if (h < MinDimension)
                h = unit;
            if (w > MaxDimension)
                w = AlignDown(MaxDimension, unit);
            if (h > MaxDimension)
                h = AlignDown(MaxDimension, unit);
            return new Size(w, h);
        }

        public static PreBucketSettings Normalize(PreBucketSettings settings)
        {
            settings ??= new PreBucketSettings();
            int steps = ClampSteps(settings.BucketResoSteps);
            Size reso = NormalizeResolution(settings.ResolutionWidth, settings.ResolutionHeight, steps);
            int min = settings.MinBucketReso;
            int max = settings.MaxBucketReso;
            AdjustMinMaxBySteps(steps, ref min, ref max);
            int longest = Math.Max(reso.Width, reso.Height);
            if (max < longest)
                max = AlignUp(longest, steps);

            int target = settings.TargetBucketCount;
            if (target < MinTarget)
                target = MinTarget;
            if (target > MaxTarget)
                target = MaxTarget;
            if (!settings.EnableBucket)
                target = 0;

            return new PreBucketSettings
            {
                ResolutionWidth = reso.Width,
                ResolutionHeight = reso.Height,
                EnableBucket = settings.EnableBucket,
                MinBucketReso = min,
                MaxBucketReso = max,
                BucketResoSteps = steps,
                TargetBucketCount = target,
                AllowUpscale = settings.AllowUpscale,
                Repeats = ClampRange(settings.Repeats, MinRepeats, MaxRepeats),
                BatchSize = ClampRange(settings.BatchSize, MinBatch, MaxBatch),
                Epochs = ClampRange(settings.Epochs, MinEpochs, MaxEpochs),
                OutputRoot = settings.OutputRoot ?? string.Empty
            };
        }

        /// <summary>
        /// Kohya <c>model_util.make_bucket_resolutions</c>.
        /// </summary>
        public static IReadOnlyList<Size> MakeBucketResolutions(Size maxReso, int minSize, int maxSize, int steps)
        {
            int unit = ClampSteps(steps);
            Size reso = NormalizeResolution(maxReso.Width, maxReso.Height, unit);
            int min = minSize;
            int max = maxSize;
            AdjustMinMaxBySteps(unit, ref min, ref max);
            if (max < Math.Max(reso.Width, reso.Height))
                max = AlignUp(Math.Max(reso.Width, reso.Height), unit);

            long maxArea = (long)reso.Width * reso.Height;
            var set = new HashSet<(int W, int H)>();

            int square = (int)(Math.Floor(Math.Sqrt(maxArea) / unit) * unit);
            if (square >= min && square <= max)
                set.Add((square, square));

            for (int width = min; width <= max; width += unit)
            {
                int height = (int)Math.Min(max, (maxArea / width / unit) * unit);
                if (height < min)
                    continue;
                set.Add((width, height));
                set.Add((height, width));
            }

            var list = new List<Size>(set.Count);
            foreach ((int w, int h) in set)
                list.Add(new Size(w, h));
            list.Sort(CompareSize);
            return list;
        }

        public static Size SelectClosest(Size image, IReadOnlyList<Size> buckets)
        {
            if (buckets == null || buckets.Count == 0 || image.Width < 1 || image.Height < 1)
                return Size.Empty;

            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Width == image.Width && buckets[i].Height == image.Height)
                    return buckets[i];
            }

            double aspect = (double)image.Width / image.Height;
            Size best = buckets[0];
            double bestError = double.MaxValue;
            foreach (Size bucket in buckets)
            {
                if (bucket.Width < 1 || bucket.Height < 1)
                    continue;
                double error = Math.Abs((double)bucket.Width / bucket.Height - aspect);
                if (error < bestError - 1e-12)
                {
                    bestError = error;
                    best = bucket;
                }
            }
            return best;
        }

        /// <summary>
        /// Merge adjacent-AR used buckets until <paramref name="targetCount"/>
        /// remain. Target 0 means "keep every used Kohya bucket".
        /// </summary>
        public static IReadOnlyList<Size> ReduceToTarget(
            IReadOnlyList<Size> usedBuckets,
            IReadOnlyList<Size> imageSizes,
            int targetCount)
        {
            if (usedBuckets == null || usedBuckets.Count == 0)
                return Array.Empty<Size>();

            var current = new List<Size>(usedBuckets);
            current.Sort(CompareByAspect);
            if (targetCount <= 0 || targetCount >= current.Count)
                return current;

            var assigned = new List<int>();
            if (imageSizes != null)
            {
                foreach (Size image in imageSizes)
                {
                    if (image.Width < 1 || image.Height < 1)
                        continue;
                    assigned.Add(IndexOfClosest(image, current));
                }
            }

            while (current.Count > targetCount)
            {
                int mergeAt = FindCheapestAdjacentMerge(current, assigned);
                Size survivor = PickSurvivor(current[mergeAt], current[mergeAt + 1], assigned, mergeAt);
                current[mergeAt] = survivor;
                current.RemoveAt(mergeAt + 1);
                for (int i = 0; i < assigned.Count; i++)
                {
                    if (assigned[i] == mergeAt + 1)
                        assigned[i] = mergeAt;
                    else if (assigned[i] > mergeAt + 1)
                        assigned[i]--;
                }
            }

            return current;
        }

        public static Size FitInside(Size source, Size bucket, bool allowUpscale)
        {
            if (source.Width < 1 || source.Height < 1 || bucket.Width < 1 || bucket.Height < 1)
                return Size.Empty;

            double scale = Math.Min(
                (double)bucket.Width / source.Width,
                (double)bucket.Height / source.Height);
            if (!allowUpscale && scale > 1)
                scale = 1;

            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            if (width > bucket.Width)
                width = bucket.Width;
            if (height > bucket.Height)
                height = bucket.Height;
            return new Size(width, height);
        }

        public static Point CenterOffset(Size fitted, Size bucket)
        {
            int x = (bucket.Width - fitted.Width) / 2;
            int y = (bucket.Height - fitted.Height) / 2;
            if (x < 0)
                x = 0;
            if (y < 0)
                y = 0;
            return new Point(x, y);
        }

        public static string FolderName(Size size)
        {
            return size.Width + "x" + size.Height;
        }

        public static int TheoreticalSteps(int images, int repeats, int batch, int epochs)
        {
            repeats = ClampRange(repeats, MinRepeats, MaxRepeats);
            batch = ClampRange(batch, MinBatch, MaxBatch);
            epochs = ClampRange(epochs, MinEpochs, MaxEpochs);
            if (images <= 0)
                return 0;
            // ceil((images × repeats) / batch) × epochs — integer division
            // would show 0 whenever batch is larger than the image count.
            return (images * repeats + batch - 1) / batch * epochs;
        }

        public static int BucketedSteps(IEnumerable<int> bucketCounts, int repeats, int batch, int epochs)
        {
            repeats = ClampRange(repeats, MinRepeats, MaxRepeats);
            batch = ClampRange(batch, MinBatch, MaxBatch);
            epochs = ClampRange(epochs, MinEpochs, MaxEpochs);
            if (bucketCounts == null)
                return 0;

            int perEpoch = 0;
            foreach (int count in bucketCounts)
            {
                if (count <= 0)
                    continue;
                perEpoch += (count * repeats + batch - 1) / batch;
            }
            return perEpoch * epochs;
        }

        public static string AllocateOutputPath(
            string outputRoot,
            Size bucket,
            string sourcePath,
            ISet<string> reserved,
            Func<string, bool> exists)
        {
            if (string.IsNullOrWhiteSpace(outputRoot) || string.IsNullOrWhiteSpace(sourcePath))
                return null;

            string root = Path.GetFullPath(outputRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string folder = FolderName(bucket);
            if (!DatasetFolderIndex.IsSafeRelativeFolder(folder))
                return null;

            string directory = Path.GetFullPath(Path.Combine(root, folder));
            if (!DatasetFolderIndex.IsUnderRoot(root, directory))
                return null;

            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName == "." || fileName == "..")
                return null;

            string baseName = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
                extension = ".png";
            if (string.IsNullOrEmpty(baseName))
                baseName = "image";

            exists ??= (_ => false);
            reserved ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 1; ; index++)
            {
                string extra = index == 1 ? string.Empty : index.ToString();
                string candidate = Path.Combine(directory, baseName + extra + extension);
                if (reserved.Contains(candidate) || exists(candidate))
                    continue;
                reserved.Add(candidate);
                return candidate;
            }
        }

        /// <summary>
        /// Sources that were written to a different path and can be deleted
        /// after the new files land. Failed writes and in-place writes stay.
        /// </summary>
        public static IReadOnlyList<string> CollectRemovableSources(
            IEnumerable<PreBucketJob> jobs,
            IEnumerable<string> writtenOutputs)
        {
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (writtenOutputs != null)
            {
                foreach (string path in writtenOutputs)
                    TryAddFullPath(written, path);
            }

            var removable = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (jobs == null)
                return removable;

            foreach (PreBucketJob job in jobs)
            {
                if (job == null
                    || string.IsNullOrWhiteSpace(job.SourcePath)
                    || string.IsNullOrWhiteSpace(job.OutputPath))
                {
                    continue;
                }

                if (!TryGetFullPath(job.SourcePath, out string source)
                    || !TryGetFullPath(job.OutputPath, out string output))
                {
                    continue;
                }

                if (!written.Contains(output) || written.Contains(source))
                    continue;
                if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(source))
                    removable.Add(source);
            }

            return removable;
        }

        public static IReadOnlyList<string> CollectSourceDirectories(IEnumerable<string> sourcePaths)
        {
            var directories = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sourcePaths == null)
                return directories;

            foreach (string path in sourcePaths)
            {
                if (!TryGetFullPath(path, out string full))
                    continue;
                string directory = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(directory) && seen.Add(directory))
                    directories.Add(directory);
            }

            return directories;
        }

        private static bool TryGetFullPath(string path, out string full)
        {
            full = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            try
            {
                full = Path.GetFullPath(path);
                return !string.IsNullOrEmpty(full);
            }
            catch
            {
                return false;
            }
        }

        private static void TryAddFullPath(ISet<string> set, string path)
        {
            if (TryGetFullPath(path, out string full))
                set.Add(full);
        }

        public static PreBucketPlan Plan(
            IEnumerable<(string Path, Size? Size)> items,
            PreBucketSettings settings)
        {
            PreBucketSettings normalized = Normalize(settings);
            var jobs = new List<PreBucketJob>();
            var imageSizes = new List<Size>();
            var validItems = new List<(string Path, Size Size)>();
            int imageCount = 0;
            int skipped = 0;

            if (items != null)
            {
                foreach ((string path, Size? size) in items)
                {
                    imageCount++;
                    if (string.IsNullOrEmpty(path) || VideoProcessingService.IsVideoFile(path))
                    {
                        skipped++;
                        continue;
                    }
                    if (size is not Size imageSize || imageSize.Width < 1 || imageSize.Height < 1)
                    {
                        skipped++;
                        continue;
                    }
                    validItems.Add((path, imageSize));
                    imageSizes.Add(imageSize);
                }
            }

            IReadOnlyList<Size> predefined;
            if (normalized.EnableBucket)
            {
                predefined = MakeBucketResolutions(
                    new Size(normalized.ResolutionWidth, normalized.ResolutionHeight),
                    normalized.MinBucketReso,
                    normalized.MaxBucketReso,
                    normalized.BucketResoSteps);
            }
            else
            {
                predefined = new[]
                {
                    new Size(normalized.ResolutionWidth, normalized.ResolutionHeight)
                };
            }

            var kohyaUsed = new List<Size>();
            var kohyaSeen = new HashSet<(int, int)>();
            foreach (Size image in imageSizes)
            {
                Size bucket = SelectClosest(image, predefined);
                if (bucket.Width < 1)
                    continue;
                if (kohyaSeen.Add((bucket.Width, bucket.Height)))
                    kohyaUsed.Add(bucket);
            }
            kohyaUsed.Sort(CompareByAspect);

            IReadOnlyList<Size> selected = ReduceToTarget(
                kohyaUsed,
                imageSizes,
                normalized.EnableBucket ? normalized.TargetBucketCount : 1);

            var counts = new Dictionary<(int, int), int>();
            foreach ((string path, Size imageSize) in validItems)
            {
                Size bucket = SelectClosest(imageSize, selected.Count > 0 ? selected : predefined);
                if (bucket.Width < 1)
                {
                    skipped++;
                    continue;
                }

                Size fitted = FitInside(imageSize, bucket, normalized.AllowUpscale);
                if (fitted.Width < 1)
                {
                    skipped++;
                    continue;
                }

                (int, int) key = (bucket.Width, bucket.Height);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;

                jobs.Add(new PreBucketJob
                {
                    SourcePath = path,
                    SourceSize = imageSize,
                    BucketSize = bucket,
                    FittedSize = fitted,
                    DrawOffset = CenterOffset(fitted, bucket)
                });
            }

            var groups = new List<PreBucketGroup>();
            foreach (Size reso in selected)
            {
                counts.TryGetValue((reso.Width, reso.Height), out int count);
                if (count <= 0)
                    continue;
                groups.Add(new PreBucketGroup { Size = reso, Count = count });
            }
            groups.Sort((a, b) => CompareByAspect(a.Size, b.Size));

            var groupCounts = new List<int>();
            foreach (PreBucketGroup group in groups)
                groupCounts.Add(group.Count);

            return new PreBucketPlan
            {
                Jobs = jobs,
                Groups = groups,
                PredefinedResolutions = predefined,
                SelectedResolutions = selected,
                ImageCount = imageCount,
                SkippedImages = skipped,
                KohyaUsedCount = kohyaUsed.Count,
                TheoreticalSteps = TheoreticalSteps(
                    jobs.Count, normalized.Repeats, normalized.BatchSize, normalized.Epochs),
                BucketedSteps = BucketedSteps(
                    groupCounts, normalized.Repeats, normalized.BatchSize, normalized.Epochs)
            };
        }

        private static int FindCheapestAdjacentMerge(IReadOnlyList<Size> buckets, IReadOnlyList<int> assigned)
        {
            int bestIndex = 0;
            double bestCost = double.MaxValue;
            for (int i = 0; i < buckets.Count - 1; i++)
            {
                double arA = Aspect(buckets[i]);
                double arB = Aspect(buckets[i + 1]);
                int countA = 0;
                int countB = 0;
                foreach (int index in assigned)
                {
                    if (index == i)
                        countA++;
                    else if (index == i + 1)
                        countB++;
                }
                double cost = Math.Abs(arA - arB) * (1 + Math.Min(countA, countB));
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static Size PickSurvivor(Size left, Size right, IReadOnlyList<int> assigned, int leftIndex)
        {
            double target = WeightedAspect(left, right, assigned, leftIndex);
            double leftError = Math.Abs(Aspect(left) - target);
            double rightError = Math.Abs(Aspect(right) - target);
            return leftError <= rightError ? left : right;
        }

        private static double WeightedAspect(Size left, Size right, IReadOnlyList<int> assigned, int leftIndex)
        {
            int countA = 0;
            int countB = 0;
            foreach (int index in assigned)
            {
                if (index == leftIndex)
                    countA++;
                else if (index == leftIndex + 1)
                    countB++;
            }
            if (countA + countB == 0)
                return (Aspect(left) + Aspect(right)) / 2;
            return (Aspect(left) * countA + Aspect(right) * countB) / (countA + countB);
        }

        private static int IndexOfClosest(Size image, IReadOnlyList<Size> buckets)
        {
            Size best = SelectClosest(image, buckets);
            for (int i = 0; i < buckets.Count; i++)
            {
                if (buckets[i].Width == best.Width && buckets[i].Height == best.Height)
                    return i;
            }
            return 0;
        }

        private static double Aspect(Size size)
        {
            return size.Height == 0 ? 0 : (double)size.Width / size.Height;
        }

        private static int CompareByAspect(Size a, Size b)
        {
            int cmp = Aspect(a).CompareTo(Aspect(b));
            if (cmp != 0)
                return cmp;
            return CompareSize(a, b);
        }

        private static int CompareSize(Size a, Size b)
        {
            int cmp = a.Width.CompareTo(b.Width);
            return cmp != 0 ? cmp : a.Height.CompareTo(b.Height);
        }

        private static int ClampRange(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
