using System;
using System.Collections.Generic;
using System.Linq;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public static class TagWriteService
    {
        public static void ApplyTags(DataItem item, IReadOnlyList<AutoTagProviderItem> tags, TaggerSettings settings)
        {
            if (item == null || tags == null)
                return;

            IEnumerable<AutoTagProviderItem> ordered = tags;
            if (settings.SortMode == AutoTaggerSort.Confidence)
                ordered = tags.OrderByDescending(tag => tag.Confidence);
            else if (settings.SortMode == AutoTaggerSort.Alphabetical)
                ordered = tags.OrderBy(tag => tag.Tag, StringComparer.OrdinalIgnoreCase);

            List<string> tagNames = TagPostProcessor.Process(ordered.Select(tag => tag.Tag), settings);
            ApplyTagNames(item, tagNames, settings);
        }

        public static void ApplyTagNames(DataItem item, IReadOnlyList<string> tagNames, TaggerSettings settings)
        {
            if (item == null || tagNames == null || tagNames.Count == 0)
                return;

            if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
            {
                item.Tags.Clear();
                item.Tags.AddRange(tagNames, true);
            }
            else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
            {
                foreach (string tag in tagNames)
                    item.Tags.AddTag(tag, true, DatasetManager.AddingType.Down, 0);
            }
            else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList && item.Tags.Count == 0)
            {
                item.Tags.AddRange(tagNames, true);
            }
        }
    }
}
