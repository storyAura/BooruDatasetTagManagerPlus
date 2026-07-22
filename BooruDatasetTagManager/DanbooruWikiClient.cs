using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class DanbooruWikiClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly bool ownsClient;

        public DanbooruWikiClient() : this(new HttpClient(), true)
        {
        }

        public DanbooruWikiClient(HttpClient client) : this(client, false)
        {
        }

        private DanbooruWikiClient(HttpClient client, bool ownsClient)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.ownsClient = ownsClient;
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("BooruDatasetTagManager/1.0");
            this.client.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<DanbooruWikiPage> GetWikiPageAsync(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            try
            {
                string normalizedTag = NormalizeTag(tag);
                string url = "https://danbooru.donmai.us/wiki_pages.json?search[title]="
                    + Uri.EscapeDataString(normalizedTag)
                    + "&limit=1";

                using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                JArray root = JArray.Parse(json);
                JObject item = root.First as JObject;
                if (item == null)
                    return null;

                DateTimeOffset updatedAt;
                DateTimeOffset? parsedUpdatedAt = DateTimeOffset.TryParse(item.Value<string>("updated_at"), out updatedAt)
                    ? updatedAt
                    : null;

                return new DanbooruWikiPage
                {
                    Title = item.Value<string>("title") ?? normalizedTag,
                    Body = item.Value<string>("body") ?? string.Empty,
                    OtherNames = item["other_names"]?.Values<string>().ToArray() ?? Array.Empty<string>(),
                    UpdatedAt = parsedUpdatedAt,
                    Url = GetWikiUrl(normalizedTag)
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Post ids referenced as "post #123" in a wiki body (the curated
        /// Examples section danbooru renders as thumbnails), in order of
        /// appearance, de-duplicated, capped at <paramref name="max"/>.
        /// </summary>
        public static IReadOnlyList<int> ExtractExamplePostIds(string body, int max)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(body) || max <= 0)
                return result;
            var seen = new HashSet<int>();
            foreach (Match match in Regex.Matches(body, @"post #(\d+)", RegexOptions.IgnoreCase))
            {
                if (int.TryParse(match.Groups[1].Value, out int id) && id > 0 && seen.Add(id))
                {
                    result.Add(id);
                    if (result.Count >= max)
                        break;
                }
            }
            return result;
        }

        /// <summary>
        /// Cuts a wiki dtext body down to its intro: everything before the
        /// first dtext section header ("h4. Examples", "h4#see-also. ..."
        /// etc.). Falls back to the full body when the page starts with a
        /// header. Example-post extraction keeps using the full body.
        /// </summary>
        public static string TrimToIntroSection(string body)
        {
            if (string.IsNullOrEmpty(body))
                return string.Empty;
            string[] lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var kept = new List<string>();
            foreach (string line in lines)
            {
                if (Regex.IsMatch(line.TrimStart(), @"^h[1-6](#[^.\s]*)?\.", RegexOptions.IgnoreCase))
                    break;
                kept.Add(line);
            }
            while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[kept.Count - 1]))
                kept.RemoveAt(kept.Count - 1);
            string intro = string.Join("\n", kept);
            return string.IsNullOrWhiteSpace(intro) ? body : intro;
        }

        /// <summary>Preview info for one post; null when the post is missing
        /// or has no public preview (restricted assets).</summary>
        public async Task<DanbooruPostPreview> GetPostPreviewAsync(int postId)
        {
            if (postId <= 0)
                return null;
            try
            {
                string url = "https://danbooru.donmai.us/posts/" + postId + ".json";
                using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
                JObject item = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                string previewUrl = item.Value<string>("preview_file_url");
                if (string.IsNullOrEmpty(previewUrl))
                    return null;
                return new DanbooruPostPreview
                {
                    Id = postId,
                    PreviewUrl = previewUrl,
                    PostUrl = "https://danbooru.donmai.us/posts/" + postId
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<byte[]> DownloadBytesAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            try
            {
                return await client.GetByteArrayAsync(url).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public static string NormalizeTag(string tag)
        {
            return tag.Trim().ToLowerInvariant().Replace(' ', '_');
        }

        public static string GetWikiUrl(string tag)
        {
            return "https://danbooru.donmai.us/wiki_pages/" + Uri.EscapeDataString(NormalizeTag(tag));
        }

        public void Dispose()
        {
            if (ownsClient)
                client.Dispose();
        }
    }

    public sealed class DanbooruWikiPage
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string[] OtherNames { get; set; } = Array.Empty<string>();
        public DateTimeOffset? UpdatedAt { get; set; }
        public string Url { get; set; }
    }

    public sealed class DanbooruPostPreview
    {
        public int Id { get; set; }
        public string PreviewUrl { get; set; }
        public string PostUrl { get; set; }
    }
}
