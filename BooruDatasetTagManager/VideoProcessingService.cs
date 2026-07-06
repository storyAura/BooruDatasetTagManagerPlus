using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public enum VideoConvertCodec
    {
        Copy,
        H264,
        H265
    }

    public enum FrameExtractMode
    {
        All,
        ByFps,
        NativeFps,
        Specific
    }

    public sealed class VideoInfo
    {
        public double Fps { get; set; }
        public double DurationSeconds { get; set; }
        public int FrameCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class VideoProcessResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
        public int OutputFileCount { get; set; }
    }

    public sealed class VideoProcessingService
    {
        private static readonly Regex FrameProgressRegex = new Regex(@"frame=\s*(?<frame>\d+)", RegexOptions.Compiled);
        private static readonly Regex TimeProgressRegex = new Regex(@"time=(?<time>[\d:.]+)", RegexOptions.Compiled);
        private static readonly string[] SupportedVideoExtensions = { ".mp4", ".flv", ".mkv", ".ts", ".avi", ".webm", ".mov" };

        private readonly FfmpegLocator locator;

        public VideoProcessingService(FfmpegLocator locator)
        {
            this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        }

        public static VideoProcessingService CreateDefault()
        {
            return new VideoProcessingService(FfmpegLocator.FromSettings());
        }

        public static bool IsVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return SupportedVideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        public static IReadOnlyList<string> GetSupportedOutputFormats()
        {
            return new[] { "mp4", "mkv", "avi", "webm", "mov", "flv" };
        }

        public static double ParseFpsString(string fpsText)
        {
            if (string.IsNullOrWhiteSpace(fpsText) || fpsText == "0/0")
                return 0;

            int slashIndex = fpsText.IndexOf('/');
            if (slashIndex > 0)
            {
                if (double.TryParse(fpsText.AsSpan(0, slashIndex), NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator)
                    && double.TryParse(fpsText.AsSpan(slashIndex + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator)
                    && denominator > 0)
                {
                    return numerator / denominator;
                }
            }

            if (double.TryParse(fpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) && fps > 0)
                return fps;

            return 0;
        }

        public static ParsedFrameSelection ParseFrameSelection(string input)
        {
            var result = new ParsedFrameSelection();
            if (string.IsNullOrWhiteSpace(input))
                return result;

            foreach (string token in input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frameIndex))
                {
                    result.FrameNumbers.Add(Math.Max(0, frameIndex));
                    continue;
                }

                if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan timestamp)
                    || TimeSpan.TryParseExact(trimmed, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out timestamp)
                    || TimeSpan.TryParseExact(trimmed, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out timestamp)
                    || TimeSpan.TryParseExact(trimmed, @"mm\:ss", CultureInfo.InvariantCulture, out timestamp))
                {
                    result.Timestamps.Add(timestamp);
                    continue;
                }

                result.InvalidTokens.Add(trimmed);
            }

            result.FrameNumbers = result.FrameNumbers.Distinct().OrderBy(v => v).ToList();
            result.Timestamps = result.Timestamps.Distinct().OrderBy(v => v).ToList();
            return result;
        }

        public string GetConvertOutputPath(string inputPath, string targetFormat, bool replaceOriginal)
        {
            if (replaceOriginal)
                return inputPath;

            string directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string ext = targetFormat.TrimStart('.').ToLowerInvariant();
            return Path.Combine(directory, baseName + "_converted." + ext);
        }

        public string GetDefaultConvertOutputPath(string inputPath, string targetFormat)
        {
            return GetConvertOutputPath(inputPath, targetFormat, false);
        }

        public string GetFlatExtractOutputDirectory(string inputPath)
        {
            return Path.GetDirectoryName(inputPath) ?? string.Empty;
        }

        public string GetDefaultExtractOutputDirectory(string inputPath)
        {
            return GetFlatExtractOutputDirectory(inputPath);
        }

        public string GetFlatExtractOutputPattern(string inputPath, string imageFormat)
        {
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string ext = NormalizeImageExtension(imageFormat);
            return Path.Combine(GetFlatExtractOutputDirectory(inputPath), baseName + "_frame_%06d." + ext);
        }

        public IReadOnlyList<string> ListExtractedFrameFiles(string inputPath, string imageFormat)
        {
            string directory = GetFlatExtractOutputDirectory(inputPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            string ext = NormalizeImageExtension(imageFormat);
            string searchPattern = baseName + "_frame_*." + ext;
            return Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string FilterProgressLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            if (line.IndexOf("Press [q]", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("frame=", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("time=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return string.Empty;
            }

            Match frameMatch = FrameProgressRegex.Match(line);
            if (frameMatch.Success)
                return "frame=" + frameMatch.Groups["frame"].Value;

            Match timeMatch = TimeProgressRegex.Match(line);
            if (timeMatch.Success)
                return "time=" + timeMatch.Groups["time"].Value;

            return string.Empty;
        }

        public async Task<VideoProcessResult> ConvertAsync(
            string inputPath,
            string outputPath,
            VideoConvertCodec codec,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i", Quote(inputPath)
            };

            switch (codec)
            {
                case VideoConvertCodec.Copy:
                    args.AddRange(new[] { "-c", "copy" });
                    break;
                case VideoConvertCodec.H265:
                    args.AddRange(new[] { "-c:v", "libx265", "-c:a", "aac" });
                    break;
                default:
                    args.AddRange(new[] { "-c:v", "libx264", "-c:a", "aac" });
                    break;
            }

            args.Add(Quote(outputPath));
            return await RunFfmpegAsync(args, outputPath, progress, cancellationToken).ConfigureAwait(false);
        }

        public async Task<VideoProcessResult> ExtractFramesAsync(
            string inputPath,
            string outputDirectory,
            FrameExtractMode mode,
            double fps,
            string specificSelection,
            string imageFormat,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);
            string ext = NormalizeImageExtension(imageFormat);
            string outputPattern = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + "_frame_%06d." + ext);

            switch (mode)
            {
                case FrameExtractMode.All:
                    return await ExtractAllFramesAsync(inputPath, outputPattern, progress, cancellationToken).ConfigureAwait(false);
                case FrameExtractMode.ByFps:
                    return await ExtractByFpsAsync(inputPath, outputPattern, fps, progress, cancellationToken).ConfigureAwait(false);
                case FrameExtractMode.NativeFps:
                {
                    VideoInfo info = await GetVideoInfoAsync(inputPath, cancellationToken).ConfigureAwait(false);
                    double nativeFps = info.Fps > 0 ? info.Fps : fps;
                    return await ExtractByFpsAsync(inputPath, outputPattern, nativeFps, progress, cancellationToken).ConfigureAwait(false);
                }
                case FrameExtractMode.Specific:
                    return await ExtractSpecificFramesAsync(inputPath, outputDirectory, ext, specificSelection, progress, cancellationToken).ConfigureAwait(false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public async Task<VideoInfo> GetVideoInfoAsync(string inputPath, CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            var args = new List<string>
            {
                "-hide_banner",
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=width,height,avg_frame_rate,r_frame_rate,nb_frames",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1",
                Quote(inputPath)
            };

            var result = await RunProcessAsync(locator.FfprobeExe, args, null, cancellationToken).ConfigureAwait(false);
            var info = new VideoInfo();
            foreach (string rawLine in (result.StdOut ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                int eqIndex = line.IndexOf('=');
                if (eqIndex <= 0)
                    continue;

                string key = line.Substring(0, eqIndex);
                string value = line.Substring(eqIndex + 1);
                switch (key)
                {
                    case "width":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
                            info.Width = width;
                        break;
                    case "height":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                            info.Height = height;
                        break;
                    case "avg_frame_rate":
                    case "r_frame_rate":
                        if (info.Fps <= 0)
                        {
                            double parsed = ParseFpsString(value);
                            if (parsed > 0)
                                info.Fps = parsed;
                        }
                        break;
                    case "nb_frames":
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long nbFrames) && nbFrames > 0)
                            info.FrameCount = (int)Math.Min(int.MaxValue, nbFrames);
                        break;
                    case "duration":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration) && duration > 0)
                            info.DurationSeconds = duration;
                        break;
                }
            }

            if (info.DurationSeconds <= 0)
                info.DurationSeconds = await GetDurationSecondsAsync(inputPath, cancellationToken).ConfigureAwait(false);

            if (info.Fps <= 0)
                info.Fps = 24;

            if (info.FrameCount <= 0 && info.DurationSeconds > 0 && info.Fps > 0)
                info.FrameCount = Math.Max(1, (int)Math.Round(info.DurationSeconds * info.Fps));

            return info;
        }

        public async Task<string> ExtractFrameAsync(
            string inputPath,
            int frameIndex,
            string tempDirectory,
            CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            if (frameIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));

            Directory.CreateDirectory(tempDirectory);
            string outputPath = Path.Combine(tempDirectory, $"frame_{frameIndex:D8}.png");
            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i", Quote(inputPath),
                "-vf", Quote($"select=eq(n\\,{frameIndex})"),
                "-vframes", "1",
                Quote(outputPath)
            };

            var result = await RunFfmpegAsync(args, outputPath, null, cancellationToken).ConfigureAwait(false);
            if (!result.Success || !File.Exists(outputPath))
                return null;

            return outputPath;
        }

        public async Task<string> ExtractFirstFrameAsync(
            string inputPath,
            int maxSize,
            CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            string tempDirectory = Path.Combine(Path.GetTempPath(), "BDTM_vframe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string outputPath = Path.Combine(tempDirectory, "first_frame.png");

            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-ss", "0",
                "-i", Quote(inputPath),
                "-vframes", "1"
            };

            if (maxSize > 0)
            {
                args.Add("-vf");
                args.Add(Quote($"scale='min({maxSize},iw)':'min({maxSize},ih)':force_original_aspect_ratio=decrease"));
            }

            args.Add(Quote(outputPath));
            var result = await RunFfmpegAsync(args, outputPath, null, cancellationToken).ConfigureAwait(false);
            if (!result.Success || !File.Exists(outputPath))
            {
                try
                {
                    Directory.Delete(tempDirectory, true);
                }
                catch
                {
                    // ignored
                }

                return null;
            }

            return outputPath;
        }

        public async Task<IReadOnlyList<KeyValuePair<TimeSpan, string>>> ExtractPreviewFramesAsync(
            string inputPath,
            int count,
            int percentResize,
            string tempDirectory,
            CancellationToken cancellationToken)
        {
            ValidateInputPath(inputPath);
            locator.EnsureAvailable();

            if (count <= 0)
                return Array.Empty<KeyValuePair<TimeSpan, string>>();

            Directory.CreateDirectory(tempDirectory);
            string pattern = Path.Combine(tempDirectory, "preview_%03d.png");
            double fps = Math.Max(0.1, count / Math.Max(1.0, await GetDurationSecondsAsync(inputPath, cancellationToken).ConfigureAwait(false)));

            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i", Quote(inputPath),
                "-vf", Quote($"fps={fps.ToString(CultureInfo.InvariantCulture)}"),
                "-frames:v", count.ToString(CultureInfo.InvariantCulture)
            };

            if (percentResize > 0 && percentResize < 100)
            {
                int scale = Math.Max(1, percentResize);
                args[5] = Quote($"fps={fps.ToString(CultureInfo.InvariantCulture)},scale=iw*{scale}/100:ih*{scale}/100");
            }

            args.Add(Quote(pattern));
            var result = await RunFfmpegAsync(args, tempDirectory, null, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return Array.Empty<KeyValuePair<TimeSpan, string>>();

            var files = Directory.GetFiles(tempDirectory, "preview_*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(count).ToList();
            var output = new List<KeyValuePair<TimeSpan, string>>();
            for (int i = 0; i < files.Count; i++)
            {
                output.Add(new KeyValuePair<TimeSpan, string>(TimeSpan.FromSeconds(i / Math.Max(fps, 0.001)), files[i]));
            }

            return output;
        }

        private async Task<VideoProcessResult> ExtractAllFramesAsync(
            string inputPath,
            string outputPattern,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            string resolvedPattern = ResolveOutputPattern(inputPath, outputPattern);
            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i", Quote(inputPath),
                Quote(resolvedPattern)
            };

            var result = await RunFfmpegAsync(args, Path.GetDirectoryName(resolvedPattern), progress, cancellationToken).ConfigureAwait(false);
            if (result.Success)
                result.OutputFileCount = CountMatchingFiles(Path.GetDirectoryName(resolvedPattern), Path.GetFileName(resolvedPattern));
            return result;
        }

        private async Task<VideoProcessResult> ExtractByFpsAsync(
            string inputPath,
            string outputPattern,
            double fps,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (fps <= 0)
                throw new ArgumentOutOfRangeException(nameof(fps));

            string resolvedPattern = ResolveOutputPattern(inputPath, outputPattern);
            var args = new List<string>
            {
                "-hide_banner",
                "-y",
                "-i", Quote(inputPath),
                "-vf", Quote($"fps={fps.ToString(CultureInfo.InvariantCulture)}"),
                Quote(resolvedPattern)
            };

            var result = await RunFfmpegAsync(args, Path.GetDirectoryName(resolvedPattern), progress, cancellationToken).ConfigureAwait(false);
            if (result.Success)
                result.OutputFileCount = CountMatchingFiles(Path.GetDirectoryName(resolvedPattern), Path.GetFileName(resolvedPattern));
            return result;
        }

        private async Task<VideoProcessResult> ExtractSpecificFramesAsync(
            string inputPath,
            string outputDirectory,
            string imageFormat,
            string specificSelection,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            ParsedFrameSelection selection = ParseFrameSelection(specificSelection);
            if (selection.InvalidTokens.Count > 0)
            {
                return new VideoProcessResult
                {
                    Success = false,
                    ErrorMessage = string.Join(", ", selection.InvalidTokens)
                };
            }

            if (selection.FrameNumbers.Count == 0 && selection.Timestamps.Count == 0)
            {
                return new VideoProcessResult
                {
                    Success = false,
                    ErrorMessage = I18n.GetText("VideoToolsNoFramesSpecified")
                };
            }

            int written = 0;
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            foreach (int frameNumber in selection.FrameNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string outputPath = Path.Combine(outputDirectory, $"{baseName}_frame_{frameNumber:D6}.{imageFormat}");
                var args = new List<string>
                {
                    "-hide_banner",
                    "-y",
                    "-i", Quote(inputPath),
                    "-vf", Quote($"select=eq(n\\,{frameNumber})"),
                    "-vframes", "1",
                    Quote(outputPath)
                };

                var result = await RunFfmpegAsync(args, outputPath, progress, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                    return result;
                written++;
            }

            foreach (TimeSpan timestamp in selection.Timestamps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string stamp = timestamp.ToString(@"hh\-mm\-ss\.fff", CultureInfo.InvariantCulture);
                string outputPath = Path.Combine(outputDirectory, $"{baseName}_time_{stamp.Replace('\\', '-')}.{imageFormat}");
                var args = new List<string>
                {
                    "-hide_banner",
                    "-y",
                    "-ss", Quote(timestamp.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)),
                    "-i", Quote(inputPath),
                    "-vframes", "1",
                    Quote(outputPath)
                };

                var result = await RunFfmpegAsync(args, outputPath, progress, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                    return result;
                written++;
            }

            return new VideoProcessResult
            {
                Success = true,
                OutputPath = outputDirectory,
                OutputFileCount = written
            };
        }

        private async Task<double> GetDurationSecondsAsync(string inputPath, CancellationToken cancellationToken)
        {
            var args = new List<string>
            {
                "-hide_banner",
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                Quote(inputPath)
            };

            var result = await RunProcessAsync(locator.FfprobeExe, args, null, cancellationToken).ConfigureAwait(false);
            if (double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
                return seconds;

            return 1;
        }

        private async Task<VideoProcessResult> RunFfmpegAsync(
            IList<string> arguments,
            string outputHint,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var processResult = await RunProcessAsync(locator.FfmpegExe, arguments, progress, cancellationToken).ConfigureAwait(false);
            return new VideoProcessResult
            {
                Success = processResult.ExitCode == 0,
                OutputPath = outputHint,
                ErrorMessage = processResult.ExitCode == 0 ? null : processResult.GetErrorSummary()
            };
        }

        private static async Task<ProcessExecutionResult> RunProcessAsync(
            string executable,
            IList<string> arguments,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stderr = new StringBuilder();
            var stdout = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;

                stderr.AppendLine(e.Data);
                progress?.Report(FilterProgressLine(e.Data));
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await Task.Run(() => process.WaitForExit(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }

                throw;
            }

            return new ProcessExecutionResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString(),
                StdErr = stderr.ToString()
            };
        }

        private static string ParseProgressLine(string line)
        {
            return FilterProgressLine(line);
        }

        private static void ValidateInputPath(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("Input path is required.", nameof(inputPath));

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input file not found.", inputPath);

            if (!IsVideoFile(inputPath))
                throw new ArgumentException("Unsupported video file.", nameof(inputPath));
        }

        private static string ResolveOutputPattern(string inputPath, string outputPattern)
        {
            string baseName = Path.GetFileNameWithoutExtension(inputPath);
            return outputPattern.Replace("{basename}", baseName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeImageExtension(string imageFormat)
        {
            string ext = (imageFormat ?? "png").Trim().TrimStart('.').ToLowerInvariant();
            return ext == "jpg" || ext == "jpeg" ? "jpg" : "png";
        }

        private static int CountMatchingFiles(string directory, string patternFileName)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            string searchPattern = Path.GetFileName(patternFileName)?.Replace("%06d", "*") ?? "*";
            return Directory.GetFiles(directory, searchPattern).Length;
        }

        private static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        private sealed class ProcessExecutionResult
        {
            public int ExitCode { get; set; }
            public string StdOut { get; set; }
            public string StdErr { get; set; }

            public string GetErrorSummary()
            {
                string[] lines = (StdErr ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length == 0)
                    return I18n.GetText("VideoToolsProcessFailed");

                return lines.Last();
            }
        }
    }

    public sealed class ParsedFrameSelection
    {
        public List<int> FrameNumbers { get; set; } = new List<int>();
        public List<TimeSpan> Timestamps { get; set; } = new List<TimeSpan>();
        public List<string> InvalidTokens { get; set; } = new List<string>();

        public bool IsValid => InvalidTokens.Count == 0 && (FrameNumbers.Count > 0 || Timestamps.Count > 0);
    }
}
