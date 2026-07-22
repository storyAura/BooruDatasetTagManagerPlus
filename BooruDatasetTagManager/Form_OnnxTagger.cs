using System;
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
    public sealed class Form_OnnxTagger : Form
    {
        private readonly MainForm owner;
        private readonly Wd14OnnxTaggerService wd14Service = new Wd14OnnxTaggerService();
        private readonly PixAiOnnxTaggerService pixAiService = new PixAiOnnxTaggerService();
        private readonly ClTaggerOnnxService clService = new ClTaggerOnnxService();
        private readonly ToolTip toolTip = new ToolTip();

        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceAllImages = new RadioButton();
        private readonly RadioButton radioSourceFolder = new RadioButton();
        private readonly bool folderSourceAvailable =
            !string.IsNullOrEmpty(Program.DataManager?.ActiveFolder);
        private readonly ComboBox comboModel = new ComboBox();
        private readonly NumericUpDown numericThreshold = new NumericUpDown();
        private readonly NumericUpDown numericCharacterThreshold = new NumericUpDown();
        private readonly ComboBox comboDownloadSource = new ComboBox();
        private readonly Button buttonDownloadModel = new Button();
        private readonly Label labelModelStatus = new Label();
        private readonly ComboBox comboSetMode = new ComboBox();
        private readonly ComboBox comboSortMode = new ComboBox();
        private readonly Label labelThreshold = new Label();
        private readonly Label labelCharacterThreshold = new Label();
        private readonly Label labelDownloadSource = new Label();
        private readonly Label labelSetMode = new Label();
        private readonly Label labelSortMode = new Label();
        private readonly Label labelSizeHint = new Label();
        private readonly CheckBox checkReplaceUnderscores = new CheckBox();
        private readonly TextBox textTagPrefix = new TextBox();
        private readonly TextBox textTagSuffix = new TextBox();
        private readonly Label labelTagPrefix = new Label();
        private readonly Label labelTagSuffix = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonRun = new Button();
        private readonly Button buttonCancelJob = new Button();
        private readonly Button buttonClose = new Button();

        private CancellationTokenSource jobCancellation;
        // Guards the window between the download sub-step and SetJobRunning(true),
        // where a second Run click could race two inferences onto one session.
        private bool runJobActive;
        // Set when the user tries to close mid-job: the close is deferred until the
        // background inference finishes, because disposing the native ONNX session
        // under an in-flight Run() is an uncatchable AccessViolation.
        private bool closeAfterJob;
        private bool loadingSettings;

        public Form_OnnxTagger(MainForm owner)
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

            Text = "ONNX Tagger";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(720, 580);
            ClientSize = new Size(800, 640);

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
            mainLayout.Controls.Add(BuildModelGroup(), 0, 1);
            mainLayout.Controls.Add(BuildBottomPanel(), 0, 2);
            Controls.Add(mainLayout);

            foreach (OnnxTaggerModelEntry model in OnnxTaggerCatalog.AllModels)
                comboModel.Items.Add(model);

            comboDownloadSource.DropDownStyle = ComboBoxStyle.DropDownList;
            comboDownloadSource.Items.AddRange(Extensions.GetFriendlyEnumValues<HuggingFaceDownloadSource>());

            comboSetMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSetMode.Items.AddRange(Extensions.GetFriendlyEnumValues<NetworkResultSetMode>());

            comboSortMode.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSortMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutoTaggerSort>());

            numericThreshold.DecimalPlaces = 2;
            numericThreshold.Increment = 0.01m;
            numericThreshold.Minimum = 0.01m;
            numericThreshold.Maximum = 1.00m;

            numericCharacterThreshold.DecimalPlaces = 2;
            numericCharacterThreshold.Increment = 0.01m;
            numericCharacterThreshold.Minimum = 0.01m;
            numericCharacterThreshold.Maximum = 1.00m;

            radioSourceSelected.Checked = true;
            comboModel.SelectedIndexChanged += (_, _) =>
            {
                if (loadingSettings)
                    return;

                OnnxTaggerModelEntry entry = GetSelectedModel();
                if (entry.Kind != OnnxTaggerModelKind.PixAi
                    && !Program.Settings.Wd14Tagger.HasThresholdsForRepo(ThresholdKey(entry)))
                {
                    ApplyModelDefaults();
                }
                else
                {
                    LoadThresholdsForSelectedModel();
                }

                LoadPostProcessForSelectedModel();
                UpdateModelKindUi();
                UpdateModelStatus();
                PersistSettings();
            };

            numericThreshold.ValueChanged += (_, _) => PersistSettings();
            numericCharacterThreshold.ValueChanged += (_, _) => PersistSettings();
            comboDownloadSource.SelectedIndexChanged += (_, _) => PersistSettings();
            comboSetMode.SelectedIndexChanged += (_, _) => PersistSettings();
            comboSortMode.SelectedIndexChanged += (_, _) => PersistSettings();
            checkReplaceUnderscores.CheckedChanged += (_, _) => PersistSettings();
            textTagPrefix.TextChanged += (_, _) => PersistSettings();
            textTagSuffix.TextChanged += (_, _) => PersistSettings();

            buttonDownloadModel.Click += async (_, _) => await DownloadModelAsync().ConfigureAwait(true);
            buttonRun.Click += async (_, _) => await RunJobAsync().ConfigureAwait(true);
            buttonCancelJob.Click += (_, _) => jobCancellation?.Cancel();
            buttonClose.Click += (_, _) => Close();

            Shown += (_, _) =>
            {
                UpdateModelStatus();
                UpdateIdleStatus();
            };

            FormClosing += (_, e) =>
            {
                if (runJobActive || IsJobRunning())
                {
                    // Never dispose the services while inference/download runs:
                    // cancel and close automatically once the job winds down.
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
                pixAiService.Dispose();
                clService.Dispose();
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

        private GroupBox BuildModelGroup()
        {
            var groupModel = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupModel",
                Padding = new Padding(10, 4, 10, 6)
            };

            comboModel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboModel.Dock = DockStyle.Fill;
            comboModel.Margin = new Padding(0, 0, 8, 0);

            numericThreshold.Dock = DockStyle.Fill;
            numericThreshold.Margin = new Padding(0, 0, 8, 0);

            numericCharacterThreshold.Dock = DockStyle.Fill;
            numericCharacterThreshold.Margin = new Padding(0, 0, 8, 0);

            comboDownloadSource.Dock = DockStyle.Fill;
            comboDownloadSource.Margin = new Padding(0, 0, 8, 0);

            comboSetMode.Dock = DockStyle.Fill;
            comboSetMode.Margin = new Padding(0, 0, 8, 0);

            comboSortMode.Dock = DockStyle.Fill;

            labelThreshold.Dock = DockStyle.Fill;
            labelThreshold.TextAlign = ContentAlignment.MiddleLeft;

            labelCharacterThreshold.Dock = DockStyle.Fill;
            labelCharacterThreshold.TextAlign = ContentAlignment.MiddleLeft;

            labelDownloadSource.Dock = DockStyle.Fill;
            labelDownloadSource.TextAlign = ContentAlignment.MiddleLeft;

            labelSetMode.Dock = DockStyle.Fill;
            labelSetMode.TextAlign = ContentAlignment.MiddleLeft;

            labelSortMode.Dock = DockStyle.Fill;
            labelSortMode.TextAlign = ContentAlignment.MiddleLeft;

            labelModelStatus.Dock = DockStyle.Fill;
            labelModelStatus.AutoEllipsis = true;
            labelModelStatus.TextAlign = ContentAlignment.MiddleLeft;
            labelModelStatus.Margin = new Padding(0, 6, 0, 0);

            labelSizeHint.Dock = DockStyle.Fill;
            labelSizeHint.AutoEllipsis = true;
            labelSizeHint.TextAlign = ContentAlignment.MiddleLeft;

            checkReplaceUnderscores.AutoSize = true;
            checkReplaceUnderscores.Dock = DockStyle.Fill;

            textTagPrefix.Dock = DockStyle.Fill;
            textTagPrefix.Margin = new Padding(0, 0, 8, 0);

            textTagSuffix.Dock = DockStyle.Fill;

            labelTagPrefix.Dock = DockStyle.Fill;
            labelTagPrefix.TextAlign = ContentAlignment.MiddleLeft;

            labelTagSuffix.Dock = DockStyle.Fill;
            labelTagSuffix.TextAlign = ContentAlignment.MiddleLeft;

            buttonDownloadModel.AutoSize = false;
            buttonDownloadModel.Width = ScaleLayout(140);
            buttonDownloadModel.Height = ScaleLayout(32);
            buttonDownloadModel.Margin = new Padding(0, 6, 0, 0);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 7
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLayout(120F)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLayout(120F)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(labelThreshold, 0, 0);
            layout.Controls.Add(numericThreshold, 1, 0);
            layout.Controls.Add(labelCharacterThreshold, 2, 0);
            layout.Controls.Add(numericCharacterThreshold, 3, 0);

            layout.Controls.Add(new Label { Text = string.Empty, Dock = DockStyle.Fill }, 0, 1);
            layout.Controls.Add(comboModel, 1, 1);
            layout.SetColumnSpan(comboModel, 3);

            layout.Controls.Add(labelDownloadSource, 0, 2);
            layout.Controls.Add(comboDownloadSource, 1, 2);
            layout.Controls.Add(labelSetMode, 2, 2);
            layout.Controls.Add(comboSetMode, 3, 2);

            layout.Controls.Add(labelSortMode, 0, 3);
            layout.Controls.Add(comboSortMode, 1, 3);
            layout.Controls.Add(labelSizeHint, 2, 3);
            layout.SetColumnSpan(labelSizeHint, 2);

            layout.Controls.Add(checkReplaceUnderscores, 0, 4);
            layout.SetColumnSpan(checkReplaceUnderscores, 4);

            layout.Controls.Add(labelTagPrefix, 0, 5);
            layout.Controls.Add(textTagPrefix, 1, 5);
            layout.Controls.Add(labelTagSuffix, 2, 5);
            layout.Controls.Add(textTagSuffix, 3, 5);

            layout.Controls.Add(buttonDownloadModel, 0, 6);
            layout.SetColumnSpan(buttonDownloadModel, 2);
            layout.Controls.Add(labelModelStatus, 2, 6);
            layout.SetColumnSpan(labelModelStatus, 2);

            groupModel.Controls.Add(layout);
            return groupModel;
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
            Text = I18n.GetText("TaggerOnnxForm");
            if (Controls.Find("groupSource", true).FirstOrDefault() is GroupBox groupSource)
                groupSource.Text = I18n.GetText("TaggerSourceGroup");
            if (Controls.Find("groupModel", true).FirstOrDefault() is GroupBox groupModel)
                groupModel.Text = I18n.GetText("TaggerModelGroup");

            radioSourceSelected.Text = I18n.GetText("TaggerSourceSelected");
            radioSourceAllImages.Text = I18n.GetText("TaggerSourceAllImages");
            radioSourceFolder.Text = I18n.GetText("TaggerSourceFolder");
            labelCharacterThreshold.Text = I18n.GetText("TaggerCharacterThreshold");
            labelDownloadSource.Text = I18n.GetText("TaggerDownloadSource");
            labelSetMode.Text = I18n.GetText("TaggerSetMode");
            labelSortMode.Text = I18n.GetText("TaggerSortMode");
            labelSizeHint.Text = I18n.GetText("TaggerPixAiSizeHint");
            checkReplaceUnderscores.Text = I18n.GetText("TaggerReplaceUnderscores");
            labelTagPrefix.Text = I18n.GetText("TaggerTagPrefix");
            labelTagSuffix.Text = I18n.GetText("TaggerTagSuffix");
            buttonDownloadModel.Text = I18n.GetText("TaggerDownloadModel");
            buttonRun.Text = I18n.GetText("TaggerRun");
            buttonCancelJob.Text = I18n.GetText("TaggerCancel");
            buttonClose.Text = I18n.GetText("TaggerClose");

            UpdateModelKindUi();
            ApplyButtonStyles();
            UpdateModelStatus();
        }

        private void ApplyButtonStyles()
        {
            ApplyPrimaryButton(buttonRun);
            ApplySecondaryButton(buttonDownloadModel);
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
                string modelId = OnnxTaggerCatalog.ResolveInitialModelId(
                    Program.Settings.OnnxTaggerLastModelId,
                    Program.Settings.Wd14Tagger.SelectedModelRepo);
                SelectModel(modelId);
                LoadThresholdsForSelectedModel();
                LoadPostProcessForSelectedModel();
                SelectEnum(comboDownloadSource, GetDownloadSourceForSelectedModel());
                SelectEnum(comboSetMode, GetSetModeForSelectedModel());
                SelectEnum(comboSortMode, GetSortModeForSelectedModel());
                UpdateModelKindUi();
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private void SelectModel(string modelId)
        {
            for (int i = 0; i < comboModel.Items.Count; i++)
            {
                if (comboModel.Items[i] is OnnxTaggerModelEntry entry
                    && string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    comboModel.SelectedIndex = i;
                    return;
                }
            }

            if (comboModel.Items.Count > 0)
                comboModel.SelectedIndex = 0;
        }

        private OnnxTaggerModelEntry GetSelectedModel()
        {
            if (comboModel.SelectedItem is OnnxTaggerModelEntry entry)
                return entry;

            return OnnxTaggerCatalog.AllModels[0];
        }

        private TaggerSettings GetActiveTaggerSettings()
        {
            return GetSelectedModel().Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger
                : Program.Settings.Wd14Tagger;
        }

        private void LoadPostProcessForSelectedModel()
        {
            TaggerSettings settings = GetActiveTaggerSettings();
            textTagPrefix.Text = settings.TagPrefix ?? string.Empty;
            textTagSuffix.Text = settings.TagSuffix ?? string.Empty;
            checkReplaceUnderscores.Checked = settings switch
            {
                Wd14TaggerSettings wd => wd.ReplaceUnderscoresWithSpaces,
                PixAiTaggerSettings pix => pix.ReplaceUnderscoresWithSpaces,
                _ => true
            };
        }

        private void LoadThresholdsForSelectedModel()
        {
            OnnxTaggerModelEntry entry = GetSelectedModel();
            if (entry.Kind == OnnxTaggerModelKind.PixAi)
            {
                PixAiTaggerSettings settings = Program.Settings.PixAiTagger;
                numericThreshold.Value = (decimal)Math.Clamp(settings.GeneralThreshold, (double)numericThreshold.Minimum, (double)numericThreshold.Maximum);
                numericCharacterThreshold.Value = (decimal)Math.Clamp(settings.CharacterThreshold, (double)numericCharacterThreshold.Minimum, (double)numericCharacterThreshold.Maximum);
            }
            else
            {
                Wd14TaggerSettings settings = Program.Settings.Wd14Tagger;
                string key = ThresholdKey(entry);
                (double threshold, double characterThreshold) = settings.GetThresholdsForRepo(key);
                if (entry.Kind == OnnxTaggerModelKind.ClTagger && !settings.HasThresholdsForRepo(key))
                {
                    // First use of a CL model: the WD fallback defaults do not
                    // apply, take the catalog defaults instead.
                    threshold = entry.DefaultThreshold;
                    characterThreshold = entry.DefaultCharacterThreshold ?? threshold;
                }
                numericThreshold.Value = (decimal)Math.Clamp(threshold, (double)numericThreshold.Minimum, (double)numericThreshold.Maximum);
                numericCharacterThreshold.Value = (decimal)Math.Clamp(characterThreshold, (double)numericCharacterThreshold.Minimum, (double)numericCharacterThreshold.Maximum);
            }
        }

        // CL entries store per-model thresholds under their catalog id (several
        // models can live in one repo); WD keeps the historical repo key.
        private static string ThresholdKey(OnnxTaggerModelEntry entry)
        {
            return entry.Kind == OnnxTaggerModelKind.ClTagger ? entry.Id : entry.Repo;
        }

        private HuggingFaceDownloadSource GetDownloadSourceForSelectedModel()
        {
            return GetSelectedModel().Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger.DownloadSource
                : Program.Settings.Wd14Tagger.DownloadSource;
        }

        private NetworkResultSetMode GetSetModeForSelectedModel()
        {
            return GetSelectedModel().Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger.SetMode
                : Program.Settings.Wd14Tagger.SetMode;
        }

        private AutoTaggerSort GetSortModeForSelectedModel()
        {
            return GetSelectedModel().Kind == OnnxTaggerModelKind.PixAi
                ? Program.Settings.PixAiTagger.SortMode
                : Program.Settings.Wd14Tagger.SortMode;
        }

        private void ApplyModelDefaults()
        {
            OnnxTaggerModelEntry entry = GetSelectedModel();
            numericThreshold.Value = (decimal)Math.Clamp(entry.DefaultThreshold, (double)numericThreshold.Minimum, (double)numericThreshold.Maximum);
            if (entry.DefaultCharacterThreshold.HasValue)
            {
                numericCharacterThreshold.Value = (decimal)Math.Clamp(
                    entry.DefaultCharacterThreshold.Value,
                    (double)numericCharacterThreshold.Minimum,
                    (double)numericCharacterThreshold.Maximum);
            }
        }

        private void UpdateModelKindUi()
        {
            bool pixAi = GetSelectedModel().Kind == OnnxTaggerModelKind.PixAi;
            labelCharacterThreshold.Visible = true;
            numericCharacterThreshold.Visible = true;
            labelSizeHint.Visible = pixAi;
            labelThreshold.Text = I18n.GetText("TaggerGeneralThreshold");
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
            OnnxTaggerModelEntry entry = GetSelectedModel();
            Program.Settings.OnnxTaggerLastModelId = entry.Id;

            HuggingFaceDownloadSource downloadSource = GetSelectedEnum<HuggingFaceDownloadSource>(comboDownloadSource);
            NetworkResultSetMode setMode = GetSelectedEnum<NetworkResultSetMode>(comboSetMode);
            AutoTaggerSort sortMode = GetSelectedEnum<AutoTaggerSort>(comboSortMode);

            if (entry.Kind == OnnxTaggerModelKind.PixAi)
            {
                PixAiTaggerSettings settings = Program.Settings.PixAiTagger;
                settings.GeneralThreshold = (double)numericThreshold.Value;
                settings.CharacterThreshold = (double)numericCharacterThreshold.Value;
                settings.ReplaceUnderscoresWithSpaces = checkReplaceUnderscores.Checked;
                settings.TagPrefix = textTagPrefix.Text ?? string.Empty;
                settings.TagSuffix = textTagSuffix.Text ?? string.Empty;
                settings.DownloadSource = downloadSource;
                settings.SetMode = setMode;
                settings.SortMode = sortMode;
            }
            else
            {
                Wd14TaggerSettings settings = Program.Settings.Wd14Tagger;
                settings.SetThresholdsForRepo(
                    ThresholdKey(entry),
                    (double)numericThreshold.Value,
                    (double)numericCharacterThreshold.Value);
                settings.ReplaceUnderscoresWithSpaces = checkReplaceUnderscores.Checked;
                settings.TagPrefix = textTagPrefix.Text ?? string.Empty;
                settings.TagSuffix = textTagSuffix.Text ?? string.Empty;
                settings.DownloadSource = downloadSource;
                settings.SetMode = setMode;
                settings.SortMode = sortMode;
            }
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

        private void UpdateModelStatus()
        {
            OnnxTaggerModelEntry entry = GetSelectedModel();
            bool ready = IsModelReady(entry);
            string text = ready ? I18n.GetText("TaggerModelReady") : I18n.GetText("TaggerModelMissing");
            labelModelStatus.Text = text;
            toolTip.SetToolTip(labelModelStatus, text);
        }

        private void UpdateStatus(string line)
        {
            labelStatus.Text = line;
            toolTip.SetToolTip(labelStatus, line);
        }

        private void CompleteDownloadSuccess()
        {
            progressBar.Value = 0;
            UpdateModelStatus();
            UpdateStatus(I18n.GetText("TaggerReadyToRun"));
            MessageBox.Show(this, I18n.GetText("TaggerDownloadCompleted"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateIdleStatus()
        {
            if (IsJobRunning())
                return;

            OnnxTaggerModelEntry entry = GetSelectedModel();
            UpdateStatus(IsModelReady(entry)
                ? I18n.GetText("TaggerReadyToRun")
                : I18n.GetText("TaggerModelMissing"));
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

            IEnumerable<DatasetManager.DataItem> pool = radioSourceFolder.Checked
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

            string extension = Path.GetExtension(path).ToLowerInvariant();
            return Extensions.ImageExtensions.Contains(extension);
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

        private bool IsModelReady(OnnxTaggerModelEntry entry)
        {
            return entry.Kind switch
            {
                OnnxTaggerModelKind.PixAi => pixAiService.IsModelReady(),
                OnnxTaggerModelKind.ClTagger => clService.IsModelReady(entry.ClModel),
                _ => wd14Service.IsModelReady(entry.Repo)
            };
        }

        private HuggingFaceDownloadSource GetSelectedDownloadSource()
        {
            return GetSelectedEnum<HuggingFaceDownloadSource>(comboDownloadSource);
        }

        private string FormatDownloadProgress(string fileName, long downloaded, long? total)
        {
            if (total.HasValue && total.Value > 0)
            {
                int percent = (int)Math.Clamp(Math.Round(downloaded * 100.0 / total.Value), 0, 100);
                return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, percent);
            }

            return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, 0);
        }

        private async Task DownloadModelAsync()
        {
            if (IsJobRunning())
                return;

            OnnxTaggerModelEntry entry = GetSelectedModel();
            string clAuthToken = null;
            if (entry.Kind == OnnxTaggerModelKind.ClTagger && entry.ClModel.IsGated)
            {
                // The author's license forbids redistribution/bundling and the
                // repo is gated: confirm the terms and collect the user's own
                // HuggingFace token before touching the network.
                if (!Form_GatedModelNotice.ConfirmDownload(this, entry.ClModel, out clAuthToken))
                    return;
            }
            SaveSettingsFromControls();
            SetJobRunning(true);
            jobCancellation = new CancellationTokenSource();
            var reporter = VideoProgressReporter.CreateForControl(this, UpdateStatus);

            try
            {
                progressBar.Value = 0;
                reporter.Report(I18n.GetText("TaggerDownloadModel"));
                var progress = new Progress<(string file, long downloaded, long? total)>(report =>
                {
                    reporter.Report(FormatDownloadProgress(report.file, report.downloaded, report.total));
                    if (report.total.HasValue && report.total.Value > 0)
                    {
                        int percent = (int)Math.Clamp(Math.Round(report.downloaded * 100.0 / report.total.Value), 0, 100);
                        progressBar.Value = percent;
                    }
                });

                if (entry.Kind == OnnxTaggerModelKind.PixAi)
                    await pixAiService.DownloadModelAsync(GetSelectedDownloadSource(), progress, jobCancellation.Token).ConfigureAwait(true);
                else if (entry.Kind == OnnxTaggerModelKind.ClTagger)
                    await clService.DownloadModelAsync(entry.ClModel, GetSelectedDownloadSource(), clAuthToken, progress, jobCancellation.Token).ConfigureAwait(true);
                else
                    await wd14Service.DownloadModelAsync(entry.Repo, GetSelectedDownloadSource(), progress, jobCancellation.Token).ConfigureAwait(true);

                await VerifyDownloadedModelAsync(entry).ConfigureAwait(true);

                CompleteDownloadSuccess();
            }
            catch (OperationCanceledException)
            {
                progressBar.Value = 0;
                UpdateStatus(I18n.GetText("TaggerCancelled"));
                UpdateModelStatus();
            }
            catch (System.Net.Http.HttpRequestException ex) when (entry.Kind == OnnxTaggerModelKind.ClTagger
                && (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized || ex.StatusCode == System.Net.HttpStatusCode.Forbidden))
            {
                // Gated repo refused the request: access not granted yet or a
                // bad/expired token. Point at the manual-placement folder too.
                progressBar.Value = 0;
                UpdateModelStatus();
                UpdateIdleStatus();
                MessageBox.Show(
                    this,
                    string.Format(I18n.GetText("TaggerGatedDownloadDenied"),
                        HuggingFaceModelDownloader.GetLocalDirectory(entry.Repo)),
                    I18n.GetText("UIError"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                progressBar.Value = 0;
                UpdateModelStatus();
                UpdateIdleStatus();
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                jobCancellation?.Dispose();
                jobCancellation = null;
                SetJobRunning(false);
                // Direct download-button path: honor a close requested mid-download.
                // When called from RunJobCoreAsync, the RunJobAsync wrapper closes.
                if (closeAfterJob && !runJobActive)
                {
                    closeAfterJob = false;
                    Close();
                }
            }
        }

        private async Task VerifyDownloadedModelAsync(OnnxTaggerModelEntry entry)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (entry.Kind == OnnxTaggerModelKind.PixAi)
                    {
                        pixAiService.Unload();
                        pixAiService.LoadModel();
                    }
                    else if (entry.Kind == OnnxTaggerModelKind.ClTagger)
                    {
                        clService.Unload();
                        clService.LoadModel(entry.ClModel);
                    }
                    else
                    {
                        wd14Service.Unload();
                        wd14Service.LoadModel(entry.Repo);
                    }
                }, jobCancellation.Token).ConfigureAwait(true);
            }
            catch (ModelCorruptedException)
            {
                // The service already deleted the bad file(s) and localized the
                // message; just propagate.
                throw;
            }
            catch (Exception ex)
            {
                ClearModelCache(entry);
                throw new InvalidOperationException(I18n.GetText("TaggerModelCorrupt"), ex);
            }
        }

        private void ClearModelCache(OnnxTaggerModelEntry entry)
        {
            if (entry.Kind == OnnxTaggerModelKind.PixAi)
                pixAiService.ClearModelCache();
            else if (entry.Kind == OnnxTaggerModelKind.ClTagger)
                clService.ClearModelCache(entry.ClModel);
            else
                wd14Service.ClearModelCache(entry.Repo);
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
            SaveSettingsFromControls();
            List<string> inputs = ResolveInputImages();
            if (inputs.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OnnxTaggerModelEntry entry = GetSelectedModel();
            if (!IsModelReady(entry))
            {
                if (MessageBox.Show(this, I18n.GetText("TaggerDownloadConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                await DownloadModelAsync().ConfigureAwait(true);
                if (!IsModelReady(entry))
                    return;
            }

            SetJobRunning(true);
            jobCancellation = new CancellationTokenSource();
            int completed = 0;
            var errors = new List<string>();
            var progressTracker = new OnnxTaggerProgressTracker();
            int totalImages = inputs.Count;
            var progressReporter = VideoProgressReporter.CreateForControl(this, UpdateStatus, throttleMilliseconds: 100);

            try
            {
                List<BatchInferenceResult> inferenceResults;
                if (entry.Kind == OnnxTaggerModelKind.PixAi)
                {
                    PixAiTaggerSettings settings = Program.Settings.PixAiTagger;
                    inferenceResults = await Task.Run(() =>
                    {
                        pixAiService.LoadModel();
                        return RunBatchInference(
                            inputs,
                            input => pixAiService.TagImageWithTiming(input, settings.GeneralThreshold, settings.CharacterThreshold),
                            progressTracker,
                            progressReporter,
                            value => ReportBatchProgress(value, totalImages),
                            ref completed,
                            errors,
                            jobCancellation.Token);
                    }, jobCancellation.Token).ConfigureAwait(true);

                    ApplyBatchInferenceResults(inferenceResults, () => settings, errors);
                }
                else if (entry.Kind == OnnxTaggerModelKind.ClTagger)
                {
                    Wd14TaggerSettings settings = Program.Settings.Wd14Tagger;
                    (double generalThreshold, double characterThreshold) = settings.GetThresholdsForRepo(ThresholdKey(entry));
                    inferenceResults = await Task.Run(() =>
                    {
                        clService.LoadModel(entry.ClModel);
                        return RunBatchInference(
                            inputs,
                            input => clService.TagImageWithTiming(input, generalThreshold, characterThreshold),
                            progressTracker,
                            progressReporter,
                            value => ReportBatchProgress(value, totalImages),
                            ref completed,
                            errors,
                            jobCancellation.Token);
                    }, jobCancellation.Token).ConfigureAwait(true);

                    ApplyBatchInferenceResults(inferenceResults, () => settings, errors);
                }
                else
                {
                    Wd14TaggerSettings settings = Program.Settings.Wd14Tagger;
                    (double generalThreshold, double characterThreshold) = settings.GetThresholdsForRepo(entry.Repo);
                    inferenceResults = await Task.Run(() =>
                    {
                        wd14Service.LoadModel(entry.Repo);
                        return RunBatchInference(
                            inputs,
                            input => wd14Service.TagImageWithTiming(input, generalThreshold, characterThreshold),
                            progressTracker,
                            progressReporter,
                            value => ReportBatchProgress(value, totalImages),
                            ref completed,
                            errors,
                            jobCancellation.Token);
                    }, jobCancellation.Token).ConfigureAwait(true);

                    ApplyBatchInferenceResults(inferenceResults, () => settings, errors);
                }

                progressBar.Value = Math.Min(100, (int)Math.Round(completed * 100.0 / inputs.Count));
                progressReporter.Report(progressTracker.FormatStatusLine(
                    completed > 0 ? Path.GetFileName(inputs[completed - 1]) : string.Empty,
                    completed,
                    inputs.Count));

                Program.Settings.SaveSettings();
                await Task.Run(() => Program.DataManager?.SaveAll()).ConfigureAwait(true);
                if (Program.DataManager != null && Program.DataManager.LastSaveErrors.Count > 0)
                    errors.AddRange(Program.DataManager.LastSaveErrors);

                bool wasCanceled = jobCancellation?.Token.IsCancellationRequested == true;
                if (errors.Count > 0 && !closeAfterJob)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors), I18n.GetText("TaggerCompletedWithErrors"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    UpdateStatus(I18n.GetText(wasCanceled ? "TaggerCancelled" : "TaggerCompleted"));
                }
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

        private void ReportBatchProgress(int completed, int total)
        {
            if (IsDisposed)
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
                // Form destroyed between the IsDisposed check and BeginInvoke:
                // an exception here runs on the inference thread pool thread and
                // would take the whole process down.
            }
            catch (InvalidOperationException)
            {
            }
        }

        private sealed class BatchInferenceResult
        {
            public string Input { get; init; }
            public OnnxTagResult Result { get; init; }
        }

        private static List<BatchInferenceResult> RunBatchInference(
            IReadOnlyList<string> inputs,
            Func<string, OnnxTagResult> tagImage,
            OnnxTaggerProgressTracker progressTracker,
            VideoProgressReporter progressReporter,
            Action<int> reportProgress,
            ref int completed,
            List<string> errors,
            CancellationToken cancellationToken)
        {
            var results = new List<BatchInferenceResult>(inputs.Count);
            foreach (string input in inputs)
            {
                // Stop instead of throwing so results computed so far survive a
                // cancel/close and still get applied and saved by the caller.
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    OnnxTagResult result = tagImage(input);
                    progressTracker.RecordInference(result.ElapsedMilliseconds);
                    results.Add(new BatchInferenceResult { Input = input, Result = result });
                }
                catch (Exception ex)
                {
                    errors.Add(input + ": " + ex.Message);
                }

                completed++;
                reportProgress(completed);
                progressReporter.Report(progressTracker.FormatStatusLine(
                    Path.GetFileName(input),
                    completed,
                    inputs.Count));
            }

            return results;
        }

        private void ApplyBatchInferenceResults<TSettings>(
            IReadOnlyList<BatchInferenceResult> results,
            Func<TSettings> getSettings,
            List<string> errors)
            where TSettings : TaggerSettings
        {
            if (results.Count == 0 || Program.DataManager == null)
                return;

            TSettings settings = getSettings();
            owner.PrepareForBulkTagWrite();
            try
            {
                Program.DataManager.ExecuteBulkMutation(() =>
                {
                    foreach (BatchInferenceResult item in results)
                    {
                        if (!TryGetDataItem(item.Input, out DataItem dataItem))
                        {
                            errors.Add(item.Input + ": " + I18n.GetText("TaggerDatasetItemMissing"));
                            continue;
                        }

                        TagWriteService.ApplyTags(dataItem, item.Result.Tags, settings);
                    }
                });
            }
            finally
            {
                owner.CompleteBulkTagWrite();
            }
        }

        private bool IsJobRunning()
        {
            return jobCancellation != null;
        }

        private void SetJobRunning(bool running)
        {
            buttonRun.Enabled = !running;
            buttonCancelJob.Enabled = running;
            buttonDownloadModel.Enabled = !running;
            radioSourceSelected.Enabled = !running;
            radioSourceAllImages.Enabled = !running;
            radioSourceFolder.Enabled = !running && folderSourceAvailable;
            comboModel.Enabled = !running;
            numericThreshold.Enabled = !running;
            numericCharacterThreshold.Enabled = !running;
            checkReplaceUnderscores.Enabled = !running;
            textTagPrefix.Enabled = !running;
            textTagSuffix.Enabled = !running;
            comboDownloadSource.Enabled = !running;
            comboSetMode.Enabled = !running;
            comboSortMode.Enabled = !running;
        }
    }
}
