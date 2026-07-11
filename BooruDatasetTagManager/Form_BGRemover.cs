using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public partial class Form_BGRemover : Form
    {
        private readonly MainForm owner;
        private bool modelReady;
        private CancellationTokenSource downloadCancellation;
        private Color selectedFillColor = Color.White;

        public Form_BGRemover(MainForm owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            InitializeComponent();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            comboModels.Items.AddRange(RmbgBackgroundRemoverService.Models.ToArray());
            comboDownloadSource.Items.AddRange(Extensions.GetFriendlyEnumValues<HuggingFaceDownloadSource>());
            SwitchLanguage();
        }

        private void SwitchLanguage()
        {
            Text = I18n.GetText("UIBGRemovalForm");
            labelModel.Text = I18n.GetText("UIBGRemovalFormModelsLabel");
            labelSource.Text = I18n.GetText("TaggerDownloadSource");
            buttonPrepareModel.Text = I18n.GetText("UIBGRemovalFormPrepareBtn");
            groupBox1.Text = I18n.GetText("UIBGRemovalFormGroupText");
            label4.Text = I18n.GetText("UIBGRemovalFormModeLabel");
            radioButtonAllImages.Text = I18n.GetText("UICropImagesFormRadioAll");
            radioButtonOnlySelected.Text = I18n.GetText("UICropImagesFormRadioSelected");
            labelBg.Text = I18n.GetText("UIBGRemovalFormBgLabel");
            radioBgTransparent.Text = I18n.GetText("UIBGRemovalFormBgTransparent");
            radioBgColor.Text = I18n.GetText("UIBGRemovalFormBgColor");
            labelOutput.Text = I18n.GetText("UIBGRemovalFormOutputLabel");
            radioOutputReplace.Text = I18n.GetText("UIBGRemovalFormOutputReplace");
            radioOutputCopy.Text = I18n.GetText("UIBGRemovalFormOutputCopy");
            buttonRemovingTest.Text = I18n.GetText("UIBGRemovalFormRemovingTestBtn");
            button2.Text = I18n.GetText("BtnOK");
            button3.Text = I18n.GetText("BtnCancel");
        }

        private async void Form_BGRemover_Load(object sender, EventArgs e)
        {
            comboDownloadSource.SelectedIndex = 0;

            int lastIndex = RmbgBackgroundRemoverService.Models
                .ToList()
                .FindIndex(m => string.Equals(m.Id, Program.Settings.BackgroundRemoverModelId, StringComparison.OrdinalIgnoreCase));
            comboModels.SelectedIndex = lastIndex >= 0 ? lastIndex : 0;

            ApplyOptionsFromSettings();

            // Auto-load when the model is already downloaded so the dialog is
            // immediately usable — no need to click "download and load" again.
            RmbgModelDefinition selected = GetSelectedModelDefinition();
            if (selected != null && Program.BackgroundRemover.IsModelReady(selected))
                await PrepareModelAsync(selected);
            else
                SetStatus(I18n.GetText("UIBGRemovalFormDownloadHint"));
        }

        private void ApplyOptionsFromSettings()
        {
            radioBgColor.Checked = Program.Settings.BackgroundRemoverFillBackground;
            radioBgTransparent.Checked = !Program.Settings.BackgroundRemoverFillBackground;
            selectedFillColor = Color.FromArgb(Program.Settings.BackgroundRemoverColorArgb);
            buttonBgColor.BackColor = Color.FromArgb(255, selectedFillColor);
            buttonBgColor.Enabled = radioBgColor.Checked;
            radioOutputReplace.Checked = Program.Settings.BackgroundRemoverReplaceOriginal;
            radioOutputCopy.Checked = !Program.Settings.BackgroundRemoverReplaceOriginal;
        }

        private RmbgModelDefinition GetSelectedModelDefinition()
        {
            return comboModels.SelectedItem as RmbgModelDefinition;
        }

        private void radioBgColor_CheckedChanged(object sender, EventArgs e)
        {
            buttonBgColor.Enabled = radioBgColor.Checked;
        }

        private void buttonBgColor_Click(object sender, EventArgs e)
        {
            using var dialog = new ColorDialog { Color = selectedFillColor, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                selectedFillColor = dialog.Color;
                buttonBgColor.BackColor = Color.FromArgb(255, selectedFillColor);
            }
        }

        private async void buttonPrepareModel_Click(object sender, EventArgs e)
        {
            RmbgModelDefinition model = GetSelectedModelDefinition();
            if (model == null)
                return;
            await PrepareModelAsync(model);
        }

        private async Task PrepareModelAsync(RmbgModelDefinition model)
        {
            buttonPrepareModel.Enabled = false;
            comboModels.Enabled = false;
            comboDownloadSource.Enabled = false;
            modelReady = false;
            groupBox1.Enabled = false;
            downloadCancellation = new CancellationTokenSource();
            try
            {
                var source = comboDownloadSource.SelectedIndex == (int)HuggingFaceDownloadSource.HfMirror
                    ? HuggingFaceDownloadSource.HfMirror
                    : HuggingFaceDownloadSource.HuggingFace;

                var progress = new Progress<(string file, long downloaded, long? total)>(report =>
                {
                    if (report.total is > 0)
                    {
                        int percent = (int)Math.Clamp(report.downloaded * 100 / report.total.Value, 0, 100);
                        progressBarDownload.Value = percent;
                        SetStatus($"{Path.GetFileName(report.file)}  {percent}%");
                    }
                });

                // Two attempts: if the integrity check fails on load, the service
                // deletes the bad file, so the second pass re-downloads it once.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (!Program.BackgroundRemover.IsModelReady(model))
                        {
                            SetStatus(I18n.GetText("TaggerDownloadModel"));
                            await Program.BackgroundRemover.DownloadModelAsync(model, source, progress, downloadCancellation.Token);
                        }

                        SetStatus(I18n.GetText("UIBGRemovalFormLoadingModel"));
                        await Task.Run(() => Program.BackgroundRemover.LoadModel(model));
                        break;
                    }
                    catch (ModelCorruptedException) when (attempt == 0)
                    {
                        progressBarDownload.Value = 0;
                        SetStatus(I18n.GetText("TaggerModelCorruptCleared"));
                    }
                }

                Program.Settings.BackgroundRemoverModelId = model.Id;
                Program.Settings.SaveSettings();
                progressBarDownload.Value = 100;
                modelReady = true;
                groupBox1.Enabled = true;
                SetStatus(I18n.GetText("UIBGRemovalFormModelReady"));
            }
            catch (OperationCanceledException)
            {
                SetStatus(I18n.GetText("TaggerCancelled"));
            }
            catch (Exception ex)
            {
                progressBarDownload.Value = 0;
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus(ex.Message);
            }
            finally
            {
                downloadCancellation?.Dispose();
                downloadCancellation = null;
                buttonPrepareModel.Enabled = true;
                comboModels.Enabled = true;
                comboDownloadSource.Enabled = true;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            SaveOptionsToSettings();
            DialogResult = DialogResult.OK;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void SaveOptionsToSettings()
        {
            Program.Settings.BackgroundRemoverFillBackground = radioBgColor.Checked;
            Program.Settings.BackgroundRemoverColorArgb = selectedFillColor.ToArgb();
            Program.Settings.BackgroundRemoverReplaceOriginal = radioOutputReplace.Checked;
            Program.Settings.SaveSettings();
        }

        private async void button4_Click(object sender, EventArgs e)
        {
            if (!modelReady)
                return;

            string imagePath = owner.GetSelectedDatasetImagePaths().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                MessageBox.Show(I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            buttonRemovingTest.Enabled = false;
            try
            {
                byte[] res = await RemoveBackgroundAsync(imagePath, null);
                if (res == null)
                    return;

                Image img;
                using (var ms = new MemoryStream(res))
                using (var decoded = Image.FromStream(ms))
                {
                    // GDI+ requires the stream to stay open for the image's lifetime;
                    // clone to a stream-independent bitmap before the stream closes.
                    img = new Bitmap(decoded);
                }

                // Modal: a non-modal Show() on a `using` form flashes and vanishes
                // as the form is disposed on scope exit.
                using Form_preview preview = new Form_preview();
                preview.ShowDialog(img);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonRemovingTest.Enabled = true;
            }
        }

        /// <summary>
        /// Non-null once a model has been downloaded and loaded; the batch caller
        /// (Form1.RemoveBackgrounds) uses this as its "ready" gate.
        /// </summary>
        public string GetSelectedModel()
        {
            return modelReady ? (GetSelectedModelDefinition()?.Id) : null;
        }

        /// <summary>True = overwrite the source image; false = save a copy alongside it.</summary>
        public bool ReplaceOriginal => radioOutputReplace.Checked;

        /// <summary>
        /// Runs RMBG locally (no external service) and returns a PNG. Background
        /// is transparent or filled with the chosen solid color per the dialog.
        /// </summary>
        public async Task<byte[]> RemoveBackgroundAsync(string imgFilePath, string model)
        {
            Color? fill = radioBgColor.Checked ? selectedFillColor : (Color?)null;
            return await Task.Run(() => Program.BackgroundRemover.RemoveBackground(imgFilePath, fill));
        }

        private void SetStatus(string text)
        {
            toolStripStatusLabel1.Text = text;
        }
    }
}
