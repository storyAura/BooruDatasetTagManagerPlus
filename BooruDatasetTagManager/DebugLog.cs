using System;
using System.IO;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Opt-in diagnostic log (Settings -> General -> Debug mode). Disabled by
    /// default: Write is a no-op until Enabled is set, so call sites may log
    /// unconditionally. Appends to debug.log next to the executable and must
    /// never throw (same contract as Program.LogCrash).
    /// </summary>
    internal static class DebugLog
    {
        private static readonly object writeLock = new object();

        public static bool Enabled { get; set; }

        public static string LogPath => Path.Combine(Program.AppPath ?? AppContext.BaseDirectory, "debug.log");

        public static void Write(string source, string message)
        {
            if (!Enabled)
                return;
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {message}{Environment.NewLine}";
            try
            {
                lock (writeLock)
                    File.AppendAllText(LogPath, entry);
            }
            catch
            {
                // Diagnostics must never take the app down.
            }
        }
    }
}
