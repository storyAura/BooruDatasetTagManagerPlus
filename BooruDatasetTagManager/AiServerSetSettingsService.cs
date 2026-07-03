using System;
using System.Collections.Generic;
using BooruDatasetTagManager.AiApi;

namespace BooruDatasetTagManager
{
    public static class AiServerSetSettingsService
    {
        public static void Save(
            string openAiEndpoint,
            string openAiApiKey,
            int openAiTimeout,
            string openAiModel,
            int llmT2NlConcurrency,
            IEnumerable<AiPromptTemplateSettings> promptTemplates,
            string selectedPromptTemplateId)
        {
            AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                promptTemplates,
                selectedPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            Program.Settings.OpenAiAutoTagger.ConnectionAddress = openAiEndpoint ?? string.Empty;
            Program.Settings.OpenAiAutoTagger.ApiKey = openAiApiKey ?? string.Empty;
            Program.Settings.OpenAiAutoTagger.RequestTimeout = openAiTimeout;
            Program.Settings.OpenAiAutoTagger.Model = openAiModel ?? string.Empty;
            Program.Settings.LlmT2NlConcurrency = Math.Clamp(llmT2NlConcurrency, 1, 100);
            Program.Settings.AiServerSetPromptTemplates = promptLibrary.CreateSnapshot();
            Program.Settings.AiServerSetPromptTemplateId = promptLibrary.SelectedTemplateId;
            Program.Settings.AiServerSetPromptTemplate = promptLibrary.SelectedTemplate.Name;
            Program.Settings.OpenAiAutoTagger.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;

            if (!string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ConnectionAddress))
            {
                try
                {
                    Program.OpenAiAutoTagger = new AiOpenAiClient(
                        Program.Settings.OpenAiAutoTagger.ConnectionAddress,
                        string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ApiKey)
                            ? "not-required"
                            : Program.Settings.OpenAiAutoTagger.ApiKey,
                        Program.Settings.OpenAiAutoTagger.RequestTimeout);
                }
                catch
                {
                }
            }
            else
            {
                Program.OpenAiAutoTagger = null;
            }

            Program.Settings.SaveSettings();
        }
    }
}
