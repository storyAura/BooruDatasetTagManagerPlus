using System;

namespace BooruDatasetTagManager
{
    public enum OpenAiConnectionErrorKind
    {
        Unknown,
        InvalidApiResponse,
        Authentication,
        Timeout,
        Network
    }

    public static class OpenAiConnectionErrorClassifier
    {
        public static OpenAiConnectionErrorKind Classify(string error)
        {
            string value = error ?? string.Empty;
            if (value.Contains("invalid start of a value", StringComparison.OrdinalIgnoreCase)
                || value.Contains("invalid json", StringComparison.OrdinalIgnoreCase)
                || value.Contains("unexpected character encountered while parsing", StringComparison.OrdinalIgnoreCase)
                || value.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                return OpenAiConnectionErrorKind.InvalidApiResponse;
            }

            if (value.Contains("401", StringComparison.OrdinalIgnoreCase)
                || value.Contains("403", StringComparison.OrdinalIgnoreCase)
                || value.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
                || value.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return OpenAiConnectionErrorKind.Authentication;
            }

            if (value.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || value.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || value.Contains("request was canceled", StringComparison.OrdinalIgnoreCase))
            {
                return OpenAiConnectionErrorKind.Timeout;
            }

            if (value.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || value.Contains("host", StringComparison.OrdinalIgnoreCase)
                || value.Contains("name or service", StringComparison.OrdinalIgnoreCase))
            {
                return OpenAiConnectionErrorKind.Network;
            }

            return OpenAiConnectionErrorKind.Unknown;
        }
    }
}
