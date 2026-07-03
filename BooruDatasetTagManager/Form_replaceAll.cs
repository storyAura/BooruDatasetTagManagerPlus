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
    public partial class Form_replaceAll : Form
    {
        public bool DataSetFiltered = false;
        private AutoCompleteTextBox newTagTextBox;

        public Form_replaceAll()
        {
            InitializeComponent();
            InitializeNewTagAutocomplete();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            SwitchLanguage();
            labelWarning.ForeColor = Color.OrangeRed;
        }

        public string NewTagText
        {
            get
            {
                return Program.ChineseTagLookup.ResolveInput(newTagTextBox.Text, Program.Settings.Language);
            }
        }

        public void SetNewTagText(string tag)
        {
            newTagTextBox.Text = tag;
            newTagTextBox.SelectAll();
        }

        public void SetNewTagValues(IEnumerable<string> tags)
        {
            newTagTextBox.SetAutocompleteMode(Program.Settings.AutocompleteMode, Program.Settings.AutocompleteSort);
            newTagTextBox.Values = Program.ChineseTagLookup.CreateAutocompleteValues(tags, Program.Settings.Language);
        }

        private void InitializeNewTagAutocomplete()
        {
            newTagTextBox = new AutoCompleteTextBox();
            newTagTextBox.Location = comboBox2.Location;
            newTagTextBox.Size = comboBox2.Size;
            newTagTextBox.Anchor = comboBox2.Anchor;
            newTagTextBox.TabIndex = comboBox2.TabIndex;
            comboBox2.Visible = false;
            Controls.Add(newTagTextBox);
        }

        private void Form_replaceAll_Load(object sender, EventArgs e)
        {
            labelWarning.Visible = DataSetFiltered;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void SwitchLanguage()
        {
            this.Text = I18n.GetText("UIReplaceForm");
            label1.Text = I18n.GetText("UIReplaceSourceTag");
            label2.Text = I18n.GetText("UIReplaceNewTag");
            labelWarning.Text = I18n.GetText("TipWarningFiltered");
        }


    }
}
