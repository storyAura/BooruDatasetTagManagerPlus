using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Standalone YOLO detect review: draw boxes, keep/drop, optional
    /// ONNX tags, then export through the multi-crop writer.
    /// </summary>
    public sealed class Form_YoloDetect : Form
    {
        private readonly MainForm owner;
        private readonly bool folderSourceAvailable =
            !string.IsNullOrEmpty(Program.DataManager?.ActiveFolder);
        private readonly YoloPersonDetectorService yoloService = new YoloPersonDetectorService();
        private readonly Wd14OnnxTaggerService wd14Service = new Wd14OnnxTaggerService();
        private readonly PixAiOnnxTaggerService pixAiService = new PixAiOnnxTaggerService();
        private readonly ClTaggerOnnxService clService = new ClTaggerOnnxService();
        private readonly Dictionary<string, Size?> sizeCache = new Dictionary<string, Size?>(StringComparer.OrdinalIgnoreCase);

        private readonly RadioButton radioSourceSelected = new RadioButton();
        private readonly RadioButton radioSourceFolder = new RadioButton();
        private readonly RadioButton radioSourceAllImages = new RadioButton();
        private readonly ListBox listImages = new ListBox();
        private readonly Panel canvas = new Panel();
        private readonly CheckedListBox listBoxes = new CheckedListBox();
        private readonly TextBox textTags = new TextBox();
        private readonly ComboBox comboAspect = new ComboBox();
        private readonly ComboBox comboModel = new ComboBox();
        private readonly ComboBox comboDownloadSource = new ComboBox();
        private readonly NumericUpDown numYoloConfidence = new NumericUpDown();
        private readonly Label labelModelStatus = new Label();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonDetect = new Button();
        private readonly Button buttonOnnx = new Button();
        private readonly Button buttonOpenOnnx = new Button();
        private readonly Button buttonExport = new Button();
        private readonly Button buttonImport = new Button();
        private readonly Button buttonDownload = new Button();
        private readonly Button buttonCancel = new Button();

        private readonly List<ImageDetection> detections = new List<ImageDetection>();
        private System.Drawing.Image previewImage;
        private bool running;
        private bool closeAfterRun;
        private bool loadingSettings;
        private CancellationTokenSource runCancellation;
        private bool syncingChecks;

        public IReadOnlyList<string> NewFilePaths { get; private set; } = Array.Empty<string>();

        public Form_YoloDetect(MainForm owner, ResolutionPrepSource? source = null)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            MinimumSize = new Size(LogicalToDeviceUnits(820), LogicalToDeviceUnits(560));
            ClientSize = new Size(LogicalToDeviceUnits(960), LogicalToDeviceUnits(640));
            Text = I18n.GetText("YoloDetectTitle");
            Padding = new Padding(LogicalToDeviceUnits(10));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildSourceGroup(), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildOptionsPanel(), 0, 2);
            root.Controls.Add(BuildButtons(), 0, 3);
            Controls.Add(root);

            LoadAspectPresets();
            LoadSettings();
            if (source.HasValue)
                SelectSource(source.Value);
            else
                ApplyAutoSource();
            UpdateStatus();

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
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true
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

        private TableLayoutPanel BuildBody()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, LogicalToDeviceUnits(6), 0, LogicalToDeviceUnits(6))
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(220)));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(240)));

            listImages.Dock = DockStyle.Fill;
            listImages.DisplayMember = nameof(ImageDetection.DisplayName);
            listImages.SelectedIndexChanged += (_, _) => ShowSelectedImage();
            body.Controls.Add(listImages, 0, 0);

            canvas.Dock = DockStyle.Fill;
            canvas.BackColor = System.Drawing.Color.Black;
            canvas.Paint += Canvas_Paint;
            canvas.Resize += (_, _) => canvas.Invalidate();
            body.Controls.Add(canvas, 1, 0);

            var side = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
            listBoxes.Dock = DockStyle.Fill;
            listBoxes.CheckOnClick = true;
            listBoxes.ItemCheck += ListBoxes_ItemCheck;
            var labelTags = new Label
            {
                Text = I18n.GetText("YoloDetectTags"),
                AutoSize = true,
                Dock = DockStyle.Top
            };
            textTags.Dock = DockStyle.Fill;
            textTags.Multiline = true;
            textTags.ReadOnly = true;
            textTags.ScrollBars = ScrollBars.Vertical;
            side.Controls.Add(listBoxes, 0, 0);
            side.Controls.Add(labelTags, 0, 1);
            side.Controls.Add(textTags, 0, 2);
            body.Controls.Add(side, 2, 0);
            return body;
        }

        private TableLayoutPanel BuildOptionsPanel()
        {
            var panel = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(BuildModelRow(), 0, 0);
            panel.Controls.Add(BuildAspectRow(), 0, 1);
            return panel;
        }

        private FlowLayoutPanel BuildModelRow()
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true
            };
            var labelModel = new Label
            {
                Text = I18n.GetText("YoloDetectModel"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            comboModel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboModel.Width = LogicalToDeviceUnits(220);
            foreach (YoloDetectorModelEntry model in YoloDetectorCatalog.AllModels)
                comboModel.Items.Add(model);
            comboModel.SelectedIndexChanged += (_, _) => OnSelectedModelChanged();

            var labelSource = new Label
            {
                Text = I18n.GetText("TaggerDownloadSource"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            comboDownloadSource.DropDownStyle = ComboBoxStyle.DropDownList;
            comboDownloadSource.Width = LogicalToDeviceUnits(160);
            comboDownloadSource.Items.AddRange(Extensions.GetFriendlyEnumValues<HuggingFaceDownloadSource>());
            comboDownloadSource.SelectedIndexChanged += (_, _) =>
            {
                if (!loadingSettings)
                    PersistSettings();
            };

            buttonDownload.Text = I18n.GetText("TaggerDownloadModel");
            buttonDownload.AutoSize = true;
            buttonDownload.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            buttonDownload.Click += async (_, _) => await DownloadSelectedModelAsync();

            labelModelStatus.AutoSize = true;
            labelModelStatus.Anchor = AnchorStyles.Left;
            labelModelStatus.Padding = new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(6), 0, 0);

            row.Controls.Add(labelModel);
            row.Controls.Add(comboModel);
            row.Controls.Add(labelSource);
            row.Controls.Add(comboDownloadSource);
            row.Controls.Add(buttonDownload);
            row.Controls.Add(labelModelStatus);
            return row;
        }

        private FlowLayoutPanel BuildAspectRow()
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = true
            };
            var labelAspect = new Label
            {
                Text = I18n.GetText("ResolutionPrepAspect"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            comboAspect.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAspect.Width = LogicalToDeviceUnits(140);
            var labelConf = new Label
            {
                Text = I18n.GetText("ResolutionPrepYoloConfidence"),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            numYoloConfidence.DecimalPlaces = 2;
            numYoloConfidence.Increment = 0.05m;
            numYoloConfidence.Minimum = 0.05m;
            numYoloConfidence.Maximum = 1.00m;
            numYoloConfidence.Value = 0.30m;
            numYoloConfidence.Width = LogicalToDeviceUnits(60);
            labelStatus.AutoSize = true;
            labelStatus.Anchor = AnchorStyles.Left;
            labelStatus.Padding = new Padding(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6), 0, 0);
            row.Controls.Add(labelAspect);
            row.Controls.Add(comboAspect);
            row.Controls.Add(labelConf);
            row.Controls.Add(numYoloConfidence);
            row.Controls.Add(labelStatus);
            return row;
        }

        private FlowLayoutPanel BuildButtons()
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            buttonDetect.Text = I18n.GetText("YoloDetectRun");
            buttonOnnx.Text = I18n.GetText("YoloDetectOnnx");
            buttonOpenOnnx.Text = I18n.GetText("YoloDetectOpenOnnx");
            buttonExport.Text = I18n.GetText("YoloDetectExport");
            buttonImport.Text = I18n.GetText("YoloDetectImport");
            buttonCancel.Text = I18n.GetText("ResolutionPrepCancel");
            foreach (Button button in new[] { buttonDetect, buttonOnnx, buttonOpenOnnx, buttonExport, buttonImport, buttonCancel })
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(LogicalToDeviceUnits(90), LogicalToDeviceUnits(28));
            }
            buttonDetect.Click += async (_, _) => await RunDetectAsync();
            buttonOnnx.Click += async (_, _) => await RunOnnxAsync();
            buttonOpenOnnx.Click += (_, _) => OpenOnnxTagger();
            buttonExport.Click += async (_, _) => await RunExportAsync();
            buttonImport.Click += (_, _) => ImportYoloModel();
            buttonCancel.Click += ButtonCancel_Click;
            row.Controls.AddRange(new Control[]
            {
                buttonDetect, buttonOnnx, buttonOpenOnnx, buttonExport, buttonImport, buttonCancel
            });
            return row;
        }

        private void LoadAspectPresets()
        {
            comboAspect.Items.Clear();
            foreach ((int width, int height) in BatchCropMath.Presets)
                comboAspect.Items.Add(new AspectItem($"{width}:{height}", width, height));
            comboAspect.DisplayMember = nameof(AspectItem.Text);
            comboAspect.SelectedIndex = 0;
        }

        private void LoadSettings()
        {
            AppSettings settings = Program.Settings;
            loadingSettings = true;
            try
            {
                YoloDetectorModelEntry entry = YoloDetectorCatalog.ResolveInitial(
                    settings?.YoloPersonModelId,
                    settings?.YoloPersonImportPath);
                SelectModel(entry.Id);
                SelectEnum(comboDownloadSource, settings?.Wd14Tagger?.DownloadSource
                    ?? HuggingFaceDownloadSource.HfMirror);
                decimal confidence = (decimal)(settings?.YoloPersonConfidence ?? entry.DefaultConfidence);
                numYoloConfidence.Value = Math.Clamp(confidence, numYoloConfidence.Minimum, numYoloConfidence.Maximum);
                int aspectW = settings == null || settings.ResolutionPrepAspectWidth <= 0 ? 1 : settings.ResolutionPrepAspectWidth;
                int aspectH = settings == null || settings.ResolutionPrepAspectHeight <= 0 ? 1 : settings.ResolutionPrepAspectHeight;
                for (int i = 0; i < comboAspect.Items.Count; i++)
                {
                    if (comboAspect.Items[i] is AspectItem item && item.Width == aspectW && item.Height == aspectH)
                    {
                        comboAspect.SelectedIndex = i;
                        break;
                    }
                }
            }
            finally
            {
                loadingSettings = false;
            }
            UpdateModelStatus();
        }

        private void PersistSettings()
        {
            if (Program.Settings == null)
                return;
            GetAspect(out int aspectW, out int aspectH);
            Program.Settings.ResolutionPrepAspectWidth = aspectW;
            Program.Settings.ResolutionPrepAspectHeight = aspectH;
            Program.Settings.YoloPersonConfidence = (float)numYoloConfidence.Value;
            Program.Settings.YoloPersonModelId = GetSelectedModel().Id;
            if (Program.Settings.Wd14Tagger != null)
                Program.Settings.Wd14Tagger.DownloadSource = GetSelectedDownloadSource();
            Program.Settings.SaveSettings();
        }

        private ResolutionPrepSource CurrentSource()
        {
            if (radioSourceFolder.Checked)
                return ResolutionPrepSource.Folder;
            if (radioSourceAllImages.Checked)
                return ResolutionPrepSource.AllImages;
            return ResolutionPrepSource.Selected;
        }

        private IReadOnlyList<string> TargetPaths()
        {
            List<string> selected = owner.GetSelectedDatasetImagePaths();
            List<string> folder = FilterImagePaths(
                (Program.DataManager?.GetScopedItems() ?? new List<DatasetManager.DataItem>())
                    .Select(item => item.ImageFilePath));
            List<string> all = FilterImagePaths(
                (Program.DataManager?.DataSet.Values ?? Array.Empty<DatasetManager.DataItem>())
                    .Select(item => item.ImageFilePath));
            return ResolutionPrepMath.ResolveSourcePaths(CurrentSource(), selected, folder, all);
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

        private void GetAspect(out int width, out int height)
        {
            if (comboAspect.SelectedItem is AspectItem item)
            {
                width = item.Width;
                height = item.Height;
                return;
            }
            width = 1;
            height = 1;
        }

        private void UpdateStatus()
        {
            if (running)
                return;
            int people = detections.Sum(item => item.Boxes.Count);
            if (detections.Count == 0)
                labelStatus.Text = string.Format(I18n.GetText("YoloDetectStatus"), TargetPaths().Count);
            else
                labelStatus.Text = string.Format(I18n.GetText("YoloDetectDone"), people, detections.Count);
        }

        private ImageDetection CurrentDetection()
        {
            return listImages.SelectedItem as ImageDetection;
        }

        private void ShowSelectedImage()
        {
            ImageDetection item = CurrentDetection();
            previewImage?.Dispose();
            previewImage = null;
            syncingChecks = true;
            listBoxes.Items.Clear();
            textTags.Text = item?.Tags ?? string.Empty;
            if (item != null)
            {
                previewImage = ImageLoader.GetImageFromFile(item.Path);
                for (int i = 0; i < item.Boxes.Count; i++)
                {
                    BoxItem box = item.Boxes[i];
                    listBoxes.Items.Add($"{i + 1}  {box.Score:P0}", box.Keep);
                }
            }
            syncingChecks = false;
            canvas.Invalidate();
        }

        private void ListBoxes_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (syncingChecks)
                return;
            ImageDetection item = CurrentDetection();
            if (item == null || e.Index < 0 || e.Index >= item.Boxes.Count)
                return;
            item.Boxes[e.Index].Keep = e.NewValue == CheckState.Checked;
            canvas.Invalidate();
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(canvas.BackColor);
            if (previewImage == null)
                return;

            var imageSize = previewImage.Size;
            var viewport = canvas.ClientSize;
            Rectangle dest = CropCanvasHelper.CalcImageLocation(imageSize, viewport);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(previewImage, dest);

            ImageDetection item = CurrentDetection();
            if (item == null)
                return;
            for (int i = 0; i < item.Boxes.Count; i++)
            {
                BoxItem box = item.Boxes[i];
                Rectangle screen = CropCanvasHelper.ImageRectToScreenRect(box.Box, imageSize, viewport);
                System.Drawing.Color color = box.Keep
                    ? CropCanvasHelper.RegionColors[i % CropCanvasHelper.RegionColors.Length]
                    : System.Drawing.Color.FromArgb(160, 180, 180, 180);
                using var pen = new System.Drawing.Pen(color, LogicalToDeviceUnits(2));
                e.Graphics.DrawRectangle(pen, screen);
                using var font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
                using var brush = new SolidBrush(color);
                e.Graphics.DrawString((i + 1).ToString(), font, brush, screen.Location);
            }
        }

        private async Task RunDetectAsync()
        {
            if (running)
                return;
            IReadOnlyList<string> paths = TargetPaths();
            if (paths.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TaggerNoImages"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PersistSettings();
            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            try
            {
                CancellationToken token = runCancellation.Token;
                await EnsureYoloModelAsync(token).ConfigureAwait(true);
                token.ThrowIfCancellationRequested();
                string modelPath = yoloService.ResolveModelPath(GetSelectedModel(), Program.Settings?.YoloPersonImportPath);
                if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                    throw new FileNotFoundException(I18n.GetText("YoloDetectNoModel"));
                yoloService.LoadModel(modelPath);

                float confidence = (float)numYoloConfidence.Value;
                var found = new List<ImageDetection>();
                IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(tuple =>
                {
                    labelStatus.Text = string.Format(I18n.GetText("YoloDetectProgress"), tuple.Done, tuple.Total);
                });

                await Task.Run(() =>
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string path = paths[i];
                        Size? size = SizeOf(path);
                        var boxes = new List<BoxItem>();
                        if (size != null)
                        {
                            foreach ((Rectangle box, float score) in yoloService.Detect(path, confidence))
                                boxes.Add(new BoxItem { Box = box, Score = score, Keep = true });
                        }
                        found.Add(new ImageDetection
                        {
                            Path = path,
                            Size = size ?? Size.Empty,
                            Boxes = boxes
                        });
                        progress.Report((i + 1, paths.Count));
                    }
                }, token).ConfigureAwait(true);

                detections.Clear();
                detections.AddRange(found);
                listImages.DataSource = null;
                listImages.DataSource = detections;
                if (listImages.Items.Count > 0)
                    listImages.SelectedIndex = 0;
                if (detections.Sum(item => item.Boxes.Count) == 0)
                    MessageBox.Show(this, I18n.GetText("YoloDetectNone"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                FinishRun();
            }
        }

        private async Task RunOnnxAsync()
        {
            if (running)
                return;
            List<BoxItem> kept = KeptBoxes();
            if (kept.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("YoloDetectNoKeep"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            try
            {
                CancellationToken token = runCancellation.Token;
                if (!TryPrepareOnnxTagger(out string error))
                {
                    MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                await Task.Run(() =>
                {
                    foreach (ImageDetection item in detections)
                    {
                        token.ThrowIfCancellationRequested();
                        var tags = new List<string>();
                        foreach (BoxItem box in item.Boxes.Where(b => b.Keep))
                        {
                            string temp = WriteTempCrop(item.Path, box.Box);
                            if (temp == null)
                                continue;
                            try
                            {
                                IReadOnlyList<AutoTagProviderItem> result = TagCrop(temp);
                                tags.AddRange(result.Select(t => t.Tag));
                            }
                            finally
                            {
                                try { File.Delete(temp); } catch { }
                            }
                        }
                        item.Tags = string.Join(", ", tags.Distinct(StringComparer.OrdinalIgnoreCase));
                    }
                }, token).ConfigureAwait(true);
                ShowSelectedImage();
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
                FinishRun();
            }
        }

        private async Task RunExportAsync()
        {
            if (running)
                return;
            var crops = new List<(string Path, Size Size, IReadOnlyList<Rectangle> Crops)>();
            GetAspect(out int aspectW, out int aspectH);
            foreach (ImageDetection item in detections)
            {
                var rects = new List<Rectangle>();
                foreach (BoxItem box in item.Boxes.Where(b => b.Keep))
                {
                    Rectangle expanded = YoloDetectionMath.ExpandToAspect(box.Box, item.Size, aspectW, aspectH);
                    if (expanded.Width > 0 && expanded.Height > 0)
                        rects.Add(expanded);
                }
                crops.Add((item.Path, item.Size, rects));
            }

            var request = new ResolutionPrepRequest
            {
                Mode = ResolutionPrepMode.YoloPerson,
                AspectWidth = aspectW,
                AspectHeight = aspectH,
                Gears = ResolutionPrepMath.NormalizeSelectedGears(Program.Settings?.ResolutionPrepSelectedGears)
            };
            if (request.Gears.Count == 0)
                request = new ResolutionPrepRequest
                {
                    Mode = request.Mode,
                    AspectWidth = aspectW,
                    AspectHeight = aspectH,
                    Gears = new[] { 1024 }
                };

            ResolutionPrepPlan plan = ResolutionPrepMath.PlanFromCrops(crops, request);
            if (plan.Jobs.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("YoloDetectNoKeep"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PersistSettings();
            ResolutionPrepService.AssignOutputPaths(plan.Jobs);
            running = true;
            closeAfterRun = false;
            runCancellation = new CancellationTokenSource();
            SetUiLocked(true);
            var created = new List<string>();
            try
            {
                CancellationToken token = runCancellation.Token;
                string[] tagExtensions = Program.Settings?.GetTagFilesExtensions();
                bool sharpen = Program.Settings?.ResolutionPrepSharpen ?? true;
                IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(tuple =>
                {
                    labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepProgress"), tuple.Done, tuple.Total);
                });
                await Task.Run(() =>
                {
                    for (int i = 0; i < plan.Jobs.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        string written = ResolutionPrepService.TryWrite(plan.Jobs[i], sharpen, tagExtensions);
                        if (written != null)
                            lock (created)
                                created.Add(written);
                        progress.Report((i + 1, plan.Jobs.Count));
                    }
                }, token).ConfigureAwait(true);
                NewFilePaths = created;
                if (created.Count > 0)
                {
                    labelStatus.Text = string.Format(I18n.GetText("ResolutionPrepDone"), created.Count);
                }
            }
            catch (OperationCanceledException)
            {
                NewFilePaths = created;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                FinishRun();
            }
        }

        private void OpenOnnxTagger()
        {
            if (NewFilePaths.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("YoloDetectExportFirst"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            owner.ImportWrittenImages(NewFilePaths);
            owner.SelectDatasetImagesByPaths(NewFilePaths);
            using Form_OnnxTagger form = new Form_OnnxTagger(owner);
            form.ShowDialog(this);
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
                    SelectModel(YoloDetectorCatalog.ImportId);
                }
                finally
                {
                    loadingSettings = false;
                }
                labelStatus.Text = dest;
                UpdateModelStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task EnsureYoloModelAsync(CancellationToken token)
        {
            YoloDetectorModelEntry entry = GetSelectedModel();
            string import = Program.Settings?.YoloPersonImportPath;
            if (yoloService.IsModelReady(entry, import))
                return;
            if (entry.Kind == YoloDetectorKind.Import)
                throw new FileNotFoundException(I18n.GetText("YoloDetectImportMissing"));

            labelStatus.Text = I18n.GetText("ResolutionPrepDownloading");
            var progress = new Progress<(string file, long downloaded, long? total)>(report =>
            {
                labelStatus.Text = FormatDownloadProgress(report.file, report.downloaded, report.total);
            });
            await yoloService.DownloadModelAsync(entry, GetSelectedDownloadSource(), progress, token)
                .ConfigureAwait(true);
            UpdateModelStatus();
        }

        private async Task DownloadSelectedModelAsync()
        {
            if (running)
                return;
            YoloDetectorModelEntry entry = GetSelectedModel();
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
                FinishRun();
            }
        }

        private void OnSelectedModelChanged()
        {
            if (!loadingSettings)
            {
                YoloDetectorModelEntry entry = GetSelectedModel();
                numYoloConfidence.Value = Math.Clamp(
                    (decimal)entry.DefaultConfidence,
                    numYoloConfidence.Minimum,
                    numYoloConfidence.Maximum);
                PersistSettings();
            }
            UpdateModelStatus();
        }

        private void SelectModel(string modelId)
        {
            for (int i = 0; i < comboModel.Items.Count; i++)
            {
                if (comboModel.Items[i] is YoloDetectorModelEntry entry
                    && string.Equals(entry.Id, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    comboModel.SelectedIndex = i;
                    return;
                }
            }

            if (comboModel.Items.Count > 0)
                comboModel.SelectedIndex = 0;
        }

        private YoloDetectorModelEntry GetSelectedModel()
        {
            if (comboModel.SelectedItem is YoloDetectorModelEntry entry)
                return entry;
            return YoloDetectorCatalog.Default;
        }

        private HuggingFaceDownloadSource GetSelectedDownloadSource()
        {
            return GetSelectedEnum<HuggingFaceDownloadSource>(comboDownloadSource);
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

        private void UpdateModelStatus()
        {
            YoloDetectorModelEntry entry = GetSelectedModel();
            string import = Program.Settings?.YoloPersonImportPath;
            bool ready = yoloService.IsModelReady(entry, import);
            if (entry.Kind == YoloDetectorKind.Import)
            {
                labelModelStatus.Text = ready
                    ? string.Format(I18n.GetText("YoloDetectImportReady"), Path.GetFileName(import))
                    : I18n.GetText("YoloDetectImportMissing");
            }
            else
            {
                labelModelStatus.Text = ready
                    ? I18n.GetText("TaggerModelReady")
                    : I18n.GetText("TaggerModelMissing");
            }
            buttonDownload.Enabled = !running && entry.Kind != YoloDetectorKind.Import;
        }

        private static string FormatDownloadProgress(string fileName, long downloaded, long? total)
        {
            if (total.HasValue && total.Value > 0)
            {
                int percent = (int)Math.Clamp(Math.Round(downloaded * 100.0 / total.Value), 0, 100);
                return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, percent);
            }

            return string.Format(I18n.GetText("TaggerDownloadProgress"), fileName, 0);
        }

        private bool TryPrepareOnnxTagger(out string error)
        {
            error = I18n.GetText("YoloDetectNoOnnxModel");
            OnnxTaggerModelEntry entry = OnnxTaggerCatalog.GetById(Program.Settings?.OnnxTaggerLastModelId);
            if (entry == null)
                return false;
            switch (entry.Kind)
            {
                case OnnxTaggerModelKind.Wd14:
                    if (!wd14Service.IsModelReady(entry.Repo))
                        return false;
                    wd14Service.LoadModel(entry.Repo);
                    return true;
                case OnnxTaggerModelKind.PixAi:
                    if (!pixAiService.IsModelReady())
                        return false;
                    pixAiService.LoadModel();
                    return true;
                case OnnxTaggerModelKind.ClTagger:
                    if (entry.ClModel == null || !clService.IsModelReady(entry.ClModel))
                        return false;
                    clService.LoadModel(entry.ClModel);
                    return true;
                default:
                    return false;
            }
        }

        private IReadOnlyList<AutoTagProviderItem> TagCrop(string path)
        {
            OnnxTaggerModelEntry entry = OnnxTaggerCatalog.GetById(Program.Settings?.OnnxTaggerLastModelId);
            if (entry == null)
                return Array.Empty<AutoTagProviderItem>();
            switch (entry.Kind)
            {
                case OnnxTaggerModelKind.Wd14:
                    (double g, double c) = Program.Settings.Wd14Tagger.GetThresholdsForRepo(entry.Repo);
                    return wd14Service.TagImageWithTiming(path, g, c).Tags;
                case OnnxTaggerModelKind.PixAi:
                    return pixAiService.TagImageWithTiming(
                        path,
                        Program.Settings.PixAiTagger.GeneralThreshold,
                        Program.Settings.PixAiTagger.CharacterThreshold).Tags;
                case OnnxTaggerModelKind.ClTagger:
                    return clService.TagImageWithTiming(
                        path,
                        entry.DefaultThreshold,
                        entry.DefaultCharacterThreshold ?? entry.DefaultThreshold).Tags;
                default:
                    return Array.Empty<AutoTagProviderItem>();
            }
        }

        private static string WriteTempCrop(string sourcePath, Rectangle box)
        {
            try
            {
                using var image = SixLabors.ImageSharp.Image.Load(sourcePath);
                image.Mutate(context => context.AutoOrient());
                var bounds = SixLabors.ImageSharp.Rectangle.Intersect(
                    new SixLabors.ImageSharp.Rectangle(box.X, box.Y, box.Width, box.Height),
                    new SixLabors.ImageSharp.Rectangle(0, 0, image.Width, image.Height));
                if (bounds.Width < 1 || bounds.Height < 1)
                    return null;
                image.Mutate(context => context.Crop(bounds));
                string temp = Path.Combine(Path.GetTempPath(), "bdtm-yolo-" + Guid.NewGuid().ToString("N") + ".png");
                image.SaveAsPng(temp);
                return temp;
            }
            catch
            {
                return null;
            }
        }

        private List<BoxItem> KeptBoxes()
        {
            return detections.SelectMany(item => item.Boxes).Where(box => box.Keep).ToList();
        }

        private void FinishRun()
        {
            running = false;
            runCancellation?.Dispose();
            runCancellation = null;
            if (closeAfterRun)
            {
                DialogResult = NewFilePaths.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
                Close();
                return;
            }
            SetUiLocked(false);
            UpdateStatus();
        }

        private void SetUiLocked(bool locked)
        {
            radioSourceSelected.Enabled = !locked;
            radioSourceFolder.Enabled = !locked && folderSourceAvailable;
            radioSourceAllImages.Enabled = !locked;
            listImages.Enabled = !locked;
            listBoxes.Enabled = !locked;
            comboAspect.Enabled = !locked;
            comboModel.Enabled = !locked;
            comboDownloadSource.Enabled = !locked;
            numYoloConfidence.Enabled = !locked;
            buttonDetect.Enabled = !locked;
            buttonOnnx.Enabled = !locked;
            buttonOpenOnnx.Enabled = !locked;
            buttonExport.Enabled = !locked;
            buttonImport.Enabled = !locked;
            buttonDownload.Enabled = !locked && GetSelectedModel().Kind != YoloDetectorKind.Import;
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
        {
            if (running)
            {
                runCancellation?.Cancel();
                return;
            }
            DialogResult = NewFilePaths.Count > 0 ? DialogResult.OK : DialogResult.Cancel;
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
                previewImage?.Dispose();
                previewImage = null;
                yoloService.Dispose();
                wd14Service.Dispose();
                pixAiService.Dispose();
                clService.Dispose();
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

        private sealed class ImageDetection
        {
            public string Path { get; init; }
            public Size Size { get; init; }
            public List<BoxItem> Boxes { get; init; } = new List<BoxItem>();
            public string Tags { get; set; } = string.Empty;
            public string DisplayName => System.IO.Path.GetFileName(Path);
        }

        private sealed class BoxItem
        {
            public Rectangle Box { get; init; }
            public float Score { get; init; }
            public bool Keep { get; set; }
        }

        private sealed class AspectItem
        {
            public AspectItem(string text, int width, int height)
            {
                Text = text;
                Width = width;
                Height = height;
            }

            public string Text { get; }
            public int Width { get; }
            public int Height { get; }

            public override string ToString() => Text;
        }
    }
}
