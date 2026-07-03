using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public sealed class OpenAiSpeedTestResult
    {
        public OpenAiSpeedTestResult(bool success, TimeSpan elapsed, string errorMessage)
        {
            Success = success;
            Elapsed = elapsed;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; }
        public TimeSpan Elapsed { get; }
        public string ErrorMessage { get; }
    }

    public static class OpenAiSpeedTestService
    {
        public static async Task<OpenAiSpeedTestResult> MeasureAsync(
            Func<CancellationToken, Task<(string Result, string ErrorMessage)>> requestAsync,
            CancellationToken cancellationToken = default)
        {
            if (requestAsync == null)
                throw new ArgumentNullException(nameof(requestAsync));

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                (string result, string errorMessage) = await requestAsync(cancellationToken);
                stopwatch.Stop();
                bool success = !string.IsNullOrWhiteSpace(result);
                string error = success
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(errorMessage) ? "Empty model response." : errorMessage;
                return new OpenAiSpeedTestResult(success, stopwatch.Elapsed, error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new OpenAiSpeedTestResult(false, stopwatch.Elapsed, ex.Message);
            }
        }
    }
}
