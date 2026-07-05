using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public enum CharacterTagAuditStyle
    {
        Sparse,
        Full
    }

    public enum CharacterTagAuditExecutionMode
    {
        Review,
        SummaryApply
    }

    public enum CharacterTagDecision
    {
        Keep,
        Delete,
        Replace,
        Uncertain
    }

    public enum CharacterTagAuditStage
    {
        TextScreening,
        TextScreeningCompleted,
        VisualReview,
        Repair
    }

    public enum CharacterTagCategory
    {
        Identity,
        Hair,
        Eyes,
        Face,
        Body,
        Clothing,
        Footwear,
        Legwear,
        WearableAccessory,
        Action,
        Pose,
        Expression,
        Scene,
        Composition,
        Quality,
        Object,
        Other
    }

    public static class CharacterTagCategoryLocalization
    {
        public static string GetKey(CharacterTagCategory category)
        {
            return "CharacterTagCategory" + category;
        }
    }

    public sealed class CharacterTagInventoryItem
    {
        public string Tag { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public sealed class CharacterTagInventory
    {
        public IReadOnlyList<CharacterTagInventoryItem> Tags { get; private set; } = Array.Empty<CharacterTagInventoryItem>();

        public static CharacterTagInventory Create(IEnumerable<IEnumerable<string>> imageTags)
        {
            if (imageTags == null)
                throw new ArgumentNullException(nameof(imageTags));

            var ordered = new List<CharacterTagInventoryItem>();
            var index = new Dictionary<string, CharacterTagInventoryItem>(StringComparer.Ordinal);
            foreach (IEnumerable<string> tags in imageTags)
            {
                var seenInImage = new HashSet<string>(StringComparer.Ordinal);
                foreach (string rawTag in tags ?? Enumerable.Empty<string>())
                {
                    string tag = rawTag?.Trim();
                    if (string.IsNullOrWhiteSpace(tag) || !seenInImage.Add(tag))
                        continue;
                    if (!index.TryGetValue(tag, out CharacterTagInventoryItem item))
                    {
                        item = new CharacterTagInventoryItem { Tag = tag };
                        index.Add(tag, item);
                        ordered.Add(item);
                    }
                    item.Count++;
                }
            }
            return new CharacterTagInventory { Tags = ordered };
        }

        public CharacterTagInventory WhereMinimumCount(int minimumCount)
        {
            return new CharacterTagInventory
            {
                Tags = Tags.Where(item => item.Count >= minimumCount).Select(item => new CharacterTagInventoryItem
                {
                    Tag = item.Tag,
                    Count = item.Count
                }).ToList()
            };
        }
    }

    public static class CharacterTagAuditPolicy
    {
        public static bool CanDelete(CharacterTagCategory category)
        {
            return category == CharacterTagCategory.Hair
                || category == CharacterTagCategory.Eyes
                || category == CharacterTagCategory.Face
                || category == CharacterTagCategory.Body
                || category == CharacterTagCategory.Clothing
                || category == CharacterTagCategory.Footwear
                || category == CharacterTagCategory.Legwear
                || category == CharacterTagCategory.WearableAccessory;
        }

        public static bool IsValidReplacement(string sourceTag, string replacementTag)
        {
            return !string.IsNullOrWhiteSpace(replacementTag)
                && !string.Equals(sourceTag, replacementTag, StringComparison.Ordinal)
                && replacementTag.IndexOfAny(new[] { ',', '\r', '\n' }) < 0
                && !replacementTag.Any(char.IsControl);
        }
    }

    public sealed class CharacterTagAuditItem
    {
        public string Tag { get; set; } = string.Empty;
        public int Count { get; set; }
        public CharacterTagDecision InitialDecision { get; set; }
        public CharacterTagDecision FinalDecision { get; set; }
        public CharacterTagCategory Category { get; set; } = CharacterTagCategory.Other;
        public string Reason { get; set; } = string.Empty;
        public string ReplacementTag { get; set; } = string.Empty;
        public bool IncludeInPrompt { get; set; }
        public int PromptOrder { get; set; }

        [JsonIgnore]
        public bool CanDelete => CharacterTagAuditPolicy.CanDelete(Category);

        [JsonIgnore]
        public bool CanReplace => CharacterTagAuditPolicy.CanDelete(Category);

        [JsonIgnore]
        public bool ShouldDelete => CanDelete && FinalDecision == CharacterTagDecision.Delete;

        [JsonIgnore]
        public bool ShouldReplace => CanReplace && FinalDecision == CharacterTagDecision.Replace;

        [JsonIgnore]
        public string EffectiveTag => ShouldReplace ? ReplacementTag : Tag;
    }

    public sealed class CharacterTagTokenUsage
    {
        public CharacterTagTokenUsage(int inputTokens, int outputTokens, int totalTokens)
        {
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            TotalTokens = totalTokens;
        }

        public int InputTokens { get; }
        public int OutputTokens { get; }
        public int TotalTokens { get; }
    }

    public sealed class CharacterTagRequestMetrics
    {
        public CharacterTagAuditStage Stage { get; set; }
        public TimeSpan Duration { get; set; }
        public CharacterTagTokenUsage Usage { get; set; }
    }

    public sealed class CharacterTagAuditMetrics
    {
        public TimeSpan TotalDuration { get; set; }
        public List<CharacterTagRequestMetrics> Requests { get; } = new List<CharacterTagRequestMetrics>();
        public bool HasTokenUsage => Requests.Any(item => item.Usage != null);
        public int InputTokens => Requests.Where(item => item.Usage != null).Sum(item => item.Usage.InputTokens);
        public int OutputTokens => Requests.Where(item => item.Usage != null).Sum(item => item.Usage.OutputTokens);
        public int TotalTokens => Requests.Where(item => item.Usage != null).Sum(item => item.Usage.TotalTokens);
    }

    public sealed class CharacterTagAuditResult
    {
        public IReadOnlyList<CharacterTagAuditItem> Items { get; set; } = Array.Empty<CharacterTagAuditItem>();
        public IReadOnlyList<CharacterTagInventoryItem> ExcludedItems { get; set; } = Array.Empty<CharacterTagInventoryItem>();
        public CharacterTagAuditStyle Style { get; set; }
        public string FinalPrompt { get; set; } = string.Empty;
        public CharacterTagAuditMetrics Metrics { get; set; } = new CharacterTagAuditMetrics();
    }

    public sealed class CharacterTagAuditProgress
    {
        public CharacterTagAuditStage Stage { get; set; }
        public IReadOnlyList<CharacterTagAuditItem> Items { get; set; } = Array.Empty<CharacterTagAuditItem>();
        public int CompletedSteps { get; set; }
        public int TotalSteps { get; set; } = 2;
    }

    public sealed class CharacterTagAuditOptions
    {
        public CharacterTagInventory Inventory { get; set; }
        public string TriggerWord { get; set; } = string.Empty;
        public CharacterTagAuditStyle Style { get; set; } = CharacterTagAuditStyle.Sparse;
        public int MinimumCount { get; set; } = 10;
        public string Model { get; set; } = string.Empty;
        public string ReferenceImagePath { get; set; } = string.Empty;
        public string CharacterAuditorSkill { get; set; } = string.Empty;
        public string PromptPyramidSkill { get; set; } = string.Empty;
    }

    public sealed class CharacterTagTriggerCandidate
    {
        public string Tag { get; set; } = string.Empty;
        public int Count { get; set; }

        public override string ToString() => $"{Tag} ({Count})";
    }

    public static class CharacterTagTriggerCandidates
    {
        public static IReadOnlyList<CharacterTagTriggerCandidate> Create(CharacterTagInventory inventory)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            return inventory.Tags
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Tag, StringComparer.Ordinal)
                .Select(item => new CharacterTagTriggerCandidate { Tag = item.Tag, Count = item.Count })
                .ToList();
        }
    }

    public static class CharacterTagPreviewLayout
    {
        public static int CalculateWidth(int containerWidth, int availableHeight, int imageWidth, int imageHeight)
        {
            if (containerWidth <= 0)
                return 0;
            int minimum = Math.Min(containerWidth, Math.Max(1, (int)Math.Round(containerWidth * 0.32)));
            int maximum = Math.Min(containerWidth, Math.Max(minimum, (int)Math.Round(containerWidth * 0.48)));
            if (availableHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return minimum;
            int proportional = (int)Math.Round(availableHeight * (double)imageWidth / imageHeight);
            return Math.Max(minimum, Math.Min(maximum, proportional));
        }
    }

    public static class CharacterTagChoiceLayout
    {
        public static int CalculateDropDownWidth(int controlWidth, IEnumerable<int> itemWidths)
        {
            if (itemWidths == null)
                throw new ArgumentNullException(nameof(itemWidths));
            int longest = itemWidths.DefaultIfEmpty(0).Max();
            return Math.Max(Math.Max(1, controlWidth), longest + 36);
        }
    }

    public sealed class CharacterTagModelRequest
    {
        public CharacterTagAuditStage Stage { get; set; }
        public string Model { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public List<string> ImagePaths { get; } = new List<string>();
    }

    public sealed class CharacterTagModelResponse
    {
        public CharacterTagModelResponse(string result, string errorMessage, CharacterTagTokenUsage usage = null)
        {
            Result = result;
            ErrorMessage = errorMessage;
            Usage = usage;
        }

        public string Result { get; }
        public string ErrorMessage { get; }
        public CharacterTagTokenUsage Usage { get; }
    }

    public sealed class CharacterTagAuditResponseException : Exception
    {
        public CharacterTagAuditResponseException(string message, Exception innerException = null) : base(message, innerException)
        {
        }

        public CharacterTagAuditResponseException(
            string message,
            string sourceTag,
            string replacementTag,
            Exception innerException = null) : base(message, innerException)
        {
            SourceTag = sourceTag;
            ReplacementTag = replacementTag;
        }

        public string SourceTag { get; }
        public string ReplacementTag { get; }
    }

    public static class CharacterTagAuditErrorFormatter
    {
        public static string Format(Exception exception, Func<string, string> getText)
        {
            if (exception is CharacterTagAuditResponseException responseError
                && responseError.SourceTag != null)
            {
                return string.Format(
                    getText("CharacterTagAuditModelInvalidReplacement"),
                    EscapeValue(responseError.SourceTag),
                    EscapeValue(responseError.ReplacementTag));
            }
            return exception?.Message ?? string.Empty;
        }

        private static string EscapeValue(string value)
        {
            if (value == null)
                return string.Empty;
            var result = new StringBuilder();
            foreach (char character in value)
            {
                if (character == '\r')
                    result.Append("\\r");
                else if (character == '\n')
                    result.Append("\\n");
                else if (char.IsControl(character))
                    result.Append("\\u").Append(((int)character).ToString("X4"));
                else
                    result.Append(character);
            }
            return result.ToString();
        }
    }

    public sealed class CharacterTagSkillBundle
    {
        public string CharacterAuditor { get; set; } = string.Empty;
        public string PromptPyramid { get; set; } = string.Empty;
    }

    public static class CharacterTagSkillLoader
    {
        public static CharacterTagSkillBundle Load(string applicationRoot)
        {
            if (string.IsNullOrWhiteSpace(applicationRoot))
                throw new ArgumentException("Application root is required.", nameof(applicationRoot));

            string skillsRoot = Path.Combine(applicationRoot, "Agent", "skills");
            return new CharacterTagSkillBundle
            {
                CharacterAuditor = ReadSkill(Path.Combine(skillsRoot, "character-tag-auditor", "SKILL.md")),
                PromptPyramid = ReadSkill(Path.Combine(skillsRoot, "prompt-pyramid", "SKILL.md"))
            };
        }

        private static string ReadSkill(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Required character tag skill was not found.", path);
            return File.ReadAllText(path);
        }
    }

    public static class CharacterTagDeletionPlanner
    {
        public static IReadOnlyList<string> Remove(IEnumerable<string> originalTags, IEnumerable<string> tagsToDelete)
        {
            if (originalTags == null)
                throw new ArgumentNullException(nameof(originalTags));
            if (tagsToDelete == null)
                throw new ArgumentNullException(nameof(tagsToDelete));
            var deleteSet = new HashSet<string>(tagsToDelete, StringComparer.Ordinal);
            return originalTags.Where(tag => !deleteSet.Contains(tag)).ToList();
        }
    }

    public static class CharacterTagTransformation
    {
        public static IReadOnlyList<string> Apply(
            IEnumerable<string> originalTags,
            IEnumerable<CharacterTagAuditItem> decisions)
        {
            if (originalTags == null)
                throw new ArgumentNullException(nameof(originalTags));
            if (decisions == null)
                throw new ArgumentNullException(nameof(decisions));

            var byTag = decisions.ToDictionary(item => item.Tag, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (string original in originalTags)
            {
                if (byTag.TryGetValue(original, out CharacterTagAuditItem decision) && decision.ShouldDelete)
                    continue;
                string effective = byTag.TryGetValue(original, out decision) && decision.ShouldReplace
                    ? decision.ReplacementTag
                    : original;
                if (!string.IsNullOrWhiteSpace(effective) && emitted.Add(effective))
                    result.Add(effective);
            }
            return result;
        }
    }

    public static class CharacterTagResultCanonicalizer
    {
        private static readonly HashSet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
        {
            "black", "blue", "brown", "green", "grey", "gray", "orange", "pink", "purple", "red", "white", "yellow",
            "multicolored"
        };

        private static readonly HashSet<string> SparseMinorHairTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "hair between eyes", "ahoge", "one side up"
        };

        private static readonly HashSet<string> SparseMinorFaceTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "fang", "fangs", "mole under eye"
        };

        private static readonly string[] JacketAliases =
        {
            "jacket", "open jacket", "cropped jacket", "jacket on shoulders", "off shoulder jacket"
        };

        private static readonly (string Garment, CharacterTagCategory Category)[] GenericGarmentBases =
        {
            ("jacket", CharacterTagCategory.Clothing),
            ("skirt", CharacterTagCategory.Clothing),
            ("dress", CharacterTagCategory.Clothing),
            ("shirt", CharacterTagCategory.Clothing),
            ("leotard", CharacterTagCategory.Clothing),
            ("swimsuit", CharacterTagCategory.Clothing),
            ("bikini", CharacterTagCategory.Clothing),
            ("shorts", CharacterTagCategory.Clothing),
            ("pants", CharacterTagCategory.Clothing),
            ("boots", CharacterTagCategory.Footwear),
            ("shoes", CharacterTagCategory.Footwear),
            ("thighhighs", CharacterTagCategory.Legwear),
            ("socks", CharacterTagCategory.Legwear),
            ("hat", CharacterTagCategory.WearableAccessory),
            ("headwear", CharacterTagCategory.WearableAccessory),
            ("gloves", CharacterTagCategory.WearableAccessory),
            ("hair ribbon", CharacterTagCategory.WearableAccessory),
            ("hairband", CharacterTagCategory.WearableAccessory)
        };

        public static void Apply(IEnumerable<CharacterTagAuditItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<CharacterTagAuditItem> list = items.ToList();

            ApplyBaseRules(list);
        }

        public static void Apply(IEnumerable<CharacterTagAuditItem> items, CharacterTagAuditStyle style)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<CharacterTagAuditItem> list = items.ToList();

            ApplyBaseRules(list);
            if (style == CharacterTagAuditStyle.Sparse)
                ApplySparseRules(list);
        }

        private static void ApplyBaseRules(List<CharacterTagAuditItem> list)
        {

            CharacterTagAuditItem lowTwintails = FindVerified(list, CharacterTagCategory.Hair, "low twintails");
            if (lowTwintails != null)
            {
                ReplaceVerified(list, CharacterTagCategory.Hair, "twin braids", lowTwintails.EffectiveTag);
                ReplaceVerified(list, CharacterTagCategory.Hair, "twintails", lowTwintails.EffectiveTag);
            }

            CharacterTagAuditItem coloredBikini = FindVerifiedColored(list, CharacterTagCategory.Clothing, "bikini");
            CharacterTagAuditItem coloredSkirt = FindVerifiedColored(list, CharacterTagCategory.Clothing, "skirt");
            CharacterTagAuditItem skirtFallback = coloredSkirt
                ?? FindVerified(list, CharacterTagCategory.Clothing, "bikini skirt");

            if (coloredBikini != null)
                ReplaceVerified(list, CharacterTagCategory.Clothing, "bikini", coloredBikini.EffectiveTag);
            if (skirtFallback != null)
            {
                ReplaceVerified(list, CharacterTagCategory.Clothing, "skirt", skirtFallback.EffectiveTag);
                ReplaceVerified(list, CharacterTagCategory.Clothing, "bikini skirt", skirtFallback.EffectiveTag);
            }
            if (coloredBikini != null && skirtFallback != null)
                DeleteVerified(list, CharacterTagCategory.Clothing, "swimsuit");

            ApplyGenericGarmentRules(list);
        }

        private static void ApplyGenericGarmentRules(List<CharacterTagAuditItem> list)
        {
            foreach ((string garment, CharacterTagCategory category) in GenericGarmentBases)
            {
                CharacterTagAuditItem target = FindVerifiedColored(list, category, garment)
                    ?? FindVerifiedSpecificGarment(list, category, garment);
                if (target == null)
                    continue;
                string targetTag = target.EffectiveTag;
                if (string.Equals(garment, "jacket", StringComparison.Ordinal))
                {
                    foreach (string alias in JacketAliases)
                        ReplaceVerified(list, category, alias, targetTag);
                }
                else
                {
                    ReplaceVerified(list, category, garment, targetTag);
                }
            }
        }

        private static CharacterTagAuditItem FindVerifiedSpecificGarment(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagCategory category,
            string garment)
        {
            return items
                .Where(item => IsVerified(item, category)
                    && !string.Equals(item.EffectiveTag, garment, StringComparison.Ordinal)
                    && item.EffectiveTag.EndsWith(" " + garment, StringComparison.Ordinal))
                .OrderByDescending(item => item.EffectiveTag.Length)
                .ThenBy(item => item.Tag, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static void ApplySparseRules(List<CharacterTagAuditItem> list)
        {
            foreach (CharacterTagAuditItem item in list.Where(IsSparseMinorFeature))
                DeleteItem(item, "Non-core hair or facial detail removed in sparse mode.");

            foreach (CharacterTagAuditItem item in list.Where(IsGenericHairOrnament))
                DeleteItem(item, "Generic hair ornament removed in sparse mode.");

            CharacterTagAuditItem coloredHairRibbon = FindVerifiedColored(
                list, CharacterTagCategory.WearableAccessory, "hair ribbon");
            if (coloredHairRibbon != null)
            {
                ReplaceVerified(
                    list,
                    CharacterTagCategory.WearableAccessory,
                    "hair ribbon",
                    coloredHairRibbon.EffectiveTag);
            }

            CharacterTagAuditItem coloredHairband = FindVerifiedColored(
                list, CharacterTagCategory.WearableAccessory, "hairband");
            if (coloredHairband != null)
            {
                ReplaceVerified(
                    list,
                    CharacterTagCategory.WearableAccessory,
                    "hairband",
                    coloredHairband.EffectiveTag);
            }

            CharacterTagAuditItem coloredJacket = FindVerifiedColored(
                list, CharacterTagCategory.Clothing, "jacket");
            if (coloredJacket != null)
            {
                foreach (string alias in JacketAliases)
                {
                    if (!string.Equals(alias, coloredJacket.EffectiveTag, StringComparison.Ordinal))
                        ReplaceVerified(list, CharacterTagCategory.Clothing, alias, coloredJacket.EffectiveTag);
                }
            }
        }

        private static bool IsSparseMinorFeature(CharacterTagAuditItem item)
        {
            if (item.Category == CharacterTagCategory.Hair && IsVerified(item, CharacterTagCategory.Hair))
            {
                return string.Equals(item.Tag, "bangs", StringComparison.Ordinal)
                    || item.Tag.EndsWith(" bangs", StringComparison.Ordinal)
                    || SparseMinorHairTags.Contains(item.Tag);
            }
            return item.Category == CharacterTagCategory.Face
                && IsVerified(item, CharacterTagCategory.Face)
                && SparseMinorFaceTags.Contains(item.Tag);
        }

        private static bool IsGenericHairOrnament(CharacterTagAuditItem item)
        {
            return IsVerified(item, CharacterTagCategory.WearableAccessory)
                && (string.Equals(item.Tag, "hair ornament", StringComparison.Ordinal)
                    || string.Equals(item.Tag, "hair accessory", StringComparison.Ordinal)
                    || item.Tag.EndsWith(" hair ornament", StringComparison.Ordinal));
        }

        private static CharacterTagAuditItem FindVerified(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagCategory category,
            string effectiveTag)
        {
            return items.FirstOrDefault(item => IsVerified(item, category)
                && string.Equals(item.EffectiveTag, effectiveTag, StringComparison.Ordinal));
        }

        private static CharacterTagAuditItem FindVerifiedColored(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagCategory category,
            string garment)
        {
            return items.FirstOrDefault(item => IsVerified(item, category)
                && IsColoredGarment(item.EffectiveTag, garment));
        }

        private static bool IsColoredGarment(string tag, string garment)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            string suffix = " " + garment;
            if (!tag.EndsWith(suffix, StringComparison.Ordinal))
                return false;
            string prefix = tag.Substring(0, tag.Length - suffix.Length);
            return Colors.Contains(prefix);
        }

        private static bool IsVerified(CharacterTagAuditItem item, CharacterTagCategory category)
        {
            return item.Category == category
                && item.IncludeInPrompt
                && item.FinalDecision != CharacterTagDecision.Delete
                && item.FinalDecision != CharacterTagDecision.Uncertain;
        }

        private static void ReplaceVerified(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagCategory category,
            string sourceTag,
            string targetTag)
        {
            List<CharacterTagAuditItem> list = items.ToList();
            CharacterTagAuditItem target = list.FirstOrDefault(item => IsVerified(item, category)
                && string.Equals(item.EffectiveTag, targetTag, StringComparison.Ordinal));
            foreach (CharacterTagAuditItem item in list.Where(item => IsVerified(item, category)
                && string.Equals(item.Tag, sourceTag, StringComparison.Ordinal)
                && !string.Equals(item.Tag, targetTag, StringComparison.Ordinal)))
            {
                item.FinalDecision = CharacterTagDecision.Replace;
                item.ReplacementTag = targetTag;
                item.Reason = "Normalized to the most specific visually confirmed tag.";
                if (target != null)
                    item.PromptOrder = target.PromptOrder;
            }
        }

        private static void DeleteVerified(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagCategory category,
            string sourceTag)
        {
            foreach (CharacterTagAuditItem item in items.Where(item => IsVerified(item, category)
                && string.Equals(item.Tag, sourceTag, StringComparison.Ordinal)))
            {
                DeleteItem(item, "Redundant generic garment category.");
            }
        }

        private static void DeleteItem(CharacterTagAuditItem item, string reason)
        {
            item.FinalDecision = CharacterTagDecision.Delete;
            item.ReplacementTag = string.Empty;
            item.IncludeInPrompt = false;
            item.Reason = reason;
        }
    }

    public static class CharacterTagPromptBuilder
    {
        public static string Build(IEnumerable<CharacterTagAuditItem> items, string triggerWord)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            string trigger = triggerWord?.Trim() ?? string.Empty;
            List<CharacterTagAuditItem> ordered = items
                .Where(item => item.IncludeInPrompt && !item.ShouldDelete)
                .OrderBy(item => item.PromptOrder)
                .ThenBy(item => item.Tag, StringComparer.Ordinal)
                .ToList();
            var candidateTags = ordered
                .Select(item => item.EffectiveTag?.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
            var allEffective = new HashSet<string>(candidateTags, StringComparer.Ordinal);
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(trigger) && seen.Add(trigger))
                result.Add(trigger);
            foreach (CharacterTagAuditItem item in ordered)
            {
                string effective = item.EffectiveTag?.Trim();
                if (string.IsNullOrWhiteSpace(effective))
                    continue;
                if (IsRedundantGenericGarment(effective, allEffective))
                    continue;
                if (seen.Add(effective))
                    result.Add(effective);
            }
            return string.Join(", ", result);
        }

        private static bool IsRedundantGenericGarment(string tag, HashSet<string> allTags)
        {
            foreach (string other in allTags)
            {
                if (string.Equals(other, tag, StringComparison.Ordinal))
                    continue;
                if (other.EndsWith(" " + tag, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }

    public static class CharacterTagAuditResponseParser
    {
        public static IReadOnlyList<CharacterTagAuditItem> ParseAndValidate(
            string response,
            CharacterTagInventory inventory,
            string triggerWord)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            try
            {
                string json = ExtractJson(response);
                JObject root = JObject.Parse(json);
                JArray tags = root["tags"] as JArray
                    ?? throw new CharacterTagAuditResponseException("Response must contain a tags array.");
                var expected = inventory.Tags.ToDictionary(item => item.Tag, StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var parsed = new Dictionary<string, CharacterTagAuditItem>(StringComparer.Ordinal);
                foreach (JToken token in tags)
                {
                    string tag = token.Value<string>("tag")?.Trim();
                    if (string.IsNullOrEmpty(tag) || !expected.ContainsKey(tag))
                        throw new CharacterTagAuditResponseException("Response contains an unknown or empty tag.");
                    if (!seen.Add(tag))
                        throw new CharacterTagAuditResponseException("Response contains a duplicate tag: " + tag);
                    if (!TryParseDecision(token.Value<string>("decision"), out CharacterTagDecision decision))
                        throw new CharacterTagAuditResponseException("Response contains an invalid decision for: " + tag);
                    CharacterTagCategory category = ParseCategory(token.Value<string>("category"));
                    string replacementTag = token.Value<string>("replacement_tag")?.Trim() ?? string.Empty;
                    if (decision == CharacterTagDecision.Replace
                        && string.Equals(tag, replacementTag, StringComparison.Ordinal))
                    {
                        decision = CharacterTagDecision.Keep;
                        replacementTag = string.Empty;
                    }
                    if (decision == CharacterTagDecision.Replace && !CharacterTagAuditPolicy.IsValidReplacement(tag, replacementTag))
                    {
                        throw new CharacterTagAuditResponseException(
                            "Response contains an invalid replacement: " + tag + " -> " + replacementTag,
                            tag,
                            replacementTag);
                    }
                    bool includeInPrompt = token.Value<bool?>("include_in_prompt") ?? false;
                    int promptOrder = token.Value<int?>("prompt_order") ?? int.MaxValue;
                    if (string.Equals(tag, triggerWord?.Trim(), StringComparison.Ordinal))
                    {
                        decision = CharacterTagDecision.Keep;
                        replacementTag = string.Empty;
                        includeInPrompt = true;
                        promptOrder = 0;
                    }
                    if ((decision == CharacterTagDecision.Delete || decision == CharacterTagDecision.Replace)
                        && !CharacterTagAuditPolicy.CanDelete(category))
                    {
                        decision = CharacterTagDecision.Keep;
                        replacementTag = string.Empty;
                    }
                    if (decision == CharacterTagDecision.Delete)
                        includeInPrompt = false;
                    if (!CharacterTagAuditPolicy.CanDelete(category) && category != CharacterTagCategory.Identity)
                        includeInPrompt = false;
                    parsed[tag] = new CharacterTagAuditItem
                    {
                        Tag = tag,
                        Count = expected[tag].Count,
                        FinalDecision = decision,
                        Category = category,
                        Reason = token.Value<string>("reason") ?? string.Empty,
                        ReplacementTag = replacementTag,
                        IncludeInPrompt = includeInPrompt,
                        PromptOrder = promptOrder
                    };
                }
                if (seen.Count != expected.Count)
                    throw new CharacterTagAuditResponseException("Response does not cover every input tag.");
                List<CharacterTagAuditItem> result = inventory.Tags.Select(item => parsed[item.Tag]).ToList();
                var replacementSources = new HashSet<string>(
                    result.Where(item => item.FinalDecision == CharacterTagDecision.Replace).Select(item => item.Tag),
                    StringComparer.Ordinal);
                if (result.Any(item => item.FinalDecision == CharacterTagDecision.Replace
                    && replacementSources.Contains(item.ReplacementTag)))
                {
                    throw new CharacterTagAuditResponseException("Response contains a replacement chain or cycle.");
                }
                return result;
            }
            catch (CharacterTagAuditResponseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CharacterTagAuditResponseException("The model response is not valid character tag JSON.", ex);
            }
        }

        private static bool TryParseDecision(string value, out CharacterTagDecision decision)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "keep":
                    decision = CharacterTagDecision.Keep;
                    return true;
                case "delete":
                    decision = CharacterTagDecision.Delete;
                    return true;
                case "replace":
                    decision = CharacterTagDecision.Replace;
                    return true;
                case "uncertain":
                    decision = CharacterTagDecision.Uncertain;
                    return true;
                default:
                    decision = CharacterTagDecision.Uncertain;
                    return false;
            }
        }

        private static CharacterTagCategory ParseCategory(string value)
        {
            string normalized = value?.Trim().Replace("-", "_").ToLowerInvariant();
            return normalized switch
            {
                "identity" => CharacterTagCategory.Identity,
                "hair" => CharacterTagCategory.Hair,
                "eyes" => CharacterTagCategory.Eyes,
                "face" => CharacterTagCategory.Face,
                "body" or "anatomy" => CharacterTagCategory.Body,
                "clothing" => CharacterTagCategory.Clothing,
                "footwear" => CharacterTagCategory.Footwear,
                "legwear" => CharacterTagCategory.Legwear,
                "wearable_accessory" or "accessory" => CharacterTagCategory.WearableAccessory,
                "action" => CharacterTagCategory.Action,
                "pose" => CharacterTagCategory.Pose,
                "expression" => CharacterTagCategory.Expression,
                "scene" or "background" => CharacterTagCategory.Scene,
                "composition" => CharacterTagCategory.Composition,
                "quality" => CharacterTagCategory.Quality,
                "object" => CharacterTagCategory.Object,
                _ => CharacterTagCategory.Other
            };
        }

        private static string ExtractJson(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                throw new CharacterTagAuditResponseException("The model returned an empty response.");
            string cleaned = response.Trim();
            int thinkEnd = cleaned.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0)
                cleaned = cleaned.Substring(thinkEnd + "</think>".Length).Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLine = cleaned.IndexOf('\n');
                int closing = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (firstLine >= 0 && closing > firstLine)
                    cleaned = cleaned.Substring(firstLine + 1, closing - firstLine - 1).Trim();
            }
            return cleaned;
        }
    }

    public sealed class CharacterTagAuditService
    {
        public const int MaximumPromptCharacters = 1_000_000;
        private readonly Func<CharacterTagModelRequest, CancellationToken, Task<CharacterTagModelResponse>> requestAsync;

        public CharacterTagAuditService(Func<CharacterTagModelRequest, CancellationToken, Task<CharacterTagModelResponse>> requestAsync)
        {
            this.requestAsync = requestAsync ?? throw new ArgumentNullException(nameof(requestAsync));
        }

        public async Task<CharacterTagAuditResult> ExecuteAsync(
            CharacterTagAuditOptions options,
            IProgress<CharacterTagAuditProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            Stopwatch totalTimer = Stopwatch.StartNew();
            var metrics = new CharacterTagAuditMetrics();
            ValidateOptions(options);
            CharacterTagInventory auditedInventory = options.Inventory.WhereMinimumCount(options.MinimumCount);
            IReadOnlyList<CharacterTagInventoryItem> excludedItems = options.Inventory.Tags
                .Where(item => item.Count < options.MinimumCount).ToList();
            if (auditedInventory.Tags.Count == 0)
            {
                totalTimer.Stop();
                metrics.TotalDuration = totalTimer.Elapsed;
                return new CharacterTagAuditResult
                {
                    ExcludedItems = excludedItems,
                    Style = options.Style,
                    Metrics = metrics
                };
            }
            string systemPrompt = BuildSystemPrompt(options);
            string inventoryJson = JsonConvert.SerializeObject(auditedInventory.Tags);
            string textPrompt = "Audit every supplied tag using the requested style. Return strict JSON only.\n"
                + "Trigger word (must keep): " + options.TriggerWord.Trim() + "\n"
                + "Style: " + options.Style.ToString().ToLowerInvariant() + "\nTags: " + inventoryJson;
            EnsurePromptSize(systemPrompt, textPrompt);

            progress?.Report(new CharacterTagAuditProgress
            {
                Stage = CharacterTagAuditStage.TextScreening,
                CompletedSteps = 0,
                TotalSteps = 2
            });
            IReadOnlyList<CharacterTagAuditItem> initial = await RequestValidatedAsync(
                new CharacterTagModelRequest
                {
                    Stage = CharacterTagAuditStage.TextScreening,
                    Model = options.Model,
                    SystemPrompt = systemPrompt,
                    UserPrompt = textPrompt
                }, auditedInventory, options.TriggerWord, metrics, cancellationToken).ConfigureAwait(false);

            progress?.Report(new CharacterTagAuditProgress
            {
                Stage = CharacterTagAuditStage.TextScreeningCompleted,
                Items = initial,
                CompletedSteps = 1,
                TotalSteps = 2
            });

            IReadOnlyList<CharacterTagAuditItem> final = await RunVisualReviewAsync(
                options, auditedInventory, initial, metrics, progress, visualReviewOnly: false, cancellationToken).ConfigureAwait(false);
            totalTimer.Stop();
            metrics.TotalDuration = totalTimer.Elapsed;
            return new CharacterTagAuditResult
            {
                Items = final,
                ExcludedItems = excludedItems,
                Style = options.Style,
                FinalPrompt = CharacterTagPromptBuilder.Build(final, options.TriggerWord),
                Metrics = metrics
            };
        }

        public async Task<CharacterTagAuditResult> ExecuteVisualReviewAsync(
            CharacterTagAuditOptions options,
            IReadOnlyList<CharacterTagAuditItem> initialItems,
            IProgress<CharacterTagAuditProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            Stopwatch totalTimer = Stopwatch.StartNew();
            var metrics = new CharacterTagAuditMetrics();
            ValidateOptions(options);
            if (initialItems == null || initialItems.Count == 0)
                throw new ArgumentException("Initial screening items are required.", nameof(initialItems));
            CharacterTagInventory auditedInventory = options.Inventory.WhereMinimumCount(options.MinimumCount);
            IReadOnlyList<CharacterTagInventoryItem> excludedItems = options.Inventory.Tags
                .Where(item => item.Count < options.MinimumCount).ToList();
            IReadOnlyList<CharacterTagAuditItem> final = await RunVisualReviewAsync(
                options, auditedInventory, initialItems, metrics, progress, visualReviewOnly: true, cancellationToken).ConfigureAwait(false);
            totalTimer.Stop();
            metrics.TotalDuration = totalTimer.Elapsed;
            return new CharacterTagAuditResult
            {
                Items = final,
                ExcludedItems = excludedItems,
                Style = options.Style,
                FinalPrompt = CharacterTagPromptBuilder.Build(final, options.TriggerWord),
                Metrics = metrics
            };
        }

        private async Task<IReadOnlyList<CharacterTagAuditItem>> RunVisualReviewAsync(
            CharacterTagAuditOptions options,
            CharacterTagInventory auditedInventory,
            IReadOnlyList<CharacterTagAuditItem> initial,
            CharacterTagAuditMetrics metrics,
            IProgress<CharacterTagAuditProgress> progress,
            bool visualReviewOnly,
            CancellationToken cancellationToken)
        {
            int totalSteps = visualReviewOnly ? 1 : 2;
            int completedBeforeVisual = visualReviewOnly ? 0 : 1;
            string systemPrompt = BuildSystemPrompt(options);
            string visualPrompt = BuildVisualPrompt(initial);
            EnsurePromptSize(systemPrompt, visualPrompt);
            var visualRequest = new CharacterTagModelRequest
            {
                Stage = CharacterTagAuditStage.VisualReview,
                Model = options.Model,
                SystemPrompt = systemPrompt,
                UserPrompt = visualPrompt
            };
            visualRequest.ImagePaths.Add(options.ReferenceImagePath);
            progress?.Report(new CharacterTagAuditProgress
            {
                Stage = CharacterTagAuditStage.VisualReview,
                Items = initial,
                CompletedSteps = completedBeforeVisual,
                TotalSteps = totalSteps
            });
            IReadOnlyList<CharacterTagAuditItem> final = await RequestValidatedAsync(
                visualRequest, auditedInventory, options.TriggerWord, metrics, cancellationToken).ConfigureAwait(false);

            var initialByTag = initial.ToDictionary(item => item.Tag, StringComparer.Ordinal);
            foreach (CharacterTagAuditItem item in final)
                item.InitialDecision = initialByTag[item.Tag].FinalDecision;
            CharacterTagResultCanonicalizer.Apply(final, options.Style);
            progress?.Report(new CharacterTagAuditProgress
            {
                Stage = CharacterTagAuditStage.VisualReview,
                Items = final,
                CompletedSteps = totalSteps,
                TotalSteps = totalSteps
            });
            return final;
        }

        private static string BuildVisualPrompt(IReadOnlyList<CharacterTagAuditItem> initial)
        {
            return "Review the preliminary tag decisions against the attached reference image. "
                + "Return the same complete strict JSON schema. Replacement targets may be new normalized tags, "
                + "but every original tag must still appear exactly once.\n"
                + "Explicitly list and re-check every color-less garment, footwear, legwear, and wearable accessory tag "
                + "(for example jacket, boots, shirt, skirt, hair ribbon). When the reference clearly shows its color on "
                + "the locked character, use replace with the color-prefixed tag (for example jacket -> black jacket) even "
                + "if that colored tag does not exist anywhere in the inventory. Keep the color-less tag only when the "
                + "color is genuinely unverifiable, and explain why in reason. Never answer replace with an empty "
                + "replacement_tag.\nPreliminary: "
                + JsonConvert.SerializeObject(initial.Select(item => new
                {
                    tag = item.Tag,
                    decision = item.FinalDecision.ToString().ToLowerInvariant(),
                    replacement_tag = item.ReplacementTag,
                    category = CategoryToWireValue(item.Category),
                    reason = item.Reason,
                    include_in_prompt = item.IncludeInPrompt,
                    prompt_order = item.PromptOrder
                }));
        }

        private async Task<IReadOnlyList<CharacterTagAuditItem>> RequestValidatedAsync(
            CharacterTagModelRequest request,
            CharacterTagInventory inventory,
            string triggerWord,
            CharacterTagAuditMetrics metrics,
            CancellationToken cancellationToken)
        {
            CharacterTagModelResponse response = await RequestWithMetricsAsync(request, metrics, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                throw new InvalidOperationException(response.ErrorMessage);
            try
            {
                return CharacterTagAuditResponseParser.ParseAndValidate(response.Result, inventory, triggerWord);
            }
            catch (CharacterTagAuditResponseException original)
            {
                var repair = new CharacterTagModelRequest
                {
                    Stage = CharacterTagAuditStage.Repair,
                    Model = request.Model,
                    SystemPrompt = "Repair JSON syntax, schema, and semantic validation. Preserve every original tag name exactly once. "
                        + "You may correct invalid decisions and replacement targets. If a replace target equals its source, use keep with an empty replacement_tag. "
                        + "Return strict JSON only.",
                    UserPrompt = "Expected tags: " + JsonConvert.SerializeObject(inventory.Tags.Select(item => item.Tag))
                        + "\nMalformed response:\n" + response.Result
                };
                CharacterTagModelResponse repaired = await RequestWithMetricsAsync(repair, metrics, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(repaired.ErrorMessage))
                    throw new CharacterTagAuditResponseException(repaired.ErrorMessage, original);
                return CharacterTagAuditResponseParser.ParseAndValidate(repaired.Result, inventory, triggerWord);
            }
        }

        private async Task<CharacterTagModelResponse> RequestWithMetricsAsync(
            CharacterTagModelRequest request,
            CharacterTagAuditMetrics metrics,
            CancellationToken cancellationToken)
        {
            Stopwatch timer = Stopwatch.StartNew();
            CharacterTagModelResponse response = await requestAsync(request, cancellationToken).ConfigureAwait(false);
            timer.Stop();
            metrics.Requests.Add(new CharacterTagRequestMetrics
            {
                Stage = request.Stage,
                Duration = timer.Elapsed,
                Usage = response.Usage
            });
            return response;
        }

        private static string BuildSystemPrompt(CharacterTagAuditOptions options)
        {
            string styleRule = options.Style == CharacterTagAuditStyle.Sparse
                ? "Sparse style: delete incorrect/conflicting and non-core appearance details, including generic redundant, pattern, frill, ruffle, and material tags. "
                    + "Replace correct but imprecise clothing/headwear tags with visually verified normalized color+item tags. "
                : "Full style: delete only incorrect/conflicting appearance details, keep real pattern/frill/material details, "
                    + "and replace only clearly redundant generic tags with visually verified normalized tags. ";
            return "You are a strict character LoRA tag auditor. Follow both skills below. "
                + "The character-auditor skill decides keep/delete/replace/uncertain. The prompt-pyramid skill orders the core final prompt. "
                + styleRule
                + "Deletion and replacement are allowed only for hair, eyes, face, body, clothing, footwear, legwear, and wearable_accessory. "
                + "Always keep identity, actions, poses, expressions, scenes, composition, quality, ordinary objects, and other categories. "
                + "Return every original tag exactly once and never modify the tag field. Replacement targets belong only in replacement_tag. "
                + "Output {\"tags\":[{\"tag\":string,\"decision\":\"keep|delete|replace|uncertain\","
                + "\"replacement_tag\":string|null,\"category\":\"identity|hair|eyes|face|body|clothing|footwear|legwear|wearable_accessory|action|pose|expression|scene|composition|quality|object|other\","
                + "\"reason\":string,\"include_in_prompt\":boolean,\"prompt_order\":integer}]}.\n\n"
                + options.CharacterAuditorSkill + "\n\n" + options.PromptPyramidSkill;
        }

        private static string CategoryToWireValue(CharacterTagCategory category)
        {
            return category == CharacterTagCategory.WearableAccessory
                ? "wearable_accessory"
                : category.ToString().ToLowerInvariant();
        }

        private static void ValidateOptions(CharacterTagAuditOptions options)
        {
            if (options == null || options.Inventory == null || options.Inventory.Tags.Count == 0)
                throw new ArgumentException("A non-empty tag inventory is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.TriggerWord))
                throw new ArgumentException("A trigger word is required.", nameof(options));
            if (options.MinimumCount < 1)
                throw new ArgumentOutOfRangeException(nameof(options.MinimumCount));
            if (string.IsNullOrWhiteSpace(options.Model))
                throw new ArgumentException("A model is required.", nameof(options));
            if (string.IsNullOrWhiteSpace(options.ReferenceImagePath) || !File.Exists(options.ReferenceImagePath))
                throw new FileNotFoundException("The reference image was not found.", options.ReferenceImagePath);
            if (string.IsNullOrWhiteSpace(options.CharacterAuditorSkill) || string.IsNullOrWhiteSpace(options.PromptPyramidSkill))
                throw new InvalidOperationException("Both character tag skills are required.");
        }

        private static void EnsurePromptSize(params string[] parts)
        {
            long length = parts.Sum(part => (long)(part?.Length ?? 0));
            if (length > MaximumPromptCharacters)
                throw new InvalidOperationException("Character tag audit request exceeds the 1,000,000 character limit.");
        }
    }
}
