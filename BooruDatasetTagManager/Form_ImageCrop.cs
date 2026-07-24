using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_ImageCrop : Form
    {
        private const int MinimumCropSize = 8;

        private readonly string imagePath;
        private readonly Bitmap imageData;
        private readonly List<CropRegion> regions = new List<CropRegion>();
        private readonly ToolStrip toolStrip = new ToolStrip();
        private readonly ToolStripButton buttonExport = new ToolStripButton();
        private readonly ToolStripButton buttonDelete = new ToolStripButton();
        private readonly ToolStripButton buttonFit = new ToolStripButton();
        private readonly ToolStripButton buttonCancel = new ToolStripButton();
        private readonly SplitContainer splitContainer = new SplitContainer();
        private readonly PictureBox canvas = new PictureBox();
        private readonly Panel previewPanel = new Panel();
        private readonly Label previewHeader = new Label();
        private readonly FlowLayoutPanel previewFlow = new FlowLayoutPanel();

        private bool isDraggingNewRegion;
        private Point dragStartPoint;
        private Point dragEndPoint;
        private Rectangle dragScreenRect = Rectangle.Empty;
        private CropRegion selectedRegion;

        public IReadOnlyList<string> ExportedPaths { get; private set; } = Array.Empty<string>();

        private bool splitLayoutApplied;

        public Form_ImageCrop(string imagePath)
        {
            this.imagePath = imagePath;
            imageData = (Bitmap)Program.DataManager.GetImageFromFileWithCache(imagePath);
            InitializeComponent();
            canvas.Image = imageData;
            ApplyLocalization();
        }

        private void InitializeComponent()
        {
            Text = "Crop image";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;

            buttonExport.DisplayStyle = ToolStripItemDisplayStyle.Text;
            buttonExport.Click += (_, _) => ExportAndClose();
            buttonDelete.DisplayStyle = ToolStripItemDisplayStyle.Text;
            buttonDelete.Click += (_, _) => DeleteSelectedRegion();
            buttonFit.DisplayStyle = ToolStripItemDisplayStyle.Text;
            buttonFit.Click += (_, _) => canvas.Invalidate();
            buttonCancel.DisplayStyle = ToolStripItemDisplayStyle.Text;
            buttonCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
            toolStrip.Dock = DockStyle.Top;
            toolStrip.Items.AddRange(new ToolStripItem[] { buttonExport, buttonDelete, buttonFit, buttonCancel });

            canvas.Dock = DockStyle.Fill;
            canvas.SizeMode = PictureBoxSizeMode.Zoom;
            canvas.TabStop = true;
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Resize += (_, _) => canvas.Invalidate();

            previewHeader.Dock = DockStyle.Top;
            previewHeader.Height = 28;
            previewHeader.Padding = new Padding(6, 6, 6, 0);
            previewHeader.Font = new Font(Font, FontStyle.Bold);

            previewFlow.Dock = DockStyle.Fill;
            previewFlow.AutoScroll = true;
            previewFlow.FlowDirection = FlowDirection.TopDown;
            previewFlow.WrapContents = false;
            previewFlow.Padding = new Padding(6);

            previewPanel.Dock = DockStyle.Fill;
            previewPanel.Controls.Add(previewFlow);
            previewPanel.Controls.Add(previewHeader);

            splitContainer.Dock = DockStyle.Fill;
            splitContainer.FixedPanel = FixedPanel.Panel2;
            splitContainer.Panel1MinSize = 200;
            splitContainer.Panel1.Controls.Add(canvas);
            splitContainer.Panel2.Controls.Add(previewPanel);

            Controls.Add(splitContainer);
            Controls.Add(toolStrip);

            KeyDown += Form_ImageCrop_KeyDown;
            Shown += (_, _) => ApplySplitLayout();
        }

        private void ApplySplitLayout()
        {
            if (splitLayoutApplied)
                return;
            const int previewWidth = 220;
            int total = splitContainer.ClientSize.Width;
            if (total <= previewWidth + splitContainer.Panel1MinSize + splitContainer.SplitterWidth)
                return;

            splitContainer.Panel2MinSize = Math.Min(180, total - splitContainer.Panel1MinSize - splitContainer.SplitterWidth);
            int maxDistance = total - splitContainer.Panel2MinSize - splitContainer.SplitterWidth;
            int distance = Math.Clamp(total - previewWidth, splitContainer.Panel1MinSize, maxDistance);
            if (distance != splitContainer.SplitterDistance)
                splitContainer.SplitterDistance = distance;
            splitLayoutApplied = true;
        }

        private void ApplyLocalization()
        {
            Text = I18n.GetText("CropImageTitle");
            buttonExport.Text = I18n.GetText("CropImageExport");
            buttonDelete.Text = I18n.GetText("CropImageDeleteRegion");
            buttonFit.Text = I18n.GetText("CropImageFitWindow");
            buttonCancel.Text = I18n.GetText("BtnCancel");
            previewHeader.Text = I18n.GetText("CropImagePreview");
        }

        private void Form_ImageCrop_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedRegion();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                e.Handled = true;
            }
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (imageData == null)
                return;

            CropRegion hit = CropCanvasHelper.HitTest(regions, e.Location, imageData.Size, canvas.ClientSize);
            if (hit != null)
            {
                selectedRegion = hit;
                RebuildPreviewPanel();
                canvas.Invalidate();
                return;
            }

            selectedRegion = null;
            isDraggingNewRegion = true;
            dragStartPoint = e.Location;
            dragEndPoint = e.Location;
            dragScreenRect = Rectangle.Empty;
            canvas.Invalidate();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDraggingNewRegion)
                return;
            dragEndPoint = e.Location;
            dragScreenRect = CropCanvasHelper.NormalizeDragRectangle(dragStartPoint, dragEndPoint);
            canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isDraggingNewRegion)
                return;

            isDraggingNewRegion = false;
            dragScreenRect = CropCanvasHelper.NormalizeDragRectangle(dragStartPoint, dragEndPoint);
            Rectangle imageRect = CropCanvasHelper.ScreenRectToImageRect(dragScreenRect, imageData.Size, canvas.ClientSize);
            imageRect = CropCanvasHelper.ClampToImage(imageRect, imageData.Size);
            dragScreenRect = Rectangle.Empty;

            if (imageRect.Width >= MinimumCropSize && imageRect.Height >= MinimumCropSize)
            {
                var region = new CropRegion
                {
                    Index = regions.Count + 1,
                    Bounds = imageRect,
                    DisplayColor = CropCanvasHelper.RegionColors[regions.Count % CropCanvasHelper.RegionColors.Length]
                };
                regions.Add(region);
                selectedRegion = region;
                RebuildPreviewPanel();
            }
            canvas.Invalidate();
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (imageData == null)
                return;

            foreach (CropRegion region in regions)
            {
                Rectangle screenRect = CropCanvasHelper.ImageRectToScreenRect(region.Bounds, imageData.Size, canvas.ClientSize);
                using (Pen pen = new Pen(region.DisplayColor, region == selectedRegion ? 3 : 2))
                {
                    e.Graphics.DrawRectangle(pen, screenRect);
                }
                using (SolidBrush brush = new SolidBrush(region.DisplayColor))
                {
                    e.Graphics.FillRectangle(brush, screenRect.X, screenRect.Y, 28, 18);
                }
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    e.Graphics.DrawString("#" + region.Index, Font, textBrush, screenRect.X + 2, screenRect.Y + 1);
                }
            }

            if (isDraggingNewRegion && !dragScreenRect.IsEmpty)
            {
                using (Pen pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, dragScreenRect);
                }
            }
        }

        private void DeleteSelectedRegion()
        {
            if (selectedRegion == null)
                return;
            regions.Remove(selectedRegion);
            selectedRegion = null;
            RenumberRegions();
            RebuildPreviewPanel();
            canvas.Invalidate();
        }

        private void RenumberRegions()
        {
            for (int i = 0; i < regions.Count; i++)
            {
                regions[i].Index = i + 1;
                regions[i].DisplayColor = CropCanvasHelper.RegionColors[i % CropCanvasHelper.RegionColors.Length];
            }
        }

        /// <summary>Disposes preview rows INCLUDING their PictureBox images:
        /// control.Dispose() alone leaked one bitmap per region per rebuild.</summary>
        private void DisposePreviewControls()
        {
            foreach (Control control in previewFlow.Controls.OfType<Control>().ToList())
            {
                foreach (PictureBox pictureBox in control.Controls.OfType<PictureBox>())
                {
                    pictureBox.Image?.Dispose();
                    pictureBox.Image = null;
                }
                control.Dispose();
            }
            previewFlow.Controls.Clear();
        }

        private void RebuildPreviewPanel()
        {
            previewFlow.SuspendLayout();
            DisposePreviewControls();

            foreach (CropRegion region in regions)
            {
                Rectangle bounds = CropCanvasHelper.ClampToImage(region.Bounds, imageData.Size);
                if (bounds.Width < 2 || bounds.Height < 2)
                    continue;

                var panel = new Panel
                {
                    Width = previewFlow.ClientSize.Width - 24,
                    Height = 140,
                    BorderStyle = region == selectedRegion ? BorderStyle.FixedSingle : BorderStyle.None,
                    BackColor = region == selectedRegion ? Color.FromArgb(240, 248, 255) : Color.White,
                    Margin = new Padding(0, 0, 0, 8),
                    Tag = region
                };

                var thumbnail = new PictureBox
                {
                    Width = 96,
                    Height = 96,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Left = 8,
                    Top = 8
                };
                // Render the ≤96px thumbnail straight from the source region:
                // keeping a full-size crop clone per row held entire images in
                // memory just to show a 96×96 preview.
                double thumbScale = Math.Min(1.0, 96.0 / Math.Max(bounds.Width, bounds.Height));
                int thumbWidth = Math.Max(1, (int)Math.Round(bounds.Width * thumbScale));
                int thumbHeight = Math.Max(1, (int)Math.Round(bounds.Height * thumbScale));
                var thumbBitmap = new Bitmap(thumbWidth, thumbHeight);
                using (Graphics graphics = Graphics.FromImage(thumbBitmap))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(imageData, new Rectangle(0, 0, thumbWidth, thumbHeight), bounds, GraphicsUnit.Pixel);
                }
                thumbnail.Image = thumbBitmap;

                var label = new Label
                {
                    AutoSize = false,
                    Left = 112,
                    Top = 12,
                    Width = panel.Width - 120,
                    Height = 40,
                    Text = string.Format(
                        I18n.GetText("CropImageRegionSize"),
                        region.Index,
                        bounds.Width,
                        bounds.Height)
                };

                panel.Controls.Add(thumbnail);
                panel.Controls.Add(label);
                panel.Click += (_, _) =>
                {
                    selectedRegion = region;
                    RebuildPreviewPanel();
                    canvas.Invalidate();
                };
                previewFlow.Controls.Add(panel);
            }
            previewFlow.ResumeLayout();
        }

        private void ExportAndClose()
        {
            if (regions.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("CropImageNoRegions"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExportedPaths = ImageCropExporter.ExportRegions(imagePath, imageData, regions);
            if (ExportedPaths.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("CropImageNoRegions"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string outputDirectory = ImageCropExporter.GetOutputDirectory(imagePath);
            MessageBox.Show(
                this,
                string.Format(I18n.GetText("CropImageExportDone"), ExportedPaths.Count, outputDirectory),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposePreviewControls();
                imageData?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
