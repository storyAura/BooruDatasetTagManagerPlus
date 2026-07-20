using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BooruDatasetTagManager
{
    public class AppSettings
    {
        public string TranslationLanguage { get; set; } = "ru";
        public int PreviewSize { get; set; } = 130;
        [JsonIgnore]
        public List<LanguageItem> AvaibleLanguages;
        public TranslationService TransService { get; set; } = TranslationService.ChineseTranslate;
        public List<TranslationService> TranslationProviderOrder { get; set; } = GetDefaultTranslationProviderOrder();
        public int TranslationTimeoutSeconds { get; set; } = 5;
        public bool OnlyManualTransInAutocomplete { get; set; } = false;
        public AutocompleteMode AutocompleteMode { get; set; } = AutocompleteMode.StartWith;
        public AutocompleteSort AutocompleteSort { get; set; } = AutocompleteSort.Alphabetical;
        public bool FixTagsOnSaveLoad { get; set; } = true;
        public ImagePreviewType PreviewType { get; set; } = ImagePreviewType.PreviewInMainWindow;
        //public bool FixTagsOnSave { get; set; } = true;
        public string SeparatorOnLoad { get; set; } = ",";
        public string SeparatorOnSave { get; set; } = ", ";
        public string DefaultTagsFileExtension { get; set; } = "txt";
        public string CaptionFileExtensions
        {
            get
            {
                return string.Join(',', _tagsFilesExt);
            }
            set
            {
                _tagsFilesExt = value.Split(new char[] { ',' }, StringSplitOptions.TrimEntries);
            }
        }
        public int ShowAutocompleteAfterCharCount { get; set; } = 3;
        public bool AskSaveChanges { get; set; } = true;
        public int GridViewRowHeight { get; set; } = 29;
        public FontSettings GridViewFont { get; set; } = new FontSettings();
        public FontSettings AutocompleteFont { get; set; } = new FontSettings() { Name = "Segoe UI", Size = 9, GdiCharSet = 1 };

        public HotkeyData Hotkeys { get; set; }

        public InterragatorSettings AutoTagger { get; set; }
        public OpenAiSettings OpenAiAutoTagger { get; set; }

        public int TagImagesGridSize { get; set; } = 400;

        public bool AutoSort { get; set; } = false;

        public string Language { get; set; } = "zh-CN";

        public string ColorScheme { get; set; } = "Classic";

        public bool CacheOpenImages { get; set; } = true;

        public bool LoadSettingsLoadPreviewImages { get; set; } = true;
        public bool LoadSettingsReadMetadata { get; set; } = false;
        public bool UseDanbooruZhCsvBeforeTranslation { get; set; } = true;
        public int QuickReplaceThreshold { get; set; } = 30;
        // Unified concurrency for ALL external-LLM batch operations (tagging + TAG2NL),
        // not just TAG2NL. Kept under the legacy JSON name for settings back-compat.
        public int LlmT2NlConcurrency { get; set; } = 5;
        // Persisted state of the unified LLM tagging window (Form_LlmTagger).
        public LlmTaggerMode LlmTaggerMode { get; set; } = LlmTaggerMode.Tags;
        public LlmCaptionOutputTarget LlmCaptionOutputTarget { get; set; } = LlmCaptionOutputTarget.SeparateFolder;
        public LlmCaptionFormat LlmCaptionFormat { get; set; } = LlmCaptionFormat.TagsAndNaturalLanguage;
        public bool LlmTaggerReprocessExisting { get; set; } = false;
        // Natural-language mode: run the local ONNX tagger first on images that have no tags.
        public bool LlmTaggerAutoOnnxIfNoTags { get; set; } = true;
        public string CharacterTagAuditModel { get; set; } = string.Empty;
        public CharacterTagAuditStyle CharacterTagAuditStyle { get; set; } = CharacterTagAuditStyle.Sparse;
        public CharacterTagAuditExecutionMode CharacterTagAuditExecutionMode { get; set; } = CharacterTagAuditExecutionMode.Review;
        public int CharacterTagAuditMinimumCount { get; set; } = 10;
        public string AutoTagProviderId { get; set; } = "openai-compatible";
        public string FfmpegPath { get; set; } = string.Empty;
        public Wd14TaggerSettings Wd14Tagger { get; set; } = new Wd14TaggerSettings();
        public PixAiTaggerSettings PixAiTagger { get; set; } = new PixAiTaggerSettings();
        public string OnnxTaggerLastModelId { get; set; } = string.Empty;
        public string BackgroundRemoverModelId { get; set; } = string.Empty;
        // Background-removal output options (see Form_BGRemover).
        public bool BackgroundRemoverFillBackground { get; set; } = true;   // true = solid color, false = transparent
        public int BackgroundRemoverColorArgb { get; set; } = unchecked((int)0xFFFFFFFF); // default white
        public bool BackgroundRemoverReplaceOriginal { get; set; } = true;  // true = overwrite, false = save a copy
        // Default save behavior of the image editor (Form_ImageEditor).
        public ImageEditorSaveMode ImageEditorSaveMode { get; set; } = ImageEditorSaveMode.Ask;
        // Double-click action on the All Tags grid (Form1).
        public AllTagsQuickAction AllTagsDoubleClickAction { get; set; } = AllTagsQuickAction.QuickActionReplaceTag;

        // HuggingFace access token used for gated model repos (e.g. cl_tagger_v2).
        [JsonIgnore]
        public string HuggingFaceToken { get; set; } = string.Empty;

        // Persisted (DPAPI-encrypted) form, same pattern as the API keys.
        [JsonProperty("HuggingFaceToken")]
        public string HuggingFaceTokenProtected
        {
            get => SecretProtector.Protect(HuggingFaceToken);
            set => HuggingFaceToken = SecretProtector.Unprotect(value);
        }
        public string AiServerSetPromptTemplate { get; set; } = AiPromptTemplateCatalog.DanbooruTag;
        public string AiServerSetPromptTemplateId { get; set; } = AiPromptTemplateCatalog.DanbooruTagId;
        public List<AiPromptTemplateSettings> AiServerSetPromptTemplates { get; set; } =
            AiPromptTemplateCatalog.CreateDefaultSettings().Select(template => template.Clone()).ToList();

        private string[] _tagsFilesExt = { "txt", "caption" };

        private string settingsFile;


        public AppSettings(string appDir)
        {
            InitAvaibleLangs();
            AutoTagger = new InterragatorSettings();
            OpenAiAutoTagger = new OpenAiSettings();
            Hotkeys = new HotkeyData();
            Hotkeys.InitDefault();
            LoadData(appDir);
        }

        public AppSettings()
        {
            AutoTagger = new InterragatorSettings();
            OpenAiAutoTagger = new OpenAiSettings();
            Hotkeys = new HotkeyData();
            Hotkeys.InitDefault();
        }

        private void LoadData(string appDir)
        {
            settingsFile = Path.Combine(appDir, "settings.json");
            if (!File.Exists(settingsFile))
            {
                //Settings = new AppSettings();
                try
                {
                    File.WriteAllText(settingsFile, JsonConvert.SerializeObject(this));
                }
                catch (Exception ex)
                {
                    // First run from a read-only location: run with in-memory
                    // defaults instead of crashing before any window exists.
                    Trace.WriteLine($"AppSettings.LoadData: failed to create settings file: {ex}");
                }
            }
            else
            {
                AppSettings tempSettings = null;
                const int maxAttempts = 2;
                for (int attempt = 0; attempt < maxAttempts && tempSettings == null; attempt++)
                {
                    try
                    {
                        string migratedJson = AiServerSetSettingsMigration.MigrateJson(File.ReadAllText(settingsFile));
                        tempSettings = JsonConvert.DeserializeObject<AppSettings>(migratedJson);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"AppSettings.LoadData: failed to parse settings (attempt {attempt + 1}): {ex}");
                        // On the last attempt, don't try to overwrite again; just fall back to defaults.
                        if (attempt < maxAttempts - 1)
                        {
                            // Preserve the unreadable file before resetting so users
                            // can recover hand-written endpoints/keys from it.
                            try { File.Copy(settingsFile, settingsFile + ".corrupt", true); } catch { }
                            try
                            {
                                File.WriteAllText(settingsFile, JsonConvert.SerializeObject(this));
                            }
                            catch (Exception writeEx)
                            {
                                // Disk read-only / no permission: keep defaults in memory and stop retrying.
                                Trace.WriteLine($"AppSettings.LoadData: failed to rewrite settings file: {writeEx}");
                                break;
                            }
                        }
                    }
                }

                // Could not load or recover a valid settings file: keep constructor defaults.
                if (tempSettings == null)
                    return;

                TranslationLanguage = tempSettings.TranslationLanguage;
                PreviewSize = tempSettings.PreviewSize;
                TransService = tempSettings.TransService;
                if (tempSettings.TranslationProviderOrder == null || tempSettings.TranslationProviderOrder.Count == 0)
                {
                    TranslationProviderOrder = GetDefaultTranslationProviderOrder();
                    TransService = TranslationProviderOrder[0];
                }
                else
                {
                    TranslationProviderOrder = tempSettings.TranslationProviderOrder;
                }
                TranslationTimeoutSeconds = tempSettings.TranslationTimeoutSeconds <= 0 ? 5 : tempSettings.TranslationTimeoutSeconds;
                OnlyManualTransInAutocomplete = tempSettings.OnlyManualTransInAutocomplete;
                AutocompleteMode = tempSettings.AutocompleteMode;
                AutocompleteSort = tempSettings.AutocompleteSort;
                FixTagsOnSaveLoad = tempSettings.FixTagsOnSaveLoad;
                SeparatorOnLoad = tempSettings.SeparatorOnLoad;
                SeparatorOnSave = tempSettings.SeparatorOnSave;
                ShowAutocompleteAfterCharCount = tempSettings.ShowAutocompleteAfterCharCount;
                AskSaveChanges = tempSettings.AskSaveChanges;
                GridViewRowHeight = tempSettings.GridViewRowHeight;
                GridViewFont = tempSettings.GridViewFont;
                AutocompleteFont = tempSettings.AutocompleteFont;
                AutoSort = tempSettings.AutoSort || false;
                Language = tempSettings.Language;
                PreviewType = tempSettings.PreviewType;
                DefaultTagsFileExtension = tempSettings.DefaultTagsFileExtension;
                CaptionFileExtensions = tempSettings.CaptionFileExtensions;
                TagImagesGridSize = tempSettings.TagImagesGridSize;
                CacheOpenImages = tempSettings.CacheOpenImages;
                LoadSettingsLoadPreviewImages = tempSettings.LoadSettingsLoadPreviewImages;
                LoadSettingsReadMetadata = tempSettings.LoadSettingsReadMetadata;
                UseDanbooruZhCsvBeforeTranslation = tempSettings.UseDanbooruZhCsvBeforeTranslation;
                QuickReplaceThreshold = tempSettings.QuickReplaceThreshold <= 0 ? 30 : tempSettings.QuickReplaceThreshold;
                LlmT2NlConcurrency = Math.Clamp(tempSettings.LlmT2NlConcurrency, 1, 100);
                LlmTaggerMode = tempSettings.LlmTaggerMode;
                LlmCaptionOutputTarget = tempSettings.LlmCaptionOutputTarget;
                LlmCaptionFormat = tempSettings.LlmCaptionFormat;
                LlmTaggerReprocessExisting = tempSettings.LlmTaggerReprocessExisting;
                LlmTaggerAutoOnnxIfNoTags = tempSettings.LlmTaggerAutoOnnxIfNoTags;
                CharacterTagAuditModel = tempSettings.CharacterTagAuditModel ?? string.Empty;
                CharacterTagAuditStyle = tempSettings.CharacterTagAuditStyle;
                CharacterTagAuditExecutionMode = tempSettings.CharacterTagAuditExecutionMode;
                CharacterTagAuditMinimumCount = tempSettings.CharacterTagAuditMinimumCount <= 0 ? 10 : tempSettings.CharacterTagAuditMinimumCount;
                ImageEditorSaveMode = tempSettings.ImageEditorSaveMode;
                AutoTagProviderId = string.IsNullOrWhiteSpace(tempSettings.AutoTagProviderId)
                    ? "openai-compatible"
                    : tempSettings.AutoTagProviderId;
                FfmpegPath = tempSettings.FfmpegPath ?? string.Empty;
                Wd14Tagger = tempSettings.Wd14Tagger ?? new Wd14TaggerSettings();
                Wd14Tagger.EnsureLegacyThresholdMigrated();
                PixAiTagger = tempSettings.PixAiTagger ?? new PixAiTaggerSettings();
                OnnxTaggerLastModelId = tempSettings.OnnxTaggerLastModelId ?? string.Empty;
                BackgroundRemoverModelId = tempSettings.BackgroundRemoverModelId ?? string.Empty;
                BackgroundRemoverFillBackground = tempSettings.BackgroundRemoverFillBackground;
                BackgroundRemoverColorArgb = tempSettings.BackgroundRemoverColorArgb;
                BackgroundRemoverReplaceOriginal = tempSettings.BackgroundRemoverReplaceOriginal;
                AllTagsDoubleClickAction = tempSettings.AllTagsDoubleClickAction;
                HuggingFaceToken = tempSettings.HuggingFaceToken ?? string.Empty;
                if (!string.IsNullOrEmpty(tempSettings.ColorScheme))
                    ColorScheme = tempSettings.ColorScheme;
                AutoTagger = tempSettings.AutoTagger;
                if (AutoTagger == null)
                {
                    AutoTagger = new InterragatorSettings();
                }
                OpenAiAutoTagger = tempSettings.OpenAiAutoTagger;
                if (OpenAiAutoTagger == null)
                {
                    OpenAiAutoTagger = new OpenAiSettings();
                }
                if (string.IsNullOrWhiteSpace(OpenAiAutoTagger.VisionModel))
                    OpenAiAutoTagger.VisionModel = OpenAiAutoTagger.Model ?? string.Empty;
                AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                    tempSettings.AiServerSetPromptTemplates,
                    tempSettings.AiServerSetPromptTemplateId,
                    tempSettings.AiServerSetPromptTemplate);
                AiServerSetPromptTemplates = promptLibrary.CreateSnapshot();
                AiServerSetPromptTemplateId = promptLibrary.SelectedTemplateId;
                AiServerSetPromptTemplate = promptLibrary.SelectedTemplate.Name;
                OpenAiAutoTagger.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;

                if (tempSettings.Hotkeys != null)
                {
                    foreach (var item in tempSettings.Hotkeys.Items)
                    {
                        var hkItem = Hotkeys[item.Id];
                        if (hkItem != null)
                        {
                            hkItem.KeyData = item.KeyData;
                            hkItem.IsCtrl = item.IsCtrl;
                            hkItem.IsAlt = item.IsAlt;
                            hkItem.IsShift = item.IsShift;
                        }
                    }
                }
            }
        }

        public void SaveSettings()
        {
            try
            {
                // Atomic write + .bak: a crash/power loss mid-write used to truncate
                // settings.json, and the next startup silently reset all settings.
                SafeFile.WriteAllTextWithBackup(settingsFile, JsonConvert.SerializeObject(this));
            }
            catch (Exception ex)
            {
                // Read-only dir / locked file: keep running with in-memory settings.
                Trace.WriteLine($"AppSettings.SaveSettings failed: {ex}");
            }
        }

        public string[] GetTagFilesExtensions()
        {
            return _tagsFilesExt;
        }

        public List<TranslationService> GetTranslationProviderOrder()
        {
            if (TranslationProviderOrder == null || TranslationProviderOrder.Count == 0)
                return GetDefaultTranslationProviderOrder();

            return TranslationProviderOrder
                .Concat(GetDefaultTranslationProviderOrder())
                .Distinct()
                .ToList();
        }

        public static List<TranslationService> GetDefaultTranslationProviderOrder()
        {
            return new List<TranslationService>
            {
                TranslationService.ChineseTranslate,
                TranslationService.MyMemoryTranslate,
                TranslationService.GoogleJsonTranslate,
                TranslationService.GoogleTranslate
            };
        }

        public void InitAvaibleLangs()
        {
            AvaibleLanguages = new List<LanguageItem>
            {
                new LanguageItem("Afrikaans", "af"),
                new LanguageItem("Albanian", "sq"),
                new LanguageItem("Amharic", "am"),
                new LanguageItem("Arabic", "ar"),
                new LanguageItem("Armenian", "hy"),
                new LanguageItem("Assamese", "as"),
                new LanguageItem("Aymara", "ay"),
                new LanguageItem("Azerbaijani", "az"),
                new LanguageItem("Bambara", "bm"),
                new LanguageItem("Basque", "eu"),
                new LanguageItem("Belarusian", "be"),
                new LanguageItem("Bengali", "bn"),
                new LanguageItem("Bhojpuri", "bho"),
                new LanguageItem("Bosnian", "bs"),
                new LanguageItem("Bulgarian", "bg"),
                new LanguageItem("Catalan", "ca"),
                new LanguageItem("Cebuano", "ceb"),
                new LanguageItem("Chinese (Simplified)", "zh-CN"),
                new LanguageItem("Chinese (Traditional)", "zh-TW"),
                new LanguageItem("Corsican", "co"),
                new LanguageItem("Croatian", "hr"),
                new LanguageItem("Czech", "cs"),
                new LanguageItem("Danish", "da"),
                new LanguageItem("Dhivehi", "dv"),
                new LanguageItem("Dogri", "doi"),
                new LanguageItem("Dutch", "nl"),
                new LanguageItem("English", "en"),
                new LanguageItem("Esperanto", "eo"),
                new LanguageItem("Estonian", "et"),
                new LanguageItem("Ewe", "ee"),
                new LanguageItem("Filipino (Tagalog)", "fil"),
                new LanguageItem("Finnish", "fi"),
                new LanguageItem("French", "fr"),
                new LanguageItem("Frisian", "fy"),
                new LanguageItem("Galician", "gl"),
                new LanguageItem("Georgian", "ka"),
                new LanguageItem("German", "de"),
                new LanguageItem("Greek", "el"),
                new LanguageItem("Guarani", "gn"),
                new LanguageItem("Gujarati", "gu"),
                new LanguageItem("Haitian Creole", "ht"),
                new LanguageItem("Hausa", "ha"),
                new LanguageItem("Hawaiian", "haw"),
                new LanguageItem("Hebrew", "he"),
                new LanguageItem("Hindi", "hi"),
                new LanguageItem("Hmong", "hmn"),
                new LanguageItem("Hungarian", "hu"),
                new LanguageItem("Icelandic", "is"),
                new LanguageItem("Igbo", "ig"),
                new LanguageItem("Ilocano", "ilo"),
                new LanguageItem("Indonesian", "id"),
                new LanguageItem("Irish", "ga"),
                new LanguageItem("Italian", "it"),
                new LanguageItem("Japanese", "ja"),
                new LanguageItem("Javanese", "jv"),
                new LanguageItem("Kannada", "kn"),
                new LanguageItem("Kazakh", "kk"),
                new LanguageItem("Khmer", "km"),
                new LanguageItem("Kinyarwanda", "rw"),
                new LanguageItem("Konkani", "gom"),
                new LanguageItem("Korean", "ko"),
                new LanguageItem("Krio", "kri"),
                new LanguageItem("Kurdish", "ku"),
                new LanguageItem("Kurdish (Sorani)", "ckb"),
                new LanguageItem("Kyrgyz", "ky"),
                new LanguageItem("Lao", "lo"),
                new LanguageItem("Latin", "la"),
                new LanguageItem("Latvian", "lv"),
                new LanguageItem("Lingala", "ln"),
                new LanguageItem("Lithuanian", "lt"),
                new LanguageItem("Luganda", "lg"),
                new LanguageItem("Luxembourgish", "lb"),
                new LanguageItem("Macedonian", "mk"),
                new LanguageItem("Maithili", "mai"),
                new LanguageItem("Malagasy", "mg"),
                new LanguageItem("Malay", "ms"),
                new LanguageItem("Malayalam", "ml"),
                new LanguageItem("Maltese", "mt"),
                new LanguageItem("Maori", "mi"),
                new LanguageItem("Marathi", "mr"),
                new LanguageItem("Meiteilon (Manipuri)", "mni-Mtei"),
                new LanguageItem("Mizo", "lus"),
                new LanguageItem("Mongolian", "mn"),
                new LanguageItem("Myanmar (Burmese)", "my"),
                new LanguageItem("Nepali", "ne"),
                new LanguageItem("Norwegian", "no"),
                new LanguageItem("Nyanja (Chichewa)", "ny"),
                new LanguageItem("Odia (Oriya)", "or"),
                new LanguageItem("Oromo", "om"),
                new LanguageItem("Pashto", "ps"),
                new LanguageItem("Persian", "fa"),
                new LanguageItem("Polish", "pl"),
                new LanguageItem("Portuguese (Brazil)", "pt-BR"),
                new LanguageItem("Portuguese (Portugal)", "pt-PT"),
                new LanguageItem("Punjabi", "pa"),
                new LanguageItem("Quechua", "qu"),
                new LanguageItem("Romanian", "ro"),
                new LanguageItem("Russian", "ru"),
                new LanguageItem("Samoan", "sm"),
                new LanguageItem("Sanskrit", "sa"),
                new LanguageItem("Scots Gaelic", "gd"),
                new LanguageItem("Sepedi", "nso"),
                new LanguageItem("Serbian", "sr"),
                new LanguageItem("Sesotho", "st"),
                new LanguageItem("Shona", "sn"),
                new LanguageItem("Sindhi", "sd"),
                new LanguageItem("Sinhala (Sinhalese)", "si"),
                new LanguageItem("Slovak", "sk"),
                new LanguageItem("Slovenian", "sl"),
                new LanguageItem("Somali", "so"),
                new LanguageItem("Spanish", "es"),
                new LanguageItem("Sundanese", "su"),
                new LanguageItem("Swahili", "sw"),
                new LanguageItem("Swedish", "sv"),
                new LanguageItem("Tagalog (Filipino)", "tl"),
                new LanguageItem("Tajik", "tg"),
                new LanguageItem("Tamil", "ta"),
                new LanguageItem("Tatar", "tt"),
                new LanguageItem("Telugu", "te"),
                new LanguageItem("Thai", "th"),
                new LanguageItem("Tigrinya", "ti"),
                new LanguageItem("Tsonga", "ts"),
                new LanguageItem("Turkish", "tr"),
                new LanguageItem("Turkmen", "tk"),
                new LanguageItem("Twi (Akan)", "ak"),
                new LanguageItem("Ukrainian", "uk"),
                new LanguageItem("Urdu", "ur"),
                new LanguageItem("Uyghur", "ug"),
                new LanguageItem("Uzbek", "uz"),
                new LanguageItem("Vietnamese", "vi"),
                new LanguageItem("Welsh", "cy"),
                new LanguageItem("Xhosa", "xh"),
                new LanguageItem("Yiddish", "yi"),
                new LanguageItem("Yoruba", "yo"),
                new LanguageItem("Zulu", "zu")
            };
        }
    }

    public class LanguageItem
    {
        public string Name { get; set; }
        public string Code { get; set; }

        public LanguageItem(string name, string code)
        {
            Name = name;
            Code = code;
        }
        public override string ToString()
        {
            return Name;
        }
    }

    public class OpenAiSettings : TaggerSettings
    {
        public new string ConnectionAddress { get; set; } = string.Empty;

        // In-memory plaintext key. Never serialized directly.
        [JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;

        // Persisted (DPAPI-encrypted) form. The JSON property is kept as "ApiKey"
        // for backward compatibility: legacy plaintext values are read and then
        // re-written encrypted on the next save.
        [JsonProperty("ApiKey")]
        public string ApiKeyProtected
        {
            get => SecretProtector.Protect(ApiKey);
            set => ApiKey = SecretProtector.Unprotect(value);
        }

        public int RequestTimeout { get; set; } = 3600;
        public string SystemPrompt { get; set; } = string.Empty;
        public string UserPrompt { get; set; } = string.Empty;
        public float Temperature { get; set; } = -1;
        public float TopP { get; set; } = -1;
        public float RepeatPenalty { get; set; } = 0;
        public string Model { get; set; } = string.Empty;
        public string VisionModel { get; set; } = string.Empty;
        public bool SplitString { get; set; } = false;
        public string Splitter { get; set; } = ",";
        public int VideoFrameCount { get; set; } = 10;
        public int VideoFrameScale { get; set; } = 0;
        // Applied to LLM tag output in Form_LlmTagger (Tags mode).
        public bool ReplaceUnderscoresWithSpaces { get; set; } = true;

        public string ResolveVisionModel()
        {
            return !string.IsNullOrWhiteSpace(VisionModel) ? VisionModel : Model ?? string.Empty;
        }


        public OpenAiSettings()
        {
        }
    }

    public abstract class TaggerSettings
    {
        public string ConnectionAddress { get; set; } = "http://127.0.0.1:50051";
        public AutoTaggerSort SortMode { get; set; } = AutoTaggerSort.None;
        public NetworkResultSetMode SetMode { get; set; } = NetworkResultSetMode.AllWithReplacement;
        public TagFilteringMode TagFilteringMode { get; set; } = TagFilteringMode.None;
        public string TagFilter { get; set; } = "";
        public string TagPrefix { get; set; } = "";
        public string TagSuffix { get; set; } = "";
    }

    public class InterragatorSettings : TaggerSettings
    {
        public new string ConnectionAddress { get; set; } = "http://127.0.0.1:50051";
        public Dictionary<string, List<AdditionalParameters>> InterragatorParams { get; set; }
        public NetworkUnionMode UnionMode { get; set; } = NetworkUnionMode.Addition;
        public bool SerializeVramUsage { get; set; } = false;
        public bool SkipInternetRequests { get; set; } = false;
        public string CustomSystemPrompt { get; set; } = "";

        // Optional key sent as the X-Api-Key header; must match the AiApiServer
        // --api-key argument when the server is started with one.
        [JsonIgnore]
        public string ApiKey { get; set; } = string.Empty;

        [JsonProperty("ApiKey")]
        public string ApiKeyProtected
        {
            get => SecretProtector.Protect(ApiKey);
            set => ApiKey = SecretProtector.Unprotect(value);
        }

        public InterragatorSettings()
        {
            InterragatorParams = new Dictionary<string, List<AdditionalParameters>>();
        }
    }

    public class Wd14TaggerSettings : TaggerSettings
    {
        public string SelectedModelRepo { get; set; } = "SmilingWolf/wd-eva02-large-tagger-v3";
        public double Threshold { get; set; } = 0.52;
        public double CharacterThreshold { get; set; } = 0.85;
        public bool ReplaceUnderscoresWithSpaces { get; set; } = true;
        public HuggingFaceDownloadSource DownloadSource { get; set; } = HuggingFaceDownloadSource.HfMirror;
        public Dictionary<string, Wd14ModelThresholds> ModelThresholds { get; set; } = new Dictionary<string, Wd14ModelThresholds>(StringComparer.OrdinalIgnoreCase);

        public bool HasThresholdsForRepo(string repo)
        {
            return !string.IsNullOrWhiteSpace(repo)
                && ModelThresholds != null
                && ModelThresholds.ContainsKey(repo);
        }

        public (double Threshold, double CharacterThreshold) GetThresholdsForRepo(string repo)
        {
            if (HasThresholdsForRepo(repo))
            {
                Wd14ModelThresholds stored = ModelThresholds[repo];
                return (stored.Threshold, stored.CharacterThreshold);
            }

            if (string.Equals(repo, SelectedModelRepo, StringComparison.OrdinalIgnoreCase))
                return (Threshold, CharacterThreshold);

            Wd14ModelDefinition model = Wd14OnnxTaggerService.GetModel(repo);
            return (model.DefaultThreshold, model.DefaultCharacterThreshold);
        }

        public void SetThresholdsForRepo(string repo, double threshold, double characterThreshold)
        {
            ModelThresholds ??= new Dictionary<string, Wd14ModelThresholds>(StringComparer.OrdinalIgnoreCase);
            ModelThresholds[repo] = new Wd14ModelThresholds
            {
                Threshold = threshold,
                CharacterThreshold = characterThreshold
            };

            SelectedModelRepo = repo;
            Threshold = threshold;
            CharacterThreshold = characterThreshold;
        }

        public void EnsureLegacyThresholdMigrated()
        {
            ModelThresholds ??= new Dictionary<string, Wd14ModelThresholds>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(SelectedModelRepo)
                || ModelThresholds.ContainsKey(SelectedModelRepo))
            {
                return;
            }

            ModelThresholds[SelectedModelRepo] = new Wd14ModelThresholds
            {
                Threshold = Threshold,
                CharacterThreshold = CharacterThreshold
            };
        }
    }

    public sealed class Wd14ModelThresholds
    {
        public double Threshold { get; set; }
        public double CharacterThreshold { get; set; }
    }

    public class PixAiTaggerSettings : TaggerSettings
    {
        public double GeneralThreshold { get; set; } = 0.3;
        public double CharacterThreshold { get; set; } = 0.85;
        public bool ReplaceUnderscoresWithSpaces { get; set; } = true;
        public HuggingFaceDownloadSource DownloadSource { get; set; } = HuggingFaceDownloadSource.HfMirror;
    }

    public class AdditionalParameters
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Type { get; set; }
    }

    public class FontSettings
    {
        public string Name { get; set; }    = "Tahoma";
        public float Size { get; set; } = 14;
        public bool Bold { get; set; } = false;
        public byte GdiCharSet { get; set; } = 1;
        public bool Italic { get; set; } = false;
        public bool Strikeout { get; set; } = false;
        public bool Underline { get; set; } = false;

        public FontSettings() { }


        public Font GetFont()
        {
            List<FontStyle> resStyle = new List<FontStyle>();
            resStyle.Add(FontStyle.Regular);
            if (Bold)
                resStyle.Add(FontStyle.Bold);
            if (Italic)
                resStyle.Add(FontStyle.Italic);
            if(Strikeout)
                resStyle.Add(FontStyle.Strikeout);
            if(Underline) 
                resStyle.Add(FontStyle.Underline);
            return new Font(Name, Size, resStyle.Aggregate((x, y) => x |= y), GraphicsUnit.Point, GdiCharSet, false);
        }

        public static FontSettings Create(Font fnt)
        {
            FontSettings fs = new FontSettings();
            fs.Name = fnt.Name;
            fs.Underline = fnt.Underline;
            fs.GdiCharSet = fnt.GdiCharSet;
            fs.Bold = fnt.Bold;
            fs.Italic = fnt.Italic;
            fs.Size = fnt.Size;
            fs.Strikeout = fnt.Strikeout;
            return fs;
        }

        public override string ToString()
        {
            return $"{Name}; {Size}pt;";
        }
    }
}
