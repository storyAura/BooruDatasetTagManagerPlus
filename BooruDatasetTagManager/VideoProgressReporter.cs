using System;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class VideoProgressReporter : IProgress<string>
    {
        /// <summary>
        /// Creates a reporter that always marshals onto <paramref name="control"/>'s
        /// UI thread and swallows the dispose race. Report() runs directly on the
        /// worker thread (ffmpeg output reader), where an unhandled
        /// ObjectDisposedException terminates the whole process.
        /// </summary>
        public static VideoProgressReporter CreateForControl(Control control, Action<string> updateAction, int throttleMilliseconds = 200)
        {
            if (control == null) throw new ArgumentNullException(nameof(control));
            if (updateAction == null) throw new ArgumentNullException(nameof(updateAction));
            return new VideoProgressReporter(line =>
            {
                try
                {
                    if (control.IsDisposed || !control.IsHandleCreated)
                        return;
                    control.BeginInvoke(new Action(() =>
                    {
                        if (!control.IsDisposed)
                            updateAction(line);
                    }));
                }
                catch (ObjectDisposedException)
                {
                    // Control destroyed between the check and BeginInvoke.
                }
                catch (InvalidOperationException)
                {
                    // Handle torn down concurrently.
                }
            }, throttleMilliseconds);
        }

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
