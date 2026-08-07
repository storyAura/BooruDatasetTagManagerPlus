using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Scans the loaded dataset (active folder scope) for images that fail to
    /// decode, shows them on a review wall matching the similar-image finder
    /// language (green = keep, red = delete), and deletes via
    /// <see cref="MainForm.DeleteDatasetMediaFiles"/>.
    /// </summary>
    public sealed class Form_CorruptedImages : Form
    {
        private readonly MainForm mainForm;
        private readonly Button btnScan;
        private readonly Button btnMarkAllDelete;
        private readonly Button btnMarkAllKeep;
        private readonly Button btnDelete;
        private readonly Button btnClose;
        private readonly TrackBar trackZoom;
        private readonly Label labelStatus;
        private readonly FlowLayoutPanel flowResults;
        private readonly ToolTip toolTip = new ToolTip();

        private readonly List<CustomPictureBoxWithYN> resultBoxes = new List<CustomPictureBoxWithYN>();
        private readonly List<Image> ownedImages = new List<Image>();
        private bool scanning;
        private CancellationTokenSource scanCancellation;
        private bool closeAfterScan;

        private const int MaxShownImages = 1000;

        public Form_CorruptedImages(MainForm mainForm)
        {
            this.mainForm = mainForm;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 700);
            MinimumSize = new Size(640, 480);
            Text = I18n.GetText("CorruptedImagesTitle");

            btnScan = new Button { Text = I18n.GetText("CorruptedImagesScan"), AutoSize = true, MinimumSize = new Size(110, 30) };
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
            var labelZoom = new Label { Text = I18n.GetText("LabelGridZoomText"), AutoSize = true, Margin = new Padding(12, 10, 3, 3) };
            topPanel.Controls.AddRange(new Control[] { btnScan, labelZoom, trackZoom, labelStatus });

            flowResults = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                Padding = new Padding(6)
            };

            btnMarkAllDelete = new Button { Text = I18n.GetText("CorruptedImagesMarkAllDelete"), AutoSize = true, MinimumSize = new Size(120, 30) };
            btnMarkAllDelete.Click += (_, _) => SetAllStates(keep: false);
            btnMarkAllKeep = new Button { Text = I18n.GetText("CorruptedImagesMarkAllKeep"), AutoSize = true, MinimumSize = new Size(120, 30) };
            btnMarkAllKeep.Click += (_, _) => SetAllStates(keep: true);
            btnDelete = new Button { Text = I18n.GetText("CorruptedImagesDelete"), AutoSize = true, MinimumSize = new Size(120, 30) };
            btnDelete.Click += async (_, _) => await DeleteMarkedAsync();
            btnClose = new Button { Text = I18n.GetText("CorruptedImagesClose"), AutoSize = true, MinimumSize = new Size(90, 30) };
            btnClose.Click += (_, _) => Close();

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 4, 6, 4)
            };
            bottomPanel.Controls.AddRange(new Control[] { btnClose, btnDelete, btnMarkAllKeep, btnMarkAllDelete });

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
            btnScan.Enabled = btnMarkAllDelete.Enabled = btnMarkAllKeep.Enabled = btnDelete.Enabled = false;
            UseWaitCursor = true;
            try
            {
                ClearResults();
                List<DatasetManager.DataItem> items = Program.DataManager.GetScopedItems()
                    .Where(item => !VideoProcessingService.IsVideoFile(item.ImageFilePath)
                        && Extensions.ImageExtensions.Contains(
                            Path.GetExtension(item.ImageFilePath).ToLowerInvariant()))
                    .ToList();
                labelStatus.Text = string.Format(I18n.GetText("CorruptedImagesScanning"), 0, items.Count);

                string[] paths = items.Select(item => item.ImageFilePath).ToArray();
                var progress = new Progress<int>(done =>
                {
                    if (!IsDisposed)
                        labelStatus.Text = string.Format(I18n.GetText("CorruptedImagesScanning"), done, items.Count);
                });
                List<CorruptedImageFinding> findings = await Task.Run(
                    () => CorruptedImageScanner.Scan(paths, progress, token),
                    token).ConfigureAwait(true);

                if (IsDisposed || token.IsCancellationRequested)
                    return;

                ShowFindings(findings, items);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed)
                    labelStatus.Text = I18n.GetText("CorruptedImagesCancelled");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Corrupted-image scan failed: {ex}");
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
                    closeAfterScan = false;
                    Close();
                }
                else if (!IsDisposed)
                {
                    UseWaitCursor = false;
                    btnScan.Enabled = btnMarkAllDelete.Enabled = btnMarkAllKeep.Enabled = btnDelete.Enabled = true;
                }
            }
        }

        private void ShowFindings(List<CorruptedImageFinding> findings, List<DatasetManager.DataItem> items)
        {
            if (findings.Count == 0)
            {
                labelStatus.Text = I18n.GetText("CorruptedImagesNoResult");
                return;
            }

            Dictionary<string, DatasetManager.DataItem> byPath = items.ToDictionary(
                item => item.ImageFilePath,
                StringComparer.OrdinalIgnoreCase);

            int shown = Math.Min(findings.Count, MaxShownImages);
            flowResults.SuspendLayout();
            try
            {
                for (int i = 0; i < shown; i++)
                {
                    CorruptedImageFinding finding = findings[i];
                    byPath.TryGetValue(finding.Path, out DatasetManager.DataItem item);

                    // Broken files usually have no usable bitmap — always own a
                    // placeholder so the wall never shares a disposed dataset Img.
                    Image display = CreateBrokenPlaceholder(trackZoom.Value);
                    ownedImages.Add(display);

                    var box = new CustomPictureBoxWithYN(trackZoom.Value, trackZoom.Value, isYes: false);
                    box.BorderStyle = BorderStyle.FixedSingle;
                    box.SizeMode = PictureBoxSizeMode.Zoom;
                    box.SetSelectionMode(true);
                    if (item != null)
                        box.SetDataSetItem(item);
                    else
                        box.SetDataSetItem(new DatasetManager.DataItem { ImageFilePath = finding.Path });
                    box.Image = display;
                    // Don't set FullImagePreviewPath — the file is broken; right-click
                    // would only fail again. The placeholder stays as the preview.
                    box.Tag = finding;
                    string reason = LocalizeReason(finding);
                    string name = item?.Name ?? Path.GetFileName(finding.Path);
                    toolTip.SetToolTip(box, name + Environment.NewLine + reason);
                    flowResults.Controls.Add(box);
                    resultBoxes.Add(box);
                }
            }
            finally
            {
                flowResults.ResumeLayout();
            }

            string status = string.Format(I18n.GetText("CorruptedImagesResultStatus"), findings.Count);
            if (shown < findings.Count)
                status += " " + string.Format(I18n.GetText("CorruptedImagesShownCapped"), shown);
            labelStatus.Text = status;
        }

        private static string LocalizeReason(CorruptedImageFinding finding)
        {
            string key = finding.ReasonCode switch
            {
                CorruptedImageScanner.ReasonMissing => "CorruptedImagesReasonMissing",
                CorruptedImageScanner.ReasonEmpty => "CorruptedImagesReasonEmpty",
                CorruptedImageScanner.ReasonInvalidSize => "CorruptedImagesReasonInvalidSize",
                _ => "CorruptedImagesReasonDecode"
            };
            string text = I18n.GetText(key);
            if (!string.IsNullOrEmpty(finding.Detail) && finding.ReasonCode == CorruptedImageScanner.ReasonDecode)
                text += " (" + finding.Detail + ")";
            return text;
        }

        private static Image CreateBrokenPlaceholder(int size)
        {
            int dim = Math.Max(64, size);
            var bitmap = new Bitmap(dim, dim);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(240, 240, 240));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.Salmon, Math.Max(3f, dim / 24f)))
                {
                    int margin = dim / 5;
                    g.DrawLine(pen, margin, margin, dim - margin, dim - margin);
                    g.DrawLine(pen, dim - margin, margin, margin, dim - margin);
                }
                using (var border = new Pen(Color.Gray, 1f))
                    g.DrawRectangle(border, 0, 0, dim - 1, dim - 1);
            }
            return bitmap;
        }

        private void SetAllStates(bool keep)
        {
            foreach (var box in resultBoxes)
                box.ResetState(keep);
        }

        private async Task DeleteMarkedAsync()
        {
            List<string> paths = resultBoxes
                .Where(box => !box.StateYes)
                .Select(box => box.GetDataSetItem().ImageFilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            if (paths.Count == 0)
                return;
            if (MessageBox.Show(this,
                    string.Format(I18n.GetText("CorruptedImagesDeleteConfirm"), paths.Count),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

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
                    box.Image = null;
                control.Dispose();
            }
            flowResults.ResumeLayout();
            resultBoxes.Clear();
            foreach (Image image in ownedImages)
                image.Dispose();
            ownedImages.Clear();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
