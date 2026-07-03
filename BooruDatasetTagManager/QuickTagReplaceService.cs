using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    public static class QuickTagReplaceService
    {
        public static List<string> GetReplacementSourceTags(IEnumerable<AllTagsItem> tags, string selectedTag, int threshold)
        {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));

            selectedTag = NormalizeTag(selectedTag);
            if (string.IsNullOrEmpty(selectedTag))
                return new List<string>();

            string category = GetCategoryToken(selectedTag);
            if (string.IsNullOrEmpty(category))
                return new List<string>();

            return tags
                .Where(item => item != null)
                .Where(item => item.Count < threshold)
                .Select(item => NormalizeTag(item.Tag))
                .Where(tag => !string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase))
                .Where(tag => IsSameCategory(tag, category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSameCategory(string tag, string category)
        {
            return string.Equals(tag, category, StringComparison.OrdinalIgnoreCase)
                || tag.EndsWith(" " + category, StringComparison.OrdinalIgnoreCase)
                || tag.EndsWith("_" + category, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetCategoryToken(string tag)
        {
            string normalized = tag.Replace('_', ' ').Trim();
            if (normalized.Length == 0)
                return string.Empty;

            string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[^1];
        }

        private static string NormalizeTag(string tag)
        {
            return (tag ?? string.Empty).Trim().ToLowerInvariant();
        }
    }
}
