using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class ReleaseNotesLocalizerTests
{
    private const string DualBody =
        "<!-- lang:zh-CN -->\n# 中文说明\n- 修复了问题\n\n<!-- lang:en -->\n# English notes\n- Fixed a bug\n";

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void ChineseUiLanguagesGetTheChineseSection(string language)
    {
        string result = ReleaseNotesLocalizer.SelectSection(DualBody, language);
        Assert.Contains("修复了问题", result);
        Assert.DoesNotContain("Fixed a bug", result);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("pt-BR")]
    [InlineData(null)]
    public void OtherUiLanguagesGetTheEnglishSection(string language)
    {
        string result = ReleaseNotesLocalizer.SelectSection(DualBody, language);
        Assert.Contains("Fixed a bug", result);
        Assert.DoesNotContain("修复了问题", result);
    }

    [Fact]
    public void BodyWithoutMarkersPassesThroughWhole()
    {
        string body = "v1.2.0 notes without any markers\n- item";
        Assert.Equal(body.Trim(), ReleaseNotesLocalizer.SelectSection(body, "zh-CN"));
    }

    [Fact]
    public void MissingRequestedLanguageFallsBackToEnglishThenFirstSection()
    {
        string englishOnly = "<!-- lang:en -->\nEnglish only body";
        Assert.Contains("English only body", ReleaseNotesLocalizer.SelectSection(englishOnly, "zh-CN"));

        string russianOnly = "<!-- lang:ru-RU -->\nТолько русский";
        Assert.Contains("Только русский", ReleaseNotesLocalizer.SelectSection(russianOnly, "zh-CN"));
    }

    [Fact]
    public void EmptyBodyReturnsEmpty()
    {
        Assert.Equal(string.Empty, ReleaseNotesLocalizer.SelectSection(null, "zh-CN"));
        Assert.Equal(string.Empty, ReleaseNotesLocalizer.SelectSection("   ", "en-US"));
    }

    [Fact]
    public void SelectedSectionIsCappedAfterSelectionNotBefore()
    {
        // The zh section is huge; the en section must still come out intact,
        // and an over-long selected section is capped with an ellipsis.
        string longZh = new string('长', 6000);
        string body = $"<!-- lang:zh-CN -->\n{longZh}\n<!-- lang:en -->\nShort English section";

        string english = ReleaseNotesLocalizer.SelectSection(body, "en-US");
        Assert.Equal("Short English section", english);

        string chinese = ReleaseNotesLocalizer.SelectSection(body, "zh-CN");
        Assert.EndsWith("...", chinese);
        Assert.True(chinese.Length < 4100);
    }
}
