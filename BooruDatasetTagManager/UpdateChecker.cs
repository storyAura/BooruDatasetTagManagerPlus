using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BooruDatasetTagManager
{
    public sealed class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public bool HasNewer { get; set; }
        public string LatestTag { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = string.Empty;
        public string ZipAssetUrl { get; set; }
        public string ZipAssetName { get; set; }
        public string ZipAssetDigest { get; set; }
    }

    /// <summary>
    /// Manual update check invoked from the settings window. Two modes:
    /// a source checkout (a .git directory next to the solution) is updated via
    /// "git pull --ff-only"; a release install queries GitHub Releases and
    /// downloads the win-x64 zip asset.
    /// </summary>
    public static class UpdateChecker
    {
        public const string RepoOwner = "storyAura";
        public const string RepoName = "BooruDatasetTagManagerPlus";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";

        private static readonly Lazy<HttpClient> Client = new(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            // GitHub's API rejects requests without a User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BooruDatasetTagManagerPlus-UpdateCheck");
            return client;
        });

        /// <summary>
        /// Returns the repository root when the app runs from a source checkout
        /// (a .git entry AND the solution file in the same ancestor directory),
        /// otherwise null. The solution check avoids false positives when a
        /// release build merely sits inside some unrelated git-managed folder.
        /// </summary>
        public static string FindSourceCheckoutRoot()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; dir != null && i < 8; i++, dir = dir.Parent)
                {
                    string gitPath = Path.Combine(dir.FullName, ".git");
                    if ((Directory.Exists(gitPath) || File.Exists(gitPath))
                        && File.Exists(Path.Combine(dir.FullName, "BooruDatasetTagManager.sln")))
                    {
                        return dir.FullName;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UpdateChecker.FindSourceCheckoutRoot failed: {ex}");
            }
            return null;
        }

        /// <summary>Runs "git pull --ff-only" in the checkout and returns its combined output.</summary>
        public static async Task<(bool Success, string Output)> PullSourceAsync(string repoRoot)
        {
            var output = new StringBuilder();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.ArgumentList.Add("-C");
                psi.ArgumentList.Add(repoRoot);
                psi.ArgumentList.Add("pull");
                // --ff-only: never create merge commits in the user's checkout; a
                // diverged branch fails loudly and is left for the user to resolve.
                psi.ArgumentList.Add("--ff-only");

                using var process = new Process { StartInfo = psi };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                    return (false, "git pull timed out.\n" + output);
                }
                return (process.ExitCode == 0, output.ToString().Trim());
            }
            catch (Exception ex)
            {
                // Typically Win32Exception: git is not installed / not on PATH.
                return (false, ex.Message);
            }
        }

        /// <summary>Queries the latest GitHub release and compares it with <paramref name="currentVersion"/>.</summary>
        public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(string currentVersion)
        {
            var result = new UpdateCheckResult();
            ReleaseInfo latest;
            try
            {
                string json = await GetStringWithRetryAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                latest = JsonConvert.DeserializeObject<ReleaseInfo>(json);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            if (latest == null || string.IsNullOrEmpty(latest.tag_name))
            {
                result.ErrorMessage = "Unexpected response from GitHub.";
                return result;
            }

            result.Success = true;
            result.LatestTag = latest.tag_name;
            result.ReleasePageUrl = latest.html_url;
            // Raw dual-language body; the UI selects one language section (and
            // caps the length) via ReleaseNotesLocalizer before display.
            result.ReleaseNotes = (latest.body ?? string.Empty).Trim();

            if (TryParseVersion(latest.tag_name, out Version remote) &&
                TryParseVersion(currentVersion, out Version local))
            {
                result.HasNewer = remote > local;
            }

            // Only the win-x64 zip is installable here. No fallback to "any
            // zip": that used to grab source archives or other platforms; with
            // no match the UI sends the user to the release page instead.
            var zipAsset = latest.assets?.FirstOrDefault(a =>
                    a.name != null &&
                    a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    a.name.Contains("win-x64", StringComparison.OrdinalIgnoreCase));
            if (zipAsset != null)
            {
                result.ZipAssetUrl = zipAsset.browser_download_url;
                result.ZipAssetName = zipAsset.name;
                result.ZipAssetDigest = zipAsset.digest;
            }
            return result;
        }

        /// <summary>GET with one bounded retry on 429, honoring Retry-After
        /// (capped at 10s).</summary>
        private static async Task<string> GetStringWithRetryAsync(string url)
        {
            using (HttpResponseMessage first = await Client.Value.GetAsync(url).ConfigureAwait(false))
            {
                if ((int)first.StatusCode != 429)
                {
                    first.EnsureSuccessStatusCode();
                    return await first.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                TimeSpan delay = first.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.FromSeconds(1);
                else if (delay > TimeSpan.FromSeconds(10))
                    delay = TimeSpan.FromSeconds(10);
                await Task.Delay(delay).ConfigureAwait(false);
            }
            using HttpResponseMessage second = await Client.Value.GetAsync(url).ConfigureAwait(false);
            second.EnsureSuccessStatusCode();
            return await second.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a release asset next to the executable (falling back to the
        /// user's Downloads folder when the install dir is not writable) via a
        /// .partial file so an interrupted download never looks complete.
        /// </summary>
        public static async Task<string> DownloadReleaseAssetAsync(string url, string fileName, IProgress<int> progress, string expectedDigest = null)
        {
            string targetDir = GetWritableDownloadDirectory();
            string target = Path.Combine(targetDir, fileName);
            string partial = target + ".partial";

            using HttpResponseMessage response = await Client.Value
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;

            await using (Stream remote = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var local = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int read;
                int lastPercent = -1;
                while ((read = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    await local.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    downloaded += read;
                    if (total is > 0)
                    {
                        int percent = (int)(downloaded * 100 / total.Value);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(percent);
                        }
                    }
                }
            }

            if (total.HasValue && new FileInfo(partial).Length != total.Value)
            {
                try { File.Delete(partial); } catch { }
                throw new IOException("Download ended before the full file was received.");
            }
            if (!VerifyDigest(partial, expectedDigest))
            {
                try { File.Delete(partial); } catch { }
                throw new IOException("Downloaded file failed its sha256 digest check.");
            }
            File.Move(partial, target, overwrite: true);
            return target;
        }

        /// <summary>True when <paramref name="digest"/> is absent or of an
        /// unknown algorithm (nothing to check), or matches the file's SHA-256
        /// ("sha256:&lt;hex&gt;", as the GitHub API reports it).</summary>
        private static bool VerifyDigest(string path, string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)
                || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return true;
            string expected = digest.Substring("sha256:".Length).Trim();
            using FileStream stream = File.OpenRead(path);
            byte[] hash = System.Security.Cryptography.SHA256.HashData(stream);
            return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
        }

        public static void OpenInBrowser(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UpdateChecker.OpenInBrowser failed: {ex}");
            }
        }

        public static void ShowInExplorer(string filePath)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"UpdateChecker.ShowInExplorer failed: {ex}");
            }
        }

        private static string GetWritableDownloadDirectory()
        {
            string appDir = AppContext.BaseDirectory;
            try
            {
                string probe = Path.Combine(appDir, ".write_probe_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return appDir;
            }
            catch (Exception)
            {
                string downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloads);
                return downloads;
            }
        }

        private static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string trimmed = text.Trim().TrimStart('v', 'V');
            // Drop informational suffixes such as "1.1.2+abc" or "1.1.2-beta".
            int cut = trimmed.IndexOfAny(new[] { '+', '-', ' ' });
            if (cut > 0)
                trimmed = trimmed.Substring(0, cut);
            if (!trimmed.Contains('.'))
                trimmed += ".0";
            return Version.TryParse(trimmed, out version);
        }
    }
}
