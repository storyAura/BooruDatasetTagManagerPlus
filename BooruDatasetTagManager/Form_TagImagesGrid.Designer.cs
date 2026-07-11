namespace BooruDatasetTagManager
{
    partial class Form_TagImagesGrid
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            flowLayoutPanelImages = new System.Windows.Forms.FlowLayoutPanel();
            panelTagList = new System.Windows.Forms.Panel();
            listBoxTags = new System.Windows.Forms.ListBox();
            labelTagListHeader = new System.Windows.Forms.Label();
            labelActiveTag = new System.Windows.Forms.Label();
            toolStrip1 = new System.Windows.Forms.ToolStrip();
            BtnTgOk = new System.Windows.Forms.ToolStripButton();
            BtnTgCancel = new System.Windows.Forms.ToolStripButton();
            toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            LabelGridZoomText = new System.Windows.Forms.ToolStripLabel();
            TrackBarZoom = new ToolStripCustomMenuItem();
            statusStrip1 = new System.Windows.Forms.StatusStrip();
            toolStripStatusLabelMSForm = new System.Windows.Forms.ToolStripStatusLabel();
            toolStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // flowLayoutPanelImages
            // 
            flowLayoutPanelImages.AutoScroll = true;
            flowLayoutPanelImages.AutoSize = true;
            flowLayoutPanelImages.Dock = System.Windows.Forms.DockStyle.Fill;
            flowLayoutPanelImages.Location = new System.Drawing.Point(0, 33);
            flowLayoutPanelImages.Name = "flowLayoutPanelImages";
            flowLayoutPanelImages.Size = new System.Drawing.Size(800, 395);
            flowLayoutPanelImages.TabIndex = 0;
            //
            // panelTagList
            //
            panelTagList.Controls.Add(listBoxTags);
            panelTagList.Controls.Add(labelTagListHeader);
            panelTagList.Dock = System.Windows.Forms.DockStyle.Left;
            panelTagList.Name = "panelTagList";
            panelTagList.Padding = new System.Windows.Forms.Padding(4);
            panelTagList.Size = new System.Drawing.Size(240, 395);
            panelTagList.TabIndex = 3;
            panelTagList.Visible = false;
            //
            // listBoxTags
            //
            listBoxTags.Dock = System.Windows.Forms.DockStyle.Fill;
            listBoxTags.IntegralHeight = false;
            listBoxTags.Name = "listBoxTags";
            listBoxTags.TabIndex = 0;
            listBoxTags.SelectedIndexChanged += ListBoxTags_SelectedIndexChanged;
            //
            // labelTagListHeader
            //
            labelTagListHeader.Dock = System.Windows.Forms.DockStyle.Top;
            labelTagListHeader.Name = "labelTagListHeader";
            labelTagListHeader.Padding = new System.Windows.Forms.Padding(2, 4, 2, 4);
            labelTagListHeader.Size = new System.Drawing.Size(232, 24);
            labelTagListHeader.TabIndex = 1;
            labelTagListHeader.Text = "Tags (count)";
            //
            // labelActiveTag
            //
            labelActiveTag.Dock = System.Windows.Forms.DockStyle.Top;
            labelActiveTag.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            labelActiveTag.Name = "labelActiveTag";
            labelActiveTag.Padding = new System.Windows.Forms.Padding(8, 6, 8, 6);
            labelActiveTag.Size = new System.Drawing.Size(560, 30);
            labelActiveTag.TabIndex = 4;
            labelActiveTag.Text = "-";
            labelActiveTag.Visible = false;
            //
            // toolStrip1
            //
            toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { BtnTgOk, BtnTgCancel, toolStripSeparator1, LabelGridZoomText, TrackBarZoom });
            toolStrip1.Location = new System.Drawing.Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new System.Drawing.Size(800, 33);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // BtnTgOk
            // 
            BtnTgOk.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            BtnTgOk.Image = Properties.Resources.Apply;
            BtnTgOk.ImageTransparentColor = System.Drawing.Color.Magenta;
            BtnTgOk.Name = "BtnTgOk";
            BtnTgOk.Size = new System.Drawing.Size(23, 30);
            BtnTgOk.Text = "Save";
            BtnTgOk.Click += BtnTgOk_Click;
            // 
            // BtnTgCancel
            // 
            BtnTgCancel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            BtnTgCancel.Image = Properties.Resources.Delete;
            BtnTgCancel.ImageTransparentColor = System.Drawing.Color.Magenta;
            BtnTgCancel.Name = "BtnTgCancel";
            BtnTgCancel.Size = new System.Drawing.Size(23, 30);
            BtnTgCancel.Text = "Cancel";
            BtnTgCancel.Click += BtnTgCancel_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new System.Drawing.Size(6, 33);
            // 
            // LabelGridZoomText
            // 
            LabelGridZoomText.Name = "LabelGridZoomText";
            LabelGridZoomText.Size = new System.Drawing.Size(42, 30);
            LabelGridZoomText.Text = "Zoom:";
            // 
            // TrackBarZoom
            // 
            TrackBarZoom.Name = "TrackBarZoom";
            TrackBarZoom.Size = new System.Drawing.Size(200, 30);
            TrackBarZoom.Text = "Zoom";
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { toolStripStatusLabelMSForm });
            statusStrip1.Location = new System.Drawing.Point(0, 428);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new System.Drawing.Size(800, 22);
            statusStrip1.TabIndex = 2;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelMSForm
            // 
            toolStripStatusLabelMSForm.Name = "toolStripStatusLabelMSForm";
            toolStripStatusLabelMSForm.Size = new System.Drawing.Size(12, 17);
            toolStripStatusLabelMSForm.Text = "-";
            // 
            // Form_TagImagesGrid
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(flowLayoutPanelImages);
            Controls.Add(labelActiveTag);
            Controls.Add(panelTagList);
            Controls.Add(statusStrip1);
            Controls.Add(toolStrip1);
            Name = "Form_TagImagesGrid";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Multi-select tag editor";
            WindowState = System.Windows.Forms.FormWindowState.Maximized;
            Load += Form_TagImagesGrid_Load;
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton BtnTgOk;
        private System.Windows.Forms.ToolStripButton BtnTgCancel;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private ToolStripCustomMenuItem TrackBarZoom;
        public System.Windows.Forms.FlowLayoutPanel flowLayoutPanelImages;
        private System.Windows.Forms.Panel panelTagList;
        private System.Windows.Forms.ListBox listBoxTags;
        private System.Windows.Forms.Label labelTagListHeader;
        private System.Windows.Forms.Label labelActiveTag;
        private System.Windows.Forms.ToolStripLabel LabelGridZoomText;
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabelMSForm;
    }
}