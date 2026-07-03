using BooruDatasetTagManager.AiApi;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_AiServerSet : Form
    {
        private readonly MainForm owner;
        private AiPromptTemplateLibrary promptLibrary;
        private bool isCreatingTemplate;
        private bool isBindingTemplates;

        private Label labelOpenAiEndpoint;
        private TextBox textBoxOpenAiEndpoint;
        private Label labelOpenAiApiKey;
        private TextBox textBoxOpenAiApiKey;
        private Label labelOpenAiTimeout;
        private NumericUpDown numericOpenAiTimeout;
        private Label labelOpenAiModel;
        private ComboBox comboOpenAiModel;
        private Button buttonRefreshModels;
        private Label labelLlmT2NlConcurrency;
        private NumericUpDown numericLlmT2NlConcurrency;
        private Button buttonSpeedTest;
        private Label labelSpeedTestResult;
        private GroupBox groupConnection;
        private GroupBox groupAutoTagPrompt;
        private GroupBox groupLlmT2NlPrompt;
        private ComboBox comboPromptTemplate;
        private TextBox textBoxTemplateName;
        private TextBox textBoxAutoTagPrompt;
        private TextBox textBoxLlmT2NlPrompt;
        private Button buttonNewTemplate;
        private Button buttonSaveTemplate;
        private Button buttonDeleteTemplate;
        private Button buttonRestoreTemplate;
        private Button buttonExportCurrent;
        private Button buttonExportAll;
        private Button buttonSave;
        private Button buttonCancel;

        public Form_AiServerSet(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            LoadSettings();
            ApplyLanguage();
            BindPromptTemplates(promptLibrary.SelectedTemplateId);
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "AiServerSet";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 760);
            MinimumSize = new Size(760, 650);
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
            Controls.Add(root);

            groupConnection = CreateConnectionGroup();
            root.Controls.Add(groupConnection, 0, 0);

            groupAutoTagPrompt = new GroupBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
            groupAutoTagPrompt.Controls.Add(CreatePromptEditor());
            root.Controls.Add(groupAutoTagPrompt, 0, 1);

            groupLlmT2NlPrompt = new GroupBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
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

        private Control CreatePromptEditor()
        {
            TableLayoutPanel editor = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 3
            };
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel selection = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            comboPromptTemplate = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 6, 6) };
            comboPromptTemplate.SelectedIndexChanged += comboPromptTemplate_SelectedIndexChanged;
            textBoxTemplateName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 6) };
            selection.Controls.Add(comboPromptTemplate, 0, 0);
            selection.Controls.Add(textBoxTemplateName, 1, 0);
            editor.Controls.Add(selection, 0, 0);

            textBoxAutoTagPrompt = CreatePromptTextBox(false);
            editor.Controls.Add(textBoxAutoTagPrompt, 0, 1);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(0, 6, 0, 0)
            };
            buttonNewTemplate = CreateActionButton(buttonNewTemplate_Click);
            buttonSaveTemplate = CreateActionButton(buttonSaveTemplate_Click);
            buttonDeleteTemplate = CreateActionButton(buttonDeleteTemplate_Click);
            buttonRestoreTemplate = CreateActionButton(buttonRestoreTemplate_Click);
            buttonExportCurrent = CreateActionButton(buttonExportCurrent_Click);
            buttonExportAll = CreateActionButton(buttonExportAll_Click);
            actions.Controls.AddRange(new Control[]
            {
                buttonNewTemplate,
                buttonSaveTemplate,
                buttonDeleteTemplate,
                buttonRestoreTemplate,
                buttonExportCurrent,
                buttonExportAll
            });
            editor.Controls.Add(actions, 0, 2);
            return editor;
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

        private static Button CreateActionButton(EventHandler handler)
        {
            Button button = new Button { AutoSize = true, MinimumSize = new Size(105, 30), Margin = new Padding(0, 0, 6, 0) };
            button.Click += handler;
            return button;
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("MenuAiServerSet");
            groupConnection.Text = I18n.GetText("AiServerSetOpenAiConnection");
            labelOpenAiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            labelOpenAiApiKey.Text = I18n.GetText("SettingsOpenAiApiKey");
            labelOpenAiTimeout.Text = I18n.GetText("SettingsOpenAiRequestTimeout");
            labelOpenAiModel.Text = I18n.GetText("AiServerSetOpenAiModel");
            buttonRefreshModels.Text = I18n.GetText("AiServerSetRefreshModels");
            labelLlmT2NlConcurrency.Text = I18n.GetText("AiServerSetLlmT2NlConcurrency");
            buttonSpeedTest.Text = I18n.GetText("AiServerSetSpeedTest");
            groupAutoTagPrompt.Text = I18n.GetText("AiServerSetAutoTagPrompt");
            groupLlmT2NlPrompt.Text = I18n.GetText("AiServerSetLlmT2NlPrompt");
            buttonNewTemplate.Text = I18n.GetText("AiServerSetPromptNew");
            buttonSaveTemplate.Text = I18n.GetText("AiServerSetPromptSave");
            buttonDeleteTemplate.Text = I18n.GetText("AiServerSetPromptDelete");
            buttonRestoreTemplate.Text = I18n.GetText("AiServerSetPromptRestore");
            buttonExportCurrent.Text = I18n.GetText("AiServerSetPromptExportCurrent");
            buttonExportAll.Text = I18n.GetText("AiServerSetPromptExportAll");
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
            SetModelItems(Program.OpenAiAutoTagger?.Models, Program.Settings.OpenAiAutoTagger.Model);
            promptLibrary = AiPromptTemplateLibrary.Create(
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            textBoxLlmT2NlPrompt.Text = AiPromptTemplateCatalog.LlmT2NlSystemPrompt;
        }

        private void SetModelItems(IEnumerable<string> models, string selectedModel)
        {
            string[] values = (models ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            comboOpenAiModel.BeginUpdate();
            comboOpenAiModel.Items.Clear();
            comboOpenAiModel.Items.AddRange(values);
            if (!string.IsNullOrWhiteSpace(selectedModel) && !comboOpenAiModel.Items.Contains(selectedModel))
                comboOpenAiModel.Items.Insert(0, selectedModel);
            comboOpenAiModel.SelectedItem = selectedModel;
            if (comboOpenAiModel.SelectedIndex < 0 && comboOpenAiModel.Items.Count > 0)
                comboOpenAiModel.SelectedIndex = 0;
            comboOpenAiModel.EndUpdate();
        }

        private void BindPromptTemplates(string selectedId)
        {
            isBindingTemplates = true;
            comboPromptTemplate.BeginUpdate();
            comboPromptTemplate.Items.Clear();
            foreach (AiPromptTemplateSettings template in promptLibrary.Templates)
                comboPromptTemplate.Items.Add(new PromptTemplateListItem(template.Id, GetTemplateDisplayName(template)));
            comboPromptTemplate.EndUpdate();
            comboPromptTemplate.SelectedItem = comboPromptTemplate.Items.Cast<PromptTemplateListItem>()
                .FirstOrDefault(item => item.Id == selectedId);
            isBindingTemplates = false;
            LoadSelectedTemplate();
        }

        private string GetTemplateDisplayName(AiPromptTemplateSettings template)
        {
            switch (template.Id)
            {
                case AiPromptTemplateCatalog.DanbooruTagId:
                    return I18n.GetText("PromptTemplateDanbooruTag");
                case AiPromptTemplateCatalog.NaturalLanguageId:
                    return I18n.GetText("PromptTemplateNaturalLanguage");
                case AiPromptTemplateCatalog.HybridModeId:
                    return I18n.GetText("PromptTemplateHybridMode");
                case AiPromptTemplateCatalog.NaturalLanguage2Id:
                    return I18n.GetText("PromptTemplateNaturalLanguage2");
                default:
                    return template.Name;
            }
        }

        private void comboPromptTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isBindingTemplates || !(comboPromptTemplate.SelectedItem is PromptTemplateListItem item))
                return;
            promptLibrary.Select(item.Id);
            isCreatingTemplate = false;
            LoadSelectedTemplate();
        }

        private void LoadSelectedTemplate()
        {
            AiPromptTemplateSettings template = promptLibrary.SelectedTemplate;
            textBoxTemplateName.Text = template.IsBuiltIn ? GetTemplateDisplayName(template) : template.Name;
            textBoxTemplateName.ReadOnly = template.IsBuiltIn;
            textBoxAutoTagPrompt.Text = template.SystemPrompt;
            buttonDeleteTemplate.Enabled = !template.IsBuiltIn;
            buttonRestoreTemplate.Enabled = template.IsBuiltIn;
        }

        private void buttonNewTemplate_Click(object sender, EventArgs e)
        {
            isCreatingTemplate = true;
            comboPromptTemplate.SelectedIndex = -1;
            textBoxTemplateName.ReadOnly = false;
            textBoxTemplateName.Clear();
            textBoxAutoTagPrompt.Clear();
            buttonDeleteTemplate.Enabled = false;
            buttonRestoreTemplate.Enabled = false;
            textBoxTemplateName.Focus();
        }

        private void buttonSaveTemplate_Click(object sender, EventArgs e)
        {
            SaveEditedTemplate();
        }

        private bool SaveEditedTemplate()
        {
            if (!ValidateTemplateEditor())
                return false;

            try
            {
                if (isCreatingTemplate)
                {
                    AiPromptTemplateSettings created = promptLibrary.AddCustom(textBoxTemplateName.Text, textBoxAutoTagPrompt.Text);
                    isCreatingTemplate = false;
                    BindPromptTemplates(created.Id);
                }
                else
                {
                    promptLibrary.Update(
                        promptLibrary.SelectedTemplateId,
                        textBoxTemplateName.Text,
                        textBoxAutoTagPrompt.Text);
                    BindPromptTemplates(promptLibrary.SelectedTemplateId);
                }
                return true;
            }
            catch (ArgumentException ex)
            {
                string message = ex.ParamName == "name"
                    ? I18n.GetText("AiServerSetPromptDuplicateName")
                    : ex.Message;
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private bool ValidateTemplateEditor()
        {
            if (string.IsNullOrWhiteSpace(textBoxTemplateName.Text))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetPromptNameRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(textBoxAutoTagPrompt.Text))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetPromptContentRequired"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void buttonDeleteTemplate_Click(object sender, EventArgs e)
        {
            if (promptLibrary.SelectedTemplate.IsBuiltIn)
                return;
            if (MessageBox.Show(this, I18n.GetText("AiServerSetPromptDeleteConfirm"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            promptLibrary.Delete(promptLibrary.SelectedTemplateId);
            isCreatingTemplate = false;
            BindPromptTemplates(promptLibrary.SelectedTemplateId);
        }

        private void buttonRestoreTemplate_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, I18n.GetText("AiServerSetPromptRestoreConfirm"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (promptLibrary.RestoreDefault(promptLibrary.SelectedTemplateId))
                LoadSelectedTemplate();
        }

        private void buttonExportCurrent_Click(object sender, EventArgs e)
        {
            if (!SaveEditedTemplate())
                return;
            ExportJson(promptLibrary.ExportCurrentJson(), MakeSafeFileName(promptLibrary.SelectedTemplate.Name) + ".json");
        }

        private void buttonExportAll_Click(object sender, EventArgs e)
        {
            if (!promptLibrary.Templates.Any(template => !template.IsBuiltIn))
            {
                MessageBox.Show(this, I18n.GetText("AiServerSetPromptNoCustom"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ExportJson(promptLibrary.ExportAllCustomJson(), "custom-prompt-templates.json");
        }

        private void ExportJson(string json, string fileName)
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = I18n.GetText("AiServerSetPromptJsonFilter"),
                FileName = fileName,
                AddExtension = true,
                DefaultExt = "json",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                File.WriteAllText(dialog.FileName, json);
                MessageBox.Show(this, I18n.GetText("AiServerSetPromptExported"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string MakeSafeFileName(string value)
        {
            string result = value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(result) ? "prompt-template" : result;
        }

        private async void buttonRefreshModels_Click(object sender, EventArgs e)
        {
            if (!TryGetEndpoint(out Uri endpoint))
                return;

            string selectedModel = comboOpenAiModel.SelectedItem as string;
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
                SetModelItems(client.Models, selectedModel);
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
            if (!SaveEditedTemplate())
                return;

            AiServerSetSettingsService.Save(
                textBoxOpenAiEndpoint.Text.Trim(),
                textBoxOpenAiApiKey.Text,
                (int)numericOpenAiTimeout.Value,
                comboOpenAiModel.SelectedItem as string,
                (int)numericLlmT2NlConcurrency.Value,
                promptLibrary.CreateSnapshot(),
                promptLibrary.SelectedTemplateId);
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class PromptTemplateListItem
        {
            public PromptTemplateListItem(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public override string ToString() => DisplayName;
        }
    }
}
