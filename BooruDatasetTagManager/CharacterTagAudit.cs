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
        Repair,
        // Targeted follow-up after visual review: colors of color-less
        // wearables + same-slot cluster verdicts, no skills attached.
        Resolution
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

        // Generic multi-color hair terms are structure descriptions, not colors.
        // They must never be the replacement TARGET of a non-generic tag:
        // "white hair -> colored hair" silently drops the concrete color from
        // the prompt. They stay legal as pre-existing tags (a genuinely
        // multi-colored character keeps "multicolored hair" beside its colors)
        // and may normalize among themselves ("streaked hair -> multicolored hair").
        private static readonly HashSet<string> GenericHairColorTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "colored hair", "multicolored hair", "two-tone hair", "two tone hair",
            "gradient hair", "streaked hair", "split-color hair", "split color hair",
            "colored inner hair", "rainbow hair"
        };

        public static bool IsGenericHairColorTag(string tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && GenericHairColorTags.Contains(tag.Trim());
        }

        public static bool IsForbiddenGenericHairReplacement(string sourceTag, string replacementTag)
        {
            return IsGenericHairColorTag(replacementTag) && !IsGenericHairColorTag(sourceTag);
        }

        /// <summary>
        /// Maps a tag to a <see cref="CharacterTagCategory"/> using the local
        /// semantic classifier so AI-supplied categories cannot silently promote
        /// a protected tag (e.g. sitting → clothing) into a deletable bucket.
        /// </summary>
        public static CharacterTagCategory ClassifyLocally(string tag, int danbooruType = -1)
        {
            return TagSemanticClassifier.Classify(tag, danbooruType) switch
            {
                TagSemanticCategory.Character or TagSemanticCategory.Copyright
                    or TagSemanticCategory.Artist or TagSemanticCategory.SubjectCount
                    => CharacterTagCategory.Identity,
                TagSemanticCategory.Hair => CharacterTagCategory.Hair,
                TagSemanticCategory.Eyes => CharacterTagCategory.Eyes,
                TagSemanticCategory.Body => CharacterTagCategory.Body,
                TagSemanticCategory.Expression => CharacterTagCategory.Expression,
                TagSemanticCategory.Clothing => CharacterTagCategory.Clothing,
                TagSemanticCategory.Accessory => CharacterTagCategory.WearableAccessory,
                TagSemanticCategory.Cosplay => CharacterTagCategory.Other,
                TagSemanticCategory.Object or TagSemanticCategory.Animal
                    or TagSemanticCategory.Food => CharacterTagCategory.Object,
                TagSemanticCategory.Action => CharacterTagCategory.Action,
                TagSemanticCategory.Composition => CharacterTagCategory.Composition,
                TagSemanticCategory.Background => CharacterTagCategory.Scene,
                TagSemanticCategory.Meta or TagSemanticCategory.Style => CharacterTagCategory.Quality,
                _ => CharacterTagCategory.Other
            };
        }

        /// <summary>
        /// Prefer the local classification when it is protected, so a model that
        /// mislabels a protected tag as deletable cannot bypass <see cref="CanDelete"/>.
        /// </summary>
        public static CharacterTagCategory ResolveCategoryForPolicy(
            string tag, CharacterTagCategory modelCategory, int danbooruType = -1)
        {
            CharacterTagCategory local = ClassifyLocally(tag, danbooruType);
            if (!CanDelete(local))
                return local;
            return modelCategory;
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

        // Category policy for AI decisions only: the parser uses it to sanitize
        // model output. It no longer gates ShouldDelete/ShouldReplace, which
        // honor deliberate user overrides made in the review grid.
        [JsonIgnore]
        public bool CanDelete => CharacterTagAuditPolicy.CanDelete(Category);

        // The parser forces AI decisions on protected categories back to Keep,
        // so a Delete/Replace on such a category can only be a deliberate user
        // override made in the review grid. It must be applied, not ignored.
        [JsonIgnore]
        public bool ShouldDelete => FinalDecision == CharacterTagDecision.Delete;

        [JsonIgnore]
        public bool ShouldReplace => FinalDecision == CharacterTagDecision.Replace
            && !string.IsNullOrWhiteSpace(ReplacementTag);

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
        // Which audited character this update belongs to (dual mode runs the
        // two-stage pipeline once per profile; single mode always reports 0).
        public int ProfileIndex { get; set; }
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
        // Dual/multi audits: trigger words of the OTHER characters sharing
        // images with the audited one, so the prompts can pin attribution.
        public IReadOnlyList<string> OtherCharacterTriggers { get; set; } = Array.Empty<string>();
        // Danbooru implication vocabulary (Data/danbooru_dataset_general.csv)
        // that drives the deterministic wearable-family collapse.
        public GeneralTagCategoryCatalog TagVocabulary { get; set; } = GeneralTagCategoryCatalog.Empty;
        // Danbooru related-tag graph (Data/danbooru_tag_near_synonyms.csv):
        // groups same-slot tags into clusters the visual stages must resolve.
        public TagNearSynonymIndex NearSynonyms { get; set; } = TagNearSynonymIndex.Empty;
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
        public static readonly HashSet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
        {
            "black", "blue", "brown", "green", "grey", "gray", "orange", "pink", "purple", "red", "white", "yellow",
            "multicolored"
        };

        // Danbooru implications the general CSV does not carry (hair_ribbon
        // only lists ribbon, hairband has no parent): every specific hair
        // accessory implies the hair ornament container.
        private static readonly Dictionary<string, string[]> BuiltInImplications =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["hair ribbon"] = new[] { "hair ornament", "ribbon" },
                ["hair bow"] = new[] { "hair ornament", "bow" },
                ["hairband"] = new[] { "hair ornament" },
                ["hair flower"] = new[] { "hair ornament" },
                ["hairclip"] = new[] { "hair ornament" },
                ["hairpin"] = new[] { "hair ornament" },
                ["hair scrunchie"] = new[] { "hair ornament" },
                ["hair bell"] = new[] { "hair ornament" },
                ["hair bobbles"] = new[] { "hair ornament" },
                ["hair stick"] = new[] { "hair ornament" },
                ["hair tubes"] = new[] { "hair ornament" }
            };

        private static readonly HashSet<string> HairOrnamentAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "hair ornament", "hair accessory"
        };

        /// <summary>
        /// True when <paramref name="generic"/> is a container of
        /// <paramref name="specific"/>: literal word specialization, a vocabulary
        /// implication chain, or a built-in hair accessory implication. The
        /// built-in table also matches through a colored form
        /// (<c>black hair ribbon</c> → <c>hair ornament</c>).
        /// </summary>
        public static bool IsImplied(string generic, string specific, GeneralTagCategoryCatalog vocabulary)
        {
            if (string.IsNullOrWhiteSpace(generic) || string.IsNullOrWhiteSpace(specific)
                || string.Equals(generic, specific, StringComparison.Ordinal))
            {
                return false;
            }
            if (IsWordSpecialization(generic, specific))
                return true;
            if (vocabulary != null && vocabulary.IsAncestor(generic, specific))
                return true;
            string strippedSpecific = StripColorWords(specific);
            if (strippedSpecific.Length > 0
                && !string.Equals(strippedSpecific, specific, StringComparison.Ordinal)
                && vocabulary != null
                && vocabulary.IsAncestor(generic, strippedSpecific))
            {
                return true;
            }

            string target = HairOrnamentAliases.Contains(generic) ? "hair ornament" : generic;
            foreach (KeyValuePair<string, string[]> pair in BuiltInImplications)
            {
                if (!pair.Value.Contains(target, StringComparer.Ordinal))
                    continue;
                if (string.Equals(specific, pair.Key, StringComparison.Ordinal)
                    || IsWordSpecialization(pair.Key, specific))
                {
                    return true;
                }
            }
            return false;
        }

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

        private static readonly CharacterTagCategory[] WearableCategories =
        {
            CharacterTagCategory.Clothing,
            CharacterTagCategory.Footwear,
            CharacterTagCategory.Legwear,
            CharacterTagCategory.WearableAccessory
        };

        public static void Apply(IEnumerable<CharacterTagAuditItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<CharacterTagAuditItem> list = items.ToList();

            ReviveDeletedContainers(list, GeneralTagCategoryCatalog.Empty);
            ApplyBaseRules(list);
            ApplyFamilyRules(list, GeneralTagCategoryCatalog.Empty);
        }

        public static void Apply(IEnumerable<CharacterTagAuditItem> items, CharacterTagAuditStyle style)
        {
            Apply(items, style, GeneralTagCategoryCatalog.Empty);
        }

        public static void Apply(
            IEnumerable<CharacterTagAuditItem> items,
            CharacterTagAuditStyle style,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            List<CharacterTagAuditItem> list = items.ToList();
            GeneralTagCategoryCatalog vocab = vocabulary ?? GeneralTagCategoryCatalog.Empty;

            ReviveDeletedContainers(list, vocab);
            ApplyBaseRules(list);
            ApplyFamilyRules(list, vocab);
            if (style == CharacterTagAuditStyle.Sparse)
                ApplySparseRules(list);
        }

        /// <summary>
        /// Wearable-family collapse driven by danbooru implications
        /// (<paramref name="vocabulary"/> parent chains) plus literal suffixes:
        /// first normalize invented replacement targets and fold unknown
        /// composites back onto known precise tags, then merge one color
        /// sibling with one type sibling of the same base (<c>black gloves</c>
        /// + <c>elbow gloves</c>), then fold every container tag into its
        /// confirmed specific member (<c>jewelry</c> → <c>earrings</c>). Only
        /// the locked character's verified wearable tags take part; hair,
        /// eyes, protected categories and other-person evidence are never
        /// touched. An empty vocabulary keeps the pre-1.2.7 behaviour.
        /// </summary>
        private static void ApplyFamilyRules(List<CharacterTagAuditItem> list, GeneralTagCategoryCatalog vocabulary)
        {
            NormalizeReplacementTargets(list, vocabulary);
            FoldUnknownCompositesIntoKnownColoredTags(list, vocabulary);
            ApplySiblingMerge(list, vocabulary);
            ApplyHypernymCollapse(list, vocabulary);
            DeleteBareDecorationWords(list);
        }

        private static readonly HashSet<string> BareDecorationTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "frills", "ruffles", "pleated", "lace trim", "lace"
        };

        // Pattern prefixes name a rejected print, not a type sibling of a
        // colored garment (plaid bikini is not "the same item as pink bikini").
        private static readonly HashSet<string> PatternModifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "plaid", "checkered", "gingham", "argyle", "floral", "polka", "print",
            "camouflage", "camo", "leopard", "zebra", "striped", "polka-dot"
        };

        /// <summary>
        /// A model Delete on a container or type word only strips that word
        /// from images that never received the precise tag. When exactly one
        /// confirmed specific wearable remains, turn the Delete into a
        /// Replace so those images inherit it. Runs before
        /// <see cref="ApplyBaseRules"/> so later deterministic Deletes
        /// (swimsuit, sparse bangs) are left alone.
        /// </summary>
        private static void ReviveDeletedContainers(
            List<CharacterTagAuditItem> list,
            GeneralTagCategoryCatalog vocabulary)
        {
            GeneralTagCategoryCatalog vocab = vocabulary ?? GeneralTagCategoryCatalog.Empty;
            List<CharacterTagAuditItem> verified = VerifiedWearables(list);
            if (verified.Count == 0)
                return;

            foreach (CharacterTagAuditItem deleted in list
                .Where(item => WearableCategories.Contains(item.Category)
                    && item.FinalDecision == CharacterTagDecision.Delete)
                .ToList())
            {
                List<(string Tag, CharacterTagAuditItem Item)> targets = verified
                    .Select(item => (Tag: item.EffectiveTag?.Trim(), Item: item))
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Tag)
                        && IsReviveTarget(deleted, pair.Tag, vocab))
                    .GroupBy(pair => pair.Tag, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(pair => pair.Tag, StringComparer.Ordinal)
                    .ToList();
                if (targets.Count != 1)
                    continue;
                (string targetTag, CharacterTagAuditItem source) = targets[0];
                RedirectItem(deleted, targetTag, source.PromptOrder,
                    "Deleted generic tag redirected to its confirmed specific tag so images carrying only the generic tag receive it.");
                deleted.IncludeInPrompt = source.IncludeInPrompt;
            }
        }

        private static bool IsReviveTarget(
            CharacterTagAuditItem deleted,
            string specificTag,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (IsImplied(deleted.Tag, specificTag, vocabulary) && IsCanonicalForm(specificTag, vocabulary))
                return true;
            if (ContainsColorWord(deleted.Tag)
                || deleted.Tag.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(PatternModifiers.Contains))
            {
                return false;
            }
            var deletedBases = new HashSet<string>(BasesOf(deleted.Tag, vocabulary), StringComparer.Ordinal);
            foreach (string baseTag in BasesOf(specificTag, vocabulary))
            {
                if (!deletedBases.Contains(baseTag))
                    continue;
                if (IsColoredGarment(specificTag, baseTag)
                    && deleted.Tag.EndsWith(" " + baseTag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Bare decoration words are not a feature slot. When a confirmed
        /// garment already exists they are deleted in both styles; a lone
        /// <c>frills</c> with no clothing anchor is left for the model.
        /// </summary>
        private static void DeleteBareDecorationWords(List<CharacterTagAuditItem> list)
        {
            CharacterTagAuditItem garment = list
                .Where(item => item.Category == CharacterTagCategory.Clothing
                    && IsVerified(item, CharacterTagCategory.Clothing)
                    && !BareDecorationTags.Contains(item.EffectiveTag?.Trim() ?? string.Empty))
                .OrderBy(item => item.PromptOrder)
                .ThenBy(item => item.EffectiveTag, StringComparer.Ordinal)
                .FirstOrDefault();
            if (garment == null)
                return;

            foreach (CharacterTagAuditItem item in VerifiedWearables(list))
            {
                string tag = item.EffectiveTag?.Trim();
                if (string.IsNullOrEmpty(tag) || !BareDecorationTags.Contains(tag))
                    continue;
                DeleteItem(item, "Decoration word of " + garment.EffectiveTag + "; never a standalone prompt tag.");
            }
        }

        /// <summary>
        /// True when <paramref name="tag"/> is a real vocabulary entry or a
        /// color-prefixed real entry (<c>black hair ribbon</c>,
        /// <c>blue earrings</c>). An empty vocabulary accepts every tag so
        /// existing no-catalog callers keep their previous behaviour.
        /// <c>frilled black dress</c> also passes when <c>frilled dress</c> is
        /// known — conflict with a more precise colored sibling is handled by
        /// <see cref="FoldUnknownCompositesIntoKnownColoredTags"/>.
        /// </summary>
        public static bool IsCanonicalForm(string tag, GeneralTagCategoryCatalog vocabulary)
        {
            if (vocabulary == null || vocabulary.Count == 0)
                return true;
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            if (vocabulary.Contains(tag))
                return true;
            string stripped = StripColorWords(tag);
            return stripped.Length > 0 && vocabulary.Contains(stripped);
        }

        public static string StripColorWords(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return string.Empty;
            return string.Join(" ", tag.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !Colors.Contains(word)));
        }

        public static bool ContainsColorWord(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;
            return tag.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(Colors.Contains);
        }

        private static bool IsKnownPreciseTag(string tag, GeneralTagCategoryCatalog vocabulary)
        {
            if (vocabulary.Contains(tag))
                return true;
            int space = tag.IndexOf(' ');
            if (space <= 0)
                return false;
            string color = tag.Substring(0, space);
            string rest = tag.Substring(space + 1).Trim();
            return Colors.Contains(color) && rest.Length > 0 && vocabulary.Contains(rest);
        }

        private static void NormalizeReplacementTargets(
            List<CharacterTagAuditItem> list,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (vocabulary == null || vocabulary.Count == 0)
                return;
            foreach (CharacterTagAuditItem item in list)
            {
                if (item.FinalDecision != CharacterTagDecision.Replace)
                    continue;
                string target = item.ReplacementTag?.Trim();
                if (string.IsNullOrEmpty(target) || IsKnownPreciseTag(target, vocabulary))
                    continue;
                string known = LongestKnownSubTag(target, vocabulary);
                if (string.IsNullOrEmpty(known) || string.Equals(known, target, StringComparison.Ordinal))
                    continue;
                RedirectItem(item, known, item.PromptOrder,
                    "Replacement target is not a known tag; normalized to " + known + ".");
            }
        }

        private static string LongestKnownSubTag(string tag, GeneralTagCategoryCatalog vocabulary)
        {
            string[] words = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string best = null;
            int bestLength = 0;
            bool bestHasColor = false;
            for (int start = 0; start < words.Length; start++)
            {
                for (int end = start; end < words.Length; end++)
                {
                    int length = end - start + 1;
                    string candidate = string.Join(" ", words.Skip(start).Take(length));
                    if (!vocabulary.Contains(candidate))
                        continue;
                    bool hasColor = ContainsColorWord(candidate);
                    if (length > bestLength || (length == bestLength && hasColor && !bestHasColor))
                    {
                        best = candidate;
                        bestLength = length;
                        bestHasColor = hasColor;
                    }
                }
            }
            return best;
        }

        private static void FoldUnknownCompositesIntoKnownColoredTags(
            List<CharacterTagAuditItem> list,
            GeneralTagCategoryCatalog vocabulary)
        {
            if (vocabulary == null || vocabulary.Count == 0)
                return;
            List<(CharacterTagAuditItem Item, string Effective)> snapshot = VerifiedWearables(list)
                .Select(item => (Item: item, Effective: item.EffectiveTag?.Trim()))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Effective))
                .ToList();
            List<(CharacterTagAuditItem Item, string Effective)> knownColored = snapshot
                .Where(pair => vocabulary.Contains(pair.Effective) && ContainsColorWord(pair.Effective))
                .ToList();
            if (knownColored.Count == 0)
                return;

            foreach ((CharacterTagAuditItem item, string composite) in snapshot)
            {
                if (vocabulary.Contains(composite))
                    continue;
                string best = knownColored
                    .Where(pair => !ReferenceEquals(pair.Item, item)
                        && IsWordSpecialization(pair.Effective, composite))
                    .Select(pair => pair.Effective)
                    .OrderByDescending(tag => tag.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length)
                    .ThenBy(tag => tag, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (best == null)
                    continue;
                RedirectItem(item, best, item.PromptOrder,
                    "Unknown composite folded back to the known precise tag.");
            }
        }

        private static void ApplySiblingMerge(List<CharacterTagAuditItem> list, GeneralTagCategoryCatalog vocabulary)
        {
            List<CharacterTagAuditItem> wearables = VerifiedWearables(list);
            var groups = new Dictionary<string, List<CharacterTagAuditItem>>(StringComparer.Ordinal);
            foreach (CharacterTagAuditItem item in wearables)
            {
                foreach (string baseTag in BasesOf(item.EffectiveTag, vocabulary))
                {
                    if (!groups.TryGetValue(baseTag, out List<CharacterTagAuditItem> members))
                    {
                        members = new List<CharacterTagAuditItem>();
                        groups[baseTag] = members;
                    }
                    if (!members.Contains(item))
                        members.Add(item);
                }
            }

            foreach (KeyValuePair<string, List<CharacterTagAuditItem>> group in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string baseTag = group.Key;
                string suffix = " " + baseTag;
                List<CharacterTagAuditItem> colorSiblings = group.Value
                    .Where(item => IsColoredGarment(item.EffectiveTag, baseTag))
                    .ToList();
                List<CharacterTagAuditItem> typeSiblings = group.Value
                    .Where(item => item.EffectiveTag.EndsWith(suffix, StringComparison.Ordinal)
                        && !IsColoredGarment(item.EffectiveTag, baseTag)
                        && !ContainsColorWord(item.EffectiveTag))
                    .ToList();
                if (colorSiblings.Count != 1 || typeSiblings.Count != 1)
                    continue;

                CharacterTagAuditItem color = colorSiblings[0];
                CharacterTagAuditItem type = typeSiblings[0];
                if (!IsVerifiedWearable(color) || !IsVerifiedWearable(type))
                    continue;
                string colorWord = color.EffectiveTag.Substring(0, color.EffectiveTag.Length - suffix.Length);
                string composed = colorWord + " " + type.EffectiveTag;
                int order = Math.Min(color.PromptOrder, type.PromptOrder);
                if (vocabulary.Contains(composed))
                {
                    RedirectItem(color, composed, order,
                        "Merged color and type of the same item into one canonical tag.");
                    RedirectItem(type, composed, order,
                        "Merged color and type of the same item into one canonical tag.");
                }
                else
                {
                    color.PromptOrder = order;
                    RedirectItem(type, color.EffectiveTag, order,
                        "Same item as the colored tag; the combined form is not a known tag, so the color tag is kept.");
                }
            }
        }

        private static void ApplyHypernymCollapse(List<CharacterTagAuditItem> list, GeneralTagCategoryCatalog vocabulary)
        {
            List<CharacterTagAuditItem> wearables = VerifiedWearables(list);
            var snapshot = wearables
                .Select(item => (Item: item, Effective: item.EffectiveTag))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Effective))
                .ToList();
            var changed = new HashSet<CharacterTagAuditItem>();

            foreach ((CharacterTagAuditItem generic, string genericTag) in snapshot)
            {
                if (changed.Contains(generic))
                    continue;
                List<string> specifics = snapshot
                    .Where(pair => !ReferenceEquals(pair.Item, generic)
                        && !changed.Contains(pair.Item)
                        && IsImplied(genericTag, pair.Effective, vocabulary)
                        && IsCanonicalForm(pair.Effective, vocabulary))
                    .Select(pair => pair.Effective)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(tag => tag, StringComparer.Ordinal)
                    .ToList();
                if (ContainsColorWord(genericTag)
                    && vocabulary != null
                    && vocabulary.Count > 0
                    && vocabulary.Contains(genericTag))
                {
                    specifics = specifics
                        .Where(vocabulary.Contains)
                        .ToList();
                }
                if (specifics.Count == 0)
                    continue;

                changed.Add(generic);
                if (specifics.Count == 1)
                {
                    CharacterTagAuditItem target = snapshot
                        .Select(pair => pair.Item)
                        .FirstOrDefault(item => string.Equals(item.EffectiveTag, specifics[0], StringComparison.Ordinal));
                    RedirectItem(generic, specifics[0], target?.PromptOrder ?? generic.PromptOrder,
                        "Generic category folded into its confirmed specific tag.");
                }
                else
                {
                    DeleteItem(generic, "Generic category covered by: " + string.Join(", ", specifics) + ".");
                }
            }
        }

        private static IEnumerable<string> BasesOf(string tag, GeneralTagCategoryCatalog vocabulary)
        {
            if (string.IsNullOrWhiteSpace(tag))
                yield break;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string parent in vocabulary.GetParents(tag))
            {
                if (tag.EndsWith(" " + parent, StringComparison.Ordinal) && seen.Add(parent))
                    yield return parent;
            }
            int lastSpace = tag.LastIndexOf(' ');
            if (lastSpace > 0 && lastSpace < tag.Length - 1)
            {
                string lastWord = tag.Substring(lastSpace + 1);
                if (seen.Add(lastWord))
                    yield return lastWord;
            }
        }

        /// <summary>
        /// <paramref name="specific"/> names the same item as
        /// <paramref name="generic"/> with extra words: it ends with the same
        /// head noun and contains every word of the generic tag
        /// (<c>black gloves</c> → <c>black elbow gloves</c>, <c>skirt</c> →
        /// <c>black skirt</c>). <c>blue dress</c> is not a specialization of
        /// <c>black dress</c>.
        /// </summary>
        public static bool IsWordSpecialization(string generic, string specific)
        {
            if (string.IsNullOrWhiteSpace(generic) || string.IsNullOrWhiteSpace(specific))
                return false;
            string[] genericWords = generic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] specificWords = specific.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (genericWords.Length == 0 || specificWords.Length <= genericWords.Length)
                return false;
            if (!string.Equals(genericWords[^1], specificWords[^1], StringComparison.Ordinal))
                return false;
            var pool = new HashSet<string>(specificWords, StringComparer.Ordinal);
            return genericWords.All(pool.Contains);
        }

        private static List<CharacterTagAuditItem> VerifiedWearables(IEnumerable<CharacterTagAuditItem> items)
        {
            return items.Where(IsVerifiedWearable).ToList();
        }

        private static bool IsVerifiedWearable(CharacterTagAuditItem item)
        {
            return WearableCategories.Contains(item.Category) && IsVerified(item, item.Category);
        }

        public static bool IsVerifiedWearableItem(CharacterTagAuditItem item)
        {
            return item != null && IsVerifiedWearable(item);
        }

        public static bool IsVerifiedHairOrWearable(CharacterTagAuditItem item)
        {
            return item != null
                && (IsVerifiedWearable(item)
                    || (item.Category == CharacterTagCategory.Hair && IsVerified(item, CharacterTagCategory.Hair)));
        }

        public static void Redirect(CharacterTagAuditItem item, string targetTag, int promptOrder, string reason)
        {
            if (item == null || string.IsNullOrWhiteSpace(targetTag))
                return;
            RedirectItem(item, targetTag.Trim(), promptOrder, reason);
        }

        private static void RedirectItem(CharacterTagAuditItem item, string targetTag, int promptOrder, string reason)
        {
            if (string.Equals(item.Tag, targetTag, StringComparison.Ordinal))
            {
                item.FinalDecision = CharacterTagDecision.Keep;
                item.ReplacementTag = string.Empty;
            }
            else
            {
                item.FinalDecision = CharacterTagDecision.Replace;
                item.ReplacementTag = targetTag;
            }
            item.PromptOrder = promptOrder;
            item.Reason = reason;
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
                // Two distinct colors (black dress + blue dress): no single
                // target to pick — the family rules delete the generic instead.
                int distinctColors = list
                    .Where(item => IsVerified(item, category) && IsColoredGarment(item.EffectiveTag, garment))
                    .Select(item => item.EffectiveTag)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                if (distinctColors > 1)
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
                && !string.Equals(item.Tag, targetTag, StringComparison.Ordinal)
                && !string.Equals(item.EffectiveTag, targetTag, StringComparison.Ordinal)))
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
                if (CharacterTagResultCanonicalizer.IsWordSpecialization(tag, other))
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
                    CharacterTagCategory modelCategory = ParseCategory(token.Value<string>("category"));
                    // Never trust the model alone to mark a tag deletable: derive a
                    // local category and keep the protected one when they disagree.
                    CharacterTagCategory category = CharacterTagAuditPolicy.ResolveCategoryForPolicy(
                        tag, modelCategory);
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
                    // "white hair -> colored hair" style answers drop the
                    // concrete hair color; force them back to Keep.
                    if (decision == CharacterTagDecision.Replace
                        && CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement(tag, replacementTag))
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
            // Models often wrap the JSON in prose ("Here is the JSON: {...}
            // Hope this helps!"). Cut to the outermost object.
            int start = cleaned.IndexOf('{');
            int end = cleaned.LastIndexOf('}');
            if (start >= 0 && end > start)
                cleaned = cleaned.Substring(start, end - start + 1).Trim();
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
                + BuildOtherCharactersHint(options)
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
            string visualPrompt = BuildVisualPrompt(initial, options);
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
            await RunResolutionAsync(options, final, metrics, cancellationToken).ConfigureAwait(false);
            CharacterTagResultCanonicalizer.Apply(final, options.Style, options.TagVocabulary);
            progress?.Report(new CharacterTagAuditProgress
            {
                Stage = CharacterTagAuditStage.VisualReview,
                Items = final,
                CompletedSteps = totalSteps,
                TotalSteps = totalSteps
            });
            return final;
        }

        /// <summary>
        /// The visual stage keeps leaving wearables color-less and same-slot
        /// tags side by side (bow + hair ribbon + hairband). This extra
        /// request asks only those questions with the reference attached and
        /// no skills, then applies answers that stay inside the requested
        /// tags. It is best-effort: any failure keeps the visual result.
        /// </summary>
        private async Task RunResolutionAsync(
            CharacterTagAuditOptions options,
            IReadOnlyList<CharacterTagAuditItem> final,
            CharacterTagAuditMetrics metrics,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<string> colorless = CharacterTagResolution.CollectColorlessWearables(final);
            IReadOnlyList<CharacterTagCluster> clusters = CharacterTagResolution.BuildClusters(
                final, options.NearSynonyms, options.TagVocabulary);
            if (!CharacterTagResolution.NeedsResolution(colorless, clusters))
                return;

            var request = new CharacterTagModelRequest
            {
                Stage = CharacterTagAuditStage.Resolution,
                Model = options.Model,
                SystemPrompt = CharacterTagResolution.SystemPrompt,
                UserPrompt = CharacterTagResolution.BuildUserPrompt(options.TriggerWord, colorless, clusters)
            };
            request.ImagePaths.Add(options.ReferenceImagePath);
            try
            {
                CharacterTagModelResponse response = await RequestWithMetricsAsync(request, metrics, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
                    return;
                CharacterTagResolution.Apply(
                    final.ToList(), response.Result, colorless, clusters, options.TagVocabulary);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("CharacterTagAudit resolution pass skipped: " + ex.Message);
            }
        }

        /// <summary>
        /// Dual/multi audits: the shared-image inventory mixes both
        /// characters' features, and without this hint the model tends to
        /// assign traits by frequency (character B inheriting A's hair).
        /// </summary>
        private static string BuildOtherCharactersHint(CharacterTagAuditOptions options)
        {
            if (options.OtherCharacterTriggers == null || options.OtherCharacterTriggers.Count == 0)
                return string.Empty;
            return "Other characters also appear in some of these images: "
                + string.Join(", ", options.OtherCharacterTriggers)
                + ". Tags describing THOSE characters (their hair length/color, eye color, garments, accessories) "
                + "must be decision=keep with include_in_prompt=false and a reason naming that character. "
                + "Attribute every appearance tag strictly to the correct character; never assume a tag belongs "
                + "to the locked character just because it is frequent.\n";
        }

        private static string BuildVisualPrompt(IReadOnlyList<CharacterTagAuditItem> initial, CharacterTagAuditOptions options)
        {
            IReadOnlyList<string> colorless = CharacterTagResolution.CollectColorlessWearables(initial);
            IReadOnlyList<CharacterTagCluster> clusters = CharacterTagResolution.BuildClusters(
                initial, options.NearSynonyms, options.TagVocabulary);
            string todo = string.Empty;
            if (colorless.Count > 0)
            {
                todo += "Color-less wearable tags you MUST resolve now: " + string.Join(", ", colorless)
                    + ". For each: replace -> \"<color> <tag>\" when the reference shows the color, otherwise keep with a "
                    + "reason starting \"color unverifiable:\".\n";
            }
            if (clusters.Count > 0)
            {
                todo += "Same-slot clusters in this inventory (each names related tags that may describe ONE item): "
                    + CharacterTagResolution.FormatClusters(clusters)
                    + ". For each cluster keep only the tags that are visibly distinct items on the reference; map every "
                    + "other member to the single best tag (color + type when confirmed).\n";
            }
            return BuildOtherCharactersHint(options)
                + (options.OtherCharacterTriggers != null && options.OtherCharacterTriggers.Count > 0
                    ? "The attached reference image shows ONLY the locked character ("
                        + options.TriggerWord.Trim()
                        + "); use it as the sole authority for which features are theirs.\n"
                    : string.Empty)
                + "Review the preliminary tag decisions against the attached reference image. "
                + "Return the same complete strict JSON schema. Replacement targets may be new normalized tags, "
                + "but every original tag must still appear exactly once.\n"
                + "Explicitly list and re-check every color-less garment, footwear, legwear, and wearable accessory tag "
                + "(for example jacket, boots, shirt, skirt, hair ribbon). When the reference clearly shows its color on "
                + "the locked character, use replace with the color-prefixed tag (for example jacket -> black jacket) even "
                + "if that colored tag does not exist anywhere in the inventory. Keep the color-less tag only when the "
                + "color is genuinely unverifiable, and explain why in reason. Never answer replace with an empty "
                + "replacement_tag.\n"
                + todo
                + "Each reason must cite what you see in the reference (at least 8 words); stock phrases such as "
                + "\"core tag\" or \"required tag\" are invalid.\nPreliminary: "
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

        // Text screening + visual review + the optional resolution pass.
        // Repair retries are excluded from this user-facing worst case.
        public const int MaxRequestsPerProfile = 3;

        // A malformed model answer goes through one Repair request; if that
        // still fails to validate, the whole [original → repair] pair is
        // retried once from scratch (models are nondeterministic — a fresh
        // sample usually parses) before the failure reaches the UI.
        private const int MaxValidationAttempts = 2;

        private async Task<IReadOnlyList<CharacterTagAuditItem>> RequestValidatedAsync(
            CharacterTagModelRequest request,
            CharacterTagInventory inventory,
            string triggerWord,
            CharacterTagAuditMetrics metrics,
            CancellationToken cancellationToken)
        {
            CharacterTagAuditResponseException lastFailure = null;
            for (int attempt = 0; attempt < MaxValidationAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    try
                    {
                        CharacterTagModelResponse repaired = await RequestWithMetricsAsync(repair, metrics, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(repaired.ErrorMessage))
                            throw new CharacterTagAuditResponseException(repaired.ErrorMessage, original);
                        return CharacterTagAuditResponseParser.ParseAndValidate(repaired.Result, inventory, triggerWord);
                    }
                    catch (CharacterTagAuditResponseException repairFailure)
                    {
                        lastFailure = repairFailure;
                    }
                }
            }
            throw lastFailure ?? new CharacterTagAuditResponseException("The model response is not valid character tag JSON.");
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
