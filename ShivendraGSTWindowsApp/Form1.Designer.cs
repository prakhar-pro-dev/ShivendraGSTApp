using System.Drawing;
using System.Windows.Forms;

namespace ShivendraGSTWindowsApp;

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
        START = new Button();
        PathInput = new TextBox();
        SuspendLayout();
        // 
        // START
        // 
        START.AutoSize = true;
        START.Location = new Point(621, 111);
        START.Name = "START";
        START.Size = new Size(168, 74);
        START.TabIndex = 0;
        START.Text = "START";
        START.UseVisualStyleBackColor = true;
        START.Click += button1_Click;
        // 
        // PathInput
        // 
        PathInput.Location = new Point(169, 135);
        PathInput.Name = "PathInput";
        PathInput.Size = new Size(349, 27);
        PathInput.TabIndex = 1;
        PathInput.TextChanged += textBox1_TextChanged;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(8F, 20F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1203, 823);
        Controls.Add(PathInput);
        Controls.Add(START);
        Name = "Form1";
        Text = "Form1";
        Load += Form1_Load;
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private Button START;
    private TextBox PathInput;
}