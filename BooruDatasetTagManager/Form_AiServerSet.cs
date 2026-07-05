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

        private Label labelOpenAiEndpoint;
        private TextBox textBoxOpenAiEndpoint;
        private Label labelOpenAiApiKey;
        private TextBox textBoxOpenAiApiKey;
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

        public Form_AiServerSet(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            LoadSettings();
            ApplyLanguage();
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "AiServerSet";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 520);
            MinimumSize = new Size(760, 460);
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            groupConnection = CreateConnectionGroup();
            root.Controls.Add(groupConnection, 0, 0);

            groupVisionModels = CreateVisionModelsGroup();
            root.Controls.Add(groupVisionModels, 0, 1);

            groupLlmT2NlPrompt = new GroupBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
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
                RowCount = 5
            };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 5; index++)
                connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            labelOpenAiEndpoint = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            textBoxOpenAiEndpoint = new TextBox { Dock = DockStyle.Fill };
            labelOpenAiApiKey = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
            textBoxOpenAiApiKey = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
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

            TableLayoutPanel modelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            modelRow.Controls.Add(comboOpenAiModel, 0, 0);
            modelRow.Controls.Add(buttonRefreshModels, 1, 0);

            TableLayoutPanel concurrencyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            concurrencyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            concurrencyRow.Controls.Add(numericLlmT2NlConcurrency, 0, 0);
            concurrencyRow.Controls.Add(labelSpeedTestResult, 1, 0);
            concurrencyRow.Controls.Add(buttonSpeedTest, 2, 0);

            connection.Controls.Add(labelOpenAiEndpoint, 0, 0);
            connection.Controls.Add(textBoxOpenAiEndpoint, 1, 0);
            connection.Controls.Add(labelOpenAiApiKey, 0, 1);
            connection.Controls.Add(textBoxOpenAiApiKey, 1, 1);
            connection.Controls.Add(labelOpenAiTimeout, 0, 2);
            connection.Controls.Add(numericOpenAiTimeout, 1, 2);
            connection.Controls.Add(labelOpenAiModel, 0, 3);
            connection.Controls.Add(modelRow, 1, 3);
            connection.Controls.Add(labelLlmT2NlConcurrency, 0, 4);
            connection.Controls.Add(concurrencyRow, 1, 4);
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

            TableLayoutPanel auditModelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
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
            labelOpenAiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            labelOpenAiApiKey.Text = I18n.GetText("SettingsOpenAiApiKey");
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
            textBoxOpenAiEndpoint.Text = Program.Settings.OpenAiAutoTagger.ConnectionAddress;
            textBoxOpenAiApiKey.Text = Program.Settings.OpenAiAutoTagger.ApiKey;
            numericOpenAiTimeout.Value = Math.Clamp(
                Program.Settings.OpenAiAutoTagger.RequestTimeout,
                (int)numericOpenAiTimeout.Minimum,
                (int)numericOpenAiTimeout.Maximum);
            numericLlmT2NlConcurrency.Value = Math.Clamp(
                Program.Settings.LlmT2NlConcurrency,
                (int)numericLlmT2NlConcurrency.Minimum,
                (int)numericLlmT2NlConcurrency.Maximum);
            SetModelItems(comboOpenAiModel, Program.OpenAiAutoTagger?.Models, Program.Settings.OpenAiAutoTagger.Model);
            SetModelItems(comboVisionModel, Program.OpenAiAutoTagger?.Models, Program.Settings.OpenAiAutoTagger.VisionModel);
            SetModelItems(comboCharacterTagAuditModel, Program.OpenAiAutoTagger?.Models, Program.Settings.CharacterTagAuditModel);
            textBoxLlmT2NlPrompt.Text = AiPromptTemplateCatalog.LlmT2NlSystemPrompt;
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
                    string.IsNullOrWhiteSpace(textBoxOpenAiApiKey.Text) ? "not-required" : textBoxOpenAiApiKey.Text,
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
                    string.IsNullOrWhiteSpace(textBoxOpenAiApiKey.Text) ? "not-required" : textBoxOpenAiApiKey.Text,
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
            if (!TryGetEndpoint(out _))
                return;
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

            AiServerSetSettingsService.Save(
                textBoxOpenAiEndpoint.Text.Trim(),
                textBoxOpenAiApiKey.Text,
                (int)numericOpenAiTimeout.Value,
                comboOpenAiModel.SelectedItem as string,
                comboVisionModel.SelectedItem as string,
                comboCharacterTagAuditModel.SelectedItem as string,
                (int)numericLlmT2NlConcurrency.Value,
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId);
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
