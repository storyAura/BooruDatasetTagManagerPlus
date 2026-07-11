using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public partial class Form_TagImagesGrid : Form
    {
        private string currentTag = "";
        // Tag-editor mode state: per-tag edited states so the user can switch
        // between tags in the left list without losing pending changes.
        // Key: tag -> (image item -> should have the tag).
        private readonly Dictionary<string, Dictionary<DatasetManager.DataItem, bool>> editedStates =
            new Dictionary<string, Dictionary<DatasetManager.DataItem, bool>>(StringComparer.Ordinal);
        private List<DatasetManager.DataItem> tagEditorItems;
        private bool switchingTag;

        private sealed class TagListEntry
        {
            public string Tag;
            public int Count;
            public int Total;
            public override string ToString() => $"{Tag}  ({Count}/{Total})";
        }

        public Form_TagImagesGrid()
        {
            InitializeComponent();
            TrackBarZoom.TrackBar.Minimum = 1;
            TrackBarZoom.TrackBar.Maximum = 1000;
            TrackBarZoom.TrackBar.Value = Program.Settings.TagImagesGridSize;
            TrackBarZoom.TrackBar.SmallChange = 50;
            TrackBarZoom.ValueChanged += TrackBarZoom_ValueChanged;
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            SwitchLanguage();
        }

        private void SwitchLanguage()
        {
            this.Text = I18n.GetText("UITagImagesGridForm");
            BtnTgOk.Text = I18n.GetText("SettingBtnSave");
            BtnTgCancel.Text = I18n.GetText("SettingBtnCancel");
            LabelGridZoomText.Text = I18n.GetText("LabelGridZoomText");
            toolStripStatusLabelMSForm.Text = I18n.GetText("ToolStripStatusLabelMSForm");
            labelTagListHeader.Text = I18n.GetText("TagImagesGridTagListHeader");
        }

        private void TrackBarZoom_ValueChanged(object sender, EventArgs e)
        {
            flowLayoutPanelImages.SuspendLayout();
            for (int i = 0; i < flowLayoutPanelImages.Controls.Count; i++)
            {
                if (flowLayoutPanelImages.Controls[i] is CustomPictureBoxWithYN)
                {
                    CustomPictureBoxWithYN c = (CustomPictureBoxWithYN)flowLayoutPanelImages.Controls[i];
                    c.SetSize(TrackBarZoom.TrackBar.Value);

                }
            }
            Program.Settings.TagImagesGridSize = TrackBarZoom.TrackBar.Value;
            Program.Settings.SaveSettings();
            flowLayoutPanelImages.ResumeLayout();
        }

        private void Form_TagImagesGrid_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(currentTag))
                this.Text += ": " + currentTag;
        }

        /// <summary>
        /// Tag-editor mode: shows the image wall for <paramref name="initialTag"/>
        /// and fills the left list with every tag of the selected images so the
        /// user can switch tags freely without reopening the dialog.
        /// </summary>
        public void InitTagEditor(List<DatasetManager.DataItem> items, string initialTag)
        {
            tagEditorItems = items;

            // Aggregate tag occurrence counts across the selected images.
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                foreach (string tag in item.Tags.TextTags.Distinct(StringComparer.Ordinal))
                {
                    counts.TryGetValue(tag, out int c);
                    counts[tag] = c + 1;
                }
            }
            List<TagListEntry> entries = counts
                .Select(kv => new TagListEntry { Tag = kv.Key, Count = kv.Value, Total = items.Count })
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Tag, StringComparer.Ordinal)
                .ToList();

            if (string.IsNullOrEmpty(initialTag) || !counts.ContainsKey(initialTag))
                initialTag = entries.Count > 0 ? entries[0].Tag : string.Empty;
            currentTag = initialTag;

            // Build the wall once (images without the initial tag first); tag
            // switches only recolor the existing picture boxes.
            var sortedItems = items
                .OrderBy(a => a.Tags.Contains(initialTag))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var item in sortedItems)
            {
                CustomPictureBoxWithYN pictureBox = new CustomPictureBoxWithYN(TrackBarZoom.TrackBar.Value, TrackBarZoom.TrackBar.Value, item.Tags.Contains(initialTag));
                pictureBox.BorderStyle = BorderStyle.FixedSingle;
                pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
                pictureBox.SetSelectionMode(true);
                pictureBox.Image = Program.DataManager.GetImageFromFileWithCache(item.ImageFilePath);
                pictureBox.SetDataSetItem(item);
                flowLayoutPanelImages.Controls.Add(pictureBox);
            }

            switchingTag = true;
            try
            {
                listBoxTags.Items.Clear();
                foreach (var entry in entries)
                {
                    int index = listBoxTags.Items.Add(entry);
                    if (entry.Tag == initialTag)
                        listBoxTags.SelectedIndex = index;
                }
            }
            finally
            {
                switchingTag = false;
            }

            panelTagList.Visible = true;
            labelActiveTag.Visible = true;
            UpdateActiveTagLabel();
        }

        private void ListBoxTags_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (switchingTag || listBoxTags.SelectedItem is not TagListEntry entry)
                return;
            SwitchToTag(entry.Tag);
        }

        private void SwitchToTag(string tag)
        {
            if (tag == currentTag || tagEditorItems == null)
                return;
            SnapshotActiveTagStates();
            currentTag = tag;
            flowLayoutPanelImages.SuspendLayout();
            foreach (Control control in flowLayoutPanelImages.Controls)
            {
                if (control is CustomPictureBoxWithYN pb && pb.GetDataSetItem() != null)
                    pb.ResetState(GetTagState(pb.GetDataSetItem(), tag));
            }
            flowLayoutPanelImages.ResumeLayout();
            UpdateActiveTagLabel();
        }

        private void SnapshotActiveTagStates()
        {
            if (string.IsNullOrEmpty(currentTag))
                return;
            if (!editedStates.TryGetValue(currentTag, out var map))
            {
                map = new Dictionary<DatasetManager.DataItem, bool>();
                editedStates[currentTag] = map;
            }
            foreach (Control control in flowLayoutPanelImages.Controls)
            {
                if (control is CustomPictureBoxWithYN pb && pb.GetDataSetItem() != null)
                    map[pb.GetDataSetItem()] = pb.StateYes;
            }
        }

        private bool GetTagState(DatasetManager.DataItem item, string tag)
        {
            if (editedStates.TryGetValue(tag, out var map) && map.TryGetValue(item, out bool edited))
                return edited;
            return item.Tags.Contains(tag);
        }

        private void UpdateActiveTagLabel()
        {
            labelActiveTag.Text = string.Format(I18n.GetText("TagImagesGridActiveTag"), currentTag);
            this.Text = I18n.GetText("UITagImagesGridForm") + ": " + currentTag;
        }

        /// <summary>
        /// All pending changes across every tag the user touched:
        /// tag -> list of (image, should have the tag), only where the new state
        /// differs from the image's current tags.
        /// </summary>
        public Dictionary<string, List<KeyValuePair<DatasetManager.DataItem, bool>>> GetAllChanges()
        {
            SnapshotActiveTagStates();
            var result = new Dictionary<string, List<KeyValuePair<DatasetManager.DataItem, bool>>>(StringComparer.Ordinal);
            foreach (var tagStates in editedStates)
            {
                List<KeyValuePair<DatasetManager.DataItem, bool>> changes = tagStates.Value
                    .Where(kv => kv.Value != kv.Key.Tags.Contains(tagStates.Key))
                    .Select(kv => new KeyValuePair<DatasetManager.DataItem, bool>(kv.Key, kv.Value))
                    .ToList();
                if (changes.Count > 0)
                    result[tagStates.Key] = changes;
            }
            return result;
        }
        /// <summary>
        /// Used to modify selected images in a dataset
        /// </summary>
        /// <param name="item"></param>
        /// <param name="selected"></param>
        public void AddDataItemChangeSelection(DatasetManager.DataItem item, bool selected)
        {
            CustomPictureBoxWithYN pictureBox = new CustomPictureBoxWithYN(TrackBarZoom.TrackBar.Value, TrackBarZoom.TrackBar.Value, selected);
            pictureBox.BorderStyle = BorderStyle.FixedSingle;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.SetSelectionMode(true);
            pictureBox.Image = Program.DataManager.GetImageFromFileWithCache(item.ImageFilePath);
            pictureBox.SetDataSetItem(item);
            flowLayoutPanelImages.Controls.Add(pictureBox);
        }

        public List<KeyValuePair<DatasetManager.DataItem, bool>> GetResult(bool allData = false)
        {
            List<KeyValuePair<DatasetManager.DataItem, bool>> result = new List<KeyValuePair<DatasetManager.DataItem, bool>>();
            for (int i = 0; i < flowLayoutPanelImages.Controls.Count; i++)
            {
                if (flowLayoutPanelImages.Controls[i] is CustomPictureBoxWithYN)
                {
                    CustomPictureBoxWithYN c = (CustomPictureBoxWithYN)flowLayoutPanelImages.Controls[i];
                    if (allData || c.StateChanged)
                        result.Add(new KeyValuePair<DatasetManager.DataItem, bool>(c.GetDataSetItem(), c.StateYes));
                }
            }
            return result;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            for (int i = 0; i < flowLayoutPanelImages.Controls.Count; i++)
            {
                if (flowLayoutPanelImages.Controls[i] is CustomPictureBoxWithYN)
                {
                    CustomPictureBoxWithYN c = (CustomPictureBoxWithYN)flowLayoutPanelImages.Controls[i];
                    // These images are caller-owned clones from the image cache;
                    // nulling without disposing leaked one GDI bitmap handle per
                    // thumbnail every time this grid was opened (10k handle cap).
                    Image img = c.Image;
                    c.Image = null;
                    img?.Dispose();
                }
            }
            base.OnClosing(e);
        }

        private void BtnTgOk_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void BtnTgCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (Form.ModifierKeys == Keys.None && keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }
}
