using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    public enum CharacterTagAuditSubjectMode
    {
        Single,
        Dual,
        // Up to four character slots; slots left without a trigger word are
        // skipped, so this mode also covers three-character datasets.
        Quad
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
            string single = gender == CharacterGender.Boy ? "1boy" : "1girl";
            // A mixed cast injects 1girl + 1boy on shared images; the other
            // gender's singular count must not survive into a prompt that
            // describes this character alone.
            string otherSingle = gender == CharacterGender.Boy ? "1girl" : "1boy";
            int firstRemoved = -1;
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (MultiSubjectTags.Contains(tags[i])
                    || string.Equals(tags[i], otherSingle, StringComparison.OrdinalIgnoreCase))
                {
                    firstRemoved = i;
                    tags.RemoveAt(i);
                }
            }
            if (firstRemoved < 0)
                return prompt;
            if (!tags.Contains(single, System.StringComparer.OrdinalIgnoreCase))
                tags.Insert(System.Math.Min(firstRemoved, tags.Count), single);
            return string.Join(", ", tags);
        }
    }

    /// <summary>
    /// Deterministic subject-count correction for images that contain several
    /// audited characters: injects the matching count tags (2girls / 3girls /
    /// multiple girls, 1girl + 1boy, ...) and removes the ones they refute
    /// (solo, stale lower counts). Pure list-in/list-out so it is unit-testable.
    /// </summary>
    public static class CharacterSubjectCountPlanner
    {
        // Danbooru subject-count ladders, ascending: entry n-1 is the tag for
        // n characters of that gender, the last entry covers "n or more".
        private static readonly string[] GirlCounts = { "1girl", "2girls", "3girls", "4girls", "5girls", "6+girls" };
        private static readonly string[] BoyCounts = { "1boy", "2boys", "3boys", "4boys", "5boys", "6+boys" };

        public static IReadOnlyList<string> GetRequiredTags(IReadOnlyList<CharacterGender> genders)
        {
            int girls = CountOf(genders, CharacterGender.Girl);
            int boys = CountOf(genders, CharacterGender.Boy);
            var required = new List<string>();
            if (girls > 0)
            {
                required.Add(CountTag(GirlCounts, girls));
                if (girls > 1)
                    required.Add("multiple girls");
            }
            if (boys > 0)
            {
                required.Add(CountTag(BoyCounts, boys));
                if (boys > 1)
                    required.Add("multiple boys");
            }
            return required;
        }

        /// <summary>
        /// Tags refuted by the present cast: <c>solo</c>, plus the counts of a
        /// gender that are LOWER than the number of audited characters of that
        /// gender. A higher count is left alone — an unaudited extra person in
        /// the image may legitimately justify it.
        /// </summary>
        public static IReadOnlyList<string> GetConflictingTags(IReadOnlyList<CharacterGender> genders)
        {
            var conflicting = new List<string> { "solo" };
            conflicting.AddRange(GirlCounts.Take(CountOf(genders, CharacterGender.Girl) - 1));
            conflicting.AddRange(BoyCounts.Take(CountOf(genders, CharacterGender.Boy) - 1));
            return conflicting;
        }

        /// <summary>
        /// Returns a new tag list with the subject-count tags of the present
        /// profiles enforced. Existing order is preserved; required tags are
        /// inserted right after the last present trigger word (or at the front
        /// when no trigger is present).
        /// </summary>
        public static IReadOnlyList<string> Apply(
            IEnumerable<string> tags,
            IReadOnlyList<CharacterAuditProfile> present)
        {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));
            if (present == null)
                throw new ArgumentNullException(nameof(present));

            IReadOnlyList<CharacterGender> genders = present.Select(profile => profile.Gender).ToList();
            var conflicting = new HashSet<string>(GetConflictingTags(genders), StringComparer.Ordinal);
            List<string> result = tags
                .Where(tag => tag != null && !conflicting.Contains(tag.Trim()))
                .ToList();

            var already = new HashSet<string>(result.Select(tag => tag.Trim()), StringComparer.Ordinal);
            List<string> missing = GetRequiredTags(genders)
                .Where(tag => !already.Contains(tag) && !HasHigherCount(already, tag))
                .ToList();
            if (missing.Count == 0)
                return result;

            result.InsertRange(FindInsertIndex(result, present), missing);
            return result;
        }

        private static int CountOf(IReadOnlyList<CharacterGender> genders, CharacterGender gender)
        {
            if (genders == null)
                throw new ArgumentNullException(nameof(genders));
            return genders.Count(candidate => candidate == gender);
        }

        private static string CountTag(string[] ladder, int count)
        {
            return ladder[Math.Min(count, ladder.Length) - 1];
        }

        /// <summary>
        /// An unaudited extra person can legitimately raise the count of a
        /// gender. When a higher count is already tagged, keep it instead of
        /// injecting a contradicting lower one next to it.
        /// </summary>
        private static bool HasHigherCount(HashSet<string> tags, string required)
        {
            foreach (string[] ladder in new[] { GirlCounts, BoyCounts })
            {
                int index = Array.IndexOf(ladder, required);
                if (index >= 0)
                    return ladder.Skip(index + 1).Any(tags.Contains);
            }
            return false;
        }

        private static int FindInsertIndex(IReadOnlyList<string> tags, IReadOnlyList<CharacterAuditProfile> present)
        {
            var triggers = new HashSet<string>(
                present.Select(profile => profile.TriggerWord?.Trim() ?? string.Empty)
                    .Where(trigger => trigger.Length > 0),
                StringComparer.Ordinal);
            int lastTrigger = -1;
            for (int i = 0; i < tags.Count; i++)
            {
                if (triggers.Contains(tags[i]?.Trim() ?? string.Empty))
                    lastTrigger = i;
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
    /// TAG-01 checkpoint: thrown when one profile's audit fails after earlier
    /// profiles already completed. Carries the paid, completed results
    /// (index-aligned with the profiles; null = not completed) so the caller
    /// can offer to retry ONLY the failed profile instead of re-billing
    /// everything.
    /// </summary>
    public sealed class CharacterTagDualAuditProfileException : Exception
    {
        public CharacterTagDualAuditProfileException(
            int failedProfileIndex,
            IReadOnlyList<CharacterTagAuditResult> completedResults,
            Exception inner)
            : base(inner?.Message ?? "Dual audit profile failed.", inner)
        {
            FailedProfileIndex = failedProfileIndex;
            CompletedResults = completedResults ?? Array.Empty<CharacterTagAuditResult>();
        }

        public int FailedProfileIndex { get; }

        /// <summary>Per-profile results; null entries were never run.</summary>
        public IReadOnlyList<CharacterTagAuditResult> CompletedResults { get; }
    }

    /// <summary>
    /// Runs the existing two-stage audit once per character profile (2 to
    /// <see cref="MaxProfiles"/> of them) over that character's member images
    /// and aggregates progress into one two-steps-per-profile sequence.
    /// Attribution statistics let the caller surface how many images are
    /// shared or not attributed to any character.
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

        /// <summary>Character profiles a single multi-character run accepts.</summary>
        public const int MaxProfiles = 4;

        public static void Validate(CharacterTagDualAuditOptions options)
        {
            if (options == null)
                throw new System.ArgumentNullException(nameof(options));
            if (options.Profiles == null || options.Profiles.Count < 2 || options.Profiles.Count > MaxProfiles)
            {
                throw new System.ArgumentException(
                    "A multi-character audit needs between two and " + MaxProfiles + " character profiles.",
                    nameof(options));
            }
            var triggers = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterAuditProfile profile in options.Profiles)
            {
                string trigger = profile.TriggerWord?.Trim();
                if (string.IsNullOrEmpty(trigger))
                    throw new System.ArgumentException("Every audited character needs a trigger word.", nameof(options));
                if (!triggers.Add(trigger))
                    throw new System.ArgumentException("Audited characters must use different trigger words.", nameof(options));
            }
            // Every reference is checked before the first model call so a
            // missing reference of a later profile cannot fail after an
            // earlier profile was already paid for (the inner service
            // re-validates per run).
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
            System.Threading.CancellationToken cancellationToken = default,
            IReadOnlyList<CharacterTagAuditResult> resumeFrom = null)
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
                if (resumeFrom != null && i < resumeFrom.Count && resumeFrom[i] != null)
                {
                    // Checkpoint: this profile already completed in a previous
                    // run — reuse its paid result instead of re-billing it.
                    profileResults.Add(resumeFrom[i]);
                    continue;
                }
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
                try
                {
                    profileResults.Add(await inner.ExecuteAsync(innerOptions, wrappedProgress, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Checkpoint the completed profiles so the caller can
                    // retry only this one instead of discarding paid results.
                    var completed = new CharacterTagAuditResult[options.Profiles.Count];
                    for (int c = 0; c < profileResults.Count; c++)
                        completed[c] = profileResults[c];
                    throw new CharacterTagDualAuditProfileException(i, completed, ex);
                }
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
            IReadOnlyList<CharacterAuditProfile> present)
        {
            if (tags == null)
                throw new System.ArgumentNullException(nameof(tags));
            IReadOnlyList<string> desired = CharacterSubjectCountPlanner.Apply(
                tags.Select(tag => tag.Tag).ToList(), present);
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
    /// subject-count tags on images containing more than one of them. Images
    /// that belong to no audited character are returned unchanged.
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
            if (present.Count >= 2)
            {
                transformed = CharacterSubjectCountPlanner.Apply(
                    transformed, present.Select(index => profiles[index]).ToList());
            }
            return transformed;
        }
    }
}
