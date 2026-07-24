using BooruDatasetTagManager.AiApi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_AiServerSet : Form
    {
        private readonly MainForm owner;

        private Label labelProfiles;
        private ComboBox comboProfiles;
        private Button buttonProfileAdd;
        private Button buttonProfileRename;
        private Button buttonProfileDelete;
        private Label labelOpenAiEndpoint;
        private TextBox textBoxOpenAiEndpoint;
        private Label labelOpenAiApiKey;
        private ListBox listTokens;
        private Button buttonTokenAdd;
        private Button buttonTokenDelete;
        private Label labelTokenHint;
        private Label labelOpenAiTimeout;
        private NumericUpDown numericOpenAiTimeout;
        private Label labelOpenAiModel;
        private ComboBox comboOpenAiModel;
        private Label labelVisionModel;
        private ComboBox comboVisionModel;
        private Label labelCharacterTagAuditModel;
        private ComboBox comboCharacterTagAuditModel;
        private Label labelCharacterTagAuditRecommendation;
        private Button buttonRefreshModels;
        private Label labelLlmT2NlConcurrency;
        private NumericUpDown numericLlmT2NlConcurrency;
        private Button buttonSpeedTest;
        private Label labelSpeedTestResult;
        private GroupBox groupConnection;
        private GroupBox groupVisionModels;
        private GroupBox groupLlmT2NlPrompt;
        private TextBox textBoxLlmT2NlPrompt;
        private Button buttonSave;
        private Button buttonCancel;

        // In-dialog working copy of the site profiles; committed only on Save.
        private List<LlmApiProfile> workingProfiles;
        private int workingIndex;
        private bool suppressProfileEvents;

        public Form_AiServerSet(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            LoadSettings();
            ApplyLanguage();
        }

        private void InitializeComponent()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "AiServerSet";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 530);
            MinimumSize = new Size(760, 480);
            ShowInTaskbar = false;

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(8)
            };
            buttonCancel = new Button { AutoSize = true, MinimumSize = new Size(100, 30), DialogResult = DialogResult.Cancel };
            buttonSave = new Button { AutoSize = true, MinimumSize = new Size(100, 30) };
            buttonSave.Click += buttonSave_Click;
            buttons.Controls.Add(buttonCancel);
            buttons.Controls.Add(buttonSave);
            Controls.Add(buttons);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            groupConnection = CreateConnectionGroup();
            root.Controls.Add(groupConnection, 0, 0);

            groupVisionModels = CreateVisionModelsGroup();
            root.Controls.Add(groupVisionModels, 0, 1);

            // The fixed natural-language prompt is internal detail; keep the
            // group constructed (settings load still fills it) but hidden.
            groupLlmT2NlPrompt = new GroupBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Visible = false };
            textBoxLlmT2NlPrompt = CreatePromptTextBox(true);
            groupLlmT2NlPrompt.Controls.Add(textBoxLlmT2NlPrompt);
            root.Controls.Add(groupLlmT2NlPrompt, 0, 2);

            AcceptButton = buttonSave;
            CancelButton = buttonCancel;
        }

        private GroupBox CreateConnectionGroup()
        {
            GroupBox group = new GroupBox { Dock = DockStyle.Top, AutoSize = true };
            TableLayoutPanel connection = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 6
            };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 6; index++)
                connection.RowStyles.Add(new RowStyle(SizeType.Absolute, index == 2 ? 96 : 34));

            labelProfiles = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            comboProfiles = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            comboProfiles.SelectedIndexChanged += comboProfiles_SelectedIndexChanged;
            buttonProfileAdd = new Button { AutoSize = true, MinimumSize = new Size(64, 28) };
            buttonProfileAdd.Click += buttonProfileAdd_Click;
            buttonProfileRename = new Button { AutoSize = true, MinimumSize = new Size(64, 28) };
            buttonProfileRename.Click += buttonProfileRename_Click;
            buttonProfileDelete = new Button { AutoSize = true, MinimumSize = new Size(64, 28) };
            buttonProfileDelete.Click += buttonProfileDelete_Click;

            // Sub-row panels use Margin 0 so their inner controls (own margin 3)
            // share the exact left/right rail of the directly-placed text boxes.
            TableLayoutPanel profileRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0) };
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileRow.Controls.Add(comboProfiles, 0, 0);
            profileRow.Controls.Add(buttonProfileAdd, 1, 0);
            profileRow.Controls.Add(buttonProfileRename, 2, 0);
            profileRow.Controls.Add(buttonProfileDelete, 3, 0);

            labelOpenAiEndpoint = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            textBoxOpenAiEndpoint = new TextBox { Dock = DockStyle.Fill };

            // Stored keys are listed masked (tail only) and can only be added
            // or removed — the full value is never displayed again.
            labelOpenAiApiKey = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            listTokens = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            labelTokenHint = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 2, 3, 0) };
            buttonTokenAdd = new Button { AutoSize = true, MinimumSize = new Size(84, 28), Margin = new Padding(6, 3, 3, 3) };
            buttonTokenAdd.Click += buttonTokenAdd_Click;
            buttonTokenDelete = new Button { AutoSize = true, MinimumSize = new Size(84, 28), Margin = new Padding(6, 3, 3, 3) };
            buttonTokenDelete.Click += buttonTokenDelete_Click;

            TableLayoutPanel tokenLeft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            tokenLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tokenLeft.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tokenLeft.Controls.Add(listTokens, 0, 0);
            tokenLeft.Controls.Add(labelTokenHint, 0, 1);

            // AutoSize is required: without it the panel keeps the FlowLayoutPanel
            // default width (200) and the AutoSize column inherits it, stranding
            // the buttons ~110px left of the right edge.
            FlowLayoutPanel tokenButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            tokenButtons.Controls.Add(buttonTokenAdd);
            tokenButtons.Controls.Add(buttonTokenDelete);

            TableLayoutPanel tokenRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            tokenRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tokenRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tokenRow.Controls.Add(tokenLeft, 0, 0);
            tokenRow.Controls.Add(tokenButtons, 1, 0);

            labelOpenAiTimeout = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            numericOpenAiTimeout = new NumericUpDown
            {
                Anchor = AnchorStyles.Left,
                Minimum = 1,
                Maximum = 99999,
                Width = 120
            };
            labelOpenAiModel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            comboOpenAiModel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            buttonRefreshModels = new Button { Dock = DockStyle.Right, Width = 180 };
            buttonRefreshModels.Click += buttonRefreshModels_Click;
            labelLlmT2NlConcurrency = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            numericLlmT2NlConcurrency = new NumericUpDown
            {
                Anchor = AnchorStyles.Left,
                Minimum = 1,
                Maximum = 100,
                Width = 80
            };
            buttonSpeedTest = new Button { Dock = DockStyle.Right, Width = 120 };
            buttonSpeedTest.Click += buttonSpeedTest_Click;
            labelSpeedTestResult = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            TableLayoutPanel modelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            modelRow.Controls.Add(comboOpenAiModel, 0, 0);
            modelRow.Controls.Add(buttonRefreshModels, 1, 0);

            TableLayoutPanel concurrencyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            concurrencyRow.Controls.Add(numericLlmT2NlConcurrency, 0, 0);
            concurrencyRow.Controls.Add(labelSpeedTestResult, 1, 0);
            concurrencyRow.Controls.Add(buttonSpeedTest, 2, 0);

            connection.Controls.Add(labelProfiles, 0, 0);
            connection.Controls.Add(profileRow, 1, 0);
            connection.Controls.Add(labelOpenAiEndpoint, 0, 1);
            connection.Controls.Add(textBoxOpenAiEndpoint, 1, 1);
            connection.Controls.Add(labelOpenAiApiKey, 0, 2);
            connection.Controls.Add(tokenRow, 1, 2);
            connection.Controls.Add(labelOpenAiTimeout, 0, 3);
            connection.Controls.Add(numericOpenAiTimeout, 1, 3);
            connection.Controls.Add(labelOpenAiModel, 0, 4);
            connection.Controls.Add(modelRow, 1, 4);
            connection.Controls.Add(labelLlmT2NlConcurrency, 0, 5);
            connection.Controls.Add(concurrencyRow, 1, 5);
            group.Controls.Add(connection);
            return group;
        }

        private GroupBox CreateVisionModelsGroup()
        {
            GroupBox group = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            labelVisionModel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            comboVisionModel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            labelCharacterTagAuditModel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            comboCharacterTagAuditModel = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            labelCharacterTagAuditRecommendation = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Color.DimGray,
                Margin = new Padding(10, 5, 0, 0)
            };

            TableLayoutPanel auditModelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            auditModelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            auditModelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            auditModelRow.Controls.Add(comboCharacterTagAuditModel, 0, 0);
            auditModelRow.Controls.Add(labelCharacterTagAuditRecommendation, 1, 0);

            layout.Controls.Add(labelVisionModel, 0, 0);
            layout.Controls.Add(comboVisionModel, 1, 0);
            layout.Controls.Add(labelCharacterTagAuditModel, 0, 1);
            layout.Controls.Add(auditModelRow, 1, 1);
            group.Controls.Add(layout);
            return group;
        }

        private static TextBox CreatePromptTextBox(bool readOnly)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = readOnly,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true
            };
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("MenuAiServerSet");
            groupConnection.Text = I18n.GetText("AiServerSetOpenAiConnection");
            groupVisionModels.Text = I18n.GetText("AiServerSetVisionModels");
            labelProfiles.Text = I18n.GetText("AiServerSetProfileLabel");
            buttonProfileAdd.Text = I18n.GetText("AiServerSetProfileAdd");
            buttonProfileRename.Text = I18n.GetText("AiServerSetProfileRename");
            buttonProfileDelete.Text = I18n.GetText("AiServerSetProfileDelete");
            labelOpenAiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            labelOpenAiApiKey.Text = I18n.GetText("SettingsOpenAiApiKey");
            buttonTokenAdd.Text = I18n.GetText("AiServerSetTokenAdd");
            buttonTokenDelete.Text = I18n.GetText("AiServerSetTokenDelete");
            labelTokenHint.Text = I18n.GetText("AiServerSetTokenRotateHint");
            labelOpenAiTimeout.Text = I18n.GetText("SettingsOpenAiRequestTimeout");
            labelOpenAiModel.Text = I18n.GetText("AiServerSetTextModel");
            labelVisionModel.Text = I18n.GetText("AiServerSetVisionModel");
            labelCharacterTagAuditModel.Text = I18n.GetText("CharacterTagAuditModel");
            labelCharacterTagAuditRecommendation.Text = I18n.GetText("CharacterTagAuditGeminiRecommendation");
            buttonRefreshModels.Text = I18n.GetText("AiServerSetRefreshModels");
            labelLlmT2NlConcurrency.Text = I18n.GetText("AiServerSetLlmT2NlConcurrency");
            buttonSpeedTest.Text = I18n.GetText("AiServerSetSpeedTest");
            groupLlmT2NlPrompt.Text = I18n.GetText("AiServerSetLlmT2NlPrompt");
            buttonSave.Text = I18n.GetText("SettingBtnSave");
            buttonCancel.Text = I18n.GetText("BtnCancel");
        }

        private void LoadSettings()
        {
            workingProfiles = Program.Settings.LlmApiProfiles
                .Where(profile => profile != null)
                .Select(profile => profile.Clone())
                .ToList();
            if (workingProfiles.Count == 0)
            {
                // Fresh config (or nothing to migrate): start from the flat fields.
                workingProfiles.Add(new LlmApiProfile
                {
                    Name = LlmApiProfileLogic.SuggestName(Program.Settings.OpenAiAutoTagger.ConnectionAddress),
                    Endpoint = Program.Settings.OpenAiAutoTagger.ConnectionAddress ?? string.Empty,
                    Model = Program.Settings.OpenAiAutoTagger.Model ?? string.Empty,
                    VisionModel = Program.Settings.OpenAiAutoTagger.VisionModel ?? string.Empty,
                    AuditModel = Program.Settings.CharacterTagAuditModel ?? string.Empty,
                    Tokens = LlmApiProfileLogic.SanitizeTokens(new[] { Program.Settings.OpenAiAutoTagger.ApiKey })
                });
            }
            workingIndex = LlmApiProfileLogic.ClampIndex(Program.Settings.LlmApiProfileIndex, workingProfiles.Count);

            numericOpenAiTimeout.Value = Math.Clamp(
                Program.Settings.OpenAiAutoTagger.RequestTimeout,
                (int)numericOpenAiTimeout.Minimum,
                (int)numericOpenAiTimeout.Maximum);
            numericLlmT2NlConcurrency.Value = Math.Clamp(
                Program.Settings.LlmT2NlConcurrency,
                (int)numericLlmT2NlConcurrency.Minimum,
                (int)numericLlmT2NlConcurrency.Maximum);
            RefreshProfileCombo();
            LoadProfileIntoUi(workingProfiles[workingIndex]);
            textBoxLlmT2NlPrompt.Text = AiPromptTemplateCatalog.LlmT2NlSystemPrompt;
        }

        private static string GetProfileDisplayName(LlmApiProfile profile)
        {
            return string.IsNullOrWhiteSpace(profile.Name)
                ? LlmApiProfileLogic.SuggestName(profile.Endpoint)
                : profile.Name;
        }

        private void RefreshProfileCombo()
        {
            suppressProfileEvents = true;
            try
            {
                comboProfiles.BeginUpdate();
                comboProfiles.Items.Clear();
                foreach (LlmApiProfile profile in workingProfiles)
                    comboProfiles.Items.Add(GetProfileDisplayName(profile));
                comboProfiles.SelectedIndex = workingIndex;
                comboProfiles.EndUpdate();
            }
            finally
            {
                suppressProfileEvents = false;
            }
        }

        private void LoadProfileIntoUi(LlmApiProfile profile)
        {
            textBoxOpenAiEndpoint.Text = profile.Endpoint ?? string.Empty;
            RefreshTokenList(profile);
            SetModelItems(comboOpenAiModel, Program.OpenAiAutoTagger?.Models, profile.Model);
            SetModelItems(comboVisionModel, Program.OpenAiAutoTagger?.Models, profile.VisionModel);
            SetModelItems(comboCharacterTagAuditModel, Program.OpenAiAutoTagger?.Models, profile.AuditModel);
        }

        private void StashUiIntoProfile(LlmApiProfile profile)
        {
            profile.Endpoint = textBoxOpenAiEndpoint.Text.Trim();
            profile.Model = comboOpenAiModel.SelectedItem as string ?? string.Empty;
            profile.VisionModel = comboVisionModel.SelectedItem as string ?? string.Empty;
            profile.AuditModel = comboCharacterTagAuditModel.SelectedItem as string ?? string.Empty;
        }

        private void RefreshTokenList(LlmApiProfile profile)
        {
            listTokens.BeginUpdate();
            listTokens.Items.Clear();
            for (int index = 0; index < profile.Tokens.Count; index++)
                listTokens.Items.Add($"{index + 1}.  {LlmApiProfileLogic.MaskToken(profile.Tokens[index])}");
            listTokens.EndUpdate();
        }

        private void comboProfiles_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressProfileEvents || comboProfiles.SelectedIndex < 0 || comboProfiles.SelectedIndex == workingIndex)
                return;
            StashUiIntoProfile(workingProfiles[workingIndex]);
            workingIndex = comboProfiles.SelectedIndex;
            LoadProfileIntoUi(workingProfiles[workingIndex]);
        }

        private void buttonProfileAdd_Click(object sender, EventArgs e)
        {
            string name = PromptText(buttonProfileAdd.Text, I18n.GetText("AiServerSetProfileNamePrompt"), string.Empty, false);
            if (string.IsNullOrWhiteSpace(name))
                return;
            StashUiIntoProfile(workingProfiles[workingIndex]);
            workingProfiles.Add(new LlmApiProfile { Name = name.Trim() });
            workingIndex = workingProfiles.Count - 1;
            RefreshProfileCombo();
            LoadProfileIntoUi(workingProfiles[workingIndex]);
        }

        private void buttonProfileRename_Click(object sender, EventArgs e)
        {
            LlmApiProfile profile = workingProfiles[workingIndex];
            string name = PromptText(buttonProfileRename.Text, I18n.GetText("AiServerSetProfileNamePrompt"), profile.Name, false);
            if (string.IsNullOrWhiteSpace(name))
                return;
            profile.Name = name.Trim();
            RefreshProfileCombo();
        }

        private void buttonProfileDelete_Click(object sender, EventArgs e)
        {
            if (workingProfiles.Count <= 1)
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetProfileKeepOne"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            LlmApiProfile profile = workingProfiles[workingIndex];
            string message = string.Format(I18n.GetText("AiServerSetProfileDeleteConfirm"), GetProfileDisplayName(profile));
            if (MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            workingProfiles.RemoveAt(workingIndex);
            workingIndex = LlmApiProfileLogic.ClampIndex(workingIndex, workingProfiles.Count);
            RefreshProfileCombo();
            LoadProfileIntoUi(workingProfiles[workingIndex]);
        }

        private void buttonTokenAdd_Click(object sender, EventArgs e)
        {
            string token = PromptText(buttonTokenAdd.Text, I18n.GetText("AiServerSetTokenPrompt"), string.Empty, true);
            token = token?.Trim();
            if (string.IsNullOrEmpty(token))
                return;
            LlmApiProfile profile = workingProfiles[workingIndex];
            if (profile.Tokens.Contains(token, StringComparer.Ordinal))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetTokenDuplicate"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            profile.Tokens.Add(token);
            RefreshTokenList(profile);
        }

        private void buttonTokenDelete_Click(object sender, EventArgs e)
        {
            LlmApiProfile profile = workingProfiles[workingIndex];
            int index = listTokens.SelectedIndex;
            if (index < 0 || index >= profile.Tokens.Count)
                return;
            if (MessageBox.Show(this, I18n.GetText("AiServerSetTokenDeleteConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            profile.Tokens.RemoveAt(index);
            RefreshTokenList(profile);
        }

        /// <summary>Small modal text prompt; masked input for secrets. Returns null on cancel.</summary>
        private string PromptText(string title, string caption, string initialValue, bool masked)
        {
            using (Form prompt = new Form())
            {
                prompt.Text = title;
                prompt.StartPosition = FormStartPosition.CenterParent;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.MinimizeBox = false;
                prompt.MaximizeBox = false;
                prompt.ShowInTaskbar = false;
                prompt.AutoScaleDimensions = new SizeF(96F, 96F);
                prompt.AutoScaleMode = AutoScaleMode.Dpi;
                prompt.AutoSize = true;
                prompt.AutoSizeMode = AutoSizeMode.GrowAndShrink;

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    AutoSize = true,
                    ColumnCount = 1,
                    Padding = new Padding(12),
                    Dock = DockStyle.Fill
                };
                Label label = new Label { AutoSize = true, MaximumSize = new Size(430, 0), Margin = new Padding(0, 0, 0, 8), Text = caption };
                TextBox input = new TextBox { Width = 430, UseSystemPasswordChar = masked, Text = initialValue ?? string.Empty };
                FlowLayoutPanel buttonRow = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft,
                    Margin = new Padding(0, 12, 0, 0),
                    Anchor = AnchorStyles.Right
                };
                Button cancel = new Button { DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(85, 28), Text = I18n.GetText("BtnCancel") };
                Button ok = new Button { DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(85, 28), Text = I18n.GetText("BtnOK") };
                buttonRow.Controls.Add(cancel);
                buttonRow.Controls.Add(ok);
                layout.Controls.Add(label);
                layout.Controls.Add(input);
                layout.Controls.Add(buttonRow);
                prompt.Controls.Add(layout);
                prompt.AcceptButton = ok;
                prompt.CancelButton = cancel;
                return prompt.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }

        private static void SetModelItems(ComboBox comboBox, IEnumerable<string> models, string selectedModel)
        {
            string[] values = (models ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            comboBox.BeginUpdate();
            comboBox.Items.Clear();
            comboBox.Items.AddRange(values);
            if (!string.IsNullOrWhiteSpace(selectedModel) && !comboBox.Items.Contains(selectedModel))
                comboBox.Items.Insert(0, selectedModel);
            comboBox.SelectedItem = selectedModel;
            if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
                comboBox.SelectedIndex = 0;
            comboBox.EndUpdate();
        }

        private async void buttonRefreshModels_Click(object sender, EventArgs e)
        {
            if (!TryGetEndpoint(out Uri endpoint))
                return;

            string selectedTextModel = comboOpenAiModel.SelectedItem as string;
            string selectedVisionModel = comboVisionModel.SelectedItem as string;
            string selectedAuditModel = comboCharacterTagAuditModel.SelectedItem as string;
            buttonRefreshModels.Enabled = false;
            try
            {
                AiOpenAiClient client = new AiOpenAiClient(
                    endpoint.ToString(),
                    workingProfiles[workingIndex].Tokens,
                    (int)numericOpenAiTimeout.Value);
                var result = await client.ConnectAsync();
                if (!result.Result)
                {
                    MessageBox.Show(this, result.ErrMessage, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                SetModelItems(comboOpenAiModel, client.Models, selectedTextModel);
                SetModelItems(comboVisionModel, client.Models, selectedVisionModel);
                SetModelItems(comboCharacterTagAuditModel, client.Models, selectedAuditModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonRefreshModels.Enabled = true;
            }
        }

        private async void buttonSpeedTest_Click(object sender, EventArgs e)
        {
            if (!TryGetEndpoint(out Uri endpoint))
                return;
            if (!(comboOpenAiModel.SelectedItem is string model) || string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetModelRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            buttonSpeedTest.Enabled = false;
            labelSpeedTestResult.Text = I18n.GetText("AiServerSetSpeedTesting");
            try
            {
                AiOpenAiClient client = new AiOpenAiClient(
                    endpoint.ToString(),
                    workingProfiles[workingIndex].Tokens,
                    (int)numericOpenAiTimeout.Value);
                OpenAiSpeedTestResult result = await OpenAiSpeedTestService.MeasureAsync(async cancellationToken =>
                {
                    OpenAiRequest request = new OpenAiRequest
                    {
                        Model = model,
                        SystemPrompt = "Return only the requested text.",
                        UserPrompt = "Reply with exactly: OK"
                    };
                    var response = await client.SendRequestAsync(request, cancellationToken);
                    return (response.Result, response.ErrMessage);
                });

                labelSpeedTestResult.Text = result.Success
                    ? string.Format(I18n.GetText("AiServerSetSpeedTestSuccess"), Math.Round(result.Elapsed.TotalMilliseconds))
                    : string.Format(I18n.GetText("AiServerSetSpeedTestFailed"), GetFriendlySpeedTestError(result.ErrorMessage));
            }
            catch (Exception ex)
            {
                labelSpeedTestResult.Text = string.Format(
                    I18n.GetText("AiServerSetSpeedTestFailed"),
                    GetFriendlySpeedTestError(ex.Message));
            }
            finally
            {
                buttonSpeedTest.Enabled = true;
            }
        }

        private static string GetFriendlySpeedTestError(string error)
        {
            switch (OpenAiConnectionErrorClassifier.Classify(error))
            {
                case OpenAiConnectionErrorKind.InvalidApiResponse:
                    return I18n.GetText("AiServerSetInvalidApiResponse");
                case OpenAiConnectionErrorKind.Authentication:
                    return I18n.GetText("AiServerSetAuthenticationError");
                case OpenAiConnectionErrorKind.Timeout:
                    return I18n.GetText("AiServerSetConnectionTimeout");
                case OpenAiConnectionErrorKind.Network:
                    return I18n.GetText("AiServerSetNetworkError");
                default:
                    return error;
            }
        }

        private bool TryGetEndpoint(out Uri endpoint)
        {
            if (!Uri.TryCreate(textBoxOpenAiEndpoint.Text.Trim(), UriKind.Absolute, out endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetInvalidEndpoint"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (!TryGetEndpoint(out Uri endpoint))
                return;
            if (AiServerSetSettingsService.IsInsecureEndpoint(endpoint)
                && MessageBox.Show(this, I18n.GetText("AiServerSetInsecureEndpointWarning"), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            if (comboOpenAiModel.SelectedItem == null)
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetModelRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (comboVisionModel.SelectedItem == null)
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetVisionModelRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (comboCharacterTagAuditModel.SelectedItem == null)
            {
                MessageBox.Show(this, I18n.GetText("CharacterTagAuditModelRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StashUiIntoProfile(workingProfiles[workingIndex]);
            AiServerSetSettingsService.Save(
                workingProfiles,
                workingIndex,
                (int)numericOpenAiTimeout.Value,
                (int)numericLlmT2NlConcurrency.Value,
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId);
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
