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
        Assert.Contains("public string VisionModel { get; set; } = string.Empty;", settings);
        Assert.Contains("ResolveVisionModel()", settings);
    }

    [Fact]
    public void PromptTemplateEditorMovedToAutoTaggerSettingsForm()
    {
        string project = Path.Combine(FindRepoRoot(), "BooruDatasetTagManager");
        string aiServerSet = File.ReadAllText(Path.Combine(project, "Form_AiServerSet.cs"));
        string autoTagger = File.ReadAllText(Path.Combine(project, "Form_AutoTaggerOpenAiSettings.cs"));

        Assert.DoesNotContain("groupAutoTagPrompt", aiServerSet);
        Assert.Contains("AiPromptTemplateEditorPanel", autoTagger);
        Assert.True(File.Exists(Path.Combine(project, "AiPromptTemplateEditorPanel.cs")));
    }

    [Theory]
    [InlineData("README_en.md", "## Getting started", "### LLM tagging", "### Character tag audit", "## Acknowledgments & license")]
    [InlineData("README.md", "## 快速开始", "### LLM 打标", "### 角色标签审查", "## 致谢与许可")]
    public void BilingualReadmesContainFeatureSectionsAndImages(
        string fileName,
        string quickStartHeading,
        string llmHeading,
        string auditHeading,
        string creditsHeading)
    {
        string root = FindRepoRoot();
        string readme = File.ReadAllText(Path.Combine(root, fileName));

        Assert.Contains(quickStartHeading, readme);
        Assert.Contains(llmHeading, readme);
        Assert.Contains(auditHeading, readme);
        Assert.Contains(creditsHeading, readme);
        Assert.Contains("github.com/starik222/BooruDatasetTagManager", readme);
        Assert.Contains("docs/images/main-window-dataset-browser.png", readme);
        Assert.Contains("docs/images/llm-settings.png", readme);
        Assert.Contains("docs/images/llm-tagger.png", readme);
        Assert.Contains("docs/images/onnx-tagger.png", readme);
        Assert.Contains("docs/images/character-tag-audit-review.png", readme);
        Assert.Contains("docs/images/crop-image-multi-region.png", readme);
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
