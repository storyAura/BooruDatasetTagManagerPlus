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
    public partial class Form_preview : Form
    {
        public Form_preview()
        {
            InitializeComponent();
            Program.ColorManager.ChangeColorScheme(this, Program.ColorManager.SelectedScheme);
            Program.ColorManager.ChangeColorSchemeInConteiner(Controls, Program.ColorManager.SelectedScheme);
            this.MouseWheel += Form_preview_MouseWheel;
            loaded = false;
        }
        private bool loaded;
        private void Form_preview_MouseWheel(object sender, MouseEventArgs e)
        {
            var scale = 1 + (e.Delta > 0 ? 0.1f : -0.1f);
            this.Scale(new SizeF(scale, scale));
            var screen = Screen.FromControl(this);
            this.Location = new Point(screen.WorkingArea.Width / 2 - this.Width / 2, screen.WorkingArea.Height / 2 - this.Height / 2);
        }

        public void Show(Image img)
        {
            SetImage(img);
            this.Show();
        }

        /// <summary>
        /// Modal preview. Use this when the caller owns the form in a `using`
        /// block: a non-modal Show() would return immediately and the form would
        /// be disposed on scope exit (window flashes and vanishes).
        /// </summary>
        public DialogResult ShowDialog(Image img)
        {
            SetImage(img);
            return this.ShowDialog();
        }

        private void SetImage(Image img)
        {
            // The caller owns img (the image cache hands out clones), so the
            // previous image must always be disposed. Detach it from the
            // PictureBox before disposing to avoid animating a disposed image.
            Image old = pictureBox1.Image;
            pictureBox1.Image = img;
            if (old != null && !ReferenceEquals(old, img))
                old.Dispose();

            if (!loaded)
            {
                this.AutoSize = false;
                this.ClientSize = pictureBox1.Image.Size;
                this.pictureBox1.Dock = DockStyle.Fill;
                this.pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
                loaded = true;
            }
        }

        private void Form_preview_VisibleChanged(object sender, EventArgs e)
        {
            if (!this.Visible)
            {
                if (pictureBox1.Image != null)
                {
                    Image old = pictureBox1.Image;
                    // Detach before disposing so a subsequent show / WM_SHOWWINDOW
                    // never asks the PictureBox to animate a disposed image
                    // (ImageAnimator.CanAnimate throws "Parameter is not valid").
                    pictureBox1.Image = null;
                    old.Dispose();
                }
            }
        }
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (Form.ModifierKeys == Keys.None && keyData == Keys.Escape)
            {
                this.Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }
    }
}
