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
    /// Multi-crop tool: scale / center-crop / tile-split / random crop / YOLO
    /// detect crop. Always writes new files. Internal type name stays
    /// Form_ResolutionPrep.
    /// </summary>
    public sealed class Form_ResolutionPrep : Form
    {
        private readonly MainForm owner;
        private readonly bool folderSourceAvailable =
            !string.IsNullOrEmpty(Program.DataManager?.ActiveFolder);
        private readonly Dictionary<string, Size?> sizeCache = new Dictionary<string, Size?>(StringComparer.OrdinalIgnoreCase);
        private readonly YoloPersonDetectorService yoloService = new YoloPersonDetectorService();

        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceFolder = new RadioButton();
        private readonly RadioButton radioSourceAllImages = new RadioButton();
        private readonly RadioButton radioScale = new RadioButton();
        private readonly RadioButton radioCenter = new RadioButton();
        private readonly RadioButton radioSplit = new RadioButton();
        private readonly RadioButton radioRandom = new RadioButton();
        private readonly RadioButton radioYolo = new RadioButton();
        private readonly ComboBox comboAspect = new ComboBox();
        private readonly ComboBox comboYoloModel = new ComboBox();
        private readonly ComboBox comboYoloDownloadSource = new ComboBox();
        private readonly NumericUpDown numAspectW = new NumericUpDown();
        private readonly NumericUpDown numAspectH = new NumericUpDown();
        private readonly NumericUpDown numRandomCount = new NumericUpDown();
        private readonly NumericUpDown numYoloConfidence = new NumericUpDown();
        private readonly Button buttonImportYolo = new Button();
        private readonly Button buttonDownloadYolo = new Button();
        private readonly Label labelRandomCount = new Label();
        private readonly Label labelYoloConfidence = new Label();
        private readonly Label labelYoloModel = new Label();
        private readonly Label labelYoloDownloadSource = new Label();
        private readonly Label labelYoloModelStatus = new Label();
        private readonly FlowLayoutPanel flowGears = new FlowLayoutPanel();
        private readonly NumericUpDown numCustomGear = new NumericUpDown();
        private readonly Button buttonAddGear = new Button();
        private readonly CheckBox chkSharpen = new CheckBox();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonStart = new Button();
        private readonly Button buttonCancel = new Button();

        private readonly List<int> customGears = new List<int>();
        private bool running;
        private bool closeAfterRun;
        private bool loadingSettings;
        private CancellationTokenSource runCancellation;

        public IReadOnlyList<string> NewFilePaths { get; private set; } = Array.Empty<string>();

        public Form_ResolutionPrep(MainForm owner, ResolutionPrepSource? source = null)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(LogicalToDeviceUnits(420), LogicalToDeviceUnits(720));
            ClientSize = new Size(LogicalToDeviceUnits(440), LogicalToDeviceUnits(780));
            Text = I18n.GetText("ResolutionPrepTitle");
            Padding = new Padding(LogicalToDeviceUnits(12));

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9
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
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(BuildSourceGroup(), 0, 0);
            layout.Controls.Add(BuildModeGroup(), 0, 1);
            layout.Controls.Add(BuildAspectRow(), 0, 2);

            var gearBox = new GroupBox
            {
                Text = I18n.GetText("ResolutionPrepGears"),
                Dock = DockStyle.Fill,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };
            flowGears.Dock = DockStyle.Fill;
            flowGears.AutoScroll = true;
            flowGears.FlowDirection = FlowDirection.TopDown;
            flowGears.WrapContents = false;
            gearBox.Controls.Add(flowGears);
            layout.Controls.Add(gearBox, 0, 3);

            var addRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                WrapContents = false,
                Padding = new Padding(0, LogicalToDeviceUnits(6), 0, 0)
            };
            numCustomGear.Minimum = ResolutionPrepMath.MinGear;
            numCustomGear.Maximum = ResolutionPrepMath.MaxGear;
            numCustomGear.Value = 640;
            numCustomGear.Width = LogicalToDeviceUnits(80);
            buttonAddGear.Text = I18n.GetText("ResolutionPrepAddGear");
            buttonAddGear.AutoSize = true;
            buttonAddGear.Click += (_, _) => AddCustomGear();
            addRow.Controls.Add(numCustomGear);
            addRow.Controls.Add(buttonAddGear);
            layout.Controls.Add(addRow, 0, 4);

            chkSharpen.Text = I18n.GetText("ResolutionPrepSharpen");
            chkSharpen.AutoSize = true;
            chkSharpen.Checked = true;
            layout.Controls.Add(chkSharpen, 0, 5);

            labelStatus.AutoSize = false;
            labelStatus.Dock = DockStyle.Fill;
            labelStatus.Height = LogicalToDeviceUnits(40);
            layout.Controls.Add(labelStatus, 0, 6);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            buttonCancel.Text = I18n.GetText("ResolutionPrepCancel");
            buttonCancel.AutoSize = true;
            buttonCancel.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            buttonCancel.Click += ButtonCancel_Click;
            buttonStart.Text = I18n.GetText("ResolutionPrepStart");
            buttonStart.AutoSize = true;
            buttonStart.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            buttonStart.Click += async (_, _) => await RunAsync();
            buttons.Controls.Add(buttonCancel);
            buttons.Controls.Add(buttonStart);
            layout.Controls.Add(buttons, 0, 7);
            Controls.Add(layout);

            LoadPresets();
            LoadSettings();
            if (source.HasValue)
                SelectSource(source.Value);
            else
                ApplyAutoSource();
            OnOptionsChanged();

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
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };
            radioSourceSelected.Text = I18n.GetText("TaggerSourceSelected");
            radioSourceFolder.Text = I18n.GetText("TaggerSourceFolder");
            radioSourceAllImages.Text = I18n.GetText("TaggerSourceAllImages");
            radioSourceSelected.AutoSize = true;
            radioSourceFolder.AutoSize = true;
            radioSourceAllImages.AutoSize = true;
            radioSourceFolder.Enabled = folderSourceAvailable;
            radioSourceSelected.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
            radioSourceFolder.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
            radioSourceSelected.CheckedChanged += (_, _) => UpdateStatus();
            radioSourceFolder.CheckedChanged += (_, _) => UpdateStatus();
            radioSourceAllImages.CheckedChanged += (_, _) => UpdateStatus();
            flow.Controls.AddRange(new Control[] { radioSourceSelected, radioSourceFolder, radioSourceAllImages });
            group.Controls.Add(flow);
            return group;
        }

        private GroupBox BuildModeGroup()
        {
            var modeBox = new GroupBox
            {
                Text = I18n.GetText("ResolutionPrepMode"),
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(LogicalToDeviceUnits(8))
            };
            var modeFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            radioScale.Text = I18n.GetText("ResolutionPrepModeScale");
            radioCenter.Text = I18n.GetText("ResolutionPrepModeCenter");
            radioSplit.Text = I18n.GetText("ResolutionPrepModeSplit");
            radioRandom.Text = I18n.GetText("ResolutionPrepModeRandom");
            radioYolo.Text = I18n.GetText("ResolutionPrepModeYolo");
            foreach (RadioButton radio in new[] { radioScale, radioCenter, radioSplit, radioRandom, radioYolo })
            {
                radio.AutoSize = true;
                radio.CheckedChanged += (_, _) => OnOptionsChanged();
            }
            modeFlow.Controls.AddRange(new Control[] { radioScale, radioCenter, radioSplit, radioRandom, radioYolo });
            modeBox.Controls.Add(modeFlow);
            return modeBox;
        }

        private TableLayoutPanel BuildAspectRow()
        {
            var aspectRow = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 4,
                Padding = new Padding(0, LogicalToDeviceUnits(8), 0, 0)
            };
            aspectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            aspectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            aspectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            aspectRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var labelAspect = new Label
            {
                Text = I18n.GetText("ResolutionPrepAspect"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            comboAspect.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAspect.Dock = DockStyle.Fill;
            comboAspect.SelectedIndexChanged += ComboAspect_SelectedIndexChanged;
            numAspectW.Minimum = 1;
            numAspectW.Maximum = 64;
            numAspectW.Value = 1;
            numAspectW.Width = LogicalToDeviceUnits(56);
            numAspectH.Minimum = 1;
            numAspectH.Maximum = 64;
            numAspectH.Value = 1;
            numAspectH.Width = LogicalToDeviceUnits(56);
            numAspectW.ValueChanged += (_, _) => UpdateStatus();
            numAspectH.ValueChanged += (_, _) => UpdateStatus();
            aspectRow.Controls.Add(labelAspect, 0, 0);
            aspectRow.SetColumnSpan(labelAspect, 4);
            aspectRow.Controls.Add(comboAspect, 0, 1);
            aspectRow.SetColumnSpan(comboAspect, 2);
            aspectRow.Controls.Add(numAspectW, 2, 1);
            aspectRow.Controls.Add(numAspectH, 3, 1);

            labelRandomCount.Text = I18n.GetText("ResolutionPrepRandomCount");
            labelRandomCount.AutoSize = true;
            labelRandomCount.Anchor = AnchorStyles.Left;
            numRandomCount.Minimum = ResolutionPrepMath.MinRandomCount;
            numRandomCount.Maximum = ResolutionPrepMath.MaxRandomCount;
            numRandomCount.Value = 1;
            numRandomCount.Width = LogicalToDeviceUnits(56);
            numRandomCount.ValueChanged += (_, _) => UpdateStatus();
            labelYoloConfidence.Text = I18n.GetText("ResolutionPrepYoloConfidence");
            labelYoloConfidence.AutoSize = true;
            labelYoloConfidence.Anchor = AnchorStyles.Left;
            numYoloConfidence.DecimalPlaces = 2;
            numYoloConfidence.Increment = 0.05m;
            numYoloConfidence.Minimum = 0.05m;
            numYoloConfidence.Maximum = 1.00m;
            numYoloConfidence.Value = 0.30m;
            numYoloConfidence.Width = LogicalToDeviceUnits(56);
            buttonImportYolo.Text = I18n.GetText("ResolutionPrepYoloImport");
            buttonImportYolo.AutoSize = true;
            buttonImportYolo.Click += (_, _) => ImportYoloModel();

            labelYoloModel.Text = I18n.GetText("YoloDetectModel");
            labelYoloModel.AutoSize = true;
            labelYoloModel.Anchor = AnchorStyles.Left;
            comboYoloModel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboYoloModel.Width = LogicalToDeviceUnits(220);
            foreach (YoloDetectorModelEntry model in YoloDetectorCatalog.AllModels)
                comboYoloModel.Items.Add(model);
            comboYoloModel.SelectedIndexChanged += (_, _) => OnSelectedYoloModelChanged();
            labelYoloDownloadSource.Text = I18n.GetText("TaggerDownloadSource");
            labelYoloDownloadSource.AutoSize = true;
            labelYoloDownloadSource.Anchor = AnchorStyles.Left;
            comboYoloDownloadSource.DropDownStyle = ComboBoxStyle.DropDownList;
            comboYoloDownloadSource.Width = LogicalToDeviceUnits(140);
            comboYoloDownloadSource.Items.AddRange(Extensions.GetFriendlyEnumValues<HuggingFaceDownloadSource>());
            comboYoloDownloadSource.SelectedIndexChanged += (_, _) =>
            {
                if (!loadingSettings)
                    PersistSettings();
            };
            buttonDownloadYolo.Text = I18n.GetText("TaggerDownloadModel");
            buttonDownloadYolo.AutoSize = true;
            buttonDownloadYolo.Click += async (_, _) => await DownloadSelectedYoloModelAsync();
            labelYoloModelStatus.AutoSize = true;
            labelYoloModelStatus.Anchor = AnchorStyles.Left;

            var extra = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(0, LogicalToDeviceUnits(6), 0, 0)
            };
            extra.Controls.Add(labelRandomCount);
            extra.Controls.Add(numRandomCount);
            extra.Controls.Add(labelYoloConfidence);
            extra.Controls.Add(numYoloConfidence);
            extra.Controls.Add(buttonImportYolo);
            aspectRow.Controls.Add(extra, 0, 2);
            aspectRow.SetColumnSpan(extra, 4);

            var yoloRow = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true,
                Padding = new Padding(0, LogicalToDeviceUnits(4), 0, 0)
            };
            yoloRow.Controls.Add(labelYoloModel);
            yoloRow.Controls.Add(comboYoloModel);
            yoloRow.Controls.Add(labelYoloDownloadSource);
            yoloRow.Controls.Add(comboYoloDownloadSource);
            yoloRow.Controls.Add(buttonDownloadYolo);
            yoloRow.Controls.Add(labelYoloModelStatus);
            aspectRow.Controls.Add(yoloRow, 0, 3);
            aspectRow.SetColumnSpan(yoloRow, 4);
            return aspectRow;
        }

        private void LoadPresets()
        {
            comboAspect.Items.Clear();
            foreach ((int width, int height) in BatchCropMath.Presets)
            {
                comboAspect.Items.Add(new AspectItem(
                    string.Format(I18n.GetText("ResolutionPrepAspectPreset"), width, height),
                    width,
                    height,
                    custom: false));
            }
            comboAspect.Items.Add(new AspectItem(I18n.GetText("ResolutionPrepAspectCustom"), 1, 1, custom: true));
            comboAspect.DisplayMember = nameof(AspectItem.Text);
        }

        private void LoadSettings()
        {
            AppSettings settings = Program.Settings;
            loadingSettings = true;
            try
            {
                LoadSettingsCore(settings);
            }
            finally
            {
                loadingSettings = false;
            }
            UpdateYoloModelStatus();
        }

        private void LoadSettingsCore(AppSettings settings)
        {
            ResolutionPrepMode mode = settings?.ResolutionPrepMode ?? ResolutionPrepMode.ScaleOnly;
            radioScale.Checked = mode == ResolutionPrepMode.ScaleOnly;
            radioCenter.Checked = mode == ResolutionPrepMode.CenterCrop;
            radioSplit.Checked = mode == ResolutionPrepMode.SplitTiles;
            radioRandom.Checked = mode == ResolutionPrepMode.RandomCrop;
            radioYolo.Checked = mode == ResolutionPrepMode.YoloPerson;
            if (!radioScale.Checked && !radioCenter.Checked && !radioSplit.Checked
                && !radioRandom.Checked && !radioYolo.Checked)
            {
                radioScale.Checked = true;
            }

            int aspectW = settings == null || settings.ResolutionPrepAspectWidth <= 0 ? 1 : settings.ResolutionPrepAspectWidth;
            int aspectH = settings == null || settings.ResolutionPrepAspectHeight <= 0 ? 1 : settings.ResolutionPrepAspectHeight;
            int presetIndex = -1;
            for (int i = 0; i < comboAspect.Items.Count; i++)
            {
                if (comboAspect.Items[i] is AspectItem item && !item.Custom && item.Width == aspectW && item.Height == aspectH)
                {
                    presetIndex = i;
                    break;
                }
            }
            if (presetIndex >= 0)
            {
                comboAspect.SelectedIndex = presetIndex;
            }
            else
            {
                comboAspect.SelectedIndex = comboAspect.Items.Count - 1;
                numAspectW.Value = Math.Clamp(aspectW, 1, 64);
                numAspectH.Value = Math.Clamp(aspectH, 1, 64);
            }

            if (settings?.ResolutionPrepCustomGears != null)
            {
                foreach (int gear in settings.ResolutionPrepCustomGears)
                {
                    int? normalized = ResolutionPrepMath.TryNormalizeGear(gear);
                    if (normalized.HasValue && !customGears.Contains(normalized.Value))
                        customGears.Add(normalized.Value);
                }
            }

            var selected = new HashSet<int>(
                ResolutionPrepMath.NormalizeSelectedGears(settings?.ResolutionPrepSelectedGears));
            if (selected.Count == 0)
                selected.Add(1024);
            RebuildGearChecks(selected);

            chkSharpen.Checked = settings?.ResolutionPrepSharpen ?? true;
            numRandomCount.Value = ResolutionPrepMath.ClampRandomCount(settings?.ResolutionPrepRandomCount ?? 1);
            YoloDetectorModelEntry yolo = YoloDetectorCatalog.ResolveInitial(
                settings?.YoloPersonModelId,
                settings?.YoloPersonImportPath);
            SelectYoloModel(yolo.Id);
            SelectEnum(comboYoloDownloadSource, settings?.Wd14Tagger?.DownloadSource
                ?? HuggingFaceDownloadSource.HfMirror);
            decimal confidence = (decimal)(settings?.YoloPersonConfidence ?? yolo.DefaultConfidence);
            numYoloConfidence.Value = Math.Clamp(confidence, numYoloConfidence.Minimum, numYoloConfidence.Maximum);
        }

        private void PersistSettings()
        {
            if (Program.Settings == null)
                return;
            GetAspect(out int aspectW, out int aspectH);
            Program.Settings.ResolutionPrepMode = CurrentMode();
            Program.Settings.ResolutionPrepSource = CurrentSource();
            Program.Settings.ResolutionPrepAspectWidth = aspectW;
            Program.Settings.ResolutionPrepAspectHeight = aspectH;
            Program.Settings.ResolutionPrepSelectedGears = GetCheckedGears().ToList();
            Program.Settings.ResolutionPrepCustomGears = customGears.ToList();
            Program.Settings.ResolutionPrepSharpen = chkSharpen.Checked;
            Program.Settings.ResolutionPrepRandomCount = (int)numRandomCount.Value;
            Program.Settings.YoloPersonConfidence = (float)numYoloConfidence.Value;
            Program.Settings.YoloPersonModelId = GetSelectedYoloModel().Id;
            if (Program.Settings.Wd14Tagger != null)
                Program.Settings.Wd14Tagger.DownloadSource = GetSelectedYoloDownloadSource();
            Program.Settings.SaveSettings();
        }

        private void RebuildGearChecks(HashSet<int> selected)
        {
            flowGears.Controls.Clear();
            foreach (int gear in ResolutionPrepMath.MergeGears(customGears))
            {
                var check = new CheckBox
                {
                    Text = gear.ToString(),
                    Tag = gear,
                    AutoSize = true,
                    Checked = selected.Contains(gear),
                    Margin = new Padding(0, LogicalToDeviceUnits(2), 0, LogicalToDeviceUnits(2))
                };
                check.CheckedChanged += (_, _) => UpdateStatus();
                flowGears.Controls.Add(check);
            }
        }

        private void AddCustomGear()
        {
            int? gear = ResolutionPrepMath.TryNormalizeGear((int)numCustomGear.Value);
            if (!gear.HasValue)
            {
                MessageBox.Show(this, I18n.GetText("ResolutionPrepAddGearInvalid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selected = new HashSet<int>(GetCheckedGears()) { gear.Value };
            if (!customGears.Contains(gear.Value)
                && Array.IndexOf(ResolutionPrepMath.DefaultGears, gear.Value) < 0)
            {
                customGears.Add(gear.Value);
            }
            RebuildGearChecks(selected);
            UpdateStatus();
        }

        private List<int> GetCheckedGears()
        {
            var gears = new List<int>();
            foreach (Control control in flowGears.Controls)
            {
                if (control is CheckBox check && check.Checked && check.Tag is int gear)
                    gears.Add(gear);
            }
            return gears;
        }

        private ResolutionPrepMode CurrentMode()
        {
            if (radioCenter.Checked)
                return ResolutionPrepMode.CenterCrop;
            if (radioSplit.Checked)
                return ResolutionPrepMode.SplitTiles;
            if (radioRandom.Checked)
                return ResolutionPrepMode.RandomCrop;
            if (radioYolo.Checked)
                return ResolutionPrepMode.YoloPerson;
            return ResolutionPrepMode.ScaleOnly;
        }

        private ResolutionPrepSource CurrentSource()
        {
            if (radioSourceFolder.Checked)
                return ResolutionPrepSource.Folder;
            if (radioSourceAllImages.Checked)
                return ResolutionPrepSource.AllImages;
            return ResolutionPrepSource.Selected;
        }

        private void ComboAspect_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool needsAspect = CurrentMode() != ResolutionPrepMode.ScaleOnly;
            bool custom = comboAspect.SelectedItem is AspectItem item && item.Custom;
            numAspectW.Enabled = custom && needsAspect;
            numAspectH.Enabled = custom && needsAspect;
            if (!custom && comboAspect.SelectedItem is AspectItem preset)
            {
                numAspectW.Value = preset.Width;
                numAspectH.Value = preset.Height;
            }
            UpdateStatus();
        }

        private void OnOptionsChanged()
        {
            bool needsAspect = CurrentMode() != ResolutionPrepMode.ScaleOnly;
            comboAspect.Enabled = needsAspect;
            bool custom = comboAspect.SelectedItem is AspectItem item && item.Custom;
            numAspectW.Enabled = needsAspect && custom;
            numAspectH.Enabled = needsAspect && custom;
            bool random = CurrentMode() == ResolutionPrepMode.RandomCrop;
            bool yolo = CurrentMode() == ResolutionPrepMode.YoloPerson;
            labelRandomCount.Enabled = random;
            numRandomCount.Enabled = random;
            labelYoloConfidence.Enabled = yolo;
            numYoloConfidence.Enabled = yolo;
            buttonImportYolo.Enabled = yolo;
            labelYoloModel.Enabled = yolo;
            comboYoloModel.Enabled = yolo;
            labelYoloDownloadSource.Enabled = yolo;
            comboYoloDownloadSource.Enabled = yolo;
            buttonDownloadYolo.Enabled = yolo && GetSelectedYoloModel().Kind != YoloDetectorKind.Import;
            labelYoloModelStatus.Enabled = yolo;
            UpdateStatus();
        }

        private void GetAspect(out int width, out int height)
        {
            if (comboAspect.SelectedItem is AspectItem item && !item.Custom)
            {
                width = item.Width;
                height = item.Height;
                return;
            }
            width = (int)numAspectW.Value;
            height = (int)numAspectH.Value;
        }

        private IReadOnlyList<string> TargetPaths()
        {
            List<string> selected = owner.GetSelectedDatasetImagePaths();
            List<string> folder = GetFolderImagePaths();
            List<string> all = GetAllImagePaths();
            return ResolutionPrepMath.ResolveSourcePaths(CurrentSource(), selected, folder, all);
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
            size = ResolutionPrepService.TryGetImageSize(path);
            sizeCache[path] = size;
            return size;
        }

        private ResolutionPrepRequest CurrentRequest()
        {
            GetAspect(out int aspectW, out int aspectH);
            return new ResolutionPrepRequest
            {
                Mode = CurrentMode(),
                AspectWidth = aspectW,
                AspectHeight = aspectH,
                Gears = GetCheckedGears(),
                RandomCount = (int)numRandomCount.Value
            };
        }

        private ResolutionPrepPlan BuildPlan()
        {
            var items = TargetPaths().Select(path => (path, SizeOf(path)));
            return ResolutionPrepMath.Plan(items, CurrentRequest());
        }

        private void UpdateStatus()
        {
            if (running)
                return;
            IReadOnlyList<int> gears = GetCheckedGears();
            if (gears.Count == 0)
            {
                labelStatus.Text = I18n.GetText("ResolutionPrepNoGears");
                return;
            }

            if (CurrentMode() == ResolutionPrepMode.YoloPerson)
            {
                int count = TargetPaths().Count;
                labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepYoloStatus"), count);
                return;
            }

            ResolutionPrepPlan plan = BuildPlan();
            labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepStatus"), plan.ImageCount - plan.SkippedImages, plan.Jobs.Count);
            if (plan.SkippedImages > 0 || plan.SkippedGears > 0)
            {
                labelStatus.Text += Environment.NewLine
                    + string.Format(I18n.GetText("ResolutionPrepSkipped"), plan.SkippedImages + plan.SkippedGears);
            }
        }

        private void ImportYoloModel()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "ONNX (*.onnx)|*.onnx",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                string dest = YoloPersonDetectorService.ImportOnnx(dialog.FileName);
                if (Program.Settings != null)
                {
                    Program.Settings.YoloPersonImportPath = dest;
                    Program.Settings.YoloPersonModelId = YoloDetectorCatalog.ImportId;
                    Program.Settings.SaveSettings();
                }
                loadingSettings = true;
                try
                {
                    SelectYoloModel(YoloDetectorCatalog.ImportId);
                }
                finally
                {
                    loadingSettings = false;
                }
                labelStatus.Text = dest;
                UpdateYoloModelStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task RunAsync()
        {
            if (running)
                return;
            if (GetCheckedGears().Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("ResolutionPrepNoGears"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PersistSettings();
            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            var created = new List<string>();
            int failed = 0;
            try
            {
                CancellationToken token = runCancellation.Token;
                ResolutionPrepPlan plan;
                if (CurrentMode() == ResolutionPrepMode.YoloPerson)
                    plan = await BuildYoloPlanAsync(token).ConfigureAwait(true);
                else
                    plan = BuildPlan();

                if (plan.Jobs.Count == 0)
                {
                    string empty = CurrentMode() == ResolutionPrepMode.YoloPerson
                        ? I18n.GetText("ResolutionPrepYoloNone")
                        : I18n.GetText("ResolutionPrepNoJobs");
                    MessageBox.Show(this, empty, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ResolutionPrepService.AssignOutputPaths(plan.Jobs);
                IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(tuple =>
                {
                    labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepProgress"), tuple.Done, tuple.Total);
                });
                string[] tagExtensions = Program.Settings?.GetTagFilesExtensions();
                bool sharpen = chkSharpen.Checked;
                await Task.Run(() =>
                {
                    for (int i = 0; i < plan.Jobs.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string written = ResolutionPrepService.TryWrite(plan.Jobs[i], sharpen, tagExtensions);
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
                // Keep whatever was already written.
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
                    UpdateStatus();
                    if (failed > 0)
                    {
                        MessageBox.Show(
                            this,
                            string.Format(I18n.GetText("ResolutionPrepPartial"), created.Count, failed),
                            Text,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private async Task<ResolutionPrepPlan> BuildYoloPlanAsync(CancellationToken token)
        {
            await EnsureYoloModelAsync(token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            string modelPath = yoloService.ResolveModelPath(GetSelectedYoloModel(), Program.Settings?.YoloPersonImportPath);
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                throw new FileNotFoundException(I18n.GetText("YoloDetectNoModel"));
            yoloService.LoadModel(modelPath);

            GetAspect(out int aspectW, out int aspectH);
            float confidence = (float)numYoloConfidence.Value;
            var crops = new List<(string Path, Size Size, IReadOnlyList<Rectangle> Crops)>();
            IReadOnlyList<string> paths = TargetPaths();
            IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(tuple =>
            {
                labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepProgress"), tuple.Done, tuple.Total);
            });

            await Task.Run(() =>
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string path = paths[i];
                    Size? size = SizeOf(path);
                    if (size == null)
                    {
                        progress.Report((i + 1, paths.Count));
                        continue;
                    }

                    List<(Rectangle Box, float Score)> detections = yoloService.Detect(path, confidence);
                    var rects = new List<Rectangle>();
                    foreach ((Rectangle box, float _) in detections)
                    {
                        Rectangle expanded = YoloDetectionMath.ExpandToAspect(box, size.Value, aspectW, aspectH);
                        if (expanded.Width > 0 && expanded.Height > 0)
                            rects.Add(expanded);
                    }
                    crops.Add((path, size.Value, rects));
                    progress.Report((i + 1, paths.Count));
                }
            }, token).ConfigureAwait(true);

            return ResolutionPrepMath.PlanFromCrops(crops, CurrentRequest());
        }

        private async Task EnsureYoloModelAsync(CancellationToken token)
        {
            YoloDetectorModelEntry entry = GetSelectedYoloModel();
            string import = Program.Settings?.YoloPersonImportPath;
            if (yoloService.IsModelReady(entry, import))
                return;
            if (entry.Kind == YoloDetectorKind.Import)
                throw new FileNotFoundException(I18n.GetText("YoloDetectImportMissing"));

            labelStatus.Text = I18n.GetText("ResolutionPrepDownloading");
            var progress = new Progress<(string file, long downloaded, long? total)>(report =>
            {
                labelStatus.Text = FormatYoloDownloadProgress(report.file, report.downloaded, report.total);
            });
            await yoloService.DownloadModelAsync(entry, GetSelectedYoloDownloadSource(), progress, token)
                .ConfigureAwait(true);
            UpdateYoloModelStatus();
        }

        private async Task DownloadSelectedYoloModelAsync()
        {
            if (running || CurrentMode() != ResolutionPrepMode.YoloPerson)
                return;
            YoloDetectorModelEntry entry = GetSelectedYoloModel();
            if (entry.Kind == YoloDetectorKind.Import)
            {
                ImportYoloModel();
                return;
            }

            PersistSettings();
            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            try
            {
                await EnsureYoloModelAsync(runCancellation.Token).ConfigureAwait(true);
                labelStatus.Text = I18n.GetText("TaggerModelReady");
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
                if (closeAfterRun)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else
                {
                    SetUiLocked(false);
                    UpdateStatus();
                }
            }
        }

        private void OnSelectedYoloModelChanged()
        {
            if (!loadingSettings)
            {
                YoloDetectorModelEntry entry = GetSelectedYoloModel();
                numYoloConfidence.Value = Math.Clamp(
                    (decimal)entry.DefaultConfidence,
                    numYoloConfidence.Minimum,
                    numYoloConfidence.Maximum);
                PersistSettings();
            }
            UpdateYoloModelStatus();
        }

        private void SelectYoloModel(string modelId)
        {
            for (int i = 0; i < comboYoloModel.Items.Count; i++)
            {
                if (comboYoloModel.Items[i] is YoloDetectorModelEntry entry
                    && string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    comboYoloModel.SelectedIndex = i;
                    return;
                }
            }

            if (comboYoloModel.Items.Count > 0)
                comboYoloModel.SelectedIndex = 0;
        }

        private YoloDetectorModelEntry GetSelectedYoloModel()
        {
            if (comboYoloModel.SelectedItem is YoloDetectorModelEntry entry)
                return entry;
            return YoloDetectorCatalog.Default;
        }

        private HuggingFaceDownloadSource GetSelectedYoloDownloadSource()
        {
            return GetSelectedEnum<HuggingFaceDownloadSource>(comboYoloDownloadSource);
        }

        private static T GetSelectedEnum<T>(ComboBox combo) where T : struct, Enum
        {
            Array values = Enum.GetValues(typeof(T));
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < values.Length)
                return (T)values.GetValue(combo.SelectedIndex);

            return (T)values.GetValue(0);
        }

        private static void SelectEnum<T>(ComboBox combo, T value) where T : struct, Enum
        {
            int index = Extensions.GetEnumIndexFromValue<T>(value.ToString());
            combo.SelectedIndex = index >= 0 ? index : 0;
        }

        private void UpdateYoloModelStatus()
        {
            YoloDetectorModelEntry entry = GetSelectedYoloModel();
            string import = Program.Settings?.YoloPersonImportPath;
            bool ready = yoloService.IsModelReady(entry, import);
            if (entry.Kind == YoloDetectorKind.Import)
            {
                labelYoloModelStatus.Text = ready
                    ? string.Format(I18n.GetText("YoloDetectImportReady"), Path.GetFileName(import))
                    : I18n.GetText("YoloDetectImportMissing");
            }
            else
            {
                labelYoloModelStatus.Text = ready
                    ? I18n.GetText("TaggerModelReady")
                    : I18n.GetText("TaggerModelMissing");
            }
            bool yolo = !running && CurrentMode() == ResolutionPrepMode.YoloPerson;
            buttonDownloadYolo.Enabled = yolo && entry.Kind != YoloDetectorKind.Import;
        }

        private static string FormatYoloDownloadProgress(string fileName, long downloaded, long? total)
        {
            if (total.HasValue && total.Value > 0)
            {
                int percent = (int)Math.Clamp(Math.Round(downloaded * 100.0 / total.Value), 0, 100);
                return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, percent);
            }

            return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, 0);
        }

        private void SetUiLocked(bool locked)
        {
            radioSourceSelected.Enabled = !locked;
            radioSourceFolder.Enabled = !locked && folderSourceAvailable;
            radioSourceAllImages.Enabled = !locked;
            radioScale.Enabled = !locked;
            radioCenter.Enabled = !locked;
            radioSplit.Enabled = !locked;
            radioRandom.Enabled = !locked;
            radioYolo.Enabled = !locked;
            bool needsAspect = CurrentMode() != ResolutionPrepMode.ScaleOnly;
            comboAspect.Enabled = !locked && needsAspect;
            bool custom = comboAspect.SelectedItem is AspectItem item && item.Custom;
            numAspectW.Enabled = !locked && custom && needsAspect;
            numAspectH.Enabled = !locked && custom && needsAspect;
            numRandomCount.Enabled = !locked && CurrentMode() == ResolutionPrepMode.RandomCrop;
            numYoloConfidence.Enabled = !locked && CurrentMode() == ResolutionPrepMode.YoloPerson;
            buttonImportYolo.Enabled = !locked && CurrentMode() == ResolutionPrepMode.YoloPerson;
            bool yolo = !locked && CurrentMode() == ResolutionPrepMode.YoloPerson;
            labelYoloModel.Enabled = yolo;
            comboYoloModel.Enabled = yolo;
            labelYoloDownloadSource.Enabled = yolo;
            comboYoloDownloadSource.Enabled = yolo;
            buttonDownloadYolo.Enabled = yolo && GetSelectedYoloModel().Kind != YoloDetectorKind.Import;
            labelYoloModelStatus.Enabled = yolo;
            flowGears.Enabled = !locked;
            numCustomGear.Enabled = !locked;
            buttonAddGear.Enabled = !locked;
            chkSharpen.Enabled = !locked;
            buttonStart.Enabled = !locked;
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
                yoloService.Dispose();
            }
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                ButtonCancel_Click(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private sealed class AspectItem
        {
            public AspectItem(string text, int width, int height, bool custom)
            {
                Text = text;
                Width = width;
                Height = height;
                Custom = custom;
            }

            public string Text { get; }
            public int Width { get; }
            public int Height { get; }
            public bool Custom { get; }

            public override string ToString() => Text;
        }
    }
}
