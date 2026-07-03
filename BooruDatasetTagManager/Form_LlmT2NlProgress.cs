using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BooruDatasetTagManager
{
    public sealed class Form_LlmT2NlProgress : Form
    {
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private Label labelCurrentTitle;
        private Label labelCurrentFile;
        private Label labelCounts;
        private ProgressBar progressBar;
        private Button buttonCancel;
        private bool running;
        private bool allowClose;

        public Form_LlmT2NlProgress()
        {
            InitializeComponent();
            ApplyLanguage();
        }

        public CaptionGenerationResult Run(
            IWin32Window owner,
            Func<IProgress<CaptionGenerationProgress>, CancellationToken, Task<CaptionGenerationResult>> operation)
        {
            CaptionGenerationResult result = null;
            Exception error = null;
            Shown += async (_, _) =>
            {
                running = true;
                try
                {
                    Progress<CaptionGenerationProgress> progress = new Progress<CaptionGenerationProgress>(UpdateProgress);
                    result = await operation(progress, cancellation.Token);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    running = false;
                    allowClose = true;
                    Close();
                }
            };

            ShowDialog(owner);
            if (error != null)
                throw error;
            return result ?? new CaptionGenerationResult { Canceled = cancellation.IsCancellationRequested };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running && !allowClose)
            {
                cancellation.Cancel();
                buttonCancel.Enabled = false;
                labelCurrentFile.Text = I18n.GetText("LlmT2NlCanceling");
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                cancellation.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 210);
            MinimumSize = new Size(520, 230);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            labelCurrentTitle = new Label
            {
                Location = new Point(18, 18),
                AutoSize = true
            };
            labelCurrentFile = new Label
            {
                Location = new Point(18, 44),
                Size = new Size(580, 38),
                AutoEllipsis = true
            };
            progressBar = new ProgressBar
            {
                Location = new Point(18, 90),
                Size = new Size(580, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            labelCounts = new Label
            {
                Location = new Point(18, 126),
                Size = new Size(580, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            buttonCancel = new Button
            {
                Location = new Point(498, 164),
                Size = new Size(100, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            buttonCancel.Click += (_, _) =>
            {
                cancellation.Cancel();
                buttonCancel.Enabled = false;
                labelCurrentFile.Text = I18n.GetText("LlmT2NlCanceling");
            };
            Controls.AddRange(new Control[]
            {
                labelCurrentTitle,
                labelCurrentFile,
                progressBar,
                labelCounts,
                buttonCancel
            });
        }

        private void ApplyLanguage()
        {
            Text = I18n.GetText("LlmT2NlProgressTitle");
            labelCurrentTitle.Text = I18n.GetText("LlmT2NlCurrentFile");
            buttonCancel.Text = I18n.GetText("BtnCancel");
            labelCounts.Text = string.Format(I18n.GetText("LlmT2NlProgressCounts"), 0, 0, 0);
        }

        private void UpdateProgress(CaptionGenerationProgress progress)
        {
            labelCurrentFile.Text = progress.CurrentFile;
            progressBar.Maximum = Math.Max(1, progress.Total);
            progressBar.Value = Math.Min(progressBar.Maximum, Math.Max(0, progress.Completed));
            labelCounts.Text = string.Format(
                I18n.GetText("LlmT2NlProgressCounts"),
                progress.Succeeded,
                progress.Skipped,
                progress.Failed);
        }
    }
}
