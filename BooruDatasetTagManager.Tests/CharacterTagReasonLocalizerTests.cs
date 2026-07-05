using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CharacterTagReasonLocalizerTests
{
    [Fact]
    public async Task UsesUiLanguageAndCoalescesConcurrentDuplicateReasons()
    {
        int calls = 0;
        string observedTarget = null;
        var localizer = new CharacterTagReasonLocalizer("zh-CN", async (text, source, target, token) =>
        {
            Interlocked.Increment(ref calls);
            observedTarget = target;
            await Task.Delay(10, token);
            return "核心发色";
        });

        CharacterTagLocalizedReason[] results = await Task.WhenAll(
            localizer.LocalizeAsync("Core hair color."),
            localizer.LocalizeAsync("Core hair color."));

        Assert.Equal(1, calls);
        Assert.Equal("zh-CN", observedTarget);
        Assert.All(results, result => Assert.Equal("核心发色", result.Text));
        Assert.All(results, result => Assert.False(result.UsedFallback));
    }

    [Fact]
    public async Task EnglishBypassesTranslationAndFailureFallsBackToOriginal()
    {
        int calls = 0;
        var english = new CharacterTagReasonLocalizer("en-US", (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult("unused");
        });
        var failed = new CharacterTagReasonLocalizer("pt-BR", (_, _, _, _) =>
            throw new InvalidOperationException("offline"));

        CharacterTagLocalizedReason englishResult = await english.LocalizeAsync("Core garment.");
        CharacterTagLocalizedReason failedResult = await failed.LocalizeAsync("Core garment.");

        Assert.Equal(0, calls);
        Assert.Equal("Core garment.", englishResult.Text);
        Assert.Equal("Core garment.", failedResult.Text);
        Assert.True(failedResult.UsedFallback);
    }
}
