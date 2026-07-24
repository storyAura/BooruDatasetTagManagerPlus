using System.Text;
using BooruDatasetTagManager;
using Newtonsoft.Json;
using Xunit;

namespace BooruDatasetTagManager.Tests;

// Multi-site LLM API profiles: per-site key lists rotate per request, are
// DPAPI-encrypted at rest and never shown in the UI (masked tail only).
public sealed class LlmApiProfileTests
{
    [Fact]
    public void MaskTokenNeverRevealsMoreThanTheTail()
    {
        Assert.Equal(string.Empty, LlmApiProfileLogic.MaskToken(null));
        Assert.Equal(string.Empty, LlmApiProfileLogic.MaskToken(""));
        Assert.Equal("••••", LlmApiProfileLogic.MaskToken("short"));
        Assert.Equal("••••wxyz", LlmApiProfileLogic.MaskToken("sk-abcdefg-wxyz"));
    }

    [Fact]
    public void SanitizeTokensTrimsDedupesAndDropsEmpties()
    {
        var result = LlmApiProfileLogic.SanitizeTokens(new[] { " a ", "a", "", null, "b", "  " });

        Assert.Equal(new List<string> { "a", "b" }, result);
    }

    [Fact]
    public void RotationIndexCyclesRoundRobinAndSurvivesCounterOverflow()
    {
        Assert.Equal(
            new[] { 1, 2, 0, 1 },
            new[] { 1, 2, 3, 4 }.Select(counter => LlmApiProfileLogic.RotationIndex(counter, 3)).ToArray());
        Assert.InRange(LlmApiProfileLogic.RotationIndex(int.MinValue, 3), 0, 2);
        Assert.InRange(LlmApiProfileLogic.RotationIndex(-1, 3), 0, 2);
        Assert.Equal(0, LlmApiProfileLogic.RotationIndex(123, 1));
        Assert.Equal(0, LlmApiProfileLogic.RotationIndex(123, 0));
    }

    [Fact]
    public void LegacyFlatSettingsMigrateIntoOneProfileExactlyOnce()
    {
        var settings = new AppSettings();
        settings.OpenAiAutoTagger.ConnectionAddress = "https://ai.example.com/v1";
        settings.OpenAiAutoTagger.ApiKey = "sk-legacy";
        settings.OpenAiAutoTagger.Model = "text-model";
        settings.OpenAiAutoTagger.VisionModel = "vision-model";
        settings.CharacterTagAuditModel = "audit-model";

        LlmApiProfileLogic.EnsureLegacyProfile(settings);
        LlmApiProfileLogic.EnsureLegacyProfile(settings);

        var profile = Assert.Single(settings.LlmApiProfiles);
        Assert.Equal("ai.example.com", profile.Name);
        Assert.Equal("https://ai.example.com/v1", profile.Endpoint);
        Assert.Equal(new List<string> { "sk-legacy" }, profile.Tokens);
        Assert.Equal("text-model", profile.Model);
        Assert.Equal("vision-model", profile.VisionModel);
        Assert.Equal("audit-model", profile.AuditModel);
        Assert.Equal(0, settings.LlmApiProfileIndex);
    }

    [Fact]
    public void EmptyLegacySettingsCreateNoProfile()
    {
        var settings = new AppSettings();
        settings.OpenAiAutoTagger.ConnectionAddress = string.Empty;
        settings.OpenAiAutoTagger.ApiKey = string.Empty;

        LlmApiProfileLogic.EnsureLegacyProfile(settings);

        Assert.Empty(settings.LlmApiProfiles);
    }

    [Fact]
    public void ApplyActiveProfileMirrorsIntoFlatFieldsAndSanitizesKeys()
    {
        var settings = new AppSettings();
        settings.LlmApiProfiles.Add(new LlmApiProfile
        {
            Name = "a",
            Endpoint = "https://a.example.com/v1",
            Tokens = { "key-a" }
        });
        settings.LlmApiProfiles.Add(new LlmApiProfile
        {
            Name = "b",
            Endpoint = "https://b.example.com/v1",
            Model = "b-model",
            VisionModel = "b-vision",
            AuditModel = "b-audit",
            Tokens = { "key-b1", "key-b2", " key-b1 ", "" }
        });
        settings.LlmApiProfileIndex = 1;

        LlmApiProfileLogic.ApplyActiveProfile(settings);

        Assert.Equal("https://b.example.com/v1", settings.OpenAiAutoTagger.ConnectionAddress);
        Assert.Equal("key-b1", settings.OpenAiAutoTagger.ApiKey);
        Assert.Equal("b-model", settings.OpenAiAutoTagger.Model);
        Assert.Equal("b-vision", settings.OpenAiAutoTagger.VisionModel);
        Assert.Equal("b-audit", settings.CharacterTagAuditModel);
        Assert.Equal(new List<string> { "key-b1", "key-b2" }, settings.GetActiveLlmApiKeys());
    }

    [Fact]
    public void GetActiveLlmApiKeysFallsBackToLegacyFlatKey()
    {
        var settings = new AppSettings();
        settings.OpenAiAutoTagger.ApiKey = "sk-flat";

        Assert.Equal(new List<string> { "sk-flat" }, settings.GetActiveLlmApiKeys());
    }

    [Fact]
    public void TokensSerializeEncryptedAndRoundTrip()
    {
        var profile = new LlmApiProfile { Name = "site", Tokens = { "sk-secret-token-1234" } };

        string json = JsonConvert.SerializeObject(profile);
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain("sk-secret-token-1234", json);

        var restored = JsonConvert.DeserializeObject<LlmApiProfile>(json);
        Assert.Equal(new List<string> { "sk-secret-token-1234" }, restored.Tokens);
    }

    [Fact]
    public void ProfilesRoundTripThroughTheSettingsFile()
    {
        using var temp = new TemporaryDirectory();
        var source = new AppSettings();
        source.LlmApiProfiles.Add(new LlmApiProfile
        {
            Name = "site-a",
            Endpoint = "https://a.example.com/v1",
            Tokens = { "sk-one", "sk-two" }
        });
        source.LlmApiProfiles.Add(new LlmApiProfile { Name = "site-b", Endpoint = "https://b.example.com/v1" });
        source.LlmApiProfileIndex = 1;
        File.WriteAllText(
            Path.Combine(temp.Path, "settings.json"),
            JsonConvert.SerializeObject(source),
            new UTF8Encoding(false));

        var loaded = new AppSettings(temp.Path);

        Assert.Equal(2, loaded.LlmApiProfiles.Count);
        Assert.Equal(new List<string> { "sk-one", "sk-two" }, loaded.LlmApiProfiles[0].Tokens);
        Assert.Equal("site-b", loaded.LlmApiProfiles[1].Name);
        Assert.Equal(1, loaded.LlmApiProfileIndex);
        Assert.Equal(new List<string>(), loaded.GetActiveLlmApiKeys());
    }

    [Fact]
    public void CloneIsIndependentOfTheOriginal()
    {
        var profile = new LlmApiProfile { Name = "a", Tokens = { "k1" } };

        var clone = profile.Clone();
        clone.Tokens.Add("k2");
        clone.Name = "b";

        Assert.Equal(new List<string> { "k1" }, profile.Tokens);
        Assert.Equal("a", profile.Name);
    }
}
