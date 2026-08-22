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
    /// <summary>
    /// Tools → pre-bucket: letterbox images onto resolution buckets, write
    /// them into {width}x{height} folders, then delete emptied source folders.
    /// </summary>
    public sealed class Form_PreBucket : Form
    {
        private readonly MainForm owner;
        private readonly bool folderSourceAvailable =
            !string.IsNullOrEmpty(Program.DataManager?.ActiveFolder);
        private readonly Dictionary<string, Size?> sizeCache = new Dictionary<string, Size?>(StringComparer.OrdinalIgnoreCase);

        private const int SliderRepeatsMax = 100;
        private const int SliderBatchMax = 64;
        private const int SliderEpochsMax = 100;

        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceFolder = new RadioButton();
        private readonly RadioButton radioSourceAllImages = new RadioButton();
        private readonly NumericUpDown numResoW = new NumericUpDown();
        private readonly NumericUpDown numResoH = new NumericUpDown();
        private readonly NumericUpDown numMinReso = new NumericUpDown();
        private readonly NumericUpDown numMaxReso = new NumericUpDown();
        private readonly NumericUpDown numSteps = new NumericUpDown();
        private readonly NumericUpDown numTarget = new NumericUpDown();
        private readonly Button buttonQuick4 = new Button();
        private readonly Button buttonQuick8 = new Button();
        private readonly Button buttonQuick12 = new Button();
        private readonly Button buttonQuick16 = new Button();
        private readonly Button buttonQuickAll = new Button();
        private readonly CheckBox chkNoUpscale = new CheckBox();
        private readonly ListView listBuckets = new ListView();
        private readonly Label labelSummary = new Label();
        private readonly TrackBar trackRepeats = new TrackBar();
        private readonly TrackBar trackBatch = new TrackBar();
        private readonly TrackBar trackEpochs = new TrackBar();
        private readonly Label labelRepeatsValue = new Label();
        private readonly Label labelBatchValue = new Label();
        private readonly Label labelEpochsValue = new Label();
        private readonly Label labelHint = new Label();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonStart = new Button();
        private readonly Button buttonCancel = new Button();

        private bool running;
        private bool closeAfterRun;
        private bool loadingSettings;
        private CancellationTokenSource runCancellation;

        public IReadOnlyList<string> NewFilePaths { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> RemovedSourcePaths { get; private set; } = Array.Empty<string>();

        public Form_PreBucket(MainForm owner, ResolutionPrepSource? source = null)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(LogicalToDeviceUnits(580), LogicalToDeviceUnits(720));
            Size = new Size(LogicalToDeviceUnits(660), LogicalToDeviceUnits(820));
            Text = I18n.GetText("PreBucketTitle");
            Padding = new Padding(LogicalToDeviceUnits(12));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(BuildSourceGroup(), 0, 0);
            layout.Controls.Add(BuildSettingsGroup(), 0, 1);
            layout.Controls.Add(BuildTargetRow(), 0, 2);
            layout.Controls.Add(BuildBucketList(), 0, 3);
            layout.Controls.Add(BuildSummary(), 0, 4);
            layout.Controls.Add(BuildOutputRow(), 0, 5);
            layout.Controls.Add(BuildStatus(), 0, 6);
            layout.Controls.Add(BuildButtons(), 0, 7);
            Controls.Add(layout);

            LoadSettings();
            if (source.HasValue)
                SelectSource(source.Value);
            else
                ApplyAutoSource();
            UpdateBucketControls();
            RefreshPreview();

            if (Program.ColorManager != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        public void SelectFolderSource()
        {
            if (radioSourceFolder.Enabled)
                radioSourceFolder.Checked = true;
        }

        public void SelectAllImagesSource()
        {
            radioSourceAllImages.Checked = true;
        }

        public void SelectSelectedSource()
        {
            radioSourceSelected.Checked = true;
        }

        private void SelectSource(ResolutionPrepSource source)
        {
            switch (source)
            {
                case ResolutionPrepSource.Folder:
                    SelectFolderSource();
                    break;
                case ResolutionPrepSource.AllImages:
                    SelectAllImagesSource();
                    break;
                default:
                    SelectSelectedSource();
                    break;
            }
        }

        private void ApplyAutoSource()
        {
            if (owner.GetSelectedDatasetImagePaths().Count > 0)
                radioSourceSelected.Checked = true;
            else if (folderSourceAvailable)
                radioSourceFolder.Checked = true;
            else
                radioSourceAllImages.Checked = true;
        }

        private GroupBox BuildSourceGroup()
        {
            var group = new GroupBox
            {
                Text = I18n.GetText("TaggerSourceGroup"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            radioSourceSelected.Text = I18n.GetText("TaggerSourceSelected");
            radioSourceFolder.Text = I18n.GetText("TaggerSourceFolder");
            radioSourceAllImages.Text = I18n.GetText("TaggerSourceAllImages");
            foreach (RadioButton radio in new[] { radioSourceSelected, radioSourceFolder, radioSourceAllImages })
            {
                radio.AutoSize = true;
                radio.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
                radio.CheckedChanged += (_, _) => RefreshPreview();
            }
            radioSourceFolder.Enabled = folderSourceAvailable;
            flow.Controls.AddRange(new Control[] { radioSourceSelected, radioSourceFolder, radioSourceAllImages });
            group.Controls.Add(flow);
            return group;
        }

        private GroupBox BuildSettingsGroup()
        {
            var group = new GroupBox
            {
                Text = I18n.GetText("PreBucketSettings"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 5
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            SetupNumeric(numResoW, PreBucketMath.MinDimension, PreBucketMath.MaxDimension, 64);
            SetupNumeric(numResoH, PreBucketMath.MinDimension, PreBucketMath.MaxDimension, 64);
            SetupNumeric(numMinReso, PreBucketMath.MinDimension, PreBucketMath.MaxDimension, 64);
            SetupNumeric(numMaxReso, PreBucketMath.MinDimension, PreBucketMath.MaxDimension, 64);
            SetupNumeric(numSteps, PreBucketMath.MinSteps, PreBucketMath.MaxSteps, 8);
            numResoW.ValueChanged += (_, _) => OnSettingsChanged();
            numResoH.ValueChanged += (_, _) => OnSettingsChanged();
            numMinReso.ValueChanged += (_, _) => OnSettingsChanged();
            numMaxReso.ValueChanged += (_, _) => OnSettingsChanged();
            numSteps.ValueChanged += (_, _) => OnSettingsChanged();

            var resoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            resoRow.Controls.Add(numResoW);
            resoRow.Controls.Add(new Label
            {
                Text = "×",
                AutoSize = true,
                Padding = new Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(6), LogicalToDeviceUnits(4), 0)
            });
            resoRow.Controls.Add(numResoH);

            AddSettingRow(table, 0, "PreBucketResolution", "PreBucketResolutionHint", resoRow);
            AddSettingRow(table, 1, "PreBucketMinReso", "PreBucketMinResoHint", numMinReso);
            AddSettingRow(table, 2, "PreBucketMaxReso", "PreBucketMaxResoHint", numMaxReso);
            AddSettingRow(table, 3, "PreBucketResoSteps", "PreBucketResoStepsHint", numSteps);

            chkNoUpscale.Text = I18n.GetText("PreBucketNoUpscale");
            chkNoUpscale.AutoSize = true;
            chkNoUpscale.Checked = true;
            chkNoUpscale.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0);
            chkNoUpscale.CheckedChanged += (_, _) => OnSettingsChanged();
            table.Controls.Add(chkNoUpscale, 0, 4);
            table.SetColumnSpan(chkNoUpscale, 2);

            group.Controls.Add(table);
            return group;
        }

        private void AddSettingRow(TableLayoutPanel table, int row, string nameKey, string hintKey, Control editor)
        {
            var text = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 0, 8, 4)
            };
            text.Controls.Add(new Label
            {
                Text = I18n.GetText(nameKey),
                AutoSize = true,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            });
            text.Controls.Add(new Label
            {
                Text = I18n.GetText(hintKey),
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(360), 0)
            });
            editor.Anchor = AnchorStyles.Right;
            table.Controls.Add(text, 0, row);
            table.Controls.Add(editor, 1, row);
        }

        private Control BuildTargetRow()
        {
            var box = new GroupBox
            {
                Text = I18n.GetText("PreBucketTarget"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            SetupNumeric(numTarget, PreBucketMath.MinTarget, PreBucketMath.MaxTarget, 1);
            numTarget.ValueChanged += (_, _) => OnSettingsChanged();
            foreach (Button button in new[] { buttonQuick4, buttonQuick8, buttonQuick12, buttonQuick16, buttonQuickAll })
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(LogicalToDeviceUnits(40), LogicalToDeviceUnits(26));
            }
            buttonQuick4.Text = "4";
            buttonQuick8.Text = "8";
            buttonQuick12.Text = "12";
            buttonQuick16.Text = "16";
            buttonQuickAll.Text = I18n.GetText("PreBucketTargetAll");
            buttonQuick4.Click += (_, _) => SetTarget(4);
            buttonQuick8.Click += (_, _) => SetTarget(8);
            buttonQuick12.Click += (_, _) => SetTarget(12);
            buttonQuick16.Click += (_, _) => SetTarget(16);
            buttonQuickAll.Click += (_, _) => SetTarget(0);
            flow.Controls.Add(numTarget);
            flow.Controls.Add(buttonQuick4);
            flow.Controls.Add(buttonQuick8);
            flow.Controls.Add(buttonQuick12);
            flow.Controls.Add(buttonQuick16);
            flow.Controls.Add(buttonQuickAll);
            box.Controls.Add(flow);
            return box;
        }

        private Control BuildBucketList()
        {
            listBuckets.Dock = DockStyle.Fill;
            listBuckets.View = View.Details;
            listBuckets.FullRowSelect = true;
            listBuckets.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            listBuckets.HideSelection = false;
            listBuckets.Columns.Add(I18n.GetText("PreBucketColReso"), LogicalToDeviceUnits(140));
            listBuckets.Columns.Add(I18n.GetText("PreBucketColAspect"), LogicalToDeviceUnits(90));
            listBuckets.Columns.Add(I18n.GetText("PreBucketColCount"), LogicalToDeviceUnits(80));
            return listBuckets;
        }

        private Control BuildSummary()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4)),
                MinimumSize = new Size(0, LogicalToDeviceUnits(128))
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var sliders = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 3
            };
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sliders.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sliders.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sliders.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            SetupSlider(trackRepeats, PreBucketMath.MinRepeats, SliderRepeatsMax);
            SetupSlider(trackBatch, PreBucketMath.MinBatch, SliderBatchMax);
            SetupSlider(trackEpochs, PreBucketMath.MinEpochs, SliderEpochsMax);
            AddSliderRow(sliders, 0, "PreBucketRepeats", trackRepeats, labelRepeatsValue);
            AddSliderRow(sliders, 1, "PreBucketBatch", trackBatch, labelBatchValue);
            AddSliderRow(sliders, 2, "PreBucketEpochs", trackEpochs, labelEpochsValue);
            UpdateSliderValueLabels();

            labelSummary.AutoSize = false;
            labelSummary.Dock = DockStyle.Fill;
            labelSummary.UseMnemonic = false;
            labelSummary.Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(2), 0, 0);

            panel.Controls.Add(sliders, 0, 0);
            panel.Controls.Add(labelSummary, 1, 0);
            return panel;
        }

        private Control BuildOutputRow()
        {
            labelHint.Text = I18n.GetText("PreBucketOutputHint");
            labelHint.AutoSize = true;
            labelHint.MaximumSize = new Size(LogicalToDeviceUnits(560), 0);
            labelHint.Dock = DockStyle.Top;
            labelHint.Padding = new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4));
            return labelHint;
        }

        private Control BuildStatus()
        {
            labelStatus.AutoSize = false;
            labelStatus.Dock = DockStyle.Fill;
            labelStatus.Height = LogicalToDeviceUnits(36);
            return labelStatus;
        }

        private Control BuildButtons()
        {
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonCancel.Text = I18n.GetText("PreBucketCancel");
            buttonCancel.AutoSize = true;
            buttonCancel.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            buttonCancel.Click += ButtonCancel_Click;
            buttonStart.Text = I18n.GetText("PreBucketStart");
            buttonStart.AutoSize = true;
            buttonStart.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            buttonStart.Click += async (_, _) => await RunAsync();
            buttons.Controls.Add(buttonCancel);
            buttons.Controls.Add(buttonStart);
            return buttons;
        }

        private void AddSliderRow(TableLayoutPanel table, int row, string nameKey, TrackBar slider, Label valueLabel)
        {
            var name = new Label
            {
                Text = I18n.GetText(nameKey),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(8), 0)
            };
            valueLabel.AutoSize = false;
            valueLabel.Width = LogicalToDeviceUnits(36);
            valueLabel.TextAlign = ContentAlignment.MiddleRight;
            valueLabel.Anchor = AnchorStyles.Right;
            valueLabel.Padding = new Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(6), 0, 0);
            slider.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            slider.MinimumSize = new Size(LogicalToDeviceUnits(80), LogicalToDeviceUnits(28));
            table.Controls.Add(name, 0, row);
            table.Controls.Add(slider, 1, row);
            table.Controls.Add(valueLabel, 2, row);
        }

        private void SetupSlider(TrackBar bar, int min, int max)
        {
            bar.Minimum = min;
            bar.Maximum = max;
            bar.TickStyle = TickStyle.None;
            bar.SmallChange = 1;
            bar.LargeChange = Math.Max(1, max / 10);
            bar.AutoSize = false;
            bar.Height = LogicalToDeviceUnits(28);
            bar.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(4));
            bar.ValueChanged += Slider_ValueChanged;
        }

        private void Slider_ValueChanged(object sender, EventArgs e)
        {
            UpdateSliderValueLabels();
            OnSettingsChanged();
        }

        private void UpdateSliderValueLabels()
        {
            labelRepeatsValue.Text = trackRepeats.Value.ToString();
            labelBatchValue.Text = trackBatch.Value.ToString();
            labelEpochsValue.Text = trackEpochs.Value.ToString();
        }

        private void SetupNumeric(NumericUpDown box, int min, int max, int increment)
        {
            box.Minimum = min;
            box.Maximum = max;
            box.Increment = increment;
            box.Width = LogicalToDeviceUnits(80);
            box.ThousandsSeparator = false;
        }

        private void SetTarget(int value)
        {
            loadingSettings = true;
            try
            {
                numTarget.Value = Math.Clamp(value, (int)numTarget.Minimum, (int)numTarget.Maximum);
            }
            finally
            {
                loadingSettings = false;
            }
            OnSettingsChanged();
        }

        private void UpdateBucketControls()
        {
            bool enabled = !running;
            numMinReso.Enabled = enabled;
            numMaxReso.Enabled = enabled;
            numSteps.Enabled = enabled;
            numTarget.Enabled = enabled;
            buttonQuick4.Enabled = enabled;
            buttonQuick8.Enabled = enabled;
            buttonQuick12.Enabled = enabled;
            buttonQuick16.Enabled = enabled;
            buttonQuickAll.Enabled = enabled;
        }

        private void OnSettingsChanged()
        {
            if (loadingSettings || running)
                return;
            PersistSettings();
            RefreshPreview();
        }

        private void LoadSettings()
        {
            AppSettings settings = Program.Settings;
            loadingSettings = true;
            try
            {
                PreBucketSettings normalized = PreBucketMath.Normalize(new PreBucketSettings
                {
                    ResolutionWidth = settings?.PreBucketResolutionWidth ?? PreBucketMath.DefaultResolution,
                    ResolutionHeight = settings?.PreBucketResolutionHeight ?? PreBucketMath.DefaultResolution,
                    EnableBucket = true,
                    MinBucketReso = settings?.PreBucketMinReso ?? PreBucketMath.DefaultMinReso,
                    MaxBucketReso = settings?.PreBucketMaxReso ?? PreBucketMath.DefaultMaxReso,
                    BucketResoSteps = settings?.PreBucketResoSteps ?? PreBucketMath.DefaultSteps,
                    TargetBucketCount = settings?.PreBucketTargetCount ?? 0,
                    AllowUpscale = settings?.PreBucketAllowUpscale ?? false,
                    Repeats = settings?.PreBucketRepeats ?? 1,
                    BatchSize = settings?.PreBucketBatchSize ?? 4,
                    Epochs = settings?.PreBucketEpochs ?? 1,
                    OutputRoot = DefaultOutputRoot()
                });
                SetNumeric(numResoW, normalized.ResolutionWidth);
                SetNumeric(numResoH, normalized.ResolutionHeight);
                SetNumeric(numMinReso, normalized.MinBucketReso);
                SetNumeric(numMaxReso, normalized.MaxBucketReso);
                SetNumeric(numSteps, normalized.BucketResoSteps);
                SetNumeric(numTarget, normalized.TargetBucketCount);
                chkNoUpscale.Checked = !normalized.AllowUpscale;
                SetSlider(trackRepeats, normalized.Repeats);
                SetSlider(trackBatch, normalized.BatchSize);
                SetSlider(trackEpochs, normalized.Epochs);
                ResolutionPrepSource source = settings?.PreBucketSource ?? ResolutionPrepSource.Selected;
                if (Enum.IsDefined(typeof(ResolutionPrepSource), source))
                    SelectSource(source);
            }
            finally
            {
                loadingSettings = false;
            }
        }

        private void PersistSettings()
        {
            if (Program.Settings == null || loadingSettings)
                return;
            PreBucketSettings current = CurrentSettings();
            Program.Settings.PreBucketSource = CurrentSource();
            Program.Settings.PreBucketResolutionWidth = current.ResolutionWidth;
            Program.Settings.PreBucketResolutionHeight = current.ResolutionHeight;
            Program.Settings.PreBucketEnableBucket = true;
            Program.Settings.PreBucketMinReso = current.MinBucketReso;
            Program.Settings.PreBucketMaxReso = current.MaxBucketReso;
            Program.Settings.PreBucketResoSteps = current.BucketResoSteps;
            Program.Settings.PreBucketTargetCount = current.TargetBucketCount;
            Program.Settings.PreBucketAllowUpscale = current.AllowUpscale;
            Program.Settings.PreBucketRepeats = current.Repeats;
            Program.Settings.PreBucketBatchSize = current.BatchSize;
            Program.Settings.PreBucketEpochs = current.Epochs;
            Program.Settings.PreBucketOutputFolder = string.Empty;
            Program.Settings.SaveSettings();
        }

        private static void SetNumeric(NumericUpDown box, int value)
        {
            decimal clamped = Math.Clamp(value, box.Minimum, box.Maximum);
            box.Value = clamped;
        }

        private static void SetSlider(TrackBar bar, int value)
        {
            bar.Value = Math.Clamp(value, bar.Minimum, bar.Maximum);
        }

        private ResolutionPrepSource CurrentSource()
        {
            if (radioSourceFolder.Checked)
                return ResolutionPrepSource.Folder;
            if (radioSourceAllImages.Checked)
                return ResolutionPrepSource.AllImages;
            return ResolutionPrepSource.Selected;
        }

        private PreBucketSettings CurrentSettings()
        {
            return PreBucketMath.Normalize(new PreBucketSettings
            {
                ResolutionWidth = (int)numResoW.Value,
                ResolutionHeight = (int)numResoH.Value,
                EnableBucket = true,
                MinBucketReso = (int)numMinReso.Value,
                MaxBucketReso = (int)numMaxReso.Value,
                BucketResoSteps = (int)numSteps.Value,
                TargetBucketCount = (int)numTarget.Value,
                AllowUpscale = !chkNoUpscale.Checked,
                Repeats = trackRepeats.Value,
                BatchSize = trackBatch.Value,
                Epochs = trackEpochs.Value,
                OutputRoot = DefaultOutputRoot()
            });
        }

        private IReadOnlyList<string> TargetPaths()
        {
            return PreBucketMath.ResolveSourcePaths(
                CurrentSource(),
                owner.GetSelectedDatasetImagePaths(),
                GetFolderImagePaths(),
                GetAllImagePaths());
        }

        private static List<string> GetFolderImagePaths()
        {
            if (Program.DataManager == null)
                return new List<string>();
            return FilterImagePaths(Program.DataManager.GetScopedItems().Select(item => item.ImageFilePath));
        }

        private static List<string> GetAllImagePaths()
        {
            if (Program.DataManager == null)
                return new List<string>();
            return FilterImagePaths(Program.DataManager.DataSet.Values.Select(item => item.ImageFilePath));
        }

        private static List<string> FilterImagePaths(IEnumerable<string> paths)
        {
            return paths
                .Where(path => !string.IsNullOrWhiteSpace(path)
                    && Extensions.ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())
                    && !VideoProcessingService.IsVideoFile(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Size? SizeOf(string path)
        {
            if (sizeCache.TryGetValue(path, out Size? size))
                return size;
            size = PreBucketService.TryGetImageSize(path);
            sizeCache[path] = size;
            return size;
        }

        private PreBucketPlan BuildPlan()
        {
            var items = TargetPaths().Select(path => (path, SizeOf(path)));
            return PreBucketMath.Plan(items, CurrentSettings());
        }

        private void RefreshPreview()
        {
            if (running)
                return;
            PreBucketPlan plan = BuildPlan();
            listBuckets.BeginUpdate();
            try
            {
                listBuckets.Items.Clear();
                foreach (PreBucketGroup group in plan.Groups)
                {
                    var item = new ListViewItem(group.FolderName);
                    item.SubItems.Add(group.AspectRatio.ToString("0.###"));
                    item.SubItems.Add(group.Count.ToString());
                    listBuckets.Items.Add(item);
                }
            }
            finally
            {
                listBuckets.EndUpdate();
            }

            labelSummary.Text = string.Format(I18n.GetText("PreBucketSummary"), plan.Groups.Count)
                + Environment.NewLine
                + Environment.NewLine
                + string.Format(I18n.GetText("PreBucketTheoretical"), plan.TheoreticalSteps)
                + Environment.NewLine
                + I18n.GetText("PreBucketTheoreticalFormula")
                + Environment.NewLine
                + Environment.NewLine
                + string.Format(I18n.GetText("PreBucketEstimated"), plan.BucketedSteps)
                + Environment.NewLine
                + I18n.GetText("PreBucketActualFormula");

            if (plan.Jobs.Count == 0)
            {
                labelStatus.Text = plan.ImageCount == 0
                    ? I18n.GetText("PreBucketNoJobs")
                    : string.Format(I18n.GetText("PreBucketSkipped"), plan.SkippedImages);
            }
            else
            {
                labelStatus.Text = string.Format(I18n.GetText("PreBucketStatus"), plan.Jobs.Count, plan.Groups.Count);
                if (plan.SkippedImages > 0)
                {
                    labelStatus.Text += Environment.NewLine
                        + string.Format(I18n.GetText("PreBucketSkipped"), plan.SkippedImages);
                }
            }
        }

        private string DefaultOutputRoot()
        {
            if (!string.IsNullOrWhiteSpace(Program.DataManager?.DatasetRoot))
                return Program.DataManager.DatasetRoot;
            return owner.GetSelectedDatasetDirectory() ?? string.Empty;
        }

        private async Task RunAsync()
        {
            if (running)
                return;

            string outputRoot = DefaultOutputRoot();
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(
                    this,
                    I18n.GetText("PreBucketConfirm"),
                    Text,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            PersistSettings();
            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            var created = new List<string>();
            PreBucketPlan plan = null;
            int failed = 0;
            try
            {
                CancellationToken token = runCancellation.Token;
                plan = BuildPlan();
                if (plan.Jobs.Count == 0)
                {
                    MessageBox.Show(this, I18n.GetText("PreBucketNoJobs"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                PreBucketService.AssignOutputPaths(plan.Jobs, outputRoot);
                IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(tuple =>
                {
                    labelStatus.Text = string.Format(I18n.GetText("PreBucketProgress"), tuple.Done, tuple.Total);
                });
                string[] tagExtensions = Program.Settings?.GetTagFilesExtensions();
                await Task.Run(() =>
                {
                    for (int i = 0; i < plan.Jobs.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string written = PreBucketService.TryWrite(plan.Jobs[i], tagExtensions);
                        if (written != null)
                            lock (created)
                                created.Add(written);
                        else
                            Interlocked.Increment(ref failed);
                        progress.Report((i + 1, plan.Jobs.Count));
                    }
                }, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                running = false;
                runCancellation?.Dispose();
                runCancellation = null;
                NewFilePaths = created;
                RemovedSourcePaths = plan == null
                    ? Array.Empty<string>()
                    : PreBucketMath.CollectRemovableSources(plan.Jobs, created);
                if (closeAfterRun)
                {
                    DialogResult = created.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
                    Close();
                }
                else if (created.Count > 0)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    SetUiLocked(false);
                    RefreshPreview();
                    if (failed > 0)
                    {
                        MessageBox.Show(
                            this,
                            string.Format(I18n.GetText("PreBucketPartial"), created.Count, failed),
                            Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SetUiLocked(bool locked)
        {
            radioSourceSelected.Enabled = !locked;
            radioSourceFolder.Enabled = !locked && folderSourceAvailable;
            radioSourceAllImages.Enabled = !locked;
            numResoW.Enabled = !locked;
            numResoH.Enabled = !locked;
            chkNoUpscale.Enabled = !locked;
            trackRepeats.Enabled = !locked;
            trackBatch.Enabled = !locked;
            trackEpochs.Enabled = !locked;
            buttonStart.Enabled = !locked;
            UpdateBucketControls();
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            if (running)
            {
                runCancellation?.Cancel();
                return;
            }
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running)
            {
                e.Cancel = true;
                closeAfterRun = true;
                runCancellation?.Cancel();
            }
            else
            {
                PersistSettings();
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                ButtonCancel_Click(this, EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}
