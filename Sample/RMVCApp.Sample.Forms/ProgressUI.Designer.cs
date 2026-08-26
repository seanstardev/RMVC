namespace RMVCApp.Forms {
    partial class ProgressUI {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            progressBar = new ProgressBar();
            label1 = new Label();
            titleLabel = new Label();
            label2 = new Label();
            actionLabel = new Label();
            percentLabel = new Label();
            SuspendLayout();
            // 
            // progressBar
            // 
            progressBar.Location = new Point(5, 21);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(520, 23);
            progressBar.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(3, 3);
            label1.Name = "label1";
            label1.Size = new Size(32, 15);
            label1.TabIndex = 1;
            label1.Text = "Task:";
            // 
            // titleLabel
            // 
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(54, 3);
            titleLabel.Name = "titleLabel";
            titleLabel.Size = new Size(16, 15);
            titleLabel.TabIndex = 2;
            titleLabel.Text = "...";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(3, 47);
            label2.Name = "label2";
            label2.Size = new Size(45, 15);
            label2.TabIndex = 3;
            label2.Text = "Action:";
            // 
            // actionLabel
            // 
            actionLabel.AutoSize = true;
            actionLabel.Location = new Point(54, 47);
            actionLabel.Name = "actionLabel";
            actionLabel.Size = new Size(16, 15);
            actionLabel.TabIndex = 4;
            actionLabel.Text = "...";
            // 
            // percentLabel
            // 
            percentLabel.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            percentLabel.Location = new Point(531, 24);
            percentLabel.Name = "percentLabel";
            percentLabel.Size = new Size(52, 20);
            percentLabel.TabIndex = 5;
            percentLabel.Text = "100%";
            // 
            // ProgressUI
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(percentLabel);
            Controls.Add(actionLabel);
            Controls.Add(label2);
            Controls.Add(titleLabel);
            Controls.Add(label1);
            Controls.Add(progressBar);
            Name = "ProgressUI";
            Size = new Size(586, 66);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ProgressBar progressBar;
        private Label label1;
        private Label titleLabel;
        private Label label2;
        private Label actionLabel;
        private Label percentLabel;
    }
}
