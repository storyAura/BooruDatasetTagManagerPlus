using System;
using System.Collections.Generic;
using System.Threading;

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
    }
}
