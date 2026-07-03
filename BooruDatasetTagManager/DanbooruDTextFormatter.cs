using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    public static class DanbooruDTextFormatter
    {
        private static readonly Regex HeadingRegex = new Regex(@"^h[1-6]\.\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WikiLinkRegex = new Regex(@"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled);
        private static readonly Regex PostLinkRegex = new Regex(@"!post\s+#(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UrlLinkRegex = new Regex(@"""([^""]+)"":https?://\S+", RegexOptions.Compiled);

        public static string ToPlainText(string dtext)
        {
            if (string.IsNullOrWhiteSpace(dtext))
                return string.Empty;

            string normalized = dtext.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            List<string> result = new List<string>();
            bool previousBlank = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();
                line = HeadingRegex.Replace(line, string.Empty).TrimEnd();
                line = PostLinkRegex.Replace(line, "Post #$1");
                line = WikiLinkRegex.Replace(line, "$1");
                line = UrlLinkRegex.Replace(line, "$1");

                string trimmed = line.Trim();
                if (trimmed.StartsWith("* "))
                    line = "- " + trimmed.Substring(2).TrimStart();

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!previousBlank && result.Count > 0)
                    {
                        result.Add(string.Empty);
                        previousBlank = true;
                    }
                    continue;
                }

                result.Add(line.TrimEnd());
                previousBlank = false;
            }

            while (result.Count > 0 && result[result.Count - 1] == string.Empty)
                result.RemoveAt(result.Count - 1);

            return string.Join(Environment.NewLine, result);
        }
    }
}
