using System;

namespace BooruDatasetTagManager
{
    public sealed class VideoProgressReporter : IProgress<string>
    {
        private readonly Action<string> updateAction;
        private readonly TimeSpan throttle;
        private DateTime lastReportUtc = DateTime.MinValue;
        private string lastLine = string.Empty;

        public VideoProgressReporter(Action<string> updateAction, int throttleMilliseconds = 200)
        {
            this.updateAction = updateAction ?? throw new ArgumentNullException(nameof(updateAction));
            throttle = TimeSpan.FromMilliseconds(Math.Max(50, throttleMilliseconds));
        }

        public void Report(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            DateTime now = DateTime.UtcNow;
            if (value == lastLine && now - lastReportUtc < throttle)
                return;

            lastLine = value;
            lastReportUtc = now;
            updateAction(value);
        }
    }
}
