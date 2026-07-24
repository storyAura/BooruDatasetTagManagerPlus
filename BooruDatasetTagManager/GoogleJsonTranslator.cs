using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class GoogleJsonTranslator : AbstractTranslator
    {
        private readonly HttpClient client;

        public GoogleJsonTranslator() : this(new HttpClient())
        {
        }

        public GoogleJsonTranslator(HttpClient client) : base(TranslationService.GoogleJsonTranslate)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.client.DefaultRequestHeaders.UserAgent.ParseAdd("BooruDatasetTagManager/1.0");
        }

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl="
                + Uri.EscapeDataString(fromLang)
                + "&tl="
                + Uri.EscapeDataString(toLang)
                + "&dt=t&q="
                + Uri.EscapeDataString(text);

            string json = await client.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            JArray root = JArray.Parse(json);
            return root.First?.First?.First?.Value<string>() ?? string.Empty;
        }

        public override void Dispose()
        {
            client.Dispose();
        }
    }
}
