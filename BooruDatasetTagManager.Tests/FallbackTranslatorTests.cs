using System.Net;
using System.Text;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class FallbackTranslatorTests
{
    [Fact]
    public async Task FallbackTranslatorSkipsTimeoutAndUsesNextProvider()
    {
        var slow = new BlockingTranslator(TranslationService.ChineseTranslate);
        var fast = new StaticTranslator(TranslationService.MyMemoryTranslate, "black shoes translated");
        using var translator = new FallbackTranslator(new AbstractTranslator[] { slow, fast }, TimeSpan.FromMilliseconds(40));

        string result = await translator.TranslateAsync("black shoes", "en", "zh-CN");

        Assert.Equal("black shoes translated", result);
        Assert.Equal(1, slow.CallCount);
        Assert.Equal(1, fast.CallCount);
    }

    [Fact]
    public async Task FallbackTranslatorCancelsTheUnderlyingCallOnTimeout()
    {
        var slow = new CancellationObservingTranslator();
        var fast = new StaticTranslator(TranslationService.MyMemoryTranslate, "fallback result");
        using var translator = new FallbackTranslator(new AbstractTranslator[] { slow, fast }, TimeSpan.FromMilliseconds(40));

        string result = await translator.TranslateAsync("black shoes", "en", "zh-CN");

        Assert.Equal("fallback result", result);
        // TRANS-01b: the timeout must actually cancel the in-flight request,
        // not abandon it to keep running in the background.
        Assert.True(slow.WasCanceled);
    }

    [Fact]
    public async Task FallbackTranslatorReturnsEmptyStringWhenEveryProviderFails()
    {
        using var translator = new FallbackTranslator(
            new AbstractTranslator[]
            {
                new StaticTranslator(TranslationService.ChineseTranslate, null),
                new ThrowingTranslator(TranslationService.MyMemoryTranslate)
            },
            TimeSpan.FromMilliseconds(40));

        string result = await translator.TranslateAsync("black shoes", "en", "zh-CN");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task TranslationManagerForceRefreshOverwritesAutomaticCache()
    {
        using var temp = new TemporaryDirectory();
        var translator = new QueueTranslator("old translation", "new translation");
        var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);

        Assert.Equal("old translation", await manager.TranslateAsync("black shoes"));
        Assert.Equal("new translation", await manager.TranslateAsync("black shoes", forceRefresh: true));
        Assert.Equal("new translation", manager.GetTranslation("black shoes"));
    }

    [Fact]
    public async Task TranslationManagerForceRefreshDoesNotOverwriteManualCache()
    {
        using var temp = new TemporaryDirectory();
        var translator = new QueueTranslator("automatic translation");
        var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);
        await manager.AddTranslationAsync("black shoes", "manual translation", true);

        string result = await manager.TranslateAsync("black shoes", forceRefresh: true);

        Assert.Equal("manual translation", result);
        Assert.Equal(0, translator.CallCount);
    }

    [Fact]
    public async Task TranslationManagerUsesDanbooruCsvBeforeOnlineTranslatorWhenEnabled()
    {
        try
        {
            using var temp = new TemporaryDirectory();
            string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
            File.WriteAllText(csvPath, "black_shoes,\u9ed1\u978b");
            Program.ChineseTagLookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
            Program.Settings.UseDanbooruZhCsvBeforeTranslation = true;
            var translator = new QueueTranslator("online translation");
            var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);

            string result = await manager.TranslateAsync("black shoes");

            Assert.Equal("\u9ed1\u978b", result);
            Assert.Equal(0, translator.CallCount);
            Assert.Equal("\u9ed1\u978b", manager.GetTranslation("black shoes"));
        }
        finally
        {
            Program.Settings.UseDanbooruZhCsvBeforeTranslation = false;
            Program.ChineseTagLookup = ChineseTagLookupService.Empty;
        }
    }

    [Fact]
    public async Task TranslationManagerForceRefreshBypassesDanbooruCsv()
    {
        try
        {
            using var temp = new TemporaryDirectory();
            string csvPath = Path.Combine(temp.Path, "danbooru-0-zh.csv");
            File.WriteAllText(csvPath, "black_shoes,\u9ed1\u978b");
            Program.ChineseTagLookup = ChineseTagLookupService.LoadFromFile(csvPath, fixTags: true);
            Program.Settings.UseDanbooruZhCsvBeforeTranslation = true;
            var translator = new QueueTranslator("online translation");
            var manager = new TranslationManager("zh-CN", TranslationService.GoogleTranslate, temp.Path, translator);

            string result = await manager.TranslateAsync("black shoes", forceRefresh: true);

            Assert.Equal("online translation", result);
            Assert.Equal(1, translator.CallCount);
        }
        finally
        {
            Program.Settings.UseDanbooruZhCsvBeforeTranslation = false;
            Program.ChineseTagLookup = ChineseTagLookupService.Empty;
        }
    }

    [Fact]
    public async Task DanbooruWikiClientParsesWikiPage()
    {
        using var client = new DanbooruWikiClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "[{\"title\":\"black_shoes\",\"body\":\"[[Shoes]] that are colored black.\",\"other_names\":[\"black footwear\"],\"updated_at\":\"2026-02-27T14:04:21.120-05:00\"}]",
                    Encoding.UTF8,
                    "application/json")
            })));

        DanbooruWikiPage page = await client.GetWikiPageAsync("black shoes");

        Assert.NotNull(page);
        Assert.Equal("black_shoes", page.Title);
        Assert.Equal("[[Shoes]] that are colored black.", page.Body);
        Assert.Equal(new[] { "black footwear" }, page.OtherNames);
    }

    [Fact]
    public async Task DanbooruWikiClientReturnsNullForEmptyOrFailedResponse()
    {
        using var emptyClient = new DanbooruWikiClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            })));
        using var failedClient = new DanbooruWikiClient(new HttpClient(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        Assert.Null(await emptyClient.GetWikiPageAsync("missing tag"));
        Assert.Null(await failedClient.GetWikiPageAsync("black shoes"));
    }

    [Fact]
    public void DanbooruWikiClientBuildsWikiUrlWithoutLoadedPage()
    {
        Assert.Equal(
            "https://danbooru.donmai.us/wiki_pages/adjusting_eyewear",
            DanbooruWikiClient.GetWikiUrl("adjusting eyewear"));
    }

    [Fact]
    public void DanbooruDTextFormatterConvertsCommonWikiMarkupToPlainText()
    {
        string source = "[[Shoes]] that are colored black.\r\n\r\n"
            + "h4. Examples\r\n\r\n"
            + "* !post #5971134\r\n"
            + "* !post #5211418\r\n\r\n\r\n"
            + "h4. See also\r\n\r\n"
            + "* [[glasses]]\r\n"
            + "* [[monocle]]\r\n"
            + "If two or more fingers are doing the adjusting, also use [[hand on eyewear]].";

        string result = DanbooruDTextFormatter.ToPlainText(source);

        Assert.Equal(
            "Shoes that are colored black.\r\n\r\n"
            + "Examples\r\n\r\n"
            + "- Post #5971134\r\n"
            + "- Post #5211418\r\n\r\n"
            + "See also\r\n\r\n"
            + "- glasses\r\n"
            + "- monocle\r\n"
            + "If two or more fingers are doing the adjusting, also use hand on eyewear.",
            result);
    }

    private sealed class BlockingTranslator : AbstractTranslator
    {
        private int callCount;

        public int CallCount => callCount;

        public BlockingTranslator(TranslationService service) : base(service)
        {
        }

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return "too late";
        }

        public override void Dispose()
        {
        }
    }

    private sealed class CancellationObservingTranslator : AbstractTranslator
    {
        public bool WasCanceled { get; private set; }

        public CancellationObservingTranslator() : base(TranslationService.ChineseTranslate)
        {
        }

        public override async Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                throw;
            }
            return "too late";
        }

        public override void Dispose()
        {
        }
    }

    private sealed class StaticTranslator : AbstractTranslator
    {
        private readonly string result;
        private int callCount;

        public int CallCount => callCount;

        public StaticTranslator(TranslationService service, string result) : base(service)
        {
            this.result = result;
        }

        public override Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(result);
        }

        public override void Dispose()
        {
        }
    }

    private sealed class ThrowingTranslator : AbstractTranslator
    {
        public ThrowingTranslator(TranslationService service) : base(service)
        {
        }

        public override Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated failure");
        }

        public override void Dispose()
        {
        }
    }

    private sealed class QueueTranslator : AbstractTranslator
    {
        private readonly Queue<string> results;
        private int callCount;

        public int CallCount => callCount;

        public QueueTranslator(params string[] results) : base(TranslationService.GoogleTranslate)
        {
            this.results = new Queue<string>(results);
        }

        public override Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(results.Dequeue());
        }

        public override void Dispose()
        {
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> send;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        {
            this.send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"BDTM-fallback-tests-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
