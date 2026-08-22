using System.Text;
using BooruDatasetTagManager;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class AppSettingsPathTests
{
    [Fact]
    public void Resolve_usesDocumentsDirWhenNoLegacyFileExists()
    {
        using var startup = new TemporaryDirectory();
        using var documents = new TemporaryDirectory();

        string resolved = AppSettings.ResolveUserSettingsDirectory(startup.Path, documents.Path);

        Assert.Equal(documents.Path, resolved);
        Assert.False(File.Exists(Path.Combine(documents.Path, AppSettings.SettingsFileName)));
    }

    [Fact]
    public void Resolve_copiesLegacySettingsAndBakWhenDocumentsFileIsMissing()
    {
        using var startup = new TemporaryDirectory();
        using var documents = new TemporaryDirectory();
        string legacy = Path.Combine(startup.Path, AppSettings.SettingsFileName);
        File.WriteAllText(legacy, "{\"Language\":\"zh-CN\"}", new UTF8Encoding(false));
        File.WriteAllText(legacy + ".bak", "{\"Language\":\"en-US\"}", new UTF8Encoding(false));

        string resolved = AppSettings.ResolveUserSettingsDirectory(startup.Path, documents.Path);

        string dest = Path.Combine(documents.Path, AppSettings.SettingsFileName);
        Assert.Equal(documents.Path, resolved);
        Assert.True(File.Exists(dest));
        Assert.Equal("{\"Language\":\"zh-CN\"}", File.ReadAllText(dest));
        Assert.True(File.Exists(dest + ".bak"));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void Resolve_doesNotOverwriteExistingDocumentsSettings()
    {
        using var startup = new TemporaryDirectory();
        using var documents = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(startup.Path, AppSettings.SettingsFileName), "legacy", new UTF8Encoding(false));
        string dest = Path.Combine(documents.Path, AppSettings.SettingsFileName);
        File.WriteAllText(dest, "documents", new UTF8Encoding(false));

        string resolved = AppSettings.ResolveUserSettingsDirectory(startup.Path, documents.Path);

        Assert.Equal(documents.Path, resolved);
        Assert.Equal("documents", File.ReadAllText(dest));
    }

    [Fact]
    public void Resolve_fallsBackToStartupWhenDocumentsDirIsBlank()
    {
        using var startup = new TemporaryDirectory();

        string resolved = AppSettings.ResolveUserSettingsDirectory(startup.Path, documentsDir: "   ");

        Assert.Equal(startup.Path, resolved);
    }

    [Fact]
    public void Resolve_mergesRecognizedLegacyApiWhenDocumentsFileHasNone()
    {
        using var startup = new TemporaryDirectory();
        using var documents = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(startup.Path, AppSettings.SettingsFileName),
            """
            {
              "Language": "en-US",
              "OpenAiAutoTagger": {
                "ConnectionAddress": "https://api.example.com/v1",
                "ApiKey": "sk-legacy-key",
                "Model": "gpt-4o"
              }
            }
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(documents.Path, AppSettings.SettingsFileName),
            """{"Language":"zh-CN"}""",
            new UTF8Encoding(false));

        AppSettings.ResolveUserSettingsDirectory(startup.Path, documents.Path);
        var loaded = new AppSettings(documents.Path);

        Assert.Equal("zh-CN", loaded.Language);
        Assert.True(AppSettings.HasRecognizableApiConfig(loaded));
        Assert.Equal("https://api.example.com/v1", loaded.OpenAiAutoTagger.ConnectionAddress);
        Assert.Equal("sk-legacy-key", loaded.OpenAiAutoTagger.ApiKey);
        Assert.Equal("gpt-4o", loaded.OpenAiAutoTagger.Model);
        Assert.NotEmpty(loaded.LlmApiProfiles);
        Assert.Equal("https://api.example.com/v1", loaded.LlmApiProfiles[0].Endpoint);
    }

    [Fact]
    public void Resolve_doesNotReplaceDocumentsApiWhenAlreadyPresent()
    {
        using var startup = new TemporaryDirectory();
        using var documents = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(startup.Path, AppSettings.SettingsFileName),
            """{"OpenAiAutoTagger":{"ConnectionAddress":"https://old.example","ApiKey":"sk-old"}}""",
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(documents.Path, AppSettings.SettingsFileName),
            """{"OpenAiAutoTagger":{"ConnectionAddress":"https://new.example","ApiKey":"sk-new"}}""",
            new UTF8Encoding(false));

        AppSettings.ResolveUserSettingsDirectory(startup.Path, documents.Path);
        var loaded = new AppSettings(documents.Path);

        Assert.Equal("https://new.example", loaded.OpenAiAutoTagger.ConnectionAddress);
        Assert.Equal("sk-new", loaded.OpenAiAutoTagger.ApiKey);
    }

    [Fact]
    public void LoadData_remapsLeftoverRussianTranslationTargetOnChineseUi()
    {
        using var documents = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(documents.Path, AppSettings.SettingsFileName),
            """{"Language":"zh-CN","TranslationLanguage":"ru"}""",
            new UTF8Encoding(false));

        var loaded = new AppSettings(documents.Path);

        Assert.Equal("zh-CN", loaded.Language);
        Assert.Equal("zh-CN", loaded.TranslationLanguage);
        Assert.True(loaded.TranslationLanguageMigratedFromLegacyRu);
    }

    [Fact]
    public void LoadData_keepsIntentionalRussianAfterLegacyRemap()
    {
        using var documents = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(documents.Path, AppSettings.SettingsFileName),
            """{"Language":"zh-CN","TranslationLanguage":"ru","TranslationLanguageMigratedFromLegacyRu":true}""",
            new UTF8Encoding(false));

        var loaded = new AppSettings(documents.Path);

        Assert.Equal("ru", loaded.TranslationLanguage);
        Assert.True(loaded.TranslationLanguageMigratedFromLegacyRu);
    }

    [Fact]
    public void LoadData_remapsLeftoverRussianTranslationTargetOnTraditionalChineseUi()
    {
        using var documents = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(documents.Path, AppSettings.SettingsFileName),
            """{"Language":"zh-TW","TranslationLanguage":"ru"}""",
            new UTF8Encoding(false));

        var loaded = new AppSettings(documents.Path);

        Assert.Equal("zh-TW", loaded.TranslationLanguage);
    }

    [Fact]
    public void NewSettingsDefaultTranslationLanguageIsSimplifiedChinese()
    {
        var settings = new AppSettings();
        Assert.Equal("zh-CN", settings.TranslationLanguage);
        Assert.Equal("zh-CN", settings.Language);
    }

    [Fact]
    public void HasRecognizableApiConfig_requiresEndpointKeyOrToken()
    {
        Assert.False(AppSettings.HasRecognizableApiConfig(new AppSettings()));
        Assert.False(AppSettings.HasRecognizableApiConfig(new AppSettings
        {
            OpenAiAutoTagger = new OpenAiSettings()
        }));
        Assert.True(AppSettings.HasRecognizableApiConfig(new AppSettings
        {
            OpenAiAutoTagger = new OpenAiSettings { ConnectionAddress = "https://api.example.com" }
        }));
    }
}
