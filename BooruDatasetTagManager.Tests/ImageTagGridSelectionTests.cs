using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Xunit;

namespace BooruDatasetTagManager.Tests;

public class ImageTagGridSelectionTests
{
    [Fact]
    public void UniqueRowIndexes_dedupes_and_orders_for_delete_and_copy()
    {
        int[] descending = ImageTagGridSelection.UniqueRowIndexes(
            new[] { 2, 0, 2, 5, -1, 0 }, descending: true);
        Assert.Equal(new[] { 5, 2, 0 }, descending);

        int[] ascending = ImageTagGridSelection.UniqueRowIndexes(
            new[] { 2, 0, 2, 5, -1, 0 }, descending: false);
        Assert.Equal(new[] { 0, 2, 5 }, ascending);

        Assert.Empty(ImageTagGridSelection.UniqueRowIndexes(null, true));
        Assert.Empty(ImageTagGridSelection.UniqueRowIndexes(Array.Empty<int>(), false));
    }

    [Fact]
    public void ShouldBeginRowDrag_stays_off_for_shift_ctrl_and_range_select()
    {
        var box = new Rectangle(10, 10, 8, 8);
        var outside = new Point(40, 40);

        Assert.True(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.None, MouseButtons.Left, box, outside, selectedRowCount: 1));
        Assert.False(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.Shift, MouseButtons.Left, box, outside, selectedRowCount: 1));
        Assert.False(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.Control, MouseButtons.Left, box, outside, selectedRowCount: 1));
        Assert.False(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.None, MouseButtons.Left, box, outside, selectedRowCount: 3));
        Assert.False(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.None, MouseButtons.Left, box, new Point(12, 12), selectedRowCount: 1));
        Assert.False(ImageTagGridSelection.ShouldBeginRowDrag(
            Keys.None, MouseButtons.Left, Rectangle.Empty, outside, selectedRowCount: 1));
    }

    [Fact]
    public void VisibleRangeIndexes_skips_hidden_rows_between_anchor_and_click()
    {
        var rows = new (int Index, bool Visible)[]
        {
            (0, true),
            (1, false),
            (2, true),
            (3, false),
            (4, true)
        };
        Assert.Equal(new[] { 0, 2, 4 }, ImageTagGridSelection.VisibleRangeIndexes(rows, 0, 4));
        Assert.Equal(new[] { 2, 4 }, ImageTagGridSelection.VisibleRangeIndexes(rows, 4, 2));
        Assert.Equal(new[] { 0 }, ImageTagGridSelection.VisibleRangeIndexes(rows, 0, 1));
        Assert.Empty(ImageTagGridSelection.VisibleRangeIndexes(null, 0, 4));
    }

    [Fact]
    public void Image_tags_grid_enables_shift_multiselect_and_bulk_row_ops()
    {
        string designer = ReadMainSource("Form1.Designer.cs");
        Assert.Contains("gridViewTags = new ImageTagDataGridView();", designer);
        Assert.Contains("gridViewTags.MultiSelect = true;", designer);
        Assert.Contains(
            "gridViewTags.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;",
            designer);

        string source = ReadMainSource("Form1.cs");
        Assert.Contains("gridViewTags.MultiSelect = true;", source);
        Assert.Contains("gridViewTags.SelectionMode = DataGridViewSelectionMode.FullRowSelect;", source);

        int delete = source.IndexOf("private void BtnTagDelete_Click", StringComparison.Ordinal);
        Assert.True(delete >= 0);
        string deleteBody = source.Substring(delete, 700);
        Assert.Contains("GetSelectedImageTagRowIndexes(true)", deleteBody);
        Assert.DoesNotContain(
            "gridViewTags.Rows.RemoveAt(gridViewTags.SelectedCells[0].RowIndex)",
            deleteBody);

        int mouseMove = source.IndexOf("private void dataGridView1_MouseMove", StringComparison.Ordinal);
        Assert.True(mouseMove >= 0);
        Assert.Contains("ImageTagGridSelection.ShouldBeginRowDrag", source.Substring(mouseMove, 500));

        int copy = source.IndexOf("e.Control && e.KeyCode == Keys.C", StringComparison.Ordinal);
        Assert.True(copy >= 0);
        Assert.Contains("GetSelectedImageTagRowIndexes(false)", source.Substring(copy, 600));
    }

    private static string ReadMainSource(string fileName)
    {
        return File.ReadAllText(Path.Combine(RepoRoot(), "BooruDatasetTagManager", fileName), Encoding.UTF8);
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "BooruDatasetTagManager.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
