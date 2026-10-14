using BooruDatasetTagManager;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class OnnxInferenceBatchTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(99, 8)]
    public void ClampSize_stays_between_one_and_eight(int requested, int expected)
    {
        Assert.Equal(expected, OnnxInferenceBatch.ClampSize(requested));
    }

    [Fact]
    public void EffectiveSize_honors_a_fixed_batch_of_one()
    {
        Assert.Equal(1, OnnxInferenceBatch.EffectiveSize(new[] { 1, 448, 448, 3 }, 4));
        Assert.Equal(4, OnnxInferenceBatch.EffectiveSize(new[] { -1, 3, 1008, 1008 }, 4));
        Assert.Equal(2, OnnxInferenceBatch.EffectiveSize(new[] { 2, 448, 448, 3 }, 4));
    }

    [Fact]
    public void Concat_stacks_items_on_the_batch_axis()
    {
        var first = new DenseTensor<float>(new float[] { 1f, 2f }, new[] { 1, 2 });
        var second = new DenseTensor<float>(new float[] { 3f, 4f }, new[] { 1, 2 });

        DenseTensor<float> batch = OnnxInferenceBatch.Concat(new[] { first, second });

        Assert.Equal(new[] { 2, 2 }, batch.Dimensions.ToArray());
        Assert.Equal(1f, batch[0, 0]);
        Assert.Equal(4f, batch[1, 1]);
    }

    [Fact]
    public void RunInBatches_groups_inputs_by_the_requested_size()
    {
        var seen = new List<int>();
        List<OnnxBatchItemResult> results = OnnxBatchRunner.RunInBatches(
            new[] { "a", "b", "c" },
            2,
            chunk =>
            {
                seen.Add(chunk.Count);
                return chunk.Select(path => new OnnxTagResult
                {
                    Tags = new[] { new AutoTagProviderItem { Tag = path, Confidence = 1f } },
                    ElapsedMilliseconds = 10
                }).ToList();
            },
            new List<string>(),
            CancellationToken.None);

        Assert.Equal(new[] { 2, 1 }, seen);
        Assert.Equal(new[] { "a", "b", "c" }, results.Select(item => item.Input));
    }
}
