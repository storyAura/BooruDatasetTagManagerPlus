using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class TagNearSynonymIndexTests
{
    private static TagNearSynonymIndex Load(params string[] rows)
    {
        using var reader = new StringReader("tag,near_synonyms\n" + string.Join("\n", rows) + "\n");
        return TagNearSynonymIndex.LoadFromReader(reader);
    }

    [Fact]
    public void ParsesQuotedMultiValueRowsAndNormalizesUnderscores()
    {
        TagNearSynonymIndex index = Load(
            "hair_bow,\"hair_ribbon, hairband, bow, hair_ornament\"",
            "4th_match_flame_(e.g.o),e.g.o_(project_moon)",
            "bow,hair_bow");

        Assert.Equal(3, index.Count);
        Assert.Equal(new[] { "bow", "hair ornament", "hair ribbon", "hairband" }, index.RelatedTo("hair bow"));
        Assert.Equal(new[] { "e.g.o (project moon)" }, index.RelatedTo("4th match flame \\(e.g.o\\)"));
        Assert.Empty(index.RelatedTo("unknown"));
        Assert.Empty(index.RelatedTo(null));
    }

    [Fact]
    public void AreRelatedMatchesEitherDirectionAndNeverSelf()
    {
        TagNearSynonymIndex index = Load("elbow_gloves,gloves", "choker,\"collar, scarf\"");

        Assert.True(index.AreRelated("elbow gloves", "gloves"));
        Assert.True(index.AreRelated("gloves", "elbow_gloves"));
        Assert.True(index.AreRelated("scarf", "choker"));
        Assert.False(index.AreRelated("gloves", "gloves"));
        Assert.False(index.AreRelated("gloves", "choker"));
        Assert.False(TagNearSynonymIndex.Empty.AreRelated("elbow gloves", "gloves"));
    }

    [Fact]
    public void ClustersAreConnectedComponentsOfAtLeastTwoMembersInInputOrder()
    {
        TagNearSynonymIndex index = Load(
            "hair_bow,\"hair_ribbon, hairband, bow\"",
            "bow,\"hair_bow, ribbon, hair_ribbon\"",
            "hair_ribbon,\"hairband, hair_ornament\"",
            "elbow_gloves,gloves",
            "black_gloves,gloves");

        IReadOnlyList<IReadOnlyList<string>> clusters = index.Clusters(new[]
        {
            "bow", "hair ornament", "hair ribbon", "hairband", "black gloves", "elbow gloves", "gloves", "black dress", "black dress"
        });

        // black gloves and elbow gloves are only related THROUGH gloves, so
        // the component forms only when gloves is present too.
        Assert.Equal(2, clusters.Count);
        Assert.Equal(new[] { "bow", "hair ornament", "hair ribbon", "hairband" }, clusters[0]);
        Assert.Equal(new[] { "black gloves", "elbow gloves", "gloves" }, clusters[1]);
        Assert.Single(index.Clusters(new[] { "bow", "hair ribbon", "black gloves", "elbow gloves" }));
    }

    [Fact]
    public void ClustersHonorTheExcludedPairPredicateAndEmptyInputs()
    {
        TagNearSynonymIndex index = Load("elbow_gloves,gloves", "black_gloves,gloves");

        Assert.Empty(index.Clusters(new[] { "black gloves", "elbow gloves" }, (a, b) => true));
        Assert.Empty(index.Clusters(new[] { "black gloves" }));
        Assert.Empty(index.Clusters(null));
        Assert.Empty(TagNearSynonymIndex.Empty.Clusters(new[] { "black gloves", "elbow gloves" }));
        Assert.Equal(0, TagNearSynonymIndex.LoadFromReader(null).Count);
        Assert.Equal(0, TagNearSynonymIndex.LoadFromFile(Path.Combine(Path.GetTempPath(), "missing-near-synonyms.csv")).Count);
    }

    [Fact]
    public void ShippedRelationFileLoadsAndLinksHairAccessories()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        string path = null;
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "BooruDatasetTagManager", "Data", "danbooru_tag_near_synonyms.csv");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
            directory = directory.Parent;
        }
        Assert.False(string.IsNullOrEmpty(path), "Data/danbooru_tag_near_synonyms.csv should ship in the repo.");

        TagNearSynonymIndex index = TagNearSynonymIndex.LoadFromFile(path);

        Assert.True(index.Count > 7000);
        Assert.True(index.AreRelated("hair ribbon", "hairband"));
        Assert.True(index.AreRelated("bow", "hair bow"));
        Assert.True(index.AreRelated("black gloves", "gloves"));
        Assert.True(index.AreRelated("earrings", "jewelry"));
        Assert.False(index.AreRelated("hair ribbon", "black thighhighs"));
    }
}
