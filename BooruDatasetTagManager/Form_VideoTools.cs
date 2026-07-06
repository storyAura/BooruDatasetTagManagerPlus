using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_VideoTools : Form
    {
        private const int FrameCacheLimit = 30;

        private readonly MainForm owner;
        private readonly VideoProcessingService videoService;
        private readonly string initialVideoPath;
        private readonly SplitContainer splitMain = new SplitContainer();
        private readonly Panel panelPreview = new Panel();
        private readonly PictureBox previewBox = new PictureBox();
        private readonly TrackBar scrubber = new TrackBar();
        private readonly Label labelFrameInfo = new Label();
        private readonly Button buttonPlayPause = new Button();
        private readonly Button buttonPrevFrame = new Button();
        private readonly Button buttonNextFrame = new Button();
        private readonly Button buttonPrevVideo = new Button();
        private readonly Button buttonNextVideo = new Button();
        private readonly Button buttonLockFrame = new Button();
        private readonly ListBox lockedFrames = new ListBox();
        private readonly Button buttonClearLocked = new Button();
        private readonly Label labelNativeFpsValue = new Label();
        private readonly GroupBox groupExtract = new GroupBox();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceAllVideos = new RadioButton();
        private readonly RadioButton radioExtractAll = new RadioButton();
        private readonly RadioButton radioExtractFps = new RadioButton();
        private readonly RadioButton radioExtractNativeFps = new RadioButton();
        private readonly RadioButton radioExtractSpecific = new RadioButton();
        private readonly NumericUpDown numericFps = new NumericUpDown();
        private readonly TextBox textSpecificFrames = new TextBox();
        private readonly ComboBox comboImageFormat = new ComboBox();
        private readonly ProgressBar progressBar = new ProgressBar();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonRun = new Button();
        private readonly Button buttonCancelJob = new Button();
        private readonly Button buttonClose = new Button();
        private readonly System.Windows.Forms.Timer playbackTimer = new System.Windows.Forms.Timer();
        private readonly Dictionary<int, Image> frameCache = new Dictionary<int, Image>();
        private readonly List<int> lockedFrameNumbers = new List<int>();
        private readonly string previewTempDirectory;

        private List<string> previewVideos = new List<string>();
        private VideoInfo currentVideoInfo;
        private string currentVideoPath;
        private int currentVideoIndex = -1;
        private int currentFrameIndex;
        private bool isPlaying;
        private bool isLoadingFrame;
        private CancellationTokenSource jobCancellation;

        public Form_VideoTools(MainForm owner, string initialVideoPath = null)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.initialVideoPath = initialVideoPath;
            videoService = VideoProcessingService.CreateDefault();
            previewTempDirectory = Path.Combine(Path.GetTempPath(), "BDTM_preview_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(previewTempDirectory);
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

            Text = "Video Tools";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(1080, 820);
            ClientSize = new Size(1180, 860);

            splitMain.Dock = DockStyle.Fill;
            splitMain.Panel1.Controls.Add(panelPreview);

            panelPreview.Dock = DockStyle.Fill;
            previewBox.Dock = DockStyle.Fill;
            previewBox.SizeMode = PictureBoxSizeMode.Zoom;
            previewBox.BackColor = Color.FromArgb(24, 24, 24);

            var panelControls = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = ScaleLayout(300),
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8, 6, 8, 8)
            };
            panelControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(52F)));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(24F)));
            panelControls.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panelControls.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(112F)));

            scrubber.Dock = DockStyle.Fill;
            scrubber.TickStyle = TickStyle.None;
            scrubber.Minimum = 0;
            scrubber.Maximum = 0;
            scrubber.Scroll += async (_, _) => await ShowFrameAsync(scrubber.Value, false).ConfigureAwait(true);

            labelFrameInfo.Dock = DockStyle.Fill;
            labelFrameInfo.TextAlign = ContentAlignment.MiddleLeft;
            labelFrameInfo.AutoEllipsis = true;

            var panelButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 4),
                Margin = new Padding(0)
            };

            buttonPlayPause.Click += async (_, _) => await TogglePlaybackAsync().ConfigureAwait(true);
            buttonPrevFrame.Click += async (_, _) => await StepFrameAsync(-1).ConfigureAwait(true);
            buttonNextFrame.Click += async (_, _) => await StepFrameAsync(1).ConfigureAwait(true);
            buttonPrevVideo.Click += async (_, _) => await SwitchVideoAsync(-1).ConfigureAwait(true);
            buttonNextVideo.Click += async (_, _) => await SwitchVideoAsync(1).ConfigureAwait(true);
            buttonLockFrame.Click += (_, _) => LockCurrentFrame();

            panelButtons.Controls.Add(buttonPlayPause);
            panelButtons.Controls.Add(buttonPrevFrame);
            panelButtons.Controls.Add(buttonNextFrame);
            panelButtons.Controls.Add(buttonPrevVideo);
            panelButtons.Controls.Add(buttonNextVideo);
            panelButtons.Controls.Add(buttonLockFrame);

            lockedFrames.Dock = DockStyle.Fill;
            lockedFrames.IntegralHeight = false;
            lockedFrames.SelectionMode = SelectionMode.None;
            buttonClearLocked.Dock = DockStyle.Bottom;
            buttonClearLocked.Height = ScaleLayout(32);
            buttonClearLocked.Click += (_, _) => ClearLockedFrames();

            var panelLocked = new GroupBox
            {
                Dock = DockStyle.Fill,
                Name = "groupLockedFrames",
                Padding = new Padding(8, 4, 8, 4)
            };
            panelLocked.Controls.Add(lockedFrames);
            panelLocked.Controls.Add(buttonClearLocked);

            panelControls.Controls.Add(scrubber, 0, 0);
            panelControls.Controls.Add(labelFrameInfo, 0, 1);
            panelControls.Controls.Add(panelButtons, 0, 2);
            panelControls.Controls.Add(panelLocked, 0, 3);

            panelPreview.Controls.Add(previewBox);
            panelPreview.Controls.Add(panelControls);

            var panelRight = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8)
            };
            panelRight.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panelRight.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(148F)));
            panelRight.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panelRight.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLayout(120F)));
            splitMain.Panel2.Controls.Add(panelRight);

            var panelSource = BuildSourceGroup();
            BuildExtractGroup();

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
            panelActionButtons.Controls.Add(buttonRun);
            panelActionButtons.Controls.Add(buttonCancelJob);
            panelActionButtons.Controls.Add(buttonClose);

            panelBottom.Controls.Add(progressBar, 0, 0);
            panelBottom.Controls.Add(labelStatus, 0, 1);
            panelBottom.Controls.Add(panelActionButtons, 0, 2);

            panelRight.Controls.Add(panelSource, 0, 0);
            panelRight.Controls.Add(groupExtract, 0, 1);
            panelRight.Controls.Add(panelBottom, 0, 2);

            Controls.Add(splitMain);

            playbackTimer.Tick += async (_, _) => await PlaybackTickAsync().ConfigureAwait(true);

            radioSourceSelected.CheckedChanged += async (_, _) => await RefreshPreviewVideoListAsync().ConfigureAwait(true);
            radioSourceAllVideos.CheckedChanged += async (_, _) => await RefreshPreviewVideoListAsync().ConfigureAwait(true);

            Shown += async (_, _) =>
            {
                radioSourceSelected.Checked = true;

                if (!string.IsNullOrWhiteSpace(initialVideoPath) && VideoProcessingService.IsVideoFile(initialVideoPath))
                {
                    if (owner.GetSelectedDatasetVideoPaths().Any(v => string.Equals(v, initialVideoPath, StringComparison.OrdinalIgnoreCase)))
                        radioSourceSelected.Checked = true;
                }

                UpdateExtractControls();
                UpdateHintLabelWidths();
                await RefreshPreviewVideoListAsync().ConfigureAwait(true);
            };

            FormClosed += (_, _) => CleanupPreviewResources();
            Load += (_, _) => ConfigureSplitContainer();
            Resize += (_, _) => ClampSplitterDistance();
            splitMain.SplitterMoved += (_, _) => ClampSplitterDistance();
            groupExtract.SizeChanged += (_, _) => UpdateHintLabelWidths();
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
            if (groupExtract.Controls.Find("labelExtractHint", true).FirstOrDefault() is Label extractHint)
                UpdateHintLabel(extractHint, groupExtract, I18n.GetText("VideoToolsExtractFlatHint"));
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

        private void BuildExtractGroup()
        {
            groupExtract.Dock = DockStyle.Fill;
            groupExtract.Name = "groupExtract";
            groupExtract.Padding = new Padding(10, 4, 10, 6);

            radioExtractAll.AutoSize = true;
            radioExtractAll.Checked = true;
            radioExtractAll.CheckedChanged += (_, _) => UpdateExtractControls();

            radioExtractFps.AutoSize = true;
            radioExtractFps.CheckedChanged += (_, _) => UpdateExtractControls();

            numericFps.Width = ScaleLayout(90);
            numericFps.Minimum = 0.1M;
            numericFps.Maximum = 120;
            numericFps.DecimalPlaces = 1;
            numericFps.Increment = 0.5M;
            numericFps.Value = 1;

            radioExtractNativeFps.AutoSize = true;
            radioExtractNativeFps.CheckedChanged += (_, _) => UpdateExtractControls();

            labelNativeFpsValue.AutoSize = true;
            labelNativeFpsValue.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            radioExtractSpecific.AutoSize = true;
            radioExtractSpecific.CheckedChanged += (_, _) => UpdateExtractControls();

            textSpecificFrames.Dock = DockStyle.Fill;
            textSpecificFrames.TextChanged += TextSpecificFrames_TextChangedSync;

            comboImageFormat.DropDownStyle = ComboBoxStyle.DropDownList;
            comboImageFormat.Width = ScaleLayout(120);
            comboImageFormat.Items.AddRange(new object[] { "png", "jpg" });
            comboImageFormat.SelectedIndex = 0;

            var labelExtractHint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Name = "labelExtractHint",
                AutoEllipsis = false,
                Margin = new Padding(0, 8, 0, 0)
            };

            var extractLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7
            };
            extractLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            extractLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            extractLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            extractLayout.Controls.Add(radioExtractAll, 0, 0);
            extractLayout.SetColumnSpan(radioExtractAll, 2);
            extractLayout.Controls.Add(radioExtractFps, 0, 1);
            extractLayout.Controls.Add(numericFps, 1, 1);
            extractLayout.Controls.Add(radioExtractNativeFps, 0, 2);
            extractLayout.Controls.Add(labelNativeFpsValue, 1, 2);
            extractLayout.Controls.Add(radioExtractSpecific, 0, 3);
            extractLayout.SetColumnSpan(radioExtractSpecific, 2);
            extractLayout.Controls.Add(textSpecificFrames, 0, 4);
            extractLayout.SetColumnSpan(textSpecificFrames, 2);
            extractLayout.Controls.Add(comboImageFormat, 0, 5);
            extractLayout.SetColumnSpan(comboImageFormat, 2);
            extractLayout.Controls.Add(labelExtractHint, 0, 6);
            extractLayout.SetColumnSpan(labelExtractHint, 2);
            groupExtract.Controls.Add(extractLayout);
        }

        private int ScaleLayout(float value)
        {
            return Math.Max(1, (int)Math.Round(value * (DeviceDpi / 96f)));
        }

        private void ConfigureSplitContainer()
        {
            if (splitMain.Width <= 0)
                return;

            int minLeft = ScaleLayout(320);
            int minRight = ScaleLayout(300);
            int splitterWidth = splitMain.SplitterWidth;
            int maxLeft = splitMain.Width - minRight - splitterWidth;
            if (maxLeft < minLeft)
            {
                minLeft = Math.Max(ScaleLayout(160), (splitMain.Width - splitterWidth) / 2);
                minRight = Math.Max(ScaleLayout(160), splitMain.Width - minLeft - splitterWidth);
                maxLeft = splitMain.Width - minRight - splitterWidth;
            }

            splitMain.Panel1MinSize = minLeft;
            splitMain.Panel2MinSize = minRight;

            int preferred = ScaleLayout(500);
            splitMain.SplitterDistance = Math.Max(minLeft, Math.Min(preferred, maxLeft));
        }

        private void ClampSplitterDistance()
        {
            if (splitMain.Width <= 0)
                return;

            int minLeft = splitMain.Panel1MinSize;
            int minRight = splitMain.Panel2MinSize;
            if (minLeft <= 0 || minRight <= 0)
                return;

            int maxLeft = Math.Max(minLeft, splitMain.Width - minRight - splitMain.SplitterWidth);
            if (splitMain.SplitterDistance < minLeft)
                splitMain.SplitterDistance = minLeft;
            else if (splitMain.SplitterDistance > maxLeft)
                splitMain.SplitterDistance = maxLeft;
        }

        public void ApplyLanguage()
        {
            Text = I18n.GetText("VideoExtractForm");
            if (Controls.Find("groupSource", true).FirstOrDefault() is GroupBox groupSource)
                groupSource.Text = I18n.GetText("VideoToolsSourceGroup");
            groupExtract.Text = I18n.GetText("VideoToolsTabExtract");
            if (Controls.Find("groupLockedFrames", true).FirstOrDefault() is GroupBox groupLocked)
                groupLocked.Text = I18n.GetText("VideoToolsLockedFrames");

            radioSourceSelected.Text = I18n.GetText("VideoToolsSourceSelected");
            radioSourceAllVideos.Text = I18n.GetText("VideoToolsSourceAllVideos");
            radioExtractAll.Text = I18n.GetText("VideoToolsExtractAll");
            radioExtractFps.Text = I18n.GetText("VideoToolsExtractByFps");
            radioExtractNativeFps.Text = I18n.GetText("VideoToolsExtractNativeFps");
            radioExtractSpecific.Text = I18n.GetText("VideoToolsExtractSpecific");
            buttonRun.Text = I18n.GetText("VideoToolsRun");
            buttonCancelJob.Text = I18n.GetText("VideoToolsCancel");
            buttonClose.Text = I18n.GetText("VideoToolsClose");
            buttonPlayPause.Text = I18n.GetText("VideoToolsPlay");
            buttonPrevFrame.Text = I18n.GetText("VideoToolsPrevFrame");
            buttonNextFrame.Text = I18n.GetText("VideoToolsNextFrame");
            buttonPrevVideo.Text = I18n.GetText("VideoToolsPrevVideo");
            buttonNextVideo.Text = I18n.GetText("VideoToolsNextVideo");
            buttonLockFrame.Text = I18n.GetText("VideoToolsLockFrame");
            buttonClearLocked.Text = I18n.GetText("VideoToolsClearLocked");

            if (groupExtract.Controls.Find("labelExtractHint", true).FirstOrDefault() is Label extractHint)
                UpdateHintLabel(extractHint, groupExtract, I18n.GetText("VideoToolsExtractFlatHint"));

            ApplyButtonStyles();
            UpdateNativeFpsLabel();
            UpdateFrameInfoLabel();
        }

        private void ApplyButtonStyles()
        {
            ApplyPrimaryButton(buttonRun);
            ApplySecondaryButton(buttonPlayPause);
            ApplySecondaryButton(buttonPrevFrame);
            ApplySecondaryButton(buttonNextFrame);
            ApplySecondaryButton(buttonPrevVideo);
            ApplySecondaryButton(buttonNextVideo);
            ApplySecondaryButton(buttonLockFrame);
            ApplySecondaryButton(buttonClearLocked);
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

        private void UpdateExtractControls()
        {
            numericFps.Enabled = radioExtractFps.Checked;
            textSpecificFrames.Enabled = radioExtractSpecific.Checked;
            UpdateNativeFpsLabel();
        }

        private void UpdateNativeFpsLabel()
        {
            if (!radioExtractNativeFps.Checked)
            {
                labelNativeFpsValue.Text = string.Empty;
                return;
            }

            double fps = currentVideoInfo?.Fps ?? 0;
            if (fps <= 0 && previewVideos.Count > 0)
            {
                try
                {
                    fps = videoService.GetVideoInfoAsync(previewVideos[0], CancellationToken.None)
                        .GetAwaiter().GetResult().Fps;
                }
                catch
                {
                    fps = 0;
                }
            }

            labelNativeFpsValue.Text = fps > 0
                ? fps.ToString("0.##", CultureInfo.InvariantCulture) + " fps"
                : I18n.GetText("VideoToolsNativeFpsUnknown");
        }

        private void UpdateStatus(string line)
        {
            labelStatus.Text = line;
            toolTip.SetToolTip(labelStatus, line);
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

        private async Task RefreshPreviewVideoListAsync()
        {
            previewVideos = ResolveInputVideos();
            if (!string.IsNullOrWhiteSpace(initialVideoPath) && VideoProcessingService.IsVideoFile(initialVideoPath))
            {
                if (!previewVideos.Any(v => string.Equals(v, initialVideoPath, StringComparison.OrdinalIgnoreCase)))
                    previewVideos.Insert(0, initialVideoPath);
                currentVideoIndex = previewVideos.FindIndex(v => string.Equals(v, initialVideoPath, StringComparison.OrdinalIgnoreCase));
            }
            else if (previewVideos.Count == 0)
            {
                currentVideoIndex = -1;
                await LoadPreviewVideoAsync(null).ConfigureAwait(true);
                return;
            }
            else if (currentVideoIndex < 0 || currentVideoIndex >= previewVideos.Count)
            {
                currentVideoIndex = 0;
            }

            await LoadPreviewVideoAsync(previewVideos[currentVideoIndex]).ConfigureAwait(true);
        }

        private async Task LoadPreviewVideoAsync(string videoPath)
        {
            StopPlayback();
            ClearFrameCache();
            previewBox.Image?.Dispose();
            previewBox.Image = null;
            currentVideoPath = videoPath;
            currentVideoInfo = null;
            currentFrameIndex = 0;
            scrubber.Value = 0;
            scrubber.Maximum = 0;

            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                labelFrameInfo.Text = I18n.GetText("VideoToolsNoPreviewVideo");
                UpdateNativeFpsLabel();
                return;
            }

            try
            {
                currentVideoInfo = await videoService.GetVideoInfoAsync(videoPath, CancellationToken.None).ConfigureAwait(true);
                int maxFrame = Math.Max(0, currentVideoInfo.FrameCount - 1);
                scrubber.Maximum = maxFrame;
                UpdateNativeFpsLabel();
                await ShowFrameAsync(0, false).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                labelFrameInfo.Text = ex.Message;
            }
        }

        private async Task ShowFrameAsync(int frameIndex, bool fromPlayback)
        {
            if (string.IsNullOrWhiteSpace(currentVideoPath) || isLoadingFrame)
                return;

            int maxFrame = Math.Max(0, scrubber.Maximum);
            frameIndex = Math.Max(0, Math.Min(frameIndex, maxFrame));
            currentFrameIndex = frameIndex;
            if (!fromPlayback && scrubber.Value != frameIndex)
                scrubber.Value = frameIndex;

            if (frameCache.TryGetValue(frameIndex, out Image cached))
            {
                previewBox.Image?.Dispose();
                previewBox.Image = (Image)cached.Clone();
                UpdateFrameInfoLabel();
                return;
            }

            isLoadingFrame = true;
            try
            {
                string framePath = await videoService.ExtractFrameAsync(
                    currentVideoPath,
                    frameIndex,
                    previewTempDirectory,
                    CancellationToken.None).ConfigureAwait(true);

                if (string.IsNullOrEmpty(framePath) || !File.Exists(framePath))
                    return;

                using var loaded = Image.FromFile(framePath);
                var clone = (Image)loaded.Clone();
                AddFrameToCache(frameIndex, clone);
                previewBox.Image?.Dispose();
                previewBox.Image = (Image)clone.Clone();
                UpdateFrameInfoLabel();
            }
            finally
            {
                isLoadingFrame = false;
            }
        }

        private void AddFrameToCache(int frameIndex, Image image)
        {
            if (frameCache.ContainsKey(frameIndex))
            {
                frameCache[frameIndex].Dispose();
                frameCache[frameIndex] = image;
                return;
            }

            while (frameCache.Count >= FrameCacheLimit)
            {
                int oldest = frameCache.Keys.Min();
                frameCache[oldest].Dispose();
                frameCache.Remove(oldest);
            }

            frameCache[frameIndex] = image;
        }

        private void ClearFrameCache()
        {
            foreach (Image image in frameCache.Values)
                image.Dispose();
            frameCache.Clear();
        }

        private void UpdateFrameInfoLabel()
        {
            if (currentVideoInfo == null)
            {
                labelFrameInfo.Text = I18n.GetText("VideoToolsNoPreviewVideo");
                return;
            }

            double fps = currentVideoInfo.Fps > 0 ? currentVideoInfo.Fps : 24;
            double seconds = fps > 0 ? currentFrameIndex / fps : 0;
            int totalFrames = Math.Max(1, currentVideoInfo.FrameCount);
            labelFrameInfo.Text = string.Format(
                CultureInfo.InvariantCulture,
                I18n.GetText("VideoToolsFrameInfoFormat"),
                currentFrameIndex,
                Math.Max(0, totalFrames - 1),
                TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture),
                fps.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private async Task StepFrameAsync(int delta)
        {
            StopPlayback();
            await ShowFrameAsync(currentFrameIndex + delta, false).ConfigureAwait(true);
        }

        private async Task SwitchVideoAsync(int delta)
        {
            if (previewVideos.Count == 0)
                return;

            StopPlayback();
            currentVideoIndex = (currentVideoIndex + delta + previewVideos.Count) % previewVideos.Count;
            await LoadPreviewVideoAsync(previewVideos[currentVideoIndex]).ConfigureAwait(true);
        }

        private async Task TogglePlaybackAsync()
        {
            if (string.IsNullOrWhiteSpace(currentVideoPath))
                return;

            if (isPlaying)
            {
                StopPlayback();
                return;
            }

            double fps = currentVideoInfo?.Fps > 0 ? currentVideoInfo.Fps : 24;
            playbackTimer.Interval = Math.Max(16, (int)Math.Round(1000.0 / fps));
            isPlaying = true;
            buttonPlayPause.Text = I18n.GetText("VideoToolsPause");
            playbackTimer.Start();
            await PlaybackTickAsync().ConfigureAwait(true);
        }

        private async Task PlaybackTickAsync()
        {
            if (!isPlaying || string.IsNullOrWhiteSpace(currentVideoPath))
                return;

            if (currentFrameIndex >= scrubber.Maximum)
            {
                StopPlayback();
                return;
            }

            await ShowFrameAsync(currentFrameIndex + 1, true).ConfigureAwait(true);
        }

        private void StopPlayback()
        {
            isPlaying = false;
            playbackTimer.Stop();
            buttonPlayPause.Text = I18n.GetText("VideoToolsPlay");
        }

        private void LockCurrentFrame()
        {
            if (string.IsNullOrWhiteSpace(currentVideoPath))
                return;

            if (!lockedFrameNumbers.Contains(currentFrameIndex))
                lockedFrameNumbers.Add(currentFrameIndex);

            lockedFrameNumbers.Sort();
            RefreshLockedFramesList();
            SyncSpecificFramesTextFromLocked();
            if (!radioExtractSpecific.Checked)
                radioExtractSpecific.Checked = true;
        }

        private void ClearLockedFrames()
        {
            lockedFrameNumbers.Clear();
            RefreshLockedFramesList();
            SyncSpecificFramesTextFromLocked();
        }

        private void RefreshLockedFramesList()
        {
            lockedFrames.Items.Clear();
            foreach (int frame in lockedFrameNumbers)
                lockedFrames.Items.Add(string.Format(CultureInfo.InvariantCulture, I18n.GetText("VideoToolsLockedFrameItem"), frame));
        }

        private void SyncSpecificFramesTextFromLocked()
        {
            textSpecificFrames.TextChanged -= TextSpecificFrames_TextChangedSync;
            textSpecificFrames.Text = string.Join(", ", lockedFrameNumbers);
            textSpecificFrames.TextChanged += TextSpecificFrames_TextChangedSync;
        }

        private void SyncLockedFramesFromText()
        {
            if (!radioExtractSpecific.Checked)
                return;

            ParsedFrameSelection selection = VideoProcessingService.ParseFrameSelection(textSpecificFrames.Text);
            lockedFrameNumbers.Clear();
            lockedFrameNumbers.AddRange(selection.FrameNumbers);
            RefreshLockedFramesList();
        }

        private void TextSpecificFrames_TextChangedSync(object sender, EventArgs e)
        {
            SyncLockedFramesFromText();
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

            if (radioExtractAll.Checked)
            {
                if (MessageBox.Show(this, I18n.GetText("VideoToolsExtractAllConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            SetJobRunning(true);
            jobCancellation = new CancellationTokenSource();
            var progress = new VideoProgressReporter(line =>
            {
                if (IsDisposed)
                    return;

                if (InvokeRequired)
                    BeginInvoke(new Action(() => UpdateStatus(line)));
                else
                    UpdateStatus(line);
            });
            int completed = 0;
            var errors = new List<string>();
            var extractedVideos = new List<string>();
            string imageFormat = comboImageFormat.SelectedItem?.ToString() ?? "png";

            try
            {
                foreach (string input in inputs)
                {
                    jobCancellation.Token.ThrowIfCancellationRequested();
                    UpdateStatus(Path.GetFileName(input));
                    progressBar.Value = 0;

                    string outputDir = videoService.GetFlatExtractOutputDirectory(input);
                    FrameExtractMode mode = radioExtractAll.Checked
                        ? FrameExtractMode.All
                        : radioExtractFps.Checked
                            ? FrameExtractMode.ByFps
                            : radioExtractNativeFps.Checked
                                ? FrameExtractMode.NativeFps
                                : FrameExtractMode.Specific;
                    double extractFps = mode == FrameExtractMode.NativeFps
                        ? (currentVideoInfo?.Fps ?? (double)numericFps.Value)
                        : (double)numericFps.Value;
                    if (mode == FrameExtractMode.NativeFps && extractFps <= 0)
                    {
                        try
                        {
                            extractFps = (await videoService.GetVideoInfoAsync(input, jobCancellation.Token).ConfigureAwait(true)).Fps;
                        }
                        catch
                        {
                            extractFps = (double)numericFps.Value;
                        }
                    }

                    var result = await videoService.ExtractFramesAsync(
                        input,
                        outputDir,
                        mode,
                        extractFps,
                        textSpecificFrames.Text,
                        imageFormat,
                        progress,
                        jobCancellation.Token).ConfigureAwait(true);
                    if (!result.Success)
                        errors.Add(input + ": " + result.ErrorMessage);
                    else
                        extractedVideos.Add(input);

                    completed++;
                    progressBar.Value = Math.Min(100, (int)Math.Round(completed * 100.0 / inputs.Count));
                }

                if (extractedVideos.Count > 0 && Program.DataManager != null)
                {
                    var framePaths = new List<string>();
                    foreach (string video in extractedVideos)
                        framePaths.AddRange(videoService.ListExtractedFrameFiles(video, imageFormat));

                    if (framePaths.Count > 0)
                    {
                        owner.RefreshDatasetGrid(
                            Program.DataManager.AddImages(framePaths, loadPreviewImages: true, readMetadata: true));
                    }
                }

                if (errors.Count > 0)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors), I18n.GetText("VideoToolsCompletedWithErrors"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (extractedVideos.Count > 0
                    && MessageBox.Show(this, I18n.GetText("VideoExtractDeleteSourceConfirm"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    owner.DeleteDatasetMediaFiles(extractedVideos);
                }
                else if (errors.Count == 0)
                {
                    UpdateStatus(I18n.GetText("VideoToolsCompleted"));
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus(I18n.GetText("VideoToolsCancelled"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, I18n.GetText("UIError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                jobCancellation?.Dispose();
                jobCancellation = null;
                SetJobRunning(false);
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
            groupExtract.Enabled = !running;
            radioSourceSelected.Enabled = !running;
            radioSourceAllVideos.Enabled = !running;
        }

        private void CleanupPreviewResources()
        {
            StopPlayback();
            playbackTimer.Dispose();
            previewBox.Image?.Dispose();
            ClearFrameCache();
            try
            {
                Directory.Delete(previewTempDirectory, true);
            }
            catch
            {
                // ignored
            }
        }
    }
}
