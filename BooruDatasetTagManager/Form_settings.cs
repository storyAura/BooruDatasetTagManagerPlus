using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BooruDatasetTagManager
{
    public partial class Form_settings : Form
    {
        public Form_settings()
        {
            InitializeComponent();
            AddCsvTranslationCheckbox();
            AddImageEditorSaveModeOption();
            AddAllTagsDoubleClickOption();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            Program.ColorManager.SchemeChanded += ColorManager_SchemeChanded;
            Program.Settings.TranslationLanguage ??= "en-US";
            textBoxOpenApiEndpoint.AutoCompleteSource = AutoCompleteSource.CustomSource;
            SettingFrame.Tabs.Remove(tabInterrogator);
        }
        private FontSettings gridFontSettings = null;
        private FontSettings autocompleteFontSettings = null;
        private System.Windows.Forms.CheckBox checkBoxUseDanbooruCsv;
        private System.Windows.Forms.Label labelImageEditorSaveMode;
        private System.Windows.Forms.ComboBox comboBoxImageEditorSaveMode;
        private System.Windows.Forms.Label labelAllTagsDoubleClick;
        private System.Windows.Forms.ComboBox comboBoxAllTagsDoubleClick;

        // Double-click quick action of the All Tags grid; lives on the General
        // tab below the auto-sort checkbox. Positions derive from the already
        // scaled designer controls (runtime controls skip WinForms auto-scaling).
        private void AddAllTagsDoubleClickOption()
        {
            int rowTop = AutoSortCheckBox.Bottom + comboAutocompMode.Height / 2;
            labelAllTagsDoubleClick = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(LabelAutocompMode.Left, rowTop + 4),
                Name = "labelAllTagsDoubleClick"
            };
            comboBoxAllTagsDoubleClick = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(comboAutocompMode.Left, rowTop),
                Width = comboAutocompMode.Width,
                Name = "comboBoxAllTagsDoubleClick"
            };
            tabGeneral.Controls.Add(labelAllTagsDoubleClick);
            tabGeneral.Controls.Add(comboBoxAllTagsDoubleClick);
        }

        // Default save behavior of the image editor; lives on the UI tab below
        // the image-cache toggle (same label/control columns as the other rows).
        // Positions are derived from the already-scaled designer controls:
        // controls added after InitializeComponent are excluded from WinForms
        // auto-scaling, so hard-coded pixels would overlap rows on high-DPI.
        private void AddImageEditorSaveModeOption()
        {
            int rowTop = checkBoxCacheImages.Bottom + comboBoxPreviewType.Height / 2;
            labelImageEditorSaveMode = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Location = new System.Drawing.Point(labelPreviewLocation.Left, rowTop + 4),
                Name = "labelImageEditorSaveMode"
            };
            comboBoxImageEditorSaveMode = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(comboBoxPreviewType.Left, rowTop),
                Width = comboBoxPreviewType.Width,
                Name = "comboBoxImageEditorSaveMode"
            };
            FillImageEditorSaveModeItems();
            tabUI.Controls.Add(labelImageEditorSaveMode);
            tabUI.Controls.Add(comboBoxImageEditorSaveMode);
        }

        private void FillImageEditorSaveModeItems()
        {
            int selected = comboBoxImageEditorSaveMode.SelectedIndex;
            comboBoxImageEditorSaveMode.Items.Clear();
            // Item order mirrors the ImageEditorSaveMode enum values.
            comboBoxImageEditorSaveMode.Items.Add(I18n.GetText("ImageEditorSaveModeAsk"));
            comboBoxImageEditorSaveMode.Items.Add(I18n.GetText("ImageEditorSaveModeOverwrite"));
            comboBoxImageEditorSaveMode.Items.Add(I18n.GetText("ImageEditorSaveModeNewFile"));
            comboBoxImageEditorSaveMode.SelectedIndex = selected >= 0 && selected < comboBoxImageEditorSaveMode.Items.Count
                ? selected
                : 0;
        }

        // The CSV-before-online-translation toggle used to live in the Test module;
        // it now belongs to the Translations settings tab (and defaults on).
        private void AddCsvTranslationCheckbox()
        {
            checkBoxUseDanbooruCsv = new System.Windows.Forms.CheckBox
            {
                AutoSize = true,
                Dock = System.Windows.Forms.DockStyle.Fill,
                Margin = new System.Windows.Forms.Padding(4, 3, 4, 3),
                Name = "checkBoxUseDanbooruCsv"
            };
            translationTableLayoutPanel.RowCount = 5;
            translationTableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            translationTableLayoutPanel.Controls.Add(checkBoxUseDanbooruCsv, 0, 4);
            translationTableLayoutPanel.SetColumnSpan(checkBoxUseDanbooruCsv, 2);
        }

        private void Form_settings_Load(object sender, EventArgs e)
        {
            comboBox1.DataSource = Program.Settings.AvaibleLanguages;
            comboBox1.DisplayMember = "Name";
            comboBox1.ValueMember = "Code";
            comboBox1.SelectedValue = Program.Settings.TranslationLanguage;
            checkBoxLoadOnlyManual.Checked = Program.Settings.OnlyManualTransInAutocomplete;
            comboBox2.Items.AddRange(Extensions.GetFriendlyEnumValues<TranslationService>());
            comboBox2.SelectedIndex = Extensions.GetEnumIndexFromValue<TranslationService>(Program.Settings.TransService.ToString());
            numericUpDownTranslationTimeout.Value = Math.Clamp(
                (decimal)(Program.Settings.TranslationTimeoutSeconds <= 0 ? 5 : Program.Settings.TranslationTimeoutSeconds),
                numericUpDownTranslationTimeout.Minimum, numericUpDownTranslationTimeout.Maximum);
            checkBoxUseDanbooruCsv.Checked = Program.Settings.UseDanbooruZhCsvBeforeTranslation;
            comboAutocompMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteMode>());
            comboAutocompMode.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteMode>(Program.Settings.AutocompleteMode.ToString());
            comboAutocompSort.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteSort>());
            comboAutocompSort.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteSort>(Program.Settings.AutocompleteSort.ToString());
            comboBoxColorScheme.Items.AddRange(Program.ColorManager.Items.Select(a => a.ToString()).ToArray());
            comboBoxColorScheme.SelectedItem = Program.Settings.ColorScheme;
            textBox1.Text = Program.Settings.SeparatorOnLoad;
            textBox2.Text = Program.Settings.SeparatorOnSave;
            textBox3.Text = Program.Settings.DefaultTagsFileExtension;
            textBox4.Text = Program.Settings.CaptionFileExtensions;
            numericUpDown1.Value = Math.Clamp((decimal)Program.Settings.PreviewSize, numericUpDown1.Minimum, numericUpDown1.Maximum);
            numericUpDown2.Value = Math.Clamp((decimal)Program.Settings.ShowAutocompleteAfterCharCount, numericUpDown2.Minimum, numericUpDown2.Maximum);
            CheckAskChange.Checked = Program.Settings.AskSaveChanges;
            checkBoxFixOnLoad.Checked = Program.Settings.FixTagsOnSaveLoad;
            AutoSortCheckBox.Checked = Program.Settings.AutoSort;
            comboBoxAllTagsDoubleClick.Items.AddRange(Extensions.GetFriendlyEnumValues<AllTagsQuickAction>());
            comboBoxAllTagsDoubleClick.SelectedIndex = Extensions.GetEnumIndexFromValue<AllTagsQuickAction>(Program.Settings.AllTagsDoubleClickAction.ToString());
            //UI
            checkBoxCacheImages.Checked = Program.Settings.CacheOpenImages;
            comboBoxImageEditorSaveMode.SelectedIndex = Math.Clamp(
                (int)Program.Settings.ImageEditorSaveMode, 0, comboBoxImageEditorSaveMode.Items.Count - 1);
            numericUpDown3.Value = Math.Clamp((decimal)Program.Settings.GridViewRowHeight, numericUpDown3.Minimum, numericUpDown3.Maximum);
            label11.Text = Program.Settings.GridViewFont.ToString();
            gridFontSettings = Program.Settings.GridViewFont;
            label14.Text = Program.Settings.AutocompleteFont.ToString();
            autocompleteFontSettings = Program.Settings.AutocompleteFont;
            comboBoxLanguage.Items.AddRange(I18n.GetLanguages().ToArray());
            comboBoxLanguage.SelectedItem = Program.Settings.Language;
            comboBoxPreviewType.Items.AddRange(Extensions.GetFriendlyEnumValues<ImagePreviewType>());
            comboBoxPreviewType.SelectedIndex = Extensions.GetEnumIndexFromValue<ImagePreviewType>(Program.Settings.PreviewType.ToString());
            //hotkeys
            foreach (var item in Program.Settings.Hotkeys.Items)
            {
                dataGridViewHotkeys.Rows.Add(item.Id, item.Text, item.GetHotkeyString());
            }
            SwitchLanguage();

        }

        private void ColorManager_SchemeChanded(object sender, EventArgs e)
        {
            if (Program.ColorManager.SelectedScheme != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            MessageBox.Show(I18n.GetText("TipSaveSettings"));
            Program.Settings.PreviewSize = (int)numericUpDown1.Value;
            Program.Settings.ShowAutocompleteAfterCharCount = (int)numericUpDown2.Value;
            Program.Settings.TranslationLanguage = (string)comboBox1.SelectedValue;
            //Program.Settings.TransService = (TranslationService)Enum.Parse(typeof(TranslationService), comboBox2.SelectedItem.ToString(), true);
            Program.Settings.TransService = Extensions.GetEnumItemFromFriendlyText<TranslationService>(comboBox2.SelectedItem.ToString());
            Program.Settings.TranslationProviderOrder = new[] { Program.Settings.TransService }
                .Concat(AppSettings.GetDefaultTranslationProviderOrder())
                .Distinct()
                .ToList();
            Program.Settings.TranslationTimeoutSeconds = (int)numericUpDownTranslationTimeout.Value;
            Program.Settings.OnlyManualTransInAutocomplete = checkBoxLoadOnlyManual.Checked;
            Program.Settings.UseDanbooruZhCsvBeforeTranslation = checkBoxUseDanbooruCsv.Checked;
            Program.Settings.AutocompleteMode = Extensions.GetEnumItemFromFriendlyText<AutocompleteMode>(comboAutocompMode.SelectedItem.ToString());
            Program.Settings.AutocompleteSort = Extensions.GetEnumItemFromFriendlyText<AutocompleteSort>(comboAutocompSort.SelectedItem.ToString());
            Program.Settings.FixTagsOnSaveLoad = checkBoxFixOnLoad.Checked;
            Program.Settings.SeparatorOnLoad = textBox1.Text;
            Program.Settings.SeparatorOnSave = textBox2.Text;
            Program.Settings.DefaultTagsFileExtension = textBox3.Text;
            Program.Settings.CaptionFileExtensions = textBox4.Text;
            Program.Settings.AskSaveChanges = CheckAskChange.Checked;
            Program.Settings.AutoSort = AutoSortCheckBox.Checked;
            if (comboBoxAllTagsDoubleClick.SelectedItem is string quickActionText)
                Program.Settings.AllTagsDoubleClickAction = Extensions.GetEnumItemFromFriendlyText<AllTagsQuickAction>(quickActionText);
            //UI
            Program.Settings.CacheOpenImages = checkBoxCacheImages.Checked;
            Program.Settings.ImageEditorSaveMode = (ImageEditorSaveMode)Math.Max(0, comboBoxImageEditorSaveMode.SelectedIndex);
            Program.Settings.GridViewRowHeight = (int)numericUpDown3.Value;
            Program.Settings.GridViewFont = gridFontSettings;
            Program.Settings.AutocompleteFont = autocompleteFontSettings;
            // Persist only real selections: writing null here used to break the
            // next startup (culture/color-scheme lookup).
            if (comboBoxLanguage.SelectedItem is string selectedLanguage)
                Program.Settings.Language = selectedLanguage;
            if (comboBoxColorScheme.SelectedItem is string selectedScheme)
                Program.Settings.ColorScheme = selectedScheme;
            Program.ColorManager.SelectScheme(Program.Settings.ColorScheme);
            Program.Settings.PreviewType = Extensions.GetEnumItemFromFriendlyText<ImagePreviewType>(comboBoxPreviewType.SelectedItem.ToString());
            //hotkeys
            if (tempHotkeys.Count > 0)
            {
                foreach (var item in tempHotkeys)
                {
                    Program.Settings.Hotkeys[item.Key] = item.Value;
                }
            }
            Program.Settings.SaveSettings();
            ReloadTranslationManager();
            DialogResult = DialogResult.OK;
        }

        private void ReloadTranslationManager()
        {
            if (Program.Settings == null)
                return;

            string translationsDir = Path.Combine(Application.StartupPath, "Translations");
            if (!Directory.Exists(translationsDir))
                Directory.CreateDirectory(translationsDir);

            TranslationManager oldManager = Program.TransManager;
            Program.TransManager = Program.CreateTranslationManager(translationsDir);
            Program.TransManager.LoadTranslations();
            oldManager?.Dispose();
            Program.TagsList?.LoadTranslation(Program.TransManager);
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private async void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            BtnCheckUpdate.Enabled = false;
            try
            {
                // Source checkout: update the working copy via git pull.
                string repoRoot = UpdateChecker.FindSourceCheckoutRoot();
                if (repoRoot != null)
                {
                    if (MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateSourceConfirm"), repoRoot),
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    BtnCheckUpdate.Text = I18n.GetText("TipUpdateInProgress");
                    (bool pullOk, string pullOutput) = await UpdateChecker.PullSourceAsync(repoRoot);
                    MessageBox.Show(this,
                        string.Format(I18n.GetText(pullOk ? "TipUpdateSourceDone" : "TipUpdateSourceFailed"), pullOutput),
                        Text, MessageBoxButtons.OK, pullOk ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    return;
                }

                // Release install: query GitHub Releases and pull the zip asset.
                BtnCheckUpdate.Text = I18n.GetText("TipUpdateInProgress");
                UpdateCheckResult check = await UpdateChecker.CheckLatestReleaseAsync(Application.ProductVersion);
                if (!check.Success)
                {
                    MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateCheckFailed"), check.ErrorMessage),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!check.HasNewer)
                {
                    MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateLatest"), "v" + Application.ProductVersion),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (Form_UpdateInfo updateInfo = new Form_UpdateInfo())
                {
                    // The WinForms TextBox needs CRLF line breaks; localized text
                    // and GitHub release bodies may carry bare LF.
                    string infoText = string.Format(I18n.GetText("TipUpdateNewVersion"), check.LatestTag, check.ReleaseNotes)
                        .Replace("\r\n", "\n").Replace("\n", "\r\n");
                    updateInfo.SetText(infoText);
                    if (updateInfo.ShowDialog(this) != DialogResult.OK)
                        return;
                }

                if (string.IsNullOrEmpty(check.ZipAssetUrl))
                {
                    // No downloadable zip on the release: fall back to the page.
                    UpdateChecker.OpenInBrowser(check.ReleasePageUrl);
                    return;
                }

                var progress = new Progress<int>(pct => BtnCheckUpdate.Text = $"{pct}%");
                string downloadedPath;
                try
                {
                    downloadedPath = await UpdateChecker.DownloadReleaseAssetAsync(check.ZipAssetUrl, check.ZipAssetName, progress);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateDownloadFailed"), ex.Message),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdateChecker.OpenInBrowser(check.ReleasePageUrl);
                    return;
                }
                MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateDownloaded"), downloadedPath),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateChecker.ShowInExplorer(downloadedPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(I18n.GetText("TipUpdateCheckFailed"), ex.Message),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                BtnCheckUpdate.Text = I18n.GetText("SettingBtnCheckUpdate");
                BtnCheckUpdate.Enabled = true;
            }
        }

        private void BtnGridviewFontChange_Click(object sender, EventArgs e)
        {
            FontDialog dialog = new FontDialog();
            dialog.Font = gridFontSettings.GetFont();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                gridFontSettings = FontSettings.Create(dialog.Font);
                label11.Text = gridFontSettings.ToString();
            }
            dialog.Dispose();
        }

        private void BtnAutocompFontChange_Click(object sender, EventArgs e)
        {
            FontDialog dialog = new FontDialog();
            dialog.Font = autocompleteFontSettings.GetFont();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                autocompleteFontSettings = FontSettings.Create(dialog.Font);
                label14.Text = autocompleteFontSettings.ToString();
            }
            dialog.Dispose();
        }

        private void SwitchLanguage()
        {
            this.Text = I18n.GetText("MenuLabelSettings");
            SettingFrame.Tabs[0].Text = I18n.GetText("SettingTabGeneral");
            SettingFrame.Tabs[1].Text = I18n.GetText("SettingTabUI");
            SettingFrame.Tabs[2].Text = I18n.GetText("SettingTabTranslations");
            LabelPreviewImageSize.Text = I18n.GetText("SettingPreviewImageSize");
            LabelAutocompMode.Text = I18n.GetText("SettingAutocompMode");
            LabelAutocompFont.Text = I18n.GetText("SettingAutocompFont");
            LabelAutocompSort.Text = I18n.GetText("SettingAutocompSort");
            LabelAutocompAfter.Text = I18n.GetText("SettingAutocompPrefix");
            LabelChars.Text = I18n.GetText("SettingAutocompChars");
            LabelLanguage.Text = I18n.GetText("SettingUILanguage");
            LabelSeparatorLoad.Text = I18n.GetText("SettingSeperatorLoad");
            LabelSeparatorSave.Text = I18n.GetText("SettingSeperatorSave");
            LabelTagFont.Text = I18n.GetText("SettingUITagFont");
            LabelTagHeight.Text = I18n.GetText("SettingUIRowHeight");
            CheckAskChange.Text = I18n.GetText("SettingPromptToSave");
            checkBoxFixOnLoad.Text = I18n.GetText("SettingFixTagLoad");
            AutoSortCheckBox.Text = I18n.GetText("SettingAutoSortCheck");
            BtnSave.Text = I18n.GetText("SettingBtnSave");
            BtnCancel.Text = I18n.GetText("SettingBtnCancel");
            BtnCheckUpdate.Text = I18n.GetText("SettingBtnCheckUpdate");
            BtnGridviewFontChange.Text = I18n.GetText("SettingBtnChange");
            BtnAutocompFontChange.Text = I18n.GetText("SettingBtnChange");
            labelDelExt.Text = I18n.GetText("SettingDefExt");
            labelCaptionFileExt.Text = I18n.GetText("SettingCaptionExt");
            labelColorScheme.Text = I18n.GetText("SettingColorScheme");
            labelPreviewLocation.Text = I18n.GetText("SettingPreviewLocation");
            tabHotkeys.Text = I18n.GetText("SettingHotkeysTab");
            labelHotkeysHelp.Text = I18n.GetText("SettingHotkeysHelpText");
            dataGridViewHotkeys.Columns["Command"].HeaderText = I18n.GetText("SettingHotkeysCommandColumn");
            dataGridViewHotkeys.Columns["Hotkey"].HeaderText = I18n.GetText("SettingHotkeysHotkeyColumn");
            labelTransLang.Text = I18n.GetText("SettingTranslationLang");
            labelTranslService.Text = I18n.GetText("SettingTranslationSrv");
            labelTranslationTimeout.Text = I18n.GetText("SettingTranslationTimeout");
            checkBoxLoadOnlyManual.Text = I18n.GetText("SettingLoadOnlyManualAutocomplete");
            checkBoxUseDanbooruCsv.Text = I18n.GetText("SettingUseDanbooruCsvBeforeTranslation");
            checkBoxCacheImages.Text = I18n.GetText("SettingsCheckBoxCacheImages");
            labelImageEditorSaveMode.Text = I18n.GetText("SettingsImageEditorSaveMode");
            FillImageEditorSaveModeItems();
            LabelApApiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            labelOpenAiEndpoint.Text = I18n.GetText("SettingsInterrogatorAddress");
            checkBoxCustomPrompt.Text = I18n.GetText("SettingsCheckBoxCustomPrompt");
            labelOpenAiApiKey.Text = I18n.GetText("SettingsOpenAiApiKey");
            labelOpenAiTimeout.Text = I18n.GetText("SettingsOpenAiRequestTimeout");

            labelAllTagsDoubleClick.Text = I18n.GetText("SettingAllTagsDoubleClick");
            comboBoxAllTagsDoubleClick.Items.Clear();
            comboBoxAllTagsDoubleClick.Items.AddRange(Extensions.GetFriendlyEnumValues<AllTagsQuickAction>());
            comboBoxAllTagsDoubleClick.SelectedIndex = Extensions.GetEnumIndexFromValue<AllTagsQuickAction>(Program.Settings.AllTagsDoubleClickAction.ToString());

            comboAutocompMode.Items.Clear();
            comboAutocompSort.Items.Clear();
            comboBox2.Items.Clear();

            comboBox2.Items.AddRange(Extensions.GetFriendlyEnumValues<TranslationService>());
            comboBox2.SelectedIndex = Extensions.GetEnumIndexFromValue<TranslationService>(Program.Settings.TransService.ToString());
            comboAutocompMode.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteMode>());
            comboAutocompMode.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteMode>(Program.Settings.AutocompleteMode.ToString());
            comboAutocompSort.Items.AddRange(Extensions.GetFriendlyEnumValues<AutocompleteSort>());
            comboAutocompSort.SelectedIndex = Extensions.GetEnumIndexFromValue<AutocompleteSort>(Program.Settings.AutocompleteSort.ToString());

            Program.Settings.Hotkeys.ChangeLanguage();
        }
        bool isControlKeyPressed = false;
        Dictionary<string, HotkeyItem> tempHotkeys = new Dictionary<string, HotkeyItem>();
        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (dataGridViewHotkeys.SelectedRows.Count == 0)
                return;
            if (e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.Menu)
            {
                string id = (string)dataGridViewHotkeys.SelectedRows[0].Cells["CmdId"].Value;
                var hkItem = (HotkeyItem)Program.Settings.Hotkeys[id].Clone();
                hkItem.KeyData = e.KeyCode;
                hkItem.IsCtrl = e.Control;
                hkItem.IsAlt = e.Alt;
                hkItem.IsShift = e.Shift;
                tempHotkeys[id] = hkItem;
                dataGridViewHotkeys.SelectedRows[0].Cells["Hotkey"].Value = hkItem.GetHotkeyString();
            }
            e.SuppressKeyPress = true;
        }

        private void checkBoxCustomPrompt_CheckedChanged(object sender, EventArgs e)
        {
            textBoxCustomPrompt.Enabled = checkBoxCustomPrompt.Checked;
        }
    }
}
