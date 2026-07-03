using System;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_LlmT2NlConfirm : Form
    {
        private readonly CaptionScanResult scan;
        private TextBox textBoxSummary;
        private CheckBox checkBoxReprocessExisting;
        private Button buttonStart;
        private Button buttonCancel;

        public Form_LlmT2NlConfirm(CaptionScanResult scan)
        {
            this.scan = scan ?? throw new ArgumentNullException(nameof(scan));
            InitializeComponent();
            ApplyLanguage();
        }

        public bool ReprocessExisting => checkBoxReprocessExisting.Checked;

        private void InitializeComponent()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 330);
            MinimumSize = new Size(540, 300);
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            textBoxSummary = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false
            };
            checkBoxReprocessExisting = new CheckBox
            {
                AutoSize = true,
                Margin = new Padding(3, 12, 3, 12)
            };
            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonCancel = new Button { AutoSize = true, MinimumSize = new Size(100, 30), DialogResult = DialogResult.Cancel };
            buttonStart = new Button { AutoSize = true, MinimumSize = new Size(100, 30), DialogResult = DialogResult.OK };
            buttons.Controls.Add(buttonCancel);
            buttons.Controls.Add(buttonStart);

            layout.Controls.Add(textBoxSummary, 0, 0);
            layout.Controls.Add(checkBoxReprocessExisting, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            AcceptButton = buttonStart;
            CancelButton = buttonCancel;
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("LlmT2NlConfirmTitle");
            textBoxSummary.Text = string.Format(
                I18n.GetText("LlmT2NlConfirmSummary"),
                scan.SourceRoot,
                scan.Total,
                scan.Existing,
                scan.Pending,
                scan.OutputRoot);
            checkBoxReprocessExisting.Text = I18n.GetText("LlmT2NlReprocessExisting");
            buttonStart.Text = I18n.GetText("LlmT2NlStart");
            buttonCancel.Text = I18n.GetText("BtnCancel");
        }
    }
}
