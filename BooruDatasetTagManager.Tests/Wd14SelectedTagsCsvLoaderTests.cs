using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class Wd14SelectedTagsCsvLoaderTests
{
    [Fact]
    public void ParseLines_reads_v3_tag_id_format()
    {
        string[] lines =
        {
            "tag_id,name,category,count",
            "9999999,general,9,1589178",
            "470575,1girl,0,5113288",
            "123456,hatsune_miku,4,99999",
        };

        var labels = SelectedTagsCsvLoader.ParseLines(lines);

        Assert.Equal(3, labels.Count);
        Assert.Equal(("general", 9), labels[0]);
        Assert.Equal(("1girl", 0), labels[1]);
        Assert.Equal(("hatsune_miku", 4), labels[2]);
    }

    [Fact]
    public void ParseLines_reads_v2_name_format()
    {
        string[] lines =
        {
            "name,category,count",
            "1girl,0,12345",
            "solo,0,67890",
        };

        var labels = SelectedTagsCsvLoader.ParseLines(lines);

        Assert.Equal(2, labels.Count);
        Assert.Equal(("1girl", 0), labels[0]);
        Assert.Equal(("solo", 0), labels[1]);
    }
}
