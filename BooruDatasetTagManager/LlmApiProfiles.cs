using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// One saved LLM API site: endpoint + its API keys (rotated per request)
    /// + the models last selected for that site. Keys are DPAPI-encrypted at
    /// rest and, once added, are never shown again in the UI (masked tail only).
    /// </summary>
    public class LlmApiProfile
    {
        public string Name { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string VisionModel { get; set; } = string.Empty;
        public string AuditModel { get; set; } = string.Empty;

        // In-memory plaintext keys. Never serialized directly.
        [JsonIgnore]
        public List<string> Tokens { get; set; } = new List<string>();

        // Persisted (DPAPI-encrypted) form, same pattern as OpenAiSettings.ApiKey.
        // ObjectCreationHandling.Replace is required: the getter returns a fresh
        // projected list, so without it Json.NET would populate that temp list
        // and never call the setter.
        [JsonProperty("Tokens", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> TokensProtected
        {
            get => Tokens.Select(SecretProtector.Protect).ToList();
            set => Tokens = (value ?? new List<string>())
                .Select(SecretProtector.Unprotect)
                .Where(token => !string.IsNullOrEmpty(token))
                .ToList();
        }

        public LlmApiProfile Clone()
        {
            return new LlmApiProfile
            {
                Name = Name,
                Endpoint = Endpoint,
                Model = Model,
                VisionModel = VisionModel,
                AuditModel = AuditModel,
                Tokens = new List<string>(Tokens ?? new List<string>())
            };
        }
    }

    public static class LlmApiProfileLogic
    {
        /// <summary>Display form of a stored key: bullets + last 4 chars, never the full value.</summary>
        public static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;
            return token.Length >= 8 ? "••••" + token[^4..] : "••••";
        }

        /// <summary>Trimmed, non-empty, de-duplicated key list (first occurrence wins).</summary>
        public static List<string> SanitizeTokens(IEnumerable<string> tokens)
        {
            return (tokens ?? Enumerable.Empty<string>())
                .Select(token => (token ?? string.Empty).Trim())
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static int ClampIndex(int index, int count)
        {
            return count <= 0 ? 0 : Math.Clamp(index, 0, count - 1);
        }

        /// <summary>Round-robin key index; safe when the shared counter overflows int.</summary>
        public static int RotationIndex(int counter, int keyCount)
        {
            return keyCount <= 1 ? 0 : (int)((uint)counter % (uint)keyCount);
        }

        /// <summary>Profile name suggested from the endpoint host, e.g. "ai.liaobots.work".</summary>
        public static string SuggestName(string endpoint)
        {
            if (Uri.TryCreate((endpoint ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri)
                && !string.IsNullOrEmpty(uri.Host))
                return uri.Host;
            return "Default";
        }

        /// <summary>
        /// Pre-profile configs carry one endpoint + one key in the flat
        /// OpenAiSettings fields: wrap them into the first profile so nothing
        /// is lost on upgrade. Idempotent; also clamps the active index.
        /// </summary>
        public static void EnsureLegacyProfile(AppSettings settings)
        {
            if (settings?.LlmApiProfiles == null)
                return;
            settings.LlmApiProfileIndex = ClampIndex(settings.LlmApiProfileIndex, settings.LlmApiProfiles.Count);
            if (settings.LlmApiProfiles.Count > 0)
                return;
            OpenAiSettings legacy = settings.OpenAiAutoTagger;
            if (legacy == null
                || (string.IsNullOrWhiteSpace(legacy.ConnectionAddress) && string.IsNullOrWhiteSpace(legacy.ApiKey)))
                return;
            settings.LlmApiProfiles.Add(new LlmApiProfile
            {
                Name = SuggestName(legacy.ConnectionAddress),
                Endpoint = legacy.ConnectionAddress ?? string.Empty,
                Model = legacy.Model ?? string.Empty,
                VisionModel = legacy.VisionModel ?? string.Empty,
                AuditModel = settings.CharacterTagAuditModel ?? string.Empty,
                Tokens = SanitizeTokens(new[] { legacy.ApiKey })
            });
            settings.LlmApiProfileIndex = 0;
        }

        /// <summary>
        /// Mirrors the active profile into the flat fields the rest of the app
        /// reads (endpoint, models, first key for back-compat).
        /// </summary>
        public static void ApplyActiveProfile(AppSettings settings)
        {
            if (settings?.LlmApiProfiles == null || settings.LlmApiProfiles.Count == 0
                || settings.OpenAiAutoTagger == null)
                return;
            settings.LlmApiProfileIndex = ClampIndex(settings.LlmApiProfileIndex, settings.LlmApiProfiles.Count);
            LlmApiProfile active = settings.LlmApiProfiles[settings.LlmApiProfileIndex];
            active.Tokens = SanitizeTokens(active.Tokens);
            settings.OpenAiAutoTagger.ConnectionAddress = active.Endpoint ?? string.Empty;
            settings.OpenAiAutoTagger.ApiKey = active.Tokens.Count > 0 ? active.Tokens[0] : string.Empty;
            settings.OpenAiAutoTagger.Model = active.Model ?? string.Empty;
            settings.OpenAiAutoTagger.VisionModel = active.VisionModel ?? string.Empty;
            settings.CharacterTagAuditModel = active.AuditModel ?? string.Empty;
        }
    }
}
