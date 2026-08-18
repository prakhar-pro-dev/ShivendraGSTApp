using System;
using System.Windows.Forms;

namespace ShivendraGstWinApp;

/// <summary>
/// "GSTIN not found - skip it?" with a countdown. The console front end continues on its
/// own after the configured timeout; this does the same, so a long unattended batch is
/// never blocked by a modal dialog nobody is watching.
///
/// DialogResult.Yes (or the timeout) means move on; DialogResult.No means keep waiting on
/// this id.
/// </summary>
internal sealed class SkipPromptForm : Form
{
    private readonly Label _message;
    private readonly Label _countdown;
    private readonly Timer _timer;
    private int _secondsLeft;

    internal SkipPromptForm(string gstin, string errorText, int timeoutSeconds)
    {
        _secondsLeft = timeoutSeconds > 0 ? timeoutSeconds : 1;

        Text = "GSTIN not found";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new System.Drawing.Size(460, 170);

        _message = new Label
        {
            AutoSize = false,
            Location = new System.Drawing.Point(15, 15),
            Size = new System.Drawing.Size(430, 70),
            Text = $"{gstin}{Environment.NewLine}{Environment.NewLine}{errorText}"
        };

        _countdown = new Label
        {
            AutoSize = false,
            Location = new System.Drawing.Point(15, 92),
            Size = new System.Drawing.Size(430, 20)
        };

        var skip = new Button
        {
            Text = "Skip",
            DialogResult = DialogResult.Yes,
            Location = new System.Drawing.Point(250, 125),
            Size = new System.Drawing.Size(90, 30)
        };

        var wait = new Button
        {
            Text = "Keep waiting",
            DialogResult = DialogResult.No,
            Location = new System.Drawing.Point(350, 125),
            Size = new System.Drawing.Size(95, 30)
        };

        Controls.Add(_message);
        Controls.Add(_countdown);
        Controls.Add(skip);
        Controls.Add(wait);

        AcceptButton = skip;
        CancelButton = wait;

        _timer = new Timer { Interval = 1000 };
        _timer.Tick += OnTick;

        UpdateCountdown();
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _secondsLeft--;

        if (_secondsLeft <= 0)
        {
            _timer.Stop();
            DialogResult = DialogResult.Yes;
            Close();
            return;
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        _countdown.Text = $"Skipping automatically in {_secondsLeft}s...";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
