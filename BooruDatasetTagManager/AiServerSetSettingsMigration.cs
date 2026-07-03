using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BooruDatasetTagManager
{
    public static class AiServerSetSettingsMigration
    {
        private static readonly (string Legacy, string Current)[] promptFields =
        {
            ("AiAgentPromptTemplate", "AiServerSetPromptTemplate"),
            ("AiAgentPromptTemplateId", "AiServerSetPromptTemplateId"),
            ("AiAgentPromptTemplates", "AiServerSetPromptTemplates")
        };

        public static string MigrateJson(string json)
        {
            JObject settings = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
            foreach ((string legacy, string current) in promptFields)
            {
                JToken legacyValue = settings[legacy];
                if (settings[current] == null && legacyValue != null)
                    settings[current] = legacyValue.DeepClone();
                settings.Remove(legacy);
            }
            return settings.ToString(Formatting.None);
        }
    }
}
