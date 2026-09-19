using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// One same-slot cluster handed to the model: tags that the danbooru
    /// related-tag graph says may describe a single item (bow / hair ribbon /
    /// hairband). Members are effective tags of verified locked-character items.
    /// </summary>
    public sealed class CharacterTagCluster
    {
        public CharacterTagCluster(IReadOnlyList<string> members)
        {
            Members = members ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Members { get; }

        public override string ToString() => "[" + string.Join(", ", Members) + "]";
    }

    /// <summary>
    /// The targeted "resolution" pass that follows visual review: it lists
    /// the color-less wearable tags and same-slot clusters that survived,
    /// asks the model (reference image, no skills) to settle them, and
    /// applies only answers that stay inside the requested tags. UI-free and
    /// Program-free so it links into the test project.
    /// </summary>
    public static class CharacterTagResolution
    {
        public const string SystemPrompt =
            "You look at ONE anime character reference image and answer two kinds of questions about the listed worn items. "
            + "Report only what is visible on the named character. Return strict JSON only, no prose, no markdown fence.";

        // Container / decoration words never take a color prefix on their own.
        private static readonly HashSet<string> ColorlessExclusions = new HashSet<string>(StringComparer.Ordinal)
        {
            "frills", "ruffles", "pleated", "lace trim", "jewelry", "hair ornament", "hair accessory", "headwear", "legwear"
        };

        public static IReadOnlyList<string> CollectColorlessWearables(IEnumerable<CharacterTagAuditItem> items)
        {
            if (items == null)
                return Array.Empty<string>();
            return items
                .Where(CharacterTagResultCanonicalizer.IsVerifiedWearableItem)
                .Select(item => (Tag: item.EffectiveTag?.Trim(), item.PromptOrder))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Tag)
                    && !ColorlessExclusions.Contains(pair.Tag)
                    && !pair.Tag.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(CharacterTagResultCanonicalizer.Colors.Contains))
                .OrderBy(pair => pair.PromptOrder)
                .ThenBy(pair => pair.Tag, StringComparer.Ordinal)
                .Select(pair => pair.Tag)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Same-slot clusters among the locked character's verified wearable
        /// and hair tags. Clustering uses color-stripped bases so
        /// <c>black hairband</c> and <c>black hair ribbon</c> still group.
        /// Parent/child pairs are excluded on those bases — the canonicalizer
        /// already folds those deterministically.
        /// </summary>
        public static IReadOnlyList<CharacterTagCluster> BuildClusters(
            IEnumerable<CharacterTagAuditItem> items,
            TagNearSynonymIndex nearSynonyms,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (items == null || nearSynonyms == null || nearSynonyms.Count == 0)
                return Array.Empty<CharacterTagCluster>();
            GeneralTagCategoryCatalog vocab = vocabulary ?? GeneralTagCategoryCatalog.Empty;
            var candidates = new List<(string Effective, string Base)>();
            var seenEffective = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterTagAuditItem item in items
                .Where(CharacterTagResultCanonicalizer.IsVerifiedHairOrWearable)
                .OrderBy(item => item.PromptOrder)
                .ThenBy(item => item.Tag, StringComparer.Ordinal))
            {
                string effective = item.EffectiveTag?.Trim();
                if (string.IsNullOrWhiteSpace(effective) || !seenEffective.Add(effective))
                    continue;
                string basis = CharacterTagResultCanonicalizer.StripColorWords(effective);
                if (basis.Length == 0)
                    basis = effective;
                candidates.Add((effective, basis));
            }

            var bases = new List<string>();
            var seenBases = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string _, string basis) in candidates)
            {
                if (seenBases.Add(basis))
                    bases.Add(basis);
            }

            return nearSynonyms
                .Clusters(bases, (a, b) =>
                    CharacterTagResultCanonicalizer.IsImplied(a, b, vocab)
                    || CharacterTagResultCanonicalizer.IsImplied(b, a, vocab))
                .Select(baseMembers =>
                {
                    var memberSet = new HashSet<string>(baseMembers, StringComparer.Ordinal);
                    return (IReadOnlyList<string>)candidates
                        .Where(pair => memberSet.Contains(pair.Base))
                        .Select(pair => pair.Effective)
                        .ToArray();
                })
                .Where(members => members.Count >= 2)
                .Select(members => new CharacterTagCluster(members))
                .ToList();
        }

        public static string FormatClusters(IReadOnlyList<CharacterTagCluster> clusters)
        {
            if (clusters == null || clusters.Count == 0)
                return string.Empty;
            return string.Join(", ", clusters.Select(cluster => cluster.ToString()));
        }

        public static bool NeedsResolution(IReadOnlyList<string> colorless, IReadOnlyList<CharacterTagCluster> clusters)
        {
            return (colorless != null && colorless.Count > 0) || (clusters != null && clusters.Count > 0);
        }

        public static string BuildUserPrompt(
            string triggerWord,
            IReadOnlyList<string> colorless,
            IReadOnlyList<CharacterTagCluster> clusters)
        {
            var text = new System.Text.StringBuilder();
            text.Append("Character: ").Append((triggerWord ?? string.Empty).Trim()).Append('\n');
            text.Append("Allowed color words: ")
                .Append(string.Join(", ", CharacterTagResultCanonicalizer.Colors.OrderBy(color => color, StringComparer.Ordinal)))
                .Append(".\n");
            if (colorless != null && colorless.Count > 0)
            {
                text.Append("1. COLORS. For each item report its dominant visible color on this character as one allowed word, ")
                    .Append("or null when the item is not visible, occluded, or its color is ambiguous. Items: ")
                    .Append(string.Join(", ", colorless))
                    .Append(".\n");
            }
            if (clusters != null && clusters.Count > 0)
            {
                text.Append("2. CLUSTERS. Each cluster lists related tags that may describe ONE physical item. ")
                    .Append("Decide whether they are the same item; if so name the single best tag for it — one of the cluster's tags, ")
                    .Append("optionally prefixed by an allowed color word (for example \"black hair ribbon\"). ")
                    .Append("If they are visibly different items, answer same_item=false. Clusters: ")
                    .Append(FormatClusters(clusters))
                    .Append(". Name the tag that matches what is visible (a band -> black hairband; a tied ribbon -> black hair ribbon); ")
                    .Append("never keep two colored tags for one headpiece.\n");
            }
            text.Append("Output exactly: {\"colors\":[{\"tag\":string,\"color\":string|null,\"evidence\":string}],")
                .Append("\"clusters\":[{\"tags\":[string],\"same_item\":boolean,\"canonical\":string|null,\"evidence\":string}]}. ")
                .Append("Use the tag strings exactly as given. Omit a list only when nothing was asked for it.");
            return text.ToString();
        }

        /// <summary>
        /// Applies the model's answers. Only requested tags change; a color
        /// must be an allowed color word; a cluster canonical must be a
        /// member of that cluster, "color + member", or "color + member base",
        /// and must also be a canonical form in <paramref name="vocabulary"/>.
        /// Returns the number of items redirected. Throws on malformed JSON
        /// so the caller can log and keep the visual-review result unchanged.
        /// </summary>
        public static int Apply(
            IList<CharacterTagAuditItem> items,
            string responseJson,
            IReadOnlyList<string> colorlessRequested,
            IReadOnlyList<CharacterTagCluster> clustersRequested,
            GeneralTagCategoryCatalog vocabulary = null)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (string.IsNullOrWhiteSpace(responseJson))
                return 0;
            JObject root = JObject.Parse(ExtractJson(responseJson));
            int changes = 0;
            var colorless = new HashSet<string>(colorlessRequested ?? Array.Empty<string>(), StringComparer.Ordinal);

            if (root["colors"] is JArray colors)
            {
                foreach (JToken entry in colors.OfType<JObject>())
                {
                    string tag = entry.Value<string>("tag")?.Trim();
                    string color = entry.Value<string>("color")?.Trim().ToLowerInvariant();
                    string evidence = entry.Value<string>("evidence")?.Trim();
                    if (string.IsNullOrEmpty(tag) || !colorless.Contains(tag)
                        || string.IsNullOrEmpty(color) || !CharacterTagResultCanonicalizer.Colors.Contains(color))
                    {
                        continue;
                    }
                    string composed = color + " " + tag;
                    foreach (CharacterTagAuditItem item in items.Where(item =>
                        CharacterTagResultCanonicalizer.IsVerifiedWearableItem(item)
                        && string.Equals(item.EffectiveTag?.Trim(), tag, StringComparison.Ordinal)))
                    {
                        CharacterTagResultCanonicalizer.Redirect(
                            item, composed, item.PromptOrder,
                            "Color check: " + (string.IsNullOrEmpty(evidence) ? composed + " seen in the reference." : evidence));
                        changes++;
                    }
                }
            }

            if (root["clusters"] is JArray clusters && clustersRequested != null)
            {
                foreach (JToken entry in clusters.OfType<JObject>())
                {
                    if (!(entry.Value<bool?>("same_item") ?? false))
                        continue;
                    List<string> tags = (entry["tags"] as JArray)?.Values<string>()
                        .Where(tag => !string.IsNullOrWhiteSpace(tag))
                        .Select(tag => tag.Trim())
                        .Distinct(StringComparer.Ordinal)
                        .ToList() ?? new List<string>();
                    string canonical = entry.Value<string>("canonical")?.Trim();
                    string evidence = entry.Value<string>("evidence")?.Trim();
                    if (tags.Count == 0 || string.IsNullOrEmpty(canonical))
                        continue;

                    CharacterTagCluster cluster = clustersRequested.FirstOrDefault(candidate =>
                        tags.All(tag => candidate.Members.Contains(tag, StringComparer.Ordinal)));
                    if (cluster == null)
                        continue;
                    List<string> affected = tags.Where(tag => cluster.Members.Contains(tag, StringComparer.Ordinal)).ToList();
                    if (affected.Count < 2 || !IsAllowedCanonical(canonical, cluster, vocabulary))
                        continue;

                    List<CharacterTagAuditItem> members = items
                        .Where(item => CharacterTagResultCanonicalizer.IsVerifiedHairOrWearable(item)
                            && affected.Contains(item.EffectiveTag?.Trim(), StringComparer.Ordinal))
                        .ToList();
                    if (members.Count == 0)
                        continue;
                    int order = members.Min(item => item.PromptOrder);
                    string reason = "Same item: " + string.Join(" + ", affected) + " → " + canonical
                        + (string.IsNullOrEmpty(evidence) ? "." : " (" + evidence + ").");
                    foreach (CharacterTagAuditItem item in members)
                    {
                        CharacterTagResultCanonicalizer.Redirect(item, canonical, order, reason);
                        changes++;
                    }
                }
            }

            return changes;
        }

        private static bool IsAllowedCanonical(
            string canonical,
            CharacterTagCluster cluster,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (!CharacterTagResultCanonicalizer.IsCanonicalForm(canonical, vocabulary))
                return false;
            if (cluster.Members.Contains(canonical, StringComparer.Ordinal))
                return true;
            int space = canonical.IndexOf(' ');
            if (space <= 0)
                return false;
            string color = canonical.Substring(0, space);
            string rest = canonical.Substring(space + 1).Trim();
            if (!CharacterTagResultCanonicalizer.Colors.Contains(color) || rest.Length == 0)
                return false;
            if (cluster.Members.Contains(rest, StringComparer.Ordinal))
                return true;
            return cluster.Members.Any(member =>
                string.Equals(CharacterTagResultCanonicalizer.StripColorWords(member), rest, StringComparison.Ordinal));
        }

        private static string ExtractJson(string response)
        {
            string cleaned = response.Trim();
            int thinkEnd = cleaned.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0)
                cleaned = cleaned.Substring(thinkEnd + "</think>".Length).Trim();
            int start = cleaned.IndexOf('{');
            int end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
                cleaned = cleaned.Substring(start, end - start + 1);
            return cleaned;
        }
    }
}
