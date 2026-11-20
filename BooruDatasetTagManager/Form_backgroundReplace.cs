using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static BooruDatasetTagManager.DatasetManager;

namespace BooruDatasetTagManager
{
    public partial class Form_backgroundReplace : Form
    {
        private readonly IReadOnlyList<KeyValuePair<string, List<DataItem>>> scopes;
        private ComboBox scopeBox;
        private Label statusLabel;
        private CancellationTokenSource scanCts;

        /// <summary>Transparent images found in the chosen scope (scan mode only).</summary>
        public List<DataItem> Targets { get; private set; } = new List<DataItem>();

        /// <summary>Image count of the scope that produced <see cref="Targets"/>.</summary>
        public int ScopeItemCount { get; private set; }

        public Form_backgroundReplace() : this(null)
        {
        }

        /// <summary>
        /// With <paramref name="scopes"/> (label → images; first entry = whole
        /// dataset) the dialog becomes the detection window: it scans the
        /// selected scope for transparent images as soon as it opens and
        /// re-scans when the scope changes. OK stays disabled until the scan
        /// finds something.
        /// </summary>
        public Form_backgroundReplace(IReadOnlyList<KeyValuePair<string, List<DataItem>>> scopes)
        {
            InitializeComponent();
            this.scopes = scopes;
            if (scopes != null && scopes.Count > 0)
                BuildScopeRow();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            SwitchLanguage();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            ColorDialog colorDialog = new ColorDialog();
            colorDialog.Color = pictureBox1.BackColor;
            if (colorDialog.ShowDialog() != DialogResult.OK)
                return;
            pictureBox1.BackColor = colorDialog.Color;
            colorDialog.Dispose();
        }

        private void SwitchLanguage()
        {
            Text = I18n.GetText("UIAutoBackRepForm");
            radioButton1.Text = I18n.GetText("UIAutoBackRepReplacementColor");
            radioButton2.Text = I18n.GetText("UIAutoBackRepRandomColor");
            radioButton3.Text = I18n.GetText("UIAutoBackRepRandomFromSet");
            button1.Text = I18n.GetText("BtnOK");
            button2.Text = I18n.GetText("BtnCancel");
            button3.Text = I18n.GetText("UIAutoBackRepAdd");
            button4.Text = I18n.GetText("UIAutoBackRepRemove");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            ColorDialog colorDialog = new ColorDialog();
            colorDialog.Color = pictureBox1.BackColor;
            if (colorDialog.ShowDialog() != DialogResult.OK)
                return;
            ListViewItem lvi = new ListViewItem(colorDialog.Color.Name);
            lvi.BackColor = colorDialog.Color;
            listView1.Items.Add(lvi);
            //listBox1.Items[listBox1.Items.Count - 1].
            colorDialog.Dispose();
        }

        private void Form_backgroundReplace_Load(object sender, EventArgs e)
        {

        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count > 0)
                listView1.SelectedItems[0].Remove();
        }

        private void BuildScopeRow()
        {
            // Runtime controls skip DPI auto-scaling: place them relative to
            // the (already scaled) designer controls, never fixed pixels.
            int gap = Font.Height / 2;
            var scopeLabel = new Label { AutoSize = true, Text = I18n.GetText("UIAutoBackRepScope") };
            scopeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = pictureBox1.Left,
                Top = listView1.Bottom + gap,
                Width = button3.Right - pictureBox1.Left
            };
            foreach (var scope in scopes)
                scopeBox.Items.Add(scope.Key);
            scopeBox.SelectedIndex = 0;
            scopeBox.SelectedIndexChanged += (_, _) => _ = RescanAsync();
            scopeLabel.Location = new Point(listView1.Left, scopeBox.Top + (scopeBox.Height - scopeLabel.PreferredHeight) / 2);
            statusLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Left = listView1.Left,
                Top = scopeBox.Bottom + gap / 2,
                Width = ClientSize.Width - listView1.Left * 2,
                Height = Math.Max(Font.Height, button1.Top - scopeBox.Bottom - gap)
            };
            Controls.Add(scopeLabel);
            Controls.Add(scopeBox);
            Controls.Add(statusLabel);
            button1.Enabled = false;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (scopeBox != null)
                _ = RescanAsync();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            scanCts?.Cancel();
            base.OnFormClosed(e);
        }

        private async Task RescanAsync()
        {
            scanCts?.Cancel();
            var cts = new CancellationTokenSource();
            scanCts = cts;
            Targets = new List<DataItem>();
            button1.Enabled = false;

            List<DataItem> items = scopes[Math.Max(0, scopeBox.SelectedIndex)].Value ?? new List<DataItem>();
            ScopeItemCount = items.Count;
            // Videos / jpg / bmp cannot carry alpha: skip them without a decode.
            List<DataItem> candidates = items
                .Where(item => item != null && TransparentImageScanner.SupportsAlpha(item.ImageFilePath))
                .ToList();
            if (candidates.Count == 0)
            {
                statusLabel.Text = I18n.GetText("TipBackgrRepNoImages");
                return;
            }

            var progress = new Progress<int>(done =>
            {
                if (!cts.IsCancellationRequested && !IsDisposed)
                    statusLabel.Text = string.Format(I18n.GetText("TipBackgrRepScanning"), done, candidates.Count);
            });
            List<string> paths = candidates.Select(item => item.ImageFilePath).ToList();
            List<string> found;
            try
            {
                found = await Task.Run(() => TransparentImageScanner.FindTransparent(paths, progress, cts.Token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    statusLabel.Text = ex.Message;
                return;
            }
            // A newer scan (scope switch) or a close superseded this one.
            if (cts.IsCancellationRequested || IsDisposed)
                return;

            var foundSet = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);
            Targets = candidates.Where(item => foundSet.Contains(item.ImageFilePath)).ToList();
            statusLabel.Text = Targets.Count > 0
                ? string.Format(I18n.GetText("UIAutoBackRepFound"), Targets.Count, candidates.Count)
                : string.Format(I18n.GetText("TipBackgrRepNoTransparent"), candidates.Count);
            button1.Enabled = Targets.Count > 0;
        }
    }
}
