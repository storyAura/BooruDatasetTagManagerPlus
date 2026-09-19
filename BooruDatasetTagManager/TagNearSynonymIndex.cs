using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Danbooru "related tags" graph from Data/danbooru_tag_near_synonyms.csv
    /// (<c>tag,near_synonyms</c>). The rows mix hyponyms, hypernyms, same-slot
    /// siblings and a few unrelated actions, so this is NOT a synonym table:
    /// it only says "these tags may describe the same feature slot". The
    /// character audit uses it to group an inventory into clusters that the
    /// model (with the reference image) resolves to distinct real items.
    /// </summary>
    public sealed class TagNearSynonymIndex
    {
        public static readonly TagNearSynonymIndex Empty = new TagNearSynonymIndex();

        private readonly Dictionary<string, HashSet<string>> related =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public int Count => related.Count;

        public static TagNearSynonymIndex LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new TagNearSynonymIndex();
            try
            {
                using var reader = new StreamReader(path);
                return LoadFromReader(reader);
            }
            catch (Exception)
            {
                return new TagNearSynonymIndex();
            }
        }

        public static TagNearSynonymIndex LoadFromReader(TextReader reader)
        {
            var index = new TagNearSynonymIndex();
            if (reader == null)
                return index;
            try
            {
                string header = reader.ReadLine();
                if (header == null)
                    return index;
                List<string> headerFields = CharacterTagCatalog.ParseCsvLine(header);
                int tagCol = IndexOf(headerFields, "tag");
                int relatedCol = IndexOf(headerFields, "near_synonyms");
                if (tagCol < 0)
                    tagCol = 0;
                if (relatedCol < 0)
                    relatedCol = 1;
                string line;
                while ((line = reader.ReadLine()) != null)
                    index.AddLine(line, tagCol, relatedCol);
            }
            catch (Exception)
            {
                // Partial data is still useful; a truncated file yields what parsed.
            }
            return index;
        }

        private void AddLine(string line, int tagCol, int relatedCol)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            List<string> fields = CharacterTagCatalog.ParseCsvLine(line);
            if (fields.Count <= Math.Max(tagCol, relatedCol))
                return;
            string key = GeneralTagCategoryCatalog.NormalizeTag(fields[tagCol]);
            if (key.Length == 0)
                return;
            string[] values = fields[relatedCol]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(GeneralTagCategoryCatalog.NormalizeTag)
                .Where(value => value.Length > 0 && !string.Equals(value, key, StringComparison.Ordinal))
                .ToArray();
            if (values.Length == 0)
                return;
            if (!related.TryGetValue(key, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                related[key] = set;
            }
            foreach (string value in values)
                set.Add(value);
        }

        public IReadOnlyList<string> RelatedTo(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return Array.Empty<string>();
            return related.TryGetValue(GeneralTagCategoryCatalog.NormalizeTag(tag), out HashSet<string> set)
                ? set.OrderBy(value => value, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        /// <summary>
        /// A hit in either direction counts: the source rows are far from
        /// symmetric (about 60% of edges are one-way).
        /// </summary>
        public bool AreRelated(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            string left = GeneralTagCategoryCatalog.NormalizeTag(a);
            string right = GeneralTagCategoryCatalog.NormalizeTag(b);
            if (string.Equals(left, right, StringComparison.Ordinal))
                return false;
            return (related.TryGetValue(left, out HashSet<string> fromLeft) && fromLeft.Contains(right))
                || (related.TryGetValue(right, out HashSet<string> fromRight) && fromRight.Contains(left));
        }

        /// <summary>
        /// Connected components of <paramref name="tags"/> under
        /// <see cref="AreRelated"/>, keeping input order inside each cluster
        /// and returning only clusters with two or more members.
        /// <paramref name="excludePair"/> can veto an edge (for example a
        /// parent/child pair that a deterministic rule already collapses).
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>> Clusters(
            IEnumerable<string> tags,
            Func<string, string, bool> excludePair = null)
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string tag in tags ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(tag) && seen.Add(tag.Trim()))
                    ordered.Add(tag.Trim());
            }
            if (ordered.Count < 2 || related.Count == 0)
                return Array.Empty<IReadOnlyList<string>>();

            int[] parent = Enumerable.Range(0, ordered.Count).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    if (!AreRelated(ordered[i], ordered[j]))
                        continue;
                    if (excludePair != null && excludePair(ordered[i], ordered[j]))
                        continue;
                    int a = Find(i);
                    int b = Find(j);
                    if (a != b)
                        parent[Math.Max(a, b)] = Math.Min(a, b);
                }
            }

            var groups = new Dictionary<int, List<string>>();
            var order = new List<int>();
            for (int i = 0; i < ordered.Count; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out List<string> members))
                {
                    members = new List<string>();
                    groups[root] = members;
                    order.Add(root);
                }
                members.Add(ordered[i]);
            }

            return order
                .Select(root => groups[root])
                .Where(members => members.Count >= 2)
                .Select(members => (IReadOnlyList<string>)members.ToArray())
                .ToArray();
        }

        private static int IndexOf(List<string> header, string name)
        {
            for (int i = 0; i < header.Count; i++)
            {
                if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
