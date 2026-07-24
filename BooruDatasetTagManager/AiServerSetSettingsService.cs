using System;
using System.Collections.Generic;
using System.Linq;
using BooruDatasetTagManager.AiApi;

namespace BooruDatasetTagManager
{
    public static class AiServerSetSettingsService
    {
        /// <summary>True for plain-HTTP endpoints on non-loopback hosts: the
        /// API key, prompts and images would cross the network unencrypted.</summary>
        public static bool IsInsecureEndpoint(Uri endpoint)
        {
            return endpoint != null
                && endpoint.Scheme == Uri.UriSchemeHttp
                && !endpoint.IsLoopback;
        }

        public static void Save(
            IEnumerable<LlmApiProfile> profiles,
            int activeProfileIndex,
            int openAiTimeout,
            int llmT2NlConcurrency,
            IEnumerable<AiPromptTemplateSettings> promptTemplates,
            string selectedPromptTemplateId)
        {
            AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                promptTemplates,
                selectedPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            Program.Settings.LlmApiProfiles = (profiles ?? Enumerable.Empty<LlmApiProfile>())
                .Where(profile => profile != null)
                .Select(profile => profile.Clone())
                .ToList();
            Program.Settings.LlmApiProfileIndex = activeProfileIndex;
            LlmApiProfileLogic.ApplyActiveProfile(Program.Settings);
            Program.Settings.OpenAiAutoTagger.RequestTimeout = openAiTimeout;
            Program.Settings.LlmT2NlConcurrency = Math.Clamp(llmT2NlConcurrency, 1, 100);
            Program.Settings.AiServerSetPromptTemplates = promptLibrary.CreateSnapshot();
            Program.Settings.AiServerSetPromptTemplateId = promptLibrary.SelectedTemplateId;
            Program.Settings.AiServerSetPromptTemplate = promptLibrary.SelectedTemplate.Name;
            Program.Settings.OpenAiAutoTagger.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;

            if (!string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ConnectionAddress))
            {
                try
                {
                    Program.OpenAiAutoTagger = AiOpenAiClient.CreateFromSettings(Program.Settings);
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

        public static void SavePromptTemplates(
            IEnumerable<AiPromptTemplateSettings> promptTemplates,
            string selectedPromptTemplateId)
        {
            AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                promptTemplates,
                selectedPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            Program.Settings.AiServerSetPromptTemplates = promptLibrary.CreateSnapshot();
            Program.Settings.AiServerSetPromptTemplateId = promptLibrary.SelectedTemplateId;
            Program.Settings.AiServerSetPromptTemplate = promptLibrary.SelectedTemplate.Name;
            Program.Settings.OpenAiAutoTagger.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;
        }
    }
}
