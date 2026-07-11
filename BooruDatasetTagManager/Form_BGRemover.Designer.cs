namespace BooruDatasetTagManager
{
    partial class Form_BGRemover
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
            labelModel = new System.Windows.Forms.Label();
            comboModels = new System.Windows.Forms.ComboBox();
            labelSource = new System.Windows.Forms.Label();
            comboDownloadSource = new System.Windows.Forms.ComboBox();
            buttonPrepareModel = new System.Windows.Forms.Button();
            progressBarDownload = new System.Windows.Forms.ProgressBar();
            groupBox1 = new System.Windows.Forms.GroupBox();
            label4 = new System.Windows.Forms.Label();
            panelMode = new System.Windows.Forms.Panel();
            radioButtonAllImages = new System.Windows.Forms.RadioButton();
            radioButtonOnlySelected = new System.Windows.Forms.RadioButton();
            labelBg = new System.Windows.Forms.Label();
            panelBg = new System.Windows.Forms.Panel();
            radioBgTransparent = new System.Windows.Forms.RadioButton();
            radioBgColor = new System.Windows.Forms.RadioButton();
            buttonBgColor = new System.Windows.Forms.Button();
            labelOutput = new System.Windows.Forms.Label();
            panelOutput = new System.Windows.Forms.Panel();
            radioOutputReplace = new System.Windows.Forms.RadioButton();
            radioOutputCopy = new System.Windows.Forms.RadioButton();
            buttonRemovingTest = new System.Windows.Forms.Button();
            button3 = new System.Windows.Forms.Button();
            button2 = new System.Windows.Forms.Button();
            statusStrip1 = new System.Windows.Forms.StatusStrip();
            toolStripStatusLabel1 = new System.Windows.Forms.ToolStripStatusLabel();
            groupBox1.SuspendLayout();
            panelMode.SuspendLayout();
            panelBg.SuspendLayout();
            panelOutput.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            //
            // labelModel
            //
            labelModel.AutoSize = true;
            labelModel.Location = new System.Drawing.Point(12, 12);
            labelModel.Name = "labelModel";
            labelModel.Size = new System.Drawing.Size(157, 15);
            labelModel.TabIndex = 0;
            labelModel.Text = "Background Removal Model";
            //
            // comboModels
            //
            comboModels.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboModels.FormattingEnabled = true;
            comboModels.Location = new System.Drawing.Point(12, 30);
            comboModels.Name = "comboModels";
            comboModels.Size = new System.Drawing.Size(467, 23);
            comboModels.TabIndex = 1;
            //
            // labelSource
            //
            labelSource.AutoSize = true;
            labelSource.Location = new System.Drawing.Point(12, 62);
            labelSource.Name = "labelSource";
            labelSource.Size = new System.Drawing.Size(80, 15);
            labelSource.TabIndex = 2;
            labelSource.Text = "Download source";
            //
            // comboDownloadSource
            //
            comboDownloadSource.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboDownloadSource.FormattingEnabled = true;
            comboDownloadSource.Location = new System.Drawing.Point(110, 59);
            comboDownloadSource.Name = "comboDownloadSource";
            comboDownloadSource.Size = new System.Drawing.Size(160, 23);
            comboDownloadSource.TabIndex = 3;
            //
            // buttonPrepareModel
            //
            buttonPrepareModel.Location = new System.Drawing.Point(285, 58);
            buttonPrepareModel.Name = "buttonPrepareModel";
            buttonPrepareModel.Size = new System.Drawing.Size(194, 25);
            buttonPrepareModel.TabIndex = 4;
            buttonPrepareModel.Text = "Download and load model";
            buttonPrepareModel.UseVisualStyleBackColor = true;
            buttonPrepareModel.Click += buttonPrepareModel_Click;
            //
            // progressBarDownload
            //
            progressBarDownload.Location = new System.Drawing.Point(12, 90);
            progressBarDownload.Name = "progressBarDownload";
            progressBarDownload.Size = new System.Drawing.Size(467, 18);
            progressBarDownload.TabIndex = 5;
            //
            // groupBox1
            //
            groupBox1.Controls.Add(label4);
            groupBox1.Controls.Add(panelMode);
            groupBox1.Controls.Add(labelBg);
            groupBox1.Controls.Add(panelBg);
            groupBox1.Controls.Add(labelOutput);
            groupBox1.Controls.Add(panelOutput);
            groupBox1.Controls.Add(buttonRemovingTest);
            groupBox1.Controls.Add(button3);
            groupBox1.Controls.Add(button2);
            groupBox1.Enabled = false;
            groupBox1.Location = new System.Drawing.Point(12, 118);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new System.Drawing.Size(467, 200);
            groupBox1.TabIndex = 6;
            groupBox1.TabStop = false;
            groupBox1.Text = "Removing settings";
            //
            // label4 (removing mode)
            //
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(6, 32);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(95, 15);
            label4.TabIndex = 0;
            label4.Text = "Removing mode";
            //
            // panelMode (isolates the removing-mode radio group)
            //
            panelMode.Controls.Add(radioButtonAllImages);
            panelMode.Controls.Add(radioButtonOnlySelected);
            panelMode.Location = new System.Drawing.Point(103, 26);
            panelMode.Name = "panelMode";
            panelMode.Size = new System.Drawing.Size(355, 26);
            panelMode.TabIndex = 1;
            //
            // radioButtonAllImages
            //
            radioButtonAllImages.AutoSize = true;
            radioButtonAllImages.Checked = true;
            radioButtonAllImages.Location = new System.Drawing.Point(3, 3);
            radioButtonAllImages.Name = "radioButtonAllImages";
            radioButtonAllImages.Size = new System.Drawing.Size(80, 19);
            radioButtonAllImages.TabIndex = 0;
            radioButtonAllImages.TabStop = true;
            radioButtonAllImages.Text = "All images";
            radioButtonAllImages.UseVisualStyleBackColor = true;
            //
            // radioButtonOnlySelected
            //
            radioButtonOnlySelected.AutoSize = true;
            radioButtonOnlySelected.Location = new System.Drawing.Point(140, 3);
            radioButtonOnlySelected.Name = "radioButtonOnlySelected";
            radioButtonOnlySelected.Size = new System.Drawing.Size(137, 19);
            radioButtonOnlySelected.TabIndex = 1;
            radioButtonOnlySelected.Text = "Only selected images";
            radioButtonOnlySelected.UseVisualStyleBackColor = true;
            //
            // labelBg (background fill)
            //
            labelBg.AutoSize = true;
            labelBg.Location = new System.Drawing.Point(6, 68);
            labelBg.Name = "labelBg";
            labelBg.Size = new System.Drawing.Size(95, 15);
            labelBg.TabIndex = 2;
            labelBg.Text = "Background";
            //
            // panelBg (isolates the background radio group)
            //
            panelBg.Controls.Add(radioBgTransparent);
            panelBg.Controls.Add(radioBgColor);
            panelBg.Controls.Add(buttonBgColor);
            panelBg.Location = new System.Drawing.Point(103, 60);
            panelBg.Name = "panelBg";
            panelBg.Size = new System.Drawing.Size(355, 30);
            panelBg.TabIndex = 3;
            //
            // radioBgTransparent
            //
            radioBgTransparent.AutoSize = true;
            radioBgTransparent.Location = new System.Drawing.Point(3, 5);
            radioBgTransparent.Name = "radioBgTransparent";
            radioBgTransparent.Size = new System.Drawing.Size(90, 19);
            radioBgTransparent.TabIndex = 0;
            radioBgTransparent.Text = "Transparent";
            radioBgTransparent.UseVisualStyleBackColor = true;
            //
            // radioBgColor
            //
            radioBgColor.AutoSize = true;
            radioBgColor.Checked = true;
            radioBgColor.Location = new System.Drawing.Point(140, 5);
            radioBgColor.Name = "radioBgColor";
            radioBgColor.Size = new System.Drawing.Size(70, 19);
            radioBgColor.TabIndex = 1;
            radioBgColor.TabStop = true;
            radioBgColor.Text = "Solid color";
            radioBgColor.UseVisualStyleBackColor = true;
            radioBgColor.CheckedChanged += radioBgColor_CheckedChanged;
            //
            // buttonBgColor
            //
            buttonBgColor.Location = new System.Drawing.Point(236, 3);
            buttonBgColor.Name = "buttonBgColor";
            buttonBgColor.Size = new System.Drawing.Size(60, 23);
            buttonBgColor.TabIndex = 2;
            buttonBgColor.Text = "";
            buttonBgColor.UseVisualStyleBackColor = false;
            buttonBgColor.Click += buttonBgColor_Click;
            //
            // labelOutput (output mode)
            //
            labelOutput.AutoSize = true;
            labelOutput.Location = new System.Drawing.Point(6, 106);
            labelOutput.Name = "labelOutput";
            labelOutput.Size = new System.Drawing.Size(95, 15);
            labelOutput.TabIndex = 4;
            labelOutput.Text = "Output";
            //
            // panelOutput (isolates the output radio group)
            //
            panelOutput.Controls.Add(radioOutputReplace);
            panelOutput.Controls.Add(radioOutputCopy);
            panelOutput.Location = new System.Drawing.Point(103, 98);
            panelOutput.Name = "panelOutput";
            panelOutput.Size = new System.Drawing.Size(355, 26);
            panelOutput.TabIndex = 5;
            //
            // radioOutputReplace
            //
            radioOutputReplace.AutoSize = true;
            radioOutputReplace.Checked = true;
            radioOutputReplace.Location = new System.Drawing.Point(3, 3);
            radioOutputReplace.Name = "radioOutputReplace";
            radioOutputReplace.Size = new System.Drawing.Size(110, 19);
            radioOutputReplace.TabIndex = 0;
            radioOutputReplace.TabStop = true;
            radioOutputReplace.Text = "Replace original";
            radioOutputReplace.UseVisualStyleBackColor = true;
            //
            // radioOutputCopy
            //
            radioOutputCopy.AutoSize = true;
            radioOutputCopy.Location = new System.Drawing.Point(140, 3);
            radioOutputCopy.Name = "radioOutputCopy";
            radioOutputCopy.Size = new System.Drawing.Size(150, 19);
            radioOutputCopy.TabIndex = 1;
            radioOutputCopy.Text = "Save a copy";
            radioOutputCopy.UseVisualStyleBackColor = true;
            //
            // buttonRemovingTest
            //
            buttonRemovingTest.Location = new System.Drawing.Point(168, 150);
            buttonRemovingTest.Name = "buttonRemovingTest";
            buttonRemovingTest.Size = new System.Drawing.Size(157, 23);
            buttonRemovingTest.TabIndex = 8;
            buttonRemovingTest.Text = "Removing test";
            buttonRemovingTest.UseVisualStyleBackColor = true;
            buttonRemovingTest.Click += button4_Click;
            //
            // button3 (cancel)
            //
            button3.Location = new System.Drawing.Point(87, 150);
            button3.Name = "button3";
            button3.Size = new System.Drawing.Size(75, 23);
            button3.TabIndex = 7;
            button3.Text = "Cancel";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            //
            // button2 (ok)
            //
            button2.Location = new System.Drawing.Point(6, 150);
            button2.Name = "button2";
            button2.Size = new System.Drawing.Size(75, 23);
            button2.TabIndex = 6;
            button2.Text = "OK";
            button2.UseVisualStyleBackColor = true;
            button2.Click += button2_Click;
            //
            // statusStrip1
            //
            statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { toolStripStatusLabel1 });
            statusStrip1.Location = new System.Drawing.Point(0, 408);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new System.Drawing.Size(491, 22);
            statusStrip1.TabIndex = 7;
            statusStrip1.Text = "statusStrip1";
            //
            // toolStripStatusLabel1
            //
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new System.Drawing.Size(12, 17);
            toolStripStatusLabel1.Text = "-";
            //
            // Form_BGRemover
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(491, 430);
            Controls.Add(groupBox1);
            Controls.Add(progressBarDownload);
            Controls.Add(buttonPrepareModel);
            Controls.Add(comboDownloadSource);
            Controls.Add(labelSource);
            Controls.Add(comboModels);
            Controls.Add(labelModel);
            Controls.Add(statusStrip1);
            Name = "Form_BGRemover";
            Text = "Background Removal ";
            Load += Form_BGRemover_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            panelMode.ResumeLayout(false);
            panelMode.PerformLayout();
            panelBg.ResumeLayout(false);
            panelBg.PerformLayout();
            panelOutput.ResumeLayout(false);
            panelOutput.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Panel panelMode;
        public System.Windows.Forms.RadioButton radioButtonOnlySelected;
        public System.Windows.Forms.RadioButton radioButtonAllImages;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button buttonRemovingTest;
        private System.Windows.Forms.Button button3;
        private System.Windows.Forms.Button button2;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;
        private System.Windows.Forms.Label labelModel;
        private System.Windows.Forms.ComboBox comboModels;
        private System.Windows.Forms.Label labelSource;
        private System.Windows.Forms.ComboBox comboDownloadSource;
        private System.Windows.Forms.Button buttonPrepareModel;
        private System.Windows.Forms.ProgressBar progressBarDownload;
        private System.Windows.Forms.Label labelBg;
        private System.Windows.Forms.Panel panelBg;
        private System.Windows.Forms.RadioButton radioBgTransparent;
        private System.Windows.Forms.RadioButton radioBgColor;
        private System.Windows.Forms.Button buttonBgColor;
        private System.Windows.Forms.Label labelOutput;
        private System.Windows.Forms.Panel panelOutput;
        private System.Windows.Forms.RadioButton radioOutputReplace;
        private System.Windows.Forms.RadioButton radioOutputCopy;
    }
}
