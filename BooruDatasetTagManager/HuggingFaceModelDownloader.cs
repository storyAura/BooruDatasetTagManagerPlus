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

        /// <summary>
        /// The user's Hugging Face token must only ever reach huggingface.co
        /// itself — never a third-party mirror host.
        /// </summary>
        public static bool ShouldAttachAuthToken(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && (string.Equals(uri.Host, "huggingface.co", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase));
        }

        public static string GetLocalDirectory(string repo)
        {
            string safeRepo = (repo ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            string modelsRoot = Path.Combine(Program.AppPath, "Models");
            string combined = Path.GetFullPath(Path.Combine(modelsRoot, safeRepo));
            EnsureWithinRoot(modelsRoot, combined, repo);
            return combined;
        }

        public static string GetLocalPath(string repo, string filename)
        {
            string dir = GetLocalDirectory(repo);
            string combined = Path.GetFullPath(Path.Combine(dir, filename ?? string.Empty));
            // Guard against '..' or rooted filenames escaping the model directory.
            EnsureWithinRoot(Path.Combine(Program.AppPath, "Models"), combined, $"{repo}/{filename}");
            return combined;
        }

        private static void EnsureWithinRoot(string root, string candidate, string original)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!candidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Resolved model path '{candidate}' escapes the Models directory (input: '{original}').");
            }
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

                // Repos can nest files in subfolders (e.g. "v2_01a/model.onnx"),
                // so match validation rules on the file name only.
                string name = Path.GetFileName(filename ?? string.Empty);
                if (string.Equals(name, "model.onnx", StringComparison.OrdinalIgnoreCase))
                {
                    // External-data exports (cl_tagger_v2: 773KB graph +
                    // 2.2GB model.onnx.data) are legitimately under the 1MB
                    // floor — accept an ONNX protobuf header as an alternative.
                    return info.Length >= MinOnnxFileBytes || LooksLikeOnnxProtobuf(path);
                }

                if (string.Equals(name, "selected_tags.csv", StringComparison.OrdinalIgnoreCase))
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

        public Task<string> DownloadFileAsync(
            HuggingFaceDownloadSource source,
            string repo,
            string filename,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            return DownloadFileAsync(source, repo, filename, authToken: null, progress, cancellationToken);
        }

        public async Task<string> DownloadFileAsync(
            HuggingFaceDownloadSource source,
            string repo,
            string filename,
            string authToken,
            IProgress<(string file, long downloaded, long? total)> progress,
            CancellationToken cancellationToken)
        {
            string localPath = GetLocalPath(repo, filename);
            // Download into a .partial sidecar and rename only after the content is
            // complete and validated. An interrupted download previously left a
            // truncated model.onnx that passed the ">= 1MB" cache check forever.
            string partialPath = localPath + ".partial";
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? Program.AppPath);

            if (File.Exists(localPath))
            {
                if (ValidateCachedFile(localPath, filename))
                {
                    long length = new FileInfo(localPath).Length;
                    progress?.Report((filename, length, length));
                    return localPath;
                }
                DeleteCachedFile(repo, filename);
            }

            // A token implies a gated repo: gated content must come from
            // huggingface.co directly, so the credential never reaches a mirror.
            if (!string.IsNullOrWhiteSpace(authToken))
                source = HuggingFaceDownloadSource.HuggingFace;
            string url = BuildDownloadUrl(source, repo, filename);

            long existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingLength > 0)
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
            // Gated repos (e.g. cl_tagger_v2) require the user's own HF token.
            if (!string.IsNullOrWhiteSpace(authToken) && ShouldAttachAuthToken(url))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken.Trim());

            using HttpResponseMessage response = await SharedClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // The partial file already spans the full remote size.
                progress?.Report((filename, existingLength, existingLength));
                PromotePartialFile(partialPath, localPath);
                EnsureValidDownloadedFile(repo, filename, localPath);
                return localPath;
            }

            response.EnsureSuccessStatusCode();

            string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(partialPath);
                throw new InvalidOperationException(I18n.GetText("TaggerModelCorrupt"));
            }

            FileMode fileMode = response.StatusCode == HttpStatusCode.PartialContent && existingLength > 0
                ? FileMode.Append
                : FileMode.Create;
            if (fileMode == FileMode.Create)
                existingLength = 0; // 200 despite a Range header: the full body is resent, stale partial bytes must not inflate `total`.

            long? total = response.Content.Headers.ContentLength.HasValue
                ? existingLength + response.Content.Headers.ContentLength.Value
                : null;

            await using (Stream remote = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream local = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.Read))
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

            // The write stream must be fully closed before validating/moving the file.
            if (total.HasValue && new FileInfo(partialPath).Length != total.Value)
            {
                TryDelete(partialPath);
                throw new InvalidOperationException(I18n.GetText("TaggerModelCorrupt"));
            }

            PromotePartialFile(partialPath, localPath);
            EnsureValidDownloadedFile(repo, filename, localPath);
            return localPath;
        }

        private static void PromotePartialFile(string partialPath, string localPath)
        {
            File.Move(partialPath, localPath, overwrite: true);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
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

        private static bool LooksLikeOnnxProtobuf(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Every ONNX export starts with the ir_version field header (0x08);
            // HTML/JSON error bodies start with '<' or '{'.
            return stream.ReadByte() == 0x08;
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
