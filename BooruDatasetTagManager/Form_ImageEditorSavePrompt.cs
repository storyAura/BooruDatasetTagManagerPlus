using System;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// "Overwrite or save as new file?" prompt shown by the image editor when
    /// the configured save mode is <see cref="ImageEditorSaveMode.Ask"/>.
    /// </summary>
    public sealed class Form_ImageEditorSavePrompt : Form
    {
        /// <summary>Overwrite or NewFile once confirmed; null on cancel.</summary>
        public ImageEditorSaveMode? Choice { get; private set; }

        public Form_ImageEditorSavePrompt()
        {
            Text = I18n.GetText("ImageEditorSavePromptTitle");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(16)
            };
            var message = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                Text = I18n.GetText("ImageEditorSavePromptText"),
                Margin = new Padding(3, 3, 3, 6)
            };
            var hint = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                ForeColor = SystemColors.GrayText,
                Text = I18n.GetText("ImageEditorSavePromptHint"),
                Margin = new Padding(3, 0, 3, 10)
            };
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Anchor = AnchorStyles.Right
            };
            Button overwrite = CreateChoiceButton(I18n.GetText("ImageEditorSaveOverwrite"), ImageEditorSaveMode.Overwrite);
            Button newFile = CreateChoiceButton(I18n.GetText("ImageEditorSaveNewFile"), ImageEditorSaveMode.NewFile);
            var cancel = new Button
            {
                Text = I18n.GetText("BtnCancel"),
                AutoSize = true,
                MinimumSize = new Size(90, 30),
                DialogResult = DialogResult.Cancel
            };
            buttons.Controls.AddRange(new Control[] { overwrite, newFile, cancel });
            layout.Controls.Add(message, 0, 0);
            layout.Controls.Add(hint, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            AcceptButton = newFile;
            CancelButton = cancel;
        }

        private Button CreateChoiceButton(string text, ImageEditorSaveMode mode)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(120, 30)
            };
            button.Click += (_, _) =>
            {
                Choice = mode;
                DialogResult = DialogResult.OK;
                Close();
            };
            return button;
        }
    }
}
