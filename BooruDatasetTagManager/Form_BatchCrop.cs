using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Batch-crop window: one interactive crop (free or locked aspect) applied
    /// to every selected image that shares the reference resolution.
    /// </summary>
    public sealed class Form_BatchCrop : Form
    {
        private readonly IReadOnlyList<string> selectedPaths;
        private readonly IReadOnlyList<string> scopedPaths;
        private readonly CropCanvas canvas = new CropCanvas();
        private readonly ComboBox comboAspect = new ComboBox();
        private readonly NumericUpDown numX = new NumericUpDown();
        private readonly NumericUpDown numY = new NumericUpDown();
        private readonly NumericUpDown numW = new NumericUpDown();
        private readonly NumericUpDown numH = new NumericUpDown();
        private readonly CheckBox chkIncludeScope = new CheckBox();
        private readonly Label labelStatus = new Label();
        private readonly Button buttonApply = new Button();
        private readonly Button buttonSaveAs = new Button();
        private readonly Button buttonCancel = new Button();

        private Bitmap image;
        private Bitmap displayImage;
        private string referencePath;
        private Rectangle crop;
        private BatchCropAspect aspect = BatchCropAspect.Free;
        private BatchCropAspect customAspect = BatchCropAspect.Custom(16, 9);
        private bool updatingFields;
        private bool draggingNew;
        private bool moving;
        private BatchCropHandle resizeHandle = BatchCropHandle.None;
        private Point dragStartScreen;
        private Point moveLastImage;
        private int lastAspectIndex;
        private bool running;
        private bool cancelRun;
        private bool closeAfterRun;

        public IReadOnlyList<string> OverwrittenPaths { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> NewFilePaths { get; private set; } = Array.Empty<string>();

        public Form_BatchCrop(IReadOnlyList<string> selectedPaths, IReadOnlyList<string> scopedPaths)
        {
            this.selectedPaths = selectedPaths ?? Array.Empty<string>();
            this.scopedPaths = scopedPaths ?? Array.Empty<string>();

            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            MinimumSize = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(480));
            Text = I18n.GetText("BatchCropTitle");

            var side = new Panel
            {
                Dock = DockStyle.Left,
                Width = LogicalToDeviceUnits(260),
                Padding = new Padding(LogicalToDeviceUnits(12))
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            comboAspect.DropDownStyle = ComboBoxStyle.DropDownList;
            comboAspect.Dock = DockStyle.Fill;
            FillAspectItems();
            comboAspect.SelectedIndexChanged += ComboAspect_SelectedIndexChanged;

            ConfigureNumeric(numX);
            ConfigureNumeric(numY);
            ConfigureNumeric(numW);
            ConfigureNumeric(numH);
            numX.ValueChanged += (_, _) => ApplyNumericPosition();
            numY.ValueChanged += (_, _) => ApplyNumericPosition();
            numW.ValueChanged += (_, _) => ApplyNumericSize(fromWidth: true);
            numH.ValueChanged += (_, _) => ApplyNumericSize(fromWidth: false);

            chkIncludeScope.AutoSize = true;
            chkIncludeScope.Text = I18n.GetText("BatchCropIncludeScope");
            chkIncludeScope.Checked = this.selectedPaths.Count <= 1;
            chkIncludeScope.CheckedChanged += (_, _) => UpdateStatus();

            labelStatus.AutoSize = true;
            labelStatus.MaximumSize = new Size(LogicalToDeviceUnits(230), 0);

            buttonApply.Text = I18n.GetText("BatchCropApply");
            buttonApply.AutoSize = true;
            buttonApply.Dock = DockStyle.Fill;
            buttonApply.Click += (_, _) => RunCrop(overwrite: true);
            buttonSaveAs.Text = I18n.GetText("BatchCropSaveAs");
            buttonSaveAs.AutoSize = true;
            buttonSaveAs.Dock = DockStyle.Fill;
            buttonSaveAs.Click += (_, _) => RunCrop(overwrite: false);
            buttonCancel.Text = I18n.GetText("BatchCropCancel");
            buttonCancel.AutoSize = true;
            buttonCancel.Dock = DockStyle.Fill;
            buttonCancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

            int row = 0;
            AddRow(layout, ref row, I18n.GetText("BatchCropAspect"), comboAspect);
            AddRow(layout, ref row, I18n.GetText("BatchCropX"), numX);
            AddRow(layout, ref row, I18n.GetText("BatchCropY"), numY);
            AddRow(layout, ref row, I18n.GetText("BatchCropWidth"), numW);
            AddRow(layout, ref row, I18n.GetText("BatchCropHeight"), numH);
            layout.Controls.Add(chkIncludeScope, 0, row);
            layout.SetColumnSpan(chkIncludeScope, 2);
            row++;
            layout.Controls.Add(labelStatus, 0, row);
            layout.SetColumnSpan(labelStatus, 2);
            row++;
            layout.Controls.Add(buttonApply, 0, row);
            layout.SetColumnSpan(buttonApply, 2);
            row++;
            layout.Controls.Add(buttonSaveAs, 0, row);
            layout.SetColumnSpan(buttonSaveAs, 2);
            row++;
            layout.Controls.Add(buttonCancel, 0, row);
            layout.SetColumnSpan(buttonCancel, 2);

            side.Controls.Add(layout);

            canvas.Dock = DockStyle.Fill;
            canvas.TabStop = true;
            canvas.BackColor = Color.FromArgb(40, 40, 40);
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Resize += (_, _) =>
            {
                RebuildDisplay();
                canvas.Invalidate();
            };

            Controls.Add(canvas);
            Controls.Add(side);
            CancelButton = buttonCancel;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape && !running)
                    DialogResult = DialogResult.Cancel;
            };
            FormClosing += Form_BatchCrop_FormClosing;
            Load += (_, _) => LoadReference();
        }

        private static void AddRow(TableLayoutPanel layout, ref int row, string caption, Control control)
        {
            var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 6, 3) };
            layout.Controls.Add(label, 0, row);
            layout.Controls.Add(control, 1, row);
            row++;
        }

        private static void ConfigureNumeric(NumericUpDown box)
        {
            box.Minimum = 0;
            box.Maximum = 100000;
            box.DecimalPlaces = 0;
            box.Dock = DockStyle.Fill;
        }

        private void FillAspectItems()
        {
            comboAspect.Items.Clear();
            comboAspect.Items.Add(new AspectItem(I18n.GetText("BatchCropAspectFree"), BatchCropAspect.Free));
            comboAspect.Items.Add(new AspectItem(I18n.GetText("BatchCropAspectCustom"), BatchCropAspect.Custom(0, 0)));
            comboAspect.Items.Add(new AspectItem(I18n.GetText("BatchCropAspectOriginal"), BatchCropAspect.Original(1, 1)));
            foreach ((int width, int height) in BatchCropMath.Presets)
            {
                comboAspect.Items.Add(new AspectItem(
                    string.Format(I18n.GetText("BatchCropAspectPreset"), width, height),
                    BatchCropAspect.Preset(width, height)));
            }
            comboAspect.SelectedIndex = 0;
            lastAspectIndex = 0;
        }

        private void LoadReference()
        {
            foreach (string path in selectedPaths.Concat(scopedPaths))
            {
                if (VideoProcessingService.IsVideoFile(path) || !File.Exists(path))
                    continue;
                Image loaded = ImageLoader.GetImageFromFile(path);
                if (loaded is Bitmap bitmap && bitmap.Width > 0 && bitmap.Height > 0)
                {
                    image?.Dispose();
                    image = bitmap;
                    referencePath = path;
                    Text = I18n.GetText("BatchCropTitle") + " — " + Path.GetFileName(path);
                    aspect = BatchCropAspect.Free;
                    crop = new Rectangle(0, 0, image.Width, image.Height);
                    numW.Maximum = image.Width;
                    numH.Maximum = image.Height;
                    numX.Maximum = image.Width;
                    numY.Maximum = image.Height;
                    SyncFields();
                    UpdateStatus();
                    RebuildDisplay();
                    canvas.Invalidate();
                    return;
                }
                loaded?.Dispose();
            }
            MessageBox.Show(this, I18n.GetText("TipImgLoadError"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
        }

        private IReadOnlyList<string> CandidatePaths()
        {
            return chkIncludeScope.Checked ? scopedPaths : selectedPaths;
        }

        private IReadOnlyList<string> TargetPaths()
        {
            if (image == null)
                return Array.Empty<string>();
            var sized = CandidatePaths().Select(path => (path, BatchCropService.TryGetImageSize(path)));
            return BatchCropMath.FilterSameSize(sized, image.Size);
        }

        private void UpdateStatus()
        {
            if (image == null)
            {
                labelStatus.Text = string.Empty;
                return;
            }
            int targets = TargetPaths().Count;
            int skipped = Math.Max(0, CandidatePaths().Count - targets);
            string text = string.Format(I18n.GetText("BatchCropStatus"), targets, image.Width, image.Height);
            if (skipped > 0)
                text += Environment.NewLine + string.Format(I18n.GetText("BatchCropSkipped"), skipped);
            labelStatus.Text = text;
        }

        private void ComboAspect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingFields || image == null || comboAspect.SelectedItem is not AspectItem item)
                return;

            if (item.IsCustomPrompt)
            {
                if (!TryPromptCustomAspect())
                {
                    updatingFields = true;
                    comboAspect.SelectedIndex = lastAspectIndex;
                    updatingFields = false;
                    return;
                }
                aspect = customAspect;
            }
            else if (item.Aspect.Kind == BatchCropAspectKind.Original)
            {
                aspect = BatchCropAspect.Original(image.Width, image.Height);
            }
            else
            {
                aspect = item.Aspect;
            }

            lastAspectIndex = comboAspect.SelectedIndex;
            crop = BatchCropMath.ApplyAspect(crop, image.Size, aspect);
            SyncFields();
            canvas.Invalidate();
        }

        private bool TryPromptCustomAspect()
        {
            using var prompt = new Form
            {
                Text = I18n.GetText("BatchCropCustomTitle"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                AutoScaleDimensions = new SizeF(96F, 96F)
            };
            prompt.ClientSize = prompt.LogicalToDeviceUnits(new Size(260, 120));
            var widthBox = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Math.Max(1, customAspect.Numerator), Left = prompt.LogicalToDeviceUnits(20), Top = prompt.LogicalToDeviceUnits(20), Width = prompt.LogicalToDeviceUnits(70) };
            var heightBox = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Math.Max(1, customAspect.Denominator), Left = prompt.LogicalToDeviceUnits(120), Top = prompt.LogicalToDeviceUnits(20), Width = prompt.LogicalToDeviceUnits(70) };
            var colon = new Label { Text = ":", AutoSize = true, Left = prompt.LogicalToDeviceUnits(98), Top = prompt.LogicalToDeviceUnits(24) };
            var ok = new Button { Text = I18n.GetText("BtnOK"), DialogResult = DialogResult.OK, Left = prompt.LogicalToDeviceUnits(70), Top = prompt.LogicalToDeviceUnits(70), Width = prompt.LogicalToDeviceUnits(70) };
            var cancel = new Button { Text = I18n.GetText("BatchCropCancel"), DialogResult = DialogResult.Cancel, Left = prompt.LogicalToDeviceUnits(150), Top = prompt.LogicalToDeviceUnits(70), Width = prompt.LogicalToDeviceUnits(80) };
            prompt.Controls.AddRange(new Control[] { widthBox, colon, heightBox, ok, cancel });
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;
            if (prompt.ShowDialog(this) != DialogResult.OK)
                return false;
            customAspect = BatchCropAspect.Custom((int)widthBox.Value, (int)heightBox.Value);
            return true;
        }

        private void ApplyNumericPosition()
        {
            if (updatingFields || image == null)
                return;
            crop = BatchCropMath.SetPosition(crop, (int)numX.Value, (int)numY.Value, image.Size);
            SyncFields();
            canvas.Invalidate();
        }

        private void ApplyNumericSize(bool fromWidth)
        {
            if (updatingFields || image == null)
                return;
            int width = (int)numW.Value;
            int height = (int)numH.Value;
            if (!aspect.IsFree && !fromWidth)
            {
                double ratio = aspect.WidthOverHeight;
                width = Math.Max(BatchCropMath.MinSize, (int)Math.Round(height * ratio));
            }
            crop = BatchCropMath.SetSize(crop, width, height, image.Size, aspect);
            SyncFields();
            canvas.Invalidate();
        }

        private bool IsDragging => draggingNew || moving || resizeHandle != BatchCropHandle.None;

        private void RebuildDisplay()
        {
            if (image == null || canvas.ClientSize.Width < 1 || canvas.ClientSize.Height < 1)
            {
                SwapDisplayImage(null);
                return;
            }
            Rectangle location = CropCanvasHelper.CalcImageLocation(image.Size, canvas.ClientSize);
            if (location.Width < 1 || location.Height < 1)
            {
                SwapDisplayImage(null);
                return;
            }

            Bitmap fitted;
            try
            {
                fitted = new Bitmap(location.Width, location.Height, PixelFormat.Format32bppPArgb);
            }
            catch (ArgumentException)
            {
                SwapDisplayImage(null);
                return;
            }

            try
            {
                using (var graphics = Graphics.FromImage(fitted))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(image, new Rectangle(0, 0, location.Width, location.Height));
                }
            }
            catch (ArgumentException)
            {
                fitted.Dispose();
                SwapDisplayImage(null);
                return;
            }

            SwapDisplayImage(fitted);
        }

        private void SwapDisplayImage(Bitmap next)
        {
            Bitmap old = displayImage;
            displayImage = next;
            if (old != null && !ReferenceEquals(old, next))
                old.Dispose();
        }

        private void SyncFields()
        {
            updatingFields = true;
            numX.Maximum = Math.Max(0, image.Width - crop.Width);
            numY.Maximum = Math.Max(0, image.Height - crop.Height);
            numW.Maximum = image.Width;
            numH.Maximum = image.Height;
            numX.Value = Math.Clamp(crop.X, 0, (int)numX.Maximum);
            numY.Value = Math.Clamp(crop.Y, 0, (int)numY.Maximum);
            numW.Value = Math.Clamp(crop.Width, 1, (int)numW.Maximum);
            numH.Value = Math.Clamp(crop.Height, 1, (int)numH.Maximum);
            updatingFields = false;
        }

        private Point ToImage(Point screen)
        {
            return CropCanvasHelper.ScreenPointToImagePoint(screen, image.Size, canvas.ClientSize);
        }

        private BatchCropHandle HitTest(Point screen)
        {
            if (image == null || crop.Width < 1)
                return BatchCropHandle.None;
            Rectangle screenCrop = CropCanvasHelper.ImageRectToScreenRect(crop, image.Size, canvas.ClientSize);
            int size = Math.Max(8, LogicalToDeviceUnits(8));
            if (HandleAt(screenCrop.Left, screenCrop.Top, size).Contains(screen))
                return BatchCropHandle.NW;
            if (HandleAt(screenCrop.Right, screenCrop.Top, size).Contains(screen))
                return BatchCropHandle.NE;
            if (HandleAt(screenCrop.Left, screenCrop.Bottom, size).Contains(screen))
                return BatchCropHandle.SW;
            if (HandleAt(screenCrop.Right, screenCrop.Bottom, size).Contains(screen))
                return BatchCropHandle.SE;
            if (HandleAt(screenCrop.Left + screenCrop.Width / 2, screenCrop.Top, size).Contains(screen))
                return BatchCropHandle.N;
            if (HandleAt(screenCrop.Left + screenCrop.Width / 2, screenCrop.Bottom, size).Contains(screen))
                return BatchCropHandle.S;
            if (HandleAt(screenCrop.Left, screenCrop.Top + screenCrop.Height / 2, size).Contains(screen))
                return BatchCropHandle.W;
            if (HandleAt(screenCrop.Right, screenCrop.Top + screenCrop.Height / 2, size).Contains(screen))
                return BatchCropHandle.E;
            if (screenCrop.Contains(screen))
                return BatchCropHandle.Move;
            return BatchCropHandle.None;
        }

        private static Rectangle HandleAt(int x, int y, int size)
        {
            return new Rectangle(x - size / 2, y - size / 2, size, size);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (running || image == null || e.Button != MouseButtons.Left)
                return;
            canvas.Focus();
            BatchCropHandle hit = HitTest(e.Location);
            dragStartScreen = e.Location;
            if (hit == BatchCropHandle.Move)
            {
                moving = true;
                moveLastImage = ToImage(e.Location);
            }
            else if (hit == BatchCropHandle.None)
            {
                draggingNew = true;
            }
            else
            {
                resizeHandle = hit;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (image == null)
                return;
            if (draggingNew)
            {
                crop = BatchCropMath.FromDrag(ToImage(dragStartScreen), ToImage(e.Location), image.Size, aspect);
                canvas.Invalidate();
                return;
            }
            if (moving)
            {
                Point current = ToImage(e.Location);
                crop = BatchCropMath.Move(crop, current.X - moveLastImage.X, current.Y - moveLastImage.Y, image.Size);
                moveLastImage = current;
                canvas.Invalidate();
                return;
            }
            if (resizeHandle != BatchCropHandle.None)
            {
                crop = BatchCropMath.Resize(crop, resizeHandle, ToImage(e.Location), image.Size, aspect);
                canvas.Invalidate();
                return;
            }

            canvas.Cursor = HitTest(e.Location) switch
            {
                BatchCropHandle.NW or BatchCropHandle.SE => Cursors.SizeNWSE,
                BatchCropHandle.NE or BatchCropHandle.SW => Cursors.SizeNESW,
                BatchCropHandle.N or BatchCropHandle.S => Cursors.SizeNS,
                BatchCropHandle.E or BatchCropHandle.W => Cursors.SizeWE,
                BatchCropHandle.Move => Cursors.SizeAll,
                _ => Cursors.Cross
            };
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (IsDragging)
                SyncFields();
            draggingNew = false;
            moving = false;
            resizeHandle = BatchCropHandle.None;
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (canvas.ClientSize.Width < 1 || canvas.ClientSize.Height < 1)
                return;

            Graphics graphics = e.Graphics;
            try
            {
                graphics.Clear(canvas.BackColor);
                if (image == null)
                    return;
                Rectangle location = CropCanvasHelper.CalcImageLocation(image.Size, canvas.ClientSize);
                if (location.Width < 1 || location.Height < 1)
                    return;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                Bitmap frame = displayImage;
                if (frame != null)
                    graphics.DrawImageUnscaled(frame, location.Location);
                else
                    graphics.DrawImage(image, location);
                if (crop.Width < 1 || crop.Height < 1)
                    return;

                Rectangle screenCrop = Rectangle.Intersect(
                    location,
                    CropCanvasHelper.ImageRectToScreenRect(crop, image.Size, canvas.ClientSize));
                if (screenCrop.Width < 1 || screenCrop.Height < 1)
                    return;

                using (var dim = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                {
                    FillPositive(graphics, dim, location.Left, location.Top, screenCrop.Left - location.Left, location.Height);
                    FillPositive(graphics, dim, screenCrop.Right, location.Top, location.Right - screenCrop.Right, location.Height);
                    FillPositive(graphics, dim, screenCrop.Left, location.Top, screenCrop.Width, screenCrop.Top - location.Top);
                    FillPositive(graphics, dim, screenCrop.Left, screenCrop.Bottom, screenCrop.Width, location.Bottom - screenCrop.Bottom);
                }
                using (var pen = new Pen(Color.White, Math.Max(1f, LogicalToDeviceUnits(2))))
                    graphics.DrawRectangle(pen, screenCrop);
                int size = Math.Max(8, LogicalToDeviceUnits(8));
                using var handle = new SolidBrush(Color.White);
                foreach (Point p in new[]
                {
                    new Point(screenCrop.Left, screenCrop.Top),
                    new Point(screenCrop.Right, screenCrop.Top),
                    new Point(screenCrop.Left, screenCrop.Bottom),
                    new Point(screenCrop.Right, screenCrop.Bottom),
                    new Point(screenCrop.Left + screenCrop.Width / 2, screenCrop.Top),
                    new Point(screenCrop.Left + screenCrop.Width / 2, screenCrop.Bottom),
                    new Point(screenCrop.Left, screenCrop.Top + screenCrop.Height / 2),
                    new Point(screenCrop.Right, screenCrop.Top + screenCrop.Height / 2)
                })
                {
                    graphics.FillRectangle(handle, HandleAt(p.X, p.Y, size));
                }
            }
            catch (ArgumentException)
            {
                // Disposed or zero-size GDI+ surface mid-rebuild: skip this frame.
            }
        }

        private static void FillPositive(Graphics graphics, Brush brush, int x, int y, int width, int height)
        {
            if (width > 0 && height > 0)
                graphics.FillRectangle(brush, x, y, width, height);
        }

        private void RunCrop(bool overwrite)
        {
            if (running || image == null)
                return;
            if (crop.Width < BatchCropMath.MinSize || crop.Height < BatchCropMath.MinSize)
            {
                MessageBox.Show(this, I18n.GetText("BatchCropEmpty"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IReadOnlyList<string> targets = TargetPaths();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("BatchCropNoValid"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (overwrite)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    string.Format(I18n.GetText("BatchCropOverwriteConfirm"), targets.Count),
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                    return;
            }

            running = true;
            cancelRun = false;
            SetButtonsEnabled(false);
            var overwritten = new List<string>();
            var created = new List<string>();
            int failed = 0;
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (cancelRun)
                        break;
                    labelStatus.Text = string.Format(I18n.GetText("BatchCropProgress"), i + 1, targets.Count);
                    labelStatus.Update();
                    string path = targets[i];
                    try
                    {
                        if (overwrite)
                        {
                            if (BatchCropService.TryOverwrite(path, image.Size, crop))
                                overwritten.Add(path);
                            else
                                failed++;
                        }
                        else
                        {
                            string copy = BatchCropService.TrySaveCopy(path, image.Size, crop);
                            if (copy != null)
                            {
                                if (Program.Settings != null)
                                    ImageEditorSaveService.CloneCaption(path, copy, Program.Settings.GetTagFilesExtensions());
                                created.Add(copy);
                            }
                            else
                            {
                                failed++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        failed++;
                    }
                }
            }
            finally
            {
                running = false;
                SetButtonsEnabled(true);
            }

            OverwrittenPaths = overwritten;
            NewFilePaths = created;
            int done = overwritten.Count + created.Count;
            if (closeAfterRun)
            {
                DialogResult = done > 0 ? DialogResult.OK : DialogResult.Cancel;
                return;
            }
            if (done == 0 && failed == 0)
            {
                UpdateStatus();
                return;
            }
            if (failed > 0)
                MessageBox.Show(this, string.Format(I18n.GetText("BatchCropPartial"), done, failed), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            buttonApply.Enabled = enabled;
            buttonSaveAs.Enabled = enabled;
            comboAspect.Enabled = enabled;
            chkIncludeScope.Enabled = enabled;
            numX.Enabled = enabled;
            numY.Enabled = enabled;
            numW.Enabled = enabled;
            numH.Enabled = enabled;
        }

        private void Form_BatchCrop_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!running)
                return;
            cancelRun = true;
            closeAfterRun = true;
            e.Cancel = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                canvas.Paint -= Canvas_Paint;
                Bitmap oldImage = image;
                Bitmap oldDisplay = displayImage;
                image = null;
                displayImage = null;
                oldImage?.Dispose();
                oldDisplay?.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class CropCanvas : Panel
        {
            public CropCanvas()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }
        }

        private sealed class AspectItem
        {
            public string Text { get; }
            public BatchCropAspect Aspect { get; }
            public bool IsCustomPrompt { get; }

            public AspectItem(string text, BatchCropAspect aspect)
            {
                Text = text;
                Aspect = aspect;
                IsCustomPrompt = aspect.Kind == BatchCropAspectKind.Custom && aspect.Numerator == 0;
            }

            public override string ToString() => Text;
        }
    }
}
