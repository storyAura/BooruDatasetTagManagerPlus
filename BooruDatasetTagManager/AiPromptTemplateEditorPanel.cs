using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class AiPromptTemplateEditorPanel : UserControl
    {
        private AiPromptTemplateLibrary promptLibrary;
        private bool isCreatingTemplate;
        private bool isBindingTemplates;

        private ComboBox comboPromptTemplate;
        private TextBox textBoxTemplateName;
        private TextBox textBoxAutoTagPrompt;
        private Button buttonNewTemplate;
        private Button buttonSaveTemplate;
        private Button buttonDeleteTemplate;
        private Button buttonRestoreTemplate;
        private Button buttonExportCurrent;
        private Button buttonExportAll;

        public AiPromptTemplateEditorPanel()
        {
            promptLibrary = AiPromptTemplateLibrary.Create(
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            InitializeComponent();
            ApplyLanguage();
            BindPromptTemplates(promptLibrary.SelectedTemplateId);
        }

        public string SelectedTemplateSystemPrompt => textBoxAutoTagPrompt.Text;

        public IEnumerable<AiPromptTemplateSettings> CreateSnapshot() => promptLibrary.CreateSnapshot();

        public string SelectedTemplateId => promptLibrary.SelectedTemplateId;

        private void InitializeComponent()
        {
            Dock = DockStyle.Fill;

            TableLayoutPanel editor = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
                ColumnCount = 1,
                RowCount = 3
            };
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(editor);

            TableLayoutPanel selection = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            comboPromptTemplate = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 6, 6) };
            comboPromptTemplate.SelectedIndexChanged += comboPromptTemplate_SelectedIndexChanged;
            textBoxTemplateName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 6) };
            selection.Controls.Add(comboPromptTemplate, 0, 0);
            selection.Controls.Add(textBoxTemplateName, 1, 0);
            editor.Controls.Add(selection, 0, 0);

            textBoxAutoTagPrompt = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true
            };
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
        }

        private static Button CreateActionButton(EventHandler handler)
        {
            Button button = new Button { AutoSize = true, MinimumSize = new Size(105, 30), Margin = new Padding(0, 0, 6, 0) };
            button.Click += handler;
            return button;
        }

        public void ApplyLanguage()
        {
            buttonNewTemplate.Text = I18n.GetText("AiServerSetPromptNew");
            buttonSaveTemplate.Text = I18n.GetText("AiServerSetPromptSave");
            buttonDeleteTemplate.Text = I18n.GetText("AiServerSetPromptDelete");
            buttonRestoreTemplate.Text = I18n.GetText("AiServerSetPromptRestore");
            buttonExportCurrent.Text = I18n.GetText("AiServerSetPromptExportCurrent");
            buttonExportAll.Text = I18n.GetText("AiServerSetPromptExportAll");
        }

        public void ReloadFromSettings()
        {
            promptLibrary = AiPromptTemplateLibrary.Create(
                Program.Settings.AiServerSetPromptTemplates,
                Program.Settings.AiServerSetPromptTemplateId,
                Program.Settings.AiServerSetPromptTemplate);
            BindPromptTemplates(promptLibrary.SelectedTemplateId);
        }

        public bool SaveEditedTemplate()
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
                MessageBox.Show(FindForm(), message, FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
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

        private bool ValidateTemplateEditor()
        {
            if (string.IsNullOrWhiteSpace(textBoxTemplateName.Text))
            {
                MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptNameRequired"), FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrWhiteSpace(textBoxAutoTagPrompt.Text))
            {
                MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptContentRequired"), FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void buttonDeleteTemplate_Click(object sender, EventArgs e)
        {
            if (promptLibrary.SelectedTemplate.IsBuiltIn)
                return;
            if (MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptDeleteConfirm"), FindForm()?.Text ?? Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            promptLibrary.Delete(promptLibrary.SelectedTemplateId);
            isCreatingTemplate = false;
            BindPromptTemplates(promptLibrary.SelectedTemplateId);
        }

        private void buttonRestoreTemplate_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptRestoreConfirm"), FindForm()?.Text ?? Text,
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
                MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptNoCustom"), FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;
            try
            {
                File.WriteAllText(dialog.FileName, json);
                MessageBox.Show(FindForm(), I18n.GetText("AiServerSetPromptExported"), FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), ex.Message, FindForm()?.Text ?? Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string MakeSafeFileName(string value)
        {
            string result = value;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(result) ? "prompt-template" : result;
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
