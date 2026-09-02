using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagConsistencyPlannerTests
{
    private static IReadOnlyList<TagConsistencyIssue> Plan(
        IReadOnlyList<string> tags,
        Func<string, bool>? isCharacter = null,
        Dictionary<string, int>? counts = null)
    {
        return TagConsistencyPlanner.Plan(
            new[] { ("img.png", tags) },
            isCharacter ?? (_ => false),
            counts ?? new Dictionary<string, int>());
    }

    [Fact]
    public void LowerSubjectCountLosesToHigherOne()
    {
        var issues = Plan(new[] { "2boys", "1boy", "smile" });

        TagConsistencyIssue issue = Assert.Single(issues);
        Assert.Equal("1boy", issue.RemoveTag);
        Assert.Equal("2boys", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.SubjectCountConflict, issue.Reason);
    }

    [Fact]
    public void AllLowerCountsAreRemovedAndGendersStayIndependent()
    {
        var issues = Plan(new[] { "6+girls", "2girls", "1girl", "1boy" });

        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.Equal("6+girls", issue.KeptTag));
        Assert.Contains(issues, issue => issue.RemoveTag == "2girls");
        Assert.Contains(issues, issue => issue.RemoveTag == "1girl");
        // 1boy is the only boy-count: no conflict there.
        Assert.DoesNotContain(issues, issue => issue.RemoveTag == "1boy");
    }

    [Fact]
    public void SoloIsRemovedWhenSubjectsProveTwoOrMore()
    {
        TagConsistencyIssue byCount = Assert.Single(
            Plan(new[] { "solo", "2girls" }));
        Assert.Equal("solo", byCount.RemoveTag);
        Assert.Equal(TagConsistencyReason.SoloWithMultipleSubjects, byCount.Reason);

        // 1girl + 1boy sums to two subjects even without a multi-count tag.
        TagConsistencyIssue bySum = Assert.Single(
            Plan(new[] { "solo", "1girl", "1boy" }));
        Assert.Equal("solo", bySum.RemoveTag);

        // A single subject keeps its solo; "solo focus" is a different tag
        // and must never be touched.
        Assert.Empty(Plan(new[] { "solo", "1girl", "smile" }));
        Assert.Empty(Plan(new[] { "solo focus", "2girls", "1boy", "2boys" })
            .Where(issue => issue.RemoveTag == "solo focus"));
    }

    [Fact]
    public void CharacterVariantResolvedByDatasetCounts()
    {
        Func<string, bool> isCharacter = tag => tag.StartsWith("hatsune miku");
        var counts = new Dictionary<string, int>
        {
            ["hatsune miku"] = 3,
            ["hatsune miku (append)"] = 40
        };

        TagConsistencyIssue issue = Assert.Single(Plan(
            new[] { "hatsune miku", "hatsune miku (append)", "smile" }, isCharacter, counts));
        Assert.Equal("hatsune miku", issue.RemoveTag);
        Assert.Equal("hatsune miku (append)", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.CharacterVariantConflict, issue.Reason);

        // Reversed dataset evidence keeps the parent instead.
        counts["hatsune miku"] = 50;
        TagConsistencyIssue reversed = Assert.Single(Plan(
            new[] { "hatsune miku", "hatsune miku (append)" }, isCharacter, counts));
        Assert.Equal("hatsune miku (append)", reversed.RemoveTag);
    }

    [Fact]
    public void CharacterVariantTiePrefersTheMoreSpecificTag()
    {
        Func<string, bool> isCharacter = tag => tag.StartsWith("surtr");

        TagConsistencyIssue issue = Assert.Single(Plan(
            new[] { "surtr (arknights)", "surtr (colorful wonderland) (arknights)" },
            isCharacter));
        Assert.Equal("surtr (arknights)", issue.RemoveTag);
        Assert.Equal("surtr (colorful wonderland) (arknights)", issue.KeptTag);
    }

    [Fact]
    public void NonCharacterTagsAndUnrelatedCharactersAreLeftAlone()
    {
        // Parenthesized general tags never enter the family grouping.
        Assert.Empty(Plan(new[] { "watercolor (medium)", "watercolor (style) (medium)" }));

        // Two different characters share no base name: no conflict.
        Assert.Empty(Plan(
            new[] { "hatsune miku", "kagamine rin" },
            tag => true,
            new Dictionary<string, int>()));
    }

    [Fact]
    public void ParentRelationGroupsRenamedVariantsAndDatasetCountsDecide()
    {
        // "racing miku" shares no base name with "hatsune miku" — only the
        // relation data can pair them.
        Func<string, bool> isCharacter = tag => tag is "hatsune miku" or "racing miku";
        Func<string, string?> parentOf = tag =>
            tag == "racing miku" ? "hatsune miku" : null;
        var counts = new Dictionary<string, int> { ["hatsune miku"] = 2, ["racing miku"] = 30 };

        TagConsistencyIssue issue = Assert.Single(TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "hatsune miku", "racing miku" }) },
            isCharacter, counts, parentOf!));
        Assert.Equal("hatsune miku", issue.RemoveTag);
        Assert.Equal("racing miku", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.CharacterVariantConflict, issue.Reason);
    }

    [Fact]
    public void WithRelationDataSameBaseNameAloneNoLongerConflicts()
    {
        // Different characters merely sharing a short base name ("surtr
        // (arknights)" vs "surtr (ark order)") must stay untouched once
        // authoritative relations are available and record no link.
        var issues = TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "surtr (arknights)", "surtr (ark order)" }) },
            tag => true,
            new Dictionary<string, int>(),
            _ => null);

        Assert.Empty(issues);
    }

    [Fact]
    public void ParentChainClimbsToTheRootAndSurvivesCycles()
    {
        // grandchild → child → root: all three group into one family.
        Func<string, string?> chain = tag => tag switch
        {
            "c" => "b",
            "b" => "a",
            _ => null
        };
        var issues = TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "a", "b", "c" }) },
            tag => true,
            new Dictionary<string, int> { ["a"] = 9 },
            chain!);
        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.Equal("a", issue.KeptTag));

        // A cycle in broken relation data must terminate, not hang.
        Func<string, string?> cycle = tag => tag == "x" ? "y" : tag == "y" ? "x" : null;
        TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "x", "y" }) },
            tag => true,
            new Dictionary<string, int>(),
            cycle!);
    }

    private static IReadOnlyList<TagConsistencyIssue> PlanMiku(
        IReadOnlyList<string> tags, Dictionary<string, int> counts, int threshold)
    {
        // racing miku → hatsune miku; snow miku → hatsune miku.
        Func<string, string?> parentOf = tag =>
            tag is "racing miku" or "snow miku" ? "hatsune miku" : null;
        return TagConsistencyPlanner.Plan(
            new[] { ("img.png", tags) },
            tag => tag.EndsWith("miku"),
            counts,
            parentOf!,
            threshold);
    }

    [Fact]
    public void ChildBelowThresholdFoldsIntoThePresentParent()
    {
        // The child even outnumbers the parent (20 vs 5), but 20 < 30 means
        // it is not trusted: the parent wins anyway.
        var counts = new Dictionary<string, int> { ["hatsune miku"] = 5, ["racing miku"] = 20 };

        TagConsistencyIssue issue = Assert.Single(PlanMiku(
            new[] { "hatsune miku", "racing miku" }, counts, threshold: 30));
        Assert.Equal("racing miku", issue.RemoveTag);
        Assert.Equal("hatsune miku", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.ChildBelowThreshold, issue.Reason);
    }

    [Fact]
    public void ChildAtOrAboveThresholdStillWinsByDatasetCount()
    {
        var counts = new Dictionary<string, int> { ["hatsune miku"] = 5, ["racing miku"] = 40 };

        TagConsistencyIssue issue = Assert.Single(PlanMiku(
            new[] { "hatsune miku", "racing miku" }, counts, threshold: 30));
        Assert.Equal("hatsune miku", issue.RemoveTag);
        Assert.Equal("racing miku", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.CharacterVariantConflict, issue.Reason);
    }

    [Fact]
    public void LoneRareChildIsFoldedIntoItsAbsentParent()
    {
        var counts = new Dictionary<string, int> { ["racing miku"] = 20 };

        TagConsistencyIssue issue = Assert.Single(PlanMiku(
            new[] { "racing miku" }, counts, threshold: 30));
        Assert.Equal("racing miku", issue.RemoveTag);
        Assert.Equal("hatsune miku", issue.KeptTag);
        Assert.Equal(TagConsistencyReason.ChildBelowThreshold, issue.Reason);

        // A trusted lone child stays untouched.
        Assert.Empty(PlanMiku(new[] { "racing miku" },
            new Dictionary<string, int> { ["racing miku"] = 30 }, threshold: 30));
    }

    [Fact]
    public void RareSiblingsBothFoldIntoTheSharedParent()
    {
        var counts = new Dictionary<string, int> { ["racing miku"] = 12, ["snow miku"] = 8 };

        var issues = PlanMiku(new[] { "racing miku", "snow miku" }, counts, threshold: 30);

        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue =>
        {
            Assert.Equal("hatsune miku", issue.KeptTag);
            Assert.Equal(TagConsistencyReason.ChildBelowThreshold, issue.Reason);
        });
    }

    [Fact]
    public void ThresholdZeroDisablesTheFoldRule()
    {
        var counts = new Dictionary<string, int> { ["hatsune miku"] = 5, ["racing miku"] = 20 };

        TagConsistencyIssue issue = Assert.Single(PlanMiku(
            new[] { "hatsune miku", "racing miku" }, counts, threshold: 0));
        Assert.Equal("hatsune miku", issue.RemoveTag);
        Assert.Equal("racing miku", issue.KeptTag);
    }

    [Fact]
    public void ThresholdZeroLeavesALoneCostumeChildUntouched()
    {
        Func<string, string?> parentOf = tag =>
            tag == "kayoko (dress) (blue archive)" ? "kayoko (blue archive)" : null;
        var issues = TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "kayoko (dress) (blue archive)" }) },
            _ => true,
            new Dictionary<string, int> { ["kayoko (dress) (blue archive)"] = 5 },
            parentOf!,
            childCountThreshold: 0);

        Assert.Empty(issues);
    }

    [Fact]
    public void FoldClimbsPastRareAncestorsToTheFirstTrustedOne()
    {
        // grandchild → mid → root; mid is itself too rare, so the fold lands
        // on the root directly.
        Func<string, string?> chain = tag => tag switch
        {
            "grandchild" => "mid",
            "mid" => "root",
            _ => null
        };
        var counts = new Dictionary<string, int> { ["grandchild"] = 2, ["mid"] = 1, ["root"] = 100 };

        TagConsistencyIssue issue = Assert.Single(TagConsistencyPlanner.Plan(
            new[] { ("img.png", (IReadOnlyList<string>)new[] { "grandchild" }) },
            tag => true,
            counts,
            chain!,
            childCountThreshold: 30));
        Assert.Equal("grandchild", issue.RemoveTag);
        Assert.Equal("root", issue.KeptTag);
    }

    [Fact]
    public void FixCharacterVariantsOffLeavesCharacterTagsAlone()
    {
        Func<string, bool> isCharacter = tag => tag.EndsWith("miku");
        Func<string, string?> parentOf = tag =>
            tag is "racing miku" or "snow miku" ? "hatsune miku" : null;
        var counts = new Dictionary<string, int>
        {
            ["hatsune miku"] = 5,
            ["racing miku"] = 20
        };

        var issues = TagConsistencyPlanner.Plan(
            new[]
            {
                ("img.png", (IReadOnlyList<string>)new[]
                {
                    "1boy", "2boys", "solo", "hatsune miku", "racing miku"
                })
            },
            isCharacter,
            counts,
            parentOf!,
            childCountThreshold: 30,
            fixCharacterVariants: false);

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, issue => issue.RemoveTag == "1boy"
            && issue.Reason == TagConsistencyReason.SubjectCountConflict);
        Assert.Contains(issues, issue => issue.RemoveTag == "solo"
            && issue.Reason == TagConsistencyReason.SoloWithMultipleSubjects);
        Assert.DoesNotContain(issues, issue =>
            issue.Reason == TagConsistencyReason.CharacterVariantConflict
            || issue.Reason == TagConsistencyReason.ChildBelowThreshold);
    }

    [Fact]
    public void BaseNameStripsAllTrailingQualifiers()
    {
        Assert.Equal("surtr", TagConsistencyPlanner.GetCharacterBaseName(
            "surtr (colorful wonderland) (arknights)"));
        Assert.Equal("hatsune miku", TagConsistencyPlanner.GetCharacterBaseName("hatsune miku"));
        // A fully parenthesized tag has nothing to strip.
        Assert.Equal("(o)_(o)", TagConsistencyPlanner.GetCharacterBaseName("(o)_(o)"));
    }
}
