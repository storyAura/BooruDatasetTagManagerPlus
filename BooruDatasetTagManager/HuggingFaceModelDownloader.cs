using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class HuggingFaceModelDownloader
    {
        private const long MinOnnxFileBytes = 1024 * 1024;

        private static readonly HttpClient SharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromHours(6)
        };

        public static string BuildDownloadUrl(HuggingFaceDownloadSource source, string repo, string filename)
        {
            string baseUrl = source == HuggingFaceDownloadSource.HfMirror
                ? "https://hf-mirror.com"
                : "https://huggingface.co";
            return $"{baseUrl}/{repo.Trim('/')}/resolve/main/{filename}";
        }

        public static string GetLocalDirectory(string repo)
        {
            string safeRepo = repo.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(Program.AppPath, "Models", safeRepo);
        }

        public static string GetLocalPath(string repo, string filename)
        {
            return Path.Combine(GetLocalDirectory(repo), filename);
        }

        public static string NormalizePathForOnnx(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!OperatingSystem.IsWindows())
                return fullPath;

            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
                return fullPath;

            if (fullPath.Any(c => c > 127))
                return @"\\?\" + fullPath;

            return fullPath;
        }

        public static bool ValidateCachedFile(string path, string filename)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                var info = new FileInfo(path);
                if (info.Length == 0)
                    return false;

                if (LooksLikeHtml(path))
                    return false;

                if (string.Equals(filename, "model.onnx", StringComparison.OrdinalIgnoreCase))
                    return info.Length >= MinOnnxFileBytes;

                if (string.Equals(filename, "selected_tags.csv", StringComparison.OrdinalIgnoreCase))
                {
                    string firstLine = ReadFirstLineShared(path);
                    return firstLine.Contains("name", StringComparison.OrdinalIgnoreCase);
                }

                return info.Length > 0;
            }
            catch (IOException)
            {
                // File is locked (e.g. still being written); treat as not ready instead of crashing.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool IsFileCached(string repo, string filename)
        {
            string path = GetLocalPath(repo, filename);
            return ValidateCachedFile(path, filename);
        }

        public void DeleteCachedFile(string repo, string filename)
        {
            string path = GetLocalPath(repo, filename);
            if (File.Exists(path))
                File.Delete(path);
        }

        public async Task<string> DownloadFileAsync(
            HuggingFaceDownloadSource source,
            string repo,
            string filename,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            string localPath = GetLocalPath(repo, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? Program.AppPath);

            if (File.Exists(localPath) && !ValidateCachedFile(localPath, filename))
                DeleteCachedFile(repo, filename);

            string url = BuildDownloadUrl(source, repo, filename);

            long existingLength = File.Exists(localPath) ? new FileInfo(localPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);

            using HttpResponseMessage response = await SharedClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                progress?.Report((filename, existingLength, existingLength));
                EnsureValidDownloadedFile(repo, filename, localPath);
                return localPath;
            }

            response.EnsureSuccessStatusCode();

            string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                DeleteCachedFile(repo, filename);
                throw new InvalidOperationException(I18n.GetText("TaggerModelCorrupt"));
            }

            long? total = response.Content.Headers.ContentLength.HasValue
                ? existingLength + response.Content.Headers.ContentLength.Value
                : null;

            FileMode fileMode = response.StatusCode == HttpStatusCode.PartialContent && existingLength > 0
                ? FileMode.Append
                : FileMode.Create;
            if (fileMode == FileMode.Create)
                existingLength = 0;

            await using (Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream local = new FileStream(localPath, fileMode, FileAccess.Write, FileShare.Read))
            {
                byte[] buffer = new byte[81920];
                long downloaded = existingLength;
                int read;
                while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await local.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report((filename, downloaded, total));
                }
            }

            // The write stream must be fully closed before validating/deleting the file.
            EnsureValidDownloadedFile(repo, filename, localPath);
            return localPath;
        }

        private void EnsureValidDownloadedFile(string repo, string filename, string localPath)
        {
            if (ValidateCachedFile(localPath, filename))
                return;

            DeleteCachedFile(repo, filename);
            throw new InvalidOperationException(I18n.GetText("TaggerModelCorrupt"));
        }

        private static string ReadFirstLineShared(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadLine() ?? string.Empty;
        }

        private static bool LooksLikeHtml(string path)
        {
            Span<byte> buffer = stackalloc byte[512];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                read = stream.Read(buffer);
            }

            if (read == 0)
                return false;

            string prefix = System.Text.Encoding.ASCII.GetString(buffer.Slice(0, read)).TrimStart();
            return prefix.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }
    }
}
