using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// czkawka-style similar-image screening: hashes the loaded dataset
    /// (honoring the active folder scope), clusters near-duplicates and shows
    /// them as a review wall in the multi-select tag editor's visual language —
    /// green frame = keep, red frame = delete, right-click = full-size preview.
    /// Deletion routes through <see cref="MainForm.DeleteDatasetMediaFiles"/>
    /// (transactional file+sidecar delete, dataset removal, grid refresh).
    /// </summary>
    public sealed class Form_SimilarImages : Form
    {
        private readonly MainForm mainForm;
        private readonly ComboBox comboThreshold;
        private readonly Button btnScan;
        private readonly Button btnAutoKeep;
        private readonly Button btnDelete;
        private readonly Button btnClose;
        private readonly TrackBar trackZoom;
        private readonly Label labelStatus;
        private readonly FlowLayoutPanel flowResults;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Font headerFont;

        // One entry per displayed group, in display order (drives auto-keep
        // and the delete collection).
        private readonly List<List<CustomPictureBoxWithYN>> groupBoxes = new List<List<CustomPictureBoxWithYN>>();
        // Display thumbnails decoded by this form (datasets loaded without
        // previews); dataset-owned thumbnails are shared and never disposed here.
        private readonly List<Image> ownedImages = new List<Image>();
        private bool scanning;
        private CancellationTokenSource scanCancellation;
        private bool closeAfterScan;

        // Hamming-distance ceilings for the 64-bit dHash, one per combo level.
        private static readonly int[] ThresholdLevels = { 2, 6, 10, 16 };
        private const int DefaultThresholdIndex = 2;
        // ponytail: FlowLayoutPanel chokes on many thousands of child controls;
        // groups beyond this are reported in the status line, not rendered.
        private const int MaxShownImages = 1000;

        public Form_SimilarImages(MainForm mainForm)
        {
            this.mainForm = mainForm;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 700);
            MinimumSize = new Size(640, 480);
            Text = I18n.GetText("SimilarImagesTitle");
            headerFont = new Font(Font, FontStyle.Bold);

            comboThreshold = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170,
                Margin = new Padding(3, 6, 12, 3)
            };
            comboThreshold.Items.AddRange(new object[]
            {
                I18n.GetText("SimilarImagesLevelIdentical"),
                I18n.GetText("SimilarImagesLevelHigh"),
                I18n.GetText("SimilarImagesLevelMedium"),
                I18n.GetText("SimilarImagesLevelLow")
            });
            comboThreshold.SelectedIndex = DefaultThresholdIndex;

            btnScan = new Button { Text = I18n.GetText("SimilarImagesScan"), AutoSize = true, MinimumSize = new Size(110, 30) };
            btnScan.Click += async (_, _) => await RunScanAsync();

            trackZoom = new TrackBar
            {
                Minimum = 120,
                Maximum = 320,
                Value = Math.Min(Math.Max(Program.Settings.PreviewSize, 120), 320),
                TickFrequency = 40,
                SmallChange = 10,
                LargeChange = 40,
                Width = 160,
                AutoSize = false,
                Height = 30,
                Margin = new Padding(3, 6, 3, 3)
            };
            trackZoom.ValueChanged += TrackZoom_ValueChanged;

            labelStatus = new Label { AutoSize = true, Margin = new Padding(12, 10, 3, 3) };

            var topPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(6, 4, 6, 0)
            };
            var labelThreshold = new Label { Text = I18n.GetText("SimilarImagesThreshold"), AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
            var labelZoom = new Label { Text = I18n.GetText("LabelGridZoomText"), AutoSize = true, Margin = new Padding(12, 10, 3, 3) };
            topPanel.Controls.AddRange(new Control[] { labelThreshold, comboThreshold, btnScan, labelZoom, trackZoom, labelStatus });

            flowResults = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                Padding = new Padding(6)
            };

            btnAutoKeep = new Button { Text = I18n.GetText("SimilarImagesAutoKeep"), AutoSize = true, MinimumSize = new Size(120, 30) };
            btnAutoKeep.Click += BtnAutoKeep_Click;
            btnDelete = new Button { Text = I18n.GetText("SimilarImagesDelete"), AutoSize = true, MinimumSize = new Size(120, 30) };
            btnDelete.Click += async (_, _) => await DeleteMarkedAsync();
            btnClose = new Button { Text = I18n.GetText("SimilarImagesClose"), AutoSize = true, MinimumSize = new Size(90, 30) };
            btnClose.Click += (_, _) => Close();

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 4, 6, 4)
            };
            bottomPanel.Controls.AddRange(new Control[] { btnClose, btnDelete, btnAutoKeep });

            Controls.Add(flowResults);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);

            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
        }

        private async Task RunScanAsync()
        {
            if (scanning)
                return;
            scanning = true;
            scanCancellation = new CancellationTokenSource();
            CancellationToken token = scanCancellation.Token;
            btnScan.Enabled = btnAutoKeep.Enabled = btnDelete.Enabled = false;
            UseWaitCursor = true;
            try
            {
                ClearResults();
                List<DatasetManager.DataItem> items = Program.DataManager.GetScopedItems()
                    .Where(item => !VideoProcessingService.IsVideoFile(item.ImageFilePath))
                    .ToList();
                labelStatus.Text = string.Format(I18n.GetText("SimilarImagesScanning"), 0, items.Count);

                var hashedItems = new List<DatasetManager.DataItem>(items.Count);
                var hashes = new List<ulong>(items.Count);
                var pending = new List<DatasetManager.DataItem>();
                foreach (var item in items)
                {
                    if (item.Img == null)
                    {
                        pending.Add(item);
                        continue;
                    }
                    // Shared UI bitmaps must be read on the UI thread; the 9x8
                    // downscale per thumbnail is microseconds, so this stays
                    // responsive even for thousands of images.
                    try
                    {
                        hashes.Add(SimilarImageFinder.ComputeDHash(item.Img));
                        hashedItems.Add(item);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Similar-image hash failed for '{item.ImageFilePath}': {ex.Message}");
                    }
                }

                if (pending.Count > 0)
                {
                    // Dataset loaded without previews: decode small thumbs from
                    // disk off the UI thread.
                    int alreadyHashed = hashedItems.Count;
                    var progress = new Progress<int>(done =>
                    {
                        if (!IsDisposed)
                            labelStatus.Text = string.Format(I18n.GetText("SimilarImagesScanning"), alreadyHashed + done, items.Count);
                    });
                    var fromDisk = await Task.Run(() =>
                    {
                        var result = new List<(DatasetManager.DataItem Item, ulong Hash)>();
                        int done = 0;
                        foreach (var item in pending)
                        {
                            if (token.IsCancellationRequested)
                                break;
                            using Image thumb = ImageLoader.MakeThumb(item.ImageFilePath, 64);
                            if (thumb != null)
                                result.Add((item, SimilarImageFinder.ComputeDHash(thumb)));
                            done++;
                            if (done % 20 == 0)
                                ((IProgress<int>)progress).Report(done);
                        }
                        return result;
                    });
                    if (IsDisposed || token.IsCancellationRequested)
                        return;
                    foreach (var (item, hash) in fromDisk)
                    {
                        hashedItems.Add(item);
                        hashes.Add(hash);
                    }
                }

                int maxDistance = ThresholdLevels[Math.Max(0, comboThreshold.SelectedIndex)];
                List<List<DatasetManager.DataItem>> groups =
                    SimilarImageFinder.GroupBySimilarity(hashedItems, hashes, maxDistance);

                // Precompute which groups fit under the render cap, so the
                // display-thumb decode below covers exactly what the wall
                // will actually show — no more, no less.
                var renderGroups = new List<List<DatasetManager.DataItem>>();
                int renderImages = 0;
                foreach (var group in groups)
                {
                    if (renderGroups.Count > 0 && renderImages + group.Count > MaxShownImages)
                        break;
                    renderGroups.Add(group);
                    renderImages += group.Count;
                }

                // Items without a dataset thumbnail need a display image of
                // their own; decode only the (few) rendered ones.
                var displayOverrides = new Dictionary<DatasetManager.DataItem, Image>();
                List<DatasetManager.DataItem> needDisplay = renderGroups.SelectMany(g => g)
                    .Where(item => item.Img == null)
                    .ToList();
                if (needDisplay.Count > 0)
                {
                    var decoded = await Task.Run(() =>
                    {
                        var result = new List<(DatasetManager.DataItem Item, Image Image)>();
                        foreach (var item in needDisplay)
                        {
                            if (token.IsCancellationRequested)
                                break;
                            Image thumb = ImageLoader.MakeThumb(item.ImageFilePath, 256);
                            if (thumb != null)
                                result.Add((item, thumb));
                        }
                        return result;
                    });
                    if (IsDisposed || token.IsCancellationRequested)
                    {
                        foreach (var pair in decoded)
                            pair.Image.Dispose();
                        return;
                    }
                    foreach (var pair in decoded)
                    {
                        displayOverrides[pair.Item] = pair.Image;
                        ownedImages.Add(pair.Image);
                    }
                }

                ShowGroups(groups, renderGroups, displayOverrides);
            }
            catch (Exception ex)
            {
                // A single unreadable file must not take down the whole scan
                // (async-void path would otherwise bubble to the global handler).
                Trace.WriteLine($"Similar-image scan failed: {ex}");
                if (!IsDisposed)
                    labelStatus.Text = ex.Message;
            }
            finally
            {
                scanning = false;
                scanCancellation.Dispose();
                scanCancellation = null;
                if (closeAfterScan)
                {
                    // Deferred close (see OnFormClosing): the scan has now
                    // unwound, so the close can proceed for real.
                    closeAfterScan = false;
                    Close();
                }
                else if (!IsDisposed)
                {
                    UseWaitCursor = false;
                    btnScan.Enabled = btnAutoKeep.Enabled = btnDelete.Enabled = true;
                }
            }
        }

        private void ShowGroups(
            List<List<DatasetManager.DataItem>> groups,
            List<List<DatasetManager.DataItem>> renderGroups,
            Dictionary<DatasetManager.DataItem, Image> displayOverrides)
        {
            if (groups.Count == 0)
            {
                labelStatus.Text = I18n.GetText("SimilarImagesNoResult");
                return;
            }

            int groupIndex = 0;
            flowResults.SuspendLayout();
            try
            {
                foreach (var group in renderGroups)
                {
                    groupIndex++;

                    var header = new Label
                    {
                        AutoSize = true,
                        Font = headerFont,
                        Margin = new Padding(3, 12, 3, 3),
                        Text = string.Format(I18n.GetText("SimilarImagesGroupHeader"), groupIndex, group.Count)
                    };
                    flowResults.Controls.Add(header);
                    flowResults.SetFlowBreak(header, true);

                    var boxes = new List<CustomPictureBoxWithYN>(group.Count);
                    foreach (var item in group)
                    {
                        var box = new CustomPictureBoxWithYN(trackZoom.Value, trackZoom.Value, true);
                        box.BorderStyle = BorderStyle.FixedSingle;
                        box.SizeMode = PictureBoxSizeMode.Zoom;
                        box.SetSelectionMode(true);
                        box.SetDataSetItem(item);
                        box.Image = item.Img ?? (displayOverrides.TryGetValue(item, out Image over) ? over : null);
                        box.FullImagePreviewPath = item.ImageFilePath;
                        long bytes = GetFileLength(item.ImageFilePath);
                        box.Tag = bytes;
                        toolTip.SetToolTip(box, $"{item.Name}  ({bytes / 1024:N0} KB)");
                        flowResults.Controls.Add(box);
                        boxes.Add(box);
                    }
                    flowResults.SetFlowBreak(boxes[boxes.Count - 1], true);
                    groupBoxes.Add(boxes);
                }
            }
            finally
            {
                flowResults.ResumeLayout();
            }

            int totalImages = groups.Sum(g => g.Count);
            string status = string.Format(I18n.GetText("SimilarImagesResultStatus"), groups.Count, totalImages);
            if (renderGroups.Count < groups.Count)
                status += " " + string.Format(I18n.GetText("SimilarImagesShownCapped"), renderGroups.Count);
            labelStatus.Text = status;
        }

        private void BtnAutoKeep_Click(object sender, EventArgs e)
        {
            // czkawka's default heuristic: keep the largest file of each group.
            foreach (var boxes in groupBoxes)
            {
                CustomPictureBoxWithYN keep = boxes[0];
                foreach (var box in boxes)
                {
                    if ((long)box.Tag > (long)keep.Tag)
                        keep = box;
                }
                foreach (var box in boxes)
                    box.ResetState(box == keep);
            }
        }

        private async Task DeleteMarkedAsync()
        {
            List<string> paths = groupBoxes.SelectMany(boxes => boxes)
                .Where(box => !box.StateYes)
                .Select(box => box.GetDataSetItem().ImageFilePath)
                .ToList();
            if (paths.Count == 0)
                return;
            if (MessageBox.Show(this,
                    string.Format(I18n.GetText("SimilarImagesDeleteConfirm"), paths.Count),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            // Detach the wall first: RemoveMany disposes the dataset thumbnails
            // the picture boxes are sharing.
            ClearResults();
            UseWaitCursor = true;
            try
            {
                mainForm.DeleteDatasetMediaFiles(paths);
            }
            finally
            {
                UseWaitCursor = false;
            }
            await RunScanAsync();
        }

        private void TrackZoom_ValueChanged(object sender, EventArgs e)
        {
            flowResults.SuspendLayout();
            foreach (Control control in flowResults.Controls)
            {
                if (control is CustomPictureBoxWithYN box)
                    box.SetSize(trackZoom.Value);
            }
            flowResults.ResumeLayout();
        }

        private void ClearResults()
        {
            flowResults.SuspendLayout();
            while (flowResults.Controls.Count > 0)
            {
                Control control = flowResults.Controls[0];
                flowResults.Controls.RemoveAt(0);
                if (control is PictureBox box)
                    box.Image = null; // dataset-owned thumbnails stay alive
                control.Dispose();
            }
            flowResults.ResumeLayout();
            groupBoxes.Clear();
            foreach (Image image in ownedImages)
                image.Dispose();
            ownedImages.Clear();
        }

        private static long GetFileLength(string path)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // House pattern for long-running dialogs: never tear the window
            // down mid-job — cancel the scan and close once it has unwound.
            if (scanning)
            {
                e.Cancel = true;
                closeAfterScan = true;
                scanCancellation?.Cancel();
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClearResults();
            toolTip.Dispose();
            headerFont.Dispose();
            base.OnFormClosed(e);
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
