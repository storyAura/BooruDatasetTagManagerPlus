using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxBatchRunnerTests
{
    [Fact]
    public void Run_collects_five_hundred_successful_results()
    {
        var inputs = MakeInputs(500);
        var errors = new List<string>();

        List<OnnxBatchItemResult> results = OnnxBatchRunner.Run(
            inputs,
            _ => FakeResult("1girl"),
            errors,
            CancellationToken.None);

        Assert.Equal(500, results.Count);
        Assert.Empty(errors);
        Assert.Equal("img-249.png", System.IO.Path.GetFileName(results[249].Input));
        Assert.Equal("1girl", results[249].Result.Tags[0].Tag);
    }

    [Fact]
    public void Run_keeps_going_when_one_image_throws()
    {
        var inputs = MakeInputs(500);
        var errors = new List<string>();

        List<OnnxBatchItemResult> results = OnnxBatchRunner.Run(
            inputs,
            path =>
            {
                if (path.EndsWith("img-249.png", StringComparison.Ordinal))
                    throw new InvalidOperationException("boom");
                return FakeResult("ok");
            },
            errors,
            CancellationToken.None);

        Assert.Equal(499, results.Count);
        Assert.Single(errors);
        Assert.Contains("img-249.png", errors[0]);
        Assert.Contains("boom", errors[0]);
    }

    [Fact]
    public void Run_records_out_of_memory_and_continues()
    {
        var inputs = MakeInputs(10);
        var errors = new List<string>();

        List<OnnxBatchItemResult> results = OnnxBatchRunner.Run(
            inputs,
            path =>
            {
                if (path.EndsWith("img-3.png", StringComparison.Ordinal))
                    throw new OutOfMemoryException("GDI");
                return FakeResult("ok");
            },
            errors,
            CancellationToken.None);

        Assert.Equal(9, results.Count);
        Assert.Single(errors);
        Assert.Contains("GDI", errors[0]);
    }

    [Fact]
    public void Run_returns_partial_results_on_cancel()
    {
        var inputs = MakeInputs(50);
        var errors = new List<string>();
        using var cts = new CancellationTokenSource();
        int seen = 0;

        List<OnnxBatchItemResult> results = OnnxBatchRunner.Run(
            inputs,
            _ =>
            {
                seen++;
                if (seen == 10)
                    cts.Cancel();
                return FakeResult("ok");
            },
            errors,
            cts.Token);

        Assert.Equal(10, results.Count);
        Assert.Empty(errors);
    }

    [Fact]
    public void Run_reports_progress_for_failures_and_successes()
    {
        var inputs = MakeInputs(3);
        var errors = new List<string>();
        var progress = new List<(int Count, string Input)>();

        OnnxBatchRunner.Run(
            inputs,
            path =>
            {
                if (path.EndsWith("img-1.png", StringComparison.Ordinal))
                    throw new InvalidOperationException("skip");
                return FakeResult("ok");
            },
            errors,
            CancellationToken.None,
            (count, input) => progress.Add((count, input)));

        Assert.Equal(3, progress.Count);
        Assert.Equal(1, progress[0].Count);
        Assert.Equal(3, progress[2].Count);
    }

    private static List<string> MakeInputs(int count)
    {
        var inputs = new List<string>(count);
        for (int i = 0; i < count; i++)
            inputs.Add($@"C:\dataset\img-{i}.png");
        return inputs;
    }

    private static OnnxTagResult FakeResult(string tag)
    {
        return new OnnxTagResult
        {
            Tags = new[] { new AutoTagProviderItem { Tag = tag, Confidence = 0.9f } },
            ElapsedMilliseconds = 1
        };
    }
}
