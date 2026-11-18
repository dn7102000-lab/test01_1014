namespace test01_1014
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            menuStrip1 = new MenuStrip();
            fiieToolStripMenuItem = new ToolStripMenuItem();
            openToolStripMenuItem = new ToolStripMenuItem();
            saveToolStripMenuItem = new ToolStripMenuItem();
            saveToolStripMenuItem1 = new ToolStripMenuItem();
            saveAsToolStripMenuItem = new ToolStripMenuItem();
            toolStripMenuItem1 = new ToolStripMenuItem();
            shapeToolStripMenuItem = new ToolStripMenuItem();
            lineToolStripMenuItem1 = new ToolStripMenuItem();
            rectangleToolStripMenuItem = new ToolStripMenuItem();
            circleToolStripMenuItem = new ToolStripMenuItem();
            textToolStripMenuItem = new ToolStripMenuItem();
            imageToolStripMenuItem = new ToolStripMenuItem();
            viewToolStripMenuItem = new ToolStripMenuItem();
            originalToolStripMenuItem = new ToolStripMenuItem();
            editedToolStripMenuItem = new ToolStripMenuItem();
            laserPathToolStripMenuItem = new ToolStripMenuItem();
            Grid = new ToolStripMenuItem();
            shareToolStripMenuItem = new ToolStripMenuItem();
            exportGCodeToolStripMenuItem = new ToolStripMenuItem();
            splitContainer1 = new SplitContainer();
            textpanel = new Panel();
            label9 = new Label();
            label8 = new Label();
            label7 = new Label();
            cmbFontName = new ComboBox();
            numFontSize = new NumericUpDown();
            txtContent = new TextBox();
            label3 = new Label();
            panelProperties = new Panel();
            label6 = new Label();
            label5 = new Label();
            label4 = new Label();
            label2 = new Label();
            label1 = new Label();
            numHeight = new NumericUpDown();
            numWidth = new NumericUpDown();
            Blur = new Label();
            trackBarBlur = new TrackBar();
            Canny = new Label();
            trackBarThreshold = new TrackBar();
            splitContainer2 = new SplitContainer();
            pictureBox1 = new CanvasBox();
            contextMenuStrip1 = new ContextMenuStrip(components);
            menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            textpanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numFontSize).BeginInit();
            panelProperties.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numHeight).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numWidth).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBarBlur).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBarThreshold).BeginInit();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(24, 24);
            menuStrip1.Items.AddRange(new ToolStripItem[] { fiieToolStripMenuItem, toolStripMenuItem1, viewToolStripMenuItem, shareToolStripMenuItem, exportGCodeToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1220, 38);
            menuStrip1.TabIndex = 3;
            menuStrip1.Text = "menuStrip1";
            menuStrip1.ItemClicked += menuStrip1_ItemClicked;
            // 
            // fiieToolStripMenuItem
            // 
            fiieToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { openToolStripMenuItem, saveToolStripMenuItem, saveToolStripMenuItem1, saveAsToolStripMenuItem });
            fiieToolStripMenuItem.Name = "fiieToolStripMenuItem";
            fiieToolStripMenuItem.Size = new Size(60, 34);
            fiieToolStripMenuItem.Text = "Fiie";
            fiieToolStripMenuItem.Click += fiieToolStripMenuItem_Click;
            // 
            // openToolStripMenuItem
            // 
            openToolStripMenuItem.Name = "openToolStripMenuItem";
            openToolStripMenuItem.Size = new Size(188, 38);
            openToolStripMenuItem.Text = "New";
            openToolStripMenuItem.Click += NewtoolStripMenuItem1_Click;
            // 
            // saveToolStripMenuItem
            // 
            saveToolStripMenuItem.Name = "saveToolStripMenuItem";
            saveToolStripMenuItem.Size = new Size(188, 38);
            saveToolStripMenuItem.Text = "Open";
            saveToolStripMenuItem.Click += openToolStripMenuItem_Click;
            // 
            // saveToolStripMenuItem1
            // 
            saveToolStripMenuItem1.Name = "saveToolStripMenuItem1";
            saveToolStripMenuItem1.Size = new Size(188, 38);
            saveToolStripMenuItem1.Text = "Save";
            saveToolStripMenuItem1.Click += saveToolStripMenuItem1_Click;
            // 
            // saveAsToolStripMenuItem
            // 
            saveAsToolStripMenuItem.Name = "saveAsToolStripMenuItem";
            saveAsToolStripMenuItem.Size = new Size(188, 38);
            saveAsToolStripMenuItem.Text = "Save As";
            saveAsToolStripMenuItem.Click += saveToolStripMenuItem_Click;
            // 
            // toolStripMenuItem1
            // 
            toolStripMenuItem1.DropDownItems.AddRange(new ToolStripItem[] { shapeToolStripMenuItem, textToolStripMenuItem, imageToolStripMenuItem });
            toolStripMenuItem1.Name = "toolStripMenuItem1";
            toolStripMenuItem1.Size = new Size(64, 34);
            toolStripMenuItem1.Text = "Edit";
            toolStripMenuItem1.Click += toolStripMenuItem1_Click;
            // 
            // shapeToolStripMenuItem
            // 
            shapeToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { lineToolStripMenuItem1, rectangleToolStripMenuItem, circleToolStripMenuItem });
            shapeToolStripMenuItem.Name = "shapeToolStripMenuItem";
            shapeToolStripMenuItem.Size = new Size(173, 38);
            shapeToolStripMenuItem.Text = "shape";
            shapeToolStripMenuItem.Click += lineToolStripMenuItem_Click;
            // 
            // lineToolStripMenuItem1
            // 
            lineToolStripMenuItem1.Name = "lineToolStripMenuItem1";
            lineToolStripMenuItem1.Size = new Size(207, 38);
            lineToolStripMenuItem1.Text = "Line";
            lineToolStripMenuItem1.Click += lineToolStripMenuItem_Click;
            // 
            // rectangleToolStripMenuItem
            // 
            rectangleToolStripMenuItem.Name = "rectangleToolStripMenuItem";
            rectangleToolStripMenuItem.Size = new Size(207, 38);
            rectangleToolStripMenuItem.Text = "Rectangle";
            rectangleToolStripMenuItem.Click += tectangleToolStripMenuItem_Click;
            // 
            // circleToolStripMenuItem
            // 
            circleToolStripMenuItem.Name = "circleToolStripMenuItem";
            circleToolStripMenuItem.Size = new Size(207, 38);
            circleToolStripMenuItem.Text = "circle";
            circleToolStripMenuItem.Click += circleToolStripMenuItem_Click;
            // 
            // textToolStripMenuItem
            // 
            textToolStripMenuItem.Name = "textToolStripMenuItem";
            textToolStripMenuItem.Size = new Size(173, 38);
            textToolStripMenuItem.Text = "text";
            textToolStripMenuItem.Click += textToolStripMenuItem_Click;
            // 
            // imageToolStripMenuItem
            // 
            imageToolStripMenuItem.Name = "imageToolStripMenuItem";
            imageToolStripMenuItem.Size = new Size(173, 38);
            imageToolStripMenuItem.Text = "image";
            imageToolStripMenuItem.Click += imageToolStripMenuItem_Click;
            // 
            // viewToolStripMenuItem
            // 
            viewToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { originalToolStripMenuItem, editedToolStripMenuItem, laserPathToolStripMenuItem, Grid });
            viewToolStripMenuItem.Name = "viewToolStripMenuItem";
            viewToolStripMenuItem.Size = new Size(73, 34);
            viewToolStripMenuItem.Text = "View";
            // 
            // originalToolStripMenuItem
            // 
            originalToolStripMenuItem.Name = "originalToolStripMenuItem";
            originalToolStripMenuItem.Size = new Size(211, 38);
            originalToolStripMenuItem.Text = "Original";
            originalToolStripMenuItem.Click += originalToolStripMenuItem_Click;
            // 
            // editedToolStripMenuItem
            // 
            editedToolStripMenuItem.Name = "editedToolStripMenuItem";
            editedToolStripMenuItem.Size = new Size(211, 38);
            editedToolStripMenuItem.Text = "Edited";
            editedToolStripMenuItem.Click += editedToolStripMenuItem_Click;
            // 
            // laserPathToolStripMenuItem
            // 
            laserPathToolStripMenuItem.Name = "laserPathToolStripMenuItem";
            laserPathToolStripMenuItem.Size = new Size(211, 38);
            laserPathToolStripMenuItem.Text = "Laser Path";
            laserPathToolStripMenuItem.Click += laserPathToolStripMenuItem_Click;
            // 
            // Grid
            // 
            Grid.Name = "Grid";
            Grid.Size = new Size(211, 38);
            Grid.Text = "Grid";
            Grid.Click += Grid_Click;
            // 
            // shareToolStripMenuItem
            // 
            shareToolStripMenuItem.Name = "shareToolStripMenuItem";
            shareToolStripMenuItem.Size = new Size(81, 34);
            shareToolStripMenuItem.Text = "Share";
            // 
            // exportGCodeToolStripMenuItem
            // 
            exportGCodeToolStripMenuItem.Name = "exportGCodeToolStripMenuItem";
            exportGCodeToolStripMenuItem.Size = new Size(164, 34);
            exportGCodeToolStripMenuItem.Text = "Export G-Code";
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 38);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(textpanel);
            splitContainer1.Panel1.Controls.Add(panelProperties);
            splitContainer1.Panel1.Controls.Add(Blur);
            splitContainer1.Panel1.Controls.Add(trackBarBlur);
            splitContainer1.Panel1.Controls.Add(Canny);
            splitContainer1.Panel1.Controls.Add(trackBarThreshold);
            splitContainer1.Panel1.Paint += splitContainer1_Panel1_Paint_1;
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(splitContainer2);
            splitContainer1.Size = new Size(1220, 625);
            splitContainer1.SplitterDistance = 215;
            splitContainer1.TabIndex = 4;
            // 
            // textpanel
            // 
            textpanel.Controls.Add(label9);
            textpanel.Controls.Add(label8);
            textpanel.Controls.Add(label7);
            textpanel.Controls.Add(cmbFontName);
            textpanel.Controls.Add(numFontSize);
            textpanel.Controls.Add(txtContent);
            textpanel.Controls.Add(label3);
            textpanel.Location = new Point(3, 175);
            textpanel.Name = "textpanel";
            textpanel.Size = new Size(210, 281);
            textpanel.TabIndex = 11;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(21, 173);
            label9.Name = "label9";
            label9.Size = new Size(94, 30);
            label9.TabIndex = 6;
            label9.Text = "Text font";
            label9.Click += label9_Click;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(19, 108);
            label8.Name = "label8";
            label8.Size = new Size(91, 30);
            label8.TabIndex = 5;
            label8.Text = "Text size";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(19, 40);
            label7.Name = "label7";
            label7.Size = new Size(87, 30);
            label7.TabIndex = 4;
            label7.Text = "Content";
            // 
            // cmbFontName
            // 
            cmbFontName.FormattingEnabled = true;
            cmbFontName.Location = new Point(19, 206);
            cmbFontName.Name = "cmbFontName";
            cmbFontName.Size = new Size(182, 38);
            cmbFontName.TabIndex = 3;
            cmbFontName.SelectedIndexChanged += cmbFontName_SelectedIndexChanged;
            // 
            // numFontSize
            // 
            numFontSize.Location = new Point(21, 141);
            numFontSize.Name = "numFontSize";
            numFontSize.Size = new Size(180, 35);
            numFontSize.TabIndex = 2;
            numFontSize.ValueChanged += numFontSize_ValueChanged;
            // 
            // txtContent
            // 
            txtContent.Location = new Point(19, 73);
            txtContent.Name = "txtContent";
            txtContent.Size = new Size(150, 35);
            txtContent.TabIndex = 1;
            txtContent.TextChanged += txtContent_TextChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(15, 10);
            label3.Name = "label3";
            label3.Size = new Size(50, 30);
            label3.TabIndex = 0;
            label3.Text = "Text";
            // 
            // panelProperties
            // 
            panelProperties.Controls.Add(label6);
            panelProperties.Controls.Add(label5);
            panelProperties.Controls.Add(label4);
            panelProperties.Controls.Add(label2);
            panelProperties.Controls.Add(label1);
            panelProperties.Controls.Add(numHeight);
            panelProperties.Controls.Add(numWidth);
            panelProperties.Enabled = false;
            panelProperties.Location = new Point(2, 13);
            panelProperties.Name = "panelProperties";
            panelProperties.Size = new Size(211, 156);
            panelProperties.TabIndex = 10;
            panelProperties.Paint += panelProperties_Paint;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(13, 14);
            label6.Name = "label6";
            label6.Size = new Size(70, 30);
            label6.TabIndex = 13;
            label6.Text = "Shape";
            label6.Click += label6_Click;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(133, 88);
            label5.Name = "label5";
            label5.Size = new Size(49, 30);
            label5.TabIndex = 12;
            label5.Text = "mm";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(134, 47);
            label4.Name = "label4";
            label4.Size = new Size(49, 30);
            label4.TabIndex = 11;
            label4.Text = "mm";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(10, 82);
            label2.Name = "label2";
            label2.Size = new Size(80, 30);
            label2.TabIndex = 10;
            label2.Text = "Height:";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(13, 44);
            label1.Name = "label1";
            label1.Size = new Size(74, 30);
            label1.TabIndex = 0;
            label1.Text = "Width:";
            label1.Click += label1_Click_3;
            // 
            // numHeight
            // 
            numHeight.Location = new Point(91, 85);
            numHeight.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numHeight.Name = "numHeight";
            numHeight.Size = new Size(117, 35);
            numHeight.TabIndex = 9;
            numHeight.ValueChanged += numHeight_ValueChanged;
            // 
            // numWidth
            // 
            numWidth.Location = new Point(91, 44);
            numWidth.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numWidth.Name = "numWidth";
            numWidth.Size = new Size(117, 35);
            numWidth.TabIndex = 8;
            numWidth.ValueChanged += numericUpDown1_ValueChanged;
            // 
            // Blur
            // 
            Blur.AutoSize = true;
            Blur.Location = new Point(36, 576);
            Blur.Name = "Blur";
            Blur.Size = new Size(49, 30);
            Blur.TabIndex = 7;
            Blur.Text = "Blur";
            Blur.Click += label1_Click_2;
            // 
            // trackBarBlur
            // 
            trackBarBlur.Location = new Point(3, 537);
            trackBarBlur.Name = "trackBarBlur";
            trackBarBlur.Size = new Size(156, 69);
            trackBarBlur.TabIndex = 6;
            trackBarBlur.Scroll += trackBarBlur_Scroll;
            // 
            // Canny
            // 
            Canny.AutoSize = true;
            Canny.Location = new Point(42, 487);
            Canny.Name = "Canny";
            Canny.Size = new Size(71, 30);
            Canny.TabIndex = 5;
            Canny.Text = "Canny";
            Canny.Click += label1_Click_1;
            // 
            // trackBarThreshold
            // 
            trackBarThreshold.Location = new Point(9, 448);
            trackBarThreshold.Name = "trackBarThreshold";
            trackBarThreshold.Size = new Size(156, 69);
            trackBarThreshold.TabIndex = 4;
            trackBarThreshold.Scroll += trackBarThreshold_Scroll;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Fill;
            splitContainer2.Location = new Point(0, 0);
            splitContainer2.Name = "splitContainer2";
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(pictureBox1);
            splitContainer2.Size = new Size(1001, 625);
            splitContainer2.SplitterDistance = 620;
            splitContainer2.TabIndex = 0;
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 0);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(620, 625);
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            pictureBox1.Click += pictureBox1_Click_2;
            pictureBox1.Paint += pictureBox1_Paint;
            pictureBox1.MouseDown += pictureBox1_MouseDown;
            pictureBox1.MouseMove += pictureBox1_MouseMove;
            pictureBox1.MouseUp += pictureBox1_MouseUp;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.ImageScalingSize = new Size(24, 24);
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(61, 4);
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(12F, 30F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1220, 663);
            Controls.Add(splitContainer1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "Form1";
            Load += Form1_Load;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            textpanel.ResumeLayout(false);
            textpanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numFontSize).EndInit();
            panelProperties.ResumeLayout(false);
            panelProperties.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numHeight).EndInit();
            ((System.ComponentModel.ISupportInitialize)numWidth).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBarBlur).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBarThreshold).EndInit();
            splitContainer2.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fiieToolStripMenuItem;
        private ToolStripMenuItem toolStripMenuItem1;
        private ToolStripMenuItem openToolStripMenuItem;
        private ToolStripMenuItem saveToolStripMenuItem;
        private ToolStripMenuItem viewToolStripMenuItem;
        private ToolStripMenuItem shareToolStripMenuItem;
        private ToolStripMenuItem saveToolStripMenuItem1;
        private ToolStripMenuItem saveAsToolStripMenuItem;
        private ToolStripMenuItem originalToolStripMenuItem;
        private ToolStripMenuItem editedToolStripMenuItem;
        private ToolStripMenuItem laserPathToolStripMenuItem;
        private ToolStripMenuItem exportGCodeToolStripMenuItem;
        private SplitContainer splitContainer1;
        private SplitContainer splitContainer2;
        private Label Canny;
        private TrackBar trackBarThreshold;
        private Label Blur;
        private TrackBar trackBarBlur;
        private Button button2;
        private ToolStripMenuItem shapeToolStripMenuItem;
        private ToolStripMenuItem textToolStripMenuItem;
        private ToolStripMenuItem imageToolStripMenuItem;
        private Panel panelProperties;
        private Label label2;
        private Label label1;
        private NumericUpDown numHeight;
        private NumericUpDown numWidth;
        private Panel textpanel;
        private TextBox txtContent;
        private Label label3;
        private ComboBox cmbFontName;
        private NumericUpDown numFontSize;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem lineToolStripMenuItem1;
        private ToolStripMenuItem rectangleToolStripMenuItem;
        private ToolStripMenuItem circleToolStripMenuItem;
        private ToolStripMenuItem Grid;
        private Label label5;
        private Label label4;
        private CanvasBox pictureBox1;
        private Label label6;
        private Label label9;
        private Label label8;
        private Label label7;
    }
}
