using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_TestModule : Form
    {
        private readonly MainForm owner;
        private GroupBox groupQuickReplace;
        private Label labelThreshold;
        private NumericUpDown numericThreshold;
        private Button buttonQuickReplace;
        private GroupBox groupTranslation;
        private CheckBox checkBoxUseCsv;
        private bool suppressSave;
        private GroupBox groupCharacterTagAudit;
        private Button buttonCharacterTagAudit;

        public Form_TestModule(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            LoadSettings();
            ApplyLanguage();
        }

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Text = "Test";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(580, 330);
            MinimumSize = new Size(500, 220);
            ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            groupQuickReplace = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            root.Controls.Add(groupQuickReplace, 0, 0);
            TableLayoutPanel quickReplaceLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1
            };
            quickReplaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            quickReplaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            quickReplaceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            labelThreshold = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) };
            numericThreshold = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 99999, Value = 30, Margin = new Padding(3, 5, 12, 3) };
            numericThreshold.ValueChanged += (_, _) => SaveSettingsImmediate();
            buttonQuickReplace = new Button { AutoSize = true, MinimumSize = new Size(160, 30), Anchor = AnchorStyles.Right };
            buttonQuickReplace.Click += (_, _) =>
            {
                SaveSettingsImmediate();
                owner.TryQuickReplaceSelectedTag((int)numericThreshold.Value);
            };
            quickReplaceLayout.Controls.Add(labelThreshold, 0, 0);
            quickReplaceLayout.Controls.Add(numericThreshold, 1, 0);
            quickReplaceLayout.Controls.Add(buttonQuickReplace, 2, 0);
            groupQuickReplace.Controls.Add(quickReplaceLayout);

            groupTranslation = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
            root.Controls.Add(groupTranslation, 0, 1);
            checkBoxUseCsv = new CheckBox { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(530, 0) };
            checkBoxUseCsv.CheckedChanged += (_, _) => SaveSettingsImmediate();
            groupTranslation.Controls.Add(checkBoxUseCsv);

            groupCharacterTagAudit = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
            buttonCharacterTagAudit = new Button { AutoSize = true, MinimumSize = new Size(220, 32) };
            buttonCharacterTagAudit.Click += (_, _) =>
            {
                using Form_CharacterTagAuditWizard form = new Form_CharacterTagAuditWizard(owner);
                form.ShowDialog(this);
            };
            groupCharacterTagAudit.Controls.Add(buttonCharacterTagAudit);
            root.Controls.Add(groupCharacterTagAudit, 0, 2);
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("MenuTestModule");
            groupQuickReplace.Text = I18n.GetText("TestQuickReplace");
            labelThreshold.Text = I18n.GetText("TestQuickReplaceThreshold");
            buttonQuickReplace.Text = I18n.GetText("TestQuickReplaceRun");
            groupTranslation.Text = I18n.GetText("TestTranslation");
            checkBoxUseCsv.Text = I18n.GetText("TestUseDanbooruCsvBeforeTranslation");
            groupCharacterTagAudit.Text = I18n.GetText("CharacterTagAuditGroup");
            buttonCharacterTagAudit.Text = I18n.GetText("CharacterTagAuditOpen");
        }

        private void LoadSettings()
        {
            suppressSave = true;
            numericThreshold.Value = Math.Clamp(Program.Settings.QuickReplaceThreshold, (int)numericThreshold.Minimum, (int)numericThreshold.Maximum);
            checkBoxUseCsv.Checked = Program.Settings.UseDanbooruZhCsvBeforeTranslation;
            suppressSave = false;
        }

        private void SaveSettingsImmediate()
        {
            if (suppressSave)
                return;

            Program.Settings.UseDanbooruZhCsvBeforeTranslation = checkBoxUseCsv.Checked;
            Program.Settings.QuickReplaceThreshold = (int)numericThreshold.Value;
            Program.Settings.SaveSettings();
            owner.ReloadTranslationManagerForCurrentSettings();
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
        }
    }
}
