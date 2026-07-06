using System;
using System.IO;

namespace BooruDatasetTagManager
{
    public sealed class FfmpegLocator
    {
        private readonly string appDirectory;
        private readonly string configuredPath;

        public FfmpegLocator(string appDirectory, string configuredPath)
        {
            this.appDirectory = appDirectory ?? throw new ArgumentNullException(nameof(appDirectory));
            this.configuredPath = configuredPath ?? string.Empty;
        }

        public static FfmpegLocator FromSettings()
        {
            return new FfmpegLocator(Program.AppPath, Program.Settings?.FfmpegPath ?? string.Empty);
        }

        public string FfmpegExe => ResolveExecutable("ffmpeg.exe");

        public string FfprobeExe => ResolveExecutable("ffprobe.exe");

        public bool IsAvailable => File.Exists(FfmpegExe) && File.Exists(FfprobeExe);

        public void EnsureAvailable()
        {
            if (IsAvailable)
                return;

            throw new InvalidOperationException(I18n.GetText("VideoToolsFfmpegMissing"));
        }

        private string ResolveExecutable(string fileName)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (configuredPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return configuredPath;

                return Path.Combine(configuredPath, fileName);
            }

            string bundled = Path.Combine(appDirectory, "ThirdParty", "ffmpeg", "win-x64", fileName);
            if (File.Exists(bundled))
                return bundled;

            string fromPath = FindOnPath(fileName);
            if (!string.IsNullOrEmpty(fromPath))
                return fromPath;

            return bundled;
        }

        private static string FindOnPath(string fileName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                return null;

            foreach (string segment in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(segment))
                    continue;

                string candidate = Path.Combine(segment.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
