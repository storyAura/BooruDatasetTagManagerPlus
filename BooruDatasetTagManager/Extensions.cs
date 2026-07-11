using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.IO;
using Translator.Crypto;
using System.Drawing;
using System.Windows.Forms;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Data;
using Newtonsoft.Json;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Threading;
using System.Drawing.Imaging;

namespace BooruDatasetTagManager
{
    public static class Extensions
    {

        // HashSet with case-insensitive comparison: O(1) lookups and no need for
        // per-call .ToLower() allocations when checking file extensions.
        public static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(new[] { ".jpg", ".png", ".bmp", ".jpeg", ".webp" }, StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> VideoExtensions =
            new HashSet<string>(new[] { ".mp4", ".flv", ".mkv", ".ts", ".avi", ".webm", ".mov" }, StringComparer.OrdinalIgnoreCase);

        public delegate void ProgressHandler(int current, int max);

        public static void AddRange(this List<TagValue> list, IEnumerable<string> range)
        {
            foreach (var item in range)
                list.Add(new TagValue(item));
        }

        public static long GetHash(this string text)
        {
            return Adler32.GenerateHash(text);
        }

        public static int CalcBracketsCount(float weight, bool positive)
        {
            if (weight == 1 || weight == 0)
                return 0;
            int count = 0;
            float mult = positive ? PromptParser.round_bracket_multiplier : PromptParser.square_bracket_multiplier;

            if (positive)
            {
                while (weight > 1)
                {
                    weight /= mult;
                    count++;
                }
            }
            else
            {
                while (weight < 1)
                {
                    weight /= mult;
                    count++;
                }
            }
            if (weight == 1)
                return count;
            else
                return 0;
        }

        public static string GetBetween(this string strSource, string strStart, string strEnd)
        {
            const int kNotFound = -1;

            var startIdx = strSource.IndexOf(strStart);
            if (startIdx != kNotFound)
            {
                startIdx += strStart.Length;
                var endIdx = strSource.IndexOf(strEnd, startIdx);
                if (endIdx > startIdx)
                {
                    return strSource.Substring(startIdx, endIdx - startIdx);
                }
            }
            return String.Empty;
        }

        public static string GetBetween(this string strSource, string strStart, string strEnd, int startIndex)
        {
            const int kNotFound = -1;

            var startIdx = strSource.IndexOf(strStart, startIndex);
            if (startIdx != kNotFound)
            {
                startIdx += strStart.Length;
                var endIdx = strSource.IndexOf(strEnd, startIdx);
                if (endIdx > startIdx)
                {
                    return strSource.Substring(startIdx, endIdx - startIdx);
                }
            }
            return String.Empty;
        }

        public static Image GetImageFromFile(string imagePath)
        {
            if (VideoExtensions.Contains(Path.GetExtension(imagePath).ToLower()))
                return null;

            return ImageLoader.GetImageFromFile(imagePath);
        }

        public static List<KeyValuePair<TimeSpan, Image>> GetImagesFromVideo(string videoPath, int count, int percentResize = 50)
        {
            if (!VideoExtensions.Contains(Path.GetExtension(videoPath).ToLower()))
                return null;

            if (count <= 0)
                return new List<KeyValuePair<TimeSpan, Image>>();

            try
            {
                var service = VideoProcessingService.CreateDefault();
                string tempDir = Path.Combine(Path.GetTempPath(), "BDTM_video_" + Guid.NewGuid().ToString("N"));
                var frames = service.ExtractPreviewFramesAsync(videoPath, count, percentResize, tempDir, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var result = new List<KeyValuePair<TimeSpan, Image>>();
                foreach (var frame in frames)
                {
                    if (!File.Exists(frame.Value))
                        continue;

                    using var loaded = Image.FromFile(frame.Value);
                    result.Add(new KeyValuePair<TimeSpan, Image>(frame.Key, (Image)loaded.Clone()));
                }

                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // ignored
                }

                return result;
            }
            catch
            {
                return null;
            }
        }

        public static byte[] ImageToByteArray(Image image)
        {
            using (var memoryStream = new MemoryStream())
            {
                image.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                return memoryStream.ToArray();
            }

        }

        public static Bitmap Transparent2Color(Bitmap bmp1, Color target)
        {
            Bitmap bmp2 = new Bitmap(bmp1.Width, bmp1.Height);
            Rectangle rect = new Rectangle(Point.Empty, bmp1.Size);
            using (Graphics G = Graphics.FromImage(bmp2))
            {
                G.Clear(target);
                G.DrawImageUnscaledAndClipped(bmp1, rect);
            }
            return bmp2;
        }

        public static Image MakeThumb(string imagePath, int imgSize)
        {
            if (VideoExtensions.Contains(Path.GetExtension(imagePath).ToLower()))
                return MakeVideoThumb(imagePath, imgSize);

            return ImageLoader.MakeThumb(imagePath, imgSize);
        }

        private static readonly object VideoThumbCacheLock = new object();

        public static Image MakeVideoThumb(string videoPath, int imgSize, bool drawBadge = true)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || imgSize <= 0)
                return CreateVideoPlaceholder(imgSize, drawBadge);

