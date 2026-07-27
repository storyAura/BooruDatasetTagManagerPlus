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
        private bool suppressSave;
        private GroupBox groupCharacterTagAudit;
        private Button buttonCharacterTagAudit;
        private GroupBox groupTagFix;
        private Label labelTagFixThreshold;
        private NumericUpDown numericTagFixThreshold;
        private Button buttonTagFix;

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

            groupCharacterTagAudit = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
            buttonCharacterTagAudit = new Button { AutoSize = true, MinimumSize = new Size(220, 32) };
            buttonCharacterTagAudit.Click += (_, _) =>
            {
                using Form_CharacterTagAuditWizard form = new Form_CharacterTagAuditWizard(owner);
                form.ShowDialog(this);
            };
            groupCharacterTagAudit.Controls.Add(buttonCharacterTagAudit);
            root.Controls.Add(groupCharacterTagAudit, 0, 1);

            groupTagFix = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
            TableLayoutPanel tagFixLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1
            };
            tagFixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tagFixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            tagFixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            labelTagFixThreshold = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 12, 3) };
            // 0 disables the fold-into-parent rule entirely.
            numericTagFixThreshold = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 99999, Value = 30, Margin = new Padding(3, 5, 12, 3) };
            numericTagFixThreshold.ValueChanged += (_, _) => SaveSettingsImmediate();
            buttonTagFix = new Button { AutoSize = true, MinimumSize = new Size(160, 30), Anchor = AnchorStyles.Right };
            buttonTagFix.Click += (_, _) =>
            {
                SaveSettingsImmediate();
                owner.RunTagConsistencyFix();
            };
            tagFixLayout.Controls.Add(labelTagFixThreshold, 0, 0);
            tagFixLayout.Controls.Add(numericTagFixThreshold, 1, 0);
            tagFixLayout.Controls.Add(buttonTagFix, 2, 0);
            groupTagFix.Controls.Add(tagFixLayout);
            root.Controls.Add(groupTagFix, 0, 2);
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("MenuTestModule");
            groupQuickReplace.Text = I18n.GetText("TestQuickReplace");
            labelThreshold.Text = I18n.GetText("TestQuickReplaceThreshold");
            buttonQuickReplace.Text = I18n.GetText("TestQuickReplaceRun");
            groupCharacterTagAudit.Text = I18n.GetText("CharacterTagAuditGroup");
            buttonCharacterTagAudit.Text = I18n.GetText("CharacterTagAuditOpen");
            groupTagFix.Text = I18n.GetText("TestTagFixGroup");
            labelTagFixThreshold.Text = I18n.GetText("TestTagFixChildThreshold");
            buttonTagFix.Text = I18n.GetText("TestTagFixRun");
        }

        private void LoadSettings()
        {
            suppressSave = true;
            numericThreshold.Value = Math.Clamp(Program.Settings.QuickReplaceThreshold, (int)numericThreshold.Minimum, (int)numericThreshold.Maximum);
            numericTagFixThreshold.Value = Math.Clamp(Program.Settings.TagFixChildThreshold, (int)numericTagFixThreshold.Minimum, (int)numericTagFixThreshold.Maximum);
            suppressSave = false;
        }

        private void SaveSettingsImmediate()
        {
            if (suppressSave)
                return;

            Program.Settings.QuickReplaceThreshold = (int)numericThreshold.Value;
            Program.Settings.TagFixChildThreshold = (int)numericTagFixThreshold.Value;
            Program.Settings.SaveSettings();
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
        }
    }
}
