using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace BooruDatasetTagManager
{
    public readonly struct OnnxTagResult
    {
        public IReadOnlyList<AutoTagProviderItem> Tags { get; init; }
        public double ElapsedMilliseconds { get; init; }
    }

    public sealed class OnnxTaggerProgressTracker
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private double totalInferenceMs;
        private int inferenceCount;
        private double lastInferenceMs;

        public double LastInferenceMs => lastInferenceMs;

        public double AverageInferenceMs =>
            inferenceCount > 0 ? totalInferenceMs / inferenceCount : 0;

        public TimeSpan Elapsed => elapsed.Elapsed;

        public void RecordInference(double milliseconds)
        {
            lastInferenceMs = milliseconds;
            totalInferenceMs += milliseconds;
            inferenceCount++;
        }

        public static TimeSpan EstimateRemaining(int completed, int total, double averageMsPerImage)
        {
            if (completed <= 0 || total <= completed || averageMsPerImage <= 0)
                return TimeSpan.Zero;

            int remaining = total - completed;
            return TimeSpan.FromMilliseconds(remaining * averageMsPerImage);
        }

        public static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

            if (duration.TotalMinutes >= 1)
                return duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

            if (duration.TotalSeconds >= 10)
                return ((int)Math.Round(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + "s";

            if (duration.TotalSeconds >= 1)
                return duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";

            return ((int)Math.Round(duration.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) + "ms";
        }

        public static string FormatInferenceMilliseconds(double milliseconds)
        {
            if (milliseconds >= 1000)
                return (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "s";

            return ((int)Math.Round(milliseconds)).ToString(CultureInfo.InvariantCulture) + "ms";
        }

        public string FormatStatusLine(string fileName, int completed, int total)
        {
            TimeSpan eta = EstimateRemaining(completed, total, AverageInferenceMs);
            return string.Format(
                CultureInfo.InvariantCulture,
                I18n.GetText("TaggerBatchStatusLine"),
                fileName,
                completed,
                total,
                I18n.GetText("TaggerInferenceTime"),
                FormatInferenceMilliseconds(LastInferenceMs),
                I18n.GetText("TaggerAvgInferenceTime"),
                FormatInferenceMilliseconds(AverageInferenceMs),
                I18n.GetText("TaggerEta"),
                FormatDuration(eta),
                I18n.GetText("TaggerElapsed"),
                FormatDuration(Elapsed));
        }
    }
}
