using BooruDatasetTagManager.AiApi;
using BooruDatasetTagManager.Properties;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
            previewPicBox = new PictureBox();
            previewPicBox.Name = "previewPicBox";
            allTagsFilter = new Form_filter();
            CreateLangMenuItems();
            InitHotkeyCommands();
            InitializeAiServerSetAndTestMenu();
            InitializeDatasetFolderList();
            //test color scheme
            //Program.ColorManager.SelectScheme("Dark");
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            Program.ColorManager.SchemeChanded += ColorManager_SchemeChanded;
            contextMenuImageGridHeader.ItemClicked += ContextMenuImageGridHeader_ItemClicked;
            gridViewDS.CellToolTipTextNeeded += GridViewDS_CellToolTipTextNeeded;
            gridViewDS.CellPainting += GridViewDS_CellPainting;
            gridViewTags.CellFormatting += GridViewTags_CellFormatting;
            InitializeTagContextMenu();
            InitializeAllTagsSearch();
            InitializeImageTagsSearch();
            InitializeTagCategoryUi();
            switchLanguage();
        }

        /// <summary>
        /// Multi-select view stores the tag text only on the first row of each
        /// tag group (the remaining rows exist per image and would otherwise look
        /// like empty rows); show the group tag dimmed on the continuation rows.
        /// </summary>
        private void GridViewTags_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            ApplyTagCategoryTint(e, GetTagsRowTag(e.RowIndex));
            if (gridViewTags.Columns[e.ColumnIndex].Name != "ImageTags")
                return;
            if (gridViewTags.DataSource is not MultiSelectDataTable dt)
                return;
            if (!string.IsNullOrEmpty(e.Value as string))
                return;
            if (e.RowIndex >= dt.Rows.Count)
                return;
            var row = dt.Rows[e.RowIndex] as MultiSelectDataRow;
            if (row == null || row.RowState == System.Data.DataRowState.Deleted || row.RowState == System.Data.DataRowState.Detached)
                return;
            string groupTag = row.GetTagText();
            if (string.IsNullOrEmpty(groupTag))
                return;
            e.Value = groupTag;
            e.CellStyle.ForeColor = Color.Gray;
            e.FormattingApplied = true;
        }

        private ToolStripMenuItem menuAiServerSet;
        private ToolStripMenuItem menuTestModule;
        private ToolStripMenuItem menuLlmTagger;
        private ToolStripMenuItem menuVideoConvert;
        private ToolStripMenuItem menuVideoExtract;
        private ToolStripMenuItem menuOnnxTagger;
        private ToolStripMenuItem menuContextDSVideoTools;
        private ToolStripMenuItem menuContextDSRetagOnnx;
        private ToolStripMenuItem menuContextDSRetagLlm;
        private ToolStripMenuItem menuContextDSEditImage;
        private DatasetBrowserView datasetBrowserView;
        private Splitter sidebarPreviewSplitter;
        private DatasetPreviewPanel datasetPreviewPanel;
        private List<string> lastEmbeddedPreviewPaths = new List<string>();
        private int lastExpandedPreviewHeight;
        private ContextMenuStrip folderContextMenu;
        private ToolStripMenuItem menuRenameFolder;
        private ToolStripMenuItem menuBatchRenameImages;
        private ToolStripMenuItem menuFolderTagOnnx;
        private ToolStripMenuItem menuFolderTagLlm;
        // Folder(s) the browser context menu was opened on: null = the pinned
        // "All" row (whole dataset), otherwise 1..n folder keys in display order.
        private List<string> folderContextKeys;

        private string SingleFolderContextKey =>
            folderContextKeys is { Count: 1 } ? folderContextKeys[0] : null;

        /// <summary>
        /// Folder right-click quick tagging. One folder: scopes to it (same as
        /// clicking its header) and preselects the "current folder" source.
        /// Several folders (Shift/Ctrl multi-select): widens scope to All,
        /// selects those folders' images and uses the default "selected
        /// images" source. The pinned All row: preselects "all dataset
        /// images". No path auto-runs — the user confirms settings and starts.
        /// </summary>
        private void TagContextFolder(bool useOnnx)
        {
            if (Program.DataManager == null)
                return;
            List<string> keys = folderContextKeys;
            // Union scope: one folder scopes to it, several scope to their
            // union (the AllTags counts and the tagger windows both honor it).
            Program.DataManager.SetActiveFolders(keys);
            RefreshDatasetGrid();
            if (keys is { Count: > 1 })
                SelectDatasetImagesInFolders(keys);
            int available = keys is { Count: > 1 }
                ? GetSelectedDatasetImagePaths().Count
                : Program.DataManager.GetActiveScopeCount();
            if (available == 0)
            {
                MessageBox.Show(I18n.GetText("TaggerNoImages"), I18n.GetText("UIError"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (useOnnx)
            {
                using Form_OnnxTagger form = new Form_OnnxTagger(this);
                PreselectContextSource(form.SelectFolderSource, form.SelectAllImagesSource, keys);
                form.ShowDialog(this);
            }
            else
            {
                using Form_LlmTagger form = new Form_LlmTagger(this);
                PreselectContextSource(form.SelectFolderSource, form.SelectAllImagesSource, keys);
                form.ShowDialog(this);
            }
        }

        private static void PreselectContextSource(Action selectFolder, Action selectAllImages, List<string> keys)
        {
            if (keys == null)
                selectAllImages();
            else if (keys.Count == 1)
                selectFolder();
            // Multi-folder: keep the default "selected images" source — the
            // folders' images were just selected in the grid.
        }

        /// <summary>
        /// Selects every dataset-grid row living in one of the given folders
        /// and mirrors the result into the browser once (the same suppression
        /// pattern BrowserSelection_Changed uses in the other direction).
        /// </summary>
        private void SelectDatasetImagesInFolders(IReadOnlyList<string> folderKeys)
        {
            var wanted = new HashSet<string>(
                folderKeys.Select(key =>
                {
                    // GetRelativeFolder reports root images as "": translate
                    // the explicit root sentinel back for the set lookup.
                    string normalized = DatasetFolderIndex.NormalizeRelative(key);
                    return normalized == DatasetFolderIndex.RootFolderKey ? string.Empty : normalized;
                }),
                StringComparer.OrdinalIgnoreCase);
            selectionMode = true;
            try
            {
                foreach (DataGridViewRow row in gridViewDS.Rows)
                {
                    bool select = row.Cells["ImageFilePath"].Value is string path
                        && wanted.Contains(DatasetFolderIndex.GetRelativeFolder(path, Program.DataManager.DatasetRoot));
                    if (row.Selected != select)
                        row.Selected = select;
                }
            }
            finally
            {
                selectionMode = false;
            }
            datasetBrowserView?.SetSelectedImagePaths(GetGridSelectedPaths());
            LoadSelectedImageToGrid();
        }

        /// <summary>
        /// Builds the unified dataset module: one browser (search box +
        /// collapsible folder groups + image rows) filling the pane, the
        /// embedded preview (the former right-pane "Preview" tab) docked
        /// below it. The legacy dataset grid stays parented but hidden — it
        /// remains the data/selection authority every existing operation
        /// reads; the browser mirrors its selection into it.
        /// </summary>
        private void InitializeDatasetFolderList()
        {
            datasetBrowserView = new DatasetBrowserView { Dock = DockStyle.Fill };
            datasetBrowserView.FolderScopeSelected += FolderList_FolderSelected;
            datasetBrowserView.FolderMultiScopeSelected += FolderList_FoldersSelected;
            datasetBrowserView.SelectionChangedByUser += BrowserSelection_Changed;
            datasetBrowserView.ImageActivated += path => ShowPreview(path, true);
            datasetBrowserView.ImageContextRequested += screenPoint => contextMenuStrip1.Show(screenPoint);
            datasetBrowserView.BrowserKeyDown += Browser_KeyDown;
            folderContextMenu = new ContextMenuStrip();
            menuRenameFolder = new ToolStripMenuItem { Name = "menuRenameFolder" };
            menuRenameFolder.Click += (_, _) => RenameDatasetFolder();
            folderContextMenu.Items.Add(menuRenameFolder);
            menuBatchRenameImages = new ToolStripMenuItem { Name = "menuBatchRenameImages" };
            menuBatchRenameImages.Click += (_, _) => BatchRenameFolderImages();
            folderContextMenu.Items.Add(menuBatchRenameImages);
            folderContextMenu.Items.Add(new ToolStripSeparator());
            menuFolderTagOnnx = new ToolStripMenuItem { Name = "menuFolderTagOnnx" };
            menuFolderTagOnnx.Click += (_, _) => TagContextFolder(useOnnx: true);
            folderContextMenu.Items.Add(menuFolderTagOnnx);
            menuFolderTagLlm = new ToolStripMenuItem { Name = "menuFolderTagLlm" };
            menuFolderTagLlm.Click += (_, _) => TagContextFolder(useOnnx: false);
            folderContextMenu.Items.Add(menuFolderTagLlm);
            datasetBrowserView.FolderContextRequested += (folderKeys, screenPoint) =>
            {
                folderContextKeys = folderKeys?.ToList();
                // Rename actions only make sense for exactly one real folder;
                // the tagging items handle one, many and All. The root group
                // (sentinel key) is a real scope but not a renamable directory.
                bool single = SingleFolderContextKey != null;
                menuRenameFolder.Enabled = single
                    && SingleFolderContextKey.Length > 0
                    && SingleFolderContextKey != DatasetFolderIndex.RootFolderKey;
                menuBatchRenameImages.Enabled = single;
                folderContextMenu.Show(screenPoint);
            };

            datasetPreviewPanel = new DatasetPreviewPanel { Dock = DockStyle.Bottom, Visible = false };
            datasetPreviewPanel.SetExpanded(Program.Settings.DatasetPreviewExpanded);
            datasetPreviewPanel.ToggleRequested += ToggleEmbeddedPreview;
            datasetPreviewPanel.OpenInWindowRequested += cellIndex =>
            {
                if (cellIndex >= 0 && cellIndex < lastEmbeddedPreviewPaths.Count)
                    ShowPreview(lastEmbeddedPreviewPaths[cellIndex], true);
            };
            sidebarPreviewSplitter = new Splitter
            {
                Dock = DockStyle.Bottom,
                Height = LogicalToDeviceUnits(5),
                MinSize = LogicalToDeviceUnits(120),
                Visible = false
            };
            sidebarPreviewSplitter.SplitterMoved += (_, _) =>
            {
                if (datasetPreviewPanel.Expanded)
                    lastExpandedPreviewHeight = datasetPreviewPanel.Height;
            };
            gridViewDS.Visible = false;
            // Dock order: the control added last docks first, so the preview
            // claims the pane bottom, the splitter sits above it and the
            // browser fills the rest (over the hidden grid).
            toolStripContainer3.ContentPanel.Controls.Add(datasetBrowserView);
            toolStripContainer3.ContentPanel.Controls.Add(sidebarPreviewSplitter);
            toolStripContainer3.ContentPanel.Controls.Add(datasetPreviewPanel);
            MenuShowPreview.Checked = Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow
                && Program.Settings.DatasetPreviewExpanded;
        }

        /// <summary>
        /// Mirrors the browser's selection into the hidden dataset grid (the
        /// authority all existing operations read), then reloads the tag pane
        /// once — the same suppression pattern the grid refresh uses.
        /// </summary>
        private void BrowserSelection_Changed()
        {
            var selected = new HashSet<string>(
                datasetBrowserView.GetSelectedImagePaths(), StringComparer.OrdinalIgnoreCase);
            selectionMode = true;
            try
            {
                for (int i = 0; i < gridViewDS.RowCount; i++)
                {
                    bool select = gridViewDS.Rows[i].Cells["ImageFilePath"].Value is string path
                        && selected.Contains(path);
                    if (gridViewDS.Rows[i].Selected != select)
                        gridViewDS.Rows[i].Selected = select;
                }
            }
            finally
            {
                selectionMode = false;
            }
            LoadSelectedImageToGrid();
        }

        private async void Browser_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteImage();
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                await PasteTagsFromClipboard();
                e.SuppressKeyPress = true;
            }
        }

        private List<string> GetGridSelectedPaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                if (gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value is string path)
                    paths.Add(path);
            }
            return paths;
        }

        /// <summary>
        /// Right-click → rename on a browser folder group: renames the leaf
        /// directory on disk and remaps the loaded dataset in place (unsaved
        /// tag edits survive), then rebuilds the grid and browser.
        /// </summary>
        private void RenameDatasetFolder()
        {
            string key = SingleFolderContextKey;
            if (Program.DataManager == null || string.IsNullOrEmpty(key)
                || key == DatasetFolderIndex.RootFolderKey)
                return;
            int slash = key.LastIndexOf('/');
            string leaf = slash < 0 ? key : key.Substring(slash + 1);
            string input = PromptForFolderName(leaf);
            if (input == null || string.Equals(input.Trim(), leaf, StringComparison.Ordinal))
                return;
            try
            {
                string newRelative = Program.DataManager.RenameFolder(key, input);
                RefreshDatasetGrid();
                SetStatus(string.Format(I18n.GetText("FolderRenameDone"), newRelative));
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, I18n.GetText("FolderRenameInvalid"),
                    I18n.GetText("FolderRenameTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(I18n.GetText("FolderRenameFailed"), ex.Message),
                    I18n.GetText("FolderRenameTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string PromptForFolderName(string currentName)
        {
            using var dialog = new Form
            {
                Text = I18n.GetText("FolderRenameTitle"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(LogicalToDeviceUnits(380), LogicalToDeviceUnits(128))
            };
            var label = new Label
            {
                Text = I18n.GetText("FolderRenamePrompt"),
                AutoSize = true,
                Location = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(12))
            };
            var textBox = new TextBox
            {
                Text = currentName,
                Location = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(36)),
                Width = dialog.ClientSize.Width - LogicalToDeviceUnits(24)
            };
            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            var cancelButton = new Button
            {
                Text = I18n.GetText("BtnCancel"),
                DialogResult = DialogResult.Cancel,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            okButton.Location = new Point(
                dialog.ClientSize.Width - LogicalToDeviceUnits(12) - okButton.Width * 2 - LogicalToDeviceUnits(8),
                dialog.ClientSize.Height - LogicalToDeviceUnits(40));
            cancelButton.Location = new Point(
                dialog.ClientSize.Width - LogicalToDeviceUnits(12) - cancelButton.Width,
                dialog.ClientSize.Height - LogicalToDeviceUnits(40));
            dialog.Controls.Add(label);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;
            Program.ColorManager.ChangeColorScheme(dialog, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(dialog.Controls, Program.ColorManager.SelectedScheme);
            textBox.SelectAll();
            return dialog.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
        }

        /// <summary>
        /// Folder right-click → batch rename: renames every image of the
        /// folder (captions follow) to prefix + counter (numeric/letters/
        /// original name) + suffix, in the natural display order.
        /// </summary>
        private void BatchRenameFolderImages()
        {
            string contextKey = SingleFolderContextKey;
            if (Program.DataManager == null || contextKey == null)
                return;
            // Exact directory match against GetRelativeFolder, whose root value
            // is "" — so the root sentinel key maps back to "" here.
            string folderKey = DatasetFolderIndex.NormalizeRelative(contextKey);
            if (folderKey == DatasetFolderIndex.RootFolderKey)
                folderKey = string.Empty;
            List<string> paths = Program.DataManager.DataSet.Keys
                .Where(path => string.Equals(
                    DatasetFolderIndex.GetRelativeFolder(path, Program.DataManager.DatasetRoot),
                    folderKey, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, new FileNamesComparer())
                .ToList();
            if (paths.Count == 0)
                return;

            using var dialog = new Form
            {
                Text = I18n.GetText("BatchRenameTitle"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(LogicalToDeviceUnits(420), LogicalToDeviceUnits(238))
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 5,
                Padding = new Padding(LogicalToDeviceUnits(12))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            Label MakeLabel(string key) => new Label
            {
                Text = I18n.GetText(key),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(6), 0)
            };
            var prefixBox = new TextBox { Dock = DockStyle.Fill };
            var suffixBox = new TextBox { Dock = DockStyle.Fill };
            var numberingCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            numberingCombo.Items.Add(I18n.GetText("BatchRenameNumberingNumeric"));
            numberingCombo.Items.Add(I18n.GetText("BatchRenameNumberingLetters"));
            numberingCombo.Items.Add(I18n.GetText("BatchRenameNumberingNone"));
            numberingCombo.SelectedIndex = 0;
            var startBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000000, Value = 1 };
            var digitsBox = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 10, Value = 3 };
            var previewLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            layout.Controls.Add(MakeLabel("BatchRenamePrefix"), 0, 0);
            layout.Controls.Add(prefixBox, 1, 0);
            layout.Controls.Add(MakeLabel("BatchRenameSuffix"), 2, 0);
            layout.Controls.Add(suffixBox, 3, 0);
            layout.Controls.Add(MakeLabel("BatchRenameNumbering"), 0, 1);
            layout.Controls.Add(numberingCombo, 1, 1);
            layout.Controls.Add(MakeLabel("BatchRenameStart"), 2, 1);
            layout.Controls.Add(startBox, 3, 1);
            layout.Controls.Add(MakeLabel("BatchRenameDigits"), 0, 2);
            layout.Controls.Add(digitsBox, 1, 2);
            layout.Controls.Add(MakeLabel("BatchRenamePreview"), 0, 3);
            layout.Controls.Add(previewLabel, 1, 3);
            layout.SetColumnSpan(previewLabel, 3);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill
            };
            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            var cancelButton = new Button
            {
                Text = I18n.GetText("BtnCancel"),
                DialogResult = DialogResult.Cancel,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(okButton);
            layout.Controls.Add(buttons, 1, 4);
            layout.SetColumnSpan(buttons, 3);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            BatchRenameNumbering Numbering() => (BatchRenameNumbering)numberingCombo.SelectedIndex;
            List<string> originals = paths
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
            void UpdatePreview()
            {
                startBox.Enabled = Numbering() == BatchRenameNumbering.Numeric;
                digitsBox.Enabled = Numbering() == BatchRenameNumbering.Numeric;
                try
                {
                    IReadOnlyList<string> sample = BatchRenamePlanner.BuildNames(
                        Math.Min(2, paths.Count), prefixBox.Text, suffixBox.Text,
                        Numbering(), (int)startBox.Value, (int)digitsBox.Value, originals);
                    previewLabel.Text = string.Join(",  ", sample)
                        + (paths.Count > sample.Count ? ",  …" : string.Empty);
                    okButton.Enabled = true;
                }
                catch (ArgumentException)
                {
                    previewLabel.Text = I18n.GetText("BatchRenameConflict");
                    okButton.Enabled = false;
                }
            }
            prefixBox.TextChanged += (_, _) => UpdatePreview();
            suffixBox.TextChanged += (_, _) => UpdatePreview();
            numberingCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
            startBox.ValueChanged += (_, _) => UpdatePreview();
            digitsBox.ValueChanged += (_, _) => UpdatePreview();
            UpdatePreview();
            Program.ColorManager.ChangeColorScheme(dialog, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(dialog.Controls, Program.ColorManager.SelectedScheme);

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                IReadOnlyList<string> names = BatchRenamePlanner.BuildNames(
                    paths.Count, prefixBox.Text, suffixBox.Text,
                    Numbering(), (int)startBox.Value, (int)digitsBox.Value, originals);
                var renames = new List<KeyValuePair<string, string>>(paths.Count);
                for (int i = 0; i < paths.Count; i++)
                    renames.Add(new KeyValuePair<string, string>(paths[i], names[i]));
                int renamed = Program.DataManager.RenameImages(renames);
                RefreshDatasetGrid();
                SetStatus(string.Format(I18n.GetText("BatchRenameDone"), renamed));
            }
            catch (ArgumentException)
            {
                MessageBox.Show(this, I18n.GetText("BatchRenameConflict"),
                    I18n.GetText("BatchRenameTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(I18n.GetText("FolderRenameFailed"), ex.Message),
                    I18n.GetText("BatchRenameTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshDatasetGrid();
            }
        }

        private void FolderList_FolderSelected(string relativeFolder)
        {
            if (Program.DataManager == null)
                return;
            Program.DataManager.SetActiveFolder(relativeFolder);
            RefreshDatasetGrid();
        }

        /// <summary>
        /// Browser Ctrl/Shift folder multi-select: scope to the union of the
        /// selected folders so the grid and the AllTags counts follow the
        /// selection (an empty set falls back to "all folders").
        /// </summary>
        private void FolderList_FoldersSelected(IReadOnlyList<string> folderKeys)
        {
            if (Program.DataManager == null)
                return;
            Program.DataManager.SetActiveFolders(folderKeys);
            RefreshDatasetGrid();
        }

        /// <summary>
        /// Rebuilds the dataset browser from the loaded dataset (folder
        /// headers from the full dataset, image rows from the hidden grid's
        /// current scope) and re-applies the preview layout.
        /// </summary>
        private void RefreshDatasetFolderList()
        {
            if (datasetBrowserView == null)
                return;
            if (Program.DataManager == null)
            {
                datasetBrowserView.Clear();
                ApplyDatasetSidebarLayout();
                return;
            }
            IReadOnlyList<DatasetFolderEntry> entries = Program.DataManager.GetFolderEntries();
            if (entries.Count <= 1 && !string.IsNullOrEmpty(Program.DataManager.ActiveFolder))
            {
                // The dataset collapsed to a single folder while a scope was
                // active (e.g. images deleted): fall back to "all".
                Program.DataManager.SetActiveFolder(null);
            }
            datasetBrowserView.SetRows(
                entries,
                gridViewDS.DataSource as List<DataItem>,
                Program.DataManager.DatasetRoot,
                Program.DataManager.ActiveFolder,
                Program.DataManager.DataSet.Count,
                GetGridSelectedPaths());
            ApplyDatasetSidebarLayout();
        }

        /// <summary>
        /// Applies the preview strip layout at the bottom of the dataset
        /// module. The panel is only a surface in PreviewInMainWindow mode;
        /// collapsed it keeps just its clickable header strip.
        /// </summary>
        private void ApplyDatasetSidebarLayout()
        {
            if (datasetPreviewPanel == null)
                return;
            bool embeddedMode = Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow;
            bool expanded = Program.Settings.DatasetPreviewExpanded;
            bool showPreview = embeddedMode && Program.DataManager != null;
            datasetPreviewPanel.SetExpanded(expanded);
            datasetPreviewPanel.Visible = showPreview;
            if (showPreview)
            {
                datasetPreviewPanel.Height = expanded
                    ? GetExpandedPreviewHeight()
                    : datasetPreviewPanel.HeaderHeight;
            }
            sidebarPreviewSplitter.Visible = showPreview && expanded;
            MenuShowPreview.Checked = embeddedMode ? expanded : isShowPreview;
        }

        private int GetExpandedPreviewHeight()
        {
            if (lastExpandedPreviewHeight > datasetPreviewPanel.HeaderHeight)
                return lastExpandedPreviewHeight;
            // First expansion: a bit under half the pane, bounded so the
            // browser keeps a usable share.
            int available = Math.Max(0, toolStripContainer3.ContentPanel.Height);
            return Math.Max(datasetPreviewPanel.HeaderHeight + LogicalToDeviceUnits(40),
                Math.Min(LogicalToDeviceUnits(340), available * 2 / 5));
        }

        /// <summary>
        /// Collapses/expands the embedded preview and persists the state. In
        /// SeparateWindow mode the 视图→显示预览 toggle keeps its legacy
        /// floating-window behavior instead (see the menu handler).
        /// </summary>
        private void ToggleEmbeddedPreview()
        {
            if (datasetPreviewPanel.Expanded)
                lastExpandedPreviewHeight = datasetPreviewPanel.Height;
            Program.Settings.DatasetPreviewExpanded = !Program.Settings.DatasetPreviewExpanded;
            Program.Settings.SaveSettings();
            ApplyDatasetSidebarLayout();
            RefreshEmbeddedPreviewFromSelection();
        }

        /// <summary>True when selection changes should update a preview surface.</summary>
        private bool IsPreviewFollowActive =>
            Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow
                ? datasetPreviewPanel != null && datasetPreviewPanel.Visible && datasetPreviewPanel.Expanded
                : isShowPreview;

        private void RefreshEmbeddedPreviewFromSelection()
        {
            if (!IsPreviewFollowActive)
                return;
            List<string> paths = GetGridSelectedPaths();
            if (paths.Count == 1 && !string.IsNullOrEmpty(paths[0]))
            {
                ShowPreview(paths[0]);
                return;
            }
            // Multi-selection: the embedded panel tiles the first images; the
            // floating-window mode keeps its legacy hide-on-multi behavior.
            if (paths.Count > 1 && Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
                ShowEmbeddedPreviewMulti(paths);
            else
                HidePreview();
        }

        internal List<string> GetSelectedDatasetVideoPaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                string path = (string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value;
                if (VideoProcessingService.IsVideoFile(path))
                    paths.Add(path);
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal List<string> GetSelectedDatasetImagePaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                string path = (string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value;
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (Extensions.ImageExtensions.Contains(extension) && !VideoProcessingService.IsVideoFile(path))
                    paths.Add(path);
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal void RefreshDatasetGrid(IReadOnlyList<string> addedPaths = null)
        {
            if (Program.DataManager == null)
                return;

            SaveSelectedInViewDs();
            gridViewDS.DataSource = Program.DataManager.GetDataSourceWithLastFilter();
            ApplyDataSetGridStyle();
            LoadSelectedInViewDs();
            RefreshDatasetFolderList();
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.GetActiveScopeCount()));
            if (addedPaths != null && addedPaths.Count > 0)
                SetStatus(string.Format(I18n.GetText("VideoExtractImportedCount"), addedPaths.Count));
        }

        internal void RefreshSelectedImageTags()
        {
            LoadSelectedImageToGrid();
        }

        internal void PrepareForBulkTagWrite()
        {
            LockEdit(true);
            gridViewTags.DataSource = null;
            gridViewTags.Rows.Clear();
        }

        internal void CompleteBulkTagWrite()
        {
            RefreshSelectedImageTagsAfterBulkWrite();
            gridViewAllTags.Refresh();
            LockEdit(false);
        }

        internal void RefreshSelectedImageTagsAfterBulkWrite()
        {
            if (Program.DataManager == null || gridViewDS.SelectedRows.Count == 0)
                return;

            if (gridViewDS.SelectedRows.Count == 1)
            {
                string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (!Program.DataManager.DataSet.TryGetValue(imagePath, out DataItem dataItem))
                    return;

                // Same dangling-edit guard as LoadSelectedImageToGrid.
                (gridViewTags.DataSource as EditableTagList)?.EndEdit();
                gridViewTags.AutoGenerateColumns = false;
                gridViewTags.SuspendLayout();
                BtnTagImageChecker.Enabled = false;
                gridViewTags.Tag = imagePath;
                ChageImageColumn(false);
                gridViewTags.DataSource = dataItem.Tags;
                gridViewTags.AllowDrop = true;
                gridViewTags.ResumeLayout();
                return;
            }

            RefreshSelectedImageTags();
        }

        internal void DeleteDatasetMediaFiles(IEnumerable<string> mediaPaths)
        {
            if (Program.DataManager == null || mediaPaths == null)
                return;

            List<string> deletedPaths = new List<string>();
            foreach (string file in mediaPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                // Resolve the real sidecar (txt/caption per settings) instead of
                // assuming .txt, and delete media + sidecar transactionally —
                // the old two-step direct delete could remove the video and
                // then fail on a locked caption, leaving a half-deleted pair.
                // ponytail: if both .txt and .caption exist, only the first
                // configured match is deleted (same as every other delete path).
                string tagFile = ImageEditorSaveService.FindExistingCaptionPath(file, Program.Settings.GetTagFilesExtensions());
                if (ImageFileDeleter.DeleteImageWithTags(file, tagFile, out string deleteError))
                {
                    deletedPaths.Add(file);
                }
                else
                {
                    // Keep the item in the dataset if its file couldn't be deleted,
                    // so on-disk and in-memory state stay consistent.
                    Trace.WriteLine($"Failed to delete '{file}': {deleteError}");
                }
            }

            if (deletedPaths.Count > 0)
                Program.DataManager.RemoveMany(deletedPaths);

            RefreshDatasetGrid();
        }

        private void InitializeAiServerSetAndTestMenu()
        {
            menuAiServerSet = new ToolStripMenuItem { Name = "menuAiServerSet", Text = "AiServerSet" };
            menuAiServerSet.Click += (_, _) =>
            {
                using Form_AiServerSet form = new Form_AiServerSet(this);
                form.ShowDialog(this);
            };

            menuTestModule = new ToolStripMenuItem { Name = "menuTestModule", Text = "Test" };
            menuTestModule.Click += (_, _) =>
            {
                using Form_TestModule form = new Form_TestModule(this);
                form.ShowDialog(this);
            };

            menuLlmTagger = new ToolStripMenuItem { Name = "menuLlmTagger", Text = "LLM tagging" };
            menuLlmTagger.Click += (_, _) => ShowLlmTagger();

            menuVideoConvert = new ToolStripMenuItem { Name = "menuVideoConvert", Text = "Video convert" };
            menuVideoConvert.Click += (_, _) =>
            {
                using Form_VideoConvert form = new Form_VideoConvert(this);
                form.ShowDialog(this);
            };

            menuVideoExtract = new ToolStripMenuItem { Name = "menuVideoExtract", Text = "Frame extract" };
            menuVideoExtract.Click += (_, _) =>
            {
                using Form_VideoTools form = new Form_VideoTools(this);
                form.ShowDialog(this);
            };

            menuOnnxTagger = new ToolStripMenuItem { Name = "menuOnnxTagger", Text = "ONNX tagger" };
            menuOnnxTagger.Click += (_, _) => ShowOnnxTaggerForSelectedImages();

            menuContextDSVideoTools = new ToolStripMenuItem { Name = "menuContextDSVideoTools", Text = "Video tools..." };
            menuContextDSVideoTools.Click += (_, _) =>
            {
                if (gridViewDS.SelectedRows.Count == 0)
                    return;

                string file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (!VideoProcessingService.IsVideoFile(file))
                    return;

                using Form_VideoTools form = new Form_VideoTools(this, file);
                form.ShowDialog(this);
            };

            menuContextDSRetagOnnx = new ToolStripMenuItem { Name = "menuContextDSRetagOnnx", Text = "Retag ONNX" };
            menuContextDSRetagOnnx.Click += (_, _) => ShowOnnxTaggerForSelectedImages();

            menuContextDSRetagLlm = new ToolStripMenuItem { Name = "menuContextDSRetagLlm", Text = "LLM tagging" };
            menuContextDSRetagLlm.Click += (_, _) => ShowLlmTagger();

            menuContextDSEditImage = new ToolStripMenuItem { Name = "menuContextDSEditImage", Text = "Edit image" };
            menuContextDSEditImage.Click += (_, _) => EditSelectedImage();
            int editImageIndex = contextMenuStrip1.Items.IndexOf(cropImageToolStripMenuItem) + 1;
            contextMenuStrip1.Items.Insert(editImageIndex, menuContextDSEditImage);

            contextMenuStrip1.Items.Add(menuContextDSRetagOnnx);
            contextMenuStrip1.Items.Add(menuContextDSRetagLlm);
            contextMenuStrip1.Items.Add(menuContextDSVideoTools);
            contextMenuStrip1.Opening += ContextMenuStrip1_Opening;

            toolsToolStripMenuItem.DropDownItems.Add(menuVideoConvert);
            toolsToolStripMenuItem.DropDownItems.Add(menuVideoExtract);
            toolsToolStripMenuItem.DropDownItems.Add(menuOnnxTagger);
            toolsToolStripMenuItem.DropDownItems.Add(backgroundRemovalWithRMBG20ToolStripMenuItem);
            toolsToolStripMenuItem.DropDownItems.Add(menuLlmTagger);

            int insertIndex = menuStrip1.Items.IndexOf(toolsToolStripMenuItem) + 1;
            if (insertIndex <= 0 || insertIndex > menuStrip1.Items.Count)
                insertIndex = menuStrip1.Items.Count;
            menuStrip1.Items.Insert(insertIndex, menuAiServerSet);
            menuStrip1.Items.Insert(insertIndex + 1, menuTestModule);
        }

        internal string GetSelectedDatasetDirectory()
        {
            if (!string.IsNullOrWhiteSpace(Program.DataManager?.DatasetRoot))
                return Program.DataManager.DatasetRoot;

            if (gridViewDS.SelectedRows.Count > 0)
            {
                string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                return Path.GetDirectoryName(imagePath);
            }

            return Program.DataManager.DataSet.Values
                .Select(item => Path.GetDirectoryName(item.ImageFilePath))
                .FirstOrDefault(path => !string.IsNullOrEmpty(path));
        }

        private static bool HasValidOpenAiAutoTagSettings()
        {
            string endpointText = Program.Settings.OpenAiAutoTagger.ConnectionAddress;
            return Uri.TryCreate(endpointText, UriKind.Absolute, out Uri endpoint)
                && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(Program.Settings.OpenAiAutoTagger.ResolveVisionModel())
                && Program.OpenAiAutoTagger != null;
        }

        private bool EnsureOpenAiAutoTagConfigured(bool openAdvancedSettings)
        {
            if (!HasValidOpenAiAutoTagSettings())
            {
                MessageBox.Show(
                    this,
                    I18n.GetText("OpenAiAutoTagInvalidSettings"),
                    I18n.GetText("MenuAiServerSet"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                using Form_AiServerSet settings = new Form_AiServerSet(this);
                if (settings.ShowDialog(this) != DialogResult.OK || !HasValidOpenAiAutoTagSettings())
                    return false;
            }

            if (openAdvancedSettings)
            {
                using Form_AutoTaggerOpenAiSettings advancedSettings = new Form_AutoTaggerOpenAiSettings();
                if (advancedSettings.ShowDialog(this) != DialogResult.OK)
                    return false;
            }

            return true;
        }

        private bool EnsureSelectedAutoTagProviderConfigured(bool openAdvancedSettings)
        {
            // Only the OpenAI-compatible provider remains (the legacy
            // ai-api-server backend was removed; old configs are migrated at
            // startup).
            return EnsureOpenAiAutoTagConfigured(openAdvancedSettings);
        }

        private async Task<(List<AutoTagItem> data, string errorMessage, bool canceled)> GenerateWithSelectedAutoTagProviderAsync(
            string mediaPath)
        {
            string providerId = string.IsNullOrWhiteSpace(Program.Settings.AutoTagProviderId)
                ? "openai-compatible"
                : Program.Settings.AutoTagProviderId;
            IAutoTagProvider provider = Program.AutoTagProviders.GetRequired(providerId);
            AutoTagConnectionResult connection = await provider.ConnectAsync();
            if (!connection.Success)
                return (null, connection.ErrorMessage, false);
            IReadOnlyList<string> modelIds = new[] { Program.Settings.OpenAiAutoTagger.ResolveVisionModel() };
            AutoTagProviderResult result = await provider.GenerateAsync(new AutoTagProviderRequest
            {
                MediaPath = mediaPath,
                ModelIds = modelIds
            });
            List<AutoTagItem> items = result.Items.Select(item =>
                new AutoTagItem(item.Tag, item.Confidence)).ToList();
            return (result.Success ? items : null, result.ErrorMessage, result.Canceled);
        }

        private async Task<(List<AutoTagItem> data, string errorMessage, bool canceled)> GenerateWithOpenAiAutoTaggerAsync(
            string mediaPath)
        {
            IAutoTagProvider provider = Program.AutoTagProviders.GetRequired("openai-compatible");
            AutoTagConnectionResult connection = await provider.ConnectAsync();
            if (!connection.Success)
                return (null, connection.ErrorMessage, false);
            AutoTagProviderResult result = await provider.GenerateAsync(new AutoTagProviderRequest
            {
                MediaPath = mediaPath,
                ModelIds = new[] { Program.Settings.OpenAiAutoTagger.ResolveVisionModel() }
            });
            List<AutoTagItem> items = result.Items.Select(item =>
                new AutoTagItem(item.Tag, item.Confidence)).ToList();
            return (result.Success ? items : null, result.ErrorMessage, result.Canceled);
        }

        private List<DataItem> GetSelectedImageDataItems()
        {
            var items = new List<DataItem>();
            if (Program.DataManager == null)
                return items;

            foreach (DataGridViewRow row in gridViewDS.SelectedRows)
            {
                string path = (string)row.Cells["ImageFilePath"].Value;
                if (VideoProcessingService.IsVideoFile(path))
                    continue;
                if (Program.DataManager.DataSet.TryGetValue(path, out DataItem item))
                    items.Add(item);
            }

            return items;
        }

        private void ShowLlmTagger()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            using Form_LlmTagger form = new Form_LlmTagger(this);
            form.ShowDialog(this);
        }

        private void ShowOnnxTaggerForSelectedImages()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            using Form_OnnxTagger form = new Form_OnnxTagger(this);
            form.ShowDialog(this);
        }

        internal bool TryQuickReplaceSelectedTag(int threshold)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return false;
            }
            if (gridViewAllTags.SelectedCells.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipImgOrTagNotSelect"));
                return false;
            }

            int rowIndex = gridViewAllTags.SelectedCells[0].RowIndex;
            string selectedTag = (string)gridViewAllTags.Rows[rowIndex].Cells["TagsColumn"].Value;
            var sourceTags = QuickTagReplaceService.GetReplacementSourceTags(
                Program.DataManager.AllTags.Cast<AllTagsItem>(),
                selectedTag,
                threshold);

            if (sourceTags.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipQuickReplaceNoCandidates"));
                return false;
            }

            string message = string.Format(I18n.GetText("TipQuickReplaceConfirm"), sourceTags.Count, selectedTag);
            if (MessageBox.Show(message, I18n.GetText("TestQuickReplace"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return false;

            Program.DataManager.ReplaceTagsInAll(sourceTags, selectedTag);
            Program.Settings.QuickReplaceThreshold = threshold;
            Program.Settings.SaveSettings();
            LoadSelectedImageToGrid();
            return true;
        }

        internal void ReloadTranslationManagerForCurrentSettings()
        {
            string translationsDir = Path.Combine(Application.StartupPath, "Translations");
            if (!Directory.Exists(translationsDir))
                Directory.CreateDirectory(translationsDir);

            TranslationManager oldManager = Program.TransManager;
            Program.TransManager = Program.CreateTranslationManager(translationsDir);
            Program.TransManager.LoadTranslations();
            oldManager?.Dispose();
            Program.TagsList?.LoadTranslation(Program.TransManager);
        }

        private void CreateLangMenuItems()
        {
            foreach (var lang in I18n.GetLanguages())
            {
                ToolStripMenuItem menuItem = new ToolStripMenuItem();
                menuItem.Name = "btn_" + lang;
                menuItem.Text = I18n.GetText(menuItem.Name);
                menuItem.Click += LanguageXXBtn_Click;
                MenuLanguage.DropDownItems.Add(menuItem);
            }
        }

        private ContextMenuStrip tagContextMenu;
        private DataGridView tagContextGrid;
        private int tagContextRowIndex = -1;
        private string tagContextTag;

        private void InitializeTagContextMenu()
        {
            tagContextMenu = new ContextMenuStrip();

            ToolStripMenuItem queryWikiItem = new ToolStripMenuItem
            {
                Name = "TagContextQueryDanbooruWiki"
            };
            queryWikiItem.Click += TagContextQueryWiki_Click;

            ToolStripMenuItem retranslateItem = new ToolStripMenuItem
            {
                Name = "TagContextRetranslate"
            };
            retranslateItem.Click += TagContextRetranslate_Click;

            tagContextMenu.Items.AddRange(new ToolStripItem[]
            {
                queryWikiItem,
                retranslateItem
            });

            gridViewTags.CellMouseDown += TagGrid_CellMouseDown;
            gridViewAllTags.CellMouseDown += TagGrid_CellMouseDown;
            gridViewAutoTags.CellMouseDown += TagGrid_CellMouseDown;
        }

        private void UpdateTagContextMenuText()
        {
            if (tagContextMenu == null)
                return;

            tagContextMenu.Items["TagContextQueryDanbooruWiki"].Text = I18n.GetText("TagContextQueryDanbooruWiki");
            tagContextMenu.Items["TagContextRetranslate"].Text = I18n.GetText("TagContextRetranslate");
        }

        private void TagGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                return;

            DataGridView grid = (DataGridView)sender;
            string tag = GetTagFromGrid(grid, e.RowIndex);
            if (string.IsNullOrWhiteSpace(tag))
                return;

            if (e.ColumnIndex >= 0)
                grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            grid.ClearSelection();
            grid.Rows[e.RowIndex].Selected = true;

            tagContextGrid = grid;
            tagContextRowIndex = e.RowIndex;
            tagContextTag = tag;
            tagContextMenu.Show(Cursor.Position);
        }

        private string GetTagFromGrid(DataGridView grid, int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
                return null;

            if (grid == gridViewTags && grid.DataSource is MultiSelectDataTable multiSelectTags)
            {
                if (rowIndex < multiSelectTags.Rows.Count)
                    return ((MultiSelectDataRow)multiSelectTags.Rows[rowIndex]).GetTagText();
            }

            if (grid == gridViewTags && grid.Columns.Contains("ImageTags"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["ImageTags"].Value);

            if (grid == gridViewAllTags && grid.Columns.Contains("TagsColumn"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["TagsColumn"].Value);

            if (grid == gridViewAutoTags && grid.Columns.Contains("Tag"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["Tag"].Value);

            if (grid.Columns.Contains("Tag"))
                return Convert.ToString(grid.Rows[rowIndex].Cells["Tag"].Value);

            return null;
        }

        private void TagContextQueryWiki_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tagContextTag))
                return;

            var popup = new Form_TagWikiPopup(tagContextTag);
            popup.Show(this);
        }

        private async void TagContextRetranslate_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tagContextTag) || Program.TransManager == null)
                return;

            try
            {
                tagContextMenu.Enabled = false;
                SetStatus(I18n.GetText("StatusTranslating"));
                string result = await Program.TransManager.TranslateAsync(tagContextTag, forceRefresh: true);
                UpdateTagTranslation(tagContextGrid, tagContextRowIndex, tagContextTag, result);
                SetStatus(I18n.GetText("StatusRetranslated"));
            }
            catch
            {
                SetStatus(I18n.GetText("StatusTranslationComplete"));
            }
            finally
            {
                tagContextMenu.Enabled = true;
            }
        }

        private void UpdateTagTranslation(DataGridView grid, int rowIndex, string tag, string translation)
        {
            if (grid == null || rowIndex < 0)
                return;

            string result = translation ?? string.Empty;
            if (grid.Columns.Contains("Translation"))
                grid.Columns["Translation"].Visible = true;
            if (grid.Columns.Contains("TranslationColumn"))
                grid.Columns["TranslationColumn"].Visible = true;

            if (grid == gridViewTags && grid.DataSource is EditableTagList editableTags)
            {
                if (rowIndex < editableTags.Count && editableTags[rowIndex].Tag == tag)
                    editableTags[rowIndex].Translation = result;
            }
            else if (grid == gridViewTags && grid.DataSource is MultiSelectDataTable multiSelectTags)
            {
                foreach (MultiSelectDataRow row in multiSelectTags.Rows)
                {
                    if (row.RowState != DataRowState.Deleted
                        && row.GetTagText() == tag
                        && grid.Columns.Contains("Translation"))
                    {
                        row["Translation"] = result;
                    }
                }
            }
            else if (grid == gridViewAllTags)
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (Convert.ToString(row.Cells["TagsColumn"].Value) == tag)
                    {
                        if (row.DataBoundItem is AllTagsItem item)
                            item.SetTranslation(result);
                        row.Cells["TranslationColumn"].Value = result;
                    }
                }
            }

            grid.Refresh();
        }



        private Form_filter allTagsFilter;

        private bool isAllTags = true;
        private bool isTranslate = false;
        private bool isFiltered = false;
        private bool showCount = false;

        private Form_preview fPreview;
        private bool isShowPreview = false;
        private PictureBox previewPicBox;
        private int previewRowIndex = -1;
        private FilterType filterAnd = FilterType.Or;
        private bool selectionMode = false;
        private HashSet<string> selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        private void Form1_Load(object sender, EventArgs e)
        {
            Text += " " + Application.ProductVersion;
            gridViewDS.RowTemplate.Height = Program.Settings.PreviewSize + 10;
            gridViewAllTags.RowTemplate.Height = Program.Settings.GridViewRowHeight;
            gridViewTags.RowTemplate.Height = Program.Settings.GridViewRowHeight;
            gridViewTags.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            gridViewAllTags.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            gridViewDS.DefaultCellStyle.Font = Program.Settings.GridViewFont.GetFont();
            //splitContainer2.SplitterDistance = Width / 3;
            toolStrippromptFixedLengthComboBox.SelectedIndex = 0;
            if (!Program.Settings.FixTagsOnSaveLoad)
            {
                toolStripMenuItemWeight.Enabled = false;
                toolStripTextBoxWeight.Enabled = false;
            }
#if !DEBUG
            Extensions.CheckForUpdateAsync(Application.ProductVersion);
#endif
            debugToolStripMenuItem.Visible = Program.Settings.DebugMode;
        }

        /// <summary>
        /// The Name column paints the image name with the full file path
        /// stacked underneath (dim, character-wrapped so pathological
        /// no-space paths still wrap), keeping the grid at two visible
        /// columns — thumbnail + details — with no horizontal scrolling
        /// while everything stays readable.
        /// </summary>
        private void GridViewDS_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (gridViewDS.Columns[e.ColumnIndex].Name != "Name"
                || !gridViewDS.Columns.Contains("ImageFilePath"))
            {
                return;
            }
            bool selected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;
            e.PaintBackground(e.CellBounds, selected);
            string name = e.Value as string ?? string.Empty;
            string path = gridViewDS.Rows[e.RowIndex].Cells["ImageFilePath"].Value as string ?? string.Empty;
            Color foreColor = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
            Color backColor = selected ? e.CellStyle.SelectionBackColor : e.CellStyle.BackColor;
            Font font = e.CellStyle.Font ?? gridViewDS.Font;
            int pad = LogicalToDeviceUnits(6);
            Rectangle area = Rectangle.Inflate(e.CellBounds, -pad, -pad);
            if (area.Width <= 0 || area.Height <= 0)
            {
                e.Handled = true;
                return;
            }

            const TextFormatFlags nameFlags = TextFormatFlags.Left | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int nameHeight = TextRenderer.MeasureText(e.Graphics, name.Length > 0 ? name : " ",
                font, new Size(area.Width, area.Height), nameFlags).Height;
            int gap = LogicalToDeviceUnits(3);
            int pathAvailable = area.Height - nameHeight - gap;

            // GDI+ wraps at character level when a segment has no spaces,
            // which TextRenderer (GDI) cannot do — file paths need that.
            using var pathFormat = new StringFormat(StringFormatFlags.LineLimit)
            {
                Trimming = StringTrimming.EllipsisCharacter
            };
            int pathHeight = 0;
            if (path.Length > 0 && pathAvailable > font.Height)
            {
                pathHeight = Math.Min(pathAvailable,
                    (int)Math.Ceiling(e.Graphics.MeasureString(path, font, area.Width).Height));
            }

            int total = nameHeight + (pathHeight > 0 ? gap + pathHeight : 0);
            int y = area.Y + Math.Max(0, (area.Height - total) / 2);
            TextRenderer.DrawText(e.Graphics, name, font,
                new Rectangle(area.X, y, area.Width, nameHeight), foreColor, nameFlags);
            if (pathHeight > 0)
            {
                Color pathColor = BlendColor(foreColor, backColor, 0.6f);
                using var pathBrush = new SolidBrush(pathColor);
                e.Graphics.DrawString(path, font, pathBrush,
                    new RectangleF(area.X, y + nameHeight + gap, area.Width, pathHeight), pathFormat);
            }
            e.Handled = true;
        }

        private static Color BlendColor(Color over, Color under, float amount)
        {
            float rest = 1f - amount;
            return Color.FromArgb(
                (int)(over.R * amount + under.R * rest),
                (int)(over.G * amount + under.G * rest),
                (int)(over.B * amount + under.B * rest));
        }

        /// <summary>
        /// With the file-path column hidden by default, hovering any dataset
        /// row shows the full path as a tooltip instead.
        /// </summary>
        private void GridViewDS_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridViewDS.RowCount)
                return;
            if (gridViewDS.Columns.Contains("ImageFilePath"))
                e.ToolTipText = gridViewDS.Rows[e.RowIndex].Cells["ImageFilePath"].Value as string ?? string.Empty;
        }

        private void ContextMenuImageGridHeader_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            ToolStripMenuItem tsi = (ToolStripMenuItem)e.ClickedItem;
            if (gridViewDS.Columns.Contains(tsi.Name))
            {
                int visibleCount = 0;
                for (int i = 0; i < gridViewDS.ColumnCount; i++)
                {
                    if (gridViewDS.Columns[i].Visible)
                        visibleCount++;
                }
                if (visibleCount > 1 || !tsi.Checked)
                {
                    tsi.Checked = !tsi.Checked;
                    gridViewDS.Columns[tsi.Name].Visible = tsi.Checked;
                    // Persist the choice so it survives rebinds and restarts.
                    List<string> hidden = Program.Settings.DatasetHiddenColumns
                        ?? AppSettings.GetDefaultDatasetHiddenColumns();
                    hidden.RemoveAll(name => string.Equals(name, tsi.Name, StringComparison.OrdinalIgnoreCase));
                    if (!tsi.Checked)
                        hidden.Add(tsi.Name);
                    Program.Settings.DatasetHiddenColumns = hidden;
                    Program.Settings.SaveSettings();
                }
                else
                {
                    MessageBox.Show(I18n.GetText("TipColumnMustVisible"));
                }
            }
        }

        private void ColorManager_SchemeChanded(object sender, EventArgs e)
        {
            if (Program.ColorManager.SelectedScheme != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
                // The "no match" tint restores this color, so track scheme changes.
                allTagsSearchDefaultBackColor = toolStripTextBox1.BackColor;
                if (toolStripImageTagsSearchBox != null)
                    imageTagsSearchDefaultBackColor = toolStripImageTagsSearchBox.BackColor;
            }
        }

        private async void openFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await LoadFromFolderAsync(false);
        }

        private async void loadFolderWithAdditionalSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await LoadFromFolderAsync(true);
        }

        private async Task LoadFromFolderAsync(bool useAdditionalSettings)
        {
            if (Program.DataManager != null && Program.DataManager.IsDataSetChanged())
            {
                DialogResult result = MessageBox.Show(I18n.GetText("TipDSChangeSaveText"), I18n.GetText("TipDSChangeSaveTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    Program.DataManager.SaveAll();
                    // The user chose to save before switching: a failed write
                    // must abort the switch so they can fix permissions or
                    // explicitly discard, instead of the old dataset (and its
                    // unsaved edits) being disposed below.
                    if (ReportSaveErrorsIfAny())
                        return;
                }
                else if (result == DialogResult.Cancel)
                    return;
            }
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            if (openFolderDialog.ShowDialog() != DialogResult.OK)
                return;
            bool loadPreviewImages = true;
            bool readMetadata = false;
            if (useAdditionalSettings)
            {
                Form_LoadingSettings loadingSettings = new Form_LoadingSettings();
                if (loadingSettings.ShowDialog() != DialogResult.OK)
                {
                    loadingSettings.Close();
                    return;
                }
                loadPreviewImages = Program.Settings.LoadSettingsLoadPreviewImages;
                readMetadata = Program.Settings.LoadSettingsReadMetadata;
            }
            LoadingStatusText = I18n.GetText("TipLoadingStart");
            SetStatus(LoadingStatusText);
            LockEdit(true);
            try
            {
                // Load into a candidate manager first: the old dataset (and its
                // in-memory state) is only discarded after the new folder has
                // actually loaded, so a failed/partial load keeps the current
                // dataset fully intact.
                DatasetManager candidateManager = new DatasetManager();
                candidateManager.LoadingProgressChanged += DataManager_LoadingProgressChanged;
                bool loaded;
                try
                {
                    loaded = await candidateManager.LoadFromFolderAsync(openFolderDialog.Folder, loadPreviewImages, readMetadata);
                }
                catch
                {
                    candidateManager.Dispose();
                    throw;
                }
                if (!loaded)
                {
                    candidateManager.Dispose();
                    SetStatus(I18n.GetText("TipFolderWrong"));
                    return;
                }
                TrackBarRowHeight.ValueChanged -= TrackBarRowHeight_ValueChanged;
                TrackBarRowHeight.TrackBar.Minimum = 1;
                TrackBarRowHeight.TrackBar.Maximum = Program.Settings.PreviewSize;
                TrackBarRowHeight.TrackBar.TickFrequency = 50;
                TrackBarRowHeight.TrackBar.SmallChange = 50;
                TrackBarRowHeight.TrackBar.LargeChange = 50;
                TrackBarRowHeight.Value = Program.Settings.PreviewSize;
                TrackBarRowHeight.ValueChanged += TrackBarRowHeight_ValueChanged;
                DatasetManager oldDataManager = Program.DataManager;
                Program.DataManager = candidateManager;
                if (oldDataManager != null)
                {
                    // Unbind before disposing so neither the grid nor the
                    // browser can paint disposed bitmaps.
                    gridViewDS.DataSource = null;
                    datasetBrowserView.Clear();
                    oldDataManager.Dispose();
                }
                gridViewDS.DataSource = Program.DataManager.GetDataSource();
                RefreshDatasetFolderList();
                // The rebind above raised SelectionChanged while the sidebar
                // preview was still hidden; now that the layout ran, fill it.
                RefreshEmbeddedPreviewFromSelection();
                isAllTags = true;
                toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
                gridViewAllTags.DataSource = Program.DataManager.AllTagsBindingSource;
                HookAllTagsSelectionAnchor();
                ApplyAllTagsCategorySort();
                ApplyDataSetGridStyle();
                await ApplyTranslation(isTranslate);
                gridViewDS.AutoResizeColumns();
                SetStatus(I18n.GetText("TipLoadingComplete"));
                SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.GetActiveScopeCount()));
                var loadErrors = Program.DataManager.LastLoadErrors;
                if (loadErrors.Count > 0)
                {
                    string details = string.Join("\n", loadErrors.Take(10));
                    if (loadErrors.Count > 10)
                        details += "\n...";
                    MessageBox.Show(this, string.Format(I18n.GetText("TipLoadErrors"), loadErrors.Count, details),
                        "BooruDatasetTagManagerPlus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                // A locked file / unreadable subfolder inside the parallel loader
                // surfaces here as an AggregateException; without this catch it
                // escaped the async void menu handlers with the UI left locked.
                Program.LogCrash(ex, "LoadFromFolder", terminating: false);
                SetStatus(I18n.GetText("TipFolderWrong"));
                MessageBox.Show(this, ex.InnerException?.Message ?? ex.Message,
                    "BooruDatasetTagManagerPlus", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                LockEdit(false);
            }
        }

        //This is necessary to speed up the work, since searching for a string using I18n.GetText takes a long time.
        private string LoadingStatusText = "";
        private void DataManager_LoadingProgressChanged(int current, int max)
        {
            // Raised from the parallel loader's worker threads: marshal onto the
            // UI thread instead of touching the status strip cross-thread.
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                BeginInvoke(new Action(() => SetStatus($"{LoadingStatusText} ({current}/{max})")));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void TrackBarRowHeight_ValueChanged(object sender, EventArgs e)
        {
            gridViewDS.SuspendLayout();
            if (gridViewDS.AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.AllCells)
            {
                gridViewDS.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            }
            //if (gridViewDS.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.AllCells)
            //{
            //    gridViewDS.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            //}
            //for (int i = 0; i < gridViewDS.ColumnCount; i++)
            //{
            //    if (gridViewDS.Columns[i].ValueType == typeof(Image))
            //    {
            //        gridViewDS.Columns[i].Width = TrackBarRowHeight.Value;
            //        //((DataGridViewImageColumn)gridViewDS.Columns[i]).Width = TrackBarRowHeight.Value;
            //    }
            //}
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                gridViewDS.Rows[i].Height = TrackBarRowHeight.Value;
            }
            gridViewDS.ResumeLayout();
            datasetBrowserView?.SetThumbnailHeight(TrackBarRowHeight.Value);
        }

        private async Task FillTranslation(DataGridView grid)
        {
            string transColumnName = string.Empty;
            if (grid.Columns.Contains("Translation"))
            {
                transColumnName = "Translation";
            }
            else if (grid.Columns.Contains("TranslationColumn"))
            {
                transColumnName = "TranslationColumn";
            }
            else
                return;
            if (grid.Columns[transColumnName].Visible == false)
            {
                grid.Columns[transColumnName].Visible = true;
            }
            SetStatus(I18n.GetText("StatusTranslating"));
            try
            {
                if (grid == gridViewAllTags && Program.DataManager != null)
                {
                    await Program.DataManager.AllTags.TranslateAllAsync();
                }
                else if (grid.DataSource is EditableTagList editableTags)
                {
                    await editableTags.TranslateAllAsync();
                }
                else if (grid.DataSource is MultiSelectDataTable multiSelectTags)
                {
                    await multiSelectTags.TranslateAllAsync();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                SetStatus(I18n.GetText("StatusTranslationComplete"));
            }
        }

        // True while a batch job (auto-tag, translate, batch edit…) has the UI
        // locked; the FormClosing handler uses it to refuse exit mid-job.
        private bool editLocked;

        internal void LockEdit(bool locked)
        {
            editLocked = locked;
            menuStrip1.Enabled = !locked;
            toolStripTags.Enabled = !locked;
            toolStripAllTags.Enabled = !locked;
            gridViewTags.Enabled = !locked;
            if (gridViewTags.SelectedRows.Count == 1)
                gridViewTags.AllowDrop = !locked;
            gridViewAllTags.Enabled = !locked;
            gridViewDS.Enabled = !locked;
            if (datasetBrowserView != null)
                datasetBrowserView.Enabled = !locked;
            gridViewAutoTags.Enabled = !locked;
            toolStripAutoTags.Enabled = !locked;
        }

        private void ShowPreview(string imgPath, bool separateWindow = false)
        {
            if (VideoProcessingService.IsVideoFile(imgPath))
            {
                if (separateWindow || Program.Settings.PreviewType == ImagePreviewType.SeparateWindow)
                {
                    Process.Start(new ProcessStartInfo(imgPath) { UseShellExecute = true });
                    return;
                }

                if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
                {
                    int previewSize = Math.Max(Program.Settings.PreviewSize, 320);
                    Image videoPreview = Extensions.MakeVideoThumb(imgPath, previewSize, drawBadge: false);
                    if (videoPreview == null)
                        return;
                    ShowEmbeddedPreview(imgPath, videoPreview);
                }
                return;
            }
            Image img = Program.DataManager.GetImageFromFileWithCache(imgPath);
            if (img == null)
                return;
            if (separateWindow || Program.Settings.PreviewType == ImagePreviewType.SeparateWindow)
            {
                if (fPreview == null || fPreview.IsDisposed)
                    fPreview = new Form_preview();
                fPreview.Show(this, img, Path.GetFileName(imgPath));
            }
            else if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
            {
                ShowEmbeddedPreview(imgPath, img);
            }
        }

        /// <summary>
        /// Hands the loaded image to the sidebar preview panel (which owns the
        /// detach-before-dispose swap; GetImageFromFileWithCache always returns
        /// a caller-owned instance, so the previous image must be disposed
        /// regardless of the CacheOpenImages setting).
        /// </summary>
        private void ShowEmbeddedPreview(string imgPath, Image img)
        {
            lastEmbeddedPreviewPaths = new List<string> { imgPath };
            datasetPreviewPanel.SetImage(img, Path.GetFileName(imgPath));
        }

        /// <summary>
        /// Multi-selection preview: the first <see cref="DatasetPreviewPanel.MaxImages"/>
        /// selected images side by side; the header shows the total count.
        /// </summary>
        private void ShowEmbeddedPreviewMulti(IReadOnlyList<string> paths)
        {
            var loaded = new List<Image>();
            var used = new List<string>();
            foreach (string path in paths)
            {
                if (loaded.Count >= DatasetPreviewPanel.MaxImages)
                    break;
                if (string.IsNullOrEmpty(path))
                    continue;
                Image img = VideoProcessingService.IsVideoFile(path)
                    ? Extensions.MakeVideoThumb(path, 320, drawBadge: true)
                    : Program.DataManager.GetImageFromFileWithCache(path);
                if (img == null)
                    continue;
                loaded.Add(img);
                used.Add(path);
            }
            if (loaded.Count == 0)
            {
                HidePreview();
                return;
            }
            lastEmbeddedPreviewPaths = used;
            datasetPreviewPanel.SetImages(loaded,
                string.Format(I18n.GetText("FolderListImageCount"), paths.Count));
        }

        private void HidePreview()
        {
            fPreview?.Hide();
            lastEmbeddedPreviewPaths = new List<string>();
            datasetPreviewPanel.SetImage(null, null);
        }

        // Bumped on every LoadSelectedImageToGrid call so a slow, superseded
        // load can tell it lost the race and must not touch the grid.
        private int loadSelectedImageToGridVersion;

        private async void LoadSelectedImageToGrid()
        {
            int loadVersion = ++loadSelectedImageToGridVersion;
            // Commit any in-progress cell edit before the grid is rebound: an
            // abandoned IEditableObject transaction would silently swallow
            // later programmatic tag updates (text-mirror desync).
            (gridViewTags.DataSource as EditableTagList)?.EndEdit();
            gridViewTags.AutoGenerateColumns = false;
            if (gridViewDS.SelectedRows.Count == 0)
            {
                BtnTagImageChecker.Enabled = true;
                return;
            }
            gridViewTags.SuspendLayout();
            try
            {
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    BtnTagImageChecker.Enabled = false;
                    string selectedPath = gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value as string;
                    // Guard against a stale/placeholder row (null path) or an item that
                    // was just removed from the dataset, so we never pass a null key to
                    // the DataSet dictionary.
                    if (string.IsNullOrEmpty(selectedPath)
                        || !Program.DataManager.DataSet.TryGetValue(selectedPath, out DataItem selectedItem))
                    {
                        gridViewTags.DataSource = null;
                        gridViewTags.AllowDrop = false;
                        return;
                    }
                    gridViewTags.Tag = selectedPath;
                    ChageImageColumn(false);
                    gridViewTags.DataSource = selectedItem.Tags;
                    if (IsPreviewFollowActive)
                    {
                        ShowPreview(selectedPath);
                    }
                    gridViewTags.AllowDrop = true;
                }
                else
                {
                    BtnTagImageChecker.Enabled = true;
                    if (IsPreviewFollowActive)
                    {
                        RefreshEmbeddedPreviewFromSelection();
                    }
                    gridViewTags.DataSource = null;
                    gridViewTags.AllowDrop = false;
                    gridViewTags.Rows.Clear();
                    ChageImageColumn(true);

                    gridViewTags.Tag = "0";
                    List<DataItem> selectedTagsList = new List<DataItem>();
                    for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                    {
                        string path = gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value as string;
                        // Skip stale/removed rows so a null or missing key can't crash the grid.
                        if (!string.IsNullOrEmpty(path)
                            && Program.DataManager.DataSet.TryGetValue(path, out DataItem multiItem))
                        {
                            selectedTagsList.Add(multiItem);
                        }
                    }

                    MultiSelectDataTable multiSelectData = new MultiSelectDataTable();
                    multiSelectData.SetTranslationMode(isTranslate);
                    await multiSelectData.CreateTableFromSelectedImages(selectedTagsList);
                    // The user may have re-selected while the table was being
                    // built: a stale result must not overwrite the new grid.
                    if (loadVersion != loadSelectedImageToGridVersion)
                        return;
                    gridViewTags.DataSource = multiSelectData;
                }

                if (Program.Settings.AutoSort)
                {
                    SortPrompt();
                }

                gridViewDS.Focus();
                if (isTranslate)
                {
                    var dsType = GetTagsDataSourceType();
                    if (dsType == DataSourceType.Single)
                    {
                        await ((EditableTagList)gridViewTags.DataSource).TranslateAllAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // Runs on every selection change (async void): a translation/network
                // failure must not escape as an unhandled exception.
                Trace.WriteLine($"LoadSelectedImageToGrid failed: {ex}");
                SetStatus(ex.Message);
            }
            finally
            {
                gridViewTags.ResumeLayout();
            }
            //await FillTranslation(gridViewTags);
        }

        /// <summary>
        /// Add or remove Image column
        /// </summary>
        /// <param name="add"> true to add, false to remove</param>
        private void ChageImageColumn(bool add)
        {
            DataGridViewColumn column = gridViewTags.Columns["ImageName"];
            column.Visible = add;
            if (add)
            {
                // The grid-level auto column sizing would grow this to the
                // widest kohya file name and squeeze the tag column out of
                // view: cap it to a fixed width, single line (tooltip shows
                // the full name).
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = LogicalToDeviceUnits(150);
                column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            }
        }

        private Rectangle dragBoxFromMouseDown;
        private int rowIndexFromMouseDown;
        private int rowIndexOfItemUnderMouseToDrop;
        private void dataGridView1_MouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                // If the mouse moves outside the rectangle, start the drag.
                if (dragBoxFromMouseDown != Rectangle.Empty &&
                    !dragBoxFromMouseDown.Contains(e.X, e.Y))
                {

                    // Proceed with the drag and drop, passing in the list item.                    
                    DragDropEffects dropEffect = gridViewTags.DoDragDrop(
                    gridViewTags.Rows[rowIndexFromMouseDown],
                    DragDropEffects.Move);
                }
            }
        }

        private void dataGridView1_MouseDown(object sender, MouseEventArgs e)
        {
            // Get the index of the item the mouse is below.
            rowIndexFromMouseDown = gridViewTags.HitTest(e.X, e.Y).RowIndex;
            if (rowIndexFromMouseDown != -1)
            {
                // Remember the point where the mouse down occurred. 
                // The DragSize indicates the size that the mouse can move 
                // before a drag event should be started.                
                Size dragSize = SystemInformation.DragSize;

                // Create a rectangle using the DragSize, with the mouse position being
                // at the center of the rectangle.
                dragBoxFromMouseDown = new Rectangle(new Point(e.X - (dragSize.Width / 2),
                                                               e.Y - (dragSize.Height / 2)),
                                    dragSize);
            }
            else
                // Reset the rectangle if the mouse is not over an item in the ListBox.
                dragBoxFromMouseDown = Rectangle.Empty;
        }

        private void dataGridView1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void dataGridView1_DragDrop(object sender, DragEventArgs e)
        {
            // The mouse locations are relative to the screen, so they must be 
            // converted to client coordinates.
            Point clientPoint = gridViewTags.PointToClient(new Point(e.X, e.Y));

            // Get the row index of the item the mouse is below. 
            rowIndexOfItemUnderMouseToDrop =
                gridViewTags.HitTest(clientPoint.X, clientPoint.Y).RowIndex;

            // If the drag operation was a move then remove and insert the row.
            if (e.Effect == DragDropEffects.Move)
            {
                if (rowIndexFromMouseDown != rowIndexOfItemUnderMouseToDrop)
                {
                    int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(rowIndexFromMouseDown, rowIndexOfItemUnderMouseToDrop);
                    gridViewTags.ClearSelection();
                    gridViewTags[0, destIndex].Selected = true;
                }
            }
        }

        private void BtnAddTag_Clicked(object sender, EventArgs e)
        {
            AddNewRow();
        }

        private void AddNewRow()
        {
            if (gridViewTags.DataSource == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (gridViewDS.SelectedRows.Count > 1)
            {
                using (Form_addTag addTag = new Form_addTag())
                {
                    if (gridViewTags.SelectedCells.Count > 0)
                    {
                        addTag.tagTextBox.Text = ((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[gridViewTags.SelectedCells[0].RowIndex]).GetTagText();
                        addTag.tagTextBox.SelectAll();
                    }
                    if (addTag.ShowDialog() == DialogResult.OK)
                    {
                        AddingType addType = Extensions.GetEnumItemFromFriendlyText<DatasetManager.AddingType>((string)addTag.comboBox1.SelectedItem);
                        int customIndex = (int)addTag.numericUpDown1.Value;
                        bool skipExist = addTag.checkBoxSkipExist.Checked;
                        AddTagMultiselectedMode(addTag.tagTextBox.Text, skipExist, addType, customIndex);
                    }
                    addTag.Close();
                }
            }
            else
            {
                // Autocomplete input dialog instead of appending empty rows;
                // the tag lands right after the currently selected row.
                string tag = PromptForNewTag();
                if (string.IsNullOrWhiteSpace(tag))
                    return;
                tag = tag.Trim();
                var tagList = (EditableTagList)gridViewTags.DataSource;
                int selectedIndex = GetFirstDGVSelectionIndex(gridViewTags);
                (int _, int newIndex) = selectedIndex >= 0
                    ? tagList.AddTag(tag, skipExist: true, AddingType.Custom, selectedIndex + 1)
                    : tagList.AddTag(tag, skipExist: true, AddingType.Down);
                if (newIndex >= 0 && newIndex < gridViewTags.RowCount)
                    SetDGVSelection(gridViewTags, newIndex, "ImageTags");
            }
        }

        /// <summary>
        /// Autocomplete source: the tag DB when the user installed one, else
        /// the current dataset's own tags (Tags/ ships empty, and an empty
        /// value list means the popup silently never appears).
        /// </summary>
        private List<TagsDB.TagItem> GetAutocompleteValues()
        {
            List<TagsDB.TagItem> values = Program.TagsList?.Tags;
            if ((values == null || values.Count == 0) && Program.DataManager != null)
            {
                values = Program.DataManager.AllTags.GetAllTagsList()
                    .Select(tag =>
                    {
                        var item = new TagsDB.TagItem();
                        item.SetTag(tag);
                        return item;
                    })
                    .ToList();
            }
            return values ?? new List<TagsDB.TagItem>();
        }

        /// <summary>
        /// Small modal with the same tag autocomplete the grid editor uses;
        /// Enter picks the suggestion first, a second Enter confirms.
        /// </summary>
        private string PromptForNewTag()
        {
            using var dialog = new Form
            {
                Text = I18n.GetText("UIAddTagForm"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(LogicalToDeviceUnits(380), LogicalToDeviceUnits(250))
            };
            var label = new Label
            {
                Text = I18n.GetText("UIAddTagTag"),
                AutoSize = true,
                Location = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(12))
            };
            var textBox = new AutoCompleteTextBox
            {
                Location = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(36)),
                Width = dialog.ClientSize.Width - LogicalToDeviceUnits(24)
            };
            if (Program.Settings.AutocompleteMode != AutocompleteMode.Disable)
            {
                textBox.SetAutocompleteMode(Program.Settings.AutocompleteMode, Program.Settings.AutocompleteSort);
                textBox.Values = GetAutocompleteValues();
            }
            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            var cancelButton = new Button
            {
                Text = I18n.GetText("BtnCancel"),
                DialogResult = DialogResult.Cancel,
                Size = new Size(LogicalToDeviceUnits(88), LogicalToDeviceUnits(28))
            };
            okButton.Location = new Point(
                dialog.ClientSize.Width - LogicalToDeviceUnits(12) - okButton.Width * 2 - LogicalToDeviceUnits(8),
                dialog.ClientSize.Height - LogicalToDeviceUnits(40));
            cancelButton.Location = new Point(
                dialog.ClientSize.Width - LogicalToDeviceUnits(12) - cancelButton.Width,
                dialog.ClientSize.Height - LogicalToDeviceUnits(40));
            // No AcceptButton/CancelButton bindings: Enter/Escape must first go
            // to the autocomplete popup (pick suggestion / close list); only a
            // second press closes the dialog.
            bool suppressEnterClose = false;
            bool suppressEscapeClose = false;
            textBox.ItemSelectionComplete += (_, _) => suppressEnterClose = true;
            textBox.ListBoxClosedByEscape += () => suppressEscapeClose = true;
            textBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    if (suppressEnterClose)
                    {
                        suppressEnterClose = false;
                        return;
                    }
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    if (suppressEscapeClose)
                    {
                        suppressEscapeClose = false;
                        return;
                    }
                    dialog.DialogResult = DialogResult.Cancel;
                    dialog.Close();
                }
            };
            dialog.Controls.Add(label);
            dialog.Controls.Add(textBox);
            dialog.Controls.Add(okButton);
            dialog.Controls.Add(cancelButton);
            Program.ColorManager.ChangeColorScheme(dialog, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(dialog.Controls, Program.ColorManager.SelectedScheme);
            return dialog.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
        }

        private void SetDGVSelection(DataGridView dgv, int index, string column)
        {
            dgv.ClearSelection();
            dgv[column, index].Selected = true;
        }

        private int GetFirstDGVSelectionIndex(DataGridView dgv)
        {
            if (dgv.SelectedCells.Count == 0)
                return -1;
            return dgv.SelectedCells[0].RowIndex;
        }

        private void BtnTagDelete_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0)
                return;
            gridViewTags.Rows.RemoveAt(gridViewTags.SelectedCells[0].RowIndex);
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0 || gridViewTags.SelectedCells[0].RowIndex == 0)
                return;
            int curIndex = gridViewTags.SelectedCells[0].RowIndex;
            int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(curIndex, curIndex - 1);
            gridViewTags.ClearSelection();
            gridViewTags["ImageTags", destIndex].Selected = true;
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0 || gridViewTags.SelectedCells[0].RowIndex == gridViewTags.RowCount - 1)
                return;
            int curIndex = gridViewTags.SelectedCells[0].RowIndex;
            int destIndex = ((EditableTagList)gridViewTags.DataSource).Move(curIndex, curIndex + 1);
            gridViewTags["ImageTags", destIndex].Selected = true;
        }

        private void toolStripButton6_Click(object sender, EventArgs e)
        {
            isAllTags = !isAllTags;
            if (isAllTags)
            {
                toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
                Program.DataManager.AllTagsBindingSource.RemoveFilter();
            }
            else
            {
                toolStripLabelAllTags.Text = I18n.GetText("UILabelCommonTags");
                Program.DataManager.AllTags.SetFilterByCount(Program.DataManager.DataSet.Count);
            }
        }

        private void BtnAddTagForAll_Click(object sender, EventArgs e)
        {
            AddTagToAll(false);
        }

        private async void AddTagToAll(bool filtered)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            Form_addTag addTag = new Form_addTag();
            int index = gridViewAllTags.RowCount;
            if (gridViewAllTags.SelectedCells.Count > 0)
            {
                index = gridViewAllTags.SelectedCells[0].RowIndex;
                addTag.tagTextBox.Text = (string)gridViewAllTags.Rows[index].Cells[0].Value;
                addTag.tagTextBox.SelectAll();
            }
            if (addTag.ShowDialog() == DialogResult.OK)
            {
                int customIndex = (int)addTag.numericUpDown1.Value;
                bool skipExist = addTag.checkBoxSkipExist.Checked;
                DatasetManager.AddingType addType = Extensions.GetEnumItemFromFriendlyText<DatasetManager.AddingType>((string)addTag.comboBox1.SelectedItem);
                Program.DataManager.AddTagToAll(addTag.tagTextBox.Text, skipExist, addType, customIndex, filtered);
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    if (isTranslate)
                    {
                        await ((EditableTagList)gridViewTags.DataSource).TranslateAllAsync();
                    }
                }
                else
                {
                    AddTagMultiselectedMode(addTag.tagTextBox.Text, skipExist, addType, customIndex);
                }
            }
            addTag.Close();
        }

        private void toolStripButton8_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                MessageBox.Show(I18n.GetText("TipReplaceNotSupportMultSel"));
                return;
            }

            if (gridViewAllTags.SelectedCells.Count == 0)
                return;
            Form_replaceAll replaceAll = new Form_replaceAll();
            replaceAll.DataSetFiltered = isFiltered;
            replaceAll.comboBox1.DataSource = Program.DataManager.AllTags;
            replaceAll.comboBox1.DisplayMember = "Tag";
            List<int> selectedCells = new List<int>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                if (!selectedCells.Contains(gridViewAllTags.SelectedCells[i].RowIndex))
                    selectedCells.Add(gridViewAllTags.SelectedCells[i].RowIndex);
            }
            selectedCells.Reverse();
            replaceAll.comboBox1.SelectedIndex = selectedCells[0];
            replaceAll.SetNewTagValues(Program.DataManager.AllTags.GetAllTagsList());
            if (selectedCells.Count > 1)
            {
                replaceAll.SetNewTagText((string)gridViewAllTags["TagsColumn", selectedCells[1]].Value);
            }
            if (replaceAll.ShowDialog() == DialogResult.OK)
            {
                Program.DataManager.ReplaceTagInAll(
                    ((AllTagsItem)replaceAll.comboBox1.SelectedItem).Tag,
                    replaceAll.NewTagText,
                    replaceAll.DataSetFiltered);
            }
            replaceAll.Close();
        }

        private void saveAllChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            Program.DataManager.SaveAll();
            Program.DataManager.UpdateDatasetHash();
            if (!ReportSaveErrorsIfAny())
                SetStatus(I18n.GetText("StatusSaved"));
            LoadSelectedImageToGrid();
        }

        private void showPreviewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow)
            {
                ToggleEmbeddedPreview();
                return;
            }
            isShowPreview = !isShowPreview;
            MenuShowPreview.Checked = isShowPreview;
            if (isShowPreview && gridViewDS.SelectedRows.Count == 1)
            {
                ShowPreview((string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value);
            }
            else
            {
                HidePreview();
            }
        }

        private void toolStripButton9_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 1)
            {
                List<string> tagsToCopy = new List<string>();
                for (int i = 0; i < gridViewTags.RowCount; i++)
                {
                    tagsToCopy.Add((string)gridViewTags["ImageTags", i].Value);
                }
                try
                {
                    Clipboard.SetData("TagList", tagsToCopy);
                    SetStatus(I18n.GetText("StatusCopied"));
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    // Clipboard busy in another process.
                    SetStatus(ex.Message);
                }
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                MessageBox.Show(I18n.GetText("TipMultiImageCopy"));
            }
            else
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
            }
        }

        internal void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        /// <summary>
        /// Shows the per-file failures collected by the last DataManager.SaveAll().
        /// Returns true when there were failures (items stay marked modified).
        /// </summary>
        private bool ReportSaveErrorsIfAny()
        {
            var errors = Program.DataManager?.LastSaveErrors;
            if (errors == null || errors.Count == 0)
                return false;
            string details = string.Join("\n", errors.Take(10));
            if (errors.Count > 10)
                details += "\n...";
            MessageBox.Show(this, string.Format(I18n.GetText("TipSaveErrors"), errors.Count, details),
                "BooruDatasetTagManagerPlus", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }

        private void SetDSCountStatus(string text)
        {
            toolStripLabelDSShown.Text = text;
        }

        private async void BtnPasteTag_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 1)
            {
                if (Clipboard.ContainsData("TagList"))
                {
                    // "as" instead of a hard cast: foreign clipboard content under
                    // the same format name must not throw InvalidCastException.
                    List<string> copiedTags = Clipboard.GetData("TagList") as List<string>;
                    if (copiedTags != null && copiedTags.Count > 0)
                    {
                        var eTagList = (EditableTagList)gridViewTags.DataSource;
                        eTagList.Clear();
                        eTagList.AddRange(copiedTags, true);
                        if (isTranslate)
                            await FillTranslation(gridViewTags);
                        SetStatus(I18n.GetText("StatusPasted"));
                    }
                    else
                    {
                        MessageBox.Show(I18n.GetText("TipClipboardEmpty"));
                    }
                }
                else
                {
                    MessageBox.Show(I18n.GetText("TipClipboardEmpty"));
                }
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                MessageBox.Show(I18n.GetText("TipMultiImagePaste"));
            }
            else
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
            }
        }

        private async void toolStripButton11_Click(object sender, EventArgs e)
        {
            await ApplyTagHistoryActionAsync(tags => tags.PrevState());
        }

        private void BtnDeleteTagForAll_Click(object sender, EventArgs e)
        {
            RemoveTagFromAll(false);
        }

        private void RemoveTagFromAll(bool filtered)
        {
            HashSet<string> tagsToDelete = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                var row = gridViewAllTags.SelectedCells[i].RowIndex;
                string tag = (string)gridViewAllTags.Rows[row].Cells["TagsColumn"].Value;
                if (!string.IsNullOrWhiteSpace(tag))
                    tagsToDelete.Add(tag);
            }

            if (tagsToDelete.Count == 0)
                return;

            LockEdit(true);
            gridViewAllTags.SuspendLayout();
            gridViewTags.SuspendLayout();
            try
            {
                Program.DataManager.DeleteTagsFromAll(tagsToDelete, filtered);
                if (gridViewDS.SelectedRows.Count > 1)
                    LoadSelectedImageToGrid();
            }
            finally
            {
                gridViewTags.ResumeLayout();
                gridViewAllTags.ResumeLayout();
                LockEdit(false);
            }
        }

        private async void translateTagsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            isTranslate = !isTranslate;
            await ApplyTranslation(isTranslate);
        }

        private async Task ApplyTranslation(bool needTranslate)
        {
            MenuItemTranslateTags.Checked = needTranslate;
            if (Program.DataManager != null)
            {
                Program.DataManager.SetTranslationMode(needTranslate);
            }
            if (needTranslate)
            {
                await FillTranslation(gridViewAllTags);
                await FillTranslation(gridViewTags);
            }
            else
            {
                // The columns only exist after a translation pass; toggling the
                // menu before that dereferenced a null column.
                if (gridViewTags.Columns.Contains("Translation"))
                    gridViewTags.Columns["Translation"].Visible = false;
                if (gridViewAllTags.Columns.Contains("TranslationColumn"))
                    gridViewAllTags.Columns["TranslationColumn"].Visible = false;
            }
        }

        //private int findIndex = -1;
        private void toolStripButton13_Click(object sender, EventArgs e)
        {
            SetFilter();
        }

        private HashSet<string> GetSelectedTags()
        {
            HashSet<string> findTags = new HashSet<string>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                int row = gridViewAllTags.SelectedCells[i].RowIndex;
                string value = (string)gridViewAllTags.Rows[row].Cells[0].Value;
                if (!findTags.Contains(value))
                    findTags.Add(value);
            }
            return findTags;
        }

        private void SaveSelectedInViewDs()
        {
            selectedFiles.Clear();
            for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
            {
                selectedFiles.Add((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value);
            }
        }

        private void LoadSelectedInViewDs()
        {
            gridViewDS.ClearSelection();
            bool foundSelected = false;
            int firstDisplayed = 0;
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                if (selectedFiles.Contains((string)gridViewDS["ImageFilePath", i].Value))
                {
                    if (firstDisplayed == 0)
                        firstDisplayed = i;
                    gridViewDS.Rows[i].Selected = true;
                    foundSelected = true;
                }
            }
            if (!foundSelected && gridViewDS.RowCount > 0)
            {
                gridViewDS.Rows[0].Selected = true;
            }
            // Will throw an exception by itself if there is nothing found due to being set to -1 internally when the list is loaded in empty. Lazy bypass of 
            try
            {
                gridViewDS.FirstDisplayedScrollingRowIndex = firstDisplayed;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private void SetFilter()
        {
            if (gridViewAllTags.SelectedCells.Count > 0)
            {
                SaveSelectedInViewDs();
                if (isFiltered)
                {
                    ResetFilter();
                }

                gridViewDS.DataSource = Program.DataManager.GetDataSource(DatasetManager.OrderType.Name, filterAnd, GetSelectedTags());
                ApplyDataSetGridStyle();
                if (gridViewDS.RowCount == 0)
                    gridViewTags.DataSource = null;
                isFiltered = true;
                LoadSelectedInViewDs();
                // The visible dataset browser mirrors the hidden grid; without
                // this rebuild the filter only changed the hidden rows.
                RefreshDatasetFolderList();
                BtnImageExitFilter.Enabled = true;
            }
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.GetActiveScopeCount()));
        }

        private void ResetFilter()
        {
            if (isFiltered)
            {
                SaveSelectedInViewDs();
                gridViewDS.DataSource = Program.DataManager.GetDataSource();
                ApplyDataSetGridStyle();
                isFiltered = false;
                BtnImageExitFilter.Enabled = false;
                LoadSelectedInViewDs();
                RefreshDatasetFolderList();
            }
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.GetActiveScopeCount()));
        }

        private void toolStripButton14_Click(object sender, EventArgs e)
        {
            ResetFilter();
        }

        private async void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                BtnTagDelete.PerformClick();
            }
            else if (e.KeyCode == Keys.Insert)
            {
                BtnTagAdd.PerformClick();
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                await PasteTagsFromClipboard();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                if (gridViewTags.CurrentCell != null && !gridViewTags.CurrentCell.IsInEditMode)
                {
                    List<string> tagsToCopy = new List<string>();
                    tagsToCopy.Add((string)gridViewTags["ImageTags", gridViewTags.CurrentCell.RowIndex].Value);
                    DataObject d = new DataObject();
                    d.SetText(string.Join("\r\n", tagsToCopy));
                    d.SetData("PartTagList", tagsToCopy);
                    try
                    {
                        Clipboard.SetDataObject(d);
                        SetStatus(I18n.GetText("StatusCopied"));
                    }
                    catch (System.Runtime.InteropServices.ExternalException ex)
                    {
                        SetStatus(ex.Message);
                    }
                    e.SuppressKeyPress = true;
                }
            }
        }

        private async Task PasteTagsFromClipboard()
        {
            if (Clipboard.ContainsData("PartTagList"))
            {
                List<string> copiedTags = Clipboard.GetData("PartTagList") as List<string>;
                if (copiedTags != null && copiedTags.Count > 0)
                {
                    if (gridViewDS.SelectedRows.Count == 1)
                    {

                        var eTagList = (EditableTagList)gridViewTags.DataSource;
                        copiedTags.ForEach(a => eTagList.AddTag(a, true, AddingType.Down));
                        if (isTranslate)
                            await FillTranslation(gridViewTags);
                        SetStatus(I18n.GetText("StatusPasted"));

                    }
                    else if (gridViewDS.SelectedRows.Count > 1)
                    {
                        foreach (var t in copiedTags)
                        {
                            AddTagMultiselectedMode(t, true, AddingType.Down);
                        }
                    }
                }
            }
            else
            {
                SetStatus(I18n.GetText("TipClipboardEmpty"));
            }
        }

        private void loadLossFromFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private async void toolStripButton15_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    var lines = PromptParser.ParsePrompt(text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                    EditableTagList tagList = new EditableTagList();
                    tagList.LoadFromPromptParserData(lines);
                    gridViewTags.DataSource = tagList;
                }
                //MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                //return;
            }
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                var lines = PromptParser.ParsePrompt(text, Program.Settings.FixTagsOnSaveLoad, Program.Settings.SeparatorOnLoad);
                var tagsDSType = GetTagsDataSourceType();
                if (tagsDSType == DataSourceType.Single)
                {
                    EditableTagList etl = (EditableTagList)gridViewTags.DataSource;
                    etl.Clear();
                    etl.AddRange(lines, true);
                }
                if (isTranslate)
                    await FillTranslation(gridViewTags);
            }
        }

        private DataSourceType GetTagsDataSourceType()
        {
            if (gridViewTags.DataSource == null)
                return DataSourceType.None;
            else if (gridViewTags.DataSource.GetType() == typeof(EditableTagList))
                return DataSourceType.Single;
            else if (gridViewTags.DataSource.GetType() == typeof(MultiSelectDataTable))
                return DataSourceType.Multi;
            else
                throw new Exception("Unknown datasource type!");
        }

        private void toolStripButton16_Click(object sender, EventArgs e)
        {
            var dts = GetTagsDataSourceType();
            if (dts == DataSourceType.Single)
            {
                Form_Edit fPrint = new Form_Edit();
                EditableTagList tagsDS = (EditableTagList)gridViewTags.DataSource;
                fPrint.textBox1.Text = tagsDS.ToString();
                fPrint.Show();
            }
        }

        private void dataGridView1_EditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            if (gridViewTags.CurrentCell.ColumnIndex == 0)
            {
                AutoCompleteTextBox autoText = e.Control as AutoCompleteTextBox;
                if (autoText != null)
                {
                    //autoText.SetParent(gridViewTags);
                    if (Program.Settings.AutocompleteMode != AutocompleteMode.Disable && autoText.Values == null)
                    {
                        autoText.SetAutocompleteMode(Program.Settings.AutocompleteMode, Program.Settings.AutocompleteSort);
                        autoText.Values = GetAutocompleteValues();
                    }
                    //autoText.Location = new Point(10, 10);
                    //autoText.Size = new Size(25, 75);
                    //autoText.AutoCompleteMode = AutoCompleteMode.Suggest;
                    //autoText.AutoCompleteSource = AutoCompleteSource.CustomSource;
                    //autoText.AutoCompleteCustomSource = Program.TagsList.Tags;
                }
            }
        }

        private void toolStripButton17_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                MessageBox.Show("Select one image!");
                return;
            }
            if (GetTagsDataSourceType() != DataSourceType.Single)
            {
                SetStatus(I18n.GetText("TipMultiImagePaste"));
                return;
            }
            EditableTagList clonedTagList = (EditableTagList)((EditableTagList)gridViewTags.DataSource).Clone();
            switch (MessageBox.Show(I18n.GetText("TipSetToAllText"), I18n.GetText("TipSetToAllTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes:
                    Program.DataManager.SetTagListToAll(clonedTagList, true);
                    break;
                case DialogResult.No:
                    Program.DataManager.SetTagListToAll(clonedTagList, false);
                    break;
                case DialogResult.Cancel:
                    return;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (editLocked)
            {
                // A background batch is mid-flight on this thread's
                // continuations; closing now would kill it half-applied.
                MessageBox.Show(this, I18n.GetText("TipJobRunningCannotClose"), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            if (Program.DataManager != null && Program.DataManager.IsDataSetChanged())
            {
                DialogResult result = MessageBox.Show(I18n.GetText("TipDSChangeSaveText"), I18n.GetText("TipDSChangeSaveTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    Program.DataManager.SaveAll();
                    // The user explicitly chose to save: a locked/read-only tag
                    // file must block the exit, otherwise the failed items'
                    // in-memory edits are silently lost with the process.
                    if (ReportSaveErrorsIfAny())
                        e.Cancel = true;
                }
                else if (result == DialogResult.Cancel)
                    e.Cancel = true;
            }
        }

        private void dataGridView2_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1 || e.ColumnIndex == -1)
                return;
            ExecuteAllTagsQuickAction();
        }

        /// <summary>
        /// Double-click on an All Tags row runs the configurable quick action
        /// (Settings → General; default opens "Replace all" with the tag as
        /// source). Each action maps onto one of the All Tags toolbar buttons;
        /// the double-clicked row is already selected by the first click.
        /// </summary>
        private void ExecuteAllTagsQuickAction()
        {
            switch (Program.Settings.AllTagsDoubleClickAction)
            {
                case AllTagsQuickAction.QuickActionAddTagToAll:
                    BtnTagAddToAll.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionDeleteTagFromAll:
                    BtnTagDeleteForAll.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionAddTagToSelected:
                    BtnTagAddToSelected.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionDeleteTagFromSelected:
                    BtnTagDeleteForSelected.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionAddTagToFiltered:
                    BtnTagAddToFiltered.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionDeleteTagFromFiltered:
                    BtnTagDeleteForFiltered.PerformClick();
                    break;
                case AllTagsQuickAction.QuickActionFilterByTag:
                    BtnTagFilter.PerformClick();
                    break;
                default:
                    BtnTagReplace.PerformClick();
                    break;
            }
        }

        private void dataGridView3_DataSourceChanged(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// (Re)applies the dataset grid column layout. Rebinding regenerates
        /// the auto columns, so this must run after every DataSource
        /// assignment: thumbnail zoomed, name filling the remaining width (no
        /// horizontal scrollbar), noisy columns hidden per the persisted
        /// header-menu choices.
        /// </summary>
        private void ApplyDataSetGridStyle()
        {
            for (int i = 0; i < gridViewDS.ColumnCount; i++)
            {
                if (gridViewDS.Columns[i].ValueType == typeof(Image))
                {
                    ((DataGridViewImageColumn)gridViewDS.Columns[i]).ImageLayout = DataGridViewImageCellLayout.Zoom;
                    gridViewDS.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
                }
            }
            if (gridViewDS.Columns.Contains("Name"))
            {
                gridViewDS.Columns["Name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                gridViewDS.Columns["Name"].MinimumWidth = LogicalToDeviceUnits(60);
                gridViewDS.Columns["Name"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
            List<string> hidden = Program.Settings.DatasetHiddenColumns ?? AppSettings.GetDefaultDatasetHiddenColumns();
            for (int i = 0; i < gridViewDS.ColumnCount; i++)
            {
                DataGridViewColumn column = gridViewDS.Columns[i];
                column.Visible = !hidden.Contains(column.Name, StringComparer.OrdinalIgnoreCase);
            }
            if (gridViewDS.ColumnCount > 0
                && gridViewDS.Columns.GetColumnCount(DataGridViewElementStates.Visible) == 0)
            {
                gridViewDS.Columns[0].Visible = true;
            }
            ApplyDataSetColumnHeaders();
        }

        private void ApplyDataSetColumnHeaders()
        {
            // These header texts also feed the header right-click menu used to
            // toggle column visibility, so every column needs a translation.
            if (gridViewDS.Columns.Contains("Img"))
                gridViewDS.Columns["Img"].HeaderText = I18n.GetText("GridImage");
            if (gridViewDS.Columns.Contains("Name"))
                gridViewDS.Columns["Name"].HeaderText = I18n.GetText("GridName");
            if (gridViewDS.Columns.Contains("ImageFilePath"))
                gridViewDS.Columns["ImageFilePath"].HeaderText = I18n.GetText("GridImageFilePath");
            if (gridViewDS.Columns.Contains("ImageModifyTime"))
                gridViewDS.Columns["ImageModifyTime"].HeaderText = I18n.GetText("GridImageModifyTime");
            if (gridViewDS.Columns.Contains("TagsModifyTime"))
                gridViewDS.Columns["TagsModifyTime"].HeaderText = I18n.GetText("GridTagsModifyTime");
        }

        private void dataGridView3_SelectionChanged(object sender, EventArgs e)
        {
            if (!selectionMode)
            {
                // Keep the visible browser in sync when code changes the hidden
                // grid's selection directly.
                datasetBrowserView?.SetSelectedImagePaths(GetGridSelectedPaths());
                LoadSelectedImageToGrid();
            }
        }


        private int GetgridViewTagsHash()
        {
            List<string> tags = new List<string>();
            for (int i = 0; i < gridViewTags.RowCount; i++)
            {
                tags.Add((string)gridViewTags["ImageTags", i].Value);
            }
            return string.Join("|", tags).GetHashCode();
        }

        private void dataGridViewTags_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != -1 && e.RowIndex != -1)
            {
                if (gridViewTags.Columns["ImageName"].Visible)
                {
                    if (e.RowIndex != previewRowIndex)
                    {

                        //var dataItem = Program.DataManager.DataSet[(string)gridViewTags["Image", e.RowIndex].Value];
                        //var dataItem = (DataItem)gridViewTags["ImageTags", e.RowIndex].Tag;
                        var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[e.RowIndex]).ExtendedProperties["DataItem"];
                        previewPicBox.Size = new Size(Program.Settings.PreviewSize, Program.Settings.PreviewSize);
                        previewPicBox.Image = dataItem.Img;
                        previewPicBox.SizeMode = PictureBoxSizeMode.AutoSize;
                        previewPicBox.Location = new Point(splitContainer1.Panel2.Location.X + splitContainer2.Panel2.Location.X, PointToClient(Cursor.Position).Y);

                        if (!this.Controls.ContainsKey("previewPicBox"))
                        {
                            this.Controls.Add(previewPicBox);
                        }
                        previewPicBox.BringToFront();
                        previewRowIndex = e.RowIndex;
                    }
                }
                else
                {
                    if (this.Controls.ContainsKey("previewPicBox"))
                    {
                        this.Controls.RemoveByKey("previewPicBox");
                        previewRowIndex = -1;
                    }
                }
            }
        }

        private void dataGridViewTags_CellMouseLeave(object sender, DataGridViewCellEventArgs e)
        {
            if (this.Controls.ContainsKey("previewPicBox"))
            {
                this.Controls.RemoveByKey("previewPicBox");
                previewRowIndex = -1;
            }
        }

        private void toolStripButton18_Click(object sender, EventArgs e)
        {
            switch (filterAnd)
            {
                case FilterType.Not:
                    filterAnd = FilterType.Or;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.ORIcon;
                    break;
                case FilterType.Or:
                    filterAnd = FilterType.Xor;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.XORIcon;
                    break;
                case FilterType.Xor:
                    filterAnd = FilterType.And;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.ANDIcon;
                    break;
                case FilterType.And:
                    filterAnd = FilterType.Not;
                    BtnTagMultiModeSwitch.Image = Properties.Resources.NOTIcon;
                    break;
                default:
                    throw new ArgumentException($"Invalid filter type: {filterAnd}");
            }
            SetFilter();
        }

        private async void gridViewTags_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (gridViewTags.Columns["ImageTags"].Index == e.ColumnIndex && e.RowIndex != -1)
            {
                string editedValue = (string)gridViewTags[e.ColumnIndex, e.RowIndex].Value;
                if (string.IsNullOrEmpty(editedValue))
                    return;
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    for (int i = 0; i < gridViewTags.RowCount; i++)
                    {
                        if (i != e.RowIndex && (string)gridViewTags[e.ColumnIndex, i].Value == editedValue)
                        {
                            this.BeginInvoke(new MethodInvoker(() =>
                            {
                                gridViewTags.Rows.RemoveAt(e.RowIndex);
                            }));

                        }
                    }
                }
                else if (gridViewDS.SelectedRows.Count > 1)
                {
                    if (string.IsNullOrEmpty((string)gridViewTags["Image", e.RowIndex].Value))
                    {
                        MessageBox.Show(I18n.GetText("TipImageNameMustFilled"));
                        this.BeginInvoke(new MethodInvoker(() =>
                        {
                            gridViewTags.Rows.RemoveAt(e.RowIndex);
                        }));
                    }
                    else
                    {
                        //gridViewTags["Image", e.RowIndex].Tag = gridViewTags["ImageTags", e.RowIndex].Value;
                        //gridViewTags["Name", e.RowIndex].Tag = gridViewTags["ImageTags", e.RowIndex].Value;
                    }
                }
            }
            if (isTranslate && gridViewTags.DataSource is EditableTagList editableTags)
            {
                try
                {
                    await editableTags.TranslateAllAsync();
                }
                catch (Exception ex)
                {
                    // Fires after every cell edit (async void): a translation
                    // backend failure must not escape as unhandled.
                    Trace.WriteLine($"TranslateAllAsync failed: {ex}");
                    SetStatus(ex.Message);
                }
            }
        }

        private void toolStripButton19_Click(object sender, EventArgs e)
        {
            AddSelectedAllTagsToImageTags();
        }

        private void AddTagSingleSelectedMode(string tag)
        {
            if (gridViewDS.SelectedRows.Count != 1)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustEqual1"));
                return;
            }

            for (int i = 0; i < gridViewTags.RowCount; i++)
            {
                if ((string)gridViewTags["ImageTags", i].Value == tag)
                {
                    return;
                }
            }
            ((EditableTagList)gridViewTags.DataSource).AddTag(tag, true);
        }

        private async void AddTagMultiselectedMode(string tag, bool skipExist, AddingType addType, int pos = -1)
        {
            if (gridViewDS.SelectedRows.Count < 2)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustGreated1"));
                return;
            }
            ((MultiSelectDataTable)gridViewTags.DataSource).AddTag(tag, skipExist, addType, pos);
        }

        private void RemoveTagFromImageTags(string tag)
        {
            if (gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipSelectedImgMustGreated0"));
                return;
            }

            for (int i = gridViewTags.RowCount - 1; i >= 0; i--)
            {
                if (gridViewDS.SelectedRows.Count == 1)
                {
                    if ((string)gridViewTags["ImageTags", i].Value == tag)
                    {
                        gridViewTags.Rows.RemoveAt(i);
                    }
                }
                else
                {
                    var rowTag = (string)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[i]).ExtendedProperties["TextTag"];
                    if (rowTag == tag)
                    {
                        gridViewTags.Rows.RemoveAt(i);
                    }
                }
            }
        }

        private List<string> GetSelectedTagsInAllTags()
        {
            List<string> selectedTags = new List<string>();
            for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
            {
                var row = gridViewAllTags.SelectedCells[i].RowIndex;
                var tag = (string)gridViewAllTags.Rows[row].Cells["TagsColumn"].Value;
                if (!selectedTags.Contains(tag))
                    selectedTags.Add(tag);
            }
            return selectedTags;
        }

        private async void AddSelectedAllTagsToImageTags()
        {
            if (gridViewAllTags.SelectedCells.Count == 0 || gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipImgOrTagNotSelect"));
                return;
            }
            foreach (var item in GetSelectedTagsInAllTags())
            {
                if (gridViewDS.SelectedRows.Count == 1)
                    AddTagSingleSelectedMode(item);
                else
                    AddTagMultiselectedMode(item, true, AddingType.Down);
            }
            if (isTranslate)
                await FillTranslation(gridViewTags);
        }

        private void RemoveSelectedAllTagsToImageTags()
        {
            if (gridViewAllTags.SelectedCells.Count == 0 || gridViewDS.SelectedRows.Count == 0)
            {
                SetStatus(I18n.GetText("TipImgOrTagNotSelect"));
                return;
            }
            foreach (var item in GetSelectedTagsInAllTags())
            {
                RemoveTagFromImageTags(item);
            }
        }

        private void toolStripButton20_Click(object sender, EventArgs e)
        {
            RemoveSelectedAllTagsToImageTags();
        }

        private void toolStripButton21_Click(object sender, EventArgs e)
        {
            AddTagToAll(true);
        }

        private void toolStripButton22_Click(object sender, EventArgs e)
        {
            RemoveTagFromAll(true);
        }

        private void gridViewDS_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex != -1)
            {
                if (Enum.IsDefined(typeof(DatasetManager.OrderType), gridViewDS.Columns[e.ColumnIndex].Name))
                {
                    gridViewDS.DataSource = Program.DataManager.GetDataSourceWithLastFilter((DatasetManager.OrderType)Enum.Parse(typeof(DatasetManager.OrderType), gridViewDS.Columns[e.ColumnIndex].Name));
                    ApplyDataSetGridStyle();
                }
            }
        }

        private void toolStripButton23_Click(object sender, EventArgs e)
        {
            string searchedTag;
            if (gridViewDS.SelectedRows.Count == 1)
            {
                searchedTag = (string)gridViewTags["ImageTags", gridViewTags.CurrentCell.RowIndex].Value;
            }
            else if (gridViewDS.SelectedRows.Count > 1)
            {
                searchedTag = (string)gridViewTags.Rows[gridViewTags.CurrentCell.RowIndex].Tag;
            }
            else
                return;
            for (int i = 0; i < gridViewAllTags.RowCount; i++)
            {
                if (((string)gridViewAllTags[0, i].Value) == searchedTag)
                {
                    gridViewAllTags.ClearSelection();
                    gridViewAllTags.Rows[i].Selected = true;
                    SetAllTagsSelectionAnchor(searchedTag);
                    if (i < gridViewAllTags.FirstDisplayedScrollingRowIndex || i > gridViewAllTags.FirstDisplayedScrollingRowIndex + gridViewAllTags.DisplayedRowCount(false))
                    {
                        gridViewAllTags.FirstDisplayedScrollingRowIndex = i;
                    }
                }
            }
        }



        private void gridViewTags_KeyPress(object sender, KeyPressEventArgs e)
        {

        }

        private async void gridViewDS_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteImage();
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                await PasteTagsFromClipboard();
                e.SuppressKeyPress = true;
            }
        }

        private void DeleteImage()
        {
            if (gridViewDS.SelectedRows.Count < 1)
                return;
            if (MessageBox.Show(I18n.GetText("TipDeleteFile"), I18n.GetText("LabelDeleteFile"),
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            List<string> pathsToDelete = gridViewDS.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(row => (string)row.Cells["ImageFilePath"].Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pathsToDelete.Count == 0)
                return;

            List<DataItem> list = gridViewDS.DataSource as List<DataItem>;
            if (list == null)
                return;

            int scroll = gridViewDS.FirstDisplayedScrollingRowIndex;
            int select = gridViewDS.SelectedRows.Cast<DataGridViewRow>().Min(row => row.Index);
            HashSet<string> deletedPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            selectionMode = true;
            gridViewDS.SuspendLayout();
            gridViewTags.DataSource = null;
            try
            {
                List<string> deletedPaths = new List<string>();
                List<string> failedPaths = new List<string>();
                foreach (string file in pathsToDelete)
                {
                    // Real sidecar per settings (txt/caption), not a hardcoded .txt.
                    string tagFile = ImageEditorSaveService.FindExistingCaptionPath(file, Program.Settings.GetTagFilesExtensions());
                    // Staged two-phase delete: image and tag file are removed
                    // together or not at all, so a locked/read-only tag file can
                    // no longer leave the image permanently deleted while the
                    // item is reported as "failed" and kept in the dataset.
                    if (ImageFileDeleter.DeleteImageWithTags(file, tagFile, out string deleteError))
                    {
                        deletedPaths.Add(file);
                        deletedPathSet.Add(file);
                    }
                    else
                    {
                        Trace.WriteLine($"Failed to delete '{file}': {deleteError}");
                        failedPaths.Add(file);
                    }
                }

                if (deletedPaths.Count > 0)
                    Program.DataManager.RemoveMany(deletedPaths);

                if (failedPaths.Count > 0)
                {
                    MessageBox.Show(
                        this,
                        string.Format(I18n.GetText("TipDeleteFilesFailed"), failedPaths.Count)
                            + Environment.NewLine
                            + string.Join(Environment.NewLine, failedPaths.Take(10)),
                        I18n.GetText("LabelDeleteFile"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (deletedPathSet.Contains(list[i].ImageFilePath))
                        list.RemoveAt(i);
                }

                // The grid is bound to a plain List<DataItem>, which does not raise
                // change notifications. Control.Refresh() only repaints and leaves
                // the grid's cached row count out of sync with the shortened list,
                // producing stale rows with null cells (IndexOutOfRange on paint and
                // a null-key crash in LoadSelectedImageToGrid). CurrencyManager.Refresh()
                // forces the binding to re-read the list so the row count matches.
                if (gridViewDS.DataSource != null
                    && gridViewDS.BindingContext[gridViewDS.DataSource] is CurrencyManager cm)
                {
                    cm.Refresh();
                }
                gridViewDS.Refresh();
                if (gridViewDS.RowCount > 0)
                {
                    if (scroll >= 0)
                        gridViewDS.FirstDisplayedScrollingRowIndex = Math.Min(scroll, gridViewDS.RowCount - 1);
                    if (select >= gridViewDS.RowCount)
                        select = gridViewDS.RowCount - 1;
                    gridViewDS.ClearSelection();
                    gridViewDS.Rows[select].Selected = true;
                }
            }
            finally
            {
                gridViewDS.ResumeLayout();
                selectionMode = false;
            }

            // The browser renders from the (in-place mutated) grid list:
            // rebuild it so deleted rows disappear immediately.
            RefreshDatasetFolderList();
            SetDSCountStatus(string.Format(I18n.GetText("LabelShownDsImages"), gridViewDS.RowCount, Program.DataManager.GetActiveScopeCount()));
            if (gridViewDS.SelectedRows.Count > 0)
                LoadSelectedImageToGrid();
        }

        private void gridViewDS_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && gridViewDS.SelectedRows.Count > 0)
            {
                var file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                ShowPreview(file, true);
            }
        }

        private void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count > 0)
            {
                var file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                ExplorerFile(file);
            }
        }

        private void ContextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0)
                return;

            string file = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
            bool isVideo = VideoProcessingService.IsVideoFile(file);
            cropImageToolStripMenuItem.Visible = !isVideo;
            removeBackgroundToolStripMenuItem.Visible = !isVideo;
            if (menuContextDSEditImage != null)
                menuContextDSEditImage.Visible = !isVideo;
            if (menuContextDSRetagOnnx != null)
                menuContextDSRetagOnnx.Visible = !isVideo;
            if (menuContextDSRetagLlm != null)
                menuContextDSRetagLlm.Visible = !isVideo;
            if (menuContextDSVideoTools != null)
                menuContextDSVideoTools.Visible = isVideo;
        }

        private void gridViewDS_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex != -1 && e.RowIndex < gridViewDS.Rows.Count && e.Button == MouseButtons.Right)
            {
                gridViewDS.ClearSelection();
                gridViewDS.Rows[e.RowIndex].Selected = true;
                contextMenuStrip1.Show(MousePosition);
            }
            else if (e.RowIndex == -1 && gridViewDS.Rows.Count > 0 && e.Button == MouseButtons.Right)
            {
                contextMenuImageGridHeader.Items.Clear();
                for (int i = 0; i < gridViewDS.ColumnCount; i++)
                {
                    ToolStripMenuItem tsi = new ToolStripMenuItem();
                    tsi.Name = gridViewDS.Columns[i].Name;
                    tsi.Text = gridViewDS.Columns[i].HeaderText;
                    tsi.Checked = gridViewDS.Columns[i].Visible;
                    contextMenuImageGridHeader.Items.Add(tsi);
                }
                contextMenuImageGridHeader.Show(MousePosition);
            }
        }

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern void ILFree(IntPtr pidlList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll", ExactSpelling = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlList, uint cild, IntPtr children, uint dwFlags);

        public static void ExplorerFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                IntPtr pidlList = ILCreateFromPathW(filePath);
                if (pidlList != IntPtr.Zero)
                {
                    try
                    {
                        SHOpenFolderAndSelectItems(pidlList, 0, IntPtr.Zero, 0);
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Failed to open Explorer for '{filePath}': {ex}");
                    }
                    finally
                    {
                        ILFree(pidlList);
                    }
                }
                return;
            }

            if (Directory.Exists(filePath))
            {
                Process.Start(@"explorer.exe", "/select,\"" + filePath + "\"");
                return;
            }
            var dir = Path.GetDirectoryName(filePath);
            if (Directory.Exists(dir))
            {
                Process.Start(@"explorer.exe", "\"" + dir + "\"");
            }
        }

        private void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            DeleteImage();
        }

        private void gridView_Enter(object sender, EventArgs e)
        {
            if (sender is DataGridView grid)
                grid.BorderStyle = BorderStyle.FixedSingle;
        }

        private void gridView_Leave(object sender, EventArgs e)
        {
            if (sender is DataGridView grid)
                grid.BorderStyle = BorderStyle.Fixed3D;
        }

        private Color allTagsSearchDefaultBackColor = SystemColors.Window;

        /// <summary>
        /// The All Tags search box used to be hidden until a key was pressed on
        /// the grid; it is now always visible above the list. Typing locates the
        /// first match (prefix first, then substring), Enter jumps to the next
        /// match, Escape/the clear button reset the search.
        /// </summary>
        private void InitializeAllTagsSearch()
        {
            toolStripTextBox1.Visible = true;
            toolStripButton1.Visible = true;
            allTagsSearchDefaultBackColor = toolStripTextBox1.BackColor;
            toolStripTextBox1.TextChanged += TextBox1_TextChanged;
        }

        private void TextBox1_TextChanged(object sender, EventArgs e)
        {
            LocateAllTagsSearchMatch(0);
        }

        private void LocateAllTagsSearchMatch(int startIndex)
        {
            string query = toolStripTextBox1.Text.Trim();
            if (query.Length == 0 || Program.DataManager == null)
            {
                toolStripTextBox1.BackColor = allTagsSearchDefaultBackColor;
                return;
            }
            // Chinese input also matches through the CSV dictionary (synonyms
            // included) and the translation column, not only the tag text.
            var aliasTags = Program.ChineseTagLookup.FindEnglishTagsByChineseName(query, Program.Settings.Language);
            int index = Program.DataManager.AllTags.FindTagBestMatch(query, startIndex, aliasTags);
            if (index < 0 || index >= gridViewAllTags.RowCount)
            {
                // Keep the text and tint the box instead of silently eating keys.
                toolStripTextBox1.BackColor = Color.MistyRose;
                // Color alone is invisible to color-blind/screen-reader users.
                SetStatus(string.Format(I18n.GetText("SearchNoMatch"), query));
                return;
            }
            toolStripTextBox1.BackColor = allTagsSearchDefaultBackColor;
            gridViewAllTags.CurrentCell = gridViewAllTags.Rows[index].Cells[0];
            SetAllTagsSelectionAnchor(gridViewAllTags[0, index].Value as string);
            if (index < gridViewAllTags.FirstDisplayedScrollingRowIndex
                || index > gridViewAllTags.FirstDisplayedScrollingRowIndex + gridViewAllTags.DisplayedRowCount(false))
            {
                gridViewAllTags.FirstDisplayedScrollingRowIndex = index;
            }
        }

        private void ResetAllTagsSearch()
        {
            toolStripTextBox1.Clear();
            toolStripTextBox1.BackColor = allTagsSearchDefaultBackColor;
            gridViewAllTags.Focus();
        }

        private void gridViewAllTags_KeyPress(object sender, KeyPressEventArgs e)
        {
            // Typing while the grid has focus redirects into the search box.
            if (char.IsControl(e.KeyChar))
                return;
            toolStripTextBox1.Focus();
            toolStripTextBox1.Text = e.KeyChar.ToString();
            toolStripTextBox1.SelectionStart = toolStripTextBox1.TextLength;
            e.Handled = true;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ResetAllTagsSearch();
        }

        private void gridViewAllTags_SelectionChanged(object sender, EventArgs e)
        {
            // Selecting rows no longer hides or clears the search box.
            // Update the by-tag anchor only for user-driven changes: list resets
            // also move the selection (by index) before the restore handler runs,
            // and those must not overwrite the anchor.
            if (restoringAllTagsSelection || !gridViewAllTags.Focused)
                return;
            CaptureAllTagsSelectionAnchor();
        }

        private AllTagsList anchoredAllTags;
        private readonly List<string> allTagsSelectionAnchor = new List<string>();
        private bool restoringAllTagsSelection;

        /// <summary>
        /// Count-sorted lists re-sort when tag counts change; the grid keeps the
        /// selection by row index, silently moving it onto a different tag.
        /// Anchor the selection by tag text and restore it after every reset.
        /// </summary>
        private void HookAllTagsSelectionAnchor()
        {
            if (anchoredAllTags != null)
                anchoredAllTags.ListChanged -= AllTags_ListChangedRestoreSelection;
            allTagsSelectionAnchor.Clear();
            anchoredAllTags = Program.DataManager?.AllTags;
            if (anchoredAllTags != null)
                anchoredAllTags.ListChanged += AllTags_ListChangedRestoreSelection;
        }

        private void AllTags_ListChangedRestoreSelection(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType != ListChangedType.Reset || allTagsSelectionAnchor.Count == 0)
                return;
            if (!IsHandleCreated || IsDisposed)
                return;
            // The grid processes the reset inside the BindingSource's handler
            // (subscribed before this one); restore only after it has finished.
            BeginInvoke(new Action(RestoreAllTagsSelectionFromAnchor));
        }

        private void RestoreAllTagsSelectionFromAnchor()
        {
            if (IsDisposed || Program.DataManager == null || allTagsSelectionAnchor.Count == 0)
                return;
            var indexes = new List<int>();
            foreach (string tag in allTagsSelectionAnchor)
            {
                int index = Program.DataManager.AllTags.IndexOfList(tag);
                if (index >= 0 && index < gridViewAllTags.RowCount && !indexes.Contains(index))
                    indexes.Add(index);
            }
            if (indexes.Count == 0)
                return;
            restoringAllTagsSelection = true;
            try
            {
                gridViewAllTags.CurrentCell = gridViewAllTags.Rows[indexes[0]].Cells[0];
                gridViewAllTags.ClearSelection();
                foreach (int index in indexes)
                    gridViewAllTags.Rows[index].Selected = true;
                if (indexes[0] < gridViewAllTags.FirstDisplayedScrollingRowIndex
                    || indexes[0] > gridViewAllTags.FirstDisplayedScrollingRowIndex + gridViewAllTags.DisplayedRowCount(false))
                {
                    gridViewAllTags.FirstDisplayedScrollingRowIndex = indexes[0];
                }
            }
            catch (InvalidOperationException)
            {
                // Best effort: the grid can reject CurrentCell mid-layout.
            }
            catch (ArgumentOutOfRangeException)
            {
            }
            finally
            {
                restoringAllTagsSelection = false;
            }
        }

        private void CaptureAllTagsSelectionAnchor()
        {
            // Keep the last non-empty anchor: grids clear the selection while
            // processing a reset, and that must not erase the anchor either.
            if (gridViewAllTags.SelectedCells.Count == 0)
                return;
            allTagsSelectionAnchor.Clear();
            string current = null;
            if (gridViewAllTags.CurrentCell != null
                && TryGetAllTagsRowTag(gridViewAllTags.CurrentCell.RowIndex, out string currentTag))
            {
                current = currentTag;
            }
            var seenRows = new HashSet<int>();
            foreach (DataGridViewCell cell in gridViewAllTags.SelectedCells)
            {
                if (!seenRows.Add(cell.RowIndex))
                    continue;
                if (TryGetAllTagsRowTag(cell.RowIndex, out string tag) && tag != current)
                    allTagsSelectionAnchor.Add(tag);
            }
            if (current != null)
                allTagsSelectionAnchor.Insert(0, current);
        }

        private void SetAllTagsSelectionAnchor(string tag)
        {
            allTagsSelectionAnchor.Clear();
            if (!string.IsNullOrEmpty(tag))
                allTagsSelectionAnchor.Add(tag);
        }

        private bool TryGetAllTagsRowTag(int rowIndex, out string tag)
        {
            tag = null;
            if (rowIndex < 0 || rowIndex >= gridViewAllTags.RowCount)
                return false;
            tag = gridViewAllTags[0, rowIndex].Value as string;
            return !string.IsNullOrEmpty(tag);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                int start = gridViewAllTags.CurrentCell == null ? 0 : gridViewAllTags.CurrentCell.RowIndex + 1;
                LocateAllTagsSearchMatch(start);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ResetAllTagsSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                int offset = e.KeyCode == Keys.Down ? 1 : -1;
                int index = (gridViewAllTags.CurrentCell == null ? 0 : gridViewAllTags.CurrentCell.RowIndex) + offset;
                if (index >= 0 && index < gridViewAllTags.RowCount)
                    gridViewAllTags.CurrentCell = gridViewAllTags.Rows[index].Cells[0];
                e.Handled = true;
            }
        }

        private ToolStripTextBox toolStripImageTagsSearchBox;
        private ToolStripButton toolStripImageTagsSearchClear;
        private Color imageTagsSearchDefaultBackColor = SystemColors.Window;

        /// <summary>
        /// Search box on the Image Tags toolbar, mirroring the All Tags search:
        /// typing locates the first match (tag prefix > tag substring >
        /// translation substring > Chinese CSV dictionary hit), Enter jumps to
        /// the next match, Escape/the clear button reset the search.
        /// </summary>
        private void InitializeImageTagsSearch()
        {
            toolStripImageTagsSearchBox = new ToolStripTextBox
            {
                Name = "toolStripImageTagsSearchBox",
                AutoSize = false,
                // Runtime-added controls skip WinForms auto-scaling.
                Width = LogicalToDeviceUnits(140)
            };
            toolStripImageTagsSearchClear = new ToolStripButton
            {
                Name = "toolStripImageTagsSearchClear",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = Properties.Resources.Delete,
                ImageTransparentColor = Color.Magenta
            };
            int insertIndex = toolStripTagsHeader.Items.IndexOf(toolStripLabelImageTags) + 1;
            toolStripTagsHeader.Items.Insert(insertIndex, toolStripImageTagsSearchClear);
            toolStripTagsHeader.Items.Insert(insertIndex, toolStripImageTagsSearchBox);
            imageTagsSearchDefaultBackColor = toolStripImageTagsSearchBox.BackColor;
            toolStripImageTagsSearchBox.TextChanged += (_, _) => LocateImageTagsSearchMatch(0);
            toolStripImageTagsSearchBox.KeyDown += ImageTagsSearchBox_KeyDown;
            toolStripImageTagsSearchClear.Click += (_, _) => ResetImageTagsSearch();
        }

        private void ImageTagsSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                int start = gridViewTags.CurrentCell == null ? 0 : gridViewTags.CurrentCell.RowIndex + 1;
                LocateImageTagsSearchMatch(start);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ResetImageTagsSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void LocateImageTagsSearchMatch(int startIndex)
        {
            if (toolStripImageTagsSearchBox == null)
                return;
            string query = toolStripImageTagsSearchBox.Text.Trim();
            if (query.Length == 0)
            {
                toolStripImageTagsSearchBox.BackColor = imageTagsSearchDefaultBackColor;
                return;
            }
            int index = FindImageTagRowBestMatch(query, startIndex);
            if (index < 0 || index >= gridViewTags.RowCount)
            {
                // Keep the text and tint the box instead of silently eating keys.
                toolStripImageTagsSearchBox.BackColor = Color.MistyRose;
                // Color alone is invisible to color-blind/screen-reader users.
                SetStatus(string.Format(I18n.GetText("SearchNoMatch"), query));
                return;
            }
            toolStripImageTagsSearchBox.BackColor = imageTagsSearchDefaultBackColor;
            gridViewTags.CurrentCell = gridViewTags.Rows[index].Cells["ImageTags"];
            if (index < gridViewTags.FirstDisplayedScrollingRowIndex
                || index > gridViewTags.FirstDisplayedScrollingRowIndex + gridViewTags.DisplayedRowCount(false))
            {
                gridViewTags.FirstDisplayedScrollingRowIndex = index;
            }
        }

        /// <summary>
        /// Same match priority as the All Tags search. In multi-select view the
        /// tag text lives only on the first row of each group, so continuation
        /// rows fall back to the group tag text.
        /// </summary>
        private int FindImageTagRowBestMatch(string query, int startIndex)
        {
            int count = gridViewTags.RowCount;
            if (count == 0)
                return -1;
            var aliasTags = Program.ChineseTagLookup.FindEnglishTagsByChineseName(query, Program.Settings.Language);
            startIndex = ((startIndex % count) + count) % count;
            int containsMatch = -1;
            int translationMatch = -1;
            int aliasMatch = -1;
            for (int offset = 0; offset < count; offset++)
            {
                int i = (startIndex + offset) % count;
                string tag = GetImageTagRowText(i);
                if (string.IsNullOrEmpty(tag))
                    continue;
                if (tag.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    return i;
                if (containsMatch == -1 && tag.Contains(query, StringComparison.OrdinalIgnoreCase))
                    containsMatch = i;
                if (translationMatch == -1
                    && gridViewTags.Columns.Contains("Translation")
                    && gridViewTags["Translation", i].Value is string translation
                    && translation.Contains(query, StringComparison.OrdinalIgnoreCase))
                    translationMatch = i;
                if (aliasMatch == -1 && aliasTags.Contains(tag))
                    aliasMatch = i;
            }
            if (containsMatch != -1)
                return containsMatch;
            if (translationMatch != -1)
                return translationMatch;
            return aliasMatch;
        }

        private string GetImageTagRowText(int rowIndex)
        {
            string tag = gridViewTags["ImageTags", rowIndex].Value as string;
            if (!string.IsNullOrEmpty(tag))
                return tag;
            if (gridViewTags.DataSource is MultiSelectDataTable dt
                && rowIndex < dt.Rows.Count
                && dt.Rows[rowIndex] is MultiSelectDataRow row
                && row.RowState != System.Data.DataRowState.Deleted
                && row.RowState != System.Data.DataRowState.Detached)
            {
                return row.GetTagText();
            }
            return tag;
        }

        private void ResetImageTagsSearch()
        {
            toolStripImageTagsSearchBox.Clear();
            toolStripImageTagsSearchBox.BackColor = imageTagsSearchDefaultBackColor;
            gridViewTags.Focus();
        }

        private void toolStripButton24_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (allTagsFilter == null || allTagsFilter.IsDisposed)
            {
                allTagsFilter = new Form_filter();
            }
            if (allTagsFilter.ShowDialog() != DialogResult.OK)
                return;
            if (isAllTags)
            {
                ((BindingSource)gridViewAllTags.DataSource).Filter = allTagsFilter.textBox1.Text;
            }
        }

        private void toolStripButton25_Click(object sender, EventArgs e)
        {
            ((BindingSource)gridViewAllTags.DataSource).RemoveFilter();
        }

        private void promptSortBtn_Click(object sender, EventArgs e)
        {
            SortPrompt();
        }

        private void SortPrompt()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            var fixedLengthIndex = toolStrippromptFixedLengthComboBox.SelectedIndex;
            if (fixedLengthIndex == -1)
                return;
            if (GetTagsDataSourceType() == DataSourceType.Single)
            {
                EditableTagList eTagList = (EditableTagList)gridViewTags.DataSource;
                eTagList.Sort(fixedLengthIndex);
            }
        }

        // ---- semantic tag categories: light row tints + category sort ----

        private ToolStripButton toolStripCategorySortBtn;
        private readonly Dictionary<string, TagSemanticCategory> tagCategoryCache =
            new Dictionary<string, TagSemanticCategory>(StringComparer.OrdinalIgnoreCase);

        private void InitializeTagCategoryUi()
        {
            toolStripCategorySortBtn = new ToolStripButton
            {
                Name = "toolStripCategorySortBtn",
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            toolStripCategorySortBtn.Click += (_, _) => SortPromptByCategory();
            int sortIndex = toolStripTagsHeader.Items.IndexOf(toolStripPromptSortBtn);
            toolStripTagsHeader.Items.Insert(
                sortIndex >= 0 ? sortIndex + 1 : toolStripTagsHeader.Items.Count,
                toolStripCategorySortBtn);
            gridViewAllTags.CellFormatting += GridViewAllTags_CellFormatting;

            // All-tags pane: category-grouped ordering toggle (off by default).
            toolStripAllTagsCategorySortBtn = new ToolStripButton
            {
                Name = "toolStripAllTagsCategorySortBtn",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                CheckOnClick = true,
                Checked = Program.Settings.AllTagsCategorySort
            };
            toolStripAllTagsCategorySortBtn.CheckedChanged += (_, _) =>
            {
                Program.Settings.AllTagsCategorySort = toolStripAllTagsCategorySortBtn.Checked;
                Program.Settings.SaveSettings();
                ApplyAllTagsCategorySort();
            };
            toolStrip1.Items.Add(toolStripAllTagsCategorySortBtn);
        }

        private ToolStripButton toolStripAllTagsCategorySortBtn;

        private void ApplyAllTagsCategorySort()
        {
            Program.DataManager?.AllTags.SetCategorySort(
                Program.Settings.AllTagsCategorySort ? tag => (int)ClassifyTagCached(tag) : null);
        }

        /// <summary>
        /// Sorts the current image's tags by semantic category (characters →
        /// subject count → hair/eyes/body → clothing/accessories → ... →
        /// meta), honoring the "don't sort first N rows" prefix.
        /// </summary>
        private void SortPromptByCategory()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            int fixedLengthIndex = toolStrippromptFixedLengthComboBox.SelectedIndex;
            if (fixedLengthIndex == -1)
                return;
            if (GetTagsDataSourceType() != DataSourceType.Single)
                return;
            if (gridViewTags.DataSource is EditableTagList eTagList)
                eTagList.SortByCategory(fixedLengthIndex, tag => (int)ClassifyTagCached(tag));
        }

        private TagSemanticCategory ClassifyTagCached(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return TagSemanticCategory.General;
            if (tagCategoryCache.TryGetValue(tag, out TagSemanticCategory cached))
                return cached;
            int danbooruType = Program.TagsList?.GetTagType(tag) ?? -1;
            TagSemanticCategory category;
            if (danbooruType < 0 && Program.CharacterTagLookup?.Contains(tag) == true)
                category = TagSemanticCategory.Character;
            else
                category = TagSemanticClassifier.Classify(tag, danbooruType);
            tagCategoryCache[tag] = category;
            return category;
        }

        /// <summary>
        /// Applies the 设置→匹配角色标签 toggle: loads/unloads the character
        /// catalog and drops the classification cache so tints re-evaluate.
        /// </summary>
        private void ApplyCharacterMatchSetting()
        {
            bool enabled = Program.Settings.MatchCharacterTags;
            if (enabled && Program.CharacterTagLookup == null)
            {
                Cursor = Cursors.WaitCursor;
                try
                {
                    Program.CharacterTagLookup =
                        CharacterTagCatalog.LoadFromFile(Program.GetCharacterTagCatalogPath());
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
            else if (!enabled && Program.CharacterTagLookup != null)
            {
                Program.CharacterTagLookup = null;
            }
            tagCategoryCache.Clear();
            gridViewTags.Invalidate();
            gridViewAllTags.Invalidate();
        }

        private void ApplyTagCategoryTint(DataGridViewCellFormattingEventArgs e, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;
            Color? accent = TagSemanticClassifier.GetAccent(ClassifyTagCached(tag));
            if (accent.HasValue)
                e.CellStyle.BackColor = TagSemanticClassifier.ApplyTint(accent.Value, e.CellStyle.BackColor);
        }

        /// <summary>Tag text of a row in the image-tags grid for both bind modes.</summary>
        private string GetTagsRowTag(int rowIndex)
        {
            if (gridViewTags.DataSource is EditableTagList && gridViewTags.Columns.Contains("ImageTags"))
                return gridViewTags.Rows[rowIndex].Cells["ImageTags"].Value as string;
            if (gridViewTags.DataSource is MultiSelectDataTable table && rowIndex < table.Rows.Count)
            {
                var row = table.Rows[rowIndex] as MultiSelectDataRow;
                if (row == null
                    || row.RowState == System.Data.DataRowState.Deleted
                    || row.RowState == System.Data.DataRowState.Detached)
                {
                    return null;
                }
                return row.GetTagText();
            }
            return null;
        }

        private void GridViewAllTags_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // The all-tags grid uses designer columns; the tag lives in
            // "TagsColumn" (index 0), not an auto-generated "Tag" column.
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !gridViewAllTags.Columns.Contains("TagsColumn"))
                return;
            ApplyTagCategoryTint(e, gridViewAllTags.Rows[e.RowIndex].Cells["TagsColumn"].Value as string);
        }

        public void switchLanguage()
        {
            I18n.Initialize(Program.Settings.Language);
            fileToolStripMenuItem.Text = I18n.GetText("MenuLabelFile");
            viewToolStripMenuItem.Text = I18n.GetText("MenuLabelView");
            toolStripLabelDataSet.Text = I18n.GetText("UILabelDataSet");
            toolStripLabelAllTags.Text = I18n.GetText("UILabelAllTags");
            toolStripLabelImageTags.Text = I18n.GetText("UILabelImageTags");
            toolStripPromptFixTipLabel.Text = I18n.GetText("UILabelFixPromptLength");
            openFolderToolStripMenuItem.Text = I18n.GetText("MenuItemLoadFolder");
            loadFolderWithAdditionalSettingsToolStripMenuItem.Text = I18n.GetText("MenuItemLoadFolderWithSettings");
            saveAllChangesToolStripMenuItem.Text = I18n.GetText("MenuItemSaveChanges");
            MenuShowPreview.Text = I18n.GetText("MenuItemShowPreview");
            MenuItemTranslateTags.Text = I18n.GetText("MenuItemTranslateTags");
            toolStripTextBox1.TextBox.PlaceholderText = I18n.GetText("AllTagsSearchPlaceholder");
            toolStripTextBox1.ToolTipText = I18n.GetText("AllTagsSearchPlaceholder");
            toolStripButton1.ToolTipText = I18n.GetText("AllTagsSearchClear");
            if (toolStripImageTagsSearchBox != null)
            {
                toolStripImageTagsSearchBox.TextBox.PlaceholderText = I18n.GetText("AllTagsSearchPlaceholder");
                toolStripImageTagsSearchBox.ToolTipText = I18n.GetText("AllTagsSearchPlaceholder");
                toolStripImageTagsSearchClear.ToolTipText = I18n.GetText("AllTagsSearchClear");
            }
            MenuHideAllTags.Text = I18n.GetText("MenuHideAllTags");
            MenuHideTags.Text = I18n.GetText("MenuHideTags");
            MenuHideDataset.Text = I18n.GetText("MenuHideDataset");
            datasetBrowserView?.SetTexts(
                I18n.GetText("FolderListAll"),
                I18n.GetText("FolderListRoot"),
                I18n.GetText("FolderListImageCount"),
                I18n.GetText("FolderListSearchPlaceholder"));
            datasetBrowserView?.SetFolderButtonTexts(
                I18n.GetText("FolderExpandAll"),
                I18n.GetText("FolderCollapseAll"));
            if (toolStripCategorySortBtn != null)
            {
                toolStripCategorySortBtn.Text = I18n.GetText("TagsSortByCategory");
                toolStripCategorySortBtn.ToolTipText = I18n.GetText("TagsSortByCategory");
            }
            if (toolStripAllTagsCategorySortBtn != null)
            {
                toolStripAllTagsCategorySortBtn.Text = I18n.GetText("TagsSortByCategory");
                toolStripAllTagsCategorySortBtn.ToolTipText = I18n.GetText("TagsSortByCategory");
            }
            if (menuRenameFolder != null)
                menuRenameFolder.Text = I18n.GetText("FolderRenameMenu");
            if (menuBatchRenameImages != null)
                menuBatchRenameImages.Text = I18n.GetText("FolderBatchRenameMenu");
            if (menuFolderTagOnnx != null)
                menuFolderTagOnnx.Text = I18n.GetText("FolderTagOnnxMenu");
            if (menuFolderTagLlm != null)
                menuFolderTagLlm.Text = I18n.GetText("FolderTagLlmMenu");
            datasetPreviewPanel?.SetTitle(I18n.GetText("DatasetPreviewTitle"));
            MenuLanguage.Text = I18n.GetText("MenuMenuLanguage");
            MenuSetting.Text = I18n.GetText("MenuLabelOptions");
            settingsToolStripMenuItem.Text = I18n.GetText("MenuSettings");
            autoTaggerSettingsToolStripMenuItem.Text = I18n.GetText("MenuAutoTaggerSettings");
            toolsToolStripMenuItem.Text = I18n.GetText("MenuTools");
            if (menuAiServerSet != null)
                menuAiServerSet.Text = I18n.GetText("MenuAiServerSet");
            if (menuLlmTagger != null)
                menuLlmTagger.Text = I18n.GetText("MenuLlmTagger");
            if (menuVideoConvert != null)
                menuVideoConvert.Text = I18n.GetText("MenuVideoConvert");
            if (menuVideoExtract != null)
                menuVideoExtract.Text = I18n.GetText("MenuVideoExtract");
            if (menuOnnxTagger != null)
                menuOnnxTagger.Text = I18n.GetText("MenuOnnxTagger");
            if (menuTestModule != null)
                menuTestModule.Text = I18n.GetText("MenuTestModule");
            debugToolStripMenuItem.Text = I18n.GetText("MenuDebug");
            debugOpenLogToolStripMenuItem.Text = I18n.GetText("MenuDebugOpenLog");
            replaceTransparentBackgroundToolStripMenuItem.Text = I18n.GetText("MenuReplaceTranspColor");
            generateTagsWithAutoTaggerForAllImagesToolStripMenuItem.Text = I18n.GetText("MenuGenTagsForAllImages");
            MenuOpenAiGenTagsForAllImages.Text = I18n.GetText("MenuOpenAiGenTagsForAllImages");
            backgroundRemovalWithRMBG20ToolStripMenuItem.Text = I18n.GetText("MenuToolsBGRemoval");
            removeBackgroundToolStripMenuItem.Text = I18n.GetText("MenuContextDSRemoveBG");
            toolStripMenuItem1.Text = I18n.GetText("MenuContextDSOpenFolder");
            toolStripMenuItem2.Text = I18n.GetText("MenuContextDSDeleteImage");
            cropImageToolStripMenuItem.Text = I18n.GetText("MenuContextDSCropImage");
            if (menuContextDSEditImage != null)
                menuContextDSEditImage.Text = I18n.GetText("MenuContextDSEditImage");
            if (menuContextDSVideoTools != null)
                menuContextDSVideoTools.Text = I18n.GetText("MenuContextDSVideoTools");
            if (menuContextDSRetagOnnx != null)
                menuContextDSRetagOnnx.Text = I18n.GetText("MenuContextDSRetagOnnx");
            if (menuContextDSRetagLlm != null)
                menuContextDSRetagLlm.Text = I18n.GetText("MenuContextDSRetagLlm");
            UpdateTagContextMenuText();

            BtnTagAddToAll.Text = I18n.GetText("BtnTagAddToAll");
            BtnTagAdd.Text = I18n.GetText("BtnTagAdd");
            BtnTagUndo.Text = I18n.GetText("BtnTagUndo");
            BtnTagRedo.Text = I18n.GetText("BtnTagRedo");
            BtnTagDelete.Text = I18n.GetText("BtnTagDelete");
            BtnTagCopy.Text = I18n.GetText("BtnTagCopy");
            BtnTagPaste.Text = I18n.GetText("BtnTagPaste");
            BtnTagSetToAll.Text = I18n.GetText("BtnTagSetToAll");
            BtnTagPasteFromClipBoard.Text = I18n.GetText("BtnTagPasteFromClipBoard");
            BtnTagShow.Text = I18n.GetText("BtnTagShow");
            BtnTagUp.Text = I18n.GetText("BtnTagUp");
            BtnTagDown.Text = I18n.GetText("BtnTagDown");
            BtnTagFindInAll.Text = I18n.GetText("BtnTagFindInAll");
            toolStripSplitButton1.Text = I18n.GetText("BtnAutoGenerateTagsRoot");
            btnAutoGetTagsDefSet.Text = I18n.GetText("BtnAutoGetTagsDefSet");
            btnOpenAiAutoGetTagsDefSet.Text = I18n.GetText("BtnOpenAiAutoGetTagsDefSet");
            btnAutoGetTagsOpenSet.Text = I18n.GetText("BtnAutoGetTagsOpenSet");
            btnOpenAiAutoGetTagsOpenSet.Text = I18n.GetText("BtnOpenAiAutoGetTagsOpenSet");
            btnAutoAddSelToImageTags.Text = I18n.GetText("BtnAutoAddSelToImageTags");
            BtnMenuSorting.Text = I18n.GetText("BtnMenuSorting");
            BtnMenuSortNameAsc.Text = I18n.GetText("BtnMenuSortNameAsc");
            BtnMenuSortCountAsc.Text = I18n.GetText("BtnMenuSortCountAsc");
            BtnMenuSortCountDesc.Text = I18n.GetText("BtnMenuSortCountDesc");
            BtnTagImageChecker.Text = I18n.GetText("BtnTagImageChecker");

            BtnTagSwitch.Text = I18n.GetText("BtnTagSwitch");
            BtnTagAddToAll.Text = I18n.GetText("BtnTagAddToAll");
            BtnTagDeleteForAll.Text = I18n.GetText("BtnTagDeleteForAll");
            BtnTagReplace.Text = I18n.GetText("BtnTagReplace");
            BtnTagAddToSelected.Text = I18n.GetText("BtnTagAddToSelected");
            BtnTagDeleteForSelected.Text = I18n.GetText("BtnTagDeleteForSelected");
            BtnTagAddToFiltered.Text = I18n.GetText("BtnTagAddToFiltered");
            BtnTagDeleteForFiltered.Text = I18n.GetText("BtnTagDeleteForFiltered");
            BtnTagMultiModeSwitch.Text = I18n.GetText("BtnTagMultiModeSwitch");
            BtnImageFilter.Text = I18n.GetText("BtnImageFilter");
            BtnImageExitFilter.Text = I18n.GetText("BtnImageExitFilter");
            BtnTagFilter.Text = I18n.GetText("BtnTagFilter");
            BtnTagExitFilter.Text = I18n.GetText("BtnTagExitFilter");
            MenuShowTagCount.Text = I18n.GetText("MenuShowCount");
            BtnMenuGenTagsWithCurrentSettings.Text = I18n.GetText("BtnMenuGenTagsWithCurrentSettings");
            BtnMenuOpenAiGenTagsWithCurrentSettings.Text = I18n.GetText("BtnMenuOpenAiGenTagsWithCurrentSettings");
            BtnMenuGenTagsWithSetWindow.Text = I18n.GetText("BtnMenuGenTagsWithSetWindow");
            BtnMenuOpenAiGenTagsWithSetWindow.Text = I18n.GetText("BtnMenuOpenAiGenTagsWithSetWindow");
            toolStripPromptSortBtn.Text = I18n.GetText("toolStripPromptSortBtn");
            toolStripLabelWeight.Text = I18n.GetText("UILabelWeight");
            tabAllTags.Text = I18n.GetText("UITabAllTags");
            tabAutoTags.Text = I18n.GetText("UITabAutoTags");
            BtnDSChangeSelection.Text = I18n.GetText("BtnDSChangeSelection");
            ApplyDataSetColumnHeaders();


            foreach (ToolStripMenuItem item in MenuLanguage.DropDownItems)
            {
                if (item.Name == "btn_" + Program.Settings.Language)
                {
                    item.Checked = true;
                }
                else
                {
                    item.Checked = false;
                }
            }
        }

        private void LanguageXXBtn_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            if (item.Checked)
                return;
            string lang = item.Name.Substring(4);
            Program.Settings.Language = lang;
            Program.Settings.SaveSettings();
            switchLanguage();
        }

        private void MenuShowTagCount_Click(object sender, EventArgs e)
        {
            showCount = !showCount;
            MenuShowTagCount.Checked = showCount;
            if (showCount)
            {
                gridViewAllTags.Columns["CountColumn"].Visible = true;
            }
            else
            {
                gridViewAllTags.Columns["CountColumn"].Visible = false;
            }
        }

        private async void BtnTagRedo_Click(object sender, EventArgs e)
        {
            await ApplyTagHistoryActionAsync(tags => tags.NextState());
        }

        private async Task ApplyTagHistoryActionAsync(Action<EditableTagList> historyAction)
        {
            if (GetTagsDataSourceType() != DataSourceType.Single)
            {
                MessageBox.Show(I18n.GetText("TipStateMultiselectNotSupported"));
                return;
            }

            if (!(gridViewTags.DataSource is EditableTagList editableTags))
                return;

            int preferredRow = gridViewTags.CurrentCell?.RowIndex ?? -1;
            historyAction(editableTags);
            RefreshTagGridAfterHistory(preferredRow);

            if (isTranslate)
            {
                await FillTranslation(gridViewTags);
                RefreshTagGridAfterHistory(preferredRow);
            }
        }

        private void RefreshTagGridAfterHistory(int preferredRow)
        {
            if (gridViewTags.DataSource != null)
            {
                if (BindingContext[gridViewTags.DataSource] is CurrencyManager currencyManager)
                    currencyManager.Refresh();
            }

            gridViewTags.Refresh();

            if (gridViewTags.RowCount == 0 || gridViewTags.ColumnCount == 0)
                return;

            int rowIndex = preferredRow < 0
                ? 0
                : Math.Min(preferredRow, gridViewTags.RowCount - 1);
            string columnName = gridViewTags.Columns.Contains("ImageTags")
                ? "ImageTags"
                : gridViewTags.Columns[0].Name;

            gridViewTags.ClearSelection();
            gridViewTags.CurrentCell = gridViewTags[columnName, rowIndex];
            gridViewTags[columnName, rowIndex].Selected = true;
        }

        private void gridViewTags_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != -1 && e.RowIndex != -1)
            {
                if (gridViewTags[e.ColumnIndex, e.RowIndex].Value == DBNull.Value)
                    gridViewTags[e.ColumnIndex, e.RowIndex].Value = string.Empty;
            }
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Only park the floating window; clearing the embedded sidebar
            // preview here made it go blank behind the settings dialog.
            fPreview?.Hide();
            Form_settings settings = new Form_settings();
            if (settings.ShowDialog() == DialogResult.OK)
            {
                SetStatus(I18n.GetText("TipSettingsSaved"));
            }
            settings.Close();
            switchLanguage();
            ApplyCharacterMatchSetting();
            bool debugWasEnabled = DebugLog.Enabled;
            DebugLog.Enabled = Program.Settings.DebugMode;
            debugToolStripMenuItem.Visible = Program.Settings.DebugMode;
            if (!debugWasEnabled && DebugLog.Enabled)
                DebugLog.Write("App", "Debug mode enabled in settings");
            // The preview mode may have changed in the dialog: re-run the
            // sidebar layout, then refresh whichever preview surface is active.
            ApplyDatasetSidebarLayout();
            RefreshEmbeddedPreviewFromSelection();
        }

        private async void replaceTransparentBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0)
                return;
            using Form_backgroundReplace backgroundReplace = new Form_backgroundReplace();
            if (backgroundReplace.ShowDialog() != DialogResult.OK)
                return;
            LockEdit(true);
            try
            {
                SetStatus(I18n.GetText("InProgress"));
                bool randomColor = backgroundReplace.radioButton2.Checked;
                bool randomSelectedColor = backgroundReplace.radioButton3.Checked;
                Color replColor = backgroundReplace.pictureBox1.BackColor;
                // Snapshot the candidate colors on the UI thread: the worker loop
                // below must not read controls (illegal cross-thread access).
                List<Color> selectedColors = new List<Color>();
                foreach (ListViewItem lvItem in backgroundReplace.listView1.Items)
                    selectedColors.Add(lvItem.BackColor);
                List<DataItem> selectedTagsList = new List<DataItem>();
                Random r = new Random();
                for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                {
                    if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                        selectedTagsList.Add(selectedItem);
                }
                // Old thumbnails are collected and disposed on the UI thread after the
                // background work: the grid may still paint them while the loop runs.
                List<Image> replacedThumbs = new List<Image>();
                List<string> errors = new List<string>();
                await Task.Run(() =>
                {
                    foreach (var item in selectedTagsList)
                    {
                        try
                        {
                            System.Drawing.Imaging.ImageFormat format;
                            string tmpPath = item.ImageFilePath + ".tmp";
                            using (Bitmap bmp = (Bitmap)Bitmap.FromFile(item.ImageFilePath))
                            {
                                format = bmp.RawFormat;
                                if (randomColor)
                                {
                                    replColor = Color.FromArgb(r.Next(255), r.Next(255), r.Next(255));
                                }
                                else if (randomSelectedColor && selectedColors.Count > 0)
                                {
                                    replColor = selectedColors[r.Next(selectedColors.Count)];
                                }
                                using Bitmap bmpRes = Extensions.Transparent2Color(bmp, replColor);
                                bmpRes.Save(tmpPath, format);
                            }
                            // Source handle released above; swap the finished file in.
                            SafeFile.ReplaceOrMove(tmpPath, item.ImageFilePath, null);
                            if (item.Img != null)
                                replacedThumbs.Add(item.Img);
                            item.Img = Extensions.MakeThumb(item.ImageFilePath, Program.Settings.PreviewSize);
                        }
                        catch (Exception ex)
                        {
                            // One broken/locked image must not abort the batch (and,
                            // pre-fix, crash the app via this unprotected async void).
                            errors.Add($"{item.ImageFilePath}: {ex.Message}");
                        }
                    }
                });
                gridViewDS.Refresh();
                foreach (Image oldThumb in replacedThumbs)
                    oldThumb.Dispose();
                if (errors.Count > 0)
                    MessageBox.Show(string.Join("\n", errors));
                SetStatus(I18n.GetText("TipBackgrRepComplete"));
            }
            finally
            {
                LockEdit(false);
            }
        }

        #region HotkeysCode

        private void InitHotkeyCommands()
        {
            if (Program.Settings.Hotkeys.Commands == null)
                Program.Settings.Hotkeys.Commands = new Dictionary<string, Action>();
            var cmds = Program.Settings.Hotkeys.Commands;

            cmds["DatasetFocus"] = delegate () { DatasetFocus(); };
            cmds["TagsFocus"] = delegate () { TagsFocus(); };
            cmds["AllTagsFocus"] = delegate () { AllTagsFocus(); };
            cmds["PreviewTabFocus"] = delegate () { PreviewTabFocus(); };

            cmds["MenuItemSaveChanges"] = delegate () { saveAllChangesToolStripMenuItem.PerformClick(); };
            cmds["MenuItemShowPreview"] = delegate () { MenuShowPreview.PerformClick(); };
            cmds["MenuHideAllTags"] = delegate () { MenuHideAllTags.PerformClick(); };
            cmds["MenuHideTags"] = delegate () { MenuHideTags.PerformClick(); };
            cmds["MenuHideDataset"] = delegate () { MenuHideDataset.PerformClick(); };


            cmds["BtnTagAdd"] = delegate () { BtnTagAdd.PerformClick(); TagsFocus(); };
            cmds["BtnTagDelete"] = delegate () { BtnTagDelete.PerformClick(); };
            cmds["BtnTagUndo"] = delegate () { BtnTagUndo.PerformClick(); };
            cmds["BtnTagRedo"] = delegate () { BtnTagRedo.PerformClick(); };
            cmds["BtnTagUp"] = delegate () { BtnTagUp.PerformClick(); };
            cmds["BtnTagDown"] = delegate () { BtnTagDown.PerformClick(); };
            cmds["BtnTagFindInAll"] = delegate () { BtnTagFindInAll.PerformClick(); };
            cmds["BtnTagAddToAll"] = delegate () { BtnTagAddToAll.PerformClick(); };
            cmds["BtnTagAddToSelected"] = delegate () { BtnTagAddToSelected.PerformClick(); };
            cmds["BtnTagAddToFiltered"] = delegate () { BtnTagAddToFiltered.PerformClick(); };
            cmds["BtnTagDeleteForAll"] = delegate () { BtnTagDeleteForAll.PerformClick(); };
            cmds["BtnTagDeleteForSelected"] = delegate () { BtnTagDeleteForSelected.PerformClick(); };
            cmds["BtnTagDeleteForFiltered"] = delegate () { BtnTagDeleteForFiltered.PerformClick(); };
            cmds["BtnTagReplace"] = delegate () { BtnTagReplace.PerformClick(); };
            cmds["BtnImageFilter"] = delegate () { BtnImageFilter.PerformClick(); };
            cmds["BtnImageExitFilter"] = delegate () { BtnImageExitFilter.PerformClick(); };
            cmds["BtnTagMultiModeSwitch"] = delegate () { BtnTagMultiModeSwitch.PerformClick(); };
            cmds["BtnTagFilter"] = delegate () { BtnTagFilter.PerformClick(); };
            cmds["BtnTagExitFilter"] = delegate () { BtnTagExitFilter.PerformClick(); };
            cmds["BtnMenuGenTagsWithCurrentSettings"] = delegate () { BtnMenuGenTagsWithCurrentSettings.PerformClick(); };
            cmds["BtnMenuGenTagsWithSetWindow"] = delegate () { BtnMenuGenTagsWithSetWindow.PerformClick(); };
            cmds["toolStripPromptSortBtn"] = delegate () { toolStripPromptSortBtn.PerformClick(); };
            cmds["BtnTagImageChecker"] = delegate () { BtnTagImageChecker.PerformClick(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (gridViewTags.IsCurrentCellInEditMode && (keyData == Keys.Enter || keyData == Keys.Tab))
            {
                var rowIndex = gridViewTags.CurrentCell.RowIndex;
                gridViewTags.EndEdit();
                if (GetTagsDataSourceType() == DataSourceType.Single)
                {
                    var eTagList = ((EditableTagList)gridViewTags.DataSource);
                    eTagList.EndEdit(rowIndex);
                }
                else if (GetTagsDataSourceType() == DataSourceType.Multi)
                {
                    var mTagList = ((MultiSelectDataTable)gridViewTags.DataSource);
                    mTagList.EndEdit();
                }
                //((EditableTagList)gridViewTags.DataSource).
            }
            else if (keyData == (Keys.Control | Keys.Z) && CanHandleTagHistoryShortcut())
            {
                BtnTagUndo.PerformClick();
                return true;
            }
            else if (keyData == (Keys.Control | Keys.Y) && CanHandleTagHistoryShortcut())
            {
                BtnTagRedo.PerformClick();
                return true;
            }
            else if (gridViewTags.Focused && !gridViewTags.IsCurrentCellInEditMode && keyData == Keys.Enter)
            {
                gridViewTags.BeginEdit(true);
                return true;
            }
            var hotkey = Program.Settings.Hotkeys.Items.Find(a => a.FullKeyData == keyData);
            if (hotkey != null)
            {
                Program.Settings.Hotkeys.Commands[hotkey.Id]();
                return true;
            }
            else
                return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool CanHandleTagHistoryShortcut()
        {
            if (gridViewTags.IsCurrentCellInEditMode)
                return false;

            if (ActiveControl is TextBoxBase || ActiveControl is ComboBox)
                return false;

            return true;
        }

        private void DatasetFocus()
        {
            datasetBrowserView.FocusList();
        }

        private void TagsFocus()
        {
            gridViewTags.Focus();
        }

        private void AllTagsFocus()
        {
            tabControl1.SelectedIndex = 0;
            gridViewAllTags.Focus();
        }

        private void PreviewTabFocus()
        {
            // Legacy hotkey from the removed right-pane Preview tab: now makes
            // sure the embedded sidebar preview is expanded, then returns to
            // the dataset grid.
            if (Program.Settings.PreviewType == ImagePreviewType.PreviewInMainWindow
                && Program.DataManager != null && !Program.Settings.DatasetPreviewExpanded)
            {
                ToggleEmbeddedPreview();
            }
            datasetBrowserView.FocusList();
        }

        private void HideShowAllTagsWindow()
        {
            if (splitContainer2.Panel1Collapsed)
                HideShowTagsWindow();
            splitContainer2.Panel2Collapsed = !splitContainer2.Panel2Collapsed;
            MenuHideAllTags.Checked = splitContainer2.Panel2Collapsed;
        }

        private void HideShowTagsWindow()
        {
            if (splitContainer2.Panel2Collapsed)
                HideShowAllTagsWindow();
            splitContainer2.Panel1Collapsed = !splitContainer2.Panel1Collapsed;
            MenuHideTags.Checked = splitContainer2.Panel1Collapsed;
        }

        private void HideShowDataset()
        {
            splitContainer1.Panel1Collapsed = !splitContainer1.Panel1Collapsed;
            MenuHideDataset.Checked = splitContainer1.Panel1Collapsed;
        }

        #endregion

        private void autoTaggerSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EnsureOpenAiAutoTagConfigured(true);
        }

        internal void RefreshAfterCharacterTagAudit()
        {
            gridViewDS.Refresh();
            gridViewAllTags.Refresh();
            if (gridViewDS.SelectedRows.Count == 1)
            {
                string path = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
                if (Program.DataManager.DataSet.TryGetValue(path, out DataItem item))
                {
                    gridViewTags.DataSource = item.Tags;
                }
            }
            SetStatus(I18n.GetText("CharacterTagAuditSaved"));
        }

        private async void BtnAutoGetTagsDefSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(true);
        }

        private async Task GetTagsWithAutoTagger(bool defSettings)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (gridViewDS.SelectedRows.Count == 0)
            {
                MessageBox.Show(I18n.GetText("TipSelectImage"));
                return;
            }
            if (!EnsureSelectedAutoTagProviderConfigured(!defSettings))
                return;

            SetStatus(I18n.GetText("InProgress"));
            tabAutoTags.Select();
            LockEdit(true);
            try
            {
                if (!Program.DataManager.DataSet.TryGetValue(
                        (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value, out DataItem selectedImageData))
                    return;

                (List<AutoTagItem> data, string errorMessage, bool canceled) taggerResult = (null, null, false);
                TaggerSettings settings = Program.Settings.OpenAiAutoTagger;
                taggerResult = await GenerateWithSelectedAutoTagProviderAsync(selectedImageData.ImageFilePath);
                if (!string.IsNullOrEmpty(taggerResult.errorMessage))
                    SetStatus(taggerResult.errorMessage);

                if (taggerResult.canceled)
                    return;
                if (taggerResult.data != null)
                {
                    if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
                        gridViewAutoTags.DataSource = taggerResult.data;
                    else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
                    {
                        foreach (var item in selectedImageData.Tags.TextTags)
                        {
                            taggerResult.data.RemoveAll(a => a.Tag == item);
                        }
                        gridViewAutoTags.DataSource = taggerResult.data;
                    }
                    else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList)
                    {
                        if (selectedImageData.Tags.Count == 0)
                            gridViewAutoTags.DataSource = taggerResult.data;
                    }
                    SetStatus(I18n.GetText("TipProgressComplete"));
                }
            }
            finally
            {
                // Not in a finally before: any exception on the await path left the
                // whole UI permanently disabled.
                LockEdit(false);
            }
        }

        private async void generateTagsWithCurrentSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(true, false);
        }

        internal async Task GenerateTagsInTags(bool defSettings, bool allTags)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            if (!EnsureSelectedAutoTagProviderConfigured(!defSettings))
                return;

            TaggerSettings settings = Program.Settings.OpenAiAutoTagger;
            SetStatus(I18n.GetText("InProgress"));
            StringBuilder sbErrors = new StringBuilder();
            LockEdit(true);
            try
            {
            List<DataItem> selectedTagsList = new List<DataItem>();
            if (!allTags)
            {
                for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                {
                    if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                        selectedTagsList.Add(selectedItem);
                }
            }
            else
            {
                foreach (var item in Program.DataManager.DataSet)
                {
                    selectedTagsList.Add(item.Value);
                }
            }
            int currentIndex = 0;
            foreach (var item in selectedTagsList)
            {
                (List<AutoTagItem> data, string errorMessage, bool canceled) taggerResult = (null, null, false);
                taggerResult = await GenerateWithSelectedAutoTagProviderAsync(item.ImageFilePath);
                if (taggerResult.canceled)
                {
                    break;
                }
                if (!defSettings)
                    defSettings = true;
                if (!string.IsNullOrEmpty(taggerResult.errorMessage))
                    SetStatus(taggerResult.errorMessage);
                if (taggerResult.data != null)
                {
                    if (taggerResult.data.Count == 0)
                    {
                        SetStatus(string.Format(I18n.GetText("InProgressCount"), ++currentIndex, selectedTagsList.Count));
                        continue;
                    }
                    if (settings.SetMode == NetworkResultSetMode.AllWithReplacement)
                    {
                        item.Tags.Clear();
                        item.Tags.AddRange(taggerResult.data.Select(a => a.Tag), true);
                    }
                    else if (settings.SetMode == NetworkResultSetMode.OnlyNewWithAddition)
                    {
                        foreach (var aTag in taggerResult.data)
                        {
                            item.Tags.AddTag(aTag.Tag, true, AddingType.Down, 0);
                        }
                    }
                    else if (settings.SetMode == NetworkResultSetMode.SkipExistTagList)
                    {
                        if (item.Tags.Count == 0)
                        {
                            item.Tags.AddRange(taggerResult.data.Select(a => a.Tag), true);
                        }
                    }
                }
                else
                {
                    sbErrors.AppendLine(item.ImageFilePath);
                    //LockEdit(false);
                    //return;
                }
                SetStatus(string.Format(I18n.GetText("InProgressCount"), ++currentIndex, selectedTagsList.Count));
            }
            if (selectedTagsList.Count > 1)
            {
                LoadSelectedImageToGrid();
            }
            //if (gridViewAllTags.DataSource == null)
            //    BindTagList();
            }
            finally
            {
                LockEdit(false);
            }
            if (sbErrors.Length > 0)
            {
                sbErrors.Insert(0, "The following files were not processed:\n");
                MessageBox.Show(sbErrors.ToString());
            }
            SetStatus(I18n.GetText("TipProgressComplete"));
        }

        private async Task<bool> RemoveBackgrounds(bool onlySelected)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return false;
            }
            using (Form_BGRemover bgRemoverForm = new Form_BGRemover(this))
            {
                if (onlySelected || gridViewDS.SelectedRows.Count > 1)
                    bgRemoverForm.radioButtonOnlySelected.Checked = true;
                if (bgRemoverForm.ShowDialog() != DialogResult.OK)
                    return false;
                string selectedModel = bgRemoverForm.GetSelectedModel();
                if (string.IsNullOrEmpty(selectedModel))
                    return false;
                bool allImages = bgRemoverForm.radioButtonAllImages.Checked;
                LockEdit(true);
                StringBuilder sbErrors = new StringBuilder();
                try
                {
                List<DataItem> selectedTagsList = new List<DataItem>();
                if (!allImages)
                {
                    for (int i = 0; i < gridViewDS.SelectedRows.Count; i++)
                    {
                        if (Program.DataManager.DataSet.TryGetValue((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value, out DataItem selectedItem))
                            selectedTagsList.Add(selectedItem);
                        Program.DataManager.RemoveFromCache((string)gridViewDS.SelectedRows[i].Cells["ImageFilePath"].Value);
                    }
                }
                else
                {
                    foreach (var item in Program.DataManager.DataSet)
                    {
                        selectedTagsList.Add(item.Value);
                    }
                    Program.DataManager.ClearCache();
                }
                // Output mode chosen once for the whole batch.
                bool replace = bgRemoverForm.ReplaceOriginal;
                List<string> savedBgCopies = new List<string>();
                List<string> replacedPaths = new List<string>();
                int index = 0;
                foreach (var item in selectedTagsList)
                {
                    if (Extensions.VideoExtensions.Contains(Path.GetExtension(item.ImageFilePath).ToLowerInvariant()))
                    {
                        sbErrors.AppendLine(item.ImageFilePath);
                        continue;
                    }

                    var imgData = await bgRemoverForm.RemoveBackgroundAsync(item.ImageFilePath, selectedModel);
                    if (imgData == null)
                    {
                        sbErrors.AppendLine(item.ImageFilePath);
                    }
                    else
                    {
                        string outputPath = item.ImageFilePath;
                        if (!replace)
                        {
                            string dir = Path.GetDirectoryName(item.ImageFilePath) ?? string.Empty;
                            string baseName = Path.GetFileNameWithoutExtension(item.ImageFilePath);
                            outputPath = Path.Combine(dir, baseName + "_nobg.png");
                        }
                        try
                        {
                            // Atomic swap: a failed write must not leave a truncated image.
                            await Task.Run(() => SafeFile.WriteAllBytes(outputPath, imgData));
                        }
                        catch (Exception ex)
                        {
                            sbErrors.AppendLine($"{outputPath}: {ex.Message}");
                            SetStatus($"{++index} / {selectedTagsList.Count}");
                            continue;
                        }
                        if (replace)
                        {
                            // Only the overwritten source needs its thumbnail rebuilt.
                            // MakeThumb (ImageSharp) reads the just-written file once
                            // and does not lock it.
                            try
                            {
                                Program.DataManager.DataSet[item.ImageFilePath].Img = Extensions.MakeThumb(item.ImageFilePath, Program.Settings.PreviewSize);
                            }
                            catch (Exception)
                            {
                                Program.DataManager.DataSet[item.ImageFilePath].Img = null;
                            }
                            replacedPaths.Add(item.ImageFilePath);
                        }
                        else
                        {
                            savedBgCopies.Add(outputPath);
                        }
                    }
                    SetStatus($"{++index} / {selectedTagsList.Count}");
                }

                // Refresh the grid/preview after all writes have completed. All
                // reads below use images already in memory or a single fresh read
                // through the (already-cleared) cache, so nothing races the writes.
                if (replace)
                {
                    RefreshDatasetGrid();
                    // Reload the current preview if the selected image was processed
                    // (its cache entry was cleared above, so this re-reads the new file).
                    if (IsPreviewFollowActive && gridViewDS.SelectedRows.Count == 1)
                    {
                        string cur = gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value as string;
                        if (!string.IsNullOrEmpty(cur) && replacedPaths.Contains(cur))
                            ShowPreview(cur);
                    }
                }
                else if (savedBgCopies.Count > 0)
                {
                    // Import the "_nobg.png" copies so they appear in the list.
                    IReadOnlyList<string> added = Program.DataManager.AddImages(savedBgCopies, true, false);
                    RefreshDatasetGrid(added);
                }
                //if (selectedTagsList.Count > 1)
                //{
                //    LoadSelectedImageToGrid();
                //}
                //if (gridViewAllTags.DataSource == null)
                //    BindTagList();
                }
                finally
                {
                    LockEdit(false);
                }
                if (sbErrors.Length > 0)
                {
                    sbErrors.Insert(0, "The following files were not processed (see AiApiServer log):\n");
                    MessageBox.Show(sbErrors.ToString());
                }
                return true;
            }
        }

        private async void generateTagsWithSettingsWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, false);
        }

        private async void btnAutoGetTagsOpenSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(false);
        }

        private void MenuHideAllTags_Click(object sender, EventArgs e)
        {
            HideShowAllTagsWindow();
        }

        private void MenuHideTags_Click(object sender, EventArgs e)
        {
            HideShowTagsWindow();
        }

        private void MenuHideDataset_Click(object sender, EventArgs e)
        {
            HideShowDataset();
        }

        private void toolStripMenuItemWeight_ValueChanged(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                return;
            }
            int wMod = toolStripMenuItemWeight.Value;
            float weight = 1;
            if (wMod == 0)
                weight = 1;
            else if (wMod > 0)
            {
                weight = WeightMultiplier(weight, PromptParser.round_bracket_multiplier, wMod);
            }
            else
            {
                weight = WeightMultiplier(weight, PromptParser.square_bracket_multiplier, Math.Abs(wMod));
            }
            toolStripTextBoxWeight.Text = weight.ToString();

            if (gridViewTags.SelectedCells.Count == 0)
                return;
            int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
            var dsType = GetTagsDataSourceType();

            if (dsType == DataSourceType.Single)
            {
                ((EditableTagList)gridViewTags.DataSource)[rowIndex].Weight = weight;
            }
            else if (dsType == DataSourceType.Multi)
            {
                var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetDataItem();
                dataItem.Tags[((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetTagIndex()].Weight = weight;
            }
        }

        private float WeightMultiplier(float value, float multiplier, int count)
        {
            for (int i = 0; i < count; i++)
            {
                value *= multiplier;
            }
            return value;
        }

        private void gridViewTags_SelectionChanged(object sender, EventArgs e)
        {
            if (gridViewTags.SelectedCells.Count == 0)
                return;
            int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
            var dsType = GetTagsDataSourceType();
            float weight = 1;
            if (dsType == DataSourceType.Single)
            {
                weight = ((EditableTagList)gridViewTags.DataSource)[rowIndex].Weight;
                toolStripTextBoxWeight.Text = weight.ToString();
            }
            else if (dsType == DataSourceType.Multi)
            {
                if (((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex].RowState == DataRowState.Detached && rowIndex < gridViewTags.RowCount)
                {
                    rowIndex++;
                }
                var dataItem = (DataItem)((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetDataItem();
                weight = dataItem.Tags[((MultiSelectDataRow)((MultiSelectDataTable)gridViewTags.DataSource).Rows[rowIndex]).GetTagIndex()].Weight;
                toolStripTextBoxWeight.Text = weight.ToString();
            }
            else
                return;
            if (weight == 1)
                toolStripMenuItemWeight.Value = 0;
            else if (weight > 1)
            {
                int brCount = Extensions.CalcBracketsCount(weight, true);
                if (brCount != 0)
                    toolStripMenuItemWeight.Value = brCount;
                else
                    toolStripMenuItemWeight.Value = 0;
            }
            else
            {
                int brCount = Extensions.CalcBracketsCount(weight, false);
                if (brCount != 0)
                    toolStripMenuItemWeight.Value = -brCount;
                else
                    toolStripMenuItemWeight.Value = 0;
            }
        }

        private async void generateTagsWithAutoTaggerForAllImagesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, true);
        }

        private void toolStripButton2_Click(object sender, EventArgs e)
        {
            AddSelectedAutoTagsToImageTags();
        }

        private void AddSelectedAutoTagsToImageTags()
        {
            if (gridViewTags.DataSource == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            if (gridViewAutoTags.SelectedCells.Count == 0)
                return;
            List<string> selectedTags = new List<string>();
            for (int i = 0; i < gridViewAutoTags.SelectedCells.Count; i++)
            {
                string tag = (string)gridViewAutoTags["Tag", gridViewAutoTags.SelectedCells[i].RowIndex].Value;
                if (!selectedTags.Contains(tag))
                    selectedTags.Add(tag);
            }
            if (gridViewDS.SelectedRows.Count > 1)
            {
                foreach (var item in selectedTags)
                {
                    AddTagMultiselectedMode(item, true, AddingType.Down, 0);
                }

            }
            else
            {
                var eTagList = ((EditableTagList)gridViewTags.DataSource);
                foreach (var item in selectedTags)
                {
                    eTagList.AddTag(item, true, AddingType.Down, 0);
                }
            }
        }

        private void gridViewAutoTags_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            AddSelectedAutoTagsToImageTags();
        }

        private void gridViewAllTags_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (gridViewAllTags.SelectedCells.Count > 0)
                {
                    List<string> tagsToCopy = new List<string>();
                    for (int i = 0; i < gridViewAllTags.SelectedCells.Count; i++)
                    {
                        tagsToCopy.Add((string)gridViewAllTags["TagsColumn", gridViewAllTags.SelectedCells[i].RowIndex].Value);
                    }
                    DataObject d = new DataObject();
                    d.SetText(string.Join("\r\n", tagsToCopy));
                    d.SetData("PartTagList", tagsToCopy);
                    Clipboard.SetDataObject(d);
                    SetStatus(I18n.GetText("StatusCopied"));
                }
                e.SuppressKeyPress = true;
            }
        }
        #region Debug
        // Opt-in shell for future diagnostics (Settings -> Debug mode); the old
        // developer-only test entries (sorter / image grid / manual crop) are gone.
        private void debugOpenLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!File.Exists(DebugLog.LogPath))
            {
                MessageBox.Show(this, I18n.GetText("TipDebugLogEmpty"), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(DebugLog.LogPath) { UseShellExecute = true });
        }
        #endregion

        private void nameAscendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Tag ASC";
        }

        private void countAscendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Count ASC";
        }

        private void countDescendingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            ((BindingSource)gridViewAllTags.DataSource).Sort = "Count DESC";
        }

        private void BtnTagImageChecker_Click(object sender, EventArgs e)
        {
            if (GetTagsDataSourceType() != DataSourceType.Multi)
            {
                return;
            }
            var dt = (MultiSelectDataTable)gridViewTags.DataSource;
            if (dt.Rows.Count == 0)
                return;

            // A pre-selected tag row is optional now: the editor has its own tag
            // list, the selection just decides which tag opens first.
            string initialTag = null;
            if (gridViewTags.SelectedCells.Count > 0)
            {
                int rowIndex = gridViewTags.SelectedCells[0].RowIndex;
                if (rowIndex >= 0 && rowIndex < dt.Rows.Count)
                    initialTag = ((MultiSelectDataRow)dt.Rows[rowIndex]).GetTagText();
            }

            using Form_TagImagesGrid imgGrid = new Form_TagImagesGrid();
            imgGrid.InitTagEditor(dt.GetSelectedDataItems(), initialTag);
            if (imgGrid.ShowDialog() != DialogResult.OK)
                return;
            foreach (var tagChanges in imgGrid.GetAllChanges())
            {
                dt.UpdateDataForTag(tagChanges.Key, tagChanges.Value);
            }
            gridViewTags.Refresh();
        }

        private void gridViewTags_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (GetTagsDataSourceType() == DataSourceType.Multi)
            {
                MultiSelectDataTable dt = (MultiSelectDataTable)gridViewTags.DataSource;
                MultiSelectDataRow dr = (MultiSelectDataRow)dt.Rows[e.RowIndex];
                e.ToolTipText = I18n.GetText("TipTagCountInSelected") + " " + dt.GetTagsCount(dr.GetTagText());
            }
        }

        private void BtnDSChangeSelection_Click(object sender, EventArgs e)
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            selectionMode = true;
            Form_TagImagesGrid imgGrid = new Form_TagImagesGrid();
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                var dItem = Program.DataManager.DataSet[(string)gridViewDS["ImageFilePath", i].Value];
                bool selected = gridViewDS.Rows[i].Selected;
                imgGrid.AddDataItemChangeSelection(dItem, selected);
            }
            if (imgGrid.ShowDialog() != DialogResult.OK)
            {
                imgGrid.Close();
                selectionMode = false;
                return;
            }
            var result = imgGrid.GetResult(true);
            var selectionByPath = result.ToDictionary(a => a.Key.ImageFilePath, a => a.Value, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < gridViewDS.RowCount; i++)
            {
                string imgPath = (string)gridViewDS["ImageFilePath", i].Value;
                if (selectionByPath.TryGetValue(imgPath, out bool selected))
                    gridViewDS.Rows[i].Selected = selected;
            }
            LoadSelectedImageToGrid();
            selectionMode = false;
        }

        private async void backgroundRemovalWithRMBG20ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var res = await RemoveBackgrounds(false);
            if (res)
                SetStatus(I18n.GetText("TipBGRemovalComplete"));
            else
                SetStatus(I18n.GetText("TipBGRemovalCanceled"));
        }

        private async void removeBackgroundToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var res = await RemoveBackgrounds(true);
            if (res)
                SetStatus(I18n.GetText("TipBGRemovalComplete"));
            else
                SetStatus(I18n.GetText("TipBGRemovalCanceled"));
        }

        private void cropImageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (gridViewDS.SelectedRows.Count == 0 || Program.DataManager == null)
                return;

            string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
            using (Form_ImageCrop form = new Form_ImageCrop(imagePath))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.ExportedPaths.Count == 0)
                    return;

                IReadOnlyList<string> added = Program.DataManager.AddImages(
                    form.ExportedPaths,
                    loadPreviewImages: true,
                    readMetadata: false);
                RefreshDatasetGrid();
                if (added.Count > 0)
                    SetStatus(string.Format(I18n.GetText("CropImageImportedCount"), added.Count));
            }
        }

        private void EditSelectedImage()
        {
            if (gridViewDS.SelectedRows.Count == 0 || Program.DataManager == null)
                return;
            string imagePath = (string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value;
            if (VideoProcessingService.IsVideoFile(imagePath) || !File.Exists(imagePath))
                return;
            Bitmap image = Program.DataManager.GetImageFromFileWithCache(imagePath) as Bitmap;
            if (image == null)
            {
                MessageBox.Show(this, I18n.GetText("TipImgLoadError"), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using Form_ImageEditor editor = new Form_ImageEditor(imagePath, image);
            if (editor.ShowDialog(this) != DialogResult.OK || editor.Result == null)
                return;

            if (editor.Result.Mode == ImageEditorSaveMode.Overwrite)
            {
                // Same refresh sequence as background removal: drop the cached
                // original, rebuild the thumbnail from the just-written file,
                // then rebind the grid and the open preview.
                Program.DataManager.RemoveFromCache(imagePath);
                if (Program.DataManager.DataSet.TryGetValue(imagePath, out DataItem item))
                {
                    Image oldThumb = item.Img;
                    try
                    {
                        item.Img = Extensions.MakeThumb(imagePath, Program.Settings.PreviewSize);
                    }
                    catch (Exception)
                    {
                        item.Img = null;
                    }
                    // No message pump runs between the swap and the rebind below,
                    // so nothing can still be painting the old thumbnail.
                    oldThumb?.Dispose();
                }
                RefreshDatasetGrid();
                if (IsPreviewFollowActive && gridViewDS.SelectedRows.Count == 1
                    && string.Equals((string)gridViewDS.SelectedRows[0].Cells["ImageFilePath"].Value, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    ShowPreview(imagePath);
                }
                SetStatus(I18n.GetText("ImageEditorSavedOverwrite"));
            }
            else
            {
                IReadOnlyList<string> added = Program.DataManager.AddImages(
                    new[] { editor.Result.OutputPath },
                    loadPreviewImages: true,
                    readMetadata: false);
                RefreshDatasetGrid(added);
                SetStatus(string.Format(I18n.GetText("ImageEditorSavedNewFile"), Path.GetFileName(editor.Result.OutputPath)));
            }
        }

        private async void btnOpenAiAutoGetTagsDefSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(true);
        }

        private async void btnOpenAiAutoGetTagsOpenSet_Click(object sender, EventArgs e)
        {
            await GetTagsWithAutoTagger(false);
        }

        private void BtnMenuOpenAiGenTagsWithCurrentSettings_Click(object sender, EventArgs e)
        {
            ShowLlmTagger();
        }

        private async void BtnMenuOpenAiGenTagsWithSetWindow_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, false);
        }

        private async void MenuOpenAiGenTagsForAllImages_Click(object sender, EventArgs e)
        {
            await GenerateTagsInTags(false, true);
        }


    }
}
