using BooruDatasetTagManager.AiApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    // Unified LLM tagging window. Modeled on Form_OnnxTagger: same all-in-code layout
    // and job-control pattern, but the ONNX inference is replaced by the external
    // OpenAI-compatible vision LLM, and a mode selector folds the former standalone
    // TAG2NL feature in as the "Tags -> natural language" output mode. The prompt
    // template and the full auto-tagger settings are reachable from here so the
    // separate "auto-tagger settings" window is no longer needed for day-to-day use.
    public sealed class Form_LlmTagger : Form
    {
        private readonly MainForm owner;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Wd14OnnxTaggerService wd14Service = new Wd14OnnxTaggerService();

        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceAllImages = new RadioButton();
        private readonly RadioButton radioSourceFolder = new RadioButton();
        private readonly bool folderSourceAvailable =
            !string.IsNullOrEmpty(Program.DataManager?.ActiveFolder);

        private readonly ComboBox comboMode = new ComboBox();
        private readonly NumericUpDown numericConcurrency = new NumericUpDown();
        private readonly ComboBox comboPromptTemplate = new ComboBox();
        private readonly ComboBox comboVisionModel = new ComboBox();
        private readonly ComboBox comboSetMode = new ComboBox();
        private readonly ComboBox comboSortMode = new ComboBox();
        private readonly TextBox textTagPrefix = new TextBox();
        private readonly TextBox textTagSuffix = new TextBox();
        private readonly CheckBox checkReplaceUnderscores = new CheckBox();
        private readonly ComboBox comboFormat = new ComboBox();
        private readonly ComboBox comboCaptionTarget = new ComboBox();
        private readonly CheckBox checkAutoOnnx = new CheckBox();
        private readonly CheckBox checkReprocessExisting = new CheckBox();
        private readonly Button buttonOpenSettings = new Button();
        private readonly Button buttonTaggerSettings = new Button();
        private readonly Label labelModelStatus = new Label();

        private readonly Label labelMode = new Label();
        private readonly Label labelConcurrency = new Label();
        private readonly Label labelPromptTemplate = new Label();
        private readonly Label labelVisionModel = new Label();
        private readonly Label labelSetMode = new Label();
        private readonly Label labelSortMode = new Label();
        private readonly Label labelTagPrefix = new Label();
        private readonly Label labelTagSuffix = new Label();
        private readonly Label labelFormat = new Label();
        private readonly Label labelCaptionTarget = new Label();

        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonRun = new Button();
        private readonly Button buttonCancelJob = new Button();
        private readonly Button buttonClose = new Button();

        private readonly List<string> promptTemplateIds = new List<string>();

        private CancellationTokenSource jobCancellation;
        // Guards a second Run click while the job is spinning up.
        private bool runJobActive;
        // A close requested mid-job is deferred until the batch unwinds.
        private bool closeAfterJob;
        private bool loadingSettings;

        public Form_LlmTagger(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            ApplyLanguage();
            ApplyButtonStyles();
            LoadSettingsToControls();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
        }

        private void InitializeComponent()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            Text = "LLM Tagger";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(780, 720);
            ClientSize = new Size(860, 780);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(72F)));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(108F)));

            mainLayout.Controls.Add(BuildSourceGroup(), 0, 0);
            mainLayout.Controls.Add(BuildOptionsGroup(), 0, 1);
            mainLayout.Controls.Add(BuildBottomPanel(), 0, 2);
            Controls.Add(mainLayout);

            comboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboMode.Items.Add(I18n.GetText("LlmTaggerModeTags"));
            comboMode.Items.Add(I18n.GetText("LlmTaggerModeNaturalLanguage"));

            comboPromptTemplate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboVisionModel.DropDownStyle = ComboBoxStyle.DropDownList;

            comboSetMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSetMode.Items.AddRange(Extensions.GetFriendlyEnumValues<NetworkResultSetMode>());

            comboSortMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSortMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutoTaggerSort>());

            comboFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            comboFormat.Items.Add(I18n.GetText("LlmTaggerFormatTagsAndNl"));
            comboFormat.Items.Add(I18n.GetText("LlmTaggerFormatNlOnly"));

            comboCaptionTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            comboCaptionTarget.Items.Add(I18n.GetText("LlmTaggerCaptionSeparate"));
            comboCaptionTarget.Items.Add(I18n.GetText("LlmTaggerCaptionInPlace"));

            numericConcurrency.Minimum = 1;
            numericConcurrency.Maximum = 100;

            radioSourceSelected.Checked = true;

            comboMode.SelectedIndexChanged += (_, _) =>
            {
                UpdateModeUi();
                PersistSettings();
            };
            comboPromptTemplate.SelectedIndexChanged += (_, _) => OnPromptTemplateChanged();
            comboVisionModel.SelectedIndexChanged += (_, _) =>
            {
                if (loadingSettings)
                    return;
                if (comboVisionModel.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
                    Program.Settings.OpenAiAutoTagger.VisionModel = model;
                PersistSettings();
                UpdateIdleStatus();
            };
            numericConcurrency.ValueChanged += (_, _) => PersistSettings();
            comboSetMode.SelectedIndexChanged += (_, _) => PersistSettings();
            comboSortMode.SelectedIndexChanged += (_, _) => PersistSettings();
            textTagPrefix.TextChanged += (_, _) => PersistSettings();
            textTagSuffix.TextChanged += (_, _) => PersistSettings();
            checkReplaceUnderscores.CheckedChanged += (_, _) => PersistSettings();
            comboFormat.SelectedIndexChanged += (_, _) => PersistSettings();
            comboCaptionTarget.SelectedIndexChanged += (_, _) =>
            {
                PersistSettings();
                UpdateModeUi();
            };
            checkAutoOnnx.CheckedChanged += (_, _) => PersistSettings();
            checkReprocessExisting.CheckedChanged += (_, _) => PersistSettings();

            buttonOpenSettings.Click += (_, _) => OpenLlmSettings();
            buttonTaggerSettings.Click += (_, _) => OpenTaggerSettings();
            buttonRun.Click += async (_, _) => await RunJobAsync().ConfigureAwait(true);
            buttonCancelJob.Click += (_, _) => jobCancellation?.Cancel();
            buttonClose.Click += (_, _) => Close();
            // Esc closes (deferred while a job runs, via FormClosing).
            CancelButton = buttonClose;

            Shown += async (_, _) =>
            {
                UpdateIdleStatus();
                await RefreshModelListAsync().ConfigureAwait(true);
            };

            FormClosing += (_, e) =>
            {
                if (runJobActive || IsJobRunning())
                {
                    e.Cancel = true;
                    closeAfterJob = true;
                    jobCancellation?.Cancel();
                }
            };

            FormClosed += (_, _) =>
            {
                PersistSettings();
                toolTip.Dispose();
                wd14Service.Dispose();
            };
        }

        private GroupBox BuildSourceGroup()
        {
            var groupSource = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupSource",
                Padding = new Padding(10, 4, 10, 6)
            };

            var panelRadios = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            radioSourceSelected.AutoSize = true;
            radioSourceSelected.Margin = new Padding(0, 0, 16, 0);
            radioSourceAllImages.AutoSize = true;
            radioSourceAllImages.Margin = new Padding(0, 0, 16, 0);
            radioSourceFolder.AutoSize = true;
            radioSourceFolder.Enabled = folderSourceAvailable;

            panelRadios.Controls.Add(radioSourceSelected);
            panelRadios.Controls.Add(radioSourceAllImages);
            panelRadios.Controls.Add(radioSourceFolder);
            groupSource.Controls.Add(panelRadios);
            return groupSource;
        }

        private GroupBox BuildOptionsGroup()
        {
            var groupOptions = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupOptions",
                Padding = new Padding(10, 4, 10, 6)
            };

            foreach (ComboBox combo in new[] { comboMode, comboPromptTemplate, comboVisionModel, comboSetMode, comboSortMode, comboFormat, comboCaptionTarget })
            {
                combo.Dock = DockStyle.Fill;
                combo.Margin = new Padding(0, 0, 8, 0);
            }

            numericConcurrency.Dock = DockStyle.Fill;
            numericConcurrency.Margin = new Padding(0, 0, 8, 0);
            textTagPrefix.Dock = DockStyle.Fill;
            textTagPrefix.Margin = new Padding(0, 0, 8, 0);
            textTagSuffix.Dock = DockStyle.Fill;

            foreach (Label label in new[] { labelMode, labelConcurrency, labelPromptTemplate, labelVisionModel, labelSetMode, labelSortMode, labelTagPrefix, labelTagSuffix, labelFormat, labelCaptionTarget })
            {
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleLeft;
            }

            checkReplaceUnderscores.AutoSize = true;
            checkReplaceUnderscores.Dock = DockStyle.Fill;
            checkAutoOnnx.AutoSize = true;
            checkAutoOnnx.Dock = DockStyle.Fill;
            checkReprocessExisting.AutoSize = true;
            checkReprocessExisting.Dock = DockStyle.Fill;

            labelModelStatus.Dock = DockStyle.Fill;
            labelModelStatus.AutoEllipsis = true;
            labelModelStatus.TextAlign = ContentAlignment.MiddleLeft;

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            buttonsPanel.Controls.Add(buttonOpenSettings);
            buttonsPanel.Controls.Add(buttonTaggerSettings);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 10
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLayout(120F)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLayout(120F)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            for (int i = 0; i < 9; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(labelMode, 0, 0);
            layout.Controls.Add(comboMode, 1, 0);
            layout.Controls.Add(labelConcurrency, 2, 0);
            layout.Controls.Add(numericConcurrency, 3, 0);

            layout.Controls.Add(labelPromptTemplate, 0, 1);
            layout.Controls.Add(comboPromptTemplate, 1, 1);
            layout.SetColumnSpan(comboPromptTemplate, 3);

            layout.Controls.Add(labelVisionModel, 0, 2);
            layout.Controls.Add(comboVisionModel, 1, 2);
            layout.SetColumnSpan(comboVisionModel, 3);

            layout.Controls.Add(labelSetMode, 0, 3);
            layout.Controls.Add(comboSetMode, 1, 3);
            layout.Controls.Add(labelSortMode, 2, 3);
            layout.Controls.Add(comboSortMode, 3, 3);

            layout.Controls.Add(labelTagPrefix, 0, 4);
            layout.Controls.Add(textTagPrefix, 1, 4);
            layout.Controls.Add(labelTagSuffix, 2, 4);
            layout.Controls.Add(textTagSuffix, 3, 4);

            layout.Controls.Add(checkReplaceUnderscores, 0, 5);
            layout.SetColumnSpan(checkReplaceUnderscores, 4);

            layout.Controls.Add(labelFormat, 0, 6);
            layout.Controls.Add(comboFormat, 1, 6);
            layout.Controls.Add(labelCaptionTarget, 2, 6);
            layout.Controls.Add(comboCaptionTarget, 3, 6);

            layout.Controls.Add(checkAutoOnnx, 0, 7);
            layout.SetColumnSpan(checkAutoOnnx, 2);
            layout.Controls.Add(checkReprocessExisting, 2, 7);
            layout.SetColumnSpan(checkReprocessExisting, 2);

            layout.Controls.Add(buttonsPanel, 0, 8);
            layout.SetColumnSpan(buttonsPanel, 2);
            layout.Controls.Add(labelModelStatus, 2, 8);
            layout.SetColumnSpan(labelModelStatus, 2);

            groupOptions.Controls.Add(layout);
            return groupOptions;
        }

        private TableLayoutPanel BuildBottomPanel()
        {
            var panelBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(28F)));
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(24F)));
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            progressBar.Dock = DockStyle.Fill;
            progressBar.Style = ProgressBarStyle.Continuous;

            labelStatus.Dock = DockStyle.Fill;
            labelStatus.AutoEllipsis = true;
            labelStatus.TextAlign = ContentAlignment.MiddleLeft;

            var panelActionButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 6, 0, 0)
            };

            buttonCancelJob.Enabled = false;
            panelActionButtons.Controls.Add(buttonRun);
            panelActionButtons.Controls.Add(buttonCancelJob);
            panelActionButtons.Controls.Add(buttonClose);

            panelBottom.Controls.Add(progressBar, 0, 0);
            panelBottom.Controls.Add(labelStatus, 0, 1);
            panelBottom.Controls.Add(panelActionButtons, 0, 2);
            return panelBottom;
        }

        private int ScaleLayout(float value)
        {
            return Math.Max(1, (int)Math.Round(value * (DeviceDpi / 96f)));
        }

        public void ApplyLanguage()
        {
            Text = I18n.GetText("LlmTaggerForm");
            if (Controls.Find("groupSource", true).FirstOrDefault() is GroupBox groupSource)
                groupSource.Text = I18n.GetText("TaggerSourceGroup");
            if (Controls.Find("groupOptions", true).FirstOrDefault() is GroupBox groupOptions)
                groupOptions.Text = I18n.GetText("LlmTaggerOptionsGroup");

            radioSourceSelected.Text = I18n.GetText("TaggerSourceSelected");
            radioSourceAllImages.Text = I18n.GetText("TaggerSourceAllImages");
            radioSourceFolder.Text = I18n.GetText("TaggerSourceFolder");
            labelMode.Text = I18n.GetText("LlmTaggerModeLabel");
            labelConcurrency.Text = I18n.GetText("LlmTaggerConcurrency");
            labelPromptTemplate.Text = I18n.GetText("LlmTaggerPromptTemplate");
            labelVisionModel.Text = I18n.GetText("LlmTaggerVisionModel");
            labelSetMode.Text = I18n.GetText("TaggerSetMode");
            labelSortMode.Text = I18n.GetText("TaggerSortMode");
            labelTagPrefix.Text = I18n.GetText("TaggerTagPrefix");
            labelTagSuffix.Text = I18n.GetText("TaggerTagSuffix");
            checkReplaceUnderscores.Text = I18n.GetText("TaggerReplaceUnderscores");
            labelFormat.Text = I18n.GetText("LlmTaggerFormatLabel");
            labelCaptionTarget.Text = I18n.GetText("LlmTaggerCaptionTarget");
            checkAutoOnnx.Text = I18n.GetText("LlmTaggerAutoOnnx");
            checkReprocessExisting.Text = I18n.GetText("LlmTaggerReprocessExisting");
            buttonOpenSettings.Text = I18n.GetText("LlmTaggerOpenSettings");
            buttonTaggerSettings.Text = I18n.GetText("LlmTaggerOpenTaggerSettings");
            buttonRun.Text = I18n.GetText("LlmTaggerRun");
            buttonCancelJob.Text = I18n.GetText("TaggerCancel");
            buttonClose.Text = I18n.GetText("TaggerClose");
        }

        private void ApplyButtonStyles()
        {
            ApplyPrimaryButton(buttonRun);
            ApplySecondaryButton(buttonOpenSettings);
            ApplySecondaryButton(buttonTaggerSettings);
            ApplySecondaryButton(buttonCancelJob);
            ApplySecondaryButton(buttonClose);
        }

        private static void ApplyPrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(108, 36);
            button.Padding = new Padding(10, 4, 10, 4);
            button.Margin = new Padding(0, 0, 8, 6);
            button.Font = new Font(button.Font, FontStyle.Bold);
        }

        private static void ApplySecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(96, 34);
            button.Padding = new Padding(8, 4, 8, 4);
            button.Margin = new Padding(0, 0, 8, 6);
        }

        private void LoadSettingsToControls()
        {
            loadingSettings = true;
            try
            {
                OpenAiSettings settings = Program.Settings.OpenAiAutoTagger;
                comboMode.SelectedIndex = Program.Settings.LlmTaggerMode == LlmTaggerMode.NaturalLanguage ? 1 : 0;
                numericConcurrency.Value = Math.Clamp(Program.Settings.LlmT2NlConcurrency, 1, 100);
                SelectEnum(comboSetMode, settings.SetMode);
                SelectEnum(comboSortMode, settings.SortMode);
                textTagPrefix.Text = settings.TagPrefix ?? string.Empty;
                textTagSuffix.Text = settings.TagSuffix ?? string.Empty;
                checkReplaceUnderscores.Checked = settings.ReplaceUnderscoresWithSpaces;
                comboFormat.SelectedIndex = Program.Settings.LlmCaptionFormat == LlmCaptionFormat.NaturalLanguageOnly ? 1 : 0;
                comboCaptionTarget.SelectedIndex = Program.Settings.LlmCaptionOutputTarget == LlmCaptionOutputTarget.InPlace ? 1 : 0;
                checkAutoOnnx.Checked = Program.Settings.LlmTaggerAutoOnnxIfNoTags;
                checkReprocessExisting.Checked = Program.Settings.LlmTaggerReprocessExisting;
                RefreshPromptTemplates();
                RefreshModelListFromCache();
                UpdateModeUi();
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private LlmTaggerMode GetSelectedMode()
        {
            return comboMode.SelectedIndex == 1 ? LlmTaggerMode.NaturalLanguage : LlmTaggerMode.Tags;
        }

        private LlmCaptionFormat GetSelectedFormat()
        {
            return comboFormat.SelectedIndex == 1 ? LlmCaptionFormat.NaturalLanguageOnly : LlmCaptionFormat.TagsAndNaturalLanguage;
        }

        private LlmCaptionOutputTarget GetSelectedCaptionTarget()
        {
            return comboCaptionTarget.SelectedIndex == 1 ? LlmCaptionOutputTarget.InPlace : LlmCaptionOutputTarget.SeparateFolder;
        }

        private void UpdateModeUi()
        {
            bool tags = GetSelectedMode() == LlmTaggerMode.Tags;
            // Tag post-processing options apply only to Tags mode.
            labelSetMode.Enabled = tags;
            comboSetMode.Enabled = tags;
            labelSortMode.Enabled = tags;
            comboSortMode.Enabled = tags;
            labelTagPrefix.Enabled = tags;
            textTagPrefix.Enabled = tags;
            labelTagSuffix.Enabled = tags;
            textTagSuffix.Enabled = tags;
            checkReplaceUnderscores.Enabled = tags;
            // Natural-language options apply only to that mode.
            labelFormat.Enabled = !tags;
            comboFormat.Enabled = !tags;
            labelCaptionTarget.Enabled = !tags;
            comboCaptionTarget.Enabled = !tags;
            checkAutoOnnx.Enabled = !tags;
            // "Reprocess existing" is only meaningful for the separate-folder target.
            checkReprocessExisting.Enabled = !tags && GetSelectedCaptionTarget() == LlmCaptionOutputTarget.SeparateFolder;
        }

        private void RefreshPromptTemplates()
        {
            bool wasLoading = loadingSettings;
            loadingSettings = true;
            try
            {
                AiPromptTemplateLibrary library = AiPromptTemplateLibrary.Create(
                    Program.Settings.AiServerSetPromptTemplates,
                    Program.Settings.AiServerSetPromptTemplateId,
                    Program.Settings.AiServerSetPromptTemplate);

                comboPromptTemplate.Items.Clear();
                promptTemplateIds.Clear();
                int selectedIndex = 0;
                foreach (AiPromptTemplateSettings template in library.Templates)
                {
                    if (string.Equals(template.Id, library.SelectedTemplateId, StringComparison.Ordinal))
                        selectedIndex = comboPromptTemplate.Items.Count;
                    comboPromptTemplate.Items.Add(GetTemplateDisplayName(template));
                    promptTemplateIds.Add(template.Id);
                }

                if (comboPromptTemplate.Items.Count > 0)
                    comboPromptTemplate.SelectedIndex = selectedIndex;
            }
            finally
            {
                loadingSettings = wasLoading;
            }
        }

        private static string GetTemplateDisplayName(AiPromptTemplateSettings template)
        {
            // Built-in template names are localized (the catalog stores fixed
            // Chinese/English names); custom templates keep the user's name.
            return template.Id switch
            {
                AiPromptTemplateCatalog.DanbooruTagId => I18n.GetText("PromptTemplateDanbooruTag"),
                AiPromptTemplateCatalog.NaturalLanguageId => I18n.GetText("PromptTemplateNaturalLanguage"),
                AiPromptTemplateCatalog.HybridModeId => I18n.GetText("PromptTemplateHybridMode"),
                AiPromptTemplateCatalog.NaturalLanguage2Id => I18n.GetText("PromptTemplateNaturalLanguage2"),
                _ => template.Name
            };
        }

        private void OnPromptTemplateChanged()
        {
            if (loadingSettings)
                return;
            int index = comboPromptTemplate.SelectedIndex;
            if (index < 0 || index >= promptTemplateIds.Count)
                return;

            AiServerSetSettingsService.SavePromptTemplates(Program.Settings.AiServerSetPromptTemplates, promptTemplateIds[index]);
            Program.Settings.SaveSettings();
        }

        private void RefreshModelListFromCache()
        {
            bool wasLoading = loadingSettings;
            loadingSettings = true;
            try
            {
                comboVisionModel.Items.Clear();
                IEnumerable<string> models = Program.OpenAiAutoTagger?.Models ?? Enumerable.Empty<string>();
                foreach (string model in models.Where(m => !string.IsNullOrWhiteSpace(m)))
                    comboVisionModel.Items.Add(model);

                string current = Program.Settings.OpenAiAutoTagger.ResolveVisionModel();
                if (!string.IsNullOrWhiteSpace(current) && !comboVisionModel.Items.Contains(current))
                    comboVisionModel.Items.Add(current);

                if (!string.IsNullOrWhiteSpace(current))
                    comboVisionModel.SelectedItem = current;
                else if (comboVisionModel.Items.Count > 0)
                    comboVisionModel.SelectedIndex = 0;
            }
            finally
            {
                loadingSettings = wasLoading;
            }
        }

        private async Task RefreshModelListAsync()
        {
            if (Program.OpenAiAutoTagger == null || !HasValidEndpoint())
                return;

            try
            {
                if (!Program.OpenAiAutoTagger.IsConnected)
                    await Program.OpenAiAutoTagger.ConnectAsync().ConfigureAwait(true);
            }
            catch
            {
                // Offline / bad endpoint: keep whatever the settings already hold.
            }

            if (!IsDisposed)
            {
                RefreshModelListFromCache();
                UpdateIdleStatus();
            }
        }

        private void OpenLlmSettings()
        {
            using Form_AiServerSet settings = new Form_AiServerSet(owner);
            settings.ShowDialog(this);
            LoadSettingsToControls();
            _ = RefreshModelListAsync();
            UpdateIdleStatus();
        }

        private void OpenTaggerSettings()
        {
            using Form_AutoTaggerOpenAiSettings settings = new Form_AutoTaggerOpenAiSettings();
            settings.ShowDialog(this);
            LoadSettingsToControls();
            _ = RefreshModelListAsync();
            UpdateIdleStatus();
        }

        private static bool HasValidEndpoint()
        {
            string endpointText = Program.Settings.OpenAiAutoTagger.ConnectionAddress;
            return Uri.TryCreate(endpointText, UriKind.Absolute, out Uri endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps);
        }

        private static bool HasValidSettings()
        {
            return HasValidEndpoint()
                && !string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ResolveVisionModel())
                && Program.OpenAiAutoTagger != null;
        }

        private bool EnsureConfigured()
        {
            if (HasValidSettings())
                return true;

            MessageBox.Show(this, I18n.GetText("OpenAiAutoTagInvalidSettings"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            using Form_AiServerSet settings = new Form_AiServerSet(owner);
            if (settings.ShowDialog(this) != DialogResult.OK || !HasValidSettings())
                return false;

            LoadSettingsToControls();
            return true;
        }

        private void UpdateStatus(string line)
        {
            labelStatus.Text = line;
            toolTip.SetToolTip(labelStatus, line);
        }

        private void UpdateIdleStatus()
        {
            if (IsJobRunning())
                return;

            string text = HasValidSettings()
                ? I18n.GetText("LlmTaggerReady")
                : I18n.GetText("LlmTaggerNotConfigured");
            UpdateStatus(text);
            labelModelStatus.Text = text;
            toolTip.SetToolTip(labelModelStatus, text);
        }

        /// <summary>Preselects the "current folder" source (folder quick actions).</summary>
        public void SelectFolderSource()
        {
            if (radioSourceFolder.Enabled)
                radioSourceFolder.Checked = true;
        }

        /// <summary>Preselects the "all dataset images" source (the browser's All row).</summary>
        public void SelectAllImagesSource()
        {
            radioSourceAllImages.Checked = true;
        }

        private List<string> ResolveInputImages()
        {
            if (Program.DataManager == null)
                return new List<string>();

            if (radioSourceSelected.Checked)
                return owner.GetSelectedDatasetImagePaths();

            IEnumerable<DataItem> pool = radioSourceFolder.Checked
                ? Program.DataManager.GetScopedItems()
                : Program.DataManager.DataSet.Values;
            return pool
                .Select(item => item.ImageFilePath)
                .Where(path => IsImageFile(path) && !VideoProcessingService.IsVideoFile(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            return Extensions.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        private static bool TryGetDataItem(string imagePath, out DataItem item)
        {
            item = null;
            if (Program.DataManager == null || string.IsNullOrWhiteSpace(imagePath))
                return false;

            if (Program.DataManager.DataSet.TryGetValue(imagePath, out item))
                return true;

            foreach (KeyValuePair<string, DataItem> entry in Program.DataManager.DataSet)
            {
                if (string.Equals(entry.Key, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    item = entry.Value;
                    return true;
                }
            }

            return false;
        }

        private async Task RunJobAsync()
        {
            if (runJobActive || IsJobRunning())
                return;
            runJobActive = true;
            try
            {
                await RunJobCoreAsync().ConfigureAwait(true);
            }
            finally
            {
                runJobActive = false;
                if (closeAfterJob)
                {
                    closeAfterJob = false;
                    Close();
                }
            }
        }

        private async Task RunJobCoreAsync()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveSettingsFromControls();
            if (!EnsureConfigured())
                return;

            List<string> inputs = ResolveInputImages();
            if (inputs.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetJobRunning(true);
            jobCancellation = new CancellationTokenSource();
            try
            {
                if (GetSelectedMode() == LlmTaggerMode.NaturalLanguage)
                    await RunNaturalLanguageModeAsync(inputs).ConfigureAwait(true);
                else
                    await RunTagsModeAsync(inputs).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus(I18n.GetText("TaggerCancelled"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                jobCancellation?.Dispose();
                jobCancellation = null;
                SetJobRunning(false);
            }
        }

        private async Task RunTagsModeAsync(List<string> inputs)
        {
            IAutoTagProvider provider = Program.AutoTagProviders.GetRequired("openai-compatible");
            AutoTagConnectionResult connection = await provider.ConnectAsync(jobCancellation.Token).ConfigureAwait(true);
            if (!connection.Success)
            {
                MessageBox.Show(this, connection.ErrorMessage, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string visionModel = Program.Settings.OpenAiAutoTagger.ResolveVisionModel();
            int concurrency = Math.Clamp((int)numericConcurrency.Value, 1, 100);
            int total = inputs.Count;
            int completed = 0;
            var errors = new List<string>();
            object errorsLock = new object();
            var results = new ConcurrentDictionary<string, IReadOnlyList<AutoTagProviderItem>>(StringComparer.OrdinalIgnoreCase);
            var reporter = VideoProgressReporter.CreateForControl(this, UpdateStatus, throttleMilliseconds: 100);

            try
            {
                await Parallel.ForEachAsync(
                    inputs,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = concurrency,
                        CancellationToken = jobCancellation.Token
                    },
                    async (path, itemToken) =>
                    {
                        try
                        {
                            AutoTagProviderResult result = await provider.GenerateAsync(new AutoTagProviderRequest
                            {
                                MediaPath = path,
                                ModelIds = new[] { visionModel }
                            }, itemToken).ConfigureAwait(false);

                            if (result.Canceled)
                                return;
                            if (result.Success && result.Items.Count > 0)
                                results[path] = result.Items;
                            else if (!string.IsNullOrEmpty(result.ErrorMessage))
                                AddError(errors, errorsLock, path, result.ErrorMessage);
                        }
                        catch (OperationCanceledException) when (itemToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            AddError(errors, errorsLock, path, ex.Message);
                        }

                        int done = Interlocked.Increment(ref completed);
                        ReportBatchProgress(done, total);
                        reporter.Report(string.Format(I18n.GetText("LlmTaggerProgress"), done, total, Path.GetFileName(path)));
                    }).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Partial results collected so far are still applied below.
            }

            ApplyTagResults(results, errors);
            progressBar.Value = total > 0 ? Math.Min(100, (int)Math.Round(completed * 100.0 / total)) : 0;

            await Task.Run(() => Program.DataManager?.SaveAll()).ConfigureAwait(true);
            if (Program.DataManager != null && Program.DataManager.LastSaveErrors.Count > 0)
                errors.AddRange(Program.DataManager.LastSaveErrors);

            bool wasCanceled = jobCancellation?.Token.IsCancellationRequested == true;
            if (errors.Count > 0 && !closeAfterJob)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(20)), I18n.GetText("TaggerCompletedWithErrors"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                UpdateStatus(I18n.GetText(wasCanceled ? "TaggerCancelled" : "LlmTaggerCompleted"));
        }

        private void ApplyTagResults(ConcurrentDictionary<string, IReadOnlyList<AutoTagProviderItem>> results, List<string> errors)
        {
            if (results.Count == 0 || Program.DataManager == null)
                return;

            OpenAiSettings settings = Program.Settings.OpenAiAutoTagger;
            owner.PrepareForBulkTagWrite();
            try
            {
                Program.DataManager.ExecuteBulkMutation(() =>
                {
                    foreach (KeyValuePair<string, IReadOnlyList<AutoTagProviderItem>> pair in results)
                    {
                        if (TryGetDataItem(pair.Key, out DataItem dataItem))
                            TagWriteService.ApplyTags(dataItem, pair.Value, settings);
                        else
                            errors.Add(pair.Key + ": " + I18n.GetText("TaggerDatasetItemMissing"));
                    }
                });
            }
            finally
            {
                owner.CompleteBulkTagWrite();
            }
        }

        private async Task RunNaturalLanguageModeAsync(List<string> inputs)
        {
            string root = owner.GetSelectedDatasetDirectory();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Captions are built from the on-disk .txt files, so flush pending edits first.
            // A failed save means the model would read stale files: stop before
            // any paid request instead of continuing on the old disk content.
            if (Program.DataManager.IsDataSetChanged())
            {
                await Task.Run(() => Program.DataManager.SaveAll()).ConfigureAwait(true);
                // SaveAll returns "wrote at least one", so partial failures only
                // show up in LastSaveErrors — and even one stale file is too many.
                if (Program.DataManager.LastSaveErrors.Count > 0)
                {
                    MessageBox.Show(this, DescribeSaveFailures(), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Program.DataManager.UpdateDatasetHash();
            }

            // Optionally tag untagged images with the local ONNX tagger first so the
            // caption prompt has reference tags to work from.
            if (checkAutoOnnx.Checked && !await RunOnnxPretagAsync(inputs).ConfigureAwait(true))
                return;

            LlmCaptionOutputTarget target = GetSelectedCaptionTarget();
            CaptionScanResult scan = await CaptionGenerationService.ScanDirectoryAsync(root).ConfigureAwait(true);
            // Selected and folder sources both narrow the whole-root scan.
            if (!radioSourceAllImages.Checked)
                scan = FilterScanToSelection(scan, inputs);

            if (scan.Total == 0)
            {
                UpdateStatus(I18n.GetText("TaggerNoImages"));
                return;
            }

            if (!Program.OpenAiAutoTagger.IsConnected)
            {
                var connection = await Program.OpenAiAutoTagger.ConnectAsync(jobCancellation.Token).ConfigureAwait(true);
                if (!connection.Result)
                {
                    MessageBox.Show(this, connection.ErrMessage, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            var captured = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var options = new CaptionGenerationOptions
            {
                SystemPrompt = AiPromptTemplateCatalog.LlmT2NlSystemPrompt,
                MaxConcurrency = Math.Clamp((int)numericConcurrency.Value, 1, 100),
                IncludeOriginalTags = GetSelectedFormat() == LlmCaptionFormat.TagsAndNaturalLanguage,
                SkipExisting = target == LlmCaptionOutputTarget.SeparateFolder && !checkReprocessExisting.Checked
            };
            if (target == LlmCaptionOutputTarget.InPlace)
                options.CaptionSink = (path, caption) => captured[path] = caption ?? string.Empty;

            var reporter = VideoProgressReporter.CreateForControl(this, UpdateStatus, throttleMilliseconds: 100);
            var progress = new Progress<CaptionGenerationProgress>(value =>
            {
                if (value.Total > 0)
                    ReportBatchProgress(value.Completed, value.Total);
                if (!string.IsNullOrEmpty(value.CurrentFile))
                    reporter.Report(string.Format(I18n.GetText("LlmTaggerProgress"), value.Completed, value.Total, Path.GetFileName(value.CurrentFile)));
            });

            CaptionGenerationService service = new CaptionGenerationService(async (request, itemToken) =>
            {
                OpenAiRequest openAiRequest = new OpenAiRequest
                {
                    Model = Program.Settings.OpenAiAutoTagger.ResolveVisionModel(),
                    SystemPrompt = request.SystemPrompt,
                    UserPrompt = request.UserPrompt,
                    Temperature = Program.Settings.OpenAiAutoTagger.Temperature,
                    TopP = Program.Settings.OpenAiAutoTagger.TopP,
                    RepeatPenalty = Program.Settings.OpenAiAutoTagger.RepeatPenalty,
                    ContentType = request.ContentType
                };
                openAiRequest.ImageData.Add(request.ImageData);
                var response = await Program.OpenAiAutoTagger.SendRequestAsync(openAiRequest, itemToken);
                return new CaptionModelResponse(response.Result, response.ErrMessage);
            });

            CaptionGenerationResult result = await service.ProcessAsync(scan, options, progress, jobCancellation.Token).ConfigureAwait(true);

            if (target == LlmCaptionOutputTarget.InPlace)
            {
                ApplyCaptionsInPlace(captured);
                // "In place" completion must mean persisted: flush to disk like
                // the ONNX pre-tag path does, and report files that failed.
                if (captured.Count > 0)
                {
                    await Task.Run(() => Program.DataManager.SaveAll()).ConfigureAwait(true);
                    foreach (string saveError in Program.DataManager.LastSaveErrors.Take(10))
                        result.Errors.Add(string.Format(I18n.GetText("LlmTaggerInPlaceSaveFailed"), saveError));
                }
            }

            string resultText = result.Canceled
                ? string.Format(I18n.GetText("LlmT2NlCanceledResult"), result.Succeeded, result.Skipped, result.Failed)
                : string.Format(I18n.GetText("LlmT2NlResult"), result.Succeeded, result.Skipped, result.Failed);
            UpdateStatus(resultText);
            progressBar.Value = 100;

            if (result.Errors.Count > 0 && !closeAfterJob)
            {
                MessageBox.Show(
                    this,
                    resultText + Environment.NewLine + Environment.NewLine + I18n.GetText("LlmT2NlErrors") + Environment.NewLine + string.Join(Environment.NewLine, result.Errors.Take(10)),
                    I18n.GetText("TaggerCompletedWithErrors"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // Runs the local WD14 ONNX tagger on images that currently have no tags, so the
        // caption step has reference tags. Writes tags through the DataManager + SaveAll.
        // Returns false only when freshly written tags could not be saved to disk —
        // the caption step reads disk files, so the caller must not start requests.
        private async Task<bool> RunOnnxPretagAsync(List<string> inputs)
        {
            var untagged = new List<(string path, DataItem item)>();
            foreach (string path in inputs)
            {
                if (TryGetDataItem(path, out DataItem item) && item.Tags.Count == 0)
                    untagged.Add((path, item));
            }

            if (untagged.Count == 0)
                return true;

            string repo = Program.Settings.Wd14Tagger.SelectedModelRepo;
            if (!wd14Service.IsModelReady(repo))
            {
                DialogResult choice = MessageBox.Show(
                    this,
                    string.Format(I18n.GetText("LlmTaggerOnnxNotReady"), untagged.Count),
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (choice != DialogResult.Yes)
                {
                    UpdateStatus(I18n.GetText("LlmTaggerOnnxSkipped"));
                    return true;
                }

                var downloadReporter = VideoProgressReporter.CreateForControl(this, UpdateStatus);
                var downloadProgress = new Progress<(string file, long downloaded, long? total)>(report =>
                {
                    if (report.total is > 0)
                        progressBar.Value = (int)Math.Clamp(Math.Round(report.downloaded * 100.0 / report.total.Value), 0, 100);
                    downloadReporter.Report(I18n.GetText("TaggerDownloadModel"));
                });
                await wd14Service.DownloadModelAsync(repo, Program.Settings.Wd14Tagger.DownloadSource, downloadProgress, jobCancellation.Token).ConfigureAwait(true);
                progressBar.Value = 0;
            }

            (double generalThreshold, double characterThreshold) = Program.Settings.Wd14Tagger.GetThresholdsForRepo(repo);
            int total = untagged.Count;
            var reporter = VideoProgressReporter.CreateForControl(this, UpdateStatus, throttleMilliseconds: 100);

            List<(DataItem item, IReadOnlyList<AutoTagProviderItem> tags)> tagged;
            try
            {
                tagged = await Task.Run(() =>
                {
                    wd14Service.LoadModel(repo);
                    var list = new List<(DataItem, IReadOnlyList<AutoTagProviderItem>)>(total);
                    int done = 0;
                    foreach ((string path, DataItem item) in untagged)
                    {
                        if (jobCancellation.Token.IsCancellationRequested)
                            break;
                        list.Add((item, wd14Service.TagImage(path, generalThreshold, characterThreshold)));
                        done++;
                        ReportBatchProgress(done, total);
                        reporter.Report(string.Format(I18n.GetText("LlmTaggerOnnxProgress"), done, total, Path.GetFileName(path)));
                    }
                    return list;
                }, jobCancellation.Token).ConfigureAwait(true);
            }
            catch (ModelCorruptedException ex)
            {
                // The service already deleted the bad file(s); skip pre-tagging this run.
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }

            if (tagged.Count == 0)
                return true;

            Wd14TaggerSettings wd14Settings = Program.Settings.Wd14Tagger;
            owner.PrepareForBulkTagWrite();
            try
            {
                Program.DataManager.ExecuteBulkMutation(() =>
                {
                    foreach ((DataItem item, IReadOnlyList<AutoTagProviderItem> tags) in tagged)
                        TagWriteService.ApplyTags(item, tags, wd14Settings);
                });
            }
            finally
            {
                owner.CompleteBulkTagWrite();
            }

            await Task.Run(() => Program.DataManager.SaveAll()).ConfigureAwait(true);
            if (Program.DataManager.LastSaveErrors.Count > 0)
            {
                MessageBox.Show(this, DescribeSaveFailures(), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            progressBar.Value = 0;
            return true;
        }

        private static string DescribeSaveFailures()
        {
            return I18n.GetText("LlmTaggerSaveBeforeRunFailed") + Environment.NewLine
                + string.Join(Environment.NewLine, Program.DataManager.LastSaveErrors.Take(10));
        }

        private static CaptionScanResult FilterScanToSelection(CaptionScanResult scan, List<string> selectedPaths)
        {
            var selected = new HashSet<string>(
                selectedPaths.Select(p => Path.GetFullPath(p)),
                StringComparer.OrdinalIgnoreCase);
            List<string> files = scan.Files.Where(f => selected.Contains(Path.GetFullPath(f))).ToList();
            HashSet<string> existing = scan.ExistingFiles
                .Where(f => selected.Contains(Path.GetFullPath(f)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new CaptionScanResult(scan.SourceRoot, scan.OutputRoot, files, existing);
        }

        private void ApplyCaptionsInPlace(ConcurrentDictionary<string, string> captions)
        {
            if (captions.Count == 0 || Program.DataManager == null)
                return;

            owner.PrepareForBulkTagWrite();
            try
            {
                Program.DataManager.ExecuteBulkMutation(() =>
                {
                    foreach (KeyValuePair<string, string> pair in captions)
                    {
                        if (!TryGetDataItem(pair.Key, out DataItem dataItem))
                            continue;

                        // Replace the item's tag content with the caption, preserving case
                        // (unlike EditableTagList.AddRange, which lower-cases). Going through
                        // PromptParser + the DataManager keeps in-memory and the .txt written
                        // by SaveAll consistent, so a later save can't clobber the caption.
                        var parsed = PromptParser.ParsePrompt(pair.Value, false, Program.Settings.SeparatorOnLoad);
                        dataItem.Tags.Clear();
                        dataItem.Tags.LoadFromPromptParserData(parsed);
                    }
                });
            }
            finally
            {
                owner.CompleteBulkTagWrite();
            }
        }

        private static void AddError(List<string> errors, object errorsLock, string path, string message)
        {
            lock (errorsLock)
            {
                if (errors.Count < 20)
                    errors.Add(Path.GetFileName(path) + ": " + message);
            }
        }

        private void ReportBatchProgress(int completed, int total)
        {
            if (IsDisposed || total <= 0)
                return;

            int percent = Math.Min(100, (int)Math.Round(completed * 100.0 / total));
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => progressBar.Value = percent));
                    return;
                }

                progressBar.Value = percent;
            }
            catch (ObjectDisposedException)
            {
                // Form torn down between the IsDisposed check and BeginInvoke.
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void PersistSettings()
        {
            if (loadingSettings)
                return;

            SaveSettingsFromControls();
            Program.Settings.SaveSettings();
        }

        private void SaveSettingsFromControls()
        {
            OpenAiSettings settings = Program.Settings.OpenAiAutoTagger;
            Program.Settings.LlmTaggerMode = GetSelectedMode();
            Program.Settings.LlmT2NlConcurrency = Math.Clamp((int)numericConcurrency.Value, 1, 100);
            Program.Settings.LlmCaptionFormat = GetSelectedFormat();
            Program.Settings.LlmCaptionOutputTarget = GetSelectedCaptionTarget();
            Program.Settings.LlmTaggerAutoOnnxIfNoTags = checkAutoOnnx.Checked;
            Program.Settings.LlmTaggerReprocessExisting = checkReprocessExisting.Checked;

            settings.SetMode = GetSelectedEnum<NetworkResultSetMode>(comboSetMode);
            settings.SortMode = GetSelectedEnum<AutoTaggerSort>(comboSortMode);
            settings.TagPrefix = textTagPrefix.Text ?? string.Empty;
            settings.TagSuffix = textTagSuffix.Text ?? string.Empty;
            settings.ReplaceUnderscoresWithSpaces = checkReplaceUnderscores.Checked;
            if (comboVisionModel.SelectedItem is string model && !string.IsNullOrWhiteSpace(model))
                settings.VisionModel = model;
        }

        private static T GetSelectedEnum<T>(ComboBox combo) where T : struct, Enum
        {
            Array values = Enum.GetValues(typeof(T));
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Length)
                return (T)values.GetValue(combo.SelectedIndex)!;
            return (T)values.GetValue(0)!;
        }

        private static void SelectEnum<T>(ComboBox combo, T value) where T : struct, Enum
        {
            int index = Extensions.GetEnumIndexFromValue<T>(value.ToString());
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        private bool IsJobRunning()
        {
            return jobCancellation != null;
        }

        private void SetJobRunning(bool running)
        {
            buttonRun.Enabled = !running;
            buttonCancelJob.Enabled = running;
            buttonOpenSettings.Enabled = !running;
            buttonTaggerSettings.Enabled = !running;
            radioSourceSelected.Enabled = !running;
            radioSourceAllImages.Enabled = !running;
            radioSourceFolder.Enabled = !running && folderSourceAvailable;
            comboMode.Enabled = !running;
            comboPromptTemplate.Enabled = !running;
            comboVisionModel.Enabled = !running;
            numericConcurrency.Enabled = !running;
            comboSetMode.Enabled = !running;
            comboSortMode.Enabled = !running;
            textTagPrefix.Enabled = !running;
            textTagSuffix.Enabled = !running;
            checkReplaceUnderscores.Enabled = !running;
            comboFormat.Enabled = !running;
            comboCaptionTarget.Enabled = !running;
            checkAutoOnnx.Enabled = !running;
            checkReprocessExisting.Enabled = !running;

            if (!running)
                UpdateModeUi();
        }
    }
}
