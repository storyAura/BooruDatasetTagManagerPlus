using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_TagWikiPopup : Form
    {
        private readonly string tag;
        private readonly DanbooruWikiClient client;
        private readonly string wikiUrl;
        private readonly Label titleLabel;
        private readonly Label metaLabel;
        private readonly TextBox bodyTextBox;
        private readonly Button translateButton;
        private readonly Button openButton;
        private readonly Button closeButton;
        private DanbooruWikiPage page;
        private string originalBody;

        public Form_TagWikiPopup(string tag)
        {
            this.tag = tag;
            client = new DanbooruWikiClient();
            wikiUrl = DanbooruWikiClient.GetWikiUrl(tag);

            Text = I18n.GetText("TagWikiWindowTitle") + " - " + tag;
            Width = 680;
            Height = 540;
            MinimumSize = new Size(520, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12),
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 35, 45),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = tag
            };

            metaLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(73, 85, 99),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = I18n.GetText("TagWikiLoading")
            };

            bodyTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(24, 28, 35),
                Margin = new Padding(0, 4, 0, 8)
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            closeButton = CreateButton(I18n.GetText("TagWikiClose"));
            closeButton.Click += (_, _) => Close();

            openButton = CreateButton(I18n.GetText("TagWikiOpenInBrowser"));
            openButton.Enabled = true;
            openButton.Click += OpenButton_Click;

            translateButton = CreateButton(I18n.GetText("TagWikiTranslate"));
            translateButton.Enabled = false;
            translateButton.Click += async (_, _) => await TranslateWikiAsync();

            buttonsPanel.Controls.Add(closeButton);
            buttonsPanel.Controls.Add(openButton);
            buttonsPanel.Controls.Add(translateButton);

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(metaLabel, 0, 1);
            root.Controls.Add(bodyTextBox, 0, 2);
            root.Controls.Add(buttonsPanel, 0, 3);
            Controls.Add(root);

            Shown += async (_, _) => await LoadWikiAsync();
            FormClosed += (_, _) => client.Dispose();
        }

        private static Button CreateButton(string text)
        {
            return new Button
            {
                AutoSize = false,
                Width = 138,
                Height = 32,
                Margin = new Padding(8, 0, 0, 0),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        private async Task LoadWikiAsync()
        {
            page = await client.GetWikiPageAsync(tag);
            if (page == null)
            {
                titleLabel.Text = DanbooruWikiClient.NormalizeTag(tag);
                metaLabel.Text = I18n.GetText("TagWikiNotFound");
                bodyTextBox.Text = string.Empty;
                translateButton.Enabled = false;
                return;
            }

            titleLabel.Text = page.Title;
            string otherNames = page.OtherNames != null && page.OtherNames.Length > 0
                ? string.Join(", ", page.OtherNames)
                : "-";
            string updatedAt = page.UpdatedAt.HasValue
                ? page.UpdatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "-";
            metaLabel.Text = I18n.GetText("TagWikiOtherNames") + ": " + otherNames
                + Environment.NewLine
                + I18n.GetText("TagWikiUpdatedAt") + ": " + updatedAt;
            originalBody = DanbooruDTextFormatter.ToPlainText(page.Body);
            bodyTextBox.Text = originalBody;
            translateButton.Enabled = !string.IsNullOrWhiteSpace(originalBody) && Program.TransManager != null;
        }

        private async Task TranslateWikiAsync()
        {
            if (Program.TransManager == null || string.IsNullOrWhiteSpace(originalBody))
                return;

            string beforeTranslate = bodyTextBox.Text;
            translateButton.Enabled = false;
            translateButton.Text = I18n.GetText("TagWikiTranslating");

            try
            {
                string[] lines = originalBody
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n');
                string[] translated = new string[lines.Length];
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        translated[i] = string.Empty;
                        continue;
                    }

                    translated[i] = await Program.TransManager.TranslateAsync(lines[i]);
                    if (string.IsNullOrWhiteSpace(translated[i]))
                        translated[i] = lines[i];
                }

                bodyTextBox.Text = string.Join(Environment.NewLine, translated);
            }
            catch
            {
                bodyTextBox.Text = beforeTranslate;
                metaLabel.Text = I18n.GetText("TagWikiTranslateFailed");
            }
            finally
            {
                translateButton.Text = I18n.GetText("TagWikiTranslate");
                translateButton.Enabled = true;
            }
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo(page?.Url ?? wikiUrl) { UseShellExecute = true });
        }
    }
}
