namespace BooruDatasetTagManager
{
    partial class Form_FindTag
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            lblFind = new System.Windows.Forms.Label();
            txtQuery = new System.Windows.Forms.TextBox();
            btnFindNext = new System.Windows.Forms.Button();
            btnFindPrev = new System.Windows.Forms.Button();
            chkMatchCase = new System.Windows.Forms.CheckBox();
            chkWholeWord = new System.Windows.Forms.CheckBox();
            lblStatus = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // lblFind
            // 
            lblFind.AutoSize = true;
            lblFind.Location = new System.Drawing.Point(12, 12);
            lblFind.Name = "lblFind";
            lblFind.Size = new System.Drawing.Size(65, 15);
            lblFind.TabIndex = 0;
            lblFind.Text = "Find what:";
            // 
            // txtQuery
            // 
            txtQuery.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            txtQuery.Location = new System.Drawing.Point(12, 32);
            txtQuery.Name = "txtQuery";
            txtQuery.Size = new System.Drawing.Size(225, 23);
            txtQuery.TabIndex = 1;
            txtQuery.TextChanged += txtQuery_TextChanged;
            txtQuery.KeyDown += txtQuery_KeyDown;
            // 
            // btnFindNext
            // 
            btnFindNext.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnFindNext.Location = new System.Drawing.Point(247, 30);
            btnFindNext.Name = "btnFindNext";
            btnFindNext.Size = new System.Drawing.Size(100, 26);
            btnFindNext.TabIndex = 2;
            btnFindNext.Text = "Find Next (&N)";
            btnFindNext.UseVisualStyleBackColor = true;
            btnFindNext.Click += btnFindNext_Click;
            // 
            // btnFindPrev
            // 
            btnFindPrev.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            btnFindPrev.Location = new System.Drawing.Point(247, 62);
            btnFindPrev.Name = "btnFindPrev";
            btnFindPrev.Size = new System.Drawing.Size(100, 26);
            btnFindPrev.TabIndex = 3;
            btnFindPrev.Text = "Find Prev (&P)";
            btnFindPrev.UseVisualStyleBackColor = true;
            btnFindPrev.Click += btnFindPrev_Click;
            // 
            // chkMatchCase
            // 
            chkMatchCase.AutoSize = true;
            chkMatchCase.Location = new System.Drawing.Point(12, 65);
            chkMatchCase.Name = "chkMatchCase";
            chkMatchCase.Size = new System.Drawing.Size(111, 19);
            chkMatchCase.TabIndex = 4;
            chkMatchCase.Text = "Match &case";
            chkMatchCase.UseVisualStyleBackColor = true;
            // 
            // chkWholeWord
            // 
            chkWholeWord.AutoSize = true;
            chkWholeWord.Location = new System.Drawing.Point(12, 88);
            chkWholeWord.Name = "chkWholeWord";
            chkWholeWord.Size = new System.Drawing.Size(126, 19);
            chkWholeWord.TabIndex = 5;
            chkWholeWord.Text = "Match &whole tag";
            chkWholeWord.UseVisualStyleBackColor = true;
            // 
            // lblStatus
            // 
            lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            lblStatus.AutoEllipsis = true;
            lblStatus.Location = new System.Drawing.Point(12, 114);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(335, 18);
            lblStatus.TabIndex = 6;
            // 
            // Form_FindTag
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(359, 138);
            Controls.Add(lblStatus);
            Controls.Add(chkWholeWord);
            Controls.Add(chkMatchCase);
            Controls.Add(btnFindPrev);
            Controls.Add(btnFindNext);
            Controls.Add(txtQuery);
            Controls.Add(lblFind);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Form_FindTag";
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Find Tag";
            FormClosing += Form_FindTag_FormClosing;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblFind;
        public System.Windows.Forms.TextBox txtQuery;
        private System.Windows.Forms.Button btnFindNext;
        private System.Windows.Forms.Button btnFindPrev;
        private System.Windows.Forms.CheckBox chkMatchCase;
        private System.Windows.Forms.CheckBox chkWholeWord;
        private System.Windows.Forms.Label lblStatus;
    }
}
