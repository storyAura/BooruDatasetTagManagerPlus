using Newtonsoft.Json.Linq;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class AiServerSetMigrationAndDocumentationTests
{
    [Fact]
    public void LegacyAiAgentPromptFieldsMigrateAndAreNotWrittenBack()
    {
        const string legacyJson = """
            {
              "AiAgentPromptTemplate": "Custom",
              "AiAgentPromptTemplateId": "custom.1",
              "AiAgentPromptTemplates": [{"Id":"custom.1","Name":"Custom","SystemPrompt":"Prompt","IsBuiltIn":false}]
            }
            """;

        string migrated = AiServerSetSettingsMigration.MigrateJson(legacyJson);
        JObject settings = JObject.Parse(migrated);

        Assert.Equal("Custom", settings.Value<string>("AiServerSetPromptTemplate"));
        Assert.Equal("custom.1", settings.Value<string>("AiServerSetPromptTemplateId"));
        Assert.Single(settings["AiServerSetPromptTemplates"]!);
        Assert.DoesNotContain(settings.Properties(), property => property.Name.StartsWith("AiAgent", StringComparison.Ordinal));
    }

    [Fact]
    public void NewAiServerSetFieldsTakePrecedenceOverLegacyFields()
    {
        const string mixedJson = """
            {
              "AiServerSetPromptTemplate": "New",
              "AiAgentPromptTemplate": "Old"
            }
            """;

        JObject settings = JObject.Parse(AiServerSetSettingsMigration.MigrateJson(mixedJson));

        Assert.Equal("New", settings.Value<string>("AiServerSetPromptTemplate"));
        Assert.Null(settings["AiAgentPromptTemplate"]);
    }

    [Fact]
    public void SourceUsesAiServerSetNamesAndBlankOpenAiDefaults()
    {
        string root = FindRepoRoot();
        string project = Path.Combine(root, "BooruDatasetTagManager");
        string form = Path.Combine(project, "Form_AiServerSet.cs");
        string service = Path.Combine(project, "AiServerSetSettingsService.cs");
        string settings = File.ReadAllText(Path.Combine(project, "AppSettings.cs"));

        Assert.True(File.Exists(form));
        Assert.True(File.Exists(service));
        Assert.False(File.Exists(Path.Combine(project, "Form_AiAgent.cs")));
        Assert.False(File.Exists(Path.Combine(project, "AiAgentSettingsService.cs")));
        Assert.Contains("public new string ConnectionAddress { get; set; } = string.Empty;", settings);
        Assert.Contains("public string ApiKey { get; set; } = string.Empty;", settings);
        Assert.Contains("public string Model { get; set; } = string.Empty;", settings);
    }

    [Theory]
    [InlineData("README.md", "## Differences from the Original", "## AiServerSet", "## Native LLM-T2NL")]
    [InlineData("README_zh_CN.md", "## 与原版的区别", "## AiServerSet", "## 原生 LLM-T2NL")]
    public void BilingualReadmesContainFeatureSectionsAndImages(
        string fileName,
        string comparisonHeading,
        string settingsHeading,
        string llmHeading)
    {
        string root = FindRepoRoot();
        string readme = File.ReadAllText(Path.Combine(root, fileName));

        Assert.Contains(comparisonHeading, readme);
        Assert.Contains(settingsHeading, readme);
        Assert.Contains(llmHeading, readme);
        Assert.Contains("docs/images/feature-overview.png", readme);
        Assert.Contains("docs/images/main-window.png", readme);
        Assert.Contains("docs/images/ai-server-set.png", readme);
        Assert.Contains("docs/images/llm-t2nl.png", readme);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "BooruDatasetTagManager")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
