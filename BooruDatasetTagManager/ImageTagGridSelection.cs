using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Selection helpers for the image-tags grid. Kept out of MainForm so
    /// Shift/Ctrl multi-select and drag-vs-range-select stay unit-testable.
    /// </summary>
    internal static class ImageTagGridSelection
    {
        public static int[] UniqueRowIndexes(IEnumerable<int> rowIndexes, bool descending)
        {
            if (rowIndexes == null)
                return Array.Empty<int>();
            IEnumerable<int> unique = rowIndexes.Where(i => i >= 0).Distinct();
            return descending
                ? unique.OrderByDescending(i => i).ToArray()
                : unique.OrderBy(i => i).ToArray();
        }

        /// <summary>
        /// Drag-reorder must not start while Shift/Ctrl is held (or while a
        /// range is already selected), otherwise a range-select click that
        /// moves a few pixels steals the selection.
        /// </summary>
        public static bool ShouldBeginRowDrag(
            Keys modifiers,
            MouseButtons buttons,
            Rectangle dragBox,
            Point mouse,
            int selectedRowCount)
        {
            if ((buttons & MouseButtons.Left) == 0)
                return false;
            if (dragBox.IsEmpty)
                return false;
            if (selectedRowCount > 1)
                return false;
            if ((modifiers & (Keys.Shift | Keys.Control)) != 0)
                return false;
            return !dragBox.Contains(mouse);
        }

        /// <summary>
        /// Visible row indexes between two positions, inclusive. Used when a
        /// category filter has hidden rows: native Shift-range select would
        /// try to select those hidden rows and throw.
        /// </summary>
        public static int[] VisibleRangeIndexes(
            IEnumerable<(int Index, bool Visible)> rows,
            int anchor,
            int clicked)
        {
            if (rows == null)
                return Array.Empty<int>();
            int from = Math.Min(anchor, clicked);
            int to = Math.Max(anchor, clicked);
            return rows
                .Where(row => row.Visible && row.Index >= from && row.Index <= to && row.Index >= 0)
                .Select(row => row.Index)
                .Distinct()
                .OrderBy(i => i)
                .ToArray();
        }
    }

    /// <summary>
    /// Image-tags grid: intercepts Shift+click when any row is hidden so the
    /// range covers only visible rows (category filter).
    /// </summary>
    internal sealed class ImageTagDataGridView : DataGridView
    {
        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left
                && e.RowIndex >= 0
                && e.RowIndex < Rows.Count
                && Rows[e.RowIndex].Visible
                && MultiSelect
                && (ModifierKeys & Keys.Shift) == Keys.Shift
                && HasAnyHiddenRow())
            {
                SelectVisibleRange(e.RowIndex);
                return;
            }
            base.OnCellMouseDown(e);
        }

        private bool HasAnyHiddenRow()
        {
            foreach (DataGridViewRow row in Rows)
            {
                if (!row.IsNewRow && !row.Visible)
                    return true;
            }
            return false;
        }

        private void SelectVisibleRange(int clickedRow)
        {
            int anchor = CurrentCell != null && CurrentCell.RowIndex >= 0
                ? CurrentCell.RowIndex
                : clickedRow;
            int col = CurrentCell != null ? CurrentCell.ColumnIndex : 0;
            if (col < 0 || col >= ColumnCount)
                col = 0;

            var visible = new List<(int Index, bool Visible)>(Rows.Count);
            foreach (DataGridViewRow row in Rows)
                visible.Add((row.Index, row.Visible && !row.IsNewRow));

            CurrentCell = Rows[clickedRow].Cells[col];
            ClearSelection();
            foreach (int index in ImageTagGridSelection.VisibleRangeIndexes(visible, anchor, clickedRow))
                Rows[index].Selected = true;
        }
    }
}
