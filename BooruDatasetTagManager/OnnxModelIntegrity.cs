using System;
using System.IO;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Newtonsoft.Json;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Distinguishes a genuinely unreadable ONNX/sidecar from environment
    /// problems (native runtime, file locks, GPU). Only the former may purge
    /// a just-downloaded model cache.
    /// </summary>
    public static class OnnxModelIntegrity
    {
        public static bool ShouldClearCachedModel(Exception ex)
        {
            if (ex == null || IsEnvironmentFailure(ex) || IsTransientFileLock(ex))
                return false;

            return IsParseFailure(ex);
        }

        public static bool IsTransientFileLock(Exception ex)
        {
            for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (cursor is IOException or UnauthorizedAccessException)
                    return true;

                if (LooksLikeSharingViolation(cursor.Message))
                    return true;
            }

            return false;
        }

        public static void RunWithTransientLockRetry(Action action, int attempts = 5, int delayMilliseconds = 200)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (attempts < 1)
                attempts = 1;

            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (IsTransientFileLock(ex) && i < attempts - 1)
                {
                    Thread.Sleep(delayMilliseconds);
                }
            }
        }

        private static bool IsEnvironmentFailure(Exception ex)
        {
            for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (cursor is FileNotFoundException
                    or DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException
                    or TypeInitializationException
                    or NotSupportedException
                    or OutOfMemoryException
                    or IOException
                    or UnauthorizedAccessException
                    or OperationCanceledException
                    or ObjectDisposedException)
                {
                    return true;
                }

                if (LooksLikeExecutionProviderFailure(cursor.Message))
                    return true;
            }

            return false;
        }

        private static bool IsParseFailure(Exception ex)
        {
            for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
            {
                if (cursor is JsonReaderException or JsonSerializationException or InvalidDataException)
                    return true;

                if (LooksLikeCorruptFileMessage(cursor.Message))
                    return true;

                if (cursor is OnnxRuntimeException && !LooksLikeExecutionProviderFailure(cursor.Message))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeExecutionProviderFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            return ContainsAny(message,
                "directml",
                " dml",
                "dml:",
                "cuda",
                "tensorrt",
                "execution provider",
                "gpu device",
                "dxgi",
                "out of memory");
        }

        private static bool LooksLikeCorruptFileMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            return ContainsAny(message,
                "protobuf",
                "truncated",
                "corrupt",
                "invalid protobuf",
                "parse failed",
                "parsing failed");
        }

        private static bool LooksLikeSharingViolation(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            return ContainsAny(message,
                "being used by another process",
                "sharing violation",
                "cannot access the file",
                "the process cannot access",
                "cannot open file",
                "failed to open");
        }

        private static bool ContainsAny(string message, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (message.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
