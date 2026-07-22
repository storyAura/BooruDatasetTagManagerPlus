using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Embedded preview at the bottom of the dataset module: a slim clickable
    /// header (chevron + title + current file text) over an owner-drawn
    /// canvas that aspect-fits 1..<see cref="MaxImages"/> images side by side
    /// (multi-select shows the first four, the rest are dropped). The panel
    /// only renders state — expansion, docking and persistence live in the
    /// main form (<see cref="ToggleRequested"/> /
    /// <see cref="OpenInWindowRequested"/>, the latter carrying the clicked
    /// cell index). Colors derive from the current Back/ForeColor pair.
    /// </summary>
    public sealed class DatasetPreviewPanel : UserControl
    {
        public const int MaxImages = 4;

        /// <summary>User clicked the header to collapse/expand the body.</summary>
        public event Action ToggleRequested;
        /// <summary>User double-clicked an image cell (its index) to open it in the floating window.</summary>
        public event Action<int> OpenInWindowRequested;

        private readonly HeaderStrip header;
        private readonly HeaderStrip body;
        private readonly List<Image> images = new List<Image>();
        private string title = "Preview";
        private string fileName = string.Empty;
        private bool expanded = true;

        public DatasetPreviewPanel()
        {
            body = new HeaderStrip { Dock = DockStyle.Fill };
            body.Paint += Body_Paint;
            body.MouseDoubleClick += Body_MouseDoubleClick;
            body.Resize += (_, _) => body.Invalidate();
            body.BackColorChanged += (_, _) => body.Invalidate();

            header = new HeaderStrip { Dock = DockStyle.Top, Cursor = Cursors.Hand };
            header.Paint += Header_Paint;
            header.Click += (_, _) => ToggleRequested?.Invoke();
            header.BackColorChanged += (_, _) => header.Invalidate();
            header.ForeColorChanged += (_, _) => header.Invalidate();

            // The control added last docks first: the header claims the top
            // edge before the body fills the remainder.
            Controls.Add(body);
            Controls.Add(header);
            header.Height = HeaderHeight;
        }

        /// <summary>Device-pixel height of the always-visible header strip.</summary>
        public int HeaderHeight => LogicalToDeviceUnits(28);

        public bool Expanded => expanded;

        public void SetTitle(string value)
        {
            title = string.IsNullOrEmpty(value) ? "Preview" : value;
            header.Invalidate();
        }

        /// <summary>Single-image preview (null clears).</summary>
        public void SetImage(Image image, string imageFileName)
        {
            SetImages(image == null ? Array.Empty<Image>() : new[] { image }, imageFileName);
        }

        /// <summary>
        /// Replaces the previewed images (the panel takes ownership and
        /// disposes them; anything beyond <see cref="MaxImages"/> is ignored).
        /// </summary>
        public void SetImages(IReadOnlyList<Image> newImages, string headerText)
        {
            var incoming = (newImages ?? Array.Empty<Image>())
                .Where(image => image != null)
                .Take(MaxImages)
                .ToList();
            foreach (Image old in images)
            {
                if (!incoming.Any(image => ReferenceEquals(image, old)))
                    old.Dispose();
            }
            images.Clear();
            images.AddRange(incoming);
            fileName = headerText ?? string.Empty;
            header.Invalidate();
            body.Invalidate();
        }

        public void SetExpanded(bool value)
        {
            expanded = value;
            body.Visible = value;
            header.Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            header.Height = HeaderHeight;
            header.Invalidate();
        }

        private void Body_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(body.BackColor);
            if (images.Count == 0)
                return;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            for (int i = 0; i < images.Count; i++)
            {
                Rectangle cell = CellBounds(i);
                if (cell.Width <= 1 || cell.Height <= 1)
                    continue;
                try
                {
                    Size size = images[i].Size;
                    float scale = Math.Min((float)cell.Width / size.Width, (float)cell.Height / size.Height);
                    int w = Math.Max(1, (int)(size.Width * scale));
                    int h = Math.Max(1, (int)(size.Height * scale));
                    g.DrawImage(images[i], new Rectangle(
                        cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h));
                }
                catch (ArgumentException)
                {
                    // Disposed mid-swap: skip this frame.
                }
            }
        }

        private Rectangle CellBounds(int index)
        {
            int pad = LogicalToDeviceUnits(6);
            int gap = LogicalToDeviceUnits(6);
            int count = Math.Max(1, images.Count);
            int cellWidth = (body.ClientSize.Width - pad * 2 - gap * (count - 1)) / count;
            return new Rectangle(
                pad + index * (cellWidth + gap),
                pad,
                cellWidth,
                body.ClientSize.Height - pad * 2);
        }

        private void Body_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            for (int i = 0; i < images.Count; i++)
            {
                if (CellBounds(i).Contains(e.Location))
                {
                    OpenInWindowRequested?.Invoke(i);
                    return;
                }
            }
        }

        private void Header_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = header.BackColor;
            Color fg = header.ForeColor;
            using (var back = new SolidBrush(Blend(fg, bg, 0.04f)))
                g.FillRectangle(back, header.ClientRectangle);
            using (var line = new Pen(Blend(fg, bg, 0.18f)))
                g.DrawLine(line, 0, 0, header.Width, 0);

            DrawChevron(g, Blend(fg, bg, 0.60f));

            int textLeft = LogicalToDeviceUnits(26);
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Size titleSize = TextRenderer.MeasureText(title, Font, new Size(int.MaxValue, header.Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            var titleRect = new Rectangle(textLeft, 0, Math.Min(titleSize.Width, Math.Max(0, header.Width - textLeft)), header.Height);
            TextRenderer.DrawText(g, title, Font, titleRect, fg, flags);

            if (fileName.Length > 0)
            {
                int nameLeft = titleRect.Right + LogicalToDeviceUnits(8);
                var nameRect = new Rectangle(nameLeft, 0,
                    Math.Max(0, header.Width - nameLeft - LogicalToDeviceUnits(8)), header.Height);
                TextRenderer.DrawText(g, fileName, Font, nameRect, Blend(fg, bg, 0.55f),
                    flags | TextFormatFlags.EndEllipsis);
            }
        }

        private void DrawChevron(Graphics g, Color color)
        {
            int size = LogicalToDeviceUnits(8);
            int x = LogicalToDeviceUnits(10);
            int centerY = header.Height / 2;
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1.6f, LogicalToDeviceUnits(16) / 10f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                if (expanded)
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(x, centerY - size / 4),
                        new Point(x + size / 2, centerY + size / 4),
                        new Point(x + size, centerY - size / 4)
                    });
                }
                else
                {
                    g.DrawLines(pen, new[]
                    {
                        new Point(x + size / 4, centerY - size / 2),
                        new Point(x + size * 3 / 4, centerY),
                        new Point(x + size / 4, centerY + size / 2)
                    });
                }
            }
            g.SmoothingMode = oldMode;
        }

        private static Color Blend(Color over, Color under, float amount)
        {
            float rest = 1f - amount;
            return Color.FromArgb(
                (int)(over.R * amount + under.R * rest),
                (int)(over.G * amount + under.G * rest),
                (int)(over.B * amount + under.B * rest));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Image image in images)
                    image.Dispose();
                images.Clear();
            }
            base.Dispose(disposing);
        }

        private sealed class HeaderStrip : Panel
        {
            public HeaderStrip()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw, true);
            }
        }
    }
}
