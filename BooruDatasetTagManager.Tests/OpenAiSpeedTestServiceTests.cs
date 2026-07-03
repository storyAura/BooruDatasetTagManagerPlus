using Xunit;

namespace BooruDatasetTagManager.Tests;

public class OpenAiSpeedTestServiceTests
{
    [Fact]
    public async Task SpeedTestSendsExactlyOneRequestAndReportsSuccess()
    {
        int calls = 0;

        OpenAiSpeedTestResult result = await OpenAiSpeedTestService.MeasureAsync(_ =>
        {
            calls++;
            return Task.FromResult(("OK", string.Empty));
        });

        Assert.Equal(1, calls);
        Assert.True(result.Success);
        Assert.True(result.Elapsed >= TimeSpan.Zero);
        Assert.Equal(string.Empty, result.ErrorMessage);
    }

    [Fact]
    public async Task SpeedTestReturnsModelErrorWithoutRetrying()
    {
        int calls = 0;

        OpenAiSpeedTestResult result = await OpenAiSpeedTestService.MeasureAsync(_ =>
        {
            calls++;
            return Task.FromResult<(string, string)>((null, "model failed"));
        });

        Assert.Equal(1, calls);
        Assert.False(result.Success);
        Assert.Equal("model failed", result.ErrorMessage);
    }
}
