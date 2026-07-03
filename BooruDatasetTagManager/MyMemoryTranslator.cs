using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class MyMemoryTranslator : AbstractTranslator
    {
        private readonly HttpClient client;

        public MyMemoryTranslator() : this(new HttpClient())
        {
        }

        public MyMemoryTranslator(HttpClient client) : base(TranslationService.MyMemoryTranslate)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("BooruDatasetTagManager/1.0");
        }

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string url = "https://api.mymemory.translated.net/get?q="
                + Uri.EscapeDataString(text)
                + "&langpair="
                + Uri.EscapeDataString(fromLang + "|" + NormalizeLanguage(toLang));

            string json = await client.GetStringAsync(url).ConfigureAwait(false);
            JObject root = JObject.Parse(json);
            int status = root.Value<int?>("responseStatus") ?? 0;
            if (status != 200)
                return string.Empty;

            return root["responseData"]?.Value<string>("translatedText") ?? string.Empty;
        }

        private static string NormalizeLanguage(string language)
        {
            return string.IsNullOrWhiteSpace(language) ? "en" : language;
        }

        public override void Dispose()
        {
            client.Dispose();
        }
    }
}
