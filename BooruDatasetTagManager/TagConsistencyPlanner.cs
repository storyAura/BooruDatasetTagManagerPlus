using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BooruDatasetTagManager
{
    public enum TagConsistencyReason
    {
        /// <summary>A lower subject count coexists with a higher one of the
        /// same gender (1boy next to 2boys): the lower one is wrong.</summary>
        SubjectCountConflict,
        /// <summary>"solo" coexists with tags proving two or more subjects.</summary>
        SoloWithMultipleSubjects,
        /// <summary>Two character tags of the same character family (same base
        /// name, e.g. parent "miku" and child "miku (append)") coexist; the
        /// dataset-wide counts decide which one survives.</summary>
        CharacterVariantConflict,
        /// <summary>A child variant tag appears fewer times in the dataset
        /// than the configured trust threshold: it is folded into its parent
        /// (the issue's KeptTag is the replacement, not just a survivor).</summary>
        ChildBelowThreshold
    }

    public sealed class TagConsistencyIssue
    {
        public string ImagePath { get; init; }
        public string RemoveTag { get; init; }
        public string KeptTag { get; init; }
        public TagConsistencyReason Reason { get; init; }
    }

    /// <summary>
    /// Detects per-image tag inconsistencies and plans the removals that fix
    /// them; the caller previews and applies. Pure static (test-linked).
    /// Character families come from the danbooru parent/child relations when
    /// <c>getParentTag</c> is supplied (the character catalog's parent_tag
    /// column — catches renamed variants like racing miku → hatsune miku and
    /// never merges different characters that merely share a base name);
    /// without relation data the textual base-name heuristic — same name
    /// before the parenthetical qualifiers — remains the fallback.
    /// </summary>
    public static class TagConsistencyPlanner
    {
        // "1girl", "2boys", "6+girls", "3others"; deliberately NOT "solo
        // focus"/"multiple girls" — those have their own semantics.
        private static readonly Regex SubjectCountPattern = new Regex(
            @"^(\d+)\+?\s?(girls?|boys?|others?)$", RegexOptions.Compiled);

        /// <param name="childCountThreshold">Minimum dataset-wide count a
        /// child variant needs to be trusted; a rarer child folds into its
        /// nearest trusted ancestor (0 disables the rule). Needs
        /// <paramref name="getParentTag"/> to have any effect.</param>
        public static IReadOnlyList<TagConsistencyIssue> Plan(
            IEnumerable<(string ImagePath, IReadOnlyList<string> Tags)> images,
            Func<string, bool> isCharacterTag,
            IReadOnlyDictionary<string, int> datasetTagCounts,
            Func<string, string> getParentTag = null,
            int childCountThreshold = 0)
        {
            if (images == null)
                throw new ArgumentNullException(nameof(images));
            isCharacterTag ??= _ => false;
            datasetTagCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);

            var issues = new List<TagConsistencyIssue>();
            foreach ((string path, IReadOnlyList<string> tags) in images)
            {
                if (tags == null || tags.Count == 0)
                    continue;
                PlanSubjectCounts(path, tags, issues);
                PlanCharacterVariants(path, tags, isCharacterTag, datasetTagCounts,
                    getParentTag, childCountThreshold, issues);
            }
            return issues;
        }

        private static void PlanSubjectCounts(string path, IReadOnlyList<string> tags, List<TagConsistencyIssue> issues)
        {
            var byGender = new Dictionary<string, List<(string Tag, int Value)>>(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                Match match = SubjectCountPattern.Match((tag ?? string.Empty).Trim());
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out int value))
                    continue;
                string gender = match.Groups[2].Value.TrimEnd('s');
                if (!byGender.TryGetValue(gender, out List<(string, int)> bucket))
                    byGender[gender] = bucket = new List<(string, int)>();
                bucket.Add((tag, value));
            }

            var kept = new List<(string Tag, int Value)>();
            foreach (List<(string Tag, int Value)> bucket in byGender.Values)
            {
                (string Tag, int Value) winner = bucket
                    .OrderByDescending(entry => entry.Value)
                    .ThenByDescending(entry => entry.Tag.Length)
                    .ThenBy(entry => entry.Tag, StringComparer.Ordinal)
                    .First();
                kept.Add(winner);
                foreach ((string tag, int _) in bucket)
                {
                    if (!ReferenceEquals(tag, winner.Tag) && tag != winner.Tag)
                    {
                        issues.Add(new TagConsistencyIssue
                        {
                            ImagePath = path,
                            RemoveTag = tag,
                            KeptTag = winner.Tag,
                            Reason = TagConsistencyReason.SubjectCountConflict
                        });
                    }
                }
            }

            // "solo" claims exactly one subject; the surviving counts summed
            // across genders (1girl + 1boy = 2) prove otherwise.
            if (kept.Sum(entry => entry.Value) >= 2
                && tags.Any(tag => string.Equals(tag?.Trim(), "solo", StringComparison.Ordinal)))
            {
                (string Tag, int Value) strongest = kept
                    .OrderByDescending(entry => entry.Value)
                    .ThenBy(entry => entry.Tag, StringComparer.Ordinal)
                    .First();
                issues.Add(new TagConsistencyIssue
                {
                    ImagePath = path,
                    RemoveTag = "solo",
                    KeptTag = strongest.Tag,
                    Reason = TagConsistencyReason.SoloWithMultipleSubjects
                });
            }
        }

        private static void PlanCharacterVariants(
            string path,
            IReadOnlyList<string> tags,
            Func<string, bool> isCharacterTag,
            IReadOnlyDictionary<string, int> datasetTagCounts,
            Func<string, string> getParentTag,
            int childCountThreshold,
            List<TagConsistencyIssue> issues)
        {
            var families = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag) || !isCharacterTag(tag))
                    continue;
                // Authoritative relations group by the ancestor chain's root;
                // the textual base name only serves when no relation data is
                // available at all (character catalog disabled/missing).
                string familyKey = getParentTag == null
                    ? NormalizeFamilyKey(GetCharacterBaseName(tag))
                    : NormalizeFamilyKey(GetFamilyRoot(tag, getParentTag));
                if (familyKey.Length == 0)
                    continue;
                if (!families.TryGetValue(familyKey, out List<string> family))
                    families[familyKey] = family = new List<string>();
                if (!family.Contains(tag))
                    family.Add(tag);
            }

            foreach (List<string> family in families.Values)
            {
                // Trust pass: a child variant rarer than the threshold is not
                // believed and folds into its nearest trusted ancestor (first
                // one at/above the threshold, else the chain root).
                var finalOf = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string member in family)
                {
                    bool trusted = getParentTag == null
                        || childCountThreshold <= 0
                        || string.IsNullOrEmpty(getParentTag(member))
                        || CountOf(member, datasetTagCounts) >= childCountThreshold;
                    finalOf[member] = trusted
                        ? member
                        : ClimbToTrustedAncestor(member, getParentTag, datasetTagCounts, childCountThreshold);
                }

                List<string> candidates = finalOf.Values
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (candidates.Count == 1 && family.Count == 1 && candidates[0] == family[0])
                    continue;
                // The dataset votes among the surviving identities; a tie
                // prefers the more specific tag.
                string winner = candidates
                    .OrderByDescending(tag => CountOf(tag, datasetTagCounts))
                    .ThenByDescending(CountQualifiers)
                    .ThenBy(tag => tag, StringComparer.Ordinal)
                    .First();
                foreach (string member in family)
                {
                    if (member == winner)
                        continue;
                    issues.Add(new TagConsistencyIssue
                    {
                        ImagePath = path,
                        RemoveTag = member,
                        KeptTag = winner,
                        // Folding into the winner is a replacement (the winner
                        // may be absent from the image); losing to a different
                        // surviving identity is a plain removal.
                        Reason = finalOf[member] == winner
                            ? TagConsistencyReason.ChildBelowThreshold
                            : TagConsistencyReason.CharacterVariantConflict
                    });
                }
            }
        }

        private static int CountOf(string tag, IReadOnlyDictionary<string, int> datasetTagCounts)
        {
            return datasetTagCounts.TryGetValue(tag, out int count) ? count : 0;
        }

        /// <summary>
        /// First ancestor whose dataset count reaches the threshold, else the
        /// chain root. Only called for tags that do have a parent; cycles in
        /// broken relation data terminate at the last unvisited node.
        /// </summary>
        internal static string ClimbToTrustedAncestor(
            string tag,
            Func<string, string> getParentTag,
            IReadOnlyDictionary<string, int> datasetTagCounts,
            int childCountThreshold)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { NormalizeFamilyKey(tag) };
            string current = tag;
            while (true)
            {
                string parent = getParentTag(current);
                if (string.IsNullOrEmpty(parent) || !visited.Add(NormalizeFamilyKey(parent)))
                    return current;
                current = parent;
                if (CountOf(current, datasetTagCounts) >= childCountThreshold)
                    return current;
            }
        }

        /// <summary>
        /// Climbs the parent chain to its root ("racing miku" → "hatsune
        /// miku"); a tag without a recorded parent is its own root. Cycles in
        /// the relation data terminate at the last unvisited tag.
        /// </summary>
        internal static string GetFamilyRoot(string tag, Func<string, string> getParentTag)
        {
            string current = (tag ?? string.Empty).Trim();
            var visited = new HashSet<string>(StringComparer.Ordinal) { NormalizeFamilyKey(current) };
            while (true)
            {
                string parent = getParentTag(current);
                if (string.IsNullOrEmpty(parent) || !visited.Add(NormalizeFamilyKey(parent)))
                    return current;
                current = parent;
            }
        }

        /// <summary>Grouping key form: lowercase, '_'→' ' — so underscore and
        /// space spellings of the same tag land in one family.</summary>
        private static string NormalizeFamilyKey(string tag)
        {
            return (tag ?? string.Empty).Trim().ToLowerInvariant().Replace('_', ' ');
        }

        /// <summary>
        /// Character-family key: the tag with every trailing parenthetical
        /// qualifier stripped — "surtr (colorful wonderland) (arknights)" and
        /// "surtr (arknights)" both map to "surtr".
        /// </summary>
        internal static string GetCharacterBaseName(string tag)
        {
            string current = (tag ?? string.Empty).Trim();
            while (current.EndsWith(")", StringComparison.Ordinal))
            {
                int open = current.LastIndexOf(" (", StringComparison.Ordinal);
                if (open <= 0)
                    break;
                current = current.Substring(0, open).TrimEnd();
            }
            return current;
        }

        private static int CountQualifiers(string tag)
        {
            int count = 0;
            string current = (tag ?? string.Empty).Trim();
            while (current.EndsWith(")", StringComparison.Ordinal))
            {
                int open = current.LastIndexOf(" (", StringComparison.Ordinal);
                if (open <= 0)
                    break;
                current = current.Substring(0, open).TrimEnd();
                count++;
            }
            return count;
        }
    }
}
