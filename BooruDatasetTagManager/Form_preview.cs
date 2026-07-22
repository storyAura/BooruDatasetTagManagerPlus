using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Floating image viewer: the window keeps its size while the IMAGE zooms
    /// (the old version scaled the whole form on wheel). Mouse wheel zooms
    /// around the cursor, dragging pans, double-click toggles fit ↔ 100%,
    /// Ctrl+0 fits, Ctrl+1 is 100%, Esc closes. All mapping math lives in
    /// <see cref="PreviewCanvasMath"/> (test-linked).
    /// </summary>
    public partial class Form_preview : Form
    {
        private Image image;
        private string imageName = string.Empty;
        private float zoom = 1f;
        private PointF offset;
        private bool fitMode = true;
        private Point? panLast;
        private bool sizedOnce;

        public Form_preview()
        {
            InitializeComponent();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.SizeMode = PictureBoxSizeMode.Normal;
            pictureBox1.Paint += Canvas_Paint;
            pictureBox1.MouseDown += Canvas_MouseDown;
            pictureBox1.MouseMove += Canvas_MouseMove;
            pictureBox1.MouseUp += Canvas_MouseUp;
            pictureBox1.MouseDoubleClick += Canvas_MouseDoubleClick;
            MouseWheel += Form_preview_MouseWheel;
            Resize += (_, _) => OnViewportChanged();
        }

        public void Show(Image img, string name = null)
        {
            SetImage(img, name);
            Show();
        }

        /// <summary>
        /// Modal preview. Use this when the caller owns the form in a `using`
        /// block: a non-modal Show() would return immediately and the form would
        /// be disposed on scope exit (window flashes and vanishes).
        /// </summary>
        public DialogResult ShowDialog(Image img, string name = null)
        {
            SetImage(img, name);
            return ShowDialog();
        }

        private void SetImage(Image img, string name)
        {
            // The caller owns img (the image cache hands out clones), so the
            // previous image must always be disposed. Detach it from the
            // canvas state before disposing so no paint can hit a disposed
            // bitmap.
            Image old = image;
            image = img;
            imageName = name ?? string.Empty;
            if (old != null && !ReferenceEquals(old, img))
                old.Dispose();

            if (!sizedOnce && image != null)
            {
                Rectangle work = Screen.FromControl(this).WorkingArea;
                int chromeW = Width - ClientSize.Width;
                int chromeH = Height - ClientSize.Height;
                ClientSize = new Size(
                    Math.Min(image.Width, work.Width * 85 / 100 - chromeW),
                    Math.Min(image.Height, work.Height * 85 / 100 - chromeH));
                CenterToScreen();
                sizedOnce = true;
            }
            ApplyFit();
        }

        private Size ImageSize => image?.Size ?? Size.Empty;

        private void ApplyFit()
        {
            fitMode = true;
            if (image != null)
            {
                zoom = PreviewCanvasMath.ClampZoom(PreviewCanvasMath.FitZoom(pictureBox1.ClientSize, ImageSize));
                offset = PreviewCanvasMath.CenterOffset(pictureBox1.ClientSize, ImageSize, zoom);
            }
            pictureBox1.Invalidate();
            UpdateTitle();
        }

        private void SetZoom(float newZoom, PointF anchor)
        {
            if (image == null)
                return;
            newZoom = PreviewCanvasMath.ClampZoom(newZoom);
            offset = PreviewCanvasMath.ZoomAroundPoint(anchor, offset, zoom, newZoom);
            zoom = newZoom;
            offset = PreviewCanvasMath.ClampOffset(pictureBox1.ClientSize, ImageSize, zoom, offset);
            fitMode = false;
            pictureBox1.Invalidate();
            UpdateTitle();
        }

        private void OnViewportChanged()
        {
            if (image == null)
                return;
            if (fitMode)
            {
                ApplyFit();
            }
            else
            {
                offset = PreviewCanvasMath.ClampOffset(pictureBox1.ClientSize, ImageSize, zoom, offset);
                pictureBox1.Invalidate();
            }
        }

        private void UpdateTitle()
        {
            string percent = $"{(int)Math.Round(zoom * 100)}%";
            Text = imageName.Length > 0 ? $"{imageName}  ·  {percent}" : $"Preview  ·  {percent}";
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            if (image == null)
                return;
            Graphics g = e.Graphics;
            try
            {
                // Crisp pixels once zoomed in, smooth scaling when zoomed out.
                g.InterpolationMode = zoom >= 2f
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(image, new RectangleF(
                    offset.X, offset.Y, ImageSize.Width * zoom, ImageSize.Height * zoom));
            }
            catch (ArgumentException)
            {
                // The image was swapped/disposed mid-paint: skip this frame.
            }
        }

        private void Form_preview_MouseWheel(object sender, MouseEventArgs e)
        {
            float factor = e.Delta > 0 ? 1.25f : 1f / 1.25f;
            SetZoom(zoom * factor, e.Location);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                panLast = e.Location;
                pictureBox1.Cursor = Cursors.SizeAll;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (panLast == null || image == null)
                return;
            offset = new PointF(offset.X + e.X - panLast.Value.X, offset.Y + e.Y - panLast.Value.Y);
            offset = PreviewCanvasMath.ClampOffset(pictureBox1.ClientSize, ImageSize, zoom, offset);
            panLast = e.Location;
            pictureBox1.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            panLast = null;
            pictureBox1.Cursor = Cursors.Default;
        }

        private void Canvas_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || image == null)
                return;
            float fit = PreviewCanvasMath.FitZoom(pictureBox1.ClientSize, ImageSize);
            if (Math.Abs(zoom - fit) < 0.001f)
                SetZoom(1f, e.Location);
            else
                ApplyFit();
        }

        private void Form_preview_VisibleChanged(object sender, EventArgs e)
        {
            if (!Visible && image != null)
            {
                // Detach before disposing so a later show can never paint a
                // disposed image.
                Image old = image;
                image = null;
                old.Dispose();
                pictureBox1.Invalidate();
            }
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
            {
                ApplyFit();
                return true;
            }
            if (keyData == (Keys.Control | Keys.D1) || keyData == (Keys.Control | Keys.NumPad1))
            {
                SetZoom(1f, new PointF(pictureBox1.ClientSize.Width / 2f, pictureBox1.ClientSize.Height / 2f));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
