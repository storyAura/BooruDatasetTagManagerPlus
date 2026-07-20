using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Lightweight image editor (brush, eraser, eyedropper, crop, hand,
    /// rotate/flip, undo/redo) opened from the dataset image context menu.
    /// Layout mimics Photoshop: a slim tool box docked on the left, options bar
    /// on top, and the default Photoshop shortcuts (B/E/I/C/H, [ ], wheel zoom,
    /// Ctrl+0 fit / Ctrl+1 100% / Ctrl+±, Space pans, Alt+click samples a color
    /// while painting, Ctrl+Z/Ctrl+Shift+Z, Ctrl+S, Enter/Esc for crop).
    /// The canvas is drawn manually so it supports zoom and pan.
    /// On save it writes the file itself (overwrite or "_edit" copy per
    /// settings) and exposes the outcome through <see cref="Result"/> so
    /// MainForm can refresh the dataset.
    /// </summary>
    public sealed class Form_ImageEditor : Form
    {
        private enum EditorTool
        {
            Brush,
            Eraser,
            Eyedropper,
            Crop,
            Hand
        }

        private static readonly float[] BrushSizes = { 2, 4, 6, 8, 12, 18, 26, 36, 50, 72 };
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 32f;
        private const float ZoomStep = 1.25f;

        private readonly string imagePath;
        private readonly ImageEditorDocument document;
        private readonly bool eraseToTransparent;

        private readonly ToolStrip toolStrip = new ToolStrip();
        private readonly ToolStrip toolBox = new ToolStrip();
        private readonly ToolStripButton buttonSave = new ToolStripButton();
        private readonly ToolStripButton buttonBrush = new ToolStripButton();
        private readonly ToolStripButton buttonEraser = new ToolStripButton();
        private readonly ToolStripButton buttonEyedropper = new ToolStripButton();
        private readonly ToolStripButton buttonCrop = new ToolStripButton();
        private readonly ToolStripButton buttonHand = new ToolStripButton();
        private readonly ToolStripButton buttonColor = new ToolStripButton();
        private readonly ToolStripLabel labelBrushSize = new ToolStripLabel();
        private readonly ToolStripComboBox comboBrushSize = new ToolStripComboBox();
        private readonly ToolStripButton buttonApplyCrop = new ToolStripButton();
        private readonly ToolStripButton buttonRotateLeft = new ToolStripButton();
        private readonly ToolStripButton buttonRotateRight = new ToolStripButton();
        private readonly ToolStripButton buttonFlipHorizontal = new ToolStripButton();
        private readonly ToolStripButton buttonFlipVertical = new ToolStripButton();
        private readonly ToolStripButton buttonUndo = new ToolStripButton();
        private readonly ToolStripButton buttonRedo = new ToolStripButton();
        private readonly ToolStripButton buttonCancel = new ToolStripButton();
        private readonly BufferedPictureBox canvas = new BufferedPictureBox();
        private readonly Label statusLabel = new Label();
        private readonly Bitmap swatchBitmap = new Bitmap(16, 16);

        private EditorTool activeTool = EditorTool.Brush;
        private Color brushColor = Color.Red;
        private bool strokeActive;
        private bool eyedropperActive;
        private Point lastImagePoint;
        private bool cropDragging;
        private Point cropDragStart;
        private Point cropDragEnd;
        private Rectangle cropSelection = Rectangle.Empty;

        // View transform: fit-to-window until the user zooms explicitly.
        private bool fitToWindow = true;
        private float zoom = 1f;
        private PointF viewOffset;
        private bool spacePanActive;
        private bool panDragging;
        private Point panStartMouse;
        private PointF panStartOffset;

        /// <summary>Non-null after a successful save; the dialog result is OK.</summary>
        public ImageEditorSaveOutcome Result { get; private set; }

        /// <summary>The editor takes ownership of <paramref name="image"/>.</summary>
        public Form_ImageEditor(string imagePath, Bitmap image)
        {
            this.imagePath = imagePath ?? throw new ArgumentNullException(nameof(imagePath));
            document = new ImageEditorDocument(image);
            eraseToTransparent = ImageEditorSaveService.SupportsTransparency(Path.GetExtension(imagePath));
            InitializeComponent();
            ApplyLocalization();
            SelectTool(EditorTool.Brush);
            RefreshCanvasImage();
        }

        private void InitializeComponent()
        {
            Text = "Image editor";
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            MinimumSize = new Size(760, 520);
            KeyPreview = true;

            // Options bar on top, slim symbol tool box on the left (Photoshop layout).
            toolStrip.Dock = DockStyle.Top;
            toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            toolBox.Dock = DockStyle.Left;
            toolBox.GripStyle = ToolStripGripStyle.Hidden;
            toolBox.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
            toolBox.Padding = new Padding(1, 4, 1, 1);
            toolBox.ImageScalingSize = new Size(16, 16);

            ConfigureTextButton(buttonSave, (_, _) => SaveAndClose());
            ConfigureToolButton(buttonBrush, EditorTool.Brush, "🖌");
            ConfigureToolButton(buttonEraser, EditorTool.Eraser, "🧽");
            ConfigureToolButton(buttonEyedropper, EditorTool.Eyedropper, "💧");
            ConfigureToolButton(buttonCrop, EditorTool.Crop, "✂");
            ConfigureToolButton(buttonHand, EditorTool.Hand, "✋");
            buttonColor.DisplayStyle = ToolStripItemDisplayStyle.Image;
            buttonColor.ImageTransparentColor = Color.Magenta;
            buttonColor.Image = swatchBitmap;
            buttonColor.Click += (_, _) => PickBrushColor();
            comboBrushSize.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBrushSize.AutoSize = false;
            comboBrushSize.Width = 64;
            foreach (float size in BrushSizes)
                comboBrushSize.Items.Add(size.ToString("0"));
            comboBrushSize.SelectedIndex = 3;
            ConfigureTextButton(buttonApplyCrop, (_, _) => ApplyCropSelection());
            buttonApplyCrop.Enabled = false;
            ConfigureTextButton(buttonRotateLeft, (_, _) => ApplyRotateFlip(RotateFlipType.Rotate270FlipNone));
            ConfigureTextButton(buttonRotateRight, (_, _) => ApplyRotateFlip(RotateFlipType.Rotate90FlipNone));
            ConfigureTextButton(buttonFlipHorizontal, (_, _) => ApplyRotateFlip(RotateFlipType.RotateNoneFlipX));
            ConfigureTextButton(buttonFlipVertical, (_, _) => ApplyRotateFlip(RotateFlipType.RotateNoneFlipY));
            ConfigureTextButton(buttonUndo, (_, _) => UndoRedo(undo: true));
            ConfigureTextButton(buttonRedo, (_, _) => UndoRedo(undo: false));
            ConfigureTextButton(buttonCancel, (_, _) => Close());
            buttonCancel.Alignment = ToolStripItemAlignment.Right;

            toolBox.Items.AddRange(new ToolStripItem[]
            {
                buttonBrush, buttonEraser, buttonEyedropper, buttonCrop, buttonHand,
                new ToolStripSeparator(),
                buttonColor
            });
            toolStrip.Items.AddRange(new ToolStripItem[]
            {
                buttonSave, new ToolStripSeparator(),
                labelBrushSize, comboBrushSize, new ToolStripSeparator(),
                buttonApplyCrop, new ToolStripSeparator(),
                buttonRotateLeft, buttonRotateRight, buttonFlipHorizontal, buttonFlipVertical, new ToolStripSeparator(),
                buttonUndo, buttonRedo,
                buttonCancel
            });

            canvas.Dock = DockStyle.Fill;
            canvas.BackColor = Color.FromArgb(40, 40, 40);
            canvas.Cursor = Cursors.Cross;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.Paint += Canvas_Paint;
            canvas.Resize += (_, _) => { canvas.Invalidate(); UpdateStatus(); };

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.AutoSize = false;
            statusLabel.Height = 24;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Padding = new Padding(8, 0, 8, 0);

            // Dock order (last added docks first): options bar top, status bar
            // bottom, tool box left in between, canvas fills the rest.
            Controls.Add(canvas);
            Controls.Add(toolBox);
            Controls.Add(statusLabel);
            Controls.Add(toolStrip);

            KeyDown += Form_ImageEditor_KeyDown;
            KeyUp += Form_ImageEditor_KeyUp;
            MouseWheel += Form_ImageEditor_MouseWheel;
            Deactivate += (_, _) => { spacePanActive = false; panDragging = false; UpdateCanvasCursor(); };
            FormClosing += Form_ImageEditor_FormClosing;
        }

        private static void ConfigureTextButton(ToolStripButton button, EventHandler onClick)
        {
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Click += onClick;
        }

        private void ConfigureToolButton(ToolStripButton button, EditorTool tool, string symbol)
        {
            button.DisplayStyle = ToolStripItemDisplayStyle.Text;
            button.Text = symbol;
            button.Click += (_, _) => SelectTool(tool);
        }

        private static void SetShortcutHint(ToolStripItem item, string name, string shortcut)
        {
            item.AutoToolTip = false;
            item.ToolTipText = name + " (" + shortcut + ")";
        }

        private void ApplyLocalization()
        {
            Text = I18n.GetText("ImageEditorTitle") + " - " + Path.GetFileName(imagePath);
            buttonSave.Text = I18n.GetText("ImageEditorSave");
            labelBrushSize.Text = I18n.GetText("ImageEditorBrushSize");
            comboBrushSize.ToolTipText = I18n.GetText("ImageEditorBrushSize") + " ([ / ])";
            buttonApplyCrop.Text = I18n.GetText("ImageEditorApplyCrop");
            buttonRotateLeft.Text = I18n.GetText("ImageEditorRotateLeft");
            buttonRotateRight.Text = I18n.GetText("ImageEditorRotateRight");
            buttonFlipHorizontal.Text = I18n.GetText("ImageEditorFlipHorizontal");
            buttonFlipVertical.Text = I18n.GetText("ImageEditorFlipVertical");
            buttonUndo.Text = I18n.GetText("ImageEditorUndo");
            buttonRedo.Text = I18n.GetText("ImageEditorRedo");
            buttonCancel.Text = I18n.GetText("BtnCancel");
            SetShortcutHint(buttonSave, buttonSave.Text, "Ctrl+S");
            SetShortcutHint(buttonBrush, I18n.GetText("ImageEditorToolBrush"), "B");
            SetShortcutHint(buttonEraser, I18n.GetText("ImageEditorToolEraser"), "E");
            SetShortcutHint(buttonEyedropper, I18n.GetText("ImageEditorToolEyedropper"), "I");
            SetShortcutHint(buttonCrop, I18n.GetText("ImageEditorToolCrop"), "C");
            SetShortcutHint(buttonHand, I18n.GetText("ImageEditorToolHand"), "H / Space");
            SetShortcutHint(buttonApplyCrop, buttonApplyCrop.Text, "Enter");
            SetShortcutHint(buttonUndo, buttonUndo.Text, "Ctrl+Z");
            SetShortcutHint(buttonRedo, buttonRedo.Text, "Ctrl+Shift+Z");
            buttonColor.AutoToolTip = false;
            buttonColor.ToolTipText = I18n.GetText("ImageEditorColor");
            UpdateColorSwatch();
        }

        private void SelectTool(EditorTool tool)
        {
            activeTool = tool;
            buttonBrush.Checked = tool == EditorTool.Brush;
            buttonEraser.Checked = tool == EditorTool.Eraser;
            buttonEyedropper.Checked = tool == EditorTool.Eyedropper;
            buttonCrop.Checked = tool == EditorTool.Crop;
            buttonHand.Checked = tool == EditorTool.Hand;
            if (tool != EditorTool.Crop)
                ClearCropSelection();
            UpdateCanvasCursor();
            UpdateStatus();
        }

        private void UpdateCanvasCursor()
        {
            canvas.Cursor = spacePanActive || panDragging || activeTool == EditorTool.Hand
                ? Cursors.Hand
                : Cursors.Cross;
        }

        private void PickBrushColor()
        {
            using var dialog = new ColorDialog { Color = brushColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            brushColor = dialog.Color;
            UpdateColorSwatch();
        }

        private void UpdateColorSwatch()
        {
            // Draw into the persistent 16x16 bitmap instead of assigning a new
            // Image: reassignment forces a ToolStrip re-layout on every sampled
            // pixel, which made the whole window jitter while dragging the
            // eyedropper.
            using (Graphics graphics = Graphics.FromImage(swatchBitmap))
            {
                graphics.Clear(brushColor);
                graphics.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
            }
            toolBox.Invalidate();
        }

        private float CurrentBrushSize()
        {
            int index = Math.Clamp(comboBrushSize.SelectedIndex, 0, BrushSizes.Length - 1);
            return BrushSizes[index];
        }

        private void AdjustBrushSize(int step)
        {
            comboBrushSize.SelectedIndex = Math.Clamp(comboBrushSize.SelectedIndex + step, 0, BrushSizes.Length - 1);
        }

        // ---- View transform (zoom / pan) ----

        private void GetViewTransform(out float currentZoom, out PointF offset)
        {
            Size imageSize = document.Image.Size;
            Size view = canvas.ClientSize;
            if (fitToWindow || imageSize.Width == 0 || imageSize.Height == 0)
            {
                float fit = Math.Min((float)view.Width / imageSize.Width, (float)view.Height / imageSize.Height);
                if (fit <= 0 || float.IsNaN(fit) || float.IsInfinity(fit))
                    fit = 1f;
                currentZoom = fit;
                offset = new PointF(
                    (view.Width - imageSize.Width * fit) / 2f,
                    (view.Height - imageSize.Height * fit) / 2f);
                zoom = currentZoom;
                viewOffset = offset;
                return;
            }

            currentZoom = zoom;
            offset = ClampOffset(viewOffset, currentZoom);
            viewOffset = offset;
        }

        private PointF ClampOffset(PointF offset, float currentZoom)
        {
            Size imageSize = document.Image.Size;
            Size view = canvas.ClientSize;
            float width = imageSize.Width * currentZoom;
            float height = imageSize.Height * currentZoom;

            float x = width <= view.Width
                ? (view.Width - width) / 2f
                : Math.Clamp(offset.X, view.Width - width, 0f);
            float y = height <= view.Height
                ? (view.Height - height) / 2f
                : Math.Clamp(offset.Y, view.Height - height, 0f);
            return new PointF(x, y);
        }

        private Point ToImagePoint(Point screenPoint)
        {
            GetViewTransform(out float currentZoom, out PointF offset);
            return ImageEditorCanvasMath.ScreenPointToImagePixel(screenPoint, currentZoom, offset, document.Image.Size);
        }

        private RectangleF ImageRectToScreen(Rectangle imageRect)
        {
            GetViewTransform(out float currentZoom, out PointF offset);
            return new RectangleF(
                offset.X + imageRect.X * currentZoom,
                offset.Y + imageRect.Y * currentZoom,
                imageRect.Width * currentZoom,
                imageRect.Height * currentZoom);
        }

        private void SetZoom(float newZoom, Point screenAnchor)
        {
            GetViewTransform(out float currentZoom, out PointF offset);
            newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
            if (Math.Abs(newZoom - currentZoom) < 0.0001f)
                return;
            // Keep the image point under the cursor stationary while zooming.
            float imageX = (screenAnchor.X - offset.X) / currentZoom;
            float imageY = (screenAnchor.Y - offset.Y) / currentZoom;
            fitToWindow = false;
            zoom = newZoom;
            viewOffset = ClampOffset(new PointF(
                screenAnchor.X - imageX * newZoom,
                screenAnchor.Y - imageY * newZoom), newZoom);
            canvas.Invalidate();
            UpdateStatus();
        }

        private void ZoomStepAt(int direction, Point screenAnchor)
        {
            GetViewTransform(out float currentZoom, out _);
            float target = direction > 0 ? currentZoom * ZoomStep : currentZoom / ZoomStep;
            SetZoom(target, screenAnchor);
        }

        private void ZoomToFit()
        {
            fitToWindow = true;
            canvas.Invalidate();
            UpdateStatus();
        }

        private void Form_ImageEditor_MouseWheel(object sender, MouseEventArgs e)
        {
            if (document.Image == null)
                return;
            Point canvasPoint = canvas.PointToClient(Cursor.Position);
            if (!canvas.ClientRectangle.Contains(canvasPoint))
                return;
            ZoomStepAt(e.Delta > 0 ? 1 : -1, canvasPoint);
        }

        // ---- Sampling (eyedropper) ----

        private void SampleColorAt(Point screenPoint)
        {
            Point point = ToImagePoint(screenPoint);
            Color picked = document.Image.GetPixel(point.X, point.Y);
            // Drop the alpha so the sampled color paints opaque strokes.
            brushColor = Color.FromArgb(255, picked.R, picked.G, picked.B);
            UpdateColorSwatch();
            UpdateStatus();
        }

        // ---- Mouse ----

        private bool WantsPan(MouseButtons button)
        {
            return button == MouseButtons.Middle
                || (button == MouseButtons.Left && (spacePanActive || activeTool == EditorTool.Hand));
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (document.Image == null)
                return;
            if (WantsPan(e.Button))
            {
                GetViewTransform(out _, out PointF offset);
                panDragging = true;
                fitToWindow = false;
                panStartMouse = e.Location;
                panStartOffset = offset;
                UpdateCanvasCursor();
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;
            if (activeTool == EditorTool.Crop)
            {
                cropDragging = true;
                cropDragStart = e.Location;
                cropDragEnd = e.Location;
                cropSelection = Rectangle.Empty;
                buttonApplyCrop.Enabled = false;
                canvas.Invalidate();
                return;
            }
            // Photoshop behavior: Alt while the brush is active samples a color
            // instead of painting; the eyedropper tool always samples.
            if (activeTool == EditorTool.Eyedropper
                || (activeTool == EditorTool.Brush && ModifierKeys.HasFlag(Keys.Alt)))
            {
                eyedropperActive = true;
                SampleColorAt(e.Location);
                return;
            }
            if (activeTool == EditorTool.Hand)
                return;
            strokeActive = true;
            document.BeginStroke();
            lastImagePoint = ToImagePoint(e.Location);
            document.DrawStrokeSegment(lastImagePoint, lastImagePoint, brushColor, CurrentBrushSize(),
                activeTool == EditorTool.Eraser, eraseToTransparent);
            canvas.Invalidate();
            UpdateUndoRedoButtons();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (panDragging)
            {
                viewOffset = ClampOffset(new PointF(
                    panStartOffset.X + (e.X - panStartMouse.X),
                    panStartOffset.Y + (e.Y - panStartMouse.Y)), zoom);
                canvas.Invalidate();
                return;
            }
            if (cropDragging)
            {
                cropDragEnd = e.Location;
                canvas.Invalidate();
                return;
            }
            if (eyedropperActive)
            {
                SampleColorAt(e.Location);
                return;
            }
            if (!strokeActive)
                return;
            Point current = ToImagePoint(e.Location);
            document.DrawStrokeSegment(lastImagePoint, current, brushColor, CurrentBrushSize(),
                activeTool == EditorTool.Eraser, eraseToTransparent);
            lastImagePoint = current;
            canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (panDragging)
            {
                panDragging = false;
                UpdateCanvasCursor();
                return;
            }
            if (cropDragging)
            {
                cropDragging = false;
                Rectangle screenRect = NormalizeDragRectangle(cropDragStart, e.Location);
                cropSelection = ScreenRectToImageRect(screenRect);
                bool valid = cropSelection.Width > 1 && cropSelection.Height > 1;
                if (!valid)
                    cropSelection = Rectangle.Empty;
                buttonApplyCrop.Enabled = valid;
                canvas.Invalidate();
                UpdateStatus();
                return;
            }
            eyedropperActive = false;
            strokeActive = false;
        }

        private static Rectangle NormalizeDragRectangle(Point start, Point end)
        {
            return Rectangle.FromLTRB(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
        }

        private Rectangle ScreenRectToImageRect(Rectangle screenRect)
        {
            GetViewTransform(out float currentZoom, out PointF offset);
            return ImageEditorCanvasMath.ScreenRectToImageRect(screenRect, currentZoom, offset, document.Image.Size);
        }

        // ---- Painting ----

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (document.Image == null)
                return;
            GetViewTransform(out float currentZoom, out PointF offset);
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.InterpolationMode = currentZoom >= 1f
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(document.Image, new RectangleF(
                offset.X, offset.Y,
                document.Image.Width * currentZoom,
                document.Image.Height * currentZoom));

            Rectangle overlay;
            if (cropDragging)
                overlay = NormalizeDragRectangle(cropDragStart, cropDragEnd);
            else if (cropSelection != Rectangle.Empty)
                overlay = Rectangle.Round(ImageRectToScreen(cropSelection));
            else
                return;
            if (overlay.Width < 1 || overlay.Height < 1)
                return;
            using var fill = new SolidBrush(Color.FromArgb(50, 30, 144, 255));
            e.Graphics.FillRectangle(fill, overlay);
            using var pen = new Pen(Color.DodgerBlue, 2) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, overlay);
        }

        // ---- Editing operations ----

        private void ApplyCropSelection()
        {
            if (strokeActive || cropSelection == Rectangle.Empty)
                return;
            if (document.ApplyCrop(cropSelection))
                RefreshCanvasImage();
            ClearCropSelection();
        }

        private void ApplyRotateFlip(RotateFlipType type)
        {
            // Swapping the bitmap mid-stroke would corrupt the stroke.
            if (strokeActive || cropDragging)
                return;
            document.RotateFlip(type);
            RefreshCanvasImage();
        }

        private void UndoRedo(bool undo)
        {
            if (strokeActive || cropDragging)
                return;
            bool changed = undo ? document.Undo() : document.Redo();
            if (changed)
                RefreshCanvasImage();
        }

        private void ClearCropSelection()
        {
            cropDragging = false;
            cropSelection = Rectangle.Empty;
            buttonApplyCrop.Enabled = false;
            canvas.Invalidate();
        }

        private void RefreshCanvasImage()
        {
            // Crop/rotate/undo can swap or resize the bitmap: reset to fit.
            fitToWindow = true;
            ClearCropSelection();
            UpdateUndoRedoButtons();
            UpdateStatus();
            canvas.Invalidate();
        }

        private void UpdateUndoRedoButtons()
        {
            buttonUndo.Enabled = document.CanUndo;
            buttonRedo.Enabled = document.CanRedo;
        }

        private void UpdateStatus()
        {
            if (document.Image == null)
                return;
            GetViewTransform(out float currentZoom, out _);
            string text = string.Format("{0} × {1} px | {2:0}%", document.Image.Width, document.Image.Height, currentZoom * 100);
            if (activeTool == EditorTool.Crop)
                text += " | " + I18n.GetText("ImageEditorCropHint");
            else if (activeTool == EditorTool.Eyedropper)
                text += string.Format(" | #{0:X2}{1:X2}{2:X2} | ", brushColor.R, brushColor.G, brushColor.B)
                    + I18n.GetText("ImageEditorEyedropperHint");
            else if (activeTool == EditorTool.Hand)
                text += " | " + I18n.GetText("ImageEditorHandHint");
            statusLabel.Text = text;
        }

        // ---- Keyboard ----

        private void Form_ImageEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !e.Control && !e.Alt)
            {
                // Photoshop: holding Space temporarily switches to the hand tool.
                if (!spacePanActive)
                {
                    spacePanActive = true;
                    UpdateCanvasCursor();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control && !e.Alt)
            {
                if (e.KeyCode == Keys.Z && e.Shift)
                    UndoRedo(undo: false);
                else if (e.KeyCode == Keys.Z)
                    UndoRedo(undo: true);
                else if (e.KeyCode == Keys.Y)
                    UndoRedo(undo: false);
                else if (e.KeyCode == Keys.S)
                    SaveAndClose();
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)
                    ZoomToFit();
                else if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
                    SetZoom(1f, new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2));
                else if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)
                    ZoomStepAt(1, new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2));
                else if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)
                    ZoomStepAt(-1, new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2));
                else
                    return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control || e.Alt)
                return;
            switch (e.KeyCode)
            {
                case Keys.B:
                    SelectTool(EditorTool.Brush);
                    break;
                case Keys.E:
                    SelectTool(EditorTool.Eraser);
                    break;
                case Keys.I:
                    SelectTool(EditorTool.Eyedropper);
                    break;
                case Keys.C:
                    SelectTool(EditorTool.Crop);
                    break;
                case Keys.H:
                    SelectTool(EditorTool.Hand);
                    break;
                case Keys.OemOpenBrackets:
                    AdjustBrushSize(-1);
                    break;
                case Keys.OemCloseBrackets:
                    AdjustBrushSize(1);
                    break;
                case Keys.Enter:
                    if (!buttonApplyCrop.Enabled)
                        return;
                    ApplyCropSelection();
                    break;
                case Keys.Escape:
                    if (cropSelection == Rectangle.Empty && !cropDragging)
                        return;
                    ClearCropSelection();
                    break;
                default:
                    return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void Form_ImageEditor_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                spacePanActive = false;
                if (!panDragging)
                    UpdateCanvasCursor();
                e.Handled = true;
            }
        }

        private void Form_ImageEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK || !document.IsDirty)
                return;
            DialogResult confirm = MessageBox.Show(
                this,
                I18n.GetText("ImageEditorDiscardConfirm"),
                Text,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.OK)
                e.Cancel = true;
        }

        private void SaveAndClose()
        {
            if (!document.IsDirty)
            {
                MessageBox.Show(this, I18n.GetText("ImageEditorNoChanges"), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ImageEditorSaveMode mode = Program.Settings.ImageEditorSaveMode;
            if (mode == ImageEditorSaveMode.Ask)
            {
                using var prompt = new Form_ImageEditorSavePrompt();
                if (prompt.ShowDialog(this) != DialogResult.OK || prompt.Choice == null)
                    return;
                mode = prompt.Choice.Value;
            }
            try
            {
                string extension = Path.GetExtension(imagePath);
                byte[] bytes = ImageEditorSaveService.Encode(document.Image, extension);
                string outputPath = mode == ImageEditorSaveMode.Overwrite
                    ? imagePath
                    : ImageEditorSaveService.CreateNewFilePath(imagePath);
                // Atomic swap so a failed write can't truncate the original.
                SafeFile.WriteAllBytes(outputPath, bytes);
                string clonedCaption = mode == ImageEditorSaveMode.NewFile
                    ? ImageEditorSaveService.CloneCaption(imagePath, outputPath, Program.Settings.GetTagFilesExtensions())
                    : null;
                Result = new ImageEditorSaveOutcome
                {
                    Mode = mode,
                    OutputPath = outputPath,
                    ClonedCaptionPath = clonedCaption
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    string.Format(I18n.GetText("ImageEditorSaveFailed"), ex.Message),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                buttonColor.Image = null;
                document.Dispose();
                swatchBitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
