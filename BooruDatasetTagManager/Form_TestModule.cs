using System;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_TestModule : Form
    {
        private readonly MainForm owner;
        private bool suppressSave;
        private GroupBox groupTagFix;
        private CheckBox checkTagFixCharacterVariants;
        private CheckBox checkTagFixFoldRareChildren;
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
            ClientSize = new Size(580, 180);
            MinimumSize = new Size(500, 160);
            ShowInTaskbar = false;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            groupTagFix = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
            TableLayoutPanel tagFixLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2
            };
            tagFixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tagFixLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            checkTagFixCharacterVariants = new CheckBox
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 8, 12, 3)
            };
            checkTagFixCharacterVariants.CheckedChanged += (_, _) =>
            {
                UpdateTagFixChildEnabled();
                SaveSettingsImmediate();
            };
            checkTagFixFoldRareChildren = new CheckBox
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 8, 12, 3)
            };
            checkTagFixFoldRareChildren.CheckedChanged += (_, _) => SaveSettingsImmediate();
            buttonTagFix = new Button { AutoSize = true, MinimumSize = new Size(160, 30), Anchor = AnchorStyles.Right };
            buttonTagFix.Click += (_, _) =>
            {
                SaveSettingsImmediate();
                owner.RunTagConsistencyFix();
            };
            tagFixLayout.Controls.Add(checkTagFixCharacterVariants, 0, 0);
            tagFixLayout.Controls.Add(buttonTagFix, 1, 0);
            tagFixLayout.SetRowSpan(buttonTagFix, 2);
            tagFixLayout.Controls.Add(checkTagFixFoldRareChildren, 0, 1);
            groupTagFix.Controls.Add(tagFixLayout);
            root.Controls.Add(groupTagFix, 0, 0);
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("MenuTestModule");
            groupTagFix.Text = I18n.GetText("TestTagFixGroup");
            checkTagFixCharacterVariants.Text = I18n.GetText("TestTagFixCharacterVariants");
            checkTagFixFoldRareChildren.Text = I18n.GetText("TestTagFixChildThreshold");
            buttonTagFix.Text = I18n.GetText("TestTagFixRun");
        }

        private void LoadSettings()
        {
            suppressSave = true;
            checkTagFixCharacterVariants.Checked = Program.Settings.TagFixCharacterVariants;
            checkTagFixFoldRareChildren.Checked = Program.Settings.TagFixFoldRareChildren;
            UpdateTagFixChildEnabled();
            suppressSave = false;
        }

        private void UpdateTagFixChildEnabled()
        {
            bool on = checkTagFixCharacterVariants.Checked;
            checkTagFixFoldRareChildren.Enabled = on;
            bool previousSuppress = suppressSave;
            suppressSave = true;
            checkTagFixFoldRareChildren.Checked = on && Program.Settings.TagFixFoldRareChildren;
            suppressSave = previousSuppress;
        }

        private void SaveSettingsImmediate()
        {
            if (suppressSave)
                return;

            Program.Settings.TagFixCharacterVariants = checkTagFixCharacterVariants.Checked;
            if (checkTagFixCharacterVariants.Checked)
                Program.Settings.TagFixFoldRareChildren = checkTagFixFoldRareChildren.Checked;
            Program.Settings.SaveSettings();
            owner.SetStatus(I18n.GetText("StatusSettingsSaved"));
        }
    }
}
