using System.Text;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

// CFG-01: syntactically valid but structurally incomplete settings files used
// to crash the process before the message loop ever started, and the .bak kept
// by every atomic save was never consulted for recovery.
public sealed class AppSettingsRobustnessTests
{
    [Theory]
    [InlineData("{\"Hotkeys\":{\"Items\":null}}")]
    [InlineData("{\"Hotkeys\":{\"Items\":[null]}}")]
    public void PoisonHotkeySettingsDoNotCrashStartupLoad(string json)
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), json, new UTF8Encoding(false));

        var settings = new AppSettings(temp.Path);

        Assert.NotNull(settings.Hotkeys);
    }

    [Fact]
    public void OutOfRangeNumericSettingsFallBackToDefaults()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            "{\"GridViewRowHeight\":-5,\"PreviewSize\":0,\"TagImagesGridSize\":-1}",
            new UTF8Encoding(false));

        var settings = new AppSettings(temp.Path);

        Assert.Equal(29, settings.GridViewRowHeight);
        Assert.Equal(130, settings.PreviewSize);
        Assert.Equal(400, settings.TagImagesGridSize);
    }

    [Fact]
    public void CorruptSettingsRecoverFromBakBeforeFallingBackToDefaults()
    {
        using var temp = new TemporaryDirectory();
        string settingsFile = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(settingsFile, "{ this is not json", new UTF8Encoding(false));
        File.WriteAllText(settingsFile + ".bak", "{\"Language\":\"ru-RU\"}", new UTF8Encoding(false));

        var settings = new AppSettings(temp.Path);

        Assert.Equal("ru-RU", settings.Language);
        Assert.True(File.Exists(settingsFile + ".corrupt"));
    }

    [Fact]
    public void CorruptSettingsWithoutBakFallBackToDefaults()
    {
        using var temp = new TemporaryDirectory();
        string settingsFile = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(settingsFile, "{ this is not json", new UTF8Encoding(false));

        var settings = new AppSettings(temp.Path);

        Assert.NotNull(settings.Hotkeys);
        Assert.True(File.Exists(settingsFile + ".corrupt"));
    }
}
