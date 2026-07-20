using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace BooruDatasetTagManager
{
    public partial class Form_ImageSorterSettings : Form
    {
        public Form_ImageSorterSettings()
        {
            InitializeComponent();
        }

        private void Form_ImageSorterSettings_Load(object sender, EventArgs e)
        {
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // The name becomes a directory segment under the root folder;
            // separators, "..", rooted paths and invalid characters could
            // otherwise send copies outside the selected root.
            if (!ImageSorter.IsValidCategoryName(textBoxNodeName.Text))
            {
                MessageBox.Show(this, I18n.GetText("TipSorterInvalidCategoryName"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var selectedNode = treeView1.SelectedNode;
            if (selectedNode == null)
                selectedNode = treeView1.Nodes["Root"];
            bool addToAll = checkBox1.Checked;
            if (selectedNode.Parent == null)
            {
                if (!selectedNode.Nodes.ContainsKey(textBoxNodeName.Text))
                    selectedNode.Nodes.Add(textBoxNodeName.Text, textBoxNodeName.Text);
            }
            else
            {
                if (addToAll)
                {
                    foreach (TreeNode item in selectedNode.Parent.Nodes)
                    {
                        if (!selectedNode.Nodes.ContainsKey(item.Name + "|" + textBoxNodeName.Text))
                            item.Nodes.Add(item.Name + "|" + textBoxNodeName.Text, textBoxNodeName.Text);
                    }
                }
                else
                {
                    if (!selectedNode.Nodes.ContainsKey(selectedNode.Name + "|" + textBoxNodeName.Text))
                        selectedNode.Nodes.Add(selectedNode.Name + "|" + textBoxNodeName.Text, textBoxNodeName.Text);
                }
            }
            textBoxNodeName.Text = "";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Title = "Specify the root folder where the images will be copied.";
            if (openFolderDialog.ShowDialog() != DialogResult.OK)
                return;
            textBoxRootPath.Text = openFolderDialog.Folder;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(textBoxRootPath.Text))
            {
                MessageBox.Show("Root folder not selected");
                return;
            }
            if (!long.TryParse(textBoxIndex.Text, out long fileIndex) || fileIndex < 0)
            {
                MessageBox.Show(this, I18n.GetText("TipSorterInvalidIndex"),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ImageSorter sorter = new ImageSorter(textBoxRootPath.Text);
            sorter.CreateFromTreeNode(treeView1.Nodes["Root"]);
            sorter.FileIndex = fileIndex;
            Form_ImageSorter sorterForm = new Form_ImageSorter(sorter);
            sorterForm.Show();
            //DialogResult = DialogResult.OK;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            if (treeView1.SelectedNode != null && treeView1.SelectedNode.Name != "Root")
                treeView1.SelectedNode.Remove();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (!Directory.Exists(textBoxRootPath.Text))
            {
                MessageBox.Show("Root folder not selected");
                return;
            }
            var scanErrors = new List<string>();
            var files = TolerantFileEnumerator.GetFiles(textBoxRootPath.Text, scanErrors);
            List<long> indexes = new List<long>();
            foreach (var item in files)
            {
                if (long.TryParse(Path.GetFileNameWithoutExtension(item), out long index))
                {
                    indexes.Add(index);
                }
            }
            // A root with no numeric file names must not crash on Max().
            textBoxIndex.Text = (indexes.DefaultIfEmpty(0).Max() + 1).ToString();
            if (scanErrors.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", scanErrors.Take(10)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
