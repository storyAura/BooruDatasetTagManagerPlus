using BooruDatasetTagManager;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BooruDatasetTagManager.Tests
{
    public class DatasetFolderAndDualAuditGuardTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BooruDatasetTagManager.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Repository root not found.");
        }

        private static string ProjectDirectory() => Path.Combine(RepoRoot(), "BooruDatasetTagManager");

        [Fact]
        public void DualAuditAndFolderListLocalizationKeysExistInEveryLanguage()
        {
            string[] required =
            {
                "FolderListAll", "FolderListRoot", "FolderListImageCount",
                "CharacterTagAuditSubjectMode", "CharacterTagAuditSubjectSingle", "CharacterTagAuditSubjectDual",
                "CharacterTagAuditTriggerB", "CharacterTagAuditGenders", "CharacterTagAuditFolderA",
                "CharacterTagAuditFolderB", "CharacterTagAuditFolderAny", "CharacterTagAuditReferences",
                "CharacterTagAuditSetReferenceA", "CharacterTagAuditSetReferenceB", "CharacterTagAuditReferenceUnset",
                "CharacterTagAuditTriggerBRequired", "CharacterTagAuditTriggersMustDiffer",
                "CharacterTagAuditReferencesRequired", "CharacterTagAuditProfileTab", "CharacterTagAuditDualSummary",
                "CharacterGenderGirl", "CharacterGenderBoy"
            };
            foreach (string language in new[] { "en-US", "zh-CN", "zh-TW", "ru-RU", "pt-BR" })
            {
                string text = File.ReadAllText(Path.Combine(ProjectDirectory(), "Languages", language + ".txt"));
                foreach (string key in required)
                    Assert.Contains(key + "=", text);
            }
        }

        [Fact]
        public void MainFormWiresFolderGalleryIntoDatasetScope()
        {
            string form = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form1.cs"));
            Assert.Contains("InitializeDatasetFolderList", form);
            Assert.Contains("FolderList_FolderSelected", form);
            Assert.Contains("SetActiveFolder", form);
            Assert.Contains("RefreshDatasetFolderList", form);
            Assert.Contains("GetActiveScopeCount", form);
            Assert.True(File.Exists(Path.Combine(ProjectDirectory(), "DatasetFolderListView.cs")));
        }

        [Fact]
        public void WizardSupportsDualCharacterAudit()
        {
            string wizard = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));
            Assert.Contains("comboSubjectMode", wizard);
            Assert.Contains("CharacterTagDualAuditService", wizard);
            Assert.Contains("TransformEditableTagsDualForItem", wizard);
            Assert.Contains("CharacterTagEditableTagInjector.ApplySubjectCount", wizard);
            Assert.Contains("GetScopedItems", wizard);
            Assert.Contains("SwitchResultProfile", wizard);
        }

        [Fact]
        public void AuditorSkillCoversDualModeAndCanonicalOrder()
        {
            string skill = File.ReadAllText(Path.Combine(
                RepoRoot(), "Agent", "skills", "character-tag-auditor", "SKILL.md"));
            Assert.Contains("dual", skill, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Canonical prompt order", skill);
            Assert.Contains("Reference sparse prompts", skill);
            Assert.Contains("lynae (peppermint) (wuthering waves)", skill);
            Assert.Contains("denia haonvhai", skill);
            Assert.Contains("other-person evidence", skill);
        }
    }

    public class CharacterTagDualAuditServiceTests
    {
        private static CharacterImageTagRecord Record(string path, params string[] tags) =>
            new CharacterImageTagRecord { ImagePath = path, Tags = tags };

        private static string KeepAllJson(IReadOnlyList<string> tags)
        {
            return JsonConvert.SerializeObject(new
            {
                tags = tags.Select(tag => new
                {
                    tag,
                    decision = "keep",
                    category = "hair",
                    reason = "test",
                    include_in_prompt = true,
                    prompt_order = 1
                })
            });
        }

        private static CharacterTagDualAuditOptions Options(string root, string referenceA, string referenceB)
        {
            return new CharacterTagDualAuditOptions
            {
                DatasetRoot = root,
                Images = new[]
                {
                    Record(Path.Combine(root, "a", "1.png"), "char_a", "skirt"),
                    Record(Path.Combine(root, "b", "1.png"), "char_b", "hat"),
                    Record(Path.Combine(root, "mix", "1.png"), "char_a", "char_b", "skirt"),
                    Record(Path.Combine(root, "none", "1.png"), "1girl")
                },
                Profiles = new[]
                {
                    new CharacterAuditProfile { TriggerWord = "char_a", ReferenceImagePath = referenceA },
                    new CharacterAuditProfile { TriggerWord = "char_b", ReferenceImagePath = referenceB }
                },
                Style = CharacterTagAuditStyle.Sparse,
                MinimumCount = 1,
                Model = "gemini-test",
                CharacterAuditorSkill = "auditor rules",
                PromptPyramidSkill = "pyramid rules"
            };
        }

        [Fact]
        public async Task DualAuditRunsBothProfilesWithScopedInventoriesAndAggregatedProgress()
        {
            using var temp = new TemporaryDirectory();
            string referenceA = Path.Combine(temp.Path, "a.png");
            string referenceB = Path.Combine(temp.Path, "b.png");
            File.WriteAllBytes(referenceA, new byte[] { 1 });
            File.WriteAllBytes(referenceB, new byte[] { 1 });

            var inventoriesSeen = new List<IReadOnlyList<string>>();
            IReadOnlyList<string> currentTags = null;
            var service = new CharacterTagDualAuditService(new CharacterTagAuditService((request, _) =>
            {
                if (request.Stage == CharacterTagAuditStage.TextScreening)
                {
                    string json = request.UserPrompt.Substring(
                        request.UserPrompt.IndexOf("Tags: ", StringComparison.Ordinal) + "Tags: ".Length);
                    currentTags = JArray.Parse(json).Select(token => token.Value<string>("Tag")).ToList();
                    inventoriesSeen.Add(currentTags);
                }
                return Task.FromResult(new CharacterTagModelResponse(KeepAllJson(currentTags), string.Empty));
            }));
            var updates = new List<CharacterTagAuditProgress>();
            var progress = new ImmediateProgress<CharacterTagAuditProgress>(updates.Add);

            CharacterTagDualAuditResult result = await service.ExecuteAsync(
                Options(temp.Path, referenceA, referenceB), progress);

            Assert.Equal(2, result.ProfileResults.Count);
            Assert.Equal(new[] { "char_a", "skirt", "char_b" }, inventoriesSeen[0]);
            Assert.Equal(new[] { "char_b", "hat", "char_a", "skirt" }, inventoriesSeen[1]);
            Assert.Equal(new[] { 2, 2 }, result.MemberImageCounts);
            Assert.Equal(1, result.SharedImageCount);
            Assert.Equal(1, result.UnattributedImageCount);
            Assert.Equal(new[] { 0, 1, 1, 2, 2, 3, 3, 4 }, updates.Select(update => update.CompletedSteps));
            Assert.All(updates, update => Assert.Equal(4, update.TotalSteps));
            Assert.Equal(new[] { 0, 0, 0, 0, 1, 1, 1, 1 }, updates.Select(update => update.ProfileIndex));
            Assert.Contains("char_a", result.ProfileResults[0].FinalPrompt);
        }

        [Fact]
        public async Task DualAuditRejectsDuplicateTriggersAndProfilesWithoutImages()
        {
            using var temp = new TemporaryDirectory();
            string reference = Path.Combine(temp.Path, "a.png");
            File.WriteAllBytes(reference, new byte[] { 1 });
            var service = new CharacterTagDualAuditService(new CharacterTagAuditService(
                (_, _) => Task.FromResult(new CharacterTagModelResponse("{}", string.Empty))));

            CharacterTagDualAuditOptions duplicate = Options(temp.Path, reference, reference);
            duplicate.Profiles = new[]
            {
                new CharacterAuditProfile { TriggerWord = "same", ReferenceImagePath = reference },
                new CharacterAuditProfile { TriggerWord = "same", ReferenceImagePath = reference }
            };
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(duplicate));

            CharacterTagDualAuditOptions unmatched = Options(temp.Path, reference, reference);
            unmatched.Profiles = new[]
            {
                new CharacterAuditProfile { TriggerWord = "char_a", ReferenceImagePath = reference },
                new CharacterAuditProfile { TriggerWord = "missing_trigger", ReferenceImagePath = reference }
            };
            await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(unmatched));
        }
    }

    public class DatasetFolderIndexTests
    {
        private const string Root = @"C:\dataset";

        [Fact]
        public void GetRelativeFolderNormalizesSeparatorsAndRoot()
        {
            Assert.Equal("konya_karasue_webp/1_a",
                DatasetFolderIndex.GetRelativeFolder(@"C:\dataset\konya_karasue_webp\1_a\img1.png", Root));
            Assert.Equal(string.Empty,
                DatasetFolderIndex.GetRelativeFolder(@"C:\dataset\img1.png", Root));
            Assert.Equal(string.Empty,
                DatasetFolderIndex.GetRelativeFolder(@"C:\elsewhere\img1.png", Root));
        }

        [Fact]
        public void IsInFolderTreatsEmptyFolderAsAllAndMatchesCaseInsensitive()
        {
            string image = @"C:\dataset\Andy3D\1_a\pic.webp";
            Assert.True(DatasetFolderIndex.IsInFolder(image, Root, null));
            Assert.True(DatasetFolderIndex.IsInFolder(image, Root, ""));
            Assert.True(DatasetFolderIndex.IsInFolder(image, Root, "andy3d/1_A"));
            Assert.True(DatasetFolderIndex.IsInFolder(image, Root, @"Andy3D\1_a"));
            Assert.False(DatasetFolderIndex.IsInFolder(image, Root, "Andy3D"));
            Assert.False(DatasetFolderIndex.IsInFolder(image, Root, "daodtt_webp/1_a"));
        }

        [Fact]
        public void CreateGroupsCountsAndPicksRepresentative()
        {
            var entries = DatasetFolderIndex.Create(new[]
            {
                @"C:\dataset\b_webp\1_a\z.png",
                @"C:\dataset\b_webp\1_a\a.png",
                @"C:\dataset\a_webp\1_a\only.png",
                @"C:\dataset\loose.png"
            }, Root);

            Assert.Equal(3, entries.Count);
            Assert.Equal(string.Empty, entries[0].RelativePath);
            Assert.Equal("a_webp/1_a", entries[1].RelativePath);
            Assert.Equal("b_webp/1_a", entries[2].RelativePath);
            Assert.Equal(2, entries[2].ImageCount);
            Assert.Equal(@"C:\dataset\b_webp\1_a\a.png", entries[2].RepresentativeImagePath);
        }
    }

    public class CharacterImageMembershipTests
    {
        private const string Root = @"C:\dataset";

        private static CharacterAuditProfile ProfileA(string folder = "") => new CharacterAuditProfile
        {
            TriggerWord = "alf \\(silver palace\\)",
            Gender = CharacterGender.Girl,
            FolderScope = folder
        };

        private static CharacterAuditProfile ProfileB(string folder = "") => new CharacterAuditProfile
        {
            TriggerWord = "velina airgid",
            Gender = CharacterGender.Girl,
            FolderScope = folder
        };

        [Fact]
        public void TriggerWordPresenceAttributesImage()
        {
            var tags = new[] { "alf \\(silver palace\\)", "1girl", "solo" };
            var present = CharacterImageMembership.GetPresentProfiles(
                tags, @"C:\dataset\x\1.png", Root, new[] { ProfileA(), ProfileB() });
            Assert.Equal(new[] { 0 }, present);
        }

        [Fact]
        public void BothTriggersMakeImageShared()
        {
            var tags = new[] { "alf \\(silver palace\\)", "velina airgid", "2girls" };
            var present = CharacterImageMembership.GetPresentProfiles(
                tags, @"C:\dataset\x\1.png", Root, new[] { ProfileA(), ProfileB() });
            Assert.Equal(new[] { 0, 1 }, present);
        }

        [Fact]
        public void FolderScopeAttributesImagesWithoutTrigger()
        {
            var tags = new[] { "1girl", "solo", "skirt" };
            var present = CharacterImageMembership.GetPresentProfiles(
                tags, @"C:\dataset\alf_webp\1_a\1.png", Root,
                new[] { ProfileA("alf_webp/1_a"), ProfileB("velina_webp/1_a") });
            Assert.Equal(new[] { 0 }, present);
        }

        [Fact]
        public void UnattributedImageHasNoProfiles()
        {
            var tags = new[] { "1girl", "solo" };
            var present = CharacterImageMembership.GetPresentProfiles(
                tags, @"C:\dataset\other\1.png", Root, new[] { ProfileA(), ProfileB() });
            Assert.Empty(present);
        }
    }

    public class CharacterSubjectCountPlannerTests
    {
        private static CharacterAuditProfile Girl(string trigger) => new CharacterAuditProfile
        {
            TriggerWord = trigger,
            Gender = CharacterGender.Girl
        };

        private static CharacterAuditProfile Boy(string trigger) => new CharacterAuditProfile
        {
            TriggerWord = trigger,
            Gender = CharacterGender.Boy
        };

        [Fact]
        public void TwoGirlsInjectCountTagsAfterTriggersAndDropSoloAnd1Girl()
        {
            var tags = new[] { "char_a", "char_b", "1girl", "solo", "skirt" };
            var result = CharacterSubjectCountPlanner.Apply(tags, Girl("char_a"), Girl("char_b"));
            Assert.Equal(new[] { "char_a", "char_b", "2girls", "multiple girls", "skirt" }, result);
        }

        [Fact]
        public void ExistingCountTagsAreNotDuplicated()
        {
            var tags = new[] { "char_a", "2girls", "char_b", "multiple girls", "hat" };
            var result = CharacterSubjectCountPlanner.Apply(tags, Girl("char_a"), Girl("char_b"));
            Assert.Equal(new[] { "char_a", "2girls", "char_b", "multiple girls", "hat" }, result);
        }

        [Fact]
        public void MixedPairKeepsSingularCountsAndDropsSolo()
        {
            var tags = new[] { "char_a", "char_b", "solo", "outdoors" };
            var result = CharacterSubjectCountPlanner.Apply(tags, Girl("char_a"), Boy("char_b"));
            Assert.Equal(new[] { "char_a", "char_b", "1girl", "1boy", "outdoors" }, result);
        }

        [Fact]
        public void NoTriggerInListInsertsAtFront()
        {
            var tags = new[] { "solo", "1girl", "smile" };
            var result = CharacterSubjectCountPlanner.Apply(tags, Girl("char_a"), Girl("char_b"));
            Assert.Equal(new[] { "2girls", "multiple girls", "smile" }, result);
        }
    }

    public class CharacterTagDualDecisionMergerTests
    {
        private static CharacterTagAuditItem Item(
            string tag,
            CharacterTagDecision decision,
            string replacement = "",
            CharacterTagCategory category = CharacterTagCategory.Clothing)
        {
            return new CharacterTagAuditItem
            {
                Tag = tag,
                Count = 5,
                FinalDecision = decision,
                ReplacementTag = replacement,
                Category = category,
                IncludeInPrompt = decision != CharacterTagDecision.Delete
            };
        }

        [Fact]
        public void SameOutcomeIsApplied()
        {
            var merged = CharacterTagDualDecisionMerger.MergeItem(
                Item("skirt", CharacterTagDecision.Replace, "pink skirt"),
                Item("skirt", CharacterTagDecision.Replace, "pink skirt"));
            Assert.True(merged.ShouldReplace);
            Assert.Equal("pink skirt", merged.ReplacementTag);
        }

        [Fact]
        public void DeleteLosesAgainstNonDelete()
        {
            var merged = CharacterTagDualDecisionMerger.MergeItem(
                Item("hat", CharacterTagDecision.Delete),
                Item("hat", CharacterTagDecision.Replace, "white hat"));
            Assert.False(merged.ShouldDelete);
            Assert.Equal("white hat", merged.EffectiveTag);
        }

        [Fact]
        public void ConflictingReplacementsKeepOriginalTag()
        {
            var merged = CharacterTagDualDecisionMerger.MergeItem(
                Item("skirt", CharacterTagDecision.Replace, "pink skirt"),
                Item("skirt", CharacterTagDecision.Replace, "blue skirt"));
            Assert.Equal(CharacterTagDecision.Keep, merged.FinalDecision);
            Assert.False(merged.ShouldReplace);
            Assert.Equal("skirt", merged.EffectiveTag);
            Assert.False(merged.IncludeInPrompt);
        }

        [Fact]
        public void KeepVersusReplaceKeepsOriginalTag()
        {
            var merged = CharacterTagDualDecisionMerger.MergeItem(
                Item("skirt", CharacterTagDecision.Keep),
                Item("skirt", CharacterTagDecision.Replace, "blue skirt"));
            Assert.Equal(CharacterTagDecision.Keep, merged.FinalDecision);
            Assert.Equal("skirt", merged.EffectiveTag);
        }
    }

    public class CharacterTagMultiAuditPlanTests
    {
        private const string Root = @"C:\dataset";

        private static CharacterAuditProfile ProfileA => new CharacterAuditProfile
        {
            TriggerWord = "char_a",
            Gender = CharacterGender.Girl,
            FolderScope = "a_webp/1_a"
        };

        private static CharacterAuditProfile ProfileB => new CharacterAuditProfile
        {
            TriggerWord = "char_b",
            Gender = CharacterGender.Girl,
            FolderScope = "b_webp/1_a"
        };

        private static IReadOnlyDictionary<string, CharacterTagAuditItem> Decisions(
            params CharacterTagAuditItem[] items)
        {
            return items.ToDictionary(item => item.Tag, StringComparer.Ordinal);
        }

        private static CharacterTagAuditItem Replace(string tag, string replacement) => new CharacterTagAuditItem
        {
            Tag = tag,
            FinalDecision = CharacterTagDecision.Replace,
            ReplacementTag = replacement,
            Category = CharacterTagCategory.Clothing,
            IncludeInPrompt = true
        };

        private static CharacterTagAuditItem Delete(string tag) => new CharacterTagAuditItem
        {
            Tag = tag,
            FinalDecision = CharacterTagDecision.Delete,
            Category = CharacterTagCategory.Clothing
        };

        [Fact]
        public void SingleCharacterImageUsesOwnCharactersReplacement()
        {
            var decisions = new[]
            {
                Decisions(Replace("skirt", "pink skirt")),
                Decisions(Replace("skirt", "blue skirt"))
            };
            var resultA = CharacterTagMultiAuditPlan.TransformImageTags(
                new[] { "char_a", "1girl", "skirt" }, @"C:\dataset\a_webp\1_a\1.png", Root,
                new[] { ProfileA, ProfileB }, decisions);
            var resultB = CharacterTagMultiAuditPlan.TransformImageTags(
                new[] { "char_b", "1girl", "skirt" }, @"C:\dataset\b_webp\1_a\1.png", Root,
                new[] { ProfileA, ProfileB }, decisions);

            Assert.Equal(new[] { "char_a", "1girl", "pink skirt" }, resultA);
            Assert.Equal(new[] { "char_b", "1girl", "blue skirt" }, resultB);
        }

        [Fact]
        public void SharedImageKeepsConflictingTagAndGetsSubjectCountTags()
        {
            var decisions = new[]
            {
                Decisions(Replace("skirt", "pink skirt"), Delete("hat")),
                Decisions(Replace("skirt", "blue skirt"), Replace("hat", "white hat"))
            };
            var result = CharacterTagMultiAuditPlan.TransformImageTags(
                new[] { "char_a", "char_b", "1girl", "solo", "skirt", "hat" },
                @"C:\dataset\a_webp\1_a\shared.png", Root,
                new[] { ProfileA, ProfileB }, decisions);

            Assert.Equal(new[] { "char_a", "char_b", "2girls", "multiple girls", "skirt", "white hat" }, result);
        }

        [Fact]
        public void UnattributedImageIsUntouched()
        {
            var decisions = new[]
            {
                Decisions(Replace("skirt", "pink skirt")),
                Decisions(Replace("skirt", "blue skirt"))
            };
            var result = CharacterTagMultiAuditPlan.TransformImageTags(
                new[] { "1girl", "skirt" }, @"C:\dataset\other\1.png", Root,
                new[] { ProfileA, ProfileB }, decisions);
            Assert.Equal(new[] { "1girl", "skirt" }, result);
        }
    }

    public class CharacterTagDualAuditPromptHintTests
    {
        [Fact]
        public async Task EachProfilePromptNamesTheOtherCharacterInBothStages()
        {
            using var temp = new TemporaryDirectory();
            string reference = Path.Combine(temp.Path, "a.png");
            File.WriteAllBytes(reference, new byte[] { 1 });

            var requests = new List<CharacterTagModelRequest>();
            IReadOnlyList<string> currentTags = null;
            var service = new CharacterTagDualAuditService(new CharacterTagAuditService((request, _) =>
            {
                requests.Add(request);
                if (request.Stage == CharacterTagAuditStage.TextScreening)
                {
                    string json = request.UserPrompt.Substring(
                        request.UserPrompt.IndexOf("Tags: ", StringComparison.Ordinal) + "Tags: ".Length);
                    currentTags = JArray.Parse(json).Select(token => token.Value<string>("Tag")).ToList();
                }
                string body = JsonConvert.SerializeObject(new
                {
                    tags = currentTags.Select(tag => new
                    {
                        tag,
                        decision = "keep",
                        category = "hair",
                        reason = "test",
                        include_in_prompt = true,
                        prompt_order = 1
                    })
                });
                return Task.FromResult(new CharacterTagModelResponse(body, string.Empty));
            }));

            var options = new CharacterTagDualAuditOptions
            {
                DatasetRoot = temp.Path,
                Images = new[]
                {
                    new CharacterImageTagRecord { ImagePath = Path.Combine(temp.Path, "a", "1.png"), Tags = new[] { "char_a", "skirt" } },
                    new CharacterImageTagRecord { ImagePath = Path.Combine(temp.Path, "b", "1.png"), Tags = new[] { "char_b", "hat" } }
                },
                Profiles = new[]
                {
                    new CharacterAuditProfile { TriggerWord = "char_a", ReferenceImagePath = reference },
                    new CharacterAuditProfile { TriggerWord = "char_b", ReferenceImagePath = reference }
                },
                Style = CharacterTagAuditStyle.Sparse,
                MinimumCount = 1,
                Model = "gemini-test",
                CharacterAuditorSkill = "auditor rules",
                PromptPyramidSkill = "pyramid rules"
            };

            await service.ExecuteAsync(options);

            // Neither profile's own inventory contains the other trigger here,
            // so any mention must come from the attribution hint.
            List<string> textPrompts = requests
                .Where(request => request.Stage == CharacterTagAuditStage.TextScreening)
                .Select(request => request.UserPrompt).ToList();
            Assert.Contains("char_b", textPrompts[0]);
            Assert.Contains("char_a", textPrompts[1]);
            List<string> visualPrompts = requests
                .Where(request => request.Stage == CharacterTagAuditStage.VisualReview)
                .Select(request => request.UserPrompt).ToList();
            Assert.Contains("char_b", visualPrompts[0]);
            Assert.Contains("char_a", visualPrompts[1]);
        }
    }

    public class CharacterPromptSubjectNormalizerTests
    {
        [Fact]
        public void ReplacesMultiSubjectTagsWithTheCharactersOwnCount()
        {
            Assert.Equal("nikaido hiro, 1girl, red eyes, black hair",
                CharacterPromptSubjectNormalizer.NormalizeToSingle(
                    "nikaido hiro, 2girls, red eyes, multiple girls, black hair", CharacterGender.Girl));
            Assert.Equal("char, 1boy, hat",
                CharacterPromptSubjectNormalizer.NormalizeToSingle("char, 2boys, hat", CharacterGender.Boy));
        }

        [Fact]
        public void LeavesSingleSubjectPromptsUntouchedAndNeverDuplicates()
        {
            Assert.Equal("char, 1girl, hat",
                CharacterPromptSubjectNormalizer.NormalizeToSingle("char, 1girl, hat", CharacterGender.Girl));
            Assert.Equal("char, 1girl, hat",
                CharacterPromptSubjectNormalizer.NormalizeToSingle("char, 1girl, 2girls, hat", CharacterGender.Girl));
            Assert.Equal(string.Empty,
                CharacterPromptSubjectNormalizer.NormalizeToSingle("", CharacterGender.Girl));
        }
    }
}
