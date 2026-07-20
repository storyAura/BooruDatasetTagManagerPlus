using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Shown before downloading a gated HuggingFace model (e.g. cl_tagger_v2,
    /// whose license forbids redistribution/bundling): summarizes the license
    /// terms, links to the model page where the user must request access, and
    /// collects the user's HuggingFace access token (persisted DPAPI-encrypted).
    /// </summary>
    public sealed class Form_GatedModelNotice : Form
    {
        private readonly TextBox tokenBox = new TextBox();
        private readonly ClTaggerModelDefinition model;

        /// <summary>Trimmed token entered by the user (may be empty).</summary>
        public string AccessToken => tokenBox.Text.Trim();

        public Form_GatedModelNotice(ClTaggerModelDefinition model)
        {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
        }

        /// <summary>
        /// Shows the notice; on OK persists the token into settings and
        /// returns true.
        /// </summary>
        public static bool ConfirmDownload(IWin32Window owner, ClTaggerModelDefinition model, out string token)
        {
            using var dialog = new Form_GatedModelNotice(model);
            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                token = null;
                return false;
            }

            token = dialog.AccessToken;
            if (!string.Equals(Program.Settings.HuggingFaceToken, token, StringComparison.Ordinal))
            {
                Program.Settings.HuggingFaceToken = token;
                Program.Settings.SaveSettings();
            }
            return true;
        }

        private void InitializeComponent()
        {
            Text = I18n.GetText("TaggerGatedNoticeTitle");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            string modelsFolder = HuggingFaceModelDownloader.GetLocalDirectory(model.Repo);
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(16)
            };

            var message = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Text = string.Format(I18n.GetText("TaggerGatedNoticeText"), modelsFolder),
                Margin = new Padding(3, 3, 3, 10)
            };

            var openPage = new LinkLabel
            {
                AutoSize = true,
                Text = I18n.GetText("TaggerGatedOpenPage") + " — " + model.RepoUrl,
                Margin = new Padding(3, 0, 3, 10)
            };
            openPage.LinkClicked += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(model.RepoUrl) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            var tokenLabel = new Label
            {
                AutoSize = true,
                Text = I18n.GetText("TaggerGatedTokenLabel"),
                Margin = new Padding(3, 0, 3, 2)
            };
            tokenBox.Width = 520;
            tokenBox.UseSystemPasswordChar = true;
            tokenBox.Text = Program.Settings.HuggingFaceToken ?? string.Empty;
            tokenBox.Margin = new Padding(3, 0, 3, 12);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var cancelButton = new Button
            {
                Text = I18n.GetText("BtnCancel"),
                DialogResult = DialogResult.Cancel,
                AutoSize = true
            };
            var continueButton = new Button
            {
                Text = I18n.GetText("TaggerGatedContinueDownload"),
                DialogResult = DialogResult.OK,
                AutoSize = true
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(continueButton);

            layout.Controls.Add(message);
            layout.Controls.Add(openPage);
            layout.Controls.Add(tokenLabel);
            layout.Controls.Add(tokenBox);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = continueButton;
            CancelButton = cancelButton;

            Program.ColorManager?.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager?.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
        }
    }
}
