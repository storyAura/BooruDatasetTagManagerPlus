using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Tools → move images that have every selected tag into one folder.
    /// The user names the folder; blank becomes Mix, then Mix_2 if Mix exists.
    /// </summary>
    public sealed class Form_TagFolderClassify : Form
    {
        private readonly MainForm mainForm;
        private readonly TextBox searchBox;
        private readonly ListView listTags;
        private readonly RadioButton radioAll;
        private readonly RadioButton radioScope;
        private readonly TextBox nameBox;
        private readonly TextBox previewBox;
        private readonly Label hintLabel;
        private readonly Button buttonRun;
        private readonly Button buttonCancel;
        private readonly List<(string Tag, int Count)> allTags = new List<(string Tag, int Count)>();
        private readonly HashSet<string> checkedTags = new HashSet<string>(StringComparer.Ordinal);
        private bool rebuilding;

        public Form_TagFolderClassify(MainForm mainForm)
        {
            this.mainForm = mainForm;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(480));
            Size = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(580));
            Text = I18n.GetText("TagFolderClassifyTitle");

            searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = I18n.GetText("TagFolderClassifySearch")
            };
            searchBox.TextChanged += (_, _) => RebuildTagList();

            listTags = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listTags.Columns.Add(I18n.GetText("TagFolderClassifyTag"), LogicalToDeviceUnits(280));
            listTags.Columns.Add(I18n.GetText("TagFolderClassifyCount"), LogicalToDeviceUnits(80));
            listTags.ItemChecked += ListTags_ItemChecked;

            radioAll = new RadioButton
            {
                AutoSize = true,
                Text = I18n.GetText("TagFolderClassifyScopeAll"),
                Checked = true
            };
            radioScope = new RadioButton
            {
                AutoSize = true,
                Text = I18n.GetText("TagFolderClassifyScopeFolder")
            };
            radioAll.CheckedChanged += (_, _) =>
            {
                if (radioAll.Checked)
                    ReloadSourceTags();
            };
            radioScope.CheckedChanged += (_, _) =>
            {
                if (radioScope.Checked)
                    ReloadSourceTags();
            };

            nameBox = new TextBox
            {
                Width = LogicalToDeviceUnits(240),
                PlaceholderText = TagFolderClassifyPlanner.DefaultFolderName
            };
            nameBox.TextChanged += (_, _) => RefreshPreview();

            previewBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            hintLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = I18n.GetText("TagFolderClassifyHint")
            };
            buttonRun = new Button
            {
                Text = I18n.GetText("TagFolderClassifyRun"),
                AutoSize = true,
                DialogResult = DialogResult.None
            };
            buttonRun.Click += (_, _) => RunClassify();
            buttonCancel = new Button
            {
                Text = I18n.GetText("TagFolderClassifyCancel"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            CancelButton = buttonCancel;

            var scopePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            var scopeLabel = new Label
            {
                AutoSize = true,
                Text = I18n.GetText("TagFolderClassifyScope"),
                Padding = new Padding(0, LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), 0)
            };
            scopePanel.Controls.Add(scopeLabel);
            scopePanel.Controls.Add(radioAll);
            scopePanel.Controls.Add(radioScope);

            var namePanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            var nameLabel = new Label
            {
                AutoSize = true,
                Text = I18n.GetText("TagFolderClassifyFolderName"),
                Padding = new Padding(0, LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), 0)
            };
            namePanel.Controls.Add(nameLabel);
            namePanel.Controls.Add(nameBox);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            buttons.Controls.Add(buttonCancel);
            buttons.Controls.Add(buttonRun);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(LogicalToDeviceUnits(10))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(110)));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(searchBox, 0, 0);
            layout.SetColumnSpan(searchBox, 2);
            layout.Controls.Add(listTags, 0, 1);
            var previewHost = new Panel { Dock = DockStyle.Fill };
            var previewLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Text = I18n.GetText("TagFolderClassifyPreview")
            };
            previewHost.Controls.Add(previewBox);
            previewHost.Controls.Add(previewLabel);
            layout.Controls.Add(previewHost, 1, 1);
            layout.Controls.Add(scopePanel, 0, 2);
            layout.SetColumnSpan(scopePanel, 2);
            layout.Controls.Add(namePanel, 0, 3);
            layout.SetColumnSpan(namePanel, 2);
            layout.Controls.Add(hintLabel, 0, 4);
            layout.SetColumnSpan(hintLabel, 2);
            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);

            bool hasScope = Program.DataManager?.ActiveFolders.Count > 0;
            radioScope.Enabled = hasScope;
            ReloadSourceTags();

            if (Program.ColorManager != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        private IReadOnlyList<DatasetManager.DataItem> SourceItems()
        {
            DatasetManager manager = Program.DataManager;
            if (manager == null)
                return Array.Empty<DatasetManager.DataItem>();
            if (radioScope.Checked && manager.ActiveFolders.Count > 0)
                return manager.GetScopedItems();
            return manager.DataSet.Values.ToList();
        }

        private void ReloadSourceTags()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (DatasetManager.DataItem item in SourceItems())
            {
                foreach (string tag in item.Tags.TextTags.Distinct(StringComparer.Ordinal))
                {
                    counts.TryGetValue(tag, out int count);
                    counts[tag] = count + 1;
                }
            }
            allTags.Clear();
            allTags.AddRange(counts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => (pair.Key, pair.Value)));
            checkedTags.RemoveWhere(tag => !counts.ContainsKey(tag));
            RebuildTagList();
            RefreshPreview();
        }

        private void RebuildTagList()
        {
            string filter = searchBox.Text?.Trim() ?? string.Empty;
            listTags.ItemChecked -= ListTags_ItemChecked;
            rebuilding = true;
            listTags.BeginUpdate();
            try
            {
                listTags.Items.Clear();
                foreach ((string tag, int count) in allTags)
                {
                    if (filter.Length > 0 && tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var row = new ListViewItem(tag) { Checked = checkedTags.Contains(tag) };
                    row.SubItems.Add(count.ToString());
                    row.Tag = tag;
                    listTags.Items.Add(row);
                }
            }
            finally
            {
                listTags.EndUpdate();
                rebuilding = false;
                listTags.ItemChecked += ListTags_ItemChecked;
            }
        }

        /// <summary>
        /// ListView fires ItemChecked while Clear()/rebuild tears rows down;
        /// those notifications must not walk Items (rows can be null).
        /// </summary>
        private void ListTags_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (rebuilding)
                return;
            ApplyCheck(e?.Item);
            RefreshPreview();
        }

        private void SyncCheckedTags()
        {
            if (listTags == null)
                return;
            foreach (ListViewItem row in listTags.Items)
                ApplyCheck(row);
        }

        private void ApplyCheck(ListViewItem row)
        {
            if (row?.Tag is not string tag)
                return;
            if (row.Checked)
                checkedTags.Add(tag);
            else
                checkedTags.Remove(tag);
        }

        private List<TagFolderClassifyItem> BuildClassifyItems()
        {
            DatasetManager manager = Program.DataManager;
            if (manager == null)
                return new List<TagFolderClassifyItem>();
            string root = manager.DatasetRoot;
            return SourceItems()
                .Select(item => new TagFolderClassifyItem(
                    item.ImageFilePath,
                    item.Tags.TextTags,
                    DatasetFolderIndex.GetRelativeFolder(item.ImageFilePath, root)))
                .ToList();
        }

        private IReadOnlyList<TagFolderMove> BuildPlan()
        {
            DatasetManager manager = Program.DataManager;
            if (manager == null || checkedTags.Count == 0)
                return Array.Empty<TagFolderMove>();
            var occupied = manager.DataSet.Keys.Select(path => TagFolderClassifyPlanner.DestKey(
                DatasetFolderIndex.GetRelativeFolder(path, manager.DatasetRoot),
                Path.GetFileName(path)));
            return TagFolderClassifyPlanner.Plan(
                BuildClassifyItems(),
                checkedTags.ToList(),
                occupied,
                nameBox?.Text,
                ExistingFolderNames(manager));
        }

        private static IReadOnlyList<string> ExistingFolderNames(DatasetManager manager)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DatasetFolderEntry entry in manager.GetFolderEntries())
            {
                string relative = DatasetFolderIndex.NormalizeRelative(entry.RelativePath);
                if (relative.Length > 0 && relative != DatasetFolderIndex.RootFolderKey)
                    names.Add(relative);
            }

            string root = manager.DatasetRoot;
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                try
                {
                    foreach (string directory in Directory.GetDirectories(root))
                    {
                        string leaf = Path.GetFileName(directory);
                        if (!string.IsNullOrEmpty(leaf))
                            names.Add(leaf);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"TagFolderClassify folder scan failed: {ex.Message}");
                }
            }

            return names.ToList();
        }

        private void RefreshPreview()
        {
            if (checkedTags.Count == 0)
            {
                previewBox.Text = string.Empty;
                return;
            }
            IReadOnlyList<TagFolderMove> moves = BuildPlan();
            var counts = moves
                .GroupBy(move => move.DestRelativeFolder, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => string.Format(
                    I18n.GetText("TagFolderClassifyPreviewLine"), group.Key, group.Count()));
            int stay = SourceItems().Count - moves.Count;
            previewBox.Text = string.Join(Environment.NewLine, counts
                .Append(string.Format(I18n.GetText("TagFolderClassifyPreviewLine"),
                    I18n.GetText("TagFolderClassifyStay"), stay)));
        }

        private void RunClassify()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"));
                return;
            }
            SyncCheckedTags();
            if (checkedTags.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TagFolderClassifyNoTags"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            IReadOnlyList<TagFolderMove> moves = BuildPlan();
            if (moves.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TagFolderClassifyNoMoves"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    string.Format(
                        I18n.GetText("TagFolderClassifyConfirm"),
                        moves.Count,
                        moves[0].DestRelativeFolder),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                != DialogResult.OK)
            {
                return;
            }

            int moved = 0;
            try
            {
                mainForm.LockEdit(true);
                moved = Program.DataManager.MoveImagesToFolders(moves);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    string.Format(I18n.GetText("TagFolderClassifyFailed"), ex.Message),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                try
                {
                    mainForm.RefreshDatasetGrid();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"TagFolderClassify refresh failed: {ex}");
                }
                mainForm.LockEdit(false);
            }

            if (Program.DataManager.LastMoveErrors.Count > 0)
            {
                MessageBox.Show(this,
                    string.Format(I18n.GetText("TagFolderClassifyPartial"), moved,
                        string.Join(Environment.NewLine, Program.DataManager.LastMoveErrors)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, string.Format(I18n.GetText("TagFolderClassifyDone"), moved),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
