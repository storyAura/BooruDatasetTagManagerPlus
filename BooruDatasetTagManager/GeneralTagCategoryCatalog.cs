using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Primary + optional secondary category of one tag. L2 is empty when
    /// the row has no secondary (cosplay) or the tag was classified by
    /// identity / fallback rather than the general CSV.
    /// </summary>
    public readonly struct TagCategoryPath : IEquatable<TagCategoryPath>
    {
        public TagCategoryPath(string l1, string l2 = null)
        {
            L1 = l1 ?? string.Empty;
            L2 = l2 ?? string.Empty;
        }

        public string L1 { get; }
        public string L2 { get; }
        public bool HasSecondary => L2.Length > 0;

        public static TagCategoryPath General { get; } = new TagCategoryPath(TagCategoryTaxonomy.General);

        public bool Matches(TagCategoryPath filter)
        {
            if (!string.Equals(L1, filter.L1, StringComparison.Ordinal))
                return false;
            if (!filter.HasSecondary)
                return true;
            return string.Equals(L2, filter.L2, StringComparison.Ordinal);
        }

        /// <summary>
        /// Empty <paramref name="filters"/> means every tag (no category
        /// filter). Otherwise a tag matches when it matches any one filter.
        /// </summary>
        public bool MatchesAny(IReadOnlyList<TagCategoryPath> filters)
        {
            if (filters == null || filters.Count == 0)
                return true;
            for (int i = 0; i < filters.Count; i++)
            {
                if (Matches(filters[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Toggle <paramref name="path"/> in the multi-select list.
        /// Checking an L1 stores the whole primary (and visually checks every
        /// L2). Unchecking one L2 while the primary is selected expands to
        /// every sibling except that L2. When every secondary is selected they
        /// collapse back to the primary. Pass <paramref name="secondaries"/>
        /// so parent/child checkboxes stay in sync; omitting it keeps the
        /// older "replace whole L1 with this one L2" behavior.
        /// </summary>
        public static void ToggleIn(
            List<TagCategoryPath> selected,
            TagCategoryPath path,
            IReadOnlyList<string> secondaries = null)
        {
            if (selected == null)
                return;
            if (!path.HasSecondary)
            {
                if (HasWholePrimary(selected, path.L1) || AllSecondariesSelected(selected, path.L1, secondaries))
                {
                    selected.RemoveAll(p => string.Equals(p.L1, path.L1, StringComparison.Ordinal));
                    return;
                }
                selected.RemoveAll(p => string.Equals(p.L1, path.L1, StringComparison.Ordinal));
                selected.Add(path);
                return;
            }

            if (HasWholePrimary(selected, path.L1))
            {
                selected.RemoveAll(p => string.Equals(p.L1, path.L1, StringComparison.Ordinal));
                if (secondaries != null && secondaries.Count > 0)
                {
                    foreach (string l2 in secondaries)
                    {
                        if (!string.Equals(l2, path.L2, StringComparison.Ordinal))
                            selected.Add(new TagCategoryPath(path.L1, l2));
                    }
                }
                else
                    selected.Add(path);
                return;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i].Equals(path))
                {
                    selected.RemoveAt(i);
                    return;
                }
            }
            selected.Add(path);
            if (AllSecondariesSelected(selected, path.L1, secondaries))
            {
                selected.RemoveAll(p => string.Equals(p.L1, path.L1, StringComparison.Ordinal));
                selected.Add(new TagCategoryPath(path.L1));
            }
        }

        public static bool HasWholePrimary(IReadOnlyList<TagCategoryPath> selected, string l1)
        {
            if (selected == null || string.IsNullOrEmpty(l1))
                return false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (!selected[i].HasSecondary
                    && string.Equals(selected[i].L1, l1, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool AllSecondariesSelected(
            IReadOnlyList<TagCategoryPath> selected,
            string l1,
            IReadOnlyList<string> secondaries)
        {
            if (selected == null || string.IsNullOrEmpty(l1)
                || secondaries == null || secondaries.Count == 0)
            {
                return false;
            }
            if (HasWholePrimary(selected, l1))
                return true;
            for (int s = 0; s < secondaries.Count; s++)
            {
                var path = new TagCategoryPath(l1, secondaries[s]);
                bool found = false;
                for (int i = 0; i < selected.Count; i++)
                {
                    if (selected[i].Equals(path))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            return true;
        }

        public string FormatDisplay(string localizedL1)
        {
            string head = string.IsNullOrEmpty(localizedL1) ? L1 : localizedL1;
            return HasSecondary ? head + " / " + L2 : head;
        }

        public bool Equals(TagCategoryPath other)
        {
            return string.Equals(L1, other.L1, StringComparison.Ordinal)
                && string.Equals(L2, other.L2, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TagCategoryPath other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(L1, L2);
        }

        public override string ToString()
        {
            return HasSecondary ? L1 + "/" + L2 : L1;
        }
    }

    /// <summary>
    /// Stable L1 names (CSV Chinese plus synthetic 角色 / 一般), display
    /// order, sort rank, tint mapping, and the CSV-first classify chain.
    /// </summary>
    public static class TagCategoryTaxonomy
    {
        public const string Character = "角色";
        public const string Copyright = "作品";
        public const string Artist = "画师";
        public const string SubjectCount = "人数";
        public const string Hair = "头发";
        public const string Eyes = "眼睛";
        public const string Body = "身体";
        public const string Expression = "表情";
        public const string Clothing = "服装";
        public const string Accessory = "饰品";
        public const string Cosplay = "cosplay";
        public const string Object = "物品";
        public const string Animal = "动物";
        public const string Food = "食物";
        public const string Action = "动作";
        public const string Composition = "构图";
        public const string Background = "背景";
        public const string Style = "画风";
        public const string General = "一般";
        public const string Meta = "元数据";

        public static readonly string[] PrimaryOrder =
        {
            Character, Copyright, Artist, SubjectCount,
            Hair, Eyes, Body, Expression, Clothing, Accessory, Cosplay,
            Object, Animal, Food, Action, Composition, Background, Style,
            General
        };

        public static int Rank(string l1)
        {
            if (string.IsNullOrEmpty(l1))
                return PrimaryOrder.Length;
            int index = Array.IndexOf(PrimaryOrder, l1);
            return index >= 0 ? index : PrimaryOrder.Length + 1;
        }

        public static TagSemanticCategory ToLegacy(string l1)
        {
            switch (l1)
            {
                case Character: return TagSemanticCategory.Character;
                case Copyright: return TagSemanticCategory.Copyright;
                case Artist: return TagSemanticCategory.Artist;
                case SubjectCount: return TagSemanticCategory.SubjectCount;
                case Hair: return TagSemanticCategory.Hair;
                case Eyes: return TagSemanticCategory.Eyes;
                case Body: return TagSemanticCategory.Body;
                case Expression: return TagSemanticCategory.Expression;
                case Clothing: return TagSemanticCategory.Clothing;
                case Accessory: return TagSemanticCategory.Accessory;
                case Cosplay: return TagSemanticCategory.Cosplay;
                case Object: return TagSemanticCategory.Object;
                case Animal: return TagSemanticCategory.Animal;
                case Food: return TagSemanticCategory.Food;
                case Action: return TagSemanticCategory.Action;
                case Composition: return TagSemanticCategory.Composition;
                case Background: return TagSemanticCategory.Background;
                case Style: return TagSemanticCategory.Style;
                case Meta: return TagSemanticCategory.Meta;
                default: return TagSemanticCategory.General;
            }
        }

        public static string I18nKey(string l1)
        {
            if (string.IsNullOrEmpty(l1) || l1 == General)
                return "TagCategoryGeneral";
            TagSemanticCategory legacy = ToLegacy(l1);
            if (legacy == TagSemanticCategory.General)
                return null;
            return "TagCategory" + legacy;
        }

        public static string FromLegacy(TagSemanticCategory category)
        {
            switch (category)
            {
                case TagSemanticCategory.Character: return Character;
                case TagSemanticCategory.Copyright: return Copyright;
                case TagSemanticCategory.Artist: return Artist;
                case TagSemanticCategory.SubjectCount: return SubjectCount;
                case TagSemanticCategory.Hair: return Hair;
                case TagSemanticCategory.Eyes: return Eyes;
                case TagSemanticCategory.Body: return Body;
                case TagSemanticCategory.Expression: return Expression;
                case TagSemanticCategory.Clothing: return Clothing;
                case TagSemanticCategory.Accessory: return Accessory;
                case TagSemanticCategory.Cosplay: return Cosplay;
                case TagSemanticCategory.Object: return Object;
                case TagSemanticCategory.Animal: return Animal;
                case TagSemanticCategory.Food: return Food;
                case TagSemanticCategory.Action: return Action;
                case TagSemanticCategory.Composition: return Composition;
                case TagSemanticCategory.Background: return Background;
                case TagSemanticCategory.Style: return Style;
                case TagSemanticCategory.Meta: return Meta;
                default: return General;
            }
        }

        /// <summary>
        /// CSV first; then Danbooru identity types / character catalog;
        /// unknown tags become 一般. Catalog may be null or empty.
        /// </summary>
        public static TagCategoryPath Classify(
            string tag,
            int danbooruType,
            GeneralTagCategoryCatalog catalog,
            CharacterTagCatalog characters)
        {
            if (catalog != null && catalog.TryGet(tag, out TagCategoryPath path) && path.L1.Length > 0)
                return path;
            if (danbooruType == 4 || (danbooruType < 0 && characters?.Contains(tag) == true))
                return new TagCategoryPath(Character);
            if (danbooruType == 3)
                return new TagCategoryPath(Copyright);
            if (danbooruType == 1)
                return new TagCategoryPath(Artist);
            if (danbooruType == 5)
                return new TagCategoryPath(Style);
            return TagCategoryPath.General;
        }

        /// <summary>
        /// Resolves a filter name: Chinese L1, "L1/L2", or a
        /// <see cref="TagSemanticCategory"/> member (Hair, hair, …).
        /// </summary>
        public static bool TryParseFilter(string name, out TagCategoryPath path)
        {
            path = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string trimmed = name.Trim();
            int slash = trimmed.IndexOf('/');
            if (slash >= 0)
            {
                string l1 = trimmed.Substring(0, slash).Trim();
                string l2 = trimmed.Substring(slash + 1).Trim();
                if (l1.Length == 0)
                    return false;
                if (Enum.TryParse(l1, ignoreCase: true, out TagSemanticCategory parsedL1))
                    l1 = FromLegacy(parsedL1);
                path = new TagCategoryPath(l1, l2);
                return true;
            }
            if (Enum.TryParse(trimmed, ignoreCase: true, out TagSemanticCategory parsed))
            {
                path = new TagCategoryPath(FromLegacy(parsed));
                return true;
            }
            if (Array.IndexOf(PrimaryOrder, trimmed) >= 0 || trimmed == Meta)
            {
                path = new TagCategoryPath(trimmed);
                return true;
            }
            return false;
        }

        public static IReadOnlyList<string> MenuPrimaries(GeneralTagCategoryCatalog catalog)
        {
            var list = new List<string>(PrimaryOrder);
            if (catalog == null)
                return list;
            foreach (string extra in catalog.PrimaryCategories)
            {
                if (!list.Contains(extra))
                    list.Add(extra);
            }
            return list;
        }
    }

    /// <summary>
    /// General-tag L1/L2 catalog from Data/danbooru_dataset_general.csv.
    /// Keys are normalized like <see cref="CharacterTagCatalog"/> so raw
    /// danbooru tags and space-separated dataset tags both match. Missing
    /// or unreadable files yield an empty catalog.
    /// </summary>
    public sealed class GeneralTagCategoryCatalog
    {
        public static readonly GeneralTagCategoryCatalog Empty = new GeneralTagCategoryCatalog();

        private readonly Dictionary<string, TagCategoryPath> tags =
            new Dictionary<string, TagCategoryPath>(StringComparer.Ordinal);
        private readonly Dictionary<string, SortedSet<string>> secondaries =
            new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> intern =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> primaryCategories = new List<string>();

        public int Count => tags.Count;

        public IReadOnlyList<string> PrimaryCategories => primaryCategories;

        public bool TryGet(string tag, out TagCategoryPath path)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                path = default;
                return false;
            }
            return tags.TryGetValue(Normalize(tag), out path);
        }

        public IReadOnlyList<string> SecondariesOf(string l1)
        {
            if (string.IsNullOrEmpty(l1))
                return Array.Empty<string>();
            return secondaries.TryGetValue(l1, out SortedSet<string> set)
                ? set.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>
        /// L1 and L2 name search: prefix hits before substring hits.
        /// L2 hits are returned as full paths so a UI can pick them flat.
        /// </summary>
        public IReadOnlyList<TagCategoryPath> SearchCategories(string query, int limit = 40)
        {
            if (string.IsNullOrWhiteSpace(query) || limit <= 0)
                return Array.Empty<TagCategoryPath>();
            string term = query.Trim();
            var prefix = new List<TagCategoryPath>();
            var contains = new List<TagCategoryPath>();
            var seen = new HashSet<TagCategoryPath>();

            void consider(TagCategoryPath path, string haystack)
            {
                if (string.IsNullOrEmpty(haystack) || !seen.Add(path))
                    return;
                if (haystack.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                    prefix.Add(path);
                else if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                    contains.Add(path);
            }

            foreach (string l1 in TagCategoryTaxonomy.MenuPrimaries(this))
                consider(new TagCategoryPath(l1), l1);
            foreach (KeyValuePair<string, SortedSet<string>> pair in secondaries)
            {
                foreach (string l2 in pair.Value)
                    consider(new TagCategoryPath(pair.Key, l2), l2);
            }

            return prefix.Concat(contains).Take(limit).ToArray();
        }

        public static GeneralTagCategoryCatalog LoadFromFile(string path)
        {
            var catalog = new GeneralTagCategoryCatalog();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return catalog;
            try
            {
                using var reader = new StreamReader(path);
                string header = reader.ReadLine();
                if (header == null)
                    return catalog;
                List<string> headerFields = CharacterTagCatalog.ParseCsvLine(header);
                int tagCol = IndexOfHeader(headerFields, "tag");
                int l1Col = IndexOfHeader(headerFields, "category_l1");
                int l2Col = IndexOfHeader(headerFields, "category_l2");
                if (tagCol < 0)
                    tagCol = 0;
                if (l1Col < 0)
                    l1Col = 7;
                if (l2Col < 0)
                    l2Col = 8;
                string line;
                while ((line = reader.ReadLine()) != null)
                    catalog.AddLine(line, tagCol, l1Col, l2Col);
                catalog.RebuildPrimaryList();
            }
            catch (Exception)
            {
                catalog.RebuildPrimaryList();
            }
            return catalog;
        }

        private void AddLine(string line, int tagCol, int l1Col, int l2Col)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            List<string> fields = CharacterTagCatalog.ParseCsvLine(line);
            if (fields.Count <= tagCol || fields.Count <= l1Col)
                return;
            string key = Normalize(fields[tagCol]);
            if (key.Length == 0)
                return;
            string l1 = Intern(fields[l1Col].Trim());
            if (l1.Length == 0)
                return;
            string l2 = fields.Count > l2Col ? Intern(fields[l2Col].Trim()) : string.Empty;
            tags[key] = new TagCategoryPath(l1, l2);
            if (l2.Length == 0)
                return;
            if (!secondaries.TryGetValue(l1, out SortedSet<string> set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                secondaries[l1] = set;
            }
            set.Add(l2);
        }

        private void RebuildPrimaryList()
        {
            primaryCategories.Clear();
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (TagCategoryPath path in tags.Values)
                present.Add(path.L1);
            foreach (string l1 in TagCategoryTaxonomy.PrimaryOrder)
            {
                if (present.Remove(l1))
                    primaryCategories.Add(l1);
            }
            foreach (string extra in present.OrderBy(x => x, StringComparer.Ordinal))
                primaryCategories.Add(extra);
        }

        private string Intern(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (intern.TryGetValue(value, out string existing))
                return existing;
            intern[value] = value;
            return value;
        }

        private static int IndexOfHeader(List<string> header, string name)
        {
            for (int i = 0; i < header.Count; i++)
            {
                if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static string Normalize(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return string.Empty;
            return tag.Trim()
                .ToLowerInvariant()
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace('_', ' ');
        }
    }
}
