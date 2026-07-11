using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    internal static class TagPostProcessor
    {
        // https://github.com/toriato/stable-diffusion-webui-wd14-tagger/blob/a9eacb1eff904552d3012babfa28b57e1d3e295c/tagger/ui.py#L368
        private static readonly HashSet<string> Kaomojis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "0_0",
            "(o)_(o)",
            "+_+",
            "+_-",
            "._.",
            " _ ",
            "<|>_<|>",
            "=_=",
            ">_<",
            "3_3",
            "6_9",
            ">_o",
            "@_@",
            "^_^",
            "o_o",
            "u_u",
            "x_x",
            "|_|",
            "||_||",
        };

        public static List<string> Process(IEnumerable<string> inferredTags, TaggerSettings settings)
        {
            if (settings == null)
                return inferredTags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList() ?? new List<string>();

            bool replaceUnderscores = ShouldReplaceUnderscores(settings);
            var processed = new List<string>();
            if (inferredTags != null)
            {
                foreach (string tag in inferredTags)
                {
                    if (string.IsNullOrWhiteSpace(tag))
                        continue;

                    processed.Add(replaceUnderscores ? FormatTagName(tag) : tag.Trim());
                }
            }

            var result = new List<string>();
            result.AddRange(ParseTagList(settings.TagPrefix));
            result.AddRange(processed);
            result.AddRange(ParseTagList(settings.TagSuffix));
            return result;
        }

        internal static string FormatTagName(string tag)
        {
            string trimmed = tag.Trim();
            if (Kaomojis.Contains(trimmed))
                return trimmed;

            return trimmed.Replace("_", " ");
        }

        internal static bool ShouldReplaceUnderscores(TaggerSettings settings)
        {
            return settings switch
            {
                Wd14TaggerSettings wd => wd.ReplaceUnderscoresWithSpaces,
                PixAiTaggerSettings pix => pix.ReplaceUnderscoresWithSpaces,
                OpenAiSettings openAi => openAi.ReplaceUnderscoresWithSpaces,
                _ => false
            };
        }

        internal static List<string> ParseTagList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new List<string>();

            return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }
}
