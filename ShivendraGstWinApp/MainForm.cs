using ShivendraGst.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShivendraGstWinApp;

/// <summary>
/// Front end for the batch scraper: pick a folder of input files, pick where the workbooks
/// go, watch it work. The scraping itself is ShivendraGst.Core, the same engine the console
/// app runs, so the two cannot drift apart.
/// </summary>
public partial class MainForm : Form
{
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<string> _discovered = Array.Empty<string>();

    private const int MaxLogLines = 2000;

    public MainForm()
    {
        InitializeComponent();
    }

    private void OnFormLoad(object? sender, EventArgs e)
    {
        // Seed the boxes from configFile.json so the usual run is one click.
        string configuredInput = AppConfig.InputPath;
        if (!string.IsNullOrWhiteSpace(configuredInput))
        {
            txtInputFolder.Text = InputFiles.IsDirectory(configuredInput)
                ? configuredInput
                : Path.GetDirectoryName(configuredInput) ?? configuredInput;
        }

        txtOutputFolder.Text = AppConfig.OutputPath ?? AppContext.BaseDirectory;

        Logger.MessageWritten += OnLogMessage;
        Logger.Info($"Ready. Log file: {Logger.LogFilePath}");

        RefreshFileList();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_cancellation is not null && !_cancellation.IsCancellationRequested)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "A run is still in progress. Stop it and close?",
                "GST Inspect",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            _cancellation.Cancel();
        }

        Logger.MessageWritten -= OnLogMessage;
    }

    #region Folder selection

    private void OnBrowseInput(object? sender, EventArgs e)
    {
        string? chosen = BrowseForFolder("Select the folder containing the input files", txtInputFolder.Text);
        if (chosen is not null) txtInputFolder.Text = chosen;
    }

    private void OnBrowseOutput(object? sender, EventArgs e)
    {
        string? chosen = BrowseForFolder("Select the folder for the generated workbooks", txtOutputFolder.Text);
        if (chosen is not null) txtOutputFolder.Text = chosen;
    }

    private string? BrowseForFolder(string description, string current)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void OnInputFolderChanged(object? sender, EventArgs e) => RefreshFileList();

    /// <summary>
    /// Shows what a run would actually process, so the operator sees the batch before
    /// starting it rather than discovering it in the log.
    /// </summary>
    private void RefreshFileList()
    {
        lstFiles.Items.Clear();
        _discovered = Array.Empty<string>();

        string folder = txtInputFolder.Text.Trim();

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            lblFiles.Text = "Files to process";
            btnStart.Enabled = false;
            return;
        }

        try
        {
            _discovered = InputFiles.Discover(folder);
        }
        catch (Exception ex)
        {
            lblFiles.Text = $"Files to process - {ex.Message}";
            btnStart.Enabled = false;
            return;
        }

        foreach (string file in _discovered)
        {
            lstFiles.Items.Add(Path.GetFileName(file));
        }

        lblFiles.Text = $"Files to process ({_discovered.Count})";
        btnStart.Enabled = _discovered.Count > 0;
    }

    #endregion

    #region Running

    private async void OnStart(object? sender, EventArgs e)
    {
        if (_discovered.Count == 0)
        {
            MessageBox.Show(this, "No supported input files were found in that folder.",
                "GST Inspect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string outputFolder = txtOutputFolder.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            MessageBox.Show(this, "Choose an output folder first.",
                "GST Inspect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetRunning(true);
        _cancellation = new CancellationTokenSource();

        try
        {
            IReadOnlyList<ScrapeFileResult> results = await GstBatchRunner.RunAsync(
                _discovered,
                outputFolder,
                new FormScrapeUi(this),
                _cancellation.Token);

            ReportOutcome(results);
        }
        catch (Exception ex)
        {
            Logger.Error("The run failed.", ex);
            MessageBox.Show(this, ex.Message, "GST Inspect", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            SetRunning(false);
        }
    }

    private void OnCancel(object? sender, EventArgs e)
    {
        if (_cancellation is null) return;

        Logger.Warning("Cancellation requested - finishing the current id and stopping.");
        btnCancel.Enabled = false;
        _cancellation.Cancel();
    }

    private void ReportOutcome(IReadOnlyList<ScrapeFileResult> results)
    {
        int saved = 0;
        foreach (ScrapeFileResult result in results)
        {
            if (result.Saved) saved++;
        }

        lblStatus.Text = $"Finished. {saved} of {results.Count} file(s) written.";
        progressBar.Value = progressBar.Maximum;

        MessageBoxIcon icon = saved == results.Count ? MessageBoxIcon.Information : MessageBoxIcon.Warning;

        MessageBox.Show(
            this,
            $"{saved} of {results.Count} file(s) written.{Environment.NewLine}{Environment.NewLine}Log: {Logger.LogFilePath}",
            "GST Inspect",
            MessageBoxButtons.OK,
            icon);
    }

    private void SetRunning(bool running)
    {
        btnStart.Enabled = !running && _discovered.Count > 0;
        btnCancel.Enabled = running;
        btnBrowseInput.Enabled = !running;
        btnBrowseOutput.Enabled = !running;
        txtInputFolder.Enabled = !running;
        txtOutputFolder.Enabled = !running;

        if (running)
        {
            progressBar.Value = 0;
            lblStatus.Text = "Starting...";
        }
    }

    #endregion

    #region Cross-thread updates

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Progress and log callbacks can
    /// arrive on a Playwright or thread-pool thread, so nothing may touch a control
    /// directly. BeginInvoke rather than Invoke keeps the scrape from blocking on the UI.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // The form closed mid-run; nothing left to update.
        }
    }

    private void OnLogMessage(LogLevel level, string message)
    {
        OnUiThread(() =>
        {
            string prefix = level switch
            {
                LogLevel.Error => "ERROR   ",
                LogLevel.Warning => "WARNING ",
                _ => "INFO    "
            };

            lstLog.Items.Add(prefix + message);

            // Keep the list bounded; a long batch would otherwise grow it without limit.
            while (lstLog.Items.Count > MaxLogLines)
            {
                lstLog.Items.RemoveAt(0);
            }

            lstLog.TopIndex = lstLog.Items.Count - 1;
        });
    }

    internal void ShowProgress(ScrapeProgress progress)
    {
        OnUiThread(() =>
        {
            progressBar.Value = Math.Clamp(progress.OverallPercent, progressBar.Minimum, progressBar.Maximum);

            lblStatus.Text =
                $"File {progress.FileNumber}/{progress.FileCount}: {Path.GetFileName(progress.InputFile)}  -  " +
                $"id {progress.IdNumber}/{progress.IdCount} ({progress.CurrentId})";
        });
    }

    /// <summary>
    /// Asks whether to skip a GSTIN the site could not find, closing itself and continuing
    /// after the configured timeout so an unattended batch is never stuck on a dialog.
    /// </summary>
    internal Task<bool> AskSkipAsync(string gstin, string errorText)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        OnUiThread(() =>
        {
            using var prompt = new SkipPromptForm(gstin, errorText, AppConfig.TimeoutForInvalidId);
            DialogResult result = prompt.ShowDialog(this);

            // Anything but an explicit "keep waiting" moves on, matching the console.
            completion.TrySetResult(result != DialogResult.No);
        });

        return completion.Task;
    }

    internal Task<bool> AskRetrySaveAsync(string outputFile, string reason)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        OnUiThread(() =>
        {
            DialogResult result = MessageBox.Show(
                this,
                $"Could not write:{Environment.NewLine}{outputFile}{Environment.NewLine}{Environment.NewLine}" +
                $"{reason}{Environment.NewLine}{Environment.NewLine}" +
                "It is usually open in Excel. Close it and retry?",
                "GST Inspect",
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Warning);

            completion.TrySetResult(result == DialogResult.Retry);
        });

        return completion.Task;
    }

    #endregion
}