            try
            {
                string cachePath = GetVideoThumbCachePath(videoPath, imgSize, drawBadge);
                if (File.Exists(cachePath))
                {
                    using var cached = Image.FromFile(cachePath);
                    return (Image)cached.Clone();
                }

                Image frameImage = null;
                try
                {
                    var service = VideoProcessingService.CreateDefault();
                    string frameFile = service.ExtractFirstFrameAsync(videoPath, imgSize, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(frameFile) && File.Exists(frameFile))
                    {
                        using var loaded = Image.FromFile(frameFile);
                        frameImage = (Image)loaded.Clone();
                        try
                        {
                            File.Delete(frameFile);
                            Directory.Delete(Path.GetDirectoryName(frameFile), true);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
                catch
                {
                    // ffmpeg unavailable or extraction failed
                }

                if (frameImage == null)
                    return CreateVideoPlaceholder(imgSize, drawBadge);

                using (frameImage)
                {
                    Bitmap result = drawBadge ? DrawVideoBadge((Bitmap)frameImage) : new Bitmap(frameImage);
                    SaveVideoThumbCache(cachePath, videoPath, result);
                    return (Bitmap)result.Clone();
                }
            }
            catch
            {
                return CreateVideoPlaceholder(imgSize, drawBadge);
            }
        }

        private static string GetVideoThumbCachePath(string videoPath, int imgSize, bool drawBadge)
        {
            var fileInfo = new FileInfo(videoPath);
            string key = $"{videoPath}|{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}|{imgSize}|{drawBadge}";
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            string cacheDir = Path.Combine(Program.AppPath, "Cache", "video_thumbs");
            Directory.CreateDirectory(cacheDir);
            return Path.Combine(cacheDir, hash + ".png");
        }

        private const int MaxVideoThumbCacheFiles = 2000;
        private static bool videoThumbCacheTrimmed;

        private static void SaveVideoThumbCache(string cachePath, string videoPath, Image image)
        {
            lock (VideoThumbCacheLock)
            {
                try
                {
                    image.Save(cachePath, ImageFormat.Png);
                    TrimVideoThumbCache(Path.GetDirectoryName(cachePath));
                }
                catch
                {
                    // ignored
                }
            }
        }

        /// <summary>
        /// The thumb cache previously grew forever; drop the oldest files once per
        /// session when the count exceeds the cap.
        /// </summary>
        private static void TrimVideoThumbCache(string cacheDir)
        {
            if (videoThumbCacheTrimmed || string.IsNullOrEmpty(cacheDir))
                return;
            videoThumbCacheTrimmed = true;
            try
            {
                var files = new DirectoryInfo(cacheDir).GetFiles("*.png");
                if (files.Length <= MaxVideoThumbCacheFiles)
                    return;
                foreach (var stale in files.OrderBy(f => f.LastAccessTimeUtc)
                                           .Take(files.Length - MaxVideoThumbCacheFiles))
                {
                    try { stale.Delete(); } catch { }
                }
            }
            catch
            {
                // Cache trimming must never break thumbnail generation.
            }
        }

        private static Bitmap CreateVideoPlaceholder(int imgSize, bool drawBadge)
        {
            var bitmap = new Bitmap(imgSize, imgSize);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(64, 64, 64));
                using var font = new Font("Segoe UI", Math.Max(8, imgSize / 10), FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
                var text = "VIDEO";
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, (imgSize - size.Width) / 2f, (imgSize - size.Height) / 2f);
            }

            return drawBadge ? DrawVideoBadge(bitmap) : bitmap;
        }

