using System;
using System.Drawing;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public partial class Form_FindTag : Form
    {
        private readonly MainForm _mainForm;
        private Color _defaultTextBoxBackColor;

        public Form_FindTag(MainForm mainForm)
        {
            _mainForm = mainForm;
            InitializeComponent();
            _defaultTextBoxBackColor = txtQuery.BackColor;
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            SwitchLanguage();
        }

        public string QueryText
        {
            get => txtQuery.Text;
            set => txtQuery.Text = value;
        }

        public bool MatchCase => chkMatchCase.Checked;
        public bool WholeWord => chkWholeWord.Checked;

        public void ShowFind(string initialQuery = null)
        {
            if (!string.IsNullOrEmpty(initialQuery))
            {
                txtQuery.Text = initialQuery;
            }
            if (!Visible)
            {
                Show(_mainForm);
            }
            else
            {
                BringToFront();
            }
            txtQuery.Focus();
            txtQuery.SelectAll();
        }

        public void SwitchLanguage()
        {
            Text = I18n.GetText("UIFindTagTitle");
            lblFind.Text = I18n.GetText("UIFindTagQuery");
            btnFindNext.Text = I18n.GetText("UIFindTagNext");
            btnFindPrev.Text = I18n.GetText("UIFindTagPrev");
            chkMatchCase.Text = I18n.GetText("UIFindTagMatchCase");
            chkWholeWord.Text = I18n.GetText("UIFindTagWholeWord");
        }

        public void SetMatchResult(bool found, string query, int matchedIndex = -1, int totalCount = 0)
        {
            if (found)
            {
                txtQuery.BackColor = _defaultTextBoxBackColor;
                lblStatus.ForeColor = Color.ForestGreen;
                lblStatus.Text = string.Format(I18n.GetText("UIFindTagMatched"), matchedIndex + 1, totalCount);
            }
            else
            {
                txtQuery.BackColor = Color.MistyRose;
                lblStatus.ForeColor = Color.Crimson;
                lblStatus.Text = string.Format(I18n.GetText("SearchNoMatch"), query);
            }
        }

        private void btnFindNext_Click(object sender, EventArgs e)
        {
            ExecuteSearch(forward: true);
        }

        private void btnFindPrev_Click(object sender, EventArgs e)
        {
            ExecuteSearch(forward: false);
        }

        private void ExecuteSearch(bool forward)
        {
            string query = txtQuery.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                txtQuery.BackColor = _defaultTextBoxBackColor;
                lblStatus.Text = string.Empty;
                return;
            }
            _mainForm?.PerformImageTagSearch(query, forward, chkMatchCase.Checked, chkWholeWord.Checked, this);
        }

        private void txtQuery_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ExecuteSearch(forward: !e.Shift);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void txtQuery_TextChanged(object sender, EventArgs e)
        {
            txtQuery.BackColor = _defaultTextBoxBackColor;
            lblStatus.Text = string.Empty;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (ModifierKeys == Keys.None && keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void Form_FindTag_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
