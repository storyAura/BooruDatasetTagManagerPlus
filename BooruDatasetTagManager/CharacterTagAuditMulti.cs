using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    public enum CharacterTagAuditSubjectMode
    {
        Single,
        Dual
    }

    public enum CharacterGender
    {
        Girl,
        Boy
    }

    /// <summary>
    /// One audited character in a (possibly multi-character) dataset: the
    /// locked trigger word, its visual reference, the gender used for subject
    /// count tags, and an optional repeat-folder scope (relative to the
    /// dataset root) used to attribute images that carry no trigger word yet.
    /// </summary>
    public sealed class CharacterAuditProfile
    {
        public string TriggerWord { get; set; } = string.Empty;
        public string ReferenceImagePath { get; set; } = string.Empty;
        public CharacterGender Gender { get; set; } = CharacterGender.Girl;
        public string FolderScope { get; set; } = string.Empty;
    }

    /// <summary>
    /// Attributes an image to the audited characters. Trigger-word presence is
    /// the primary signal; the profile's repeat folder is the fallback for
    /// images that do not carry the trigger yet.
    /// </summary>
    public static class CharacterImageMembership
    {
        public static bool IsMember(
            IEnumerable<string> imageTags,
            string imagePath,
            string datasetRoot,
            CharacterAuditProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            string trigger = profile.TriggerWord?.Trim();
            if (!string.IsNullOrEmpty(trigger)
                && imageTags != null
                && imageTags.Any(tag => string.Equals(tag?.Trim(), trigger, StringComparison.Ordinal)))
            {
                return true;
            }
            return !string.IsNullOrEmpty(profile.FolderScope)
                && DatasetFolderIndex.IsInFolder(imagePath, datasetRoot, profile.FolderScope);
        }

        /// <summary>
        /// Indexes (into <paramref name="profiles"/>) of every profile the
        /// image belongs to, in profile order.
        /// </summary>
        public static IReadOnlyList<int> GetPresentProfiles(
            IEnumerable<string> imageTags,
            string imagePath,
            string datasetRoot,
            IReadOnlyList<CharacterAuditProfile> profiles)
        {
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            List<string> tags = imageTags?.ToList() ?? new List<string>();
            var present = new List<int>();
            for (int i = 0; i < profiles.Count; i++)
            {
                if (IsMember(tags, imagePath, datasetRoot, profiles[i]))
                    present.Add(i);
            }
            return present;
        }
    }

    /// <summary>
    /// A single character's final/reference prompt must describe that
    /// character alone: multi-subject count tags picked up from shared
    /// images (2girls, multiple girls, ...) are replaced by the character's
    /// own 1girl/1boy at the position of the first removed tag. A prompt
    /// without multi-subject tags is returned untouched.
    /// </summary>
    public static class CharacterPromptSubjectNormalizer
    {
        private static readonly HashSet<string> MultiSubjectTags =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "2girls", "3girls", "4girls", "5girls", "6+girls", "multiple girls",
                "2boys", "3boys", "4boys", "5boys", "6+boys", "multiple boys",
                "2others", "3others", "multiple others", "group", "everyone"
            };

        public static string NormalizeToSingle(string prompt, CharacterGender gender)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return prompt ?? string.Empty;
            List<string> tags = prompt.Split(',')
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .ToList();
            int firstRemoved = -1;
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (MultiSubjectTags.Contains(tags[i]))
                {
                    firstRemoved = i;
                    tags.RemoveAt(i);
                }
            }
            if (firstRemoved < 0)
                return prompt;
            string single = gender == CharacterGender.Boy ? "1boy" : "1girl";
            if (!tags.Contains(single, System.StringComparer.OrdinalIgnoreCase))
                tags.Insert(System.Math.Min(firstRemoved, tags.Count), single);
            return string.Join(", ", tags);
        }
    }

    /// <summary>
    /// Deterministic subject-count correction for images that contain both
    /// audited characters: injects the matching count tags (2girls /
    /// multiple girls, 1girl + 1boy, ...) and removes the ones they refute
    /// (solo, stale 1girl). Pure list-in/list-out so it is unit-testable.
    /// </summary>
    public static class CharacterSubjectCountPlanner
    {
        public static IReadOnlyList<string> GetRequiredTags(CharacterGender first, CharacterGender second)
        {
            if (first == CharacterGender.Girl && second == CharacterGender.Girl)
                return new[] { "2girls", "multiple girls" };
            if (first == CharacterGender.Boy && second == CharacterGender.Boy)
                return new[] { "2boys", "multiple boys" };
            return new[] { "1girl", "1boy" };
        }

        public static IReadOnlyList<string> GetConflictingTags(CharacterGender first, CharacterGender second)
        {
            if (first == CharacterGender.Girl && second == CharacterGender.Girl)
                return new[] { "solo", "1girl" };
            if (first == CharacterGender.Boy && second == CharacterGender.Boy)
                return new[] { "solo", "1boy" };
            return new[] { "solo" };
        }

        /// <summary>
        /// Returns a new tag list with the subject-count tags of the two
        /// present profiles enforced. Existing order is preserved; required
        /// tags are inserted right after the last present trigger word (or at
        /// the front when no trigger is present).
        /// </summary>
        public static IReadOnlyList<string> Apply(
            IEnumerable<string> tags,
            CharacterAuditProfile firstPresent,
            CharacterAuditProfile secondPresent)
        {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));
            if (firstPresent == null || secondPresent == null)
                throw new ArgumentNullException(firstPresent == null ? nameof(firstPresent) : nameof(secondPresent));

            var conflicting = new HashSet<string>(
                GetConflictingTags(firstPresent.Gender, secondPresent.Gender), StringComparer.Ordinal);
            List<string> result = tags
                .Where(tag => tag != null && !conflicting.Contains(tag.Trim()))
                .ToList();

            var present = new HashSet<string>(result.Select(tag => tag.Trim()), StringComparer.Ordinal);
            List<string> missing = GetRequiredTags(firstPresent.Gender, secondPresent.Gender)
                .Where(tag => !present.Contains(tag))
                .ToList();
            if (missing.Count == 0)
                return result;

            int insertAt = FindInsertIndex(result, firstPresent.TriggerWord, secondPresent.TriggerWord);
            result.InsertRange(insertAt, missing);
            return result;
        }

        private static int FindInsertIndex(IReadOnlyList<string> tags, string firstTrigger, string secondTrigger)
        {
            int lastTrigger = -1;
            for (int i = 0; i < tags.Count; i++)
            {
                string tag = tags[i]?.Trim();
                if (string.Equals(tag, firstTrigger?.Trim(), StringComparison.Ordinal)
                    || string.Equals(tag, secondTrigger?.Trim(), StringComparison.Ordinal))
                {
                    lastTrigger = i;
                }
            }
            return lastTrigger + 1;
        }
    }

    /// <summary>
    /// Merges the two per-character decision sets for an image that contains
    /// both characters. A tag audited by only one character keeps that
    /// character's decision. When both audited the same tag:
    /// same outcome → apply it once; delete vs non-delete → the non-delete
    /// side wins (the feature exists on that character); any other
    /// disagreement (keep vs replace, replace(x) vs replace(y)) → the
    /// original tag is kept, because on a shared image the generic tag may
    /// legitimately describe both characters at once.
    /// </summary>
    public static class CharacterTagDualDecisionMerger
    {
        public static IReadOnlyDictionary<string, CharacterTagAuditItem> Merge(
            IReadOnlyDictionary<string, CharacterTagAuditItem> first,
            IReadOnlyDictionary<string, CharacterTagAuditItem> second)
        {
            if (first == null)
                throw new ArgumentNullException(nameof(first));
            if (second == null)
                throw new ArgumentNullException(nameof(second));

            var merged = new Dictionary<string, CharacterTagAuditItem>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, CharacterTagAuditItem> pair in first)
                merged[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, CharacterTagAuditItem> pair in second)
            {
                merged[pair.Key] = merged.TryGetValue(pair.Key, out CharacterTagAuditItem existing)
                    ? MergeItem(existing, pair.Value)
                    : pair.Value;
            }
            return merged;
        }

        public static CharacterTagAuditItem MergeItem(CharacterTagAuditItem first, CharacterTagAuditItem second)
        {
            if (first == null)
                throw new ArgumentNullException(nameof(first));
            if (second == null)
                throw new ArgumentNullException(nameof(second));

            bool sameOutcome = first.ShouldDelete == second.ShouldDelete
                && first.ShouldReplace == second.ShouldReplace
                && string.Equals(first.EffectiveTag, second.EffectiveTag, StringComparison.Ordinal);
            if (sameOutcome)
                return first;
            if (first.ShouldDelete && !second.ShouldDelete)
                return second;
            if (second.ShouldDelete && !first.ShouldDelete)
                return first;
            return new CharacterTagAuditItem
            {
                Tag = first.Tag,
                Count = Math.Max(first.Count, second.Count),
                InitialDecision = first.InitialDecision,
                FinalDecision = CharacterTagDecision.Keep,
                Category = first.Category,
                Reason = "Shared image: per-character decisions conflict, original tag kept.",
                ReplacementTag = string.Empty,
                IncludeInPrompt = false,
                PromptOrder = Math.Min(first.PromptOrder, second.PromptOrder)
            };
        }
    }

    /// <summary>Snapshot of one dataset image used by the dual audit.</summary>
    public sealed class CharacterImageTagRecord
    {
        public string ImagePath { get; set; } = string.Empty;
        public IReadOnlyList<string> Tags { get; set; } = System.Array.Empty<string>();
    }

    public sealed class CharacterTagDualAuditOptions
    {
        public IReadOnlyList<CharacterImageTagRecord> Images { get; set; } = System.Array.Empty<CharacterImageTagRecord>();
        public string DatasetRoot { get; set; } = string.Empty;
        public IReadOnlyList<CharacterAuditProfile> Profiles { get; set; } = System.Array.Empty<CharacterAuditProfile>();
        public CharacterTagAuditStyle Style { get; set; } = CharacterTagAuditStyle.Sparse;
        public int MinimumCount { get; set; } = 10;
        public string Model { get; set; } = string.Empty;
        public string CharacterAuditorSkill { get; set; } = string.Empty;
        public string PromptPyramidSkill { get; set; } = string.Empty;
    }

    public sealed class CharacterTagDualAuditResult
    {
        public IReadOnlyList<CharacterTagAuditResult> ProfileResults { get; set; } = System.Array.Empty<CharacterTagAuditResult>();
        public IReadOnlyList<int> MemberImageCounts { get; set; } = System.Array.Empty<int>();
        public int SharedImageCount { get; set; }
        public int UnattributedImageCount { get; set; }
    }

    /// <summary>
    /// Runs the existing two-stage audit once per character profile over that
    /// character's member images and aggregates progress into one 4-step
    /// sequence. Attribution statistics let the caller surface how many images
    /// are shared or not attributed to any character.
    /// </summary>
    public sealed class CharacterTagDualAuditService
    {
        private readonly CharacterTagAuditService inner;

        public CharacterTagDualAuditService(CharacterTagAuditService inner)
        {
            this.inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        }

        public static IReadOnlyList<CharacterImageTagRecord> GetMemberImages(
            CharacterTagDualAuditOptions options,
            int profileIndex)
        {
            if (options == null)
                throw new System.ArgumentNullException(nameof(options));
            CharacterAuditProfile profile = options.Profiles[profileIndex];
            return options.Images
                .Where(image => CharacterImageMembership.IsMember(
                    image.Tags, image.ImagePath, options.DatasetRoot, profile))
                .ToList();
        }

        public static void Validate(CharacterTagDualAuditOptions options)
        {
            if (options == null)
                throw new System.ArgumentNullException(nameof(options));
            if (options.Profiles == null || options.Profiles.Count != 2)
                throw new System.ArgumentException("Dual audit requires exactly two character profiles.", nameof(options));
            string triggerA = options.Profiles[0].TriggerWord?.Trim();
            string triggerB = options.Profiles[1].TriggerWord?.Trim();
            if (string.IsNullOrEmpty(triggerA) || string.IsNullOrEmpty(triggerB))
                throw new System.ArgumentException("Both dual audit profiles need a trigger word.", nameof(options));
            if (string.Equals(triggerA, triggerB, System.StringComparison.Ordinal))
                throw new System.ArgumentException("Dual audit profiles must use two different trigger words.", nameof(options));
            // Both references are checked before the first model call so a
            // missing reference of profile B cannot fail after a paid audit
            // of profile A (the inner service re-validates per run).
            foreach (CharacterAuditProfile profile in options.Profiles)
            {
                if (string.IsNullOrWhiteSpace(profile.ReferenceImagePath)
                    || !System.IO.File.Exists(profile.ReferenceImagePath))
                {
                    throw new System.IO.FileNotFoundException(
                        "The reference image was not found.", profile.ReferenceImagePath);
                }
            }
        }

        public async System.Threading.Tasks.Task<CharacterTagDualAuditResult> ExecuteAsync(
            CharacterTagDualAuditOptions options,
            System.IProgress<CharacterTagAuditProgress> progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            Validate(options);
            // Attribute every profile before the first model call so a typo'd
            // trigger fails fast instead of after a paid audit of profile A.
            var membersByProfile = new List<IReadOnlyList<CharacterImageTagRecord>>();
            for (int i = 0; i < options.Profiles.Count; i++)
            {
                IReadOnlyList<CharacterImageTagRecord> members = GetMemberImages(options, i);
                if (members.Count == 0)
                {
                    throw new System.ArgumentException(
                        "No dataset image matches the trigger word or folder of profile: "
                        + options.Profiles[i].TriggerWord);
                }
                membersByProfile.Add(members);
            }

            var profileResults = new List<CharacterTagAuditResult>();
            var memberCounts = new List<int>();
            for (int i = 0; i < options.Profiles.Count; i++)
            {
                CharacterAuditProfile profile = options.Profiles[i];
                IReadOnlyList<CharacterImageTagRecord> members = membersByProfile[i];
                memberCounts.Add(members.Count);
                var innerOptions = new CharacterTagAuditOptions
                {
                    Inventory = CharacterTagInventory.Create(members.Select(member => member.Tags.AsEnumerable())),
                    TriggerWord = profile.TriggerWord,
                    Style = options.Style,
                    MinimumCount = options.MinimumCount,
                    Model = options.Model,
                    ReferenceImagePath = profile.ReferenceImagePath,
                    CharacterAuditorSkill = options.CharacterAuditorSkill,
                    PromptPyramidSkill = options.PromptPyramidSkill,
                    // Name the other character(s) so shared-image features get
                    // attributed by the reference image, not by frequency.
                    OtherCharacterTriggers = options.Profiles
                        .Where((_, index) => index != i)
                        .Select(other => other.TriggerWord)
                        .ToList()
                };
                OffsetProgress wrappedProgress = progress == null
                    ? null
                    : new OffsetProgress(progress, i, options.Profiles.Count * 2);
                profileResults.Add(await inner.ExecuteAsync(innerOptions, wrappedProgress, cancellationToken)
                    .ConfigureAwait(false));
            }

            // Each profile's final prompt describes ONE character: shared
            // images leak 2girls/multiple girls into the inventory, so the
            // per-character prompt swaps them for the character's own count.
            for (int i = 0; i < profileResults.Count; i++)
            {
                profileResults[i].FinalPrompt = CharacterPromptSubjectNormalizer.NormalizeToSingle(
                    profileResults[i].FinalPrompt, options.Profiles[i].Gender);
            }

            int shared = 0;
            int unattributed = 0;
            foreach (CharacterImageTagRecord image in options.Images)
            {
                int presentCount = CharacterImageMembership.GetPresentProfiles(
                    image.Tags, image.ImagePath, options.DatasetRoot, options.Profiles).Count;
                if (presentCount == 0)
                    unattributed++;
                else if (presentCount > 1)
                    shared++;
            }

            return new CharacterTagDualAuditResult
            {
                ProfileResults = profileResults,
                MemberImageCounts = memberCounts,
                SharedImageCount = shared,
                UnattributedImageCount = unattributed
            };
        }

        // Synchronous pass-through (unlike Progress<T>, which posts to the
        // thread pool and can reorder updates); the caller's own IProgress
        // still handles UI marshaling.
        private sealed class OffsetProgress : System.IProgress<CharacterTagAuditProgress>
        {
            private readonly System.IProgress<CharacterTagAuditProgress> target;
            private readonly int profileIndex;
            private readonly int totalSteps;

            public OffsetProgress(System.IProgress<CharacterTagAuditProgress> target, int profileIndex, int totalSteps)
            {
                this.target = target;
                this.profileIndex = profileIndex;
                this.totalSteps = totalSteps;
            }

            public void Report(CharacterTagAuditProgress update)
            {
                target.Report(new CharacterTagAuditProgress
                {
                    Stage = update.Stage,
                    Items = update.Items,
                    CompletedSteps = profileIndex * 2 + update.CompletedSteps,
                    TotalSteps = totalSteps,
                    ProfileIndex = profileIndex
                });
            }
        }
    }

    /// <summary>
    /// EditableTag counterpart of <see cref="CharacterSubjectCountPlanner"/>:
    /// reorders/injects subject-count tags while reusing the original
    /// EditableTag instances (weights, ids) for tags that survive; only the
    /// injected count tags are created fresh.
    /// </summary>
    public static class CharacterTagEditableTagInjector
    {
        public static IReadOnlyList<EditableTag> ApplySubjectCount(
            IReadOnlyList<EditableTag> tags,
            CharacterAuditProfile firstPresent,
            CharacterAuditProfile secondPresent)
        {
            if (tags == null)
                throw new System.ArgumentNullException(nameof(tags));
            IReadOnlyList<string> desired = CharacterSubjectCountPlanner.Apply(
                tags.Select(tag => tag.Tag).ToList(), firstPresent, secondPresent);
            var remaining = new List<EditableTag>(tags);
            var result = new List<EditableTag>();
            foreach (string tag in desired)
            {
                int index = remaining.FindIndex(candidate =>
                    string.Equals(candidate.Tag, tag, System.StringComparison.Ordinal));
                if (index >= 0)
                {
                    result.Add(remaining[index]);
                    remaining.RemoveAt(index);
                }
                else
                {
                    result.Add(new EditableTag(0, tag));
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Per-image application pipeline for multi-character audits: attribute
    /// the image, pick or merge the per-character decisions, and enforce
    /// subject-count tags on images containing both characters. Images that
    /// belong to no audited character are returned unchanged.
    /// </summary>
    public static class CharacterTagMultiAuditPlan
    {
        public static IReadOnlyDictionary<string, CharacterTagAuditItem> BuildEffectiveDecisions(
            IReadOnlyList<int> presentProfiles,
            IReadOnlyList<IReadOnlyDictionary<string, CharacterTagAuditItem>> decisionsByProfile)
        {
            if (presentProfiles == null)
                throw new ArgumentNullException(nameof(presentProfiles));
            if (decisionsByProfile == null)
                throw new ArgumentNullException(nameof(decisionsByProfile));

            if (presentProfiles.Count == 0)
                return new Dictionary<string, CharacterTagAuditItem>(StringComparer.Ordinal);
            if (presentProfiles.Count == 1)
                return decisionsByProfile[presentProfiles[0]];
            IReadOnlyDictionary<string, CharacterTagAuditItem> merged = decisionsByProfile[presentProfiles[0]];
            for (int i = 1; i < presentProfiles.Count; i++)
                merged = CharacterTagDualDecisionMerger.Merge(merged, decisionsByProfile[presentProfiles[i]]);
            return merged;
        }

        public static IReadOnlyList<string> TransformImageTags(
            IReadOnlyList<string> originalTags,
            string imagePath,
            string datasetRoot,
            IReadOnlyList<CharacterAuditProfile> profiles,
            IReadOnlyList<IReadOnlyDictionary<string, CharacterTagAuditItem>> decisionsByProfile)
        {
            if (originalTags == null)
                throw new ArgumentNullException(nameof(originalTags));
            if (profiles == null)
                throw new ArgumentNullException(nameof(profiles));
            if (decisionsByProfile == null || decisionsByProfile.Count != profiles.Count)
                throw new ArgumentException("One decision set per profile is required.", nameof(decisionsByProfile));

            IReadOnlyList<int> present = CharacterImageMembership.GetPresentProfiles(
                originalTags, imagePath, datasetRoot, profiles);
            if (present.Count == 0)
                return originalTags.ToList();

            IReadOnlyDictionary<string, CharacterTagAuditItem> effective =
                BuildEffectiveDecisions(present, decisionsByProfile);
            IReadOnlyList<string> transformed = CharacterTagTransformation.Apply(originalTags, effective.Values);
            if (present.Count == 2)
            {
                transformed = CharacterSubjectCountPlanner.Apply(
                    transformed, profiles[present[0]], profiles[present[1]]);
            }
            return transformed;
        }
    }
}
