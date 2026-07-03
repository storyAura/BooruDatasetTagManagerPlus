using Newtonsoft.Json.Linq;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class AiPromptTemplateLibraryTests
{
    [Fact]
    public void CreatesFourBuiltInTemplatesAndMigratesLegacySelection()
    {
        AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(
            null,
            string.Empty,
            AiPromptTemplateCatalog.HybridMode);

        Assert.Equal(4, library.Templates.Count);
        Assert.All(library.Templates, template => Assert.True(template.IsBuiltIn));
        Assert.Equal(AiPromptTemplateCatalog.HybridModeId, library.SelectedTemplateId);
    }

    [Fact]
    public void BuiltInContentCanBeChangedAndRestoredButNameCannotChange()
    {
        AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(null, null, null);

        library.Update(AiPromptTemplateCatalog.DanbooruTagId, "Renamed", "customized built-in");
        Assert.Equal(AiPromptTemplateCatalog.DanbooruTag, library.SelectedTemplate.Name);
        Assert.Equal("customized built-in", library.SelectedTemplate.SystemPrompt);

        Assert.True(library.RestoreDefault(AiPromptTemplateCatalog.DanbooruTagId));
        Assert.Equal(AiPromptTemplateCatalog.AutoTaggingSystemPrompt, library.SelectedTemplate.SystemPrompt);
        Assert.False(library.Delete(AiPromptTemplateCatalog.DanbooruTagId));
    }

    [Fact]
    public void CustomTemplatesCanBeAddedUpdatedAndDeletedWithDanbooruFallback()
    {
        AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(null, null, null);

        AiPromptTemplateSettings custom = library.AddCustom("My prompt", "first");
        library.Update(custom.Id, "Renamed prompt", "second");
        library.Select(custom.Id);

        Assert.Equal("Renamed prompt", library.SelectedTemplate.Name);
        Assert.Equal("second", library.SelectedTemplate.SystemPrompt);
        Assert.True(library.Delete(custom.Id));
        Assert.Equal(AiPromptTemplateCatalog.DanbooruTagId, library.SelectedTemplateId);
    }

    [Theory]
    [InlineData("", "content")]
    [InlineData("name", "")]
    [InlineData("Danbooru Tag", "content")]
    public void CustomTemplatesRejectEmptyOrDuplicateValues(string name, string prompt)
    {
        AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(null, null, null);

        Assert.Throws<ArgumentException>(() => library.AddCustom(name, prompt));
    }

    [Fact]
    public void ExportCurrentAndAllCustomUseVersionedJsonWithoutConnectionSecrets()
    {
        AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(null, null, null);
        AiPromptTemplateSettings first = library.AddCustom("One", "prompt one");
        library.AddCustom("Two", "prompt two");
        library.Select(first.Id);

        JObject current = JObject.Parse(library.ExportCurrentJson());
        JObject allCustom = JObject.Parse(library.ExportAllCustomJson());

        Assert.Equal(1, current.Value<int>("version"));
        Assert.Single(current["templates"]!);
        Assert.Equal(2, allCustom["templates"]!.Count());
        Assert.DoesNotContain("api", allCustom.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endpoint", allCustom.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("'<' is an invalid start of a value", OpenAiConnectionErrorKind.InvalidApiResponse)]
    [InlineData("Response status code does not indicate success: 401", OpenAiConnectionErrorKind.Authentication)]
    [InlineData("The request was canceled due to the configured HttpClient.Timeout", OpenAiConnectionErrorKind.Timeout)]
    [InlineData("No connection could be made because the target machine refused it", OpenAiConnectionErrorKind.Network)]
    public void ConnectionErrorsAreClassifiedForFriendlyMessages(
        string error,
        OpenAiConnectionErrorKind expected)
    {
        Assert.Equal(expected, OpenAiConnectionErrorClassifier.Classify(error));
    }
}
