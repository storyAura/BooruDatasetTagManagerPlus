using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Flat vertical folder sidebar shown when the dataset spans multiple
    /// repeat folders: a rounded search box on top, below it one row per
    /// folder (outlined folder glyph, name, right-aligned image count) plus a
    /// pinned "all folders" row. Rows are owner-drawn and every color derives
    /// from the current Back/ForeColor pair, so the scheme manager's recolor
    /// pass is enough for both light and dark schemes. No ImageList is
    /// involved anywhere (a disposed bitmap handed to an ImageList used to
    /// crash with "Parameter is not valid" once the deferred native handle
    /// was created). Raises <see cref="FolderSelected"/> with the selected
    /// relative folder (null = all).
    /// </summary>
    public sealed class DatasetFolderListView : UserControl
    {
        public event Action<string> FolderSelected;

        private readonly FolderListBox list;
        private readonly BufferedPanel searchHost;
        private readonly TextBox searchBox;
        private readonly ToolTip rowToolTip = new ToolTip();

        private IReadOnlyList<DatasetFolderEntry> entries = Array.Empty<DatasetFolderEntry>();
        private int totalImageCount;
        private string allText = "All";
        private string rootText = "(root)";
        private string countFormat = "{0}";
        private string selectedFolder;
        private bool updatingSelection;
        private string lastToolTipText = string.Empty;

        public DatasetFolderListView()
        {
            list = new FolderListBox { Dock = DockStyle.Fill };
            list.SelectedIndexChanged += List_SelectedIndexChanged;
            list.HoverRowChanged += List_HoverRowChanged;

            searchBox = new TextBox { BorderStyle = BorderStyle.None };
            searchBox.TextChanged += (_, _) => RebuildItems();

            searchHost = new BufferedPanel { Dock = DockStyle.Top };
            searchHost.Paint += SearchHost_Paint;
            searchHost.Resize += (_, _) => LayoutSearchBox();
            searchHost.Controls.Add(searchBox);
            // The scheme manager recolors the children directly; repaint the
            // hand-drawn chrome whenever that happens.
            searchBox.BackColorChanged += (_, _) => searchHost.Invalidate();
            list.BackColorChanged += (_, _) => { searchHost.Invalidate(); list.Invalidate(); };
            list.ForeColorChanged += (_, _) => { searchHost.Invalidate(); list.Invalidate(); };

            // The control added last docks first: the search strip claims the
            // top edge before the list fills the remainder.
            Controls.Add(list);
            Controls.Add(searchHost);
            UpdateMetrics();
        }

        /// <summary>Relative folder of the current selection; null = all.</summary>
        public string SelectedFolder => selectedFolder;

        public void SetTexts(string all, string root, string imageCountFormat, string searchPlaceholder)
        {
            allText = string.IsNullOrEmpty(all) ? "All" : all;
            rootText = string.IsNullOrEmpty(root) ? "(root)" : root;
            countFormat = string.IsNullOrEmpty(imageCountFormat) ? "{0}" : imageCountFormat;
            searchBox.PlaceholderText = searchPlaceholder ?? string.Empty;
            RebuildItems();
        }

        /// <summary>
        /// Shows the given folder entries. Keeps the current selection when its
        /// folder still exists and silently falls back to "all" (raising
        /// <see cref="FolderSelected"/>) when it does not.
        /// </summary>
        public void LoadFolders(IReadOnlyList<DatasetFolderEntry> folderEntries, int totalCount)
        {
            entries = folderEntries ?? Array.Empty<DatasetFolderEntry>();
            totalImageCount = totalCount;
            bool selectionLost = selectedFolder != null
                && entries.All(entry => !string.Equals(entry.RelativePath, selectedFolder, StringComparison.OrdinalIgnoreCase));
            if (selectionLost)
                selectedFolder = null;
            RebuildItems();
            if (selectionLost)
                FolderSelected?.Invoke(null);
        }

        public void ResetSelectionToAll()
        {
            selectedFolder = null;
            ApplySelection();
        }

        private void RebuildItems()
        {
            updatingSelection = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.Add(new FolderRow(null, allText, FormatCount(totalImageCount)));
                string filter = searchBox.Text.Trim();
                foreach (DatasetFolderEntry entry in entries)
                {
                    string name = entry.RelativePath.Length == 0 ? rootText : entry.RelativePath;
                    if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    list.Items.Add(new FolderRow(entry.RelativePath, name, FormatCount(entry.ImageCount)));
                }
            }
            finally
            {
                list.EndUpdate();
                updatingSelection = false;
            }
            ApplySelection();
        }

        private void ApplySelection()
        {
            updatingSelection = true;
            try
            {
                int target = -1;
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (list.Items[i] is FolderRow row && FolderEquals(row.RelativePath, selectedFolder))
                    {
                        target = i;
                        break;
                    }
                }
                list.SelectedIndex = target;
            }
            finally
            {
                updatingSelection = false;
            }
        }

        private void List_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingSelection || list.SelectedItem is not FolderRow row)
                return;
            if (FolderEquals(row.RelativePath, selectedFolder))
                return;
            selectedFolder = row.RelativePath;
            FolderSelected?.Invoke(selectedFolder);
        }

        private void List_HoverRowChanged(int index)
        {
            string tip = string.Empty;
            if (index >= 0 && index < list.Items.Count
                && list.Items[index] is FolderRow row && list.IsNameTruncated(index, row))
            {
                tip = row.Display;
            }
            if (string.Equals(tip, lastToolTipText, StringComparison.Ordinal))
                return;
            lastToolTipText = tip;
            rowToolTip.SetToolTip(list, tip);
        }

        private static bool FolderEquals(string left, string right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private string FormatCount(int count)
        {
            try
            {
                return string.Format(countFormat, count);
            }
            catch (FormatException)
            {
                return count.ToString();
            }
        }

        private void UpdateMetrics()
        {
            searchHost.Height = LogicalToDeviceUnits(46);
            list.ItemHeight = Math.Max(LogicalToDeviceUnits(34), Font.Height + LogicalToDeviceUnits(12));
            LayoutSearchBox();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateMetrics();
            list.Invalidate();
        }

        private Rectangle SearchBoxBounds()
        {
            int marginX = LogicalToDeviceUnits(10);
            int marginTop = LogicalToDeviceUnits(10);
            int marginBottom = LogicalToDeviceUnits(6);
            return new Rectangle(
                marginX, marginTop,
                searchHost.ClientSize.Width - marginX * 2,
                searchHost.ClientSize.Height - marginTop - marginBottom);
        }

        private void LayoutSearchBox()
        {
            Rectangle box = SearchBoxBounds();
            int left = box.X + LogicalToDeviceUnits(30);
            int right = box.Right - LogicalToDeviceUnits(10);
            int height = searchBox.PreferredHeight;
            searchBox.SetBounds(
                left,
                box.Y + Math.Max(0, (box.Height - height + 1) / 2),
                Math.Max(LogicalToDeviceUnits(20), right - left),
                height);
        }

        private void SearchHost_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = list.BackColor;
            Color fg = list.ForeColor;
            using (var background = new SolidBrush(surface))
                g.FillRectangle(background, searchHost.ClientRectangle);
            Rectangle box = SearchBoxBounds();
            if (box.Width <= 0 || box.Height <= 0)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(box, LogicalToDeviceUnits(8)))
            {
                using (var fill = new SolidBrush(searchBox.BackColor))
                    g.FillPath(fill, path);
                using var border = new Pen(Blend(fg, surface, 0.25f), Math.Max(1f, DeviceDpi / 96f));
                g.DrawPath(border, path);
            }
            DrawSearchGlyph(g, box, Blend(fg, surface, 0.45f));
        }

        private void DrawSearchGlyph(Graphics g, Rectangle box, Color color)
        {
            int glyph = LogicalToDeviceUnits(13);
            int lens = LogicalToDeviceUnits(9);
            int x = box.X + LogicalToDeviceUnits(10);
            int y = box.Y + (box.Height - glyph) / 2;
            using var pen = new Pen(color, Math.Max(1.6f, LogicalToDeviceUnits(16) / 10f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawEllipse(pen, new Rectangle(x, y, lens, lens));
            g.DrawLine(pen, x + lens - 1, y + lens - 1, x + glyph, y + glyph);
        }

        private static Color Blend(Color over, Color under, float amount)
        {
            float rest = 1f - amount;
            return Color.FromArgb(
                (int)(over.R * amount + under.R * rest),
                (int)(over.G * amount + under.G * rest),
                (int)(over.B * amount + under.B * rest));
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float diameter = Math.Max(1f, Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                rowToolTip.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>One immutable list row; a null RelativePath is the pinned "all" row.</summary>
        private sealed class FolderRow
        {
            public FolderRow(string relativePath, string display, string countText)
            {
                RelativePath = relativePath;
                Display = display;
                CountText = countText;
            }

            public string RelativePath { get; }
            public string Display { get; }
            public string CountText { get; }
        }

        private sealed class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw, true);
            }
        }

        /// <summary>
        /// Owner-drawn flat list: folder glyph, name and right-aligned count
        /// per row, hover highlight, accent bar on the selected row.
        /// </summary>
        private sealed class FolderListBox : ListBox
        {
            public event Action<int> HoverRowChanged;

            public int HoverIndex { get; private set; } = -1;

            private const TextFormatFlags NameFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            private const TextFormatFlags CountFlags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            private const TextFormatFlags MeasureFlags = TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

            public FolderListBox()
            {
                DrawMode = DrawMode.OwnerDrawFixed;
                BorderStyle = BorderStyle.None;
                IntegralHeight = false;
                SelectionMode = SelectionMode.One;
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                base.OnDrawItem(e);
                if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not FolderRow row)
                    return;
                Graphics g = e.Graphics;
                Rectangle bounds = e.Bounds;
                Color bg = BackColor;
                Color fg = ForeColor;
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

                Color rowBack = selected
                    ? Blend(SystemColors.Highlight, bg, 0.22f)
                    : e.Index == HoverIndex ? Blend(fg, bg, 0.06f) : bg;
                using (var back = new SolidBrush(rowBack))
                    g.FillRectangle(back, bounds);
                if (selected)
                {
                    using var accent = new SolidBrush(SystemColors.Highlight);
                    g.FillRectangle(accent, new Rectangle(bounds.X, bounds.Y, LogicalToDeviceUnits(3), bounds.Height));
                }

                DrawGlyph(g, row, GlyphBounds(bounds), Blend(fg, bg, 0.58f));
                Rectangle countRect = new Rectangle(
                    bounds.X, bounds.Y, bounds.Width - LogicalToDeviceUnits(10), bounds.Height);
                TextRenderer.DrawText(g, row.CountText, Font, countRect, Blend(fg, bg, 0.55f), CountFlags);
                TextRenderer.DrawText(g, row.Display, Font, NameBounds(bounds, row), fg, NameFlags);
            }

            public bool IsNameTruncated(int index, FolderRow row)
            {
                if (index < 0 || index >= Items.Count)
                    return false;
                int width = TextRenderer.MeasureText(
                    row.Display, Font, new Size(int.MaxValue, ItemHeight), MeasureFlags).Width;
                return width > NameBounds(GetItemRectangle(index), row).Width;
            }

            private Rectangle GlyphBounds(Rectangle rowBounds)
            {
                int size = LogicalToDeviceUnits(16);
                return new Rectangle(
                    rowBounds.X + LogicalToDeviceUnits(10),
                    rowBounds.Y + (rowBounds.Height - size) / 2,
                    size, size);
            }

            private Rectangle NameBounds(Rectangle rowBounds, FolderRow row)
            {
                Rectangle glyph = GlyphBounds(rowBounds);
                int left = glyph.Right + LogicalToDeviceUnits(9);
                int countWidth = TextRenderer.MeasureText(
                    row.CountText, Font, new Size(int.MaxValue, rowBounds.Height), MeasureFlags).Width;
                int right = rowBounds.Right - LogicalToDeviceUnits(10) - countWidth - LogicalToDeviceUnits(8);
                return new Rectangle(left, rowBounds.Y, Math.Max(0, right - left), rowBounds.Height);
            }

            private void DrawGlyph(Graphics g, FolderRow row, Rectangle bounds, Color color)
            {
                SmoothingMode oldMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, Math.Max(1.5f, bounds.Width / 11f))
                {
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                })
                {
                    if (row.RelativePath == null)
                        DrawAllGlyph(g, bounds, pen);
                    else
                        DrawFolderGlyph(g, bounds, pen);
                }
                g.SmoothingMode = oldMode;
            }

            private static void DrawFolderGlyph(Graphics g, Rectangle bounds, Pen pen)
            {
                float x = bounds.X;
                float y = bounds.Y + bounds.Height * 0.12f;
                float w = bounds.Width;
                float h = bounds.Height * 0.76f;
                float tabWidth = w * 0.38f;
                float tabHeight = h * 0.26f;
                using var path = new GraphicsPath();
                path.AddLines(new[]
                {
                    new PointF(x, y),
                    new PointF(x + tabWidth, y),
                    new PointF(x + tabWidth + w * 0.10f, y + tabHeight),
                    new PointF(x + w, y + tabHeight),
                    new PointF(x + w, y + h),
                    new PointF(x, y + h)
                });
                path.CloseFigure();
                g.DrawPath(pen, path);
            }

            private static void DrawAllGlyph(Graphics g, Rectangle bounds, Pen pen)
            {
                float gap = bounds.Width * 0.20f;
                float cell = (bounds.Width - gap) / 2f;
                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        var cellRect = new RectangleF(
                            bounds.X + column * (cell + gap),
                            bounds.Y + row * (cell + gap),
                            cell, cell);
                        using GraphicsPath path = RoundedRect(cellRect, cell * 0.22f);
                        g.DrawPath(pen, path);
                    }
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(HitTestRow(e.Location));
            }

            /// <summary>
            /// Row under the point, or -1. IndexFromPoint alone reports the
            /// nearest row for points in the blank area below the last row.
            /// </summary>
            private int HitTestRow(Point location)
            {
                int index = IndexFromPoint(location);
                if (index < 0 || index >= Items.Count || !GetItemRectangle(index).Contains(location))
                    return -1;
                return index;
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_LBUTTONDOWN = 0x0201;
                const int WM_LBUTTONDBLCLK = 0x0203;
                if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
                {
                    long lparam = (long)m.LParam;
                    var location = new Point((short)(lparam & 0xFFFF), (short)((lparam >> 16) & 0xFFFF));
                    if (HitTestRow(location) < 0)
                    {
                        // A native ListBox selects the nearest row on blank-area
                        // clicks; that would silently rescope the dataset.
                        Focus();
                        return;
                    }
                }
                base.WndProc(ref m);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHover(-1);
            }

            private void SetHover(int index)
            {
                if (index >= Items.Count)
                    index = -1;
                if (index == HoverIndex)
                    return;
                InvalidateRow(HoverIndex);
                HoverIndex = index;
                InvalidateRow(index);
                HoverRowChanged?.Invoke(index);
            }

            private void InvalidateRow(int index)
            {
                if (index >= 0 && index < Items.Count)
                    Invalidate(GetItemRectangle(index));
            }
        }
    }
}
