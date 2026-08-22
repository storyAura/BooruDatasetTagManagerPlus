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
        public string TranslationLanguage { get; set; } = "zh-CN";
        /// <summary>
        /// Set after the one-shot leftover "ru" remap so a user who later
        /// picks Russian on a Chinese UI keeps that choice.
        /// </summary>
        public bool TranslationLanguageMigratedFromLegacyRu { get; set; }
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

        // Embedded dataset preview panel (left sidebar, PreviewInMainWindow mode).
        public bool DatasetPreviewExpanded { get; set; } = true;
        // Load Data/danbooru_character_tags.csv for character classification
        // and 译名 translation (330k rows — the toggle exists for slow machines).
        public bool MatchCharacterTags { get; set; } = true;
        // Category-grouped ordering of the all-tags list (off = alphabetical).
        public bool AllTagsCategorySort { get; set; } = false;
        // Dataset browser flat view: ignore folder grouping and show every
        // image of the current list as one flat sequence.
        public bool DatasetBrowserFlatView { get; set; } = false;
        public DatasetManager.OrderType DatasetOrderType { get; set; } = DatasetManager.OrderType.Name;
        // Tag-consistency fixer: when false (default), rare child variants are
        // left alone. When true, a child below TagFixChildThreshold folds into
        // its parent. Opt-in from the test module; 0 on the threshold still
        // disables the rule even if this is checked.
        public bool TagFixFoldRareChildren { get; set; } = false;
        public int TagFixChildThreshold { get; set; } = 30;
        // Sticky category sort for the image-tags pane: while on, every newly
        // selected image's tags are re-sorted on load (mutates tag order, so
        // visited images whose order changes become modified).
        public bool ImageTagsCategorySort { get; set; } = false;
        // Shows the Debug menu and enables DebugLog output (debug.log).
        public bool DebugMode { get; set; } = false;
        // Dataset grid columns hidden by default; the header right-click menu
        // toggles them and persists here. Replace (not append) on deserialize.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> DatasetHiddenColumns { get; set; } = GetDefaultDatasetHiddenColumns();

        public static List<string> GetDefaultDatasetHiddenColumns()
        {
            return new List<string> { "ImageFilePath", "ImageModifyTime", "TagsModifyTime", "FileType" };
        }

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
        // Single- vs multi-character audit and the per-slot genders used by
        // the multi mode's subject-count tags (2girls / multiple girls, ...).
        public CharacterTagAuditSubjectMode CharacterTagAuditSubjectMode { get; set; } = CharacterTagAuditSubjectMode.Single;
        public CharacterGender[] CharacterTagAuditGenders { get; set; } = NormalizeAuditGenders(null);

        // A hand-edited or older config can carry a missing/short/oversized
        // array; the wizard indexes it by slot, so pad and trim it on load.
        public static CharacterGender[] NormalizeAuditGenders(CharacterGender[] stored)
        {
            var genders = new CharacterGender[CharacterTagDualAuditService.MaxProfiles];
            for (int i = 0; stored != null && i < Math.Min(stored.Length, genders.Length); i++)
                genders[i] = stored[i];
            return genders;
        }
        public string AutoTagProviderId { get; set; } = "openai-compatible";
        public string FfmpegPath { get; set; } = string.Empty;
        // Video extract: random percentage sample (1–100, default 10).
        public int VideoExtractRandomPercent { get; set; } = 10;
        public RandomFrameSampleMode VideoExtractRandomMode { get; set; } = RandomFrameSampleMode.Distributed;
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

        // Last-used state of Tools → Multi-crop.
        public ResolutionPrepMode ResolutionPrepMode { get; set; } = ResolutionPrepMode.ScaleOnly;
        public ResolutionPrepSource ResolutionPrepSource { get; set; } = ResolutionPrepSource.Selected;
        public int ResolutionPrepAspectWidth { get; set; } = 1;
        public int ResolutionPrepAspectHeight { get; set; } = 1;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> ResolutionPrepSelectedGears { get; set; } = new List<int> { 1024 };
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<int> ResolutionPrepCustomGears { get; set; } = new List<int>();
        public bool ResolutionPrepSharpen { get; set; } = true;
        public int ResolutionPrepRandomCount { get; set; } = 1;
        public string YoloPersonModelId { get; set; } = string.Empty;
        public string YoloPersonImportPath { get; set; } = string.Empty;
        public float YoloPersonConfidence { get; set; } = YoloPersonDetectorService.DefaultConfidence;

        // Last-used state of Tools → Pre-bucket.
        public ResolutionPrepSource PreBucketSource { get; set; } = ResolutionPrepSource.Selected;
        public int PreBucketResolutionWidth { get; set; } = PreBucketMath.DefaultResolution;
        public int PreBucketResolutionHeight { get; set; } = PreBucketMath.DefaultResolution;
        public bool PreBucketEnableBucket { get; set; } = true;
        public int PreBucketMinReso { get; set; } = PreBucketMath.DefaultMinReso;
        public int PreBucketMaxReso { get; set; } = PreBucketMath.DefaultMaxReso;
        public int PreBucketResoSteps { get; set; } = PreBucketMath.DefaultSteps;
        public int PreBucketTargetCount { get; set; }
        public bool PreBucketAllowUpscale { get; set; }
        public int PreBucketRepeats { get; set; } = 1;
        public int PreBucketBatchSize { get; set; } = 4;
        public int PreBucketEpochs { get; set; } = 1;
        public string PreBucketOutputFolder { get; set; } = string.Empty;

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
        // LLM API site profiles (endpoint + rotating keys + per-site models).
        // The active one is mirrored into OpenAiAutoTagger's flat fields on
        // save, so every existing consumer keeps reading those.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<LlmApiProfile> LlmApiProfiles { get; set; } = new List<LlmApiProfile>();
        public int LlmApiProfileIndex { get; set; }

        /// <summary>API keys of the active profile (legacy flat key as fallback).</summary>
        public IReadOnlyList<string> GetActiveLlmApiKeys()
        {
            if (LlmApiProfiles != null && LlmApiProfiles.Count > 0)
            {
                int index = LlmApiProfileLogic.ClampIndex(LlmApiProfileIndex, LlmApiProfiles.Count);
                return LlmApiProfileLogic.SanitizeTokens(LlmApiProfiles[index].Tokens);
            }
            return LlmApiProfileLogic.SanitizeTokens(new[] { OpenAiAutoTagger?.ApiKey });
        }

        public string AiServerSetPromptTemplate { get; set; } = AiPromptTemplateCatalog.DanbooruTag;
        public string AiServerSetPromptTemplateId { get; set; } = AiPromptTemplateCatalog.DanbooruTagId;
        public List<AiPromptTemplateSettings> AiServerSetPromptTemplates { get; set; } =
            AiPromptTemplateCatalog.CreateDefaultSettings().Select(template => template.Clone()).ToList();

        private string[] _tagsFilesExt = { "txt", "caption" };

        private string settingsFile;

        public const string SettingsFileName = "settings.json";
        public const string ProductSettingsFolderName = "BooruDatasetTagManagerPlus";

        /// <summary>Resolved path of the settings file this instance loads/saves.</summary>
        [JsonIgnore]
        public string SettingsFilePath => settingsFile;

        /// <summary>
        /// Per-user Documents folder that holds the shared settings.json so
        /// Debug / Release / dist copies of the app read the same config.
        /// </summary>
        public static string GetDefaultDocumentsDirectory()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
                return null;
            return Path.Combine(documents, ProductSettingsFolderName);
        }

        /// <summary>
        /// Picks the user-settings directory. Prefer
        /// <paramref name="documentsDir"/> (or My Documents\BooruDatasetTagManagerPlus);
        /// if that folder has no settings.json yet, copy the portable file
        /// from <paramref name="startupPath"/> once. When the Documents file
        /// already exists but has no API config, and the exe-adjacent file
        /// still has a recognizable endpoint or keys, only those API fields
        /// are copied in. When Documents is unavailable the startup directory
        /// is used as a fallback. Tests pass an explicit documentsDir so they
        /// never touch the real Documents folder.
        /// </summary>
        public static string ResolveUserSettingsDirectory(string startupPath, string documentsDir = null)
        {
            string dest = documentsDir ?? GetDefaultDocumentsDirectory();
            if (string.IsNullOrWhiteSpace(dest))
                return startupPath ?? string.Empty;

            try
            {
                Directory.CreateDirectory(dest);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AppSettings.ResolveUserSettingsDirectory: cannot create '{dest}': {ex}");
                return string.IsNullOrWhiteSpace(startupPath) ? dest : startupPath;
            }

            string destFile = Path.Combine(dest, SettingsFileName);
            string legacy = string.IsNullOrWhiteSpace(startupPath)
                ? null
                : Path.Combine(startupPath, SettingsFileName);
            if (!File.Exists(destFile) && legacy != null && File.Exists(legacy))
            {
                try
                {
                    File.Copy(legacy, destFile, overwrite: false);
                    string legacyBak = legacy + ".bak";
                    string destBak = destFile + ".bak";
                    if (File.Exists(legacyBak) && !File.Exists(destBak))
                        File.Copy(legacyBak, destBak, overwrite: false);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"AppSettings.ResolveUserSettingsDirectory: migrate failed: {ex}");
                }
            }
            else if (File.Exists(destFile))
            {
                TryMigrateLegacyApiConfig(startupPath, destFile);
            }

            return dest;
        }

        /// <summary>
        /// True when <paramref name="settings"/> still carries a usable LLM/API
        /// site: a profile endpoint or token, the flat OpenAI endpoint/key, or
        /// a HuggingFace token.
        /// </summary>
        public static bool HasRecognizableApiConfig(AppSettings settings)
        {
            if (settings == null)
                return false;
            if (settings.LlmApiProfiles != null)
            {
                foreach (LlmApiProfile profile in settings.LlmApiProfiles)
                {
                    if (profile == null)
                        continue;
                    if (LlmApiProfileLogic.SanitizeTokens(profile.Tokens).Count > 0)
                        return true;
                    if (!string.IsNullOrWhiteSpace(profile.Endpoint))
                        return true;
                }
            }
            OpenAiSettings openAi = settings.OpenAiAutoTagger;
            if (openAi != null
                && (!string.IsNullOrWhiteSpace(openAi.ApiKey)
                    || !string.IsNullOrWhiteSpace(openAi.ConnectionAddress)))
            {
                return true;
            }
            return !string.IsNullOrWhiteSpace(settings.HuggingFaceToken);
        }

        /// <summary>
        /// Copies LLM/API profiles, the mirrored flat OpenAI fields, audit
        /// model, HF token and prompt templates from <paramref name="from"/>
        /// onto <paramref name="to"/>. UI preferences are left alone.
        /// </summary>
        public static void CopyApiConfig(AppSettings from, AppSettings to)
        {
            if (from == null || to == null)
                return;
            to.LlmApiProfiles = (from.LlmApiProfiles ?? new List<LlmApiProfile>())
                .Where(profile => profile != null)
                .Select(profile => profile.Clone())
                .ToList();
            to.LlmApiProfileIndex = from.LlmApiProfileIndex;
            if (from.OpenAiAutoTagger != null)
            {
                to.OpenAiAutoTagger ??= new OpenAiSettings();
                to.OpenAiAutoTagger.ConnectionAddress = from.OpenAiAutoTagger.ConnectionAddress ?? string.Empty;
                to.OpenAiAutoTagger.ApiKey = from.OpenAiAutoTagger.ApiKey ?? string.Empty;
                to.OpenAiAutoTagger.Model = from.OpenAiAutoTagger.Model ?? string.Empty;
                to.OpenAiAutoTagger.VisionModel = from.OpenAiAutoTagger.VisionModel ?? string.Empty;
                to.OpenAiAutoTagger.RequestTimeout = from.OpenAiAutoTagger.RequestTimeout;
            }
            to.CharacterTagAuditModel = from.CharacterTagAuditModel ?? string.Empty;
            to.HuggingFaceToken = from.HuggingFaceToken ?? string.Empty;
            to.AiServerSetPromptTemplate = from.AiServerSetPromptTemplate;
            to.AiServerSetPromptTemplateId = from.AiServerSetPromptTemplateId;
            if (from.AiServerSetPromptTemplates != null)
            {
                to.AiServerSetPromptTemplates = from.AiServerSetPromptTemplates
                    .Where(template => template != null)
                    .Select(template => template.Clone())
                    .ToList();
            }
            LlmApiProfileLogic.EnsureLegacyProfile(to);
            LlmApiProfileLogic.ApplyActiveProfile(to);
        }

        private static void TryMigrateLegacyApiConfig(string startupPath, string destFile)
        {
            try
            {
                AppSettings destSettings = TryLoadSettingsFile(destFile);
                if (destSettings == null || HasRecognizableApiConfig(destSettings))
                    return;
                AppSettings legacy = FindLegacyApiSettings(startupPath);
                if (legacy == null)
                    return;
                CopyApiConfig(legacy, destSettings);
                SafeFile.WriteAllTextWithBackup(destFile, JsonConvert.SerializeObject(destSettings));
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AppSettings.TryMigrateLegacyApiConfig: {ex}");
            }
        }

        private static AppSettings FindLegacyApiSettings(string startupPath)
        {
            if (string.IsNullOrWhiteSpace(startupPath))
                return null;
            string primary = Path.Combine(startupPath, SettingsFileName);
            foreach (string path in new[] { primary, primary + ".bak" })
            {
                AppSettings loaded = TryLoadSettingsFile(path);
                if (HasRecognizableApiConfig(loaded))
                    return loaded;
            }
            return null;
        }


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
            settingsFile = Path.Combine(appDir, SettingsFileName);
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
                AppSettings tempSettings = TryLoadSettingsFile(settingsFile);
                if (tempSettings == null)
                {
                    // Preserve the unreadable file before resetting so users
                    // can recover hand-written endpoints/keys from it.
                    try { File.Copy(settingsFile, settingsFile + ".corrupt", true); } catch { }

                    // SaveSettings keeps the complete previous file as .bak:
                    // recover from it before falling back to defaults.
                    tempSettings = TryLoadSettingsFile(settingsFile + ".bak");
                    try
                    {
                        File.WriteAllText(settingsFile, JsonConvert.SerializeObject(tempSettings ?? this));
                    }
                    catch (Exception writeEx)
                    {
                        // Disk read-only / no permission: keep going with in-memory settings.
                        Trace.WriteLine($"AppSettings.LoadData: failed to rewrite settings file: {writeEx}");
                    }
                }

                // Could not load or recover a valid settings file: keep constructor defaults.
                if (tempSettings == null)
                    return;

                TranslationLanguage = tempSettings.TranslationLanguage;
                TranslationLanguageMigratedFromLegacyRu = tempSettings.TranslationLanguageMigratedFromLegacyRu;
                PreviewSize = tempSettings.PreviewSize <= 0 ? 130 : tempSettings.PreviewSize;
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
                GridViewRowHeight = tempSettings.GridViewRowHeight <= 0 ? 29 : tempSettings.GridViewRowHeight;
                GridViewFont = tempSettings.GridViewFont;
                AutocompleteFont = tempSettings.AutocompleteFont;
                AutoSort = tempSettings.AutoSort || false;
                Language = tempSettings.Language;
                PreviewType = tempSettings.PreviewType;
                DefaultTagsFileExtension = tempSettings.DefaultTagsFileExtension;
                CaptionFileExtensions = tempSettings.CaptionFileExtensions;
                TagImagesGridSize = tempSettings.TagImagesGridSize <= 0 ? 400 : tempSettings.TagImagesGridSize;
                CacheOpenImages = tempSettings.CacheOpenImages;
                DatasetPreviewExpanded = tempSettings.DatasetPreviewExpanded;
                MatchCharacterTags = tempSettings.MatchCharacterTags;
                AllTagsCategorySort = tempSettings.AllTagsCategorySort;
                DatasetBrowserFlatView = tempSettings.DatasetBrowserFlatView;
                DatasetOrderType = Enum.IsDefined(typeof(DatasetManager.OrderType), tempSettings.DatasetOrderType)
                    ? tempSettings.DatasetOrderType
                    : DatasetManager.OrderType.Name;
                TagFixFoldRareChildren = tempSettings.TagFixFoldRareChildren;
                TagFixChildThreshold = Math.Max(0, tempSettings.TagFixChildThreshold);
                ImageTagsCategorySort = tempSettings.ImageTagsCategorySort;
                DebugMode = tempSettings.DebugMode;
                DatasetHiddenColumns = tempSettings.DatasetHiddenColumns ?? GetDefaultDatasetHiddenColumns();
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
                CharacterTagAuditSubjectMode = tempSettings.CharacterTagAuditSubjectMode;
                CharacterTagAuditGenders = NormalizeAuditGenders(tempSettings.CharacterTagAuditGenders);
                ImageEditorSaveMode = tempSettings.ImageEditorSaveMode;
                AutoTagProviderId = string.IsNullOrWhiteSpace(tempSettings.AutoTagProviderId)
                    ? "openai-compatible"
                    : tempSettings.AutoTagProviderId;
                FfmpegPath = tempSettings.FfmpegPath ?? string.Empty;
                VideoExtractRandomPercent = tempSettings.VideoExtractRandomPercent <= 0
                    ? 10
                    : Math.Clamp(tempSettings.VideoExtractRandomPercent, 1, 100);
                VideoExtractRandomMode = tempSettings.VideoExtractRandomMode == RandomFrameSampleMode.Regional
                    ? RandomFrameSampleMode.Regional
                    : RandomFrameSampleMode.Distributed;
                Wd14Tagger = tempSettings.Wd14Tagger ?? new Wd14TaggerSettings();
                Wd14Tagger.EnsureLegacyThresholdMigrated();
                PixAiTagger = tempSettings.PixAiTagger ?? new PixAiTaggerSettings();
                OnnxTaggerLastModelId = tempSettings.OnnxTaggerLastModelId ?? string.Empty;
                BackgroundRemoverModelId = tempSettings.BackgroundRemoverModelId ?? string.Empty;
                BackgroundRemoverFillBackground = tempSettings.BackgroundRemoverFillBackground;
                BackgroundRemoverColorArgb = tempSettings.BackgroundRemoverColorArgb;
                BackgroundRemoverReplaceOriginal = tempSettings.BackgroundRemoverReplaceOriginal;
                AllTagsDoubleClickAction = tempSettings.AllTagsDoubleClickAction;
                ResolutionPrepMode = Enum.IsDefined(typeof(ResolutionPrepMode), tempSettings.ResolutionPrepMode)
                    ? tempSettings.ResolutionPrepMode
                    : ResolutionPrepMode.ScaleOnly;
                ResolutionPrepSource = Enum.IsDefined(typeof(ResolutionPrepSource), tempSettings.ResolutionPrepSource)
                    ? tempSettings.ResolutionPrepSource
                    : ResolutionPrepSource.Selected;
                ResolutionPrepAspectWidth = tempSettings.ResolutionPrepAspectWidth <= 0 ? 1 : tempSettings.ResolutionPrepAspectWidth;
                ResolutionPrepAspectHeight = tempSettings.ResolutionPrepAspectHeight <= 0 ? 1 : tempSettings.ResolutionPrepAspectHeight;
                ResolutionPrepSelectedGears = tempSettings.ResolutionPrepSelectedGears == null || tempSettings.ResolutionPrepSelectedGears.Count == 0
                    ? new List<int> { 1024 }
                    : tempSettings.ResolutionPrepSelectedGears;
                ResolutionPrepCustomGears = tempSettings.ResolutionPrepCustomGears ?? new List<int>();
                ResolutionPrepSharpen = tempSettings.ResolutionPrepSharpen;
                ResolutionPrepRandomCount = ResolutionPrepMath.ClampRandomCount(tempSettings.ResolutionPrepRandomCount);
                YoloPersonModelId = tempSettings.YoloPersonModelId ?? string.Empty;
                YoloPersonImportPath = tempSettings.YoloPersonImportPath ?? string.Empty;
                YoloPersonConfidence = tempSettings.YoloPersonConfidence <= 0f || tempSettings.YoloPersonConfidence > 1f
                    ? YoloPersonDetectorService.DefaultConfidence
                    : tempSettings.YoloPersonConfidence;
                PreBucketSettings preBucket = PreBucketMath.Normalize(new PreBucketSettings
                {
                    ResolutionWidth = tempSettings.PreBucketResolutionWidth,
                    ResolutionHeight = tempSettings.PreBucketResolutionHeight,
                    EnableBucket = true,
                    MinBucketReso = tempSettings.PreBucketMinReso,
                    MaxBucketReso = tempSettings.PreBucketMaxReso,
                    BucketResoSteps = tempSettings.PreBucketResoSteps,
                    TargetBucketCount = tempSettings.PreBucketTargetCount,
                    AllowUpscale = tempSettings.PreBucketAllowUpscale,
                    Repeats = tempSettings.PreBucketRepeats,
                    BatchSize = tempSettings.PreBucketBatchSize,
                    Epochs = tempSettings.PreBucketEpochs,
                    OutputRoot = tempSettings.PreBucketOutputFolder
                });
                PreBucketSource = Enum.IsDefined(typeof(ResolutionPrepSource), tempSettings.PreBucketSource)
                    ? tempSettings.PreBucketSource
                    : ResolutionPrepSource.Selected;
                PreBucketResolutionWidth = preBucket.ResolutionWidth;
                PreBucketResolutionHeight = preBucket.ResolutionHeight;
                PreBucketEnableBucket = preBucket.EnableBucket;
                PreBucketMinReso = preBucket.MinBucketReso;
                PreBucketMaxReso = preBucket.MaxBucketReso;
                PreBucketResoSteps = preBucket.BucketResoSteps;
                PreBucketTargetCount = preBucket.TargetBucketCount;
                PreBucketAllowUpscale = preBucket.AllowUpscale;
                PreBucketRepeats = preBucket.Repeats;
                PreBucketBatchSize = preBucket.BatchSize;
                PreBucketEpochs = preBucket.Epochs;
                PreBucketOutputFolder = preBucket.OutputRoot ?? string.Empty;
                HuggingFaceToken = tempSettings.HuggingFaceToken ?? string.Empty;
                NormalizeLegacyTranslationLanguage();

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
                LlmApiProfiles = tempSettings.LlmApiProfiles ?? new List<LlmApiProfile>();
                LlmApiProfileIndex = tempSettings.LlmApiProfileIndex;
                LlmApiProfileLogic.EnsureLegacyProfile(this);
                AiPromptTemplateLibrary promptLibrary = AiPromptTemplateLibrary.Create(
                    tempSettings.AiServerSetPromptTemplates,
                    tempSettings.AiServerSetPromptTemplateId,
                    tempSettings.AiServerSetPromptTemplate);
                AiServerSetPromptTemplates = promptLibrary.CreateSnapshot();
                AiServerSetPromptTemplateId = promptLibrary.SelectedTemplateId;
                AiServerSetPromptTemplate = promptLibrary.SelectedTemplate.Name;
                OpenAiAutoTagger.SystemPrompt = promptLibrary.SelectedTemplate.SystemPrompt;

                // "Hotkeys":{"Items":null} or [null] entries are valid JSON and
                // used to NRE here before the message loop even started.
                if (tempSettings.Hotkeys?.Items != null)
                {
                    foreach (var item in tempSettings.Hotkeys.Items)
                    {
                        if (item == null)
                            continue;
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

        private static AppSettings TryLoadSettingsFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                string migratedJson = AiServerSetSettingsMigration.MigrateJson(File.ReadAllText(path));
                return JsonConvert.DeserializeObject<AppSettings>(migratedJson);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"AppSettings.LoadData: failed to parse '{path}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Upstream defaulted the translation target to Russian. This fork's UI
        /// defaults to zh-CN, and a Documents settings file created with the
        /// old constructor default still carries "ru" even when the user never
        /// picked Russian. Remap that leftover so built-in Chinese data is used.
        /// </summary>
        public void NormalizeLegacyTranslationLanguage()
        {
            if (TranslationLanguageMigratedFromLegacyRu)
                return;

            if (IsLegacyRussianTranslationDefault(Language, TranslationLanguage))
            {
                TranslationLanguage = string.Equals(Language, "zh-TW", StringComparison.OrdinalIgnoreCase)
                    ? "zh-TW"
                    : "zh-CN";
            }

            TranslationLanguageMigratedFromLegacyRu = true;
        }

        public static bool IsLegacyRussianTranslationDefault(string language, string translationLanguage)
        {
            return ChineseTagLookupService.IsChineseLanguage(language)
                && string.Equals(translationLanguage, "ru", StringComparison.OrdinalIgnoreCase);
        }

        public void SaveSettings()
        {
            try
            {
                // Serialize first: SecretProtector.Protect throws on DPAPI failure
                // so we never overwrite settings.json with plaintext API keys.
                string json = JsonConvert.SerializeObject(this);
                // Atomic write + .bak: a crash/power loss mid-write used to truncate
                // settings.json, and the next startup silently reset all settings.
                SafeFile.WriteAllTextWithBackup(settingsFile, json);
            }
            catch (Exception ex)
            {
                // Read-only dir / locked file / encrypt failure: keep running with
                // in-memory settings and leave the previous file untouched.
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
        public AutoTaggerSort SortMode { get; set; } = AutoTaggerSort.Confidence;
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
