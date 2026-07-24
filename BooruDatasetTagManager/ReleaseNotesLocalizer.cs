using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Release bodies published since v1.2.1 carry one complete notes copy per
    /// language, delimited by "&lt;!-- lang:xx --&gt;" marker lines (Chinese
    /// first, then English; the markers render invisibly on GitHub). Update
    /// prompts show only the section matching the UI language; bodies without
    /// markers (older releases) pass through whole. The display cap lives here,
    /// after selection — capping the raw dual-language body first would cut off
    /// the English section.
    /// </summary>
    public static class ReleaseNotesLocalizer
    {
        private const int MaxChars = 4000;

        private static readonly Regex Marker = new Regex(
            @"<!--\s*lang:([A-Za-z-]+)\s*-->",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string SelectSection(string body, string uiLanguage)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            MatchCollection matches = Marker.Matches(body);
            string selected;
            if (matches.Count == 0)
            {
                selected = body.Trim();
            }
            else
            {
                var sections = new List<(string Lang, string Text)>();
                for (int i = 0; i < matches.Count; i++)
                {
                    int start = matches[i].Index + matches[i].Length;
                    int end = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
                    sections.Add((matches[i].Groups[1].Value, body.Substring(start, end - start).Trim()));
                }
                bool wantChinese = uiLanguage != null
                    && uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                selected = Pick(sections, wantChinese ? "zh" : "en")
                    ?? Pick(sections, "en")
                    ?? sections[0].Text;
            }
            return selected.Length > MaxChars
                ? selected.Substring(0, MaxChars) + "\n..."
                : selected;
        }

        private static string Pick(List<(string Lang, string Text)> sections, string langPrefix)
        {
            foreach ((string lang, string text) in sections)
            {
                if (lang.StartsWith(langPrefix, StringComparison.OrdinalIgnoreCase) && text.Length > 0)
                    return text;
            }
            return null;
        }
    }
}
