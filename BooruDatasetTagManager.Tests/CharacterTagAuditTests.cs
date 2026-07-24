using Newtonsoft.Json;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CharacterTagAuditTests
{
    [Fact]
    public void InventoryCountsEachTagOncePerImageAndKeepsFirstSeenOrder()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[]
        {
            new[] { "trigger", "blue hair", "blue hair" },
            new[] { "blue hair", "smile" }
        });

        Assert.Equal(new[] { "trigger", "blue hair", "smile" }, inventory.Tags.Select(item => item.Tag));
        Assert.Equal(new[] { 1, 2, 1 }, inventory.Tags.Select(item => item.Count));
    }

    [Fact]
    public void ParserRejectsMissingUnknownAndDuplicateTags()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "blue hair" } });

        Assert.Throws<CharacterTagAuditResponseException>(() => CharacterTagAuditResponseParser.ParseAndValidate(
            "{\"tags\":[{\"tag\":\"trigger\",\"decision\":\"keep\",\"category\":\"identity\",\"reason\":\"locked\"}]}",
            inventory,
            "trigger"));
        Assert.Throws<CharacterTagAuditResponseException>(() => CharacterTagAuditResponseParser.ParseAndValidate(
            "{\"tags\":[{\"tag\":\"trigger\",\"decision\":\"keep\"},{\"tag\":\"unknown\",\"decision\":\"delete\"}]}",
            inventory,
            "trigger"));
        Assert.Throws<CharacterTagAuditResponseException>(() => CharacterTagAuditResponseParser.ParseAndValidate(
            "{\"tags\":[{\"tag\":\"trigger\",\"decision\":\"keep\"},{\"tag\":\"trigger\",\"decision\":\"keep\"},{\"tag\":\"blue hair\",\"decision\":\"keep\"}]}",
            inventory,
            "trigger"));
    }

    [Fact]
    public void ParserForcesTriggerToKeepAndTreatsUncertainAsSafe()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "blue hair" } });
        string json = "{\"tags\":[{\"tag\":\"trigger\",\"decision\":\"delete\"},{\"tag\":\"blue hair\",\"decision\":\"uncertain\"}]}";

        IReadOnlyList<CharacterTagAuditItem> result = CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, "trigger");

        Assert.Equal(CharacterTagDecision.Keep, result[0].FinalDecision);
        Assert.Equal(CharacterTagDecision.Uncertain, result[1].FinalDecision);
        Assert.False(result[1].ShouldDelete);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("pose")]
    [InlineData("expression")]
    [InlineData("scene")]
    [InlineData("composition")]
    [InlineData("quality")]
    [InlineData("object")]
    [InlineData("other")]
    public void ParserForcesProtectedCategoriesToKeep(string category)
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "smile" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[] { new { tag = "smile", decision = "delete", category, reason = "model requested deletion" } }
        });

        CharacterTagAuditItem result = Assert.Single(
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.Equal(CharacterTagDecision.Keep, result.FinalDecision);
        Assert.False(result.CanDelete);
        Assert.False(result.ShouldDelete);
    }

    [Theory]
    [InlineData("hair")]
    [InlineData("eyes")]
    [InlineData("face")]
    [InlineData("body")]
    [InlineData("clothing")]
    [InlineData("footwear")]
    [InlineData("legwear")]
    [InlineData("wearable_accessory")]
    public void ParserAllowsDeletionOnlyForCharacterAppearanceAndWearables(string category)
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "blue hair" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[] { new { tag = "blue hair", decision = "delete", category, reason = "wrong detail" } }
        });

        CharacterTagAuditItem result = Assert.Single(
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.True(result.CanDelete);
        Assert.True(result.ShouldDelete);
    }

    [Fact]
    public void GenericHairColorReplacementPolicyBlocksConcreteToGenericOnly()
    {
        Assert.True(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("white hair", "colored hair"));
        Assert.True(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("white hair", "Multicolored Hair"));
        Assert.True(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("twintails", "two-tone hair"));
        Assert.False(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("streaked hair", "multicolored hair"));
        Assert.False(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("white hair", "grey hair"));
        Assert.False(CharacterTagAuditPolicy.IsForbiddenGenericHairReplacement("white hair", ""));
    }

    [Fact]
    public void ParserForcesGenericHairColorReplacementsBackToKeep()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "white hair", "streaked hair" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new { tag = "white hair", decision = "replace", replacement_tag = "colored hair", category = "hair", reason = "merge hair parts" },
                new { tag = "streaked hair", decision = "replace", replacement_tag = "multicolored hair", category = "hair", reason = "normalize" }
            }
        });

        IReadOnlyList<CharacterTagAuditItem> result = CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty);

        CharacterTagAuditItem white = result.Single(item => item.Tag == "white hair");
        CharacterTagAuditItem streaked = result.Single(item => item.Tag == "streaked hair");
        Assert.Equal(CharacterTagDecision.Keep, white.FinalDecision);
        Assert.Equal(string.Empty, white.ReplacementTag);
        Assert.Equal(CharacterTagDecision.Replace, streaked.FinalDecision);
        Assert.Equal("multicolored hair", streaked.ReplacementTag);
    }

    [Fact]
    public void ParserAcceptsValidReplacementAndBuildsEffectiveTag()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "skirt" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new
                {
                    tag = "skirt", decision = "replace", replacement_tag = "pink skirt",
                    category = "clothing", reason = "verified color", include_in_prompt = true, prompt_order = 8
                }
            }
        });

        CharacterTagAuditItem item = Assert.Single(
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.Equal(CharacterTagDecision.Replace, item.FinalDecision);
        Assert.Equal("pink skirt", item.ReplacementTag);
        Assert.Equal("pink skirt", item.EffectiveTag);
        Assert.True(item.IncludeInPrompt);
        Assert.Equal(8, item.PromptOrder);
    }

    [Fact]
    public void ManualDeleteOnProtectedCategoryIsApplied()
    {
        // The review grid lets the user override any non-trigger tag, so a
        // manual Delete on a protected category must reach the caption files.
        var item = new CharacterTagAuditItem
        {
            Tag = "smile",
            FinalDecision = CharacterTagDecision.Delete,
            Category = CharacterTagCategory.Expression
        };

        Assert.False(item.CanDelete);
        Assert.True(item.ShouldDelete);
        Assert.Equal(
            new[] { "1girl" },
            CharacterTagTransformation.Apply(new[] { "1girl", "smile" }, new[] { item }));
    }

    [Fact]
    public void ManualReplaceOnProtectedCategoryIsApplied()
    {
        var item = new CharacterTagAuditItem
        {
            Tag = "standing",
            FinalDecision = CharacterTagDecision.Replace,
            ReplacementTag = "sitting",
            Category = CharacterTagCategory.Pose
        };

        Assert.True(item.ShouldReplace);
        Assert.Equal(
            new[] { "sitting" },
            CharacterTagTransformation.Apply(new[] { "standing" }, new[] { item }));
    }

    [Fact]
    public void ReplaceWithoutTargetIsNotApplied()
    {
        var item = new CharacterTagAuditItem
        {
            Tag = "jacket",
            FinalDecision = CharacterTagDecision.Replace,
            ReplacementTag = "  ",
            Category = CharacterTagCategory.Clothing
        };

        Assert.False(item.ShouldReplace);
        Assert.Equal("jacket", item.EffectiveTag);
    }

    [Fact]
    public void ParserTreatsReplacementWithIdenticalTargetAsKeep()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "black thighhighs" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new
                {
                    tag = "black thighhighs", decision = "replace", replacement_tag = "black thighhighs",
                    category = "legwear", reason = "already canonical", include_in_prompt = true, prompt_order = 8
                }
            }
        });

        CharacterTagAuditItem item = Assert.Single(
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.Equal(CharacterTagDecision.Keep, item.FinalDecision);
        Assert.Empty(item.ReplacementTag);
        Assert.Equal("black thighhighs", item.EffectiveTag);
        Assert.True(item.IncludeInPrompt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pink, skirt")]
    [InlineData("pink\nskirt")]
    public void ParserRejectsInvalidReplacementTargets(string replacement)
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "skirt" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new { tag = "skirt", decision = "replace", replacement_tag = replacement, category = "clothing", reason = "test" }
            }
        });

        Assert.Throws<CharacterTagAuditResponseException>(() =>
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));
    }

    [Fact]
    public void InvalidReplacementErrorCarriesSourceAndTargetForLocalizedDisplay()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "skirt" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new[]
            {
                new { tag = "skirt", decision = "replace", replacement_tag = "pink, skirt", category = "clothing" }
            }
        });

        CharacterTagAuditResponseException error = Assert.Throws<CharacterTagAuditResponseException>(() =>
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));

        Assert.Equal("skirt", error.SourceTag);
        Assert.Equal("pink, skirt", error.ReplacementTag);
        Assert.Equal(
            "模型返回了无效替换：skirt → pink, skirt",
            CharacterTagAuditErrorFormatter.Format(error, key =>
                key == "CharacterTagAuditModelInvalidReplacement"
                    ? "模型返回了无效替换：{0} → {1}"
                    : key));
    }

    [Fact]
    public void ParserRejectsReplacementChains()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "hat", "white headwear" } });
        string json = JsonConvert.SerializeObject(new
        {
            tags = new object[]
            {
                new { tag = "hat", decision = "replace", replacement_tag = "white headwear", category = "wearable_accessory", reason = "test" },
                new { tag = "white headwear", decision = "replace", replacement_tag = "white hat", category = "wearable_accessory", reason = "test" }
            }
        });

        Assert.Throws<CharacterTagAuditResponseException>(() =>
            CharacterTagAuditResponseParser.ParseAndValidate(json, inventory, string.Empty));
    }

    [Fact]
    public void TransformationReplacesDeletesAndDeduplicatesAtEarliestPosition()
    {
        var decisions = new[]
        {
            AuditItem("skirt", CharacterTagDecision.Replace, "pink skirt"),
            AuditItem("bikini skirt", CharacterTagDecision.Replace, "pink skirt"),
            AuditItem("plaid", CharacterTagDecision.Delete),
            AuditItem("smile", CharacterTagDecision.Keep, category: CharacterTagCategory.Expression)
        };

        IReadOnlyList<string> result = CharacterTagTransformation.Apply(
            new[] { "trigger", "skirt", "plaid", "smile", "bikini skirt", "pink skirt" }, decisions);

        Assert.Equal(new[] { "trigger", "pink skirt", "smile" }, result);
    }

    [Fact]
    public void CanonicalizerCollapsesVerifiedBikiniAndSkirtAliases()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("swimsuit", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("bikini", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("pink bikini", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("bikini skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("pink skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items);

        Assert.Equal(CharacterTagDecision.Delete, items.Single(item => item.Tag == "swimsuit").FinalDecision);
        Assert.Equal("pink bikini", items.Single(item => item.Tag == "bikini").ReplacementTag);
        Assert.Equal("pink skirt", items.Single(item => item.Tag == "skirt").ReplacementTag);
        Assert.Equal("pink skirt", items.Single(item => item.Tag == "bikini skirt").ReplacementTag);
        Assert.Equal("pink bikini, pink skirt", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void CanonicalizerKeepsOnlyVerifiedLowTwintailsForCurrentCharacter()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("twin braids", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("twintails", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("low twintails", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items);

        Assert.Equal("low twintails", items[0].ReplacementTag);
        Assert.Equal("low twintails", items[1].ReplacementTag);
        Assert.Equal("low twintails", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void CanonicalizerDoesNotTouchOtherCharacterTraitsOrInventColor()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("twin braids", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: false),
            AuditItem("twintails", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("low twintails", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("bikini skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items);

        Assert.Equal(CharacterTagDecision.Keep, items[0].FinalDecision);
        Assert.Empty(items[0].ReplacementTag);
        Assert.Equal("low twintails", items[1].ReplacementTag);
        Assert.Equal("bikini skirt", items.Single(item => item.Tag == "skirt").ReplacementTag);
        Assert.DoesNotContain(items, item => item.ReplacementTag.Contains("pink", StringComparison.Ordinal));
    }

    [Fact]
    public void SparseCanonicalizerDeletesMinorHairAndFaceFeatureFamilies()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("bangs", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("blunt bangs", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("hair between eyes", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("ahoge", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("one side up", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("fang", CharacterTagDecision.Keep, category: CharacterTagCategory.Face, includeInPrompt: true),
            AuditItem("fangs", CharacterTagDecision.Keep, category: CharacterTagCategory.Face, includeInPrompt: true),
            AuditItem("mole under eye", CharacterTagDecision.Keep, category: CharacterTagCategory.Face, includeInPrompt: true),
            AuditItem("long hair", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Sparse);

        Assert.All(items.Take(8), item => Assert.Equal(CharacterTagDecision.Delete, item.FinalDecision));
        Assert.Equal(CharacterTagDecision.Keep, items[8].FinalDecision);
        Assert.Equal("long hair", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void FullCanonicalizerKeepsRealMinorHairAndFaceFeatures()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("blunt bangs", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("hair between eyes", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("ahoge", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("one side up", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true),
            AuditItem("fangs", CharacterTagDecision.Keep, category: CharacterTagCategory.Face, includeInPrompt: true),
            AuditItem("mole under eye", CharacterTagDecision.Keep, category: CharacterTagCategory.Face, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Full);

        Assert.All(items, item => Assert.Equal(CharacterTagDecision.Keep, item.FinalDecision));
    }

    [Fact]
    public void SparseCanonicalizerPrefersVerifiedColoredHairRibbonAndDeletesGenericOrnaments()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("hair ribbon", CharacterTagDecision.Keep, category: CharacterTagCategory.WearableAccessory, includeInPrompt: true),
            AuditItem("black hair ribbon", CharacterTagDecision.Keep, category: CharacterTagCategory.WearableAccessory, includeInPrompt: true),
            AuditItem("hair ornament", CharacterTagDecision.Keep, category: CharacterTagCategory.WearableAccessory, includeInPrompt: true),
            AuditItem("pom pom hair ornament", CharacterTagDecision.Keep, category: CharacterTagCategory.WearableAccessory, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Sparse);

        Assert.Equal("black hair ribbon", items[0].ReplacementTag);
        Assert.Equal(CharacterTagDecision.Delete, items[2].FinalDecision);
        Assert.Equal(CharacterTagDecision.Delete, items[3].FinalDecision);
        Assert.Equal("black hair ribbon", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void SparseCanonicalizerCollapsesJacketAliasesWithoutInventingColorOrTouchingOtherCharacters()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("jacket", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("open jacket", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("cropped jacket", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("black jacket", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("red jacket", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: false),
            AuditItem("hair ribbon", CharacterTagDecision.Keep, category: CharacterTagCategory.WearableAccessory, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Sparse);

        Assert.Equal("black jacket", items[0].ReplacementTag);
        Assert.Equal("black jacket", items[1].ReplacementTag);
        Assert.Equal("black jacket", items[2].ReplacementTag);
        Assert.Equal(CharacterTagDecision.Keep, items[4].FinalDecision);
        Assert.Empty(items[4].ReplacementTag);
        Assert.Equal(CharacterTagDecision.Keep, items[5].FinalDecision);
        Assert.Empty(items[5].ReplacementTag);
        Assert.DoesNotContain(items, item => item.ReplacementTag.Contains("hair ribbon", StringComparison.Ordinal));
        Assert.Equal("black jacket, hair ribbon", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void FullAndSparseCanonicalizersDropGenericSkirtWhenColoredSkirtExists()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("black skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Full);

        Assert.Equal("black skirt", items[0].ReplacementTag);
        Assert.Equal("black skirt", CharacterTagPromptBuilder.Build(items, string.Empty));

        items = new List<CharacterTagAuditItem>
        {
            AuditItem("skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("black skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true)
        };
        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Sparse);
        Assert.Equal("black skirt", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void FullCanonicalizerKeepsLongHairWhenLlmMarkedKeep()
    {
        var items = new List<CharacterTagAuditItem>
        {
            AuditItem("long hair", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true)
        };

        CharacterTagResultCanonicalizer.Apply(items, CharacterTagAuditStyle.Full);

        Assert.Equal(CharacterTagDecision.Keep, items[0].FinalDecision);
        Assert.Equal("long hair", CharacterTagPromptBuilder.Build(items, string.Empty));
    }

    [Fact]
    public void PromptBuilderDropsGenericGarmentWhenSpecificTagExists()
    {
        var items = new[]
        {
            AuditItem("skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true),
            AuditItem("black skirt", CharacterTagDecision.Keep, category: CharacterTagCategory.Clothing, includeInPrompt: true)
        };

        string prompt = CharacterTagPromptBuilder.Build(items, string.Empty);

        Assert.Equal("black skirt", prompt);
        Assert.DoesNotContain("skirt, black skirt", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilderUsesEffectiveTagsOrderAndForcesTriggerFirst()
    {
        var items = new[]
        {
            AuditItem("black hair", CharacterTagDecision.Keep, includeInPrompt: true, promptOrder: 3),
            AuditItem("trigger", CharacterTagDecision.Keep, category: CharacterTagCategory.Identity, includeInPrompt: true, promptOrder: 9),
            AuditItem("skirt", CharacterTagDecision.Replace, "pink skirt", includeInPrompt: true, promptOrder: 8),
            AuditItem("beach", CharacterTagDecision.Keep, category: CharacterTagCategory.Scene, includeInPrompt: false)
        };

        string prompt = CharacterTagPromptBuilder.Build(items, "trigger");

        Assert.Equal("trigger, black hair, pink skirt", prompt);
    }

    [Fact]
    public void ChisaSparseRegressionBuildsExpectedCorePromptAndKeepsOtherCharacterHairInDataset()
    {
        string trigger = "chisa (peach parfait) (wuthering waves)";
        string[] expected =
        {
            trigger, "1girl", "red eyes", "black hair", "long hair", "low twintails",
            "white hat", "white hairband", "earrings", "pink bikini", "pink skirt", "bracelet"
        };
        List<CharacterTagAuditItem> items = expected.Select((tag, index) => new CharacterTagAuditItem
        {
            Tag = tag,
            Count = 20,
            FinalDecision = CharacterTagDecision.Keep,
            Category = index == 0 || tag == "1girl"
                ? CharacterTagCategory.Identity
                : tag.Contains("hair", StringComparison.Ordinal) || tag.Contains("twintails", StringComparison.Ordinal)
                    ? CharacterTagCategory.Hair
                    : tag.Contains("eyes", StringComparison.Ordinal)
                        ? CharacterTagCategory.Eyes
                        : CharacterTagCategory.Clothing,
            IncludeInPrompt = true,
            PromptOrder = index
        }).ToList();
        items.Add(AuditItem("twintails", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true));
        items.Add(AuditItem("twin braids", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: true));
        items.Add(AuditItem("blonde hair", CharacterTagDecision.Keep, category: CharacterTagCategory.Hair, includeInPrompt: false));
        items.Add(AuditItem("white headwear", CharacterTagDecision.Replace, "white hat", CharacterTagCategory.WearableAccessory));
        items.Add(AuditItem("plaid bikini", CharacterTagDecision.Delete, category: CharacterTagCategory.Clothing));

        CharacterTagResultCanonicalizer.Apply(items);
        string prompt = CharacterTagPromptBuilder.Build(items, trigger);
        IReadOnlyList<string> transformed = CharacterTagTransformation.Apply(
            new[] { "black hair", "blonde hair", "white headwear", "plaid bikini" }, items);

        Assert.Equal(string.Join(", ", expected), prompt);
        Assert.Equal(new[] { "black hair", "blonde hair", "white hat" }, transformed);
    }

    [Fact]
    public async Task ServiceExcludesTagsBelowMinimumCountAndReportsInitialResultBeforeVisualRequest()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(Enumerable.Range(0, 10)
            .Select(index => index < 9
                ? new[] { "trigger", "blue hair", "rare hat" }
                : new[] { "trigger", "blue hair" }));
        var events = new List<string>();
        CharacterTagAuditProgress initialProgress = null;
        var service = new CharacterTagAuditService((request, _) =>
        {
            events.Add("request:" + request.Stage);
            if (request.Stage == CharacterTagAuditStage.VisualReview)
                Assert.NotNull(initialProgress);
            return Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("blue hair", "delete")), string.Empty));
        });
        var progress = new ImmediateProgress<CharacterTagAuditProgress>(value =>
        {
            events.Add("progress:" + value.Stage);
            if (value.Stage == CharacterTagAuditStage.TextScreeningCompleted)
                initialProgress = value;
        });

        CharacterTagAuditResult result = await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            MinimumCount = 10,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        }, progress);

        Assert.Equal(new[] { "trigger", "blue hair" }, result.Items.Select(item => item.Tag));
        Assert.Equal("rare hat", Assert.Single(result.ExcludedItems).Tag);
        Assert.Equal(9, result.ExcludedItems[0].Count);
        Assert.Equal(2, initialProgress.Items.Count);
        Assert.Equal(1, initialProgress.CompletedSteps);
        Assert.Equal(2, initialProgress.TotalSteps);
        Assert.True(events.IndexOf("progress:TextScreeningCompleted") < events.IndexOf("request:VisualReview"));
    }

    [Fact]
    public async Task ServiceReportsIncreasingProgressStepsThroughFullAudit()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "blue hair" } });
        var service = new CharacterTagAuditService((_, _) =>
            Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("blue hair", "keep")), string.Empty)));
        var progressUpdates = new List<CharacterTagAuditProgress>();
        var progress = new ImmediateProgress<CharacterTagAuditProgress>(progressUpdates.Add);

        await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            MinimumCount = 1,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        }, progress);

        Assert.Equal(new[] { 0, 1, 1, 2 }, progressUpdates.Select(item => item.CompletedSteps));
        Assert.All(progressUpdates, item => Assert.Equal(2, item.TotalSteps));
    }

    [Fact]
    public async Task ServiceReportsSingleStepProgressForVisualReviewOnly()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "blue hair" } });
        var service = new CharacterTagAuditService((_, _) =>
            Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("blue hair", "keep")), string.Empty)));
        var progressUpdates = new List<CharacterTagAuditProgress>();
        var progress = new ImmediateProgress<CharacterTagAuditProgress>(progressUpdates.Add);
        var initialItems = new List<CharacterTagAuditItem>
        {
            new CharacterTagAuditItem { Tag = "trigger", FinalDecision = CharacterTagDecision.Keep, Category = CharacterTagCategory.Identity },
            new CharacterTagAuditItem { Tag = "blue hair", FinalDecision = CharacterTagDecision.Keep, Category = CharacterTagCategory.Hair }
        };

        await service.ExecuteVisualReviewAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            MinimumCount = 1,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        }, initialItems, progress);

        Assert.Equal(new[] { 0, 1 }, progressUpdates.Select(item => item.CompletedSteps));
        Assert.All(progressUpdates, item => Assert.Equal(1, item.TotalSteps));
    }

    [Theory]
    [InlineData(CharacterTagAuditStyle.Sparse, "delete incorrect/conflicting and non-core appearance")]
    [InlineData(CharacterTagAuditStyle.Full, "delete only incorrect/conflicting appearance")]
    public async Task ServicePromptStatesSelectedStylePolicy(CharacterTagAuditStyle style, string expectedRule)
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "blue hair" } });
        var prompts = new List<string>();
        var service = new CharacterTagAuditService((request, _) =>
        {
            prompts.Add(request.SystemPrompt);
            return Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("blue hair", "keep")), string.Empty));
        });

        await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            MinimumCount = 1,
            TriggerWord = "trigger",
            Style = style,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        });

        Assert.All(prompts, prompt => Assert.Contains(expectedRule, prompt, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TriggerCandidatesAreSortedByCountThenTagAndKeepLowFrequencyTags()
    {
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[]
        {
            new[] { "zeta", "alpha", "rare" },
            new[] { "zeta", "alpha" },
            new[] { "zeta" }
        });

        IReadOnlyList<CharacterTagTriggerCandidate> candidates = CharacterTagTriggerCandidates.Create(inventory);

        Assert.Equal(new[] { "zeta", "alpha", "rare" }, candidates.Select(item => item.Tag));
        Assert.Equal(new[] { 3, 2, 1 }, candidates.Select(item => item.Count));
    }

    [Theory]
    [InlineData(1200, 600, 1600, 900, 576)]
    [InlineData(1200, 600, 600, 1200, 384)]
    public void PreviewWidthUsesAspectRatioWithinResponsiveBounds(
        int containerWidth, int availableHeight, int imageWidth, int imageHeight, int expected)
    {
        Assert.Equal(expected, CharacterTagPreviewLayout.CalculateWidth(
            containerWidth, availableHeight, imageWidth, imageHeight));
    }

    [Theory]
    [InlineData(180, 200, 236)]
    [InlineData(260, 200, 260)]
    public void ChoiceDropDownWidthFitsLongestLocalizedItem(
        int controlWidth, int longestItemWidth, int expectedWidth)
    {
        Assert.Equal(expectedWidth, CharacterTagChoiceLayout.CalculateDropDownWidth(
            controlWidth, new[] { 80, longestItemWidth }));
    }

    [Fact]
    public async Task ServiceRunsTextThenVisualAndRepairsMalformedResponseOnce()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "smile" } });
        var requests = new List<CharacterTagModelRequest>();
        var responses = new Queue<string>(new[]
        {
            "not json",
            ValidJson(("trigger", "keep"), ("smile", "delete")),
            ValidJson(("trigger", "keep"), ("smile", "uncertain"))
        });
        var service = new CharacterTagAuditService((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(new CharacterTagModelResponse(responses.Dequeue(), string.Empty));
        });

        CharacterTagAuditResult result = await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            MinimumCount = 1,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        });

        Assert.Equal(3, requests.Count);
        Assert.Equal(CharacterTagAuditStage.TextScreening, requests[0].Stage);
        Assert.Equal(CharacterTagAuditStage.Repair, requests[1].Stage);
        Assert.Contains("may correct invalid decisions and replacement targets", requests[1].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(CharacterTagAuditStage.VisualReview, requests[2].Stage);
        Assert.Empty(requests[0].ImagePaths);
        Assert.Equal(referenceImage, Assert.Single(requests[2].ImagePaths));
        Assert.Equal(CharacterTagDecision.Delete, result.Items[1].InitialDecision);
        Assert.Equal(CharacterTagDecision.Uncertain, result.Items[1].FinalDecision);
    }

    [Fact]
    public async Task VisualPromptRequiresColorCheckOfColorlessGarmentTags()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger", "jacket" } });
        var requests = new List<CharacterTagModelRequest>();
        var service = new CharacterTagAuditService((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("jacket", "keep")), string.Empty));
        });

        await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            MinimumCount = 1,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        });

        CharacterTagModelRequest visual = Assert.Single(requests, request => request.Stage == CharacterTagAuditStage.VisualReview);
        Assert.Contains("color-less garment", visual.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("even if that colored tag does not exist anywhere in the inventory", visual.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Never answer replace with an empty replacement_tag", visual.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteVisualReviewRerunsOnlyTheVisualStageWithStoredInitialDecisions()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "second-reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(Enumerable.Range(0, 10)
            .Select(index => index < 9
                ? new[] { "trigger", "jacket", "rare hat" }
                : new[] { "trigger", "jacket" }));
        var requests = new List<CharacterTagModelRequest>();
        var service = new CharacterTagAuditService((request, _) =>
        {
            requests.Add(request);
            return Task.FromResult(new CharacterTagModelResponse(
                ValidJson(("trigger", "keep"), ("jacket", "delete")), string.Empty));
        });
        var initialItems = new List<CharacterTagAuditItem>
        {
            new CharacterTagAuditItem { Tag = "trigger", FinalDecision = CharacterTagDecision.Keep, Category = CharacterTagCategory.Identity },
            new CharacterTagAuditItem { Tag = "jacket", FinalDecision = CharacterTagDecision.Replace, Category = CharacterTagCategory.Clothing, ReplacementTag = "black jacket" }
        };
        var stages = new List<CharacterTagAuditStage>();
        var progress = new ImmediateProgress<CharacterTagAuditProgress>(value => stages.Add(value.Stage));

        CharacterTagAuditResult result = await service.ExecuteVisualReviewAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            TriggerWord = "trigger",
            Style = CharacterTagAuditStyle.Sparse,
            MinimumCount = 10,
            Model = "gemini-test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "auditor rules",
            PromptPyramidSkill = "pyramid rules"
        }, initialItems, progress);

        CharacterTagModelRequest visual = Assert.Single(requests);
        Assert.Equal(CharacterTagAuditStage.VisualReview, visual.Stage);
        Assert.Equal(referenceImage, Assert.Single(visual.ImagePaths));
        Assert.Contains("black jacket", visual.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(new[] { CharacterTagAuditStage.VisualReview, CharacterTagAuditStage.VisualReview }, stages);
        Assert.Equal(CharacterTagDecision.Replace, result.Items.Single(item => item.Tag == "jacket").InitialDecision);
        Assert.Equal(CharacterTagDecision.Delete, result.Items.Single(item => item.Tag == "jacket").FinalDecision);
        Assert.Equal("rare hat", Assert.Single(result.ExcludedItems).Tag);
        Assert.True(result.Metrics.TotalDuration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteVisualReviewRequiresInitialItems()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        var service = new CharacterTagAuditService((_, _) =>
            Task.FromResult(new CharacterTagModelResponse("{}", string.Empty)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteVisualReviewAsync(new CharacterTagAuditOptions
        {
            Inventory = CharacterTagInventory.Create(new[] { new[] { "trigger" } }),
            TriggerWord = "trigger",
            MinimumCount = 1,
            Model = "test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "rules",
            PromptPyramidSkill = "pyramid"
        }, new List<CharacterTagAuditItem>()));
    }

    [Fact]
    public async Task ServiceAggregatesUsageAcrossRepairAndBothStages()
    {
        using var temp = new TemporaryDirectory();
        string referenceImage = Path.Combine(temp.Path, "reference.png");
        File.WriteAllBytes(referenceImage, new byte[] { 1 });
        CharacterTagInventory inventory = CharacterTagInventory.Create(new[] { new[] { "trigger" } });
        var responses = new Queue<CharacterTagModelResponse>(new[]
        {
            new CharacterTagModelResponse("bad", string.Empty, new CharacterTagTokenUsage(10, 2, 12)),
            new CharacterTagModelResponse(ValidJson(("trigger", "keep")), string.Empty, new CharacterTagTokenUsage(4, 1, 5)),
            new CharacterTagModelResponse(ValidJson(("trigger", "keep")), string.Empty, new CharacterTagTokenUsage(20, 3, 23))
        });
        var service = new CharacterTagAuditService((_, _) => Task.FromResult(responses.Dequeue()));

        CharacterTagAuditResult result = await service.ExecuteAsync(new CharacterTagAuditOptions
        {
            Inventory = inventory,
            MinimumCount = 1,
            TriggerWord = "trigger",
            Model = "test",
            ReferenceImagePath = referenceImage,
            CharacterAuditorSkill = "rules",
            PromptPyramidSkill = "pyramid"
        });

        Assert.Equal(34, result.Metrics.InputTokens);
        Assert.Equal(6, result.Metrics.OutputTokens);
        Assert.Equal(40, result.Metrics.TotalTokens);
        Assert.Equal(3, result.Metrics.Requests.Count);
    }

    [Fact]
    public void SkillLoaderReadsBothSkillsFromApplicationRoot()
    {
        using var temp = new TemporaryDirectory();
        string auditor = Path.Combine(temp.Path, "Agent", "skills", "character-tag-auditor");
        string pyramid = Path.Combine(temp.Path, "Agent", "skills", "prompt-pyramid");
        Directory.CreateDirectory(auditor);
        Directory.CreateDirectory(pyramid);
        File.WriteAllText(Path.Combine(auditor, "SKILL.md"), "auditor");
        File.WriteAllText(Path.Combine(pyramid, "SKILL.md"), "pyramid");

        CharacterTagSkillBundle bundle = CharacterTagSkillLoader.Load(temp.Path);

        Assert.Equal("auditor", bundle.CharacterAuditor);
        Assert.Equal("pyramid", bundle.PromptPyramid);
    }

    [Fact]
    public void DeletionPlannerOnlyRemovesRequestedTagsWithoutAddingOrReordering()
    {
        IReadOnlyList<string> result = CharacterTagDeletionPlanner.Remove(
            new[] { "trigger", "blue hair", "smile", "black boots" },
            new[] { "smile", "not present" });

        Assert.Equal(new[] { "trigger", "blue hair", "black boots" }, result);
    }

    private static string ValidJson(params (string Tag, string Decision)[] values)
    {
        return JsonConvert.SerializeObject(new
        {
            tags = values.Select(value => new { tag = value.Tag, decision = value.Decision, category = "hair", reason = "test" })
        });
    }

    private static CharacterTagAuditItem AuditItem(
        string tag,
        CharacterTagDecision decision,
        string replacement = "",
        CharacterTagCategory category = CharacterTagCategory.Clothing,
        bool includeInPrompt = false,
        int promptOrder = 0)
    {
        return new CharacterTagAuditItem
        {
            Tag = tag,
            FinalDecision = decision,
            ReplacementTag = replacement,
            Category = category,
            IncludeInPrompt = includeInPrompt,
            PromptOrder = promptOrder
        };
    }
}

internal sealed class ImmediateProgress<T> : IProgress<T>
{
    private readonly Action<T> report;

    public ImmediateProgress(Action<T> report)
    {
        this.report = report;
    }

    public void Report(T value) => report(value);
}

public sealed class CharacterTagFileTransactionTests
{
    [Fact]
    public async Task CommitWritesAllFilesAndRemovesTransactionDirectory()
    {
        using var temp = new TemporaryDirectory();
        string first = Path.Combine(temp.Path, "a.txt");
        string second = Path.Combine(temp.Path, "nested", "b.txt");
        File.WriteAllText(first, "old-a");

        await CharacterTagFileTransaction.CommitAsync(temp.Path, new[]
        {
            new CharacterTagFileChange(first, "new-a"),
            new CharacterTagFileChange(second, "new-b")
        });

        Assert.Equal("new-a", File.ReadAllText(first));
        Assert.Equal("new-b", File.ReadAllText(second));
        Assert.Empty(Directory.GetDirectories(temp.Path, CharacterTagFileTransaction.DirectoryPrefix + "*"));
    }

    [Fact]
    public async Task FailureRestoresExistingFilesAndDeletesNewFiles()
    {
        using var temp = new TemporaryDirectory();
        string first = Path.Combine(temp.Path, "a.txt");
        string second = Path.Combine(temp.Path, "b.txt");
        File.WriteAllText(first, "old-a");
        int replacements = 0;

        await Assert.ThrowsAsync<IOException>(() => CharacterTagFileTransaction.CommitAsync(
            temp.Path,
            new[] { new CharacterTagFileChange(first, "new-a"), new CharacterTagFileChange(second, "new-b") },
            _ =>
            {
                replacements++;
                if (replacements == 2)
                    throw new IOException("injected");
            }));

        Assert.Equal("old-a", File.ReadAllText(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public async Task RecoveryRestoresIncompleteTransactionFromManifest()
    {
        using var temp = new TemporaryDirectory();
        string target = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(target, "old");
        await Assert.ThrowsAsync<IOException>(() => CharacterTagFileTransaction.CommitAsync(
            temp.Path,
            new[] { new CharacterTagFileChange(target, "new") },
            _ => throw new IOException("injected"),
            preserveTransactionOnFailure: true));

        await CharacterTagFileTransaction.RecoverIncompleteAsync(temp.Path);

        Assert.Equal("old", File.ReadAllText(target));
        Assert.Empty(Directory.GetDirectories(temp.Path, CharacterTagFileTransaction.DirectoryPrefix + "*"));
    }

    [Fact]
    public async Task CommitAsync_reports_progress_for_each_file()
    {
        using var temp = new TemporaryDirectory();
        string first = Path.Combine(temp.Path, "a.txt");
        string second = Path.Combine(temp.Path, "b.txt");
        File.WriteAllText(first, "old-a");
        File.WriteAllText(second, "old-b");

        var progressValues = new List<int>();
        var progress = new Progress<int>(value => progressValues.Add(value));
        await CharacterTagFileTransaction.CommitAsync(
            temp.Path,
            new[]
            {
                new CharacterTagFileChange(first, "new-a"),
                new CharacterTagFileChange(second, "new-b"),
            },
            progress: progress);

        Assert.Equal(new[] { 1, 2 }, progressValues);
    }

    [Fact]
    public void CommitAsync_does_not_rewrite_manifest_during_apply_loop()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "BooruDatasetTagManager",
            "CharacterTagFileTransaction.cs"));

        int applyLoopStart = source.IndexOf("int appliedCount = 0;", StringComparison.Ordinal);
        int applyLoopEnd = source.IndexOf("Directory.Delete(transactionDirectory, true);", applyLoopStart, StringComparison.Ordinal);
        Assert.True(applyLoopStart >= 0);
        Assert.True(applyLoopEnd > applyLoopStart);
        string applyLoop = source.Substring(applyLoopStart, applyLoopEnd - applyLoopStart);
        Assert.DoesNotContain("WriteManifestAsync", applyLoop);
    }

    [Fact]
    public void ApplyAsync_uses_bulk_mutation_and_background_scan()
    {
        string source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "BooruDatasetTagManager",
            "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("await Task.Run(() =>", source);
        Assert.Contains("ExecuteBulkMutation", source);
        Assert.Contains("CharacterTagAuditSaveProgress", source);
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BooruDatasetTagManager.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bdtm-character-audit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, true);
    }
}
