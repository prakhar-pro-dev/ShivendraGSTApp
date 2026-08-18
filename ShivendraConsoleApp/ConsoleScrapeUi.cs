using ShivendraGst.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraConsoleApp;

/// <summary>
/// Console front end for the scraping engine. Keeps the behaviour the app had before the
/// WinForms split: a y/n prompt that continues on its own after the configured timeout,
/// and a save retry that waits for the operator to close Excel.
/// </summary>
public sealed class ConsoleScrapeUi : IScrapeUi
{
    private string _lastFile = string.Empty;

    public void ReportProgress(ScrapeProgress progress)
    {
        if (!string.Equals(_lastFile, progress.InputFile, StringComparison.OrdinalIgnoreCase))
        {
            _lastFile = progress.InputFile;
            Logger.Info($"[{progress.FileNumber}/{progress.FileCount}] {progress.InputFile}");
        }
    }

    public async Task<bool> ConfirmSkipInvalidIdAsync(string gstin, string errorText, CancellationToken cancellationToken)
    {
        int timeoutSeconds = AppConfig.TimeoutForInvalidId;

        Logger.Warning($"GSTIN Not Found for id - {gstin}\tError - {errorText}");
        Logger.Prompt($"Do you want to skip? [y/n] (continues automatically after {timeoutSeconds}s) ");

        // Console.ReadLine cannot be cancelled, so the read is raced against a timer and
        // the process moves on when the timer wins. The orphaned read stays pending and
        // will consume the operator's next line - the same trade-off the app made before.
        Task<string?> read = Task.Run(() => Console.ReadLine(), CancellationToken.None);
        Task delay = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);

        Task winner = await Task.WhenAny(read, delay).ConfigureAwait(false);

        string? answer = winner == read ? await read.ConfigureAwait(false) : null;

        Console.WriteLine();
        Logger.PromptResponse(answer);

        // Anything other than an explicit "n" continues to the next id, so an unattended
        // run never stalls on a bad GSTIN.
        return !string.Equals(answer?.Trim(), "n", StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> RetrySaveAsync(string outputFile, string reason, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        Logger.Warning($"Could not save '{outputFile}' ({reason}). Close the file in Excel - retrying.");
        return Task.FromResult(true);
    }
}
