using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
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
}