        private static Bitmap DrawVideoBadge(Bitmap source)
        {
            var bitmap = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
                int badgeHeight = Math.Max(12, source.Height / 8);
                int badgeWidth = Math.Max(36, badgeHeight * 3);
                var badgeRect = new Rectangle(source.Width - badgeWidth - 2, source.Height - badgeHeight - 2, badgeWidth, badgeHeight);
                using var badgeBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
                g.FillRectangle(badgeBrush, badgeRect);
                using var font = new Font("Segoe UI", Math.Max(6, badgeHeight - 4), FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.White);
                var text = "VIDEO";
                SizeF textSize = g.MeasureString(text, font);
                g.DrawString(
                    text,
                    font,
                    textBrush,
                    badgeRect.X + (badgeRect.Width - textSize.Width) / 2f,
                    badgeRect.Y + (badgeRect.Height - textSize.Height) / 2f);
            }

            return bitmap;
        }

        public static string[] GetFriendlyEnumValues<T>()
        {
            return Enum.GetNames(typeof(T)).Select(a => I18n.GetText(a)).ToArray();
        }

        public static int GetEnumIndexFromValue<T>(string value)
        {
            string[] values = Enum.GetNames(typeof(T));
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Equals(value))
                    return i;
            }
            return -1;
        }

        public static T GetEnumItemFromFriendlyText<T>(string text)
        {
            string[] indexes = I18n.GetAllIndexes(text);
            if (indexes.Length == 1)
                return (T)Enum.Parse(typeof(T), indexes[0], true);
            else if (indexes.Length > 1)
            {
                object result;
                foreach (var item in indexes)
                {
                    if (Enum.TryParse(typeof(T), item, out result))
                    {
                        return (T)result;
                    }
                }
            }
            throw new InvalidEnumArgumentException("Cannot find Enum value");
        }


        private static readonly Lazy<HttpClient> updateHttpClient = new Lazy<HttpClient>(() =>
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.Add("User-Agent", "BooruDatasetTagManagerPlus");
            return client;
        });

        public static async void CheckForUpdateAsync(string currentVersion)
        {
            string data = null;
            await Task.Run(async () =>
            {
                try
                {
                    data = await updateHttpClient.Value.GetStringAsync("https://api.github.com/repos/storyAura/BooruDatasetTagManagerPlus/releases");
                }
                catch (Exception)
                {
                    return;
                }
            });
            if (!string.IsNullOrWhiteSpace(data))
            {
                try
                {
                    List<ReleaseInfo> releasesList = JsonConvert.DeserializeObject<List<ReleaseInfo>>(data);

                    releasesList.Sort((b, a) => a.published_at.CompareTo(b.published_at));
                    currentVersion = "v" + currentVersion;
                    int curIndex = releasesList.FindIndex(a => currentVersion.StartsWith(a.tag_name));
                    if (curIndex <= 0)
                        return;

                    List<ReleaseInfo> nVersions = releasesList.Take(curIndex).ToList();
                    string url = nVersions[0].html_url;
                    StringBuilder releaseNote = new StringBuilder();
                    foreach (var item in nVersions)
                    {
                        string text = item.body;
                        string[] listItems = text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        releaseNote.AppendLine(item.tag_name + ":");
                        for (int i = 0; i < listItems.Length; i++)
                        {
                            releaseNote.AppendLine(listItems[i]);
                        }
                    }
                    Form_UpdateInfo formUpdate = new Form_UpdateInfo();
                    formUpdate.SetText($"A new version of the program has been detected ({nVersions[0].tag_name.Substring(1)}).\r\nNew in version:\r\n{releaseNote}\r\nDo you want to go to the program download page?");
                    if (formUpdate.ShowDialog() == DialogResult.OK)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    formUpdate.Close();
                }
                catch (Exception)
                {
                    return;
                }
            }
        }


        public static T Pop<T>(this List<T> list)
        {
            if (list == null || list.Count == 0)
                return default;
            T res = list[list.Count - 1];
            //T res = list[0];
            list.RemoveAt(list.Count - 1);
            //list.RemoveAt(0);
            return res;
        }

    }
}
