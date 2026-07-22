using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Unified dataset browser that replaces the flat dataset grid plus the
    /// separate folder sidebar: one searchable list where kohya repeat
    /// folders are collapsible groups (chevron + folder glyph + name +
    /// count, "all" pinned on top) and images are rows (thumbnail + name)
    /// inside them. Selection is custom (click / Ctrl / Shift / Ctrl+A) and
    /// the main form mirrors it into the hidden dataset grid, which stays
    /// the authority all existing operations read. Clicking a folder header
    /// scopes the dataset (SetActiveFolder) exactly like the old sidebar;
    /// the chevron only collapses the group visually. Single-folder datasets
    /// render as a flat image list. All colors derive from the current
    /// Back/ForeColor pair, so the scheme recolor pass is enough.
    /// </summary>
    public sealed class DatasetBrowserView : UserControl
    {
        /// <summary>Scope change requested (null = all folders).</summary>
        public event Action<string> FolderScopeSelected;
        /// <summary>The user changed the image selection.</summary>
        public event Action SelectionChangedByUser;
        /// <summary>Image double-clicked (path).</summary>
        public event Action<string> ImageActivated;
        /// <summary>Right-click on an image row (screen coordinates, selection already updated).</summary>
        public event Action<Point> ImageContextRequested;
        /// <summary>Right-click on a folder group header or the pinned All row:
        /// the checked folder keys in display order, or null for All (= every
        /// folder). Screen coordinates.</summary>
        public event Action<IReadOnlyList<string>, Point> FolderContextRequested;
        /// <summary>Raw key events from the list (Delete, Ctrl+V, ... handled by the form).</summary>
        public event KeyEventHandler BrowserKeyDown;

        private readonly BrowserListBox list;
        private readonly BufferedPanel searchHost;
        private readonly TextBox searchBox;
        private readonly ToolTip rowToolTip = new ToolTip();

        private readonly List<Row> rows = new List<Row>();
        private readonly HashSet<string> collapsedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Folder multi-select (Shift/Ctrl + click on group headers) feeding the
        // folder context menu; independent of the image selection.
        private readonly HashSet<string> selectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<DatasetFolderEntry> folders = Array.Empty<DatasetFolderEntry>();
        private IReadOnlyList<DataItem> items = Array.Empty<DataItem>();
        private string datasetRoot = string.Empty;
        private string activeFolder;
        private int totalImageCount;
        private string allText = "All";
        private string rootText = "(root)";
        private string countFormat = "{0}";
        private string anchorPath;
        private int thumbnailHeight;
        private string lastToolTipText = string.Empty;
        // Lazily filled per visible row ("WEBP · 1200×1600 · 356 KB");
        // cleared on every SetRows so edits/overwrites refresh naturally.
        private readonly Dictionary<string, string> fileInfoCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // A freshly loaded multi-folder dataset starts with every group folded.
        private bool defaultCollapsePending = true;
        private readonly GlyphButton expandAllButton;
        private readonly GlyphButton collapseAllButton;

        public DatasetBrowserView()
        {
            thumbnailHeight = LogicalToDeviceUnits(96);
            list = new BrowserListBox(this) { Dock = DockStyle.Fill };
            list.MouseMoveHover += List_HoverChanged;

            searchBox = new TextBox { BorderStyle = BorderStyle.None };
            searchBox.TextChanged += (_, _) => Rebuild();

            expandAllButton = new GlyphButton(expanded: true) { Visible = false, Cursor = Cursors.Hand };
            expandAllButton.Click += (_, _) => ExpandAllFolders();
            collapseAllButton = new GlyphButton(expanded: false) { Visible = false, Cursor = Cursors.Hand };
            collapseAllButton.Click += (_, _) => CollapseAllFolders();

            searchHost = new BufferedPanel { Dock = DockStyle.Top };
            searchHost.Paint += SearchHost_Paint;
            searchHost.Resize += (_, _) => LayoutSearchBox();
            searchHost.Controls.Add(searchBox);
            searchHost.Controls.Add(expandAllButton);
            searchHost.Controls.Add(collapseAllButton);
            searchBox.BackColorChanged += (_, _) => searchHost.Invalidate();
            list.BackColorChanged += (_, _) => { searchHost.Invalidate(); list.Invalidate(); };
            list.ForeColorChanged += (_, _) => { searchHost.Invalidate(); list.Invalidate(); };

            // The control added last docks first: the search strip claims the
            // top edge before the list fills the remainder.
            Controls.Add(list);
            Controls.Add(searchHost);
            searchHost.Height = LogicalToDeviceUnits(46);
            LayoutSearchBox();
        }

        public void SetTexts(string all, string root, string imageCountFormat, string searchPlaceholder)
        {
            allText = string.IsNullOrEmpty(all) ? "All" : all;
            rootText = string.IsNullOrEmpty(root) ? "(root)" : root;
            countFormat = string.IsNullOrEmpty(imageCountFormat) ? "{0}" : imageCountFormat;
            searchBox.PlaceholderText = searchPlaceholder ?? string.Empty;
            Rebuild();
        }

        /// <summary>
        /// Replaces the displayed data. <paramref name="dataItems"/> is the
        /// hidden grid's current (already scope-filtered) row list; folder
        /// headers always come from the full dataset so out-of-scope folders
        /// stay clickable. Collapse state, search text and scroll position
        /// survive; the scoped folder is auto-expanded.
        /// </summary>
        public void SetRows(
            IReadOnlyList<DatasetFolderEntry> folderEntries,
            IReadOnlyList<DataItem> dataItems,
            string root,
            string scopedFolder,
            int datasetTotalCount,
            IEnumerable<string> selectedImagePaths)
        {
            folders = folderEntries ?? Array.Empty<DatasetFolderEntry>();
            items = dataItems ?? Array.Empty<DataItem>();
            datasetRoot = root ?? string.Empty;
            string normalized = DatasetFolderIndex.NormalizeRelative(scopedFolder);
            activeFolder = normalized.Length == 0 ? null : normalized;
            totalImageCount = datasetTotalCount;
            fileInfoCache.Clear();
            if (defaultCollapsePending)
            {
                defaultCollapsePending = false;
                if (folders.Count > 1)
                {
                    collapsedFolders.Clear();
                    foreach (DatasetFolderEntry folder in folders)
                        collapsedFolders.Add(folder.RelativePath ?? string.Empty);
                }
            }
            if (activeFolder != null)
                collapsedFolders.Remove(activeFolder);
            selectedFolders.RemoveWhere(key =>
                !folders.Any(folder => FolderEquals(folder.RelativePath ?? string.Empty, key)));
            selectedPaths.Clear();
            foreach (string path in selectedImagePaths ?? Enumerable.Empty<string>())
                selectedPaths.Add(path);
            Rebuild();
        }

        public void Clear()
        {
            folders = Array.Empty<DatasetFolderEntry>();
            items = Array.Empty<DataItem>();
            activeFolder = null;
            totalImageCount = 0;
            selectedPaths.Clear();
            selectedFolders.Clear();
            anchorPath = null;
            defaultCollapsePending = true;
            Rebuild();
        }

        public void ExpandAllFolders()
        {
            collapsedFolders.Clear();
            Rebuild();
        }

        public void CollapseAllFolders()
        {
            collapsedFolders.Clear();
            foreach (DatasetFolderEntry folder in folders)
                collapsedFolders.Add(folder.RelativePath ?? string.Empty);
            Rebuild();
        }

        /// <summary>Localized tooltips for the expand-all / collapse-all buttons.</summary>
        public void SetFolderButtonTexts(string expandAll, string collapseAll)
        {
            rowToolTip.SetToolTip(expandAllButton, expandAll ?? string.Empty);
            rowToolTip.SetToolTip(collapseAllButton, collapseAll ?? string.Empty);
        }

        public IReadOnlyList<string> GetSelectedImagePaths()
        {
            return rows.Where(row => row.Kind == RowKind.Image && selectedPaths.Contains(row.Item.ImageFilePath))
                .Select(row => row.Item.ImageFilePath)
                .ToList();
        }

        /// <summary>Mirror of an external (grid-side) selection change; raises no event.</summary>
        public void SetSelectedImagePaths(IEnumerable<string> paths)
        {
            var incoming = new HashSet<string>(paths ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (incoming.SetEquals(selectedPaths))
                return;
            selectedPaths.Clear();
            foreach (string path in incoming)
                selectedPaths.Add(path);
            list.Invalidate();
        }

        /// <summary>Thumbnail row height in device pixels (from the zoom slider).</summary>
        public void SetThumbnailHeight(int devicePixels)
        {
            int clamped = Math.Max(LogicalToDeviceUnits(24), Math.Min(250, devicePixels));
            if (clamped == thumbnailHeight)
                return;
            thumbnailHeight = clamped;
            Rebuild();
        }

        public void FocusList()
        {
            list.Focus();
        }

        private enum RowKind { All, Group, Image }

        private sealed class Row
        {
            public Row(RowKind kind, string folderKey, string display, string countText, DataItem item, bool hasChildren)
            {
                Kind = kind;
                FolderKey = folderKey;
                Display = display;
                CountText = countText;
                Item = item;
                HasChildren = hasChildren;
            }

            public RowKind Kind { get; }
            public string FolderKey { get; }
            public string Display { get; }
            public string CountText { get; }
            public DataItem Item { get; }
            public bool HasChildren { get; }
        }

        private bool Grouped => folders.Count > 1;

        private void Rebuild()
        {
            bool grouped = Grouped;
            if (expandAllButton.Visible != grouped)
            {
                expandAllButton.Visible = grouped;
                collapseAllButton.Visible = grouped;
                LayoutSearchBox();
                searchHost.Invalidate();
            }
            list.BeginUpdate();
            try
            {
                int topIndex = list.TopIndex;
                rows.Clear();
                list.Items.Clear();
                string filter = searchBox.Text.Trim();
                if (Grouped)
                    BuildGroupedRows(filter);
                else
                    BuildFlatRows(filter);
                if (rows.Count > 0)
                    list.Items.AddRange(rows.Cast<object>().ToArray());
                if (topIndex > 0 && topIndex < list.Items.Count)
                    list.TopIndex = topIndex;
            }
            finally
            {
                list.EndUpdate();
            }
        }

        private void BuildFlatRows(string filter)
        {
            foreach (DataItem item in items)
            {
                if (MatchesFilter(item, filter))
                    rows.Add(new Row(RowKind.Image, string.Empty, item.Name ?? string.Empty, null, item, false));
            }
        }

        private void BuildGroupedRows(string filter)
        {
            rows.Add(new Row(RowKind.All, null, allText, FormatCount(totalImageCount), null, false));
            ILookup<string, DataItem> byFolder = items.ToLookup(
                item => DatasetFolderIndex.GetRelativeFolder(item.ImageFilePath, datasetRoot),
                StringComparer.OrdinalIgnoreCase);
            foreach (DatasetFolderEntry folder in folders)
            {
                string key = folder.RelativePath ?? string.Empty;
                string display = key.Length == 0 ? rootText : key;
                List<DataItem> children = byFolder[key].ToList();
                bool groupMatches = filter.Length == 0
                    || display.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                List<DataItem> visibleChildren = groupMatches
                    ? children
                    : children.Where(item => MatchesFilter(item, filter)).ToList();
                if (filter.Length > 0 && !groupMatches && visibleChildren.Count == 0)
                    continue;
                rows.Add(new Row(RowKind.Group, key, display, FormatCount(folder.ImageCount), null, children.Count > 0));
                if (children.Count > 0 && !collapsedFolders.Contains(key))
                {
                    foreach (DataItem item in visibleChildren)
                        rows.Add(new Row(RowKind.Image, key, item.Name ?? string.Empty, null, item, false));
                }
            }
        }

        private static bool MatchesFilter(DataItem item, string filter)
        {
            if (filter.Length == 0)
                return true;
            return (item.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.ImageFilePath ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
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

        // ---- interaction ------------------------------------------------

        private int RowHeight(Row row)
        {
            int headerHeight = Math.Max(LogicalToDeviceUnits(30), Font.Height + LogicalToDeviceUnits(12));
            return row.Kind == RowKind.Image ? thumbnailHeight + LogicalToDeviceUnits(8) : headerHeight;
        }

        private void HandleMouseDown(MouseEventArgs e)
        {
            list.Focus();
            int index = HitTestRow(e.Location);
            if (index < 0)
                return;
            Row row = rows[index];
            if (e.Button == MouseButtons.Left)
            {
                switch (row.Kind)
                {
                    case RowKind.All:
                        ClearFolderSelection();
                        if (activeFolder != null)
                            FolderScopeSelected?.Invoke(null);
                        break;
                    case RowKind.Group:
                        if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                        {
                            // Shift/Ctrl + click toggles folder multi-select
                            // without touching scope or collapse state.
                            if (!selectedFolders.Remove(row.FolderKey))
                                selectedFolders.Add(row.FolderKey);
                            list.Invalidate();
                        }
                        else
                        {
                            ClearFolderSelection();
                            HandleGroupClick(index, row, e.Location);
                        }
                        break;
                    case RowKind.Image:
                        ClearFolderSelection();
                        HandleImageClick(index, row);
                        break;
                }
            }
            else if (e.Button == MouseButtons.Right && row.Kind == RowKind.Image)
            {
                if (!selectedPaths.Contains(row.Item.ImageFilePath))
                {
                    selectedPaths.Clear();
                    selectedPaths.Add(row.Item.ImageFilePath);
                    anchorPath = row.Item.ImageFilePath;
                    list.Invalidate();
                    SelectionChangedByUser?.Invoke();
                }
                ImageContextRequested?.Invoke(list.PointToScreen(e.Location));
            }
            else if (e.Button == MouseButtons.Right && row.Kind == RowKind.Group)
            {
                // A right-click outside the current multi-select retargets it,
                // like Explorer; inside it, the menu acts on the whole set.
                if (!selectedFolders.Contains(row.FolderKey))
                {
                    selectedFolders.Clear();
                    selectedFolders.Add(row.FolderKey);
                    list.Invalidate();
                }
                FolderContextRequested?.Invoke(OrderedSelectedFolders(), list.PointToScreen(e.Location));
            }
            else if (e.Button == MouseButtons.Right && row.Kind == RowKind.All)
            {
                FolderContextRequested?.Invoke(null, list.PointToScreen(e.Location));
            }
        }

        private void ClearFolderSelection()
        {
            if (selectedFolders.Count == 0)
                return;
            selectedFolders.Clear();
            list.Invalidate();
        }

        private IReadOnlyList<string> OrderedSelectedFolders()
        {
            return folders
                .Select(folder => folder.RelativePath ?? string.Empty)
                .Where(key => selectedFolders.Contains(key))
                .ToList();
        }

        private void HandleGroupClick(int index, Row row, Point location)
        {
            bool isActive = FolderEquals(row.FolderKey, activeFolder);
            bool chevronZone = location.X - list.GetItemRectangle(index).X < LogicalToDeviceUnits(24);
            if ((chevronZone || isActive) && row.HasChildren)
            {
                if (!collapsedFolders.Remove(row.FolderKey))
                    collapsedFolders.Add(row.FolderKey);
                Rebuild();
                return;
            }
            if (!isActive)
            {
                collapsedFolders.Remove(row.FolderKey);
                FolderScopeSelected?.Invoke(row.FolderKey);
            }
        }

        private void HandleImageClick(int index, Row row)
        {
            string path = row.Item.ImageFilePath;
            bool control = (ModifierKeys & Keys.Control) == Keys.Control;
            bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (shift)
            {
                SelectRange(anchorPath ?? path, path);
            }
            else if (control)
            {
                if (!selectedPaths.Remove(path))
                    selectedPaths.Add(path);
                anchorPath = path;
            }
            else
            {
                selectedPaths.Clear();
                selectedPaths.Add(path);
                anchorPath = path;
            }
            list.Invalidate();
            SelectionChangedByUser?.Invoke();
        }

        private void SelectRange(string fromPath, string toPath)
        {
            List<int> imageIndexes = ImageRowIndexes();
            int from = imageIndexes.FindIndex(i => PathEquals(rows[i].Item.ImageFilePath, fromPath));
            int to = imageIndexes.FindIndex(i => PathEquals(rows[i].Item.ImageFilePath, toPath));
            if (to < 0)
                return;
            if (from < 0)
                from = to;
            selectedPaths.Clear();
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                selectedPaths.Add(rows[imageIndexes[i]].Item.ImageFilePath);
        }

        private void HandleDoubleClick(MouseEventArgs e)
        {
            int index = HitTestRow(e.Location);
            if (index >= 0 && rows[index].Kind == RowKind.Image && e.Button == MouseButtons.Left)
                ImageActivated?.Invoke(rows[index].Item.ImageFilePath);
        }

        private void HandleKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A && e.Control)
            {
                selectedPaths.Clear();
                foreach (int i in ImageRowIndexes())
                    selectedPaths.Add(rows[i].Item.ImageFilePath);
                list.Invalidate();
                SelectionChangedByUser?.Invoke();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                MoveCaret(e.KeyCode == Keys.Down ? 1 : -1, e.Shift);
                e.Handled = true;
            }
            BrowserKeyDown?.Invoke(this, e);
        }

        private void MoveCaret(int direction, bool extendRange)
        {
            List<int> imageIndexes = ImageRowIndexes();
            if (imageIndexes.Count == 0)
                return;
            int current = imageIndexes.FindIndex(i => PathEquals(rows[i].Item.ImageFilePath, anchorLastPath ?? anchorPath));
            int next = current < 0
                ? (direction > 0 ? 0 : imageIndexes.Count - 1)
                : Math.Max(0, Math.Min(imageIndexes.Count - 1, current + direction));
            string nextPath = rows[imageIndexes[next]].Item.ImageFilePath;
            if (extendRange)
            {
                SelectRange(anchorPath ?? nextPath, nextPath);
            }
            else
            {
                selectedPaths.Clear();
                selectedPaths.Add(nextPath);
                anchorPath = nextPath;
            }
            anchorLastPath = nextPath;
            EnsureVisible(imageIndexes[next]);
            list.Invalidate();
            SelectionChangedByUser?.Invoke();
        }

        // Last caret position for keyboard navigation (shift-extend keeps the
        // original anchor while the caret walks).
        private string anchorLastPath;

        private void EnsureVisible(int index)
        {
            if (index < 0 || index >= list.Items.Count)
                return;
            if (index < list.TopIndex)
            {
                list.TopIndex = index;
                return;
            }
            int guard = 0;
            while (list.GetItemRectangle(index).Bottom > list.ClientSize.Height
                && list.TopIndex < index && guard++ < 1000)
            {
                list.TopIndex++;
            }
        }

        private List<int> ImageRowIndexes()
        {
            var result = new List<int>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Kind == RowKind.Image)
                    result.Add(i);
            }
            return result;
        }

        private int HitTestRow(Point location)
        {
            int index = list.IndexFromPoint(location);
            if (index < 0 || index >= rows.Count || !list.GetItemRectangle(index).Contains(location))
                return -1;
            return index;
        }

        private void List_HoverChanged(int index)
        {
            string tip = string.Empty;
            if (index >= 0 && index < rows.Count)
            {
                Row row = rows[index];
                tip = row.Kind == RowKind.Image ? row.Item.ImageFilePath ?? string.Empty : row.Display ?? string.Empty;
            }
            if (string.Equals(tip, lastToolTipText, StringComparison.Ordinal))
                return;
            lastToolTipText = tip;
            rowToolTip.SetToolTip(list, tip);
        }

        private static bool FolderEquals(string left, string right)
        {
            string l = DatasetFolderIndex.NormalizeRelative(left);
            string r = DatasetFolderIndex.NormalizeRelative(right);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathEquals(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        // ---- painting ---------------------------------------------------

        private void DrawRow(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= rows.Count)
                return;
            Row row = rows[e.Index];
            Graphics g = e.Graphics;
            Rectangle bounds = e.Bounds;
            Color bg = list.BackColor;
            Color fg = list.ForeColor;
            switch (row.Kind)
            {
                case RowKind.All:
                case RowKind.Group:
                    DrawHeaderRow(g, bounds, row, bg, fg);
                    break;
                case RowKind.Image:
                    DrawImageRow(g, bounds, row, bg, fg);
                    break;
            }
        }

        private void DrawHeaderRow(Graphics g, Rectangle bounds, Row row, Color bg, Color fg)
        {
            // Only one header may read as "current scope": the pinned All row
            // when unscoped, else the scoped folder (the root group normalizes
            // to null scope, so it must not light up alongside All).
            bool active = row.Kind == RowKind.All
                ? activeFolder == null
                : activeFolder != null && FolderEquals(row.FolderKey, activeFolder);
            bool multiSelected = row.Kind == RowKind.Group && selectedFolders.Contains(row.FolderKey);
            Color back = multiSelected
                ? Blend(SystemColors.Highlight, bg, 0.22f)
                : active ? Blend(SystemColors.Highlight, bg, 0.16f) : bg;
            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, bounds);
            if (active || multiSelected)
            {
                using var accent = new SolidBrush(SystemColors.Highlight);
                g.FillRectangle(accent, new Rectangle(bounds.X, bounds.Y, LogicalToDeviceUnits(3), bounds.Height));
            }

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int glyphSize = LogicalToDeviceUnits(15);
            int glyphX;
            using (var pen = new Pen(Blend(fg, bg, row.Kind == RowKind.Group && !row.HasChildren ? 0.38f : 0.58f),
                Math.Max(1.5f, glyphSize / 10f))
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (row.Kind == RowKind.Group)
                {
                    bool expanded = row.HasChildren && !collapsedFolders.Contains(row.FolderKey);
                    DrawChevron(g, pen, new Rectangle(
                        bounds.X + LogicalToDeviceUnits(8),
                        bounds.Y + (bounds.Height - LogicalToDeviceUnits(8)) / 2,
                        LogicalToDeviceUnits(8), LogicalToDeviceUnits(8)), expanded);
                    glyphX = bounds.X + LogicalToDeviceUnits(24);
                    DrawFolderGlyph(g, pen, new Rectangle(
                        glyphX, bounds.Y + (bounds.Height - glyphSize) / 2, glyphSize, glyphSize));
                }
                else
                {
                    glyphX = bounds.X + LogicalToDeviceUnits(10);
                    DrawAllGlyph(g, pen, new Rectangle(
                        glyphX, bounds.Y + (bounds.Height - glyphSize) / 2, glyphSize, glyphSize));
                }
            }
            g.SmoothingMode = oldMode;

            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int countWidth = 0;
            if (!string.IsNullOrEmpty(row.CountText))
            {
                countWidth = TextRenderer.MeasureText(row.CountText, Font,
                    new Size(int.MaxValue, bounds.Height),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                var countRect = new Rectangle(bounds.X, bounds.Y, bounds.Width - LogicalToDeviceUnits(10), bounds.Height);
                TextRenderer.DrawText(g, row.CountText, Font, countRect, Blend(fg, bg, 0.55f),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
            int textLeft = glyphX + glyphSize + LogicalToDeviceUnits(8);
            int textRight = bounds.Right - LogicalToDeviceUnits(10) - countWidth - LogicalToDeviceUnits(8);
            var nameRect = new Rectangle(textLeft, bounds.Y, Math.Max(0, textRight - textLeft), bounds.Height);
            TextRenderer.DrawText(g, row.Display, Font, nameRect, fg, flags);
        }

        private void DrawImageRow(Graphics g, Rectangle bounds, Row row, Color bg, Color fg)
        {
            bool selected = selectedPaths.Contains(row.Item.ImageFilePath);
            Color back = selected ? Blend(SystemColors.Highlight, bg, 0.22f) : bg;
            using (var brush = new SolidBrush(back))
                g.FillRectangle(brush, bounds);
            if (selected)
            {
                using var accent = new SolidBrush(SystemColors.Highlight);
                g.FillRectangle(accent, new Rectangle(bounds.X, bounds.Y, LogicalToDeviceUnits(3), bounds.Height));
            }

            int indent = Grouped ? LogicalToDeviceUnits(28) : LogicalToDeviceUnits(8);
            int box = bounds.Height - LogicalToDeviceUnits(6);
            var thumbBox = new Rectangle(bounds.X + indent, bounds.Y + (bounds.Height - box) / 2, box, box);
            DrawThumbnail(g, row.Item, thumbBox, Blend(fg, bg, 0.30f));

            const TextFormatFlags measureFlags = TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            string info = GetFileInfoLine(row.Item.ImageFilePath);
            int infoWidth = info.Length > 0
                ? TextRenderer.MeasureText(info, Font, new Size(int.MaxValue, bounds.Height), measureFlags).Width
                : 0;
            if (infoWidth > 0)
            {
                var infoRect = new Rectangle(
                    bounds.X, bounds.Y, bounds.Width - LogicalToDeviceUnits(10), bounds.Height);
                TextRenderer.DrawText(g, info, Font, infoRect, Blend(fg, bg, 0.55f),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            int nameLeft = thumbBox.Right + LogicalToDeviceUnits(9);
            int nameRight = bounds.Right - LogicalToDeviceUnits(10)
                - infoWidth - (infoWidth > 0 ? LogicalToDeviceUnits(10) : 0);
            var nameRect = new Rectangle(
                nameLeft, bounds.Y, Math.Max(0, nameRight - nameLeft), bounds.Height);
            TextRenderer.DrawText(g, row.Display, Font, nameRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        /// <summary>
        /// "WEBP · 1200×1600 · 356 KB" for images (header-only decode),
        /// "MP4 · 8.2 MB" for videos; computed once per path per rebuild.
        /// </summary>
        private string GetFileInfoLine(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            if (fileInfoCache.TryGetValue(path, out string cached))
                return cached;
            string line = string.Empty;
            try
            {
                var file = new FileInfo(path);
                if (file.Exists)
                {
                    string extension = file.Extension.TrimStart('.').ToUpperInvariant();
                    string size = FormatFileSize(file.Length);
                    string dimensions = string.Empty;
                    if (!VideoProcessingService.IsVideoFile(path))
                    {
                        try
                        {
                            SixLabors.ImageSharp.ImageInfo imageInfo = SixLabors.ImageSharp.Image.Identify(path);
                            if (imageInfo != null)
                                dimensions = imageInfo.Width + "×" + imageInfo.Height;
                        }
                        catch (Exception)
                        {
                            // Unreadable header: extension + size still show.
                        }
                    }
                    line = dimensions.Length > 0
                        ? extension + " · " + dimensions + " · " + size
                        : extension + " · " + size;
                }
            }
            catch (Exception)
            {
                // Stat failure (deleted/locked): cache the empty line.
            }
            fileInfoCache[path] = line;
            return line;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
            if (bytes >= 1024L * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.#") + " MB";
            if (bytes >= 1024)
                return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private static void DrawThumbnail(Graphics g, DataItem item, Rectangle box, Color placeholderColor)
        {
            Image image = item.Img;
            if (image != null)
            {
                try
                {
                    Size size = image.Size;
                    float scale = Math.Min((float)box.Width / size.Width, (float)box.Height / size.Height);
                    int w = Math.Max(1, (int)(size.Width * scale));
                    int h = Math.Max(1, (int)(size.Height * scale));
                    var dest = new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                    InterpolationMode oldInterpolation = g.InterpolationMode;
                    PixelOffsetMode oldOffset = g.PixelOffsetMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, dest);
                    g.InterpolationMode = oldInterpolation;
                    g.PixelOffsetMode = oldOffset;
                    return;
                }
                catch (ArgumentException)
                {
                    // Disposed bitmap (dataset being swapped): fall through to
                    // the placeholder instead of crashing the paint cycle.
                }
            }
            using var pen = new Pen(placeholderColor);
            g.DrawRectangle(pen, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1));
        }

        private static void DrawChevron(Graphics g, Pen pen, Rectangle box, bool expanded)
        {
            if (expanded)
            {
                g.DrawLines(pen, new[]
                {
                    new Point(box.X, box.Y + box.Height / 4),
                    new Point(box.X + box.Width / 2, box.Y + box.Height * 3 / 4),
                    new Point(box.Right, box.Y + box.Height / 4)
                });
            }
            else
            {
                g.DrawLines(pen, new[]
                {
                    new Point(box.X + box.Width / 4, box.Y),
                    new Point(box.X + box.Width * 3 / 4, box.Y + box.Height / 2),
                    new Point(box.X + box.Width / 4, box.Bottom)
                });
            }
        }

        private static void DrawFolderGlyph(Graphics g, Pen pen, Rectangle bounds)
        {
            float x = bounds.X;
            float y = bounds.Y + bounds.Height * 0.12f;
            float w = bounds.Width;
            float h = bounds.Height * 0.76f;
            using var path = new GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(x, y),
                new PointF(x + w * 0.38f, y),
                new PointF(x + w * 0.48f, y + h * 0.26f),
                new PointF(x + w, y + h * 0.26f),
                new PointF(x + w, y + h),
                new PointF(x, y + h)
            });
            path.CloseFigure();
            g.DrawPath(pen, path);
        }

        private static void DrawAllGlyph(Graphics g, Pen pen, Rectangle bounds)
        {
            float gap = bounds.Width * 0.20f;
            float cell = (bounds.Width - gap) / 2f;
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 2; c++)
                {
                    var rect = new RectangleF(bounds.X + c * (cell + gap), bounds.Y + r * (cell + gap), cell, cell);
                    using GraphicsPath path = RoundedRect(rect, cell * 0.22f);
                    g.DrawPath(pen, path);
                }
            }
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
            Color iconColor = Blend(fg, surface, 0.45f);
            int glyph = LogicalToDeviceUnits(13);
            int lens = LogicalToDeviceUnits(9);
            int ix = box.X + LogicalToDeviceUnits(10);
            int iy = box.Y + (box.Height - glyph) / 2;
            using var iconPen = new Pen(iconColor, Math.Max(1.6f, LogicalToDeviceUnits(16) / 10f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawEllipse(iconPen, new Rectangle(ix, iy, lens, lens));
            g.DrawLine(iconPen, ix + lens - 1, iy + lens - 1, ix + glyph, iy + glyph);
        }

        private Rectangle SearchBoxBounds()
        {
            int marginX = LogicalToDeviceUnits(10);
            int marginTop = LogicalToDeviceUnits(10);
            int marginBottom = LogicalToDeviceUnits(6);
            int buttonArea = expandAllButton != null && expandAllButton.Visible
                ? (LogicalToDeviceUnits(26) + LogicalToDeviceUnits(4)) * 2
                : 0;
            return new Rectangle(
                marginX, marginTop,
                searchHost.ClientSize.Width - marginX * 2 - buttonArea,
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
            int size = LogicalToDeviceUnits(26);
            int gap = LogicalToDeviceUnits(4);
            int buttonY = box.Y + (box.Height - size) / 2;
            collapseAllButton.SetBounds(
                searchHost.ClientSize.Width - LogicalToDeviceUnits(10) - size, buttonY, size, size);
            expandAllButton.SetBounds(collapseAllButton.Left - gap - size, buttonY, size, size);
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

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            searchHost.Height = LogicalToDeviceUnits(46);
            LayoutSearchBox();
            Rebuild();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                rowToolTip.Dispose();
            base.Dispose(disposing);
        }

        private class BufferedPanel : Panel
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
        /// Flat double-chevron button: chevrons down = expand all groups,
        /// chevrons up = collapse all. Colors derive from the list's pair.
        /// </summary>
        private sealed class GlyphButton : BufferedPanel
        {
            private readonly bool expandGlyph;
            private bool hover;

            public GlyphButton(bool expanded)
            {
                expandGlyph = expanded;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hover = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hover = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var view = Parent?.Parent as DatasetBrowserView;
                Color bg = view?.list.BackColor ?? BackColor;
                Color fg = view?.list.ForeColor ?? ForeColor;
                Graphics g = e.Graphics;
                g.Clear(bg);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (hover)
                {
                    using GraphicsPath rounded = RoundedRect(
                        new RectangleF(0, 0, Width - 1, Height - 1), Width * 0.2f);
                    using var fill = new SolidBrush(Blend(fg, bg, 0.08f));
                    g.FillPath(fill, rounded);
                }
                using var pen = new Pen(Blend(fg, bg, 0.55f), Math.Max(1.6f, Width / 16f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                int half = Width / 2;
                int span = Math.Max(3, Width * 5 / 16);
                int rise = Math.Max(2, Width * 3 / 16);
                int step = rise + Math.Max(2, Width / 8);
                int top = Height / 2 - step + rise / 2;
                for (int i = 0; i < 2; i++)
                {
                    int y = top + i * step;
                    if (expandGlyph)
                    {
                        g.DrawLines(pen, new[]
                        {
                            new Point(half - span, y),
                            new Point(half, y + rise),
                            new Point(half + span, y)
                        });
                    }
                    else
                    {
                        g.DrawLines(pen, new[]
                        {
                            new Point(half - span, y + rise),
                            new Point(half, y),
                            new Point(half + span, y + rise)
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Variable-height owner-drawn list with fully custom selection: the
        /// native selection is disabled and every mouse/key gesture is routed
        /// to the owner.
        /// </summary>
        private sealed class BrowserListBox : ListBox
        {
            private readonly DatasetBrowserView owner;
            private int hoverIndex = -1;

            public event Action<int> MouseMoveHover;

            public BrowserListBox(DatasetBrowserView owner)
            {
                this.owner = owner;
                DrawMode = DrawMode.OwnerDrawVariable;
                SelectionMode = SelectionMode.None;
                BorderStyle = BorderStyle.None;
                IntegralHeight = false;
            }

            protected override void OnMeasureItem(MeasureItemEventArgs e)
            {
                base.OnMeasureItem(e);
                if (e.Index >= 0 && e.Index < owner.rows.Count)
                    e.ItemHeight = Math.Min(250, owner.RowHeight(owner.rows[e.Index]));
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                base.OnDrawItem(e);
                owner.DrawRow(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                owner.HandleMouseDown(e);
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                owner.HandleDoubleClick(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                owner.HandleKeyDown(e);
                if (!e.Handled)
                    base.OnKeyDown(e);
            }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.Up || key == Keys.Down)
                    return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(owner.HitTestRow(e.Location));
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHover(-1);
            }

            private void SetHover(int index)
            {
                if (index == hoverIndex)
                    return;
                hoverIndex = index;
                MouseMoveHover?.Invoke(index);
            }
        }
    }
}
