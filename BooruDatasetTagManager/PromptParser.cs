using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class PromptParser
    {
        public static float round_bracket_multiplier = 1.1f;
        public static float square_bracket_multiplier = 1f / 1.1f;

        public static List<PromptItem> ParsePrompt(string promptString, bool fixTagsForWeight, string splitSeparator = ",")
        {
            List<PromptItem> result = new List<PromptItem>();
            splitSeparator = splitSeparator.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            if (fixTagsForWeight)
            {
                result = ParsePromptWeight(promptString, splitSeparator);
            }
            else
            {
                string[] tags = promptString.Split(new string[] { splitSeparator }, StringSplitOptions.RemoveEmptyEntries);
                if (tags.Length > 0)
                {
                    foreach (var tag in tags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            string textTag = tag.ToLower().Trim();
                            int tagIndex = result.FindIndex(a => a.Text == textTag);
                            if (tagIndex == -1)
                            {
                                result.Add(new PromptItem(textTag, 1f));
                            }
                                
                        }
                    }
                }
            }
            return result;
        }

        // Danbooru tags carry literal brackets ("moskva (azur lane)", "olivier (brown dust)"),
        // so weight syntax is only recognised when it wraps a WHOLE separator-delimited item:
        // "(tag:1.2)", "((tag))", "[tag]". Running WebUI's attention regex over the whole line
        // split such tags apart and re-wrapped them in another bracket pair on every save.
        // ponytail: per-item only; WebUI group weights spanning separators ("(a, b:1.2)") stay literal.
        private static readonly Regex re_explicit_weight = new Regex(@"^(.*):\s*([+-]?[.\d]+)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex re_escaped_bracket = new Regex(@"\\([()\[\]\\])", RegexOptions.Compiled);

        public static List<PromptItem> ParsePromptWeight(string promptString, string splitSeparator = ",")
        {
            List<PromptItem> result = new List<PromptItem>();
            foreach (string raw in promptString.Split(new string[] { splitSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                PromptItem item = ParseSingleTagWeight(raw);
                if (item.Text.Length > 0 && !result.Exists(a => a.Text == item.Text))
                    result.Add(item);
            }
            if (result.Count == 0)
                result.Add(new PromptItem("", 1f));
            return result;
        }

        public static PromptItem ParseSingleTagWeight(string raw)
        {
            string s = raw.Trim();
            float weight = 1f;
            while (true)
            {
                if (IsWrapped(s, '(', ')'))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                    Match m = re_explicit_weight.Match(s);
                    if (m.Success)
                    {
                        // Invariant culture: decimal-comma locales threw on "(tag:1.1)";
                        // malformed numbers like "1.2.3" fall back to neutral weight.
                        weight *= float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 1f;
                        s = m.Groups[1].Value.Trim();
                    }
                    else
                        weight *= round_bracket_multiplier;
                }
                else if (IsWrapped(s, '[', ']'))
                {
                    s = s.Substring(1, s.Length - 2).Trim();
                    weight *= square_bracket_multiplier;
                }
                else
                    break;
            }
            string text = re_escaped_bracket.Replace(s, "$1").Replace('_', ' ').ToLower().Trim();
            return new PromptItem(text, weight);
        }

        public static string EscapeBrackets(string tag)
        {
            return Regex.Replace(tag, @"(?<!\\)([()\[\]])", @"\$1");
        }

        // True when s[0] is an unescaped open bracket whose matching close is the last char.
        private static bool IsWrapped(string s, char open, char close)
        {
            if (s.Length < 2 || s[0] != open || s[s.Length - 1] != close)
                return false;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == open) depth++;
                else if (s[i] == close && --depth == 0)
                    return i == s.Length - 1;
            }
            return false;
        }

        public class PromptItem
        {
            public string Text { get; set; }
            public float Weight { get; set; }

            public PromptItem(string text, float weight)
            {
                Text = text;
                Weight = weight;
            }

            public override string ToString()
            {
                return $"{Text} : {Weight}";
            }
        }
    }
}
