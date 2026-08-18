using System.Windows.Forms;

namespace ShivendraGstWinApp;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    private Label lblInput = null!;
    private TextBox txtInputFolder = null!;
    private Button btnBrowseInput = null!;

    private Label lblOutput = null!;
    private TextBox txtOutputFolder = null!;
    private Button btnBrowseOutput = null!;

    private Label lblFiles = null!;
    private ListBox lstFiles = null!;

    private Button btnStart = null!;
    private Button btnCancel = null!;
    private ProgressBar progressBar = null!;
    private Label lblStatus = null!;

    private Label lblLog = null!;
    private ListBox lstLog = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        lblInput = new Label();
        txtInputFolder = new TextBox();
        btnBrowseInput = new Button();
        lblOutput = new Label();
        txtOutputFolder = new TextBox();
        btnBrowseOutput = new Button();
        lblFiles = new Label();
        lstFiles = new ListBox();
        btnStart = new Button();
        btnCancel = new Button();
        progressBar = new ProgressBar();
        lblStatus = new Label();
        lblLog = new Label();
        lstLog = new ListBox();

        SuspendLayout();

        // Input folder
        lblInput.AutoSize = true;
        lblInput.Location = new System.Drawing.Point(14, 18);
        lblInput.Name = nameof(lblInput);
        lblInput.Text = "Input folder";

        txtInputFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtInputFolder.Location = new System.Drawing.Point(110, 15);
        txtInputFolder.Name = nameof(txtInputFolder);
        txtInputFolder.Size = new System.Drawing.Size(640, 23);
        txtInputFolder.TextChanged += OnInputFolderChanged;

        btnBrowseInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseInput.Location = new System.Drawing.Point(760, 14);
        btnBrowseInput.Name = nameof(btnBrowseInput);
        btnBrowseInput.Size = new System.Drawing.Size(100, 25);
        btnBrowseInput.Text = "Browse...";
        btnBrowseInput.UseVisualStyleBackColor = true;
        btnBrowseInput.Click += OnBrowseInput;

        // Output folder
        lblOutput.AutoSize = true;
        lblOutput.Location = new System.Drawing.Point(14, 51);
        lblOutput.Name = nameof(lblOutput);
        lblOutput.Text = "Output folder";

        txtOutputFolder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtOutputFolder.Location = new System.Drawing.Point(110, 48);
        txtOutputFolder.Name = nameof(txtOutputFolder);
        txtOutputFolder.Size = new System.Drawing.Size(640, 23);

        btnBrowseOutput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnBrowseOutput.Location = new System.Drawing.Point(760, 47);
        btnBrowseOutput.Name = nameof(btnBrowseOutput);
        btnBrowseOutput.Size = new System.Drawing.Size(100, 25);
        btnBrowseOutput.Text = "Browse...";
        btnBrowseOutput.UseVisualStyleBackColor = true;
        btnBrowseOutput.Click += OnBrowseOutput;

        // Discovered files
        lblFiles.AutoSize = true;
        lblFiles.Location = new System.Drawing.Point(14, 86);
        lblFiles.Name = nameof(lblFiles);
        lblFiles.Text = "Files to process";

        lstFiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lstFiles.IntegralHeight = false;
        lstFiles.Location = new System.Drawing.Point(15, 106);
        lstFiles.Name = nameof(lstFiles);
        lstFiles.Size = new System.Drawing.Size(845, 110);

        // Run controls
        btnStart.Location = new System.Drawing.Point(15, 228);
        btnStart.Name = nameof(btnStart);
        btnStart.Size = new System.Drawing.Size(120, 32);
        btnStart.Text = "Start";
        btnStart.UseVisualStyleBackColor = true;
        btnStart.Click += OnStart;

        btnCancel.Enabled = false;
        btnCancel.Location = new System.Drawing.Point(145, 228);
        btnCancel.Name = nameof(btnCancel);
        btnCancel.Size = new System.Drawing.Size(120, 32);
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.Click += OnCancel;

        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new System.Drawing.Point(280, 232);
        progressBar.Name = nameof(progressBar);
        progressBar.Size = new System.Drawing.Size(580, 24);

        lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblStatus.AutoEllipsis = true;
        lblStatus.Location = new System.Drawing.Point(15, 268);
        lblStatus.Name = nameof(lblStatus);
        lblStatus.Size = new System.Drawing.Size(845, 20);
        lblStatus.Text = "Idle.";

        // Log
        lblLog.AutoSize = true;
        lblLog.Location = new System.Drawing.Point(14, 296);
        lblLog.Name = nameof(lblLog);
        lblLog.Text = "Log";

        lstLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        lstLog.HorizontalScrollbar = true;
        lstLog.IntegralHeight = false;
        lstLog.Location = new System.Drawing.Point(15, 316);
        lstLog.Name = nameof(lstLog);
        lstLog.Size = new System.Drawing.Size(845, 290);

        // Form
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(874, 621);
        MinimumSize = new System.Drawing.Size(700, 500);
        Controls.Add(lblInput);
        Controls.Add(txtInputFolder);
        Controls.Add(btnBrowseInput);
        Controls.Add(lblOutput);
        Controls.Add(txtOutputFolder);
        Controls.Add(btnBrowseOutput);
        Controls.Add(lblFiles);
        Controls.Add(lstFiles);
        Controls.Add(btnStart);
        Controls.Add(btnCancel);
        Controls.Add(progressBar);
        Controls.Add(lblStatus);
        Controls.Add(lblLog);
        Controls.Add(lstLog);
        Name = nameof(MainForm);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "GST Inspect";
        Load += OnFormLoad;
        FormClosing += OnFormClosing;

        ResumeLayout(false);
        PerformLayout();
    }
}
