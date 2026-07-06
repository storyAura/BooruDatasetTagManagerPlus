using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxTaggerProgressTrackerTests
{
    [Fact]
    public void EstimateRemaining_UsesRollingAverage()
    {
        TimeSpan eta = OnnxTaggerProgressTracker.EstimateRemaining(2, 10, 500);

        Assert.Equal(TimeSpan.FromMilliseconds(4000), eta);
    }

    [Fact]
    public void EstimateRemaining_ReturnsZeroWhenNoSamples()
    {
        Assert.Equal(TimeSpan.Zero, OnnxTaggerProgressTracker.EstimateRemaining(0, 10, 500));
        Assert.Equal(TimeSpan.Zero, OnnxTaggerProgressTracker.EstimateRemaining(5, 5, 500));
    }

    [Theory]
    [InlineData(450, "450ms")]
    [InlineData(1500, "1.5s")]
    public void FormatInferenceMilliseconds_UsesMsOrSeconds(double milliseconds, string expected)
    {
        Assert.Equal(expected, OnnxTaggerProgressTracker.FormatInferenceMilliseconds(milliseconds));
    }

    [Theory]
    [InlineData(500, "500ms")]
    [InlineData(65000, "1:05")]
    public void FormatDuration_UsesReadableUnits(int milliseconds, string expected)
    {
        Assert.Equal(expected, OnnxTaggerProgressTracker.FormatDuration(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void RecordInference_TracksAverage()
    {
        var tracker = new OnnxTaggerProgressTracker();
        tracker.RecordInference(100);
        tracker.RecordInference(300);

        Assert.Equal(300, tracker.LastInferenceMs);
        Assert.Equal(200, tracker.AverageInferenceMs);
    }
}
