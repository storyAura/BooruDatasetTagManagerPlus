using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    /// <summary>
    /// Tools → merge low-count same-suffix tags into one chosen tag.
    /// Layout follows <see cref="Form_TagFolderClassify"/>: searchable list,
    /// live preview, hint, then confirm.
    /// </summary>
    public sealed class Form_QuickReplace : Form
    {
        private readonly MainForm mainForm;
        private readonly TextBox searchBox;
        private readonly ListView listTags;
        private readonly ListView listPreview;
        private readonly NumericUpDown numericThreshold;
        private readonly Label hintLabel;
        private readonly Button buttonRun;
        private readonly Button buttonCancel;
        private readonly List<AllTagsItem> allTags = new List<AllTagsItem>();
        private string selectedTag = string.Empty;
        private bool rebuilding;

        public Form_QuickReplace(MainForm mainForm, string initialTag)
        {
            this.mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
            selectedTag = (initialTag ?? string.Empty).Trim();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(LogicalToDeviceUnits(560), LogicalToDeviceUnits(480));
            Size = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(580));
            Text = I18n.GetText("QuickReplaceTitle");

            searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = I18n.GetText("QuickReplaceSearch")
            };
            searchBox.TextChanged += (_, _) => RebuildTagList();

            listTags = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listTags.Columns.Add(I18n.GetText("QuickReplaceTag"), LogicalToDeviceUnits(280));
            listTags.Columns.Add(I18n.GetText("QuickReplaceCount"), LogicalToDeviceUnits(80));
            listTags.SelectedIndexChanged += (_, _) =>
            {
                if (rebuilding)
                    return;
                if (listTags.SelectedItems.Count > 0 && listTags.SelectedItems[0].Tag is string tag)
                    selectedTag = tag;
                RefreshPreview();
            };

            listPreview = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listPreview.Columns.Add(I18n.GetText("QuickReplaceTag"), LogicalToDeviceUnits(200));
            listPreview.Columns.Add(I18n.GetText("QuickReplaceCount"), LogicalToDeviceUnits(80));

            var thresholdLabel = new Label
            {
                AutoSize = true,
                Text = I18n.GetText("QuickReplaceThreshold"),
                Padding = new Padding(0, LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), 0)
            };
            numericThreshold = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 99999,
                Width = LogicalToDeviceUnits(110)
            };
            int stored = Program.Settings?.QuickReplaceThreshold ?? 30;
            numericThreshold.Value = Math.Clamp(stored, (int)numericThreshold.Minimum, (int)numericThreshold.Maximum);
            numericThreshold.ValueChanged += (_, _) => RefreshPreview();

            hintLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new Size(LogicalToDeviceUnits(380), 0),
                Text = I18n.GetText("QuickReplacePreviewEmpty")
            };
            buttonRun = new Button
            {
                Text = I18n.GetText("QuickReplaceRun"),
                AutoSize = true
            };
            buttonRun.Click += (_, _) => RunReplace();
            buttonCancel = new Button
            {
                Text = I18n.GetText("QuickReplaceCancel"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel
            };
            CancelButton = buttonCancel;

            var thresholdPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true
            };
            thresholdPanel.Controls.Add(thresholdLabel);
            thresholdPanel.Controls.Add(numericThreshold);

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
                RowCount = 4,
                Padding = new Padding(LogicalToDeviceUnits(10))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(searchBox, 0, 0);
            layout.SetColumnSpan(searchBox, 2);
            layout.Controls.Add(listTags, 0, 1);
            layout.Controls.Add(listPreview, 1, 1);
            layout.Controls.Add(thresholdPanel, 0, 2);
            layout.SetColumnSpan(thresholdPanel, 2);
            layout.Controls.Add(hintLabel, 0, 3);
            layout.Controls.Add(buttons, 1, 3);
            Controls.Add(layout);

            ReloadTags();

            if (Program.ColorManager != null)
            {
                Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
                Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            }
        }

        private void ReloadTags()
        {
            allTags.Clear();
            if (Program.DataManager?.AllTags != null)
            {
                allTags.AddRange(Program.DataManager.AllTags
                    .Cast<AllTagsItem>()
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Tag))
                    .OrderByDescending(item => item.Count)
                    .ThenBy(item => item.Tag, StringComparer.OrdinalIgnoreCase));
            }

            RebuildTagList();
        }

        private void RebuildTagList()
        {
            string filter = searchBox.Text?.Trim() ?? string.Empty;
            rebuilding = true;
            listTags.BeginUpdate();
            try
            {
                listTags.Items.Clear();
                ListViewItem toSelect = null;
                foreach (AllTagsItem item in allTags)
                {
                    if (filter.Length > 0 && item.Tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var row = new ListViewItem(item.Tag);
                    row.SubItems.Add(item.Count.ToString());
                    row.Tag = item.Tag;
                    listTags.Items.Add(row);
                    if (toSelect == null
                        && selectedTag.Length > 0
                        && string.Equals(item.Tag, selectedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        toSelect = row;
                    }
                }

                if (toSelect != null)
                    toSelect.Selected = true;
            }
            finally
            {
                listTags.EndUpdate();
                rebuilding = false;
            }

            RefreshPreview();
        }

        private List<AllTagsItem> GetCandidates()
        {
            if (selectedTag.Length == 0 || Program.DataManager?.AllTags == null)
                return new List<AllTagsItem>();

            HashSet<string> names = new HashSet<string>(
                QuickTagReplaceService.GetReplacementSourceTags(
                    Program.DataManager.AllTags.Cast<AllTagsItem>(),
                    selectedTag,
                    (int)numericThreshold.Value),
                StringComparer.OrdinalIgnoreCase);

            return allTags
                .Where(item => names.Contains(item.Tag))
                .OrderBy(item => item.Count)
                .ThenBy(item => item.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void RefreshPreview()
        {
            List<AllTagsItem> candidates = GetCandidates();
            listPreview.BeginUpdate();
            try
            {
                listPreview.Items.Clear();
                foreach (AllTagsItem item in candidates)
                {
                    var row = new ListViewItem(item.Tag);
                    row.SubItems.Add(item.Count.ToString());
                    listPreview.Items.Add(row);
                }
            }
            finally
            {
                listPreview.EndUpdate();
            }

            hintLabel.Text = selectedTag.Length == 0
                ? I18n.GetText("QuickReplacePreviewEmpty")
                : string.Format(I18n.GetText("QuickReplacePreview"), selectedTag, candidates.Count);
            buttonRun.Enabled = candidates.Count > 0;
        }

        private void RunReplace()
        {
            if (Program.DataManager == null)
            {
                MessageBox.Show(this, I18n.GetText("TipDatasetNoLoad"));
                return;
            }

            if (selectedTag.Length == 0)
            {
                MessageBox.Show(this, I18n.GetText("QuickReplaceNoTarget"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<AllTagsItem> candidates = GetCandidates();
            if (candidates.Count == 0)
            {
                MessageBox.Show(this, I18n.GetText("TipQuickReplaceNoCandidates"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    string.Format(I18n.GetText("TipQuickReplaceConfirm"), candidates.Count, selectedTag),
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                != DialogResult.OK)
            {
                return;
            }

            int threshold = (int)numericThreshold.Value;
            mainForm.ApplyQuickReplace(
                candidates.Select(item => item.Tag).ToList(),
                selectedTag,
                threshold);
            MessageBox.Show(this,
                string.Format(I18n.GetText("QuickReplaceDone"), candidates.Count, selectedTag),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
