using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BooruDatasetTagManager
{
    public sealed class ChineseTagLookupService
    {
        private readonly List<LookupEntry> entries;
        private readonly Dictionary<string, string> exactLookup;
        private readonly Dictionary<string, string> englishToChineseLookup;

        private ChineseTagLookupService(List<LookupEntry> entries)
        {
            this.entries = entries;
            exactLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            englishToChineseLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (entry.ChineseNames.Length > 0 && !englishToChineseLookup.ContainsKey(entry.EnglishTag))
                    englishToChineseLookup.Add(entry.EnglishTag, entry.ChineseNames[0]);

                foreach (string chineseName in entry.ChineseNames)
                {
                    if (!exactLookup.ContainsKey(chineseName))
                        exactLookup.Add(chineseName, entry.EnglishTag);
                }
            }
        }

        public static ChineseTagLookupService Empty { get; } = new ChineseTagLookupService(new List<LookupEntry>());

        public static ChineseTagLookupService LoadFromFile(string filePath, bool fixTags)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Empty;

            var entries = new List<LookupEntry>();
            foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                int separatorIndex = line.IndexOf(',');
                if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                    continue;

                string englishTag = NormalizeEnglishTag(line.Substring(0, separatorIndex), fixTags);
                string[] chineseNames = line.Substring(separatorIndex + 1)
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeChineseName)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (englishTag.Length > 0 && chineseNames.Length > 0)
                    entries.Add(new LookupEntry(englishTag, chineseNames));
            }

            return new ChineseTagLookupService(entries);
        }

        public static bool IsSimplifiedChineseLanguage(string language)
        {
            return string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(language, "zh-Hans", StringComparison.OrdinalIgnoreCase);
        }

        public string ResolveInput(string input, string language)
        {
            if (!IsSimplifiedChineseLanguage(language) || string.IsNullOrWhiteSpace(input))
                return input;

            string normalized = NormalizeChineseName(input);
            if (exactLookup.TryGetValue(normalized, out string englishTag))
                return englishTag;

            return input;
        }

        public string GetChineseNameForEnglishTag(string tag, string language)
        {
            if (!IsSimplifiedChineseLanguage(language) || string.IsNullOrWhiteSpace(tag))
                return string.Empty;

            string normalizedTag = NormalizeEnglishTag(tag, fixTags: false);
            if (englishToChineseLookup.TryGetValue(normalizedTag, out string chineseName))
                return chineseName;

            return string.Empty;
        }

        /// <summary>
        /// English tags whose Chinese dictionary names contain <paramref name="input"/>.
        /// Used by the grid search boxes so typing Chinese locates the rows of the
        /// corresponding English tags (synonyms included).
        /// </summary>
        public HashSet<string> FindEnglishTagsByChineseName(string input, string language)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsSimplifiedChineseLanguage(language) || string.IsNullOrWhiteSpace(input) || entries.Count == 0)
                return result;

            string normalized = NormalizeChineseName(input);
            if (normalized.Length == 0)
                return result;

            foreach (var entry in entries)
            {
                foreach (string chineseName in entry.ChineseNames)
                {
                    if (chineseName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(entry.EnglishTag);
                        break;
                    }
                }
            }

            return result;
        }

        public TagsDB.TagItem[] SearchAutocompleteValues(
            string input,
            IEnumerable<TagsDB.TagItem> baseValues,
            string language)
        {
            if (!IsSimplifiedChineseLanguage(language) || string.IsNullOrWhiteSpace(input) || entries.Count == 0)
                return Array.Empty<TagsDB.TagItem>();

            string normalizedInput = NormalizeChineseName(input);
            if (normalizedInput.Length == 0)
                return Array.Empty<TagsDB.TagItem>();

            var countsByTag = (baseValues ?? Enumerable.Empty<TagsDB.TagItem>())
                .GroupBy(item => item.GetTag(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Count), StringComparer.OrdinalIgnoreCase);

            var startsWith = new List<TagsDB.TagItem>();
            var contains = new List<TagsDB.TagItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                countsByTag.TryGetValue(entry.EnglishTag, out int count);
                foreach (string chineseName in entry.ChineseNames)
                {
                    if (chineseName.StartsWith(normalizedInput, StringComparison.OrdinalIgnoreCase))
                    {
                        AddSearchResult(startsWith, seen, chineseName, entry.EnglishTag, count);
                    }
                    else if (chineseName.Contains(normalizedInput))
                    {
                        AddSearchResult(contains, seen, chineseName, entry.EnglishTag, count);
                    }
                }
            }

            return startsWith
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Parent)
                .Concat(contains.OrderByDescending(item => item.Count).ThenBy(item => item.Parent))
                .ToArray();
        }

        public List<TagsDB.TagItem> CreateAutocompleteValues(IEnumerable<TagsDB.TagItem> baseValues, string language)
        {
            var result = baseValues?.ToList() ?? new List<TagsDB.TagItem>();
            if (!IsSimplifiedChineseLanguage(language) || entries.Count == 0)
                return result;

            ApplyEnglishDisplayText(result, language);

            var existingNames = new HashSet<string>(result.Select(item => item.Tag), StringComparer.OrdinalIgnoreCase);
            var countsByTag = result
                .GroupBy(item => item.GetTag(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(item => item.Count), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                countsByTag.TryGetValue(entry.EnglishTag, out int count);
                foreach (string chineseName in entry.ChineseNames)
                {
                    if (!existingNames.Add(chineseName))
                        continue;

                    result.Add(CreateAliasTagItem(chineseName, entry.EnglishTag, count));
                }
            }

            return result;
        }

        public List<TagsDB.TagItem> CreateAutocompleteValues(IEnumerable<string> tags, string language)
        {
            var baseValues = (tags ?? Enumerable.Empty<string>())
                .Select(tag => CreateTagItem(tag, 0))
                .ToList();

            return CreateAutocompleteValues(baseValues, language);
        }

        public static TagsDB.TagItem CreateTagItem(string tag, int count)
        {
            var item = new TagsDB.TagItem();
            item.SetTag(tag ?? string.Empty);
            item.Count = count;
            return item;
        }

        private static TagsDB.TagItem CreateAliasTagItem(string chineseName, string englishTag, int count)
        {
            var item = CreateTagItem(chineseName, count);
            item.IsAlias = true;
            item.Parent = englishTag;
            item.AutocompleteDisplayText = $"{englishTag} ({chineseName})";
            return item;
        }

        private void ApplyEnglishDisplayText(IEnumerable<TagsDB.TagItem> values, string language)
        {
            if (!IsSimplifiedChineseLanguage(language))
                return;

            foreach (var item in values)
            {
                if (item == null || item.IsAlias || string.IsNullOrWhiteSpace(item.Tag))
                    continue;

                string chineseName = GetChineseNameForEnglishTag(item.GetTag(), language);
                if (!string.IsNullOrEmpty(chineseName))
                    item.AutocompleteDisplayText = $"{item.GetTag()} ({chineseName})";
            }
        }

        private static void AddSearchResult(
            List<TagsDB.TagItem> results,
            HashSet<string> seen,
            string chineseName,
            string englishTag,
            int count)
        {
            string key = englishTag + "\t" + chineseName;
            if (!seen.Add(key))
                return;

            results.Add(CreateAliasTagItem(chineseName, englishTag, count));
        }

        private static string NormalizeEnglishTag(string tag, bool fixTags)
        {
            tag = (tag ?? string.Empty).Trim().Trim('"').ToLowerInvariant();
            if (fixTags)
                tag = tag.Replace('_', ' ');
            return tag;
        }

        private static string NormalizeChineseName(string name)
        {
            return (name ?? string.Empty).Trim().Trim('"');
        }

        private sealed class LookupEntry
        {
            public LookupEntry(string englishTag, string[] chineseNames)
            {
                EnglishTag = englishTag;
                ChineseNames = chineseNames;
            }

            public string EnglishTag { get; }
            public string[] ChineseNames { get; }
        }
    }
}
