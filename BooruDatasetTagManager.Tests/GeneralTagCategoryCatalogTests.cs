using System.Text;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class GeneralTagCategoryCatalogTests
{
    private static GeneralTagCategoryCatalog Load(params string[] rows)
    {
        using var temp = new TemporaryDirectory();
        string path = Path.Combine(temp.Path, "danbooru_dataset_general.csv");
        var content = new StringBuilder(
            "tag,category,other_names,copyright,level,parent_tag,post_count,category_l1,category_l2,wiki_url\r\n");
        foreach (string row in rows)
            content.Append(row).Append("\r\n");
        File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
        return GeneralTagCategoryCatalog.LoadFromFile(path);
    }

    [Fact]
    public void MatchesNormalizedUnderscoreAndSpaceTags()
    {
        GeneralTagCategoryCatalog catalog = Load(
            "long_hair,general,,,,,1,头发,发长,",
            "green_eyes,general,,,,,1,眼睛,瞳色,");

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet("long_hair", out TagCategoryPath hair));
        Assert.Equal("头发", hair.L1);
        Assert.Equal("发长", hair.L2);
        Assert.True(catalog.TryGet("long hair", out TagCategoryPath spaced));
        Assert.Equal(hair, spaced);
        Assert.True(catalog.TryGet("Green_Eyes", out TagCategoryPath eyes));
        Assert.Equal("眼睛", eyes.L1);
        Assert.Equal("瞳色", eyes.L2);
    }

    [Fact]
    public void ParsesQuotedTagAndEmptySecondary()
    {
        GeneralTagCategoryCatalog catalog = Load(
            "\"\"\"friends\"\"_(meme)\",general,,,,,1,作品,梗,",
            "hatsune_miku_(cosplay),general,,,,,1,cosplay,,");

        Assert.True(catalog.TryGet("\"friends\" (meme)", out TagCategoryPath meme));
        Assert.Equal("作品", meme.L1);
        Assert.Equal("梗", meme.L2);
        Assert.True(catalog.TryGet("hatsune_miku_(cosplay)", out TagCategoryPath cosplay));
        Assert.Equal("cosplay", cosplay.L1);
        Assert.False(cosplay.HasSecondary);
        Assert.Empty(catalog.SecondariesOf("cosplay"));
    }

    [Fact]
    public void ListsPrimariesAndSecondaries()
    {
        GeneralTagCategoryCatalog catalog = Load(
            "long_hair,general,,,,,1,头发,发长,",
            "twintails,general,,,,,1,头发,发型,",
            "white_shirt,general,,,,,1,服装,上衣,");

        Assert.Equal(new[] { "头发", "服装" }, catalog.PrimaryCategories);
        Assert.Equal(new[] { "发型", "发长" }, catalog.SecondariesOf("头发"));
        Assert.Empty(catalog.SecondariesOf("角色"));
    }

    [Fact]
    public void SearchCategoriesFindsSecondaryByName()
    {
        GeneralTagCategoryCatalog catalog = Load(
            "long_hair,general,,,,,1,头发,发长,",
            "blue_hair,general,,,,,1,头发,发色,",
            "green_eyes,general,,,,,1,眼睛,瞳色,");

        IReadOnlyList<TagCategoryPath> hits = catalog.SearchCategories("发色");
        Assert.Contains(hits, path => path.Equals(new TagCategoryPath("头发", "发色")));
        Assert.DoesNotContain(hits, path => path.L1 == "眼睛");
    }

    [Fact]
    public void MissingFileYieldsEmptyCatalog()
    {
        Assert.Equal(0, GeneralTagCategoryCatalog.LoadFromFile(
            Path.Combine(Path.GetTempPath(), "definitely_missing_general_cats.csv")).Count);
        Assert.Equal(0, GeneralTagCategoryCatalog.LoadFromFile(null).Count);
    }

    [Fact]
    public void ClassifyPrefersCsvThenIdentityThenGeneral()
    {
        GeneralTagCategoryCatalog catalog = Load(
            "looking_at_viewer,general,,,,,1,构图,视角,",
            "long_hair,general,,,,,1,头发,发长,");
        var characters = CharacterTagCatalog.LoadFromFile(null);

        TagCategoryPath look = TagCategoryTaxonomy.Classify(
            "looking at viewer", -1, catalog, characters);
        Assert.Equal("构图", look.L1);
        Assert.Equal("视角", look.L2);

        TagCategoryPath character = TagCategoryTaxonomy.Classify(
            "nakamachi arale", 4, catalog, characters);
        Assert.Equal(TagCategoryTaxonomy.Character, character.L1);

        TagCategoryPath unknown = TagCategoryTaxonomy.Classify(
            "totally_made_up_tag", -1, catalog, characters);
        Assert.Equal(TagCategoryPath.General, unknown);
    }

    [Fact]
    public void TryParseFilterAcceptsChineseAndEnumAliases()
    {
        Assert.True(TagCategoryTaxonomy.TryParseFilter("头发", out TagCategoryPath hair));
        Assert.Equal("头发", hair.L1);
        Assert.True(TagCategoryTaxonomy.TryParseFilter("Hair", out TagCategoryPath hairEn));
        Assert.Equal(hair, hairEn);
        Assert.True(TagCategoryTaxonomy.TryParseFilter("头发/发色", out TagCategoryPath l2));
        Assert.Equal("头发", l2.L1);
        Assert.Equal("发色", l2.L2);
        Assert.True(l2.HasSecondary);
        Assert.False(TagCategoryTaxonomy.TryParseFilter("not-a-category", out _));
    }

    [Fact]
    public void FilterMatchTreatsPrimaryAsAllSecondaries()
    {
        var hair = new TagCategoryPath("头发");
        var color = new TagCategoryPath("头发", "发色");
        var length = new TagCategoryPath("头发", "发长");
        var eyes = new TagCategoryPath("眼睛", "瞳色");

        Assert.True(color.Matches(hair));
        Assert.True(length.Matches(hair));
        Assert.False(eyes.Matches(hair));
        Assert.True(color.Matches(color));
        Assert.False(length.Matches(color));
    }

    [Fact]
    public void ToggleInAddsRemovesAndCollapsesSamePrimary()
    {
        var selected = new List<TagCategoryPath>();
        var hair = new TagCategoryPath("头发");
        var color = new TagCategoryPath("头发", "发色");
        var eyes = new TagCategoryPath("眼睛");

        TagCategoryPath.ToggleIn(selected, hair);
        TagCategoryPath.ToggleIn(selected, eyes);
        Assert.Equal(2, selected.Count);
        Assert.True(color.MatchesAny(selected));
        Assert.True(new TagCategoryPath("眼睛", "瞳色").MatchesAny(selected));

        TagCategoryPath.ToggleIn(selected, color);
        Assert.DoesNotContain(hair, selected);
        Assert.Contains(color, selected);
        Assert.Contains(eyes, selected);
        Assert.False(new TagCategoryPath("头发", "发长").MatchesAny(selected));

        TagCategoryPath.ToggleIn(selected, color);
        Assert.DoesNotContain(color, selected);
        Assert.Single(selected);
        Assert.Equal(eyes, selected[0]);
    }

    [Fact]
    public void ToggleInSyncsPrimaryWithAllSecondaries()
    {
        var selected = new List<TagCategoryPath>();
        var animal = new TagCategoryPath("动物");
        string[] siblings = { "动物", "幻想生物", "狗", "猫", "马", "鱼", "鸟" };
        var bird = new TagCategoryPath("动物", "鸟");
        var cat = new TagCategoryPath("动物", "猫");

        TagCategoryPath.ToggleIn(selected, animal, siblings);
        Assert.True(TagCategoryPath.HasWholePrimary(selected, "动物"));
        Assert.True(TagCategoryPath.AllSecondariesSelected(selected, "动物", siblings));

        TagCategoryPath.ToggleIn(selected, bird, siblings);
        Assert.False(TagCategoryPath.HasWholePrimary(selected, "动物"));
        Assert.DoesNotContain(bird, selected);
        Assert.Contains(cat, selected);
        Assert.Equal(siblings.Length - 1, selected.Count);

        TagCategoryPath.ToggleIn(selected, bird, siblings);
        Assert.True(TagCategoryPath.HasWholePrimary(selected, "动物"));
        Assert.Single(selected);
        Assert.Equal(animal, selected[0]);
    }

    [Fact]
    public void MatchesAnyEmptyFilterPassesEveryTag()
    {
        var tag = new TagCategoryPath("头发", "发色");
        Assert.True(tag.MatchesAny(Array.Empty<TagCategoryPath>()));
        Assert.True(tag.MatchesAny(null));
    }

    [Fact]
    public void CosplayHasAnAccent()
    {
        Assert.NotNull(TagSemanticClassifier.GetAccent(TagSemanticCategory.Cosplay));
    }

    [Fact]
    public void ShippedCatalogMapsLongHairAndHasSecondaries()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        string path = null;
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "BooruDatasetTagManager", "Data", "danbooru_dataset_general.csv");
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
            directory = directory.Parent;
        }
        Assert.False(string.IsNullOrEmpty(path), "Data/danbooru_dataset_general.csv should ship in the repo.");

        GeneralTagCategoryCatalog catalog = GeneralTagCategoryCatalog.LoadFromFile(path);
        Assert.True(catalog.Count > 100000);
        Assert.True(catalog.TryGet("long_hair", out TagCategoryPath hair));
        Assert.Equal("头发", hair.L1);
        Assert.Equal("发长", hair.L2);
        Assert.Contains("发色", catalog.SecondariesOf("头发"));
        Assert.Empty(catalog.SecondariesOf("cosplay"));
    }
}
