using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BooruDatasetTagManager
{
    public sealed class OnnxBatchItemResult
    {
        public string Input { get; init; }
        public OnnxTagResult Result { get; init; }
    }

    /// <summary>
    /// Sequential ONNX tagging loop, Program-free so the 300–500 image
    /// "no count cap / single failure does not abort" contract is unit-tested.
    /// </summary>
    public static class OnnxBatchRunner
    {
        public static List<OnnxBatchItemResult> Run(
            IReadOnlyList<string> inputs,
            Func<string, OnnxTagResult> tagImage,
            ICollection<string> errors,
            CancellationToken cancellationToken,
            Action<int, string> onProgress = null,
            OnnxTaggerProgressTracker progressTracker = null)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            if (tagImage == null)
                throw new ArgumentNullException(nameof(tagImage));
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            var results = new List<OnnxBatchItemResult>(inputs.Count);
            int completed = 0;
            foreach (string input in inputs)
            {
                // Stop instead of throwing so results computed so far survive a
                // cancel/close and still get applied and saved by the caller.
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    OnnxTagResult result = tagImage(input);
                    progressTracker?.RecordInference(result.ElapsedMilliseconds);
                    results.Add(new OnnxBatchItemResult { Input = input, Result = result });
                }
                catch (Exception ex)
                {
                    errors.Add(input + ": " + ex.Message);
                }

                completed++;
                onProgress?.Invoke(completed, input);
            }

            return results;
        }

        public static List<OnnxBatchItemResult> RunInBatches(
            IReadOnlyList<string> inputs,
            int batchSize,
            Func<IReadOnlyList<string>, IReadOnlyList<OnnxTagResult>> tagBatch,
            ICollection<string> errors,
            CancellationToken cancellationToken,
            Action<int, string> onProgress = null,
            OnnxTaggerProgressTracker progressTracker = null)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            if (tagBatch == null)
                throw new ArgumentNullException(nameof(tagBatch));
            if (errors == null)
                throw new ArgumentNullException(nameof(errors));

            int size = Math.Max(1, batchSize);
            var results = new List<OnnxBatchItemResult>(inputs.Count);
            int completed = 0;
            for (int offset = 0; offset < inputs.Count; offset += size)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                int count = Math.Min(size, inputs.Count - offset);
                var chunk = new string[count];
                for (int i = 0; i < count; i++)
                    chunk[i] = inputs[offset + i];

                IReadOnlyList<OnnxTagResult> tagged = null;
                string chunkError = null;
                try
                {
                    tagged = tagBatch(chunk);
                }
                catch (Exception ex)
                {
                    chunkError = ex.Message;
                }

                for (int i = 0; i < count; i++)
                {
                    string input = chunk[i];
                    if (chunkError != null || tagged == null || i >= tagged.Count)
                    {
                        errors.Add(input + ": " + (chunkError ?? "batch failed"));
                    }
                    else if (!string.IsNullOrEmpty(tagged[i].Error))
                    {
                        errors.Add(input + ": " + tagged[i].Error);
                    }
                    else
                    {
                        progressTracker?.RecordInference(tagged[i].ElapsedMilliseconds);
                        results.Add(new OnnxBatchItemResult { Input = input, Result = tagged[i] });
                    }

                    completed++;
                    onProgress?.Invoke(completed, input);
                }
            }

            return results;
        }
    }

    internal static class OnnxInferenceBatch
    {
        public const int MinSize = 1;
        public const int MaxSize = 8;

        public static int ClampSize(int requested)
        {
            if (requested < MinSize)
                return MinSize;
            if (requested > MaxSize)
                return MaxSize;
            return requested;
        }

        public static int EffectiveSize(IReadOnlyList<int> inputDimensions, int requested)
        {
            int size = ClampSize(requested);
            if (inputDimensions != null && inputDimensions.Count > 0 && inputDimensions[0] > 0)
                size = Math.Min(size, inputDimensions[0]);
            return Math.Max(MinSize, size);
        }

        public static DenseTensor<float> Concat(IReadOnlyList<DenseTensor<float>> tensors)
        {
            if (tensors == null || tensors.Count == 0)
                throw new ArgumentException("Batch is empty.", nameof(tensors));

            int stride = checked((int)tensors[0].Length);
            int rank = tensors[0].Dimensions.Length;
            var dims = new int[rank];
            dims[0] = tensors.Count;
            for (int d = 1; d < rank; d++)
                dims[d] = tensors[0].Dimensions[d];

            var data = new float[tensors.Count * stride];
            for (int i = 0; i < tensors.Count; i++)
            {
                if (tensors[i].Length != stride)
                    throw new InvalidOperationException("Batch items have different shapes.");
                tensors[i].Buffer.Span.CopyTo(data.AsSpan(i * stride, stride));
            }

            return new DenseTensor<float>(data, dims);
        }

        public static bool TrySplit(float[] output, int batch, out float[][] rows)
        {
            rows = null;
            if (output == null || batch <= 0 || output.Length == 0 || output.Length % batch != 0)
                return false;

            int width = output.Length / batch;
            rows = new float[batch][];
            for (int i = 0; i < batch; i++)
            {
                var row = new float[width];
                Array.Copy(output, i * width, row, 0, width);
                rows[i] = row;
            }

            return true;
        }

        public static List<OnnxTagResult> Execute(
            IReadOnlyList<string> paths,
            int requestedBatch,
            IReadOnlyList<int> inputDimensions,
            Func<string, (DenseTensor<float> Tensor, string Error)> encode,
            Func<DenseTensor<float>, float[]> predict,
            Func<float[], IReadOnlyList<AutoTagProviderItem>> decode)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            int batch = EffectiveSize(inputDimensions, requestedBatch);
            var results = new List<OnnxTagResult>(paths.Count);
            for (int offset = 0; offset < paths.Count; offset += batch)
            {
                int count = Math.Min(batch, paths.Count - offset);
                if (count == 1)
                {
                    results.Add(RunOne(paths[offset], encode, predict, decode));
                    continue;
                }

                try
                {
                    results.AddRange(RunGroup(paths, offset, count, encode, predict, decode));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    for (int i = 0; i < count; i++)
                        results.Add(RunOne(paths[offset + i], encode, predict, decode));
                }
            }

            return results;
        }

        private static OnnxTagResult RunOne(
            string path,
            Func<string, (DenseTensor<float> Tensor, string Error)> encode,
            Func<DenseTensor<float>, float[]> predict,
            Func<float[], IReadOnlyList<AutoTagProviderItem>> decode)
        {
            var stopwatch = Stopwatch.StartNew();
            (DenseTensor<float> tensor, string error) = encode(path);
            if (tensor == null)
                return new OnnxTagResult { Error = error ?? "failed" };

            float[] output = predict(tensor);
            stopwatch.Stop();
            return new OnnxTagResult
            {
                Tags = decode(output),
                ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds
            };
        }

        private static List<OnnxTagResult> RunGroup(
            IReadOnlyList<string> paths,
            int offset,
            int count,
            Func<string, (DenseTensor<float> Tensor, string Error)> encode,
            Func<DenseTensor<float>, float[]> predict,
            Func<float[], IReadOnlyList<AutoTagProviderItem>> decode)
        {
            var slots = new OnnxTagResult[count];
            var tensors = new List<DenseTensor<float>>();
            var indexes = new List<int>();
            for (int i = 0; i < count; i++)
            {
                (DenseTensor<float> tensor, string error) = encode(paths[offset + i]);
                if (tensor == null)
                    slots[i] = new OnnxTagResult { Error = error ?? "failed" };
                else
                {
                    tensors.Add(tensor);
                    indexes.Add(i);
                }
            }

            if (tensors.Count == 0)
                return new List<OnnxTagResult>(slots);

            var stopwatch = Stopwatch.StartNew();
            float[] output = predict(tensors.Count == 1 ? tensors[0] : Concat(tensors));
            stopwatch.Stop();
            double each = stopwatch.Elapsed.TotalMilliseconds / tensors.Count;
            if (tensors.Count == 1)
            {
                slots[indexes[0]] = new OnnxTagResult { Tags = decode(output), ElapsedMilliseconds = each };
                return new List<OnnxTagResult>(slots);
            }

            if (!TrySplit(output, tensors.Count, out float[][] rows))
                throw new InvalidOperationException("Model did not return one row per image.");

            for (int i = 0; i < indexes.Count; i++)
            {
                slots[indexes[i]] = new OnnxTagResult
                {
                    Tags = decode(rows[i]),
                    ElapsedMilliseconds = each
                };
            }

            return new List<OnnxTagResult>(slots);
        }
    }
}
