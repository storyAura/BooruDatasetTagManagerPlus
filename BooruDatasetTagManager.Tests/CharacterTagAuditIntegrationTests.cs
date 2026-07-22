using Xunit;

namespace BooruDatasetTagManager.Tests;

public sealed class CharacterTagAuditIntegrationTests
{
    [Fact]
    public void SettingsPersistAuditModelStyleAndExecutionMode()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "AppSettings.cs"));

        Assert.Contains("public string CharacterTagAuditModel { get; set; } = string.Empty;", source);
        Assert.Contains("public CharacterTagAuditStyle CharacterTagAuditStyle { get; set; } = CharacterTagAuditStyle.Sparse;", source);
        Assert.Contains("public CharacterTagAuditExecutionMode CharacterTagAuditExecutionMode { get; set; } = CharacterTagAuditExecutionMode.Review;", source);
        Assert.Contains("public int CharacterTagAuditMinimumCount { get; set; } = 10;", source);
        Assert.Contains("CharacterTagAuditModel = tempSettings.CharacterTagAuditModel ?? string.Empty;", source);
        Assert.Contains("CharacterTagAuditMinimumCount = tempSettings.CharacterTagAuditMinimumCount <= 0 ? 10", source);
    }

    [Fact]
    public void ProjectPublishesAgentSkillsAndVersionIs120()
    {
        string project = ProjectDirectory();
        string csproj = File.ReadAllText(Path.Combine(project, "BooruDatasetTagManager.csproj"));
        string assembly = File.ReadAllText(Path.Combine(project, "Properties", "AssemblyInfo.cs"));

        Assert.Contains("..\\Agent\\skills\\**\\*", csproj);
        Assert.Contains("<ApplicationVersion>1.2.0.0</ApplicationVersion>", csproj);
        Assert.Contains("AssemblyVersion(\"1.2.0.0\")", assembly);
        Assert.Contains("AssemblyFileVersion(\"1.2.0.0\")", assembly);
        Assert.Contains("AssemblyInformationalVersion(\"1.2.0\")", assembly);
    }

    [Fact]
    public void BothRuntimeSkillsExist()
    {
        string root = RepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "Agent", "skills", "prompt-pyramid", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(root, "Agent", "skills", "character-tag-auditor", "SKILL.md")));
    }

    [Fact]
    public void SkillsDocumentReplacementAndCharacterOnlyPromptRules()
    {
        string root = RepoRoot();
        string auditor = File.ReadAllText(Path.Combine(root, "Agent", "skills", "character-tag-auditor", "SKILL.md"));
        string pyramid = File.ReadAllText(Path.Combine(root, "Agent", "skills", "prompt-pyramid", "SKILL.md"));

        Assert.Contains("`replace`", auditor);
        Assert.Contains("white headwear", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pink skirt", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("other character", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("twin braids", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low twintails", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one canonical tag per feature family", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replacement target equals the source", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hair between eyes", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mole under eye", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("colored hair ribbon", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("colored jacket", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("colored hair ribbon", pyramid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("colored jacket", pyramid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visually confirmed", pyramid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("character-audit mode", pyramid, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkillsRequireColorCheckForColorlessGarmentTags()
    {
        string root = RepoRoot();
        string auditor = File.ReadAllText(Path.Combine(root, "Agent", "skills", "character-tag-auditor", "SKILL.md"));
        string pyramid = File.ReadAllText(Path.Combine(root, "Agent", "skills", "prompt-pyramid", "SKILL.md"));

        Assert.Contains("explicitly list and re-check every color-less garment", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("even if that colored tag does not currently exist anywhere in the tag inventory", auditor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("boots → black boots", auditor, StringComparison.Ordinal);
        Assert.Contains("Never answer `replace` with an empty `replacement_tag`", auditor, StringComparison.Ordinal);
        Assert.Contains("color-prefixed garment, footwear, and accessory tags", pyramid, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("even when the colored form never appeared in the original tag list", pyramid, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WizardLocalizesDecisionColumnsAndSupportsVisualReviewRerun()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("LocalizeDecision", source);
        Assert.Contains("nameof(ReviewRow.InitialDisplay)", source);
        Assert.Contains("nameof(ReviewRow.FinalDisplay)", source);
        Assert.DoesNotContain("item.FinalDecision.ToString()", source);
        Assert.Contains("buttonRedoVisual", source);
        Assert.Contains("ExecuteVisualReviewAsync", source);
        Assert.Contains("PickReferenceImage", source);
        Assert.Contains("initialAuditItems = items.ToList();", source);
        Assert.Contains("EditMode = DataGridViewEditMode.EditOnEnter", source);
        Assert.Contains("ValueType = typeof(CharacterTagDecision)", source);
        Assert.Contains("ResultGrid_OpenDecisionDropdown", source);
        Assert.Contains("BuildCanonicalizedAuditItems", source);
        Assert.Contains("CharacterTagResultCanonicalizer.Apply", source);
        Assert.Contains("ProgressBarStyle.Continuous", source);
        Assert.DoesNotContain("ProgressBarStyle.Marquee", source);
        Assert.Contains("UpdateAuditProgress", source);
        foreach (string language in new[] { "en-US", "zh-CN", "zh-TW", "ru-RU", "pt-BR" })
        {
            string text = File.ReadAllText(Path.Combine(ProjectDirectory(), "Languages", language + ".txt"));
            Assert.Contains("CharacterTagAuditRedoVisual=", text);
            Assert.Contains("CharacterTagAuditRedoVisualTitle=", text);
        }
    }

    [Fact]
    public void AutoTagProvidersAndSettingsAreExtensibleAndLocalized()
    {
        string project = ProjectDirectory();
        string settings = File.ReadAllText(Path.Combine(project, "AppSettings.cs"));
        string adapters = File.ReadAllText(Path.Combine(project, "AutoTagProviderAdapters.cs"));
        Assert.Contains("AutoTagProviderId { get; set; } = \"openai-compatible\"", settings);
        Assert.Contains("OpenAiCompatibleAutoTagProvider", adapters);
        Assert.Contains("AiApiServerAutoTagProvider", adapters);
        foreach (string language in new[] { "en-US", "zh-CN", "zh-TW", "ru-RU", "pt-BR" })
        {
            string text = File.ReadAllText(Path.Combine(project, "Languages", language + ".txt"));
            Assert.Contains("UIAutoTagSystemMessage=", text);
            Assert.Contains("UIAutoTagSelectedModelInfo=", text);
            Assert.Contains("CharacterTagDecisionReplace=", text);
        }
    }

    [Fact]
    public void CharacterAuditLocalizationKeysExistInEveryLanguage()
    {
        string[] required =
        {
            "CharacterTagAuditTitle", "CharacterTagAuditModel", "CharacterTagAuditTrigger",
            "CharacterTagAuditTextScreening", "CharacterTagAuditVisualReview", "CharacterTagAuditApplyConfirm",
            "CharacterTagAuditMinimumCount", "CharacterTagAuditInitialSummary", "CharacterTagAuditExcludedHeader",
            "CharacterTagAuditDetailedSummary", "CharacterTagAuditModelInvalidReplacement"
        };
        foreach (string language in new[] { "en-US", "zh-CN", "zh-TW", "ru-RU", "pt-BR" })
        {
            string text = File.ReadAllText(Path.Combine(ProjectDirectory(), "Languages", language + ".txt"));
            foreach (string key in required)
                Assert.Contains(key + "=", text);
        }
    }

    [Fact]
    public void EveryAuditCategoryHasLocalizedTextInEveryLanguage()
    {
        string[] keys = Enum.GetValues<CharacterTagCategory>()
            .Select(CharacterTagCategoryLocalization.GetKey)
            .ToArray();
        foreach (string language in new[] { "en-US", "zh-CN", "zh-TW", "ru-RU", "pt-BR" })
        {
            string text = File.ReadAllText(Path.Combine(ProjectDirectory(), "Languages", language + ".txt"));
            foreach (string key in keys)
                Assert.Contains(key + "=", text);
        }
    }

    [Fact]
    public void WizardAddsWikiLookupOnlyToInitialAndFinalAuditTables()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("AttachWikiContextMenu(initialGrid)", source);
        Assert.Contains("AttachWikiContextMenu(resultGrid)", source);
        Assert.DoesNotContain("AttachWikiContextMenu(excludedGrid)", source);
        Assert.Contains("new Form_TagWikiPopup(item.Tag)", source);
    }

    [Fact]
    public void WizardUsesBufferedControlsAndAvoidsFullGridRefreshDuringInteraction()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("BufferedDataGridView initialGrid", source);
        Assert.Contains("BufferedDataGridView resultGrid", source);
        Assert.Contains("BufferedDataGridView excludedGrid", source);
        Assert.Contains("BufferedPictureBox referencePreview", source);
        Assert.Contains("resultSplit.Panel1MinSize = PreviewMinimumWidth", source);
        Assert.Contains("resultSplit.Panel2MinSize = ResultMinimumWidth", source);
        Assert.Contains("BeginSplitterDrag", source);
        Assert.Contains("EndSplitterDrag", source);
        Assert.Contains("InvalidateColumn", source);
        Assert.DoesNotContain("grid.Refresh()", source);
    }

    [Fact]
    public void WizardRegistersNextAndCancelAsDialogButtons()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));
        Assert.Contains("AcceptButton = buttonNext;", source);
        Assert.Contains("CancelButton = buttonCancel;", source);
        Assert.Contains("buttonCancel.DialogResult = DialogResult.Cancel;", source);
    }

    [Fact]
    public void WizardChoiceBoxesFillTheirSettingsColumns()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("comboStyle.Dock = DockStyle.Fill;", source);
        Assert.Contains("comboMode.Dock = DockStyle.Fill;", source);
        Assert.Contains("UpdateChoiceDropDownWidth(comboStyle);", source);
        Assert.Contains("UpdateChoiceDropDownWidth(comboMode);", source);
    }

    [Fact]
    public void WizardChoiceLookupFallsBackBeforeDefaultSelectionIsApplied()
    {
        string source = File.ReadAllText(Path.Combine(ProjectDirectory(), "Form_CharacterTagAuditWizard.cs"));

        Assert.Contains("comboBox.SelectedItem is LocalizedChoice<T> selected", source);
        Assert.Contains("comboBox.Items.OfType<LocalizedChoice<T>>().FirstOrDefault()", source);
    }

    [Fact]
    public void QuickBuildUpdatesReleaseFolderBeforePublishingDist()
    {
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "quick_build.bat"));
        int build = script.IndexOf("dotnet build \"%PROJECT%\"", StringComparison.OrdinalIgnoreCase);
        int publish = script.IndexOf("dotnet publish \"%PROJECT%\"", StringComparison.OrdinalIgnoreCase);

        Assert.True(build >= 0, "quick_build.bat must update bin/Release for test_start.bat.");
        Assert.True(publish > build, "The Release build must finish before publishing dist/.");
    }

    private static string ProjectDirectory() => Path.Combine(RepoRoot(), "BooruDatasetTagManager");

    private static string RepoRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BooruDatasetTagManager.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
