using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Danbooru wiki popup. Loads the tag's wiki text, then — for non-English
    /// UI languages — translates it automatically through the app's
    /// translation pipeline (the button becomes an original ↔ translation
    /// toggle, translation cached). The wiki's curated "post #" examples are
    /// fetched as thumbnails (click opens the post in the browser). Colors
    /// follow the active color scheme instead of a hardcoded light palette.
    /// </summary>
    public sealed class Form_TagWikiPopup : Form
    {
        private const int MaxExampleImages = 4;

        private readonly string tag;
        private readonly DanbooruWikiClient client;
        private readonly string wikiUrl;
        private readonly TableLayoutPanel root;
        private readonly Label titleLabel;
        private readonly Label metaLabel;
        private readonly Label examplesLabel;
        private readonly FlowLayoutPanel examplesPanel;
        private readonly TextBox bodyTextBox;
        private readonly Button translateButton;
        private readonly Button openButton;
        private readonly Button closeButton;
        private readonly ToolTip toolTip = new ToolTip();
        private DanbooruWikiPage page;
        private string originalBody;
        private string translatedBody;
        private bool showingTranslation;

        public Form_TagWikiPopup(string tag)
        {
            this.tag = tag;
            client = new DanbooruWikiClient();
            wikiUrl = DanbooruWikiClient.GetWikiUrl(tag);

            Text = I18n.GetText("TagWikiWindowTitle") + " - " + tag;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 580);
            MinimumSize = new Size(540, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);

            root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(14, 10, 14, 10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = tag
            };

            metaLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Text = I18n.GetText("TagWikiLoading")
            };

            examplesLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
                Text = I18n.GetText("TagWikiExamples"),
                Visible = false
            };

            examplesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0, 0, 0, 6)
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
            openButton.Click += OpenButton_Click;

            translateButton = CreateButton(I18n.GetText("TagWikiTranslate"));
            translateButton.Enabled = false;
            translateButton.Click += async (_, _) => await ToggleTranslationAsync();

            buttonsPanel.Controls.Add(closeButton);
            buttonsPanel.Controls.Add(openButton);
            buttonsPanel.Controls.Add(translateButton);

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(metaLabel, 0, 1);
            root.Controls.Add(examplesLabel, 0, 2);
            root.Controls.Add(examplesPanel, 0, 3);
            root.Controls.Add(bodyTextBox, 0, 4);
            root.Controls.Add(buttonsPanel, 0, 5);
            Controls.Add(root);

            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            metaLabel.ForeColor = Blend(ForeColor, BackColor, 0.68f);
            examplesLabel.ForeColor = Blend(ForeColor, BackColor, 0.68f);

            Shown += async (_, _) => await LoadWikiAsync();
            FormClosed += (_, _) =>
            {
                foreach (Control control in examplesPanel.Controls)
                {
                    if (control is PictureBox box)
                    {
                        Image image = box.Image;
                        box.Image = null;
                        image?.Dispose();
                    }
                }
                toolTip.Dispose();
                client.Dispose();
            };
        }

        private static Button CreateButton(string text)
        {
            return new Button
            {
                AutoSize = true,
                MinimumSize = new Size(130, 32),
                Margin = new Padding(8, 0, 0, 0),
                Text = text,
                UseVisualStyleBackColor = true
            };
        }

        private static Color Blend(Color over, Color under, float amount)
        {
            float rest = 1f - amount;
            return Color.FromArgb(
                (int)(over.R * amount + under.R * rest),
                (int)(over.G * amount + under.G * rest),
                (int)(over.B * amount + under.B * rest));
        }

        private async Task LoadWikiAsync()
        {
            page = await client.GetWikiPageAsync(tag);
            if (IsDisposed)
                return;
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
            // Only the intro section — the Examples / tag-list sections are
            // covered by the thumbnails and the browser link.
            originalBody = DanbooruDTextFormatter.ToPlainText(
                DanbooruWikiClient.TrimToIntroSection(page.Body));
            bodyTextBox.Text = originalBody;
            translateButton.Enabled = !string.IsNullOrWhiteSpace(originalBody) && Program.TransManager != null;

            // Thumbnails and the automatic translation load in parallel.
            _ = LoadExampleImagesAsync();
            if (translateButton.Enabled && UiLanguageWantsTranslation())
                await ShowTranslationAsync();
        }

        private static bool UiLanguageWantsTranslation()
        {
            string language = Program.Settings?.Language ?? "en-US";
            return !language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ToggleTranslationAsync()
        {
            if (showingTranslation)
            {
                bodyTextBox.Text = originalBody;
                showingTranslation = false;
                UpdateTranslateButtonText();
                return;
            }
            await ShowTranslationAsync();
        }

        private async Task ShowTranslationAsync()
        {
            if (Program.TransManager == null || string.IsNullOrWhiteSpace(originalBody))
                return;

            translateButton.Enabled = false;
            translateButton.Text = I18n.GetText("TagWikiTranslating");
            try
            {
                translatedBody ??= await TranslateBodyAsync();
                if (IsDisposed)
                    return;
                bodyTextBox.Text = translatedBody;
                showingTranslation = true;
            }
            catch
            {
                translatedBody = null;
                metaLabel.Text = I18n.GetText("TagWikiTranslateFailed");
            }
            finally
            {
                if (!IsDisposed)
                {
                    UpdateTranslateButtonText();
                    translateButton.Enabled = true;
                }
            }
        }

        private void UpdateTranslateButtonText()
        {
            translateButton.Text = showingTranslation
                ? I18n.GetText("TagWikiShowOriginal")
                : I18n.GetText("TagWikiTranslate");
        }

        private Task<string> TranslateBodyAsync()
        {
            string source = originalBody;
            // The translator chain can block on slow providers; keep the whole
            // sequential loop off the UI thread so the window stays responsive.
            return Task.Run(async () =>
            {
                string[] lines = source
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
                    translated[i] = await Program.TransManager.TranslateAsync(lines[i]).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(translated[i]))
                        translated[i] = lines[i];
                }
                return string.Join(Environment.NewLine, translated);
            });
        }

        private async Task LoadExampleImagesAsync()
        {
            foreach (int postId in DanbooruWikiClient.ExtractExamplePostIds(page.Body, MaxExampleImages))
            {
                DanbooruPostPreview preview = await client.GetPostPreviewAsync(postId);
                if (IsDisposed)
                    return;
                if (preview == null)
                    continue;
                byte[] bytes = await client.DownloadBytesAsync(preview.PreviewUrl);
                if (IsDisposed)
                    return;
                if (bytes == null)
                    continue;
                Image thumbnail;
                try
                {
                    // Copy out of the stream: GDI+ otherwise keeps the stream
                    // as the image's backing store.
                    using var stream = new MemoryStream(bytes);
                    using var decoded = Image.FromStream(stream);
                    thumbnail = new Bitmap(decoded);
                }
                catch (Exception)
                {
                    continue;
                }
                AddExampleThumbnail(thumbnail, preview);
            }
        }

        private void AddExampleThumbnail(Image image, DanbooruPostPreview preview)
        {
            if (examplesPanel.Controls.Count == 0)
            {
                examplesLabel.Visible = true;
                root.RowStyles[3] = new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(170));
            }
            var box = new PictureBox
            {
                Width = LogicalToDeviceUnits(140),
                Height = LogicalToDeviceUnits(150),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = image,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0),
                BackColor = Blend(ForeColor, BackColor, 0.06f)
            };
            toolTip.SetToolTip(box, "post #" + preview.Id);
            box.Click += (_, _) =>
                Process.Start(new ProcessStartInfo(preview.PostUrl) { UseShellExecute = true });
            examplesPanel.Controls.Add(box);
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo(page?.Url ?? wikiUrl) { UseShellExecute = true });
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ModifierKeys == Keys.None && keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }
}
