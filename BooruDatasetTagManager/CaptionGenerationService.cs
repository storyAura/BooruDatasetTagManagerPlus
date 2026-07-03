using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace BooruDatasetTagManager
{
    public sealed class CaptionGenerationOptions
    {
        public string OutputSuffix { get; set; } = "_captioned";
        public bool SkipExisting { get; set; } = true;
        public bool AddTriggerToAi { get; set; } = true;
        public bool AddTagsToAi { get; set; } = true;
        public bool ReplaceUnderscores { get; set; } = true;
        public double MaxPixelsMegapixels { get; set; } = 1.0;
        public string TriggerWords { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public string OutputTemplate { get; set; } = "{caption}";
        public int MaxConcurrency { get; set; } = 5;
    }

    public sealed class CaptionModelRequest
    {
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "image/jpeg";
    }

    public sealed class CaptionModelResponse
    {
        public CaptionModelResponse(string result, string errorMessage)
        {
            Result = result;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public string Result { get; }
        public string ErrorMessage { get; }
    }

    public sealed class CaptionGenerationResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public bool Canceled { get; set; }
        public List<string> Errors { get; } = new List<string>();
    }

    public enum CaptionProgressStage
    {
        Scanning,
        Processing,
        Completed
    }

    public sealed class CaptionGenerationProgress
    {
        public CaptionProgressStage Stage { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    public sealed class CaptionScanResult
    {
        internal CaptionScanResult(
            string sourceRoot,
            string outputRoot,
            IReadOnlyList<string> files,
            IReadOnlySet<string> existingFiles)
        {
            SourceRoot = sourceRoot;
            OutputRoot = outputRoot;
            Files = files;
            ExistingFiles = existingFiles;
        }

        public string SourceRoot { get; }
        public string OutputRoot { get; }
        public int Total => Files.Count;
        public int Pending => Total - Existing;
        public int Existing => ExistingFiles.Count;
        public int Skipped => Existing;
        internal IReadOnlyList<string> Files { get; }
        internal IReadOnlySet<string> ExistingFiles { get; }
    }

    public sealed class CaptionGenerationService
    {
        private static readonly HashSet<string> imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp"
        };

        private readonly Func<CaptionModelRequest, CancellationToken, Task<CaptionModelResponse>> requestCaptionAsync;

        public CaptionGenerationService(Func<CaptionModelRequest, Task<CaptionModelResponse>> requestCaptionAsync)
            : this((request, _) => requestCaptionAsync(request))
        {
        }

        public CaptionGenerationService(Func<CaptionModelRequest, CancellationToken, Task<CaptionModelResponse>> requestCaptionAsync)
        {
            this.requestCaptionAsync = requestCaptionAsync ?? throw new ArgumentNullException(nameof(requestCaptionAsync));
        }

        public static Task<CaptionScanResult> ScanDirectoryAsync(
            string inputDirectory,
            string outputSuffix = "_captioned",
            IProgress<CaptionGenerationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(inputDirectory) || !Directory.Exists(inputDirectory))
                    throw new DirectoryNotFoundException("Input folder not found.");

                string sourceRoot = Path.GetFullPath(inputDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string outputRoot = GetOutputRoot(sourceRoot, outputSuffix);
                List<string> files = new List<string>();
                HashSet<string> existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int scanned = 0;

                foreach (string imagePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                    .Where(path => imageExtensions.Contains(Path.GetExtension(path)))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    scanned++;
                    files.Add(imagePath);
                    if (File.Exists(GetOutputTextPath(sourceRoot, imagePath, outputSuffix)))
                        existingFiles.Add(imagePath);

                    progress?.Report(new CaptionGenerationProgress
                    {
                        Stage = CaptionProgressStage.Scanning,
                        CurrentFile = imagePath,
                        Completed = scanned,
                        Skipped = existingFiles.Count
                    });
                }

                return new CaptionScanResult(sourceRoot, outputRoot, files, existingFiles);
            }, cancellationToken);
        }

        public async Task<CaptionGenerationResult> ProcessAsync(
            CaptionScanResult scan,
            CaptionGenerationOptions options,
            IProgress<CaptionGenerationProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (scan == null)
                throw new ArgumentNullException(nameof(scan));

            options ??= new CaptionGenerationOptions();
            CaptionGenerationResult result = new CaptionGenerationResult
            {
                Total = scan.Total,
                Skipped = options.SkipExisting ? scan.Existing : 0
            };

            int succeeded = 0;
            int failed = 0;
            object errorsLock = new object();
            List<string> filesToProcess = scan.Files
                .Where(imagePath => !options.SkipExisting || !scan.ExistingFiles.Contains(imagePath))
                .ToList();
            try
            {
                await Parallel.ForEachAsync(
                    filesToProcess,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(options.MaxConcurrency, 1, 100),
                        CancellationToken = cancellationToken
                    },
                    async (imagePath, itemCancellationToken) =>
                    {
                        try
                        {
                            List<string> tags = ReadTags(imagePath);
                            string originalTags = ReadOriginalTags(imagePath);
                            string userPrompt = CaptionPromptBuilder.BuildUserPrompt(tags, options.TriggerWords, options);
                            byte[] imageData = await CaptionImagePreprocessor.LoadJpegAsync(
                                imagePath,
                                options.MaxPixelsMegapixels,
                                itemCancellationToken);
                            CaptionModelResponse response = await requestCaptionAsync(new CaptionModelRequest
                            {
                                SystemPrompt = options.SystemPrompt,
                                UserPrompt = userPrompt,
                                ImageData = imageData,
                                ContentType = "image/jpeg"
                            }, itemCancellationToken);

                            itemCancellationToken.ThrowIfCancellationRequested();
                            if (string.IsNullOrWhiteSpace(response.Result))
                            {
                                throw new InvalidOperationException(
                                    string.IsNullOrWhiteSpace(response.ErrorMessage)
                                        ? "Empty caption result."
                                        : response.ErrorMessage);
                            }

                            string finalText = CaptionOutputFormatter.FormatOriginalTagsAndCaption(
                                originalTags,
                                response.Result);
                            await WriteOutputAsync(
                                scan.SourceRoot,
                                imagePath,
                                options.OutputSuffix,
                                finalText,
                                !options.SkipExisting,
                                itemCancellationToken);
                            Interlocked.Increment(ref succeeded);
                        }
                        catch (OperationCanceledException) when (itemCancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            lock (errorsLock)
                            {
                                if (result.Errors.Count < 20)
                                    result.Errors.Add($"{Path.GetFileName(imagePath)}: {ex.Message}");
                            }
                        }

                        ReportProgress(
                            progress,
                            result.Total,
                            Volatile.Read(ref succeeded),
                            result.Skipped,
                            Volatile.Read(ref failed),
                            imagePath,
                            CaptionProgressStage.Processing);
                    });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result.Canceled = true;
            }

            result.Succeeded = Volatile.Read(ref succeeded);
            result.Failed = Volatile.Read(ref failed);
            ReportProgress(progress, result, string.Empty, CaptionProgressStage.Completed);
            return result;
        }

        public async Task<CaptionGenerationResult> ProcessDirectoryAsync(
            string inputDirectory,
            CaptionGenerationOptions options,
            Action<string> progress = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new CaptionGenerationOptions();
            CaptionScanResult scan = await ScanDirectoryAsync(
                inputDirectory,
                options.OutputSuffix,
                cancellationToken: cancellationToken);
            IProgress<CaptionGenerationProgress> structuredProgress = progress == null
                ? null
                : new Progress<CaptionGenerationProgress>(value =>
                {
                    if (!string.IsNullOrEmpty(value.CurrentFile))
                        progress(Path.GetFileName(value.CurrentFile));
                });
            return await ProcessAsync(scan, options, structuredProgress, cancellationToken);
        }

        public static string GetOutputRoot(string sourceRoot, string outputSuffix)
        {
            string fullRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(fullRoot);
            string suffix = string.IsNullOrEmpty(outputSuffix) ? "_captioned" : outputSuffix;
            return Path.Combine(parent ?? string.Empty, Path.GetFileName(fullRoot) + suffix);
        }

        public static string GetOutputTextPath(string sourceRoot, string imagePath, string outputSuffix)
        {
            return Path.ChangeExtension(GetOutputImagePath(sourceRoot, imagePath, outputSuffix), ".txt");
        }

        public static string GetOutputImagePath(string sourceRoot, string imagePath, string outputSuffix)
        {
            string fullRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullImage = Path.GetFullPath(imagePath);
            string outputRoot = GetOutputRoot(fullRoot, outputSuffix);
            string relative = Path.GetRelativePath(fullRoot, fullImage);
            return Path.Combine(outputRoot, relative);
        }

        private static void ReportProgress(
            IProgress<CaptionGenerationProgress> progress,
            CaptionGenerationResult result,
            string currentFile,
            CaptionProgressStage stage)
        {
            progress?.Report(new CaptionGenerationProgress
            {
                Stage = stage,
                CurrentFile = currentFile,
                Total = result.Total,
                Completed = result.Succeeded + result.Skipped + result.Failed,
                Succeeded = result.Succeeded,
                Skipped = result.Skipped,
                Failed = result.Failed
            });
        }

        private static void ReportProgress(
            IProgress<CaptionGenerationProgress> progress,
            int total,
            int succeeded,
            int skipped,
            int failed,
            string currentFile,
            CaptionProgressStage stage)
        {
            progress?.Report(new CaptionGenerationProgress
            {
                Stage = stage,
                CurrentFile = currentFile,
                Total = total,
                Completed = succeeded + skipped + failed,
                Succeeded = succeeded,
                Skipped = skipped,
                Failed = failed
            });
        }

        private static async Task WriteOutputAsync(
            string sourceRoot,
            string imagePath,
            string outputSuffix,
            string text,
            bool overwriteText,
            CancellationToken cancellationToken)
        {
            string outputText = GetOutputTextPath(sourceRoot, imagePath, outputSuffix);
            string outputImage = GetOutputImagePath(sourceRoot, imagePath, outputSuffix);
            string outputDirectory = Path.GetDirectoryName(outputText);
            Directory.CreateDirectory(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            string tempText = outputText + ".tmp-" + Guid.NewGuid().ToString("N");
            string tempImage = outputImage + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(tempText, text, cancellationToken);
                if (!File.Exists(outputImage))
                {
                    await using FileStream source = new FileStream(
                        imagePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        81920,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using FileStream destination = new FileStream(
                        tempImage,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        FileOptions.Asynchronous);
                    await source.CopyToAsync(destination, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(tempImage))
                    File.Move(tempImage, outputImage, false);
                File.Move(tempText, outputText, overwriteText);
            }
            finally
            {
                if (File.Exists(tempText))
                    File.Delete(tempText);
                if (File.Exists(tempImage))
                    File.Delete(tempImage);
            }
        }

        private static List<string> ReadTags(string imagePath)
        {
            string tagFile = Path.ChangeExtension(imagePath, ".txt");
            if (!File.Exists(tagFile))
                return new List<string>();
            return File.ReadAllText(tagFile)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .ToList();
        }

        private static string ReadOriginalTags(string imagePath)
        {
            string tagFile = Path.ChangeExtension(imagePath, ".txt");
            return File.Exists(tagFile) ? File.ReadAllText(tagFile) : string.Empty;
        }
    }

    public static class CaptionPromptBuilder
    {
        public static string BuildUserPrompt(IEnumerable<string> tags, string triggerWords, CaptionGenerationOptions options)
        {
            options ??= new CaptionGenerationOptions();
            List<string> parts = new List<string>();

            if (options.AddTriggerToAi && !string.IsNullOrWhiteSpace(triggerWords))
                parts.Add($"Target Subject/Trigger Concept:\n{triggerWords}");

            var cleaned = CleanAndFormatTags(tags ?? Enumerable.Empty<string>(), options.ReplaceUnderscores);
            if (options.AddTagsToAi && cleaned.Count > 0)
                parts.Add($"Reference tags:\n[{string.Join(", ", cleaned)}]");

            parts.Add("Global Requirements:\n- Write one coherent natural language paragraph in English.\n- Do NOT output comma-separated tags; reference tags are hints only.\n- Output ONLY the requested content.\n- NO markdown formatting.\n- NO numbered lists or bullet points.\n- NO conversational filler.");
            return string.Join("\n\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public static List<string> CleanAndFormatTags(IEnumerable<string> tags, bool replaceUnderscores)
        {
            return tags
                .Select(tag => tag?.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.ToLowerInvariant())
                .Where(tag => tag is not ("masterpiece" or "best quality" or "highres" or "absurdres"))
                .Select(tag => replaceUnderscores && tag.Length > 3 ? tag.Replace('_', ' ') : tag)
                .OrderBy(TagPriority)
                .ToList();
        }

        private static int TagPriority(string tag)
        {
            if (tag is "safe" or "sensitive" or "nsfw" or "explicit")
                return 0;
            if (Regex.IsMatch(tag, @"^\d+(girl|boy)s?$"))
                return 1;
            if (tag is "solo" or "1girl" or "1boy")
                return 2;
            return 100;
        }
    }

    public static class CaptionOutputFormatter
    {
        public static string Format(string template, string triggerWords, string tags, string caption)
        {
            string thinkOpen = "<" + "think" + ">";
            string thinkClose = "</" + "think" + ">";
            string thinkPattern = Regex.Escape(thinkOpen) + ".*?" + Regex.Escape(thinkClose);
            string cleanedCaption = Regex.Replace(caption ?? string.Empty, thinkPattern, string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string output = (string.IsNullOrWhiteSpace(template) ? "{caption}" : template)
                .Replace("{trigger_words}", triggerWords ?? string.Empty)
                .Replace("{tags}", tags ?? string.Empty)
                .Replace("{caption}", cleanedCaption);
            output = Regex.Replace(output, @"[\s,]{2,}(?=[,\.])", string.Empty);
            output = Regex.Replace(output, @"^[\s,\.]+", string.Empty);
            output = Regex.Replace(output, @",\s*\.", ".");
            output = Regex.Replace(output, @"\n{3,}", "\n\n");
            return output.Trim();
        }

        public static string FormatOriginalTagsAndCaption(string originalTags, string caption)
        {
            string cleanedCaption = RemoveThinkBlocks(caption).Trim();
            if (string.IsNullOrWhiteSpace(originalTags))
                return cleanedCaption;

            string tagsWithoutTrailingNewlines = originalTags.TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(tagsWithoutTrailingNewlines))
                return cleanedCaption;
            return tagsWithoutTrailingNewlines + Environment.NewLine + cleanedCaption;
        }

        private static string RemoveThinkBlocks(string caption)
        {
            string thinkOpen = "<" + "think" + ">";
            string thinkClose = "</" + "think" + ">";
            string thinkPattern = Regex.Escape(thinkOpen) + ".*?" + Regex.Escape(thinkClose);
            return Regex.Replace(
                caption ?? string.Empty,
                thinkPattern,
                string.Empty,
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

    }


    public static class CaptionImagePreprocessor
    {
        public static async Task<byte[]> LoadJpegAsync(string imagePath, double maxPixelsMegapixels, CancellationToken cancellationToken = default)
        {
            using ImageSharpImage image = await ImageSharpImage.LoadAsync(imagePath, cancellationToken);
            double maxPixels = Math.Max(0.1, maxPixelsMegapixels) * 1_000_000;
            double currentPixels = image.Width * image.Height;
            if (currentPixels > maxPixels)
            {
                double scale = Math.Sqrt(maxPixels / currentPixels);
                int width = Math.Max(1, (int)(image.Width * scale));
                int height = Math.Max(1, (int)(image.Height * scale));
                image.Mutate(ctx => ctx.Resize(width, height));
            }

            using MemoryStream stream = new MemoryStream();
            await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 90 }, cancellationToken);
            return stream.ToArray();
        }
    }
}
