using System;
using System.Collections.Generic;

namespace BooruDatasetTagManager
{
    public readonly struct TagSearchItem
    {
        public string Tag { get; }
        public string Translation { get; }

        public TagSearchItem(string tag, string translation = null)
        {
            Tag = tag ?? string.Empty;
            Translation = translation ?? string.Empty;
        }
    }

    public static class TagSearchHelper
    {
        public static bool IsMatch(
            TagSearchItem item,
            string query,
            StringComparison comp,
            bool wholeWord,
            ISet<string> aliasTags)
        {
            string tag = item.Tag;
            string translation = item.Translation;

            if (string.IsNullOrEmpty(tag) && string.IsNullOrEmpty(translation))
                return false;

            if (wholeWord)
            {
                if (!string.IsNullOrEmpty(tag) && string.Equals(tag, query, comp))
                    return true;
                if (!string.IsNullOrEmpty(translation) && string.Equals(translation, query, comp))
                    return true;
                if (aliasTags != null && !string.IsNullOrEmpty(tag) && aliasTags.Contains(tag))
                    return true;
                return false;
            }

            // Substring / fuzzy match
            if (!string.IsNullOrEmpty(tag) && tag.Contains(query, comp))
                return true;
            if (!string.IsNullOrEmpty(translation) && translation.Contains(query, comp))
                return true;
            if (aliasTags != null && !string.IsNullOrEmpty(tag) && aliasTags.Contains(tag))
                return true;

            return false;
        }

        public static int FindBestMatch(
            IReadOnlyList<TagSearchItem> items,
            string query,
            int startIndex,
            bool forward = true,
            bool matchCase = false,
            bool wholeWord = false,
            ISet<string> aliasTags = null)
        {
            if (items == null)
                return -1;
            return FindBestMatch(items.Count, i => items[i], query, startIndex, forward, matchCase, wholeWord, aliasTags);
        }

        public static int FindBestMatch(
            int count,
            Func<int, TagSearchItem> getItem,
            string query,
            int startIndex,
            bool forward = true,
            bool matchCase = false,
            bool wholeWord = false,
            ISet<string> aliasTags = null)
        {
            if (getItem == null || count <= 0 || string.IsNullOrWhiteSpace(query))
                return -1;

            query = query.Trim();
            StringComparison comp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            startIndex = ((startIndex % count) + count) % count;

            for (int offset = 0; offset < count; offset++)
            {
                int i = forward
                    ? (startIndex + offset) % count
                    : ((startIndex - offset) % count + count) % count;

                TagSearchItem item = getItem(i);
                if (IsMatch(item, query, comp, wholeWord, aliasTags))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
