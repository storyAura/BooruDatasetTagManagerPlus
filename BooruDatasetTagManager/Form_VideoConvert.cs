using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_VideoConvert : Form
    {
        private readonly MainForm owner;
        private readonly VideoProcessingService videoService;
        private readonly string initialVideoPath;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceAllVideos = new RadioButton();
        private readonly ComboBox comboTargetFormat = new ComboBox();
        private readonly ComboBox comboCodec = new ComboBox();
        private readonly CheckBox checkReplaceOriginal = new CheckBox();
        private readonly Label labelOutputPreview = new Label();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonRun = new Button();
        private readonly Button buttonCancelJob = new Button();
        private readonly Button buttonClose = new Button();

        private CancellationTokenSource jobCancellation;
        // Set when the user closes the window mid-conversion: the close is deferred
        // until ffmpeg is killed and the job unwinds, because disposing the form
        // while the output-reader thread still reports progress crashes the process.
        private bool closeAfterJob;

        public Form_VideoConvert(MainForm owner, string initialVideoPath = null)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.initialVideoPath = initialVideoPath;
            videoService = VideoProcessingService.CreateDefault();
            InitializeComponent();
            ApplyLanguage();
            ApplyButtonStyles();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
        }

        private void InitializeComponent()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            Text = "Video Convert";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(720, 520);
            ClientSize = new Size(800, 560);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(72F)));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(108F)));

            var panelSource = BuildSourceGroup();
            var panelConvert = BuildConvertGroup();
            var panelBottom = BuildBottomPanel();

            mainLayout.Controls.Add(panelSource, 0, 0);
            mainLayout.Controls.Add(panelConvert, 0, 1);
            mainLayout.Controls.Add(panelBottom, 0, 2);
            Controls.Add(mainLayout);

            radioSourceSelected.CheckedChanged += (_, _) => UpdateOutputPreview();
            radioSourceAllVideos.CheckedChanged += (_, _) => UpdateOutputPreview();

            comboTargetFormat.SelectedIndexChanged += (_, _) => UpdateOutputPreview();
            checkReplaceOriginal.CheckedChanged += (_, _) => UpdateOutputPreview();

            Shown += (_, _) =>
            {
                radioSourceSelected.Checked = true;

                if (!string.IsNullOrWhiteSpace(initialVideoPath) && VideoProcessingService.IsVideoFile(initialVideoPath))
                {
                    if (owner.GetSelectedDatasetVideoPaths().Any(v => string.Equals(v, initialVideoPath, StringComparison.OrdinalIgnoreCase)))
                        radioSourceSelected.Checked = true;
                }

                UpdateOutputPreview();
                UpdateHintLabelWidths();
            };

            Resize += (_, _) => UpdateHintLabelWidths();
            FormClosing += (_, e) =>
            {
                if (IsJobRunning())
                {
                    e.Cancel = true;
                    closeAfterJob = true;
                    jobCancellation?.Cancel();
                }
            };
            FormClosed += (_, _) => toolTip.Dispose();
        }

        private GroupBox BuildSourceGroup()
        {
            var panelSource = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupSource",
                Padding = new Padding(10, 4, 10, 6)
            };

            var panelRadios = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            radioSourceSelected.AutoSize = true;
            radioSourceSelected.Checked = true;
            radioSourceSelected.Margin = new Padding(0, 0, 16, 0);

            radioSourceAllVideos.AutoSize = true;

            panelRadios.Controls.Add(radioSourceSelected);
            panelRadios.Controls.Add(radioSourceAllVideos);
            panelSource.Controls.Add(panelRadios);
            return panelSource;
        }

        private GroupBox BuildConvertGroup()
        {
            var panelConvert = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupConvert",
                Padding = new Padding(10, 4, 10, 6)
            };

            comboTargetFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            comboTargetFormat.Dock = DockStyle.Fill;
            comboTargetFormat.Margin = new Padding(0, 0, 8, 0);
            comboTargetFormat.Items.AddRange(VideoProcessingService.GetSupportedOutputFormats().Cast<object>().ToArray());
            comboTargetFormat.SelectedIndex = 0;

            comboCodec.DropDownStyle = ComboBoxStyle.DropDownList;
            comboCodec.Dock = DockStyle.Fill;
            comboCodec.Items.AddRange(new object[] { VideoConvertCodec.H264, VideoConvertCodec.H265, VideoConvertCodec.Copy });
            comboCodec.SelectedIndex = 0;

            checkReplaceOriginal.AutoSize = true;
            checkReplaceOriginal.Dock = DockStyle.Fill;
            checkReplaceOriginal.Margin = new Padding(0, 6, 0, 0);

            labelOutputPreview.Dock = DockStyle.Fill;
            labelOutputPreview.AutoEllipsis = true;
            labelOutputPreview.TextAlign = ContentAlignment.MiddleLeft;
            labelOutputPreview.Margin = new Padding(0, 6, 0, 0);

            var labelConvertHint = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Name = "labelConvertHint",
                AutoEllipsis = false,
                Margin = new Padding(0, 8, 0, 0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(34F)));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(28F)));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            layout.Controls.Add(comboTargetFormat, 0, 0);
            layout.Controls.Add(comboCodec, 1, 0);
            layout.Controls.Add(checkReplaceOriginal, 0, 1);
            layout.SetColumnSpan(checkReplaceOriginal, 2);
            layout.Controls.Add(labelOutputPreview, 0, 2);
            layout.SetColumnSpan(labelOutputPreview, 2);
            layout.Controls.Add(labelConvertHint, 0, 3);
            layout.SetColumnSpan(labelConvertHint, 2);

            panelConvert.Controls.Add(layout);
            return panelConvert;
        }

        private TableLayoutPanel BuildBottomPanel()
        {
            var panelBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(22F)));
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(24F)));
            panelBottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            progressBar.Dock = DockStyle.Fill;
            progressBar.Style = ProgressBarStyle.Continuous;

            labelStatus.Dock = DockStyle.Fill;
            labelStatus.AutoEllipsis = true;
            labelStatus.TextAlign = ContentAlignment.MiddleLeft;

            var panelActionButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 6, 0, 0)
            };

            buttonRun.Click += async (_, _) => await RunJobAsync().ConfigureAwait(true);
            buttonCancelJob.Enabled = false;
            buttonCancelJob.Click += (_, _) => jobCancellation?.Cancel();
            buttonClose.Click += (_, _) => Close();
            // Esc closes (deferred while a job runs, via FormClosing).
            CancelButton = buttonClose;

            panelActionButtons.Controls.Add(buttonRun);
            panelActionButtons.Controls.Add(buttonCancelJob);
            panelActionButtons.Controls.Add(buttonClose);

            panelBottom.Controls.Add(progressBar, 0, 0);
            panelBottom.Controls.Add(labelStatus, 0, 1);
            panelBottom.Controls.Add(panelActionButtons, 0, 2);
            return panelBottom;
        }

        private int ScaleLayout(float value)
        {
            return Math.Max(1, (int)Math.Round(value * (DeviceDpi / 96f)));
        }

        public void ApplyLanguage()
        {
            Text = I18n.GetText("VideoConvertForm");
            if (Controls.Find("groupSource", true).FirstOrDefault() is GroupBox groupSource)
                groupSource.Text = I18n.GetText("VideoToolsSourceGroup");
            if (Controls.Find("groupConvert", true).FirstOrDefault() is GroupBox groupConvert)
                groupConvert.Text = I18n.GetText("VideoToolsTabConvert");

            radioSourceSelected.Text = I18n.GetText("VideoToolsSourceSelected");
            radioSourceAllVideos.Text = I18n.GetText("VideoToolsSourceAllVideos");
            checkReplaceOriginal.Text = I18n.GetText("VideoConvertReplaceOriginal");
            buttonRun.Text = I18n.GetText("VideoToolsRun");
            buttonCancelJob.Text = I18n.GetText("VideoToolsCancel");
            buttonClose.Text = I18n.GetText("VideoToolsClose");

            if (Controls.Find("labelConvertHint", true).FirstOrDefault() is Label convertHint
                && Controls.Find("groupConvert", true).FirstOrDefault() is GroupBox convertHost)
            {
                UpdateHintLabel(convertHint, convertHost, I18n.GetText("VideoToolsConvertHint"));
            }

            ApplyButtonStyles();
            UpdateOutputPreview();
        }

        private void ApplyButtonStyles()
        {
            ApplyPrimaryButton(buttonRun);
            ApplySecondaryButton(buttonCancelJob);
            ApplySecondaryButton(buttonClose);
        }

        private static void ApplyPrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(108, 36);
            button.Padding = new Padding(10, 4, 10, 4);
            button.Margin = new Padding(0, 0, 8, 6);
            button.Font = new Font(button.Font, FontStyle.Bold);
        }

        private static void ApplySecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = true;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(96, 34);
            button.Padding = new Padding(8, 4, 8, 4);
            button.Margin = new Padding(0, 0, 8, 6);
        }

        private void UpdateHintLabel(Label label, Control host, string text)
        {
            label.AutoSize = true;
            label.Text = text;
            int width = Math.Max(200, host.ClientSize.Width - 24);
            label.MaximumSize = new Size(width, 0);
        }

        private void UpdateHintLabelWidths()
        {
            if (Controls.Find("labelConvertHint", true).FirstOrDefault() is Label convertHint
                && Controls.Find("groupConvert", true).FirstOrDefault() is GroupBox convertHost)
            {
                UpdateHintLabel(convertHint, convertHost, I18n.GetText("VideoToolsConvertHint"));
            }
        }

        private List<string> ResolveInputVideos()
        {
            if (Program.DataManager == null)
                return new List<string>();

            if (radioSourceSelected.Checked)
                return owner.GetSelectedDatasetVideoPaths();

            return Program.DataManager.DataSet.Values
                .Select(item => item.ImageFilePath)
                .Where(VideoProcessingService.IsVideoFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetPreviewInputPath()
        {
            return ResolveInputVideos().FirstOrDefault();
        }

        private void UpdateOutputPreview()
        {
            string input = GetPreviewInputPath();
            string format = comboTargetFormat.SelectedItem?.ToString() ?? "mp4";
            bool replaceOriginal = checkReplaceOriginal.Checked;

            if (string.IsNullOrWhiteSpace(input))
            {
                labelOutputPreview.Text = I18n.GetText("VideoConvertOutputPreviewEmpty");
                toolTip.SetToolTip(labelOutputPreview, labelOutputPreview.Text);
                return;
            }

            string outputPath = videoService.GetConvertOutputPath(input, format, replaceOriginal);
            labelOutputPreview.Text = string.Format(I18n.GetText("VideoConvertOutputPreview"), outputPath);
            toolTip.SetToolTip(labelOutputPreview, outputPath);
        }

        private void UpdateStatus(string line)
        {
            labelStatus.Text = line;
            toolTip.SetToolTip(labelStatus, line);
        }

        private async Task RunJobAsync()
        {
            List<string> inputs;
            try
            {
                FfmpegLocator.FromSettings().EnsureAvailable();
                inputs = ResolveInputVideos();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (inputs.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("VideoToolsNoVideos"), I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (checkReplaceOriginal.Checked)
            {
                if (MessageBox.Show(this, I18n.GetText("VideoConvertReplaceConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            SetJobRunning(true);
            jobCancellation = new CancellationTokenSource();
            var progress = VideoProgressReporter.CreateForControl(this, UpdateStatus);

            int completed = 0;
            var errors = new List<string>();
            string format = comboTargetFormat.SelectedItem?.ToString() ?? "mp4";
            bool replaceOriginal = checkReplaceOriginal.Checked;
            var codec = comboCodec.SelectedItem is VideoConvertCodec selectedCodec ? selectedCodec : VideoConvertCodec.H264;
            // Output ffmpeg is currently writing; deleted if cancel/crash
            // interrupts the conversion so no half-written file survives.
            string pendingOutput = null;

            try
            {
                foreach (string input in inputs)
                {
                    jobCancellation.Token.ThrowIfCancellationRequested();
                    UpdateStatus(Path.GetFileName(input));
                    progressBar.Value = 0;

                    string finalOutput = videoService.GetConvertOutputPath(input, format, replaceOriginal);
                    if (replaceOriginal
                        && !string.Equals(Path.GetFullPath(finalOutput), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase)
                        && File.Exists(finalOutput))
                    {
                        // clip.mkv → mp4 while an unrelated clip.mp4 already exists:
                        // refuse this item up front instead of overwriting a sibling.
                        errors.Add(input + ": " + string.Format(I18n.GetText("VideoConvertTargetExists"), Path.GetFileName(finalOutput)));
                        completed++;
                        progressBar.Value = Math.Min(100, (int)Math.Round(completed * 100.0 / inputs.Count));
                        continue;
                    }
                    // When replacing the original, ffmpeg writes to a temp file; the
                    // source is only swapped out after a fully successful conversion.
                    string ffmpegOutput = replaceOriginal
                        ? videoService.GetConvertTempOutputPath(input, format)
                        : finalOutput;
                    pendingOutput = ffmpegOutput;
                    var result = await videoService.ConvertAsync(input, ffmpegOutput, codec, progress, jobCancellation.Token).ConfigureAwait(true);
                    pendingOutput = null;
                    if (!result.Success)
                    {
                        errors.Add(input + ": " + result.ErrorMessage);
                        // Junk in both modes: the temp for replace, or a partial
                        // file already carrying the final name (the pre-check
                        // guaranteed that name was free before we started).
                        try { File.Delete(ffmpegOutput); } catch { }
                    }
                    else if (replaceOriginal)
                    {
                        try
                        {
                            videoService.FinalizeReplaceOriginal(input, ffmpegOutput, finalOutput);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(input + ": " + ex.Message);
                        }
                    }

                    completed++;
                    progressBar.Value = Math.Min(100, (int)Math.Round(completed * 100.0 / inputs.Count));
                }

                if (errors.Count > 0)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors), I18n.GetText("VideoToolsCompletedWithErrors"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, I18n.GetText("VideoToolsCompleted"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                if (pendingOutput != null)
                {
                    try { File.Delete(pendingOutput); } catch { }
                }
                UpdateStatus(I18n.GetText("VideoToolsCancelled"));
            }
            catch (Exception ex)
            {
                if (pendingOutput != null)
                {
                    try { File.Delete(pendingOutput); } catch { }
                }
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                jobCancellation?.Dispose();
                jobCancellation = null;
                SetJobRunning(false);
                if (closeAfterJob)
                {
                    closeAfterJob = false;
                    Close();
                }
            }
        }

        private bool IsJobRunning()
        {
            return jobCancellation != null;
        }

        private void SetJobRunning(bool running)
        {
            buttonRun.Enabled = !running;
            buttonCancelJob.Enabled = running;
            radioSourceSelected.Enabled = !running;
            radioSourceAllVideos.Enabled = !running;
            comboTargetFormat.Enabled = !running;
            comboCodec.Enabled = !running;
            checkReplaceOriginal.Enabled = !running;
        }
    }
}
