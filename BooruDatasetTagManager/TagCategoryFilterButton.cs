using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Two-column category filter. Left: primaries with checkboxes (tick the
    /// whole L1). Right: that primary's secondaries, scrolled in-place so a
    /// long list never flips to a nested flyout. Multi-select stays open;
    /// "All categories" clears.
    /// </summary>
    public sealed class TagCategoryFilterButton : ToolStripDropDownButton
    {
        private readonly GeneralTagCategoryCatalog catalog;
        private readonly List<TagCategoryPath> selected = new List<TagCategoryPath>();
        private readonly PickerPanel picker;
        private readonly ToolStripControlHost host;

        public TagCategoryFilterButton(string name, GeneralTagCategoryCatalog catalog)
        {
            Name = name;
            this.catalog = catalog ?? GeneralTagCategoryCatalog.Empty;
            DisplayStyle = ToolStripItemDisplayStyle.Text;
            AutoSize = false;
            ShowDropDownArrow = true;
            DropDownDirection = ToolStripDropDownDirection.BelowRight;

            picker = new PickerPanel(this.catalog, selected);
            picker.SelectionChanged += (_, _) =>
            {
                UpdateButtonText();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            picker.CloseRequested += () =>
            {
                HideDropDown();
            };

            host = new ToolStripControlHost(picker)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            var dropDown = new ToolStripDropDown
            {
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoClose = true
            };
            dropDown.Items.Add(host);
            dropDown.Closing += DropDown_Closing;
            DropDown = dropDown;

            DropDownOpening += (_, _) =>
            {
                DropDownDirection = Alignment == ToolStripItemAlignment.Right
                    ? ToolStripDropDownDirection.BelowLeft
                    : ToolStripDropDownDirection.BelowRight;
                ApplyTheme();
                picker.PrepareToShow(GetCurrentParent());
                host.Size = picker.Size;
            };
            DropDownOpened += (_, _) => picker.FocusSearch();
            UpdateButtonText();
        }

        public IReadOnlyList<TagCategoryPath> SelectedCategories => selected;

        public event EventHandler SelectionChanged;

        public void ApplyLanguage()
        {
            picker.Reload();
            UpdateButtonText();
        }

        private void DropDown_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
                return;
            }
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && CursorIsOnPicker())
                e.Cancel = true;
        }

        private bool CursorIsOnPicker()
        {
            Point pos = Cursor.Position;
            if (DropDown.Visible && DropDown.Bounds.Contains(pos))
                return true;
            IntPtr hwnd = WindowFromPoint(pos);
            if (hwnd == IntPtr.Zero)
                return false;
            Control hit = Control.FromHandle(hwnd) ?? Control.FromChildHandle(hwnd);
            while (hit != null)
            {
                if (hit == picker || hit == DropDown)
                    return true;
                hit = hit.Parent;
            }
            return picker != null
                && picker.IsHandleCreated
                && picker.RectangleToScreen(picker.ClientRectangle).Contains(pos);
        }

        private void ApplyTheme()
        {
            var scheme = Program.ColorManager?.SelectedScheme;
            if (scheme == null)
            {
                picker.ApplyColors(SystemColors.Window, SystemColors.WindowText,
                    SystemColors.Window, SystemColors.WindowText);
                return;
            }
            picker.ApplyColors(
                scheme.ComboAndListBoxStyle.BackColor,
                scheme.ComboAndListBoxStyle.ForeColor,
                scheme.TextBoxStyle.BackColor,
                scheme.TextBoxStyle.ForeColor);
        }

        private void UpdateButtonText()
        {
            ToolTipText = I18n.GetText("AllTagsCategoryFilterTip");
            if (selected.Count == 0)
            {
                Text = I18n.GetText("TagCategoryAll");
                return;
            }
            if (selected.Count == 1)
            {
                TagCategoryPath path = selected[0];
                Text = path.FormatDisplay(LocalizedPrimary(path.L1));
                return;
            }
            Text = string.Format(I18n.GetText("TagCategorySelectedCount"), selected.Count);
            var names = new List<string>(selected.Count);
            foreach (TagCategoryPath path in selected)
                names.Add(path.FormatDisplay(LocalizedPrimary(path.L1)));
            ToolTipText = string.Join(", ", names);
        }

        internal static string LocalizedPrimary(string l1)
        {
            string key = TagCategoryTaxonomy.I18nKey(l1);
            if (string.IsNullOrEmpty(key))
                return l1;
            string text = I18n.GetText(key);
            return text == key ? l1 : text;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        private sealed class PickerPanel : UserControl
        {
            private readonly GeneralTagCategoryCatalog catalog;
            private readonly List<TagCategoryPath> selected;
            private readonly TextBox searchBox = new TextBox();
            private readonly CategoryListBox leftList = new CategoryListBox();
            private readonly CategoryListBox rightList = new CategoryListBox();
            private readonly TableLayoutPanel body = new TableLayoutPanel();
            private Control dpiSource;
            private string previewL1 = string.Empty;
            private Size glyphSize = new Size(13, 13);
            private int checkColWidth = 22;
            private bool rebuilding;

            public PickerPanel(GeneralTagCategoryCatalog catalog, List<TagCategoryPath> selected)
            {
                this.catalog = catalog;
                this.selected = selected;
                AutoScaleMode = AutoScaleMode.None;
                Padding = Padding.Empty;
                Margin = Padding.Empty;

                searchBox.BorderStyle = BorderStyle.FixedSingle;
                searchBox.Dock = DockStyle.Top;
                searchBox.TextChanged += SearchBox_TextChanged;
                searchBox.KeyDown += SearchBox_KeyDown;

                leftList.Dock = DockStyle.Fill;
                leftList.SelectionMode = SelectionMode.One;
                leftList.DrawItem += LeftList_DrawItem;
                leftList.MouseDown += LeftList_MouseDown;
                leftList.MouseMove += LeftList_MouseMove;
                leftList.MouseEnter += (_, _) => leftList.Focus();
                leftList.SelectedIndexChanged += LeftList_SelectedIndexChanged;
                leftList.KeyDown += LeftList_KeyDown;

                rightList.Dock = DockStyle.Fill;
                rightList.SelectionMode = SelectionMode.None;
                rightList.DrawItem += RightList_DrawItem;
                rightList.MouseDown += RightList_MouseDown;
                rightList.MouseEnter += (_, _) => rightList.Focus();
                rightList.KeyDown += RightList_KeyDown;

                body.ColumnCount = 2;
                body.RowCount = 1;
                body.Dock = DockStyle.Fill;
                body.Margin = Padding.Empty;
                body.Padding = Padding.Empty;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                body.Controls.Add(leftList, 0, 0);
                body.Controls.Add(rightList, 1, 0);

                Controls.Add(body);
                Controls.Add(searchBox);
            }

            public event EventHandler SelectionChanged;
            public event Action CloseRequested;

            public void FocusSearch()
            {
                searchBox.Focus();
                searchBox.SelectAll();
            }

            public void ApplyColors(Color listBack, Color listFore, Color textBack, Color textFore)
            {
                BackColor = listBack;
                ForeColor = listFore;
                body.BackColor = listBack;
                searchBox.BackColor = textBack;
                searchBox.ForeColor = textFore;
                leftList.BackColor = listBack;
                leftList.ForeColor = listFore;
                rightList.BackColor = listBack;
                rightList.ForeColor = listFore;
                leftList.Invalidate();
                rightList.Invalidate();
            }

            public void PrepareToShow(Control dpi)
            {
                dpiSource = dpi ?? Parent ?? this;
                if (dpiSource != null)
                    Font = dpiSource.Font;
                searchBox.PlaceholderText = I18n.GetText("TagCategorySearchHint");
                MeasureGlyph();
                Reload();
                Relayout();
            }

            public void Reload()
            {
                rebuilding = true;
                try
                {
                    string keepL1 = previewL1;
                    int caret = searchBox.SelectionStart;
                    string query = (searchBox.Text ?? string.Empty).Trim();
                    searchBox.PlaceholderText = I18n.GetText("TagCategorySearchHint");
                    BuildLeftRows(query);
                    int restore = IndexOfPrimary(keepL1);
                    if (query.Length > 0 && (restore <= 0 || keepL1.Length == 0))
                    {
                        int withKids = FirstWithChildren();
                        if (withKids >= 0)
                            restore = withKids;
                    }
                    if (restore < 0)
                        restore = 0;
                    if (leftList.Items.Count > 0)
                        leftList.SelectedIndex = Math.Min(restore, leftList.Items.Count - 1);
                    ShowSecondariesForSelection();
                    if (searchBox.Focused)
                        searchBox.SelectionStart = Math.Min(caret, searchBox.Text.Length);
                }
                finally
                {
                    rebuilding = false;
                }
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if (keyData == Keys.Escape)
                {
                    CloseRequested?.Invoke();
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            private void SearchBox_TextChanged(object sender, EventArgs e)
            {
                if (rebuilding)
                    return;
                Reload();
            }

            private void SearchBox_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseRequested?.Invoke();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Down && leftList.Items.Count > 0)
                {
                    leftList.Focus();
                    if (leftList.SelectedIndex < 0)
                        leftList.SelectedIndex = 0;
                    e.Handled = true;
                }
            }

            private void BuildLeftRows(string query)
            {
                bool searching = query.Length > 0;
                leftList.BeginUpdate();
                leftList.Items.Clear();
                leftList.Items.Add(new PrimaryRow
                {
                    L1 = string.Empty,
                    Text = I18n.GetText("TagCategoryAll")
                });
                foreach (string l1 in TagCategoryTaxonomy.MenuPrimaries(catalog))
                {
                    string localized = LocalizedPrimary(l1);
                    bool l1Hit = !searching || NameMatches(l1, query) || NameMatches(localized, query);
                    IReadOnlyList<string> children = catalog.SecondariesOf(l1);
                    var visible = new List<string>();
                    foreach (string l2 in children)
                    {
                        if (!searching || l1Hit || NameMatches(l2, query))
                            visible.Add(l2);
                    }
                    if (searching && !l1Hit && visible.Count == 0)
                        continue;
                    leftList.Items.Add(new PrimaryRow
                    {
                        L1 = l1,
                        Text = localized,
                        Secondaries = visible
                    });
                }
                leftList.EndUpdate();
            }

            private int IndexOfPrimary(string l1)
            {
                for (int i = 0; i < leftList.Items.Count; i++)
                {
                    if (leftList.Items[i] is PrimaryRow row
                        && string.Equals(row.L1, l1, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
                return -1;
            }

            private int FirstWithChildren()
            {
                for (int i = 0; i < leftList.Items.Count; i++)
                {
                    if (leftList.Items[i] is PrimaryRow row && row.HasChildren)
                        return i;
                }
                return -1;
            }

            private void Relayout()
            {
                int itemH = Math.Max(Scale(22), glyphSize.Height + Scale(8));
                leftList.ItemHeight = itemH;
                rightList.ItemHeight = itemH;
                int searchH = Scale(26);
                searchBox.Height = searchH;
                int rows = Math.Max(leftList.Items.Count, 8);
                int listH = rows * itemH;
                int screenH = (dpiSource ?? this).IsHandleCreated
                    ? Screen.FromControl(dpiSource ?? this).WorkingArea.Height
                    : 800;
                int maxListH = Math.Max(itemH * 8, (int)(screenH * 0.55) - searchH);
                listH = Math.Min(listH, maxListH);
                Size = new Size(Scale(336), searchH + listH + Scale(4));
            }

            private void MeasureGlyph()
            {
                glyphSize = new Size(Scale(13), Scale(13));
                if (IsHandleCreated && Application.RenderWithVisualStyles)
                {
                    using Graphics g = CreateGraphics();
                    glyphSize = CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedNormal);
                }
                checkColWidth = Math.Max(glyphSize.Width + Scale(8), Scale(22));
            }

            private int Scale(int px)
            {
                Control src = dpiSource ?? Parent ?? this;
                return src.IsHandleCreated ? src.LogicalToDeviceUnits(px) : px;
            }

            private void LeftList_MouseMove(object sender, MouseEventArgs e)
            {
                int index = leftList.IndexFromPoint(e.Location);
                if (index >= 0 && leftList.SelectedIndex != index)
                    leftList.SelectedIndex = index;
            }

            private void LeftList_SelectedIndexChanged(object sender, EventArgs e)
            {
                if (rebuilding)
                    return;
                ShowSecondariesForSelection();
            }

            private void LeftList_MouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                int index = leftList.IndexFromPoint(e.Location);
                if (index < 0 || leftList.Items[index] is not PrimaryRow row)
                    return;
                bool onCheck = e.X < checkColWidth;
                if (string.IsNullOrEmpty(row.L1) || onCheck || !row.HasChildren)
                    TogglePrimary(row);
            }

            private void LeftList_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space || leftList.SelectedIndex < 0)
                    return;
                if (leftList.Items[leftList.SelectedIndex] is PrimaryRow row)
                    TogglePrimary(row);
                e.Handled = true;
            }

            private void RightList_MouseDown(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                int index = rightList.IndexFromPoint(e.Location);
                if (index < 0)
                    return;
                ToggleSecondary(rightList.Items[index] as string);
            }

            private void RightList_KeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Space)
                    return;
                int index = rightList.IndexFromPoint(rightList.PointToClient(Cursor.Position));
                if (index < 0 && rightList.SelectedIndex >= 0)
                    index = rightList.SelectedIndex;
                if (index >= 0)
                    ToggleSecondary(rightList.Items[index] as string);
                e.Handled = true;
            }

            private void ShowSecondariesForSelection()
            {
                PrimaryRow row = leftList.SelectedIndex >= 0
                    && leftList.SelectedIndex < leftList.Items.Count
                    ? leftList.Items[leftList.SelectedIndex] as PrimaryRow
                    : null;
                previewL1 = row?.L1 ?? string.Empty;
                rightList.BeginUpdate();
                rightList.Items.Clear();
                if (row != null && row.HasChildren)
                {
                    foreach (string l2 in row.Secondaries)
                        rightList.Items.Add(l2);
                }
                rightList.EndUpdate();
            }

            private void TogglePrimary(PrimaryRow row)
            {
                if (row == null)
                    return;
                if (string.IsNullOrEmpty(row.L1))
                    selected.Clear();
                else
                    TagCategoryPath.ToggleIn(selected, new TagCategoryPath(row.L1), catalog.SecondariesOf(row.L1));
                RefreshChecks();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            private void ToggleSecondary(string l2)
            {
                if (string.IsNullOrEmpty(previewL1) || string.IsNullOrEmpty(l2))
                    return;
                TagCategoryPath.ToggleIn(
                    selected,
                    new TagCategoryPath(previewL1, l2),
                    catalog.SecondariesOf(previewL1));
                RefreshChecks();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            private void RefreshChecks()
            {
                leftList.Invalidate();
                rightList.Invalidate();
            }

            private void LeftList_DrawItem(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= leftList.Items.Count)
                    return;
                var row = (PrimaryRow)leftList.Items[e.Index];
                bool preview = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                DrawRow(e, row.Text, PrimaryCheckState(row), row.HasChildren, preview);
            }

            private void RightList_DrawItem(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= rightList.Items.Count)
                    return;
                string l2 = rightList.Items[e.Index] as string ?? string.Empty;
                CheckState state = SecondaryChecked(l2) ? CheckState.Checked : CheckState.Unchecked;
                DrawRow(e, l2, state, arrow: false, preview: false);
            }

            private void DrawRow(DrawItemEventArgs e, string text, CheckState check, bool arrow, bool preview)
            {
                Graphics g = e.Graphics;
                Color bg = BackColor;
                Color fg = ForeColor;
                Color rowBack = preview
                    ? Blend(SystemColors.Highlight, bg, 0.22f)
                    : check == CheckState.Checked
                        ? Blend(fg, bg, 0.06f)
                        : bg;
                using (var brush = new SolidBrush(rowBack))
                    g.FillRectangle(brush, e.Bounds);
                if (preview)
                {
                    using var accent = new SolidBrush(SystemColors.Highlight);
                    g.FillRectangle(accent, new Rectangle(e.Bounds.X, e.Bounds.Y, Scale(3), e.Bounds.Height));
                }

                int pad = Scale(4);
                int glyphX = e.Bounds.X + pad;
                int glyphY = e.Bounds.Y + Math.Max(0, (e.Bounds.Height - glyphSize.Height) / 2);
                DrawCheck(g, new Point(glyphX, glyphY), check);

                int arrowW = arrow ? Scale(14) : 0;
                var textBounds = new Rectangle(
                    glyphX + glyphSize.Width + pad,
                    e.Bounds.Y,
                    Math.Max(0, e.Bounds.Width - (glyphX + glyphSize.Width + pad) - arrowW - pad),
                    e.Bounds.Height);
                TextRenderer.DrawText(g, text, Font, textBounds, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                if (arrow)
                {
                    var arrowBounds = new Rectangle(
                        e.Bounds.Right - arrowW - pad, e.Bounds.Y, arrowW, e.Bounds.Height);
                    TextRenderer.DrawText(g, "▸", Font, arrowBounds, Blend(fg, bg, 0.45f),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }

            private CheckState PrimaryCheckState(PrimaryRow row)
            {
                if (string.IsNullOrEmpty(row.L1))
                    return selected.Count == 0 ? CheckState.Checked : CheckState.Unchecked;
                return TagCategoryPath.HasWholePrimary(selected, row.L1)
                    ? CheckState.Checked
                    : CheckState.Unchecked;
            }

            private bool SecondaryChecked(string l2)
            {
                return TagCategoryPath.HasWholePrimary(selected, previewL1)
                    || ContainsExact(new TagCategoryPath(previewL1, l2));
            }

            private bool ContainsExact(TagCategoryPath path)
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    if (selected[i].Equals(path))
                        return true;
                }
                return false;
            }

            private static void DrawCheck(Graphics g, Point location, CheckState state)
            {
                if (Application.RenderWithVisualStyles)
                {
                    CheckBoxState visual = state == CheckState.Checked
                        ? CheckBoxState.CheckedNormal
                        : state == CheckState.Indeterminate
                            ? CheckBoxState.MixedNormal
                            : CheckBoxState.UncheckedNormal;
                    CheckBoxRenderer.DrawCheckBox(g, location, visual);
                    return;
                }
                var rect = new Rectangle(location, new Size(13, 13));
                if (state == CheckState.Indeterminate)
                    ControlPaint.DrawMixedCheckBox(g, rect, ButtonState.Normal);
                else
                    ControlPaint.DrawCheckBox(g, rect, state == CheckState.Checked ? ButtonState.Checked : ButtonState.Normal);
            }

            private static bool NameMatches(string value, string query)
            {
                return !string.IsNullOrEmpty(value)
                    && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static Color Blend(Color a, Color b, float t)
            {
                t = Math.Max(0f, Math.Min(1f, t));
                return Color.FromArgb(
                    (int)(a.A * t + b.A * (1f - t)),
                    (int)(a.R * t + b.R * (1f - t)),
                    (int)(a.G * t + b.G * (1f - t)),
                    (int)(a.B * t + b.B * (1f - t)));
            }

            private sealed class PrimaryRow
            {
                public string L1;
                public string Text;
                public List<string> Secondaries = new List<string>();
                public bool HasChildren => Secondaries.Count > 0;
                public override string ToString() => Text;
            }

            private sealed class CategoryListBox : ListBox
            {
                public CategoryListBox()
                {
                    DrawMode = DrawMode.OwnerDrawFixed;
                    IntegralHeight = false;
                    BorderStyle = BorderStyle.None;
                    HorizontalScrollbar = false;
                }
            }
        }
    }
}
