using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

/// <summary>
/// The entry point every front end uses. Launches one visible Chrome for the whole batch -
/// the operator solves each captcha in it - and runs the input files through it one at a
/// time, writing one workbook per input file.
/// </summary>
public static class GstBatchRunner
{
    /// <summary>Where Chrome is expected. Overridable for machines with a different install.</summary>
    public static string ChromeExecutablePath { get; set; } = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    /// <summary>
    /// Runs every file in <paramref name="inputFiles"/>, writing results into
    /// <paramref name="outputDirectory"/>.
    ///
    /// Files are processed sequentially and share a single browser, so the operator sees one
    /// Chrome window for the whole batch. A failure in one file is recorded and the batch
    /// continues with the next.
    /// </summary>
    /// <param name="inputFiles">Files to process, as produced by <see cref="InputFiles.Discover"/>.</param>
    /// <param name="outputDirectory">Folder for the generated workbooks. Created if missing.</param>
    /// <param name="ui">Front end handling progress and the questions the run has to ask.</param>
    /// <param name="cancellationToken">Stops the batch; the file in flight still saves what it has.</param>
    public static async Task<IReadOnlyList<ScrapeFileResult>> RunAsync(
        IReadOnlyList<string> inputFiles,
        string outputDirectory,
        IScrapeUi ui,
        CancellationToken cancellationToken)
    {
        if (inputFiles is null) throw new ArgumentNullException(nameof(inputFiles));
        if (ui is null) throw new ArgumentNullException(nameof(ui));

        AppConfig.EnsureLoaded();

        var results = new List<ScrapeFileResult>();

        if (inputFiles.Count == 0)
        {
            Logger.Warning("Nothing to do - no supported input files were found.");
            return results;
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = AppConfig.OutputPath ?? AppContext.BaseDirectory;
        }

        Directory.CreateDirectory(outputDirectory);

        Logger.Info($"Starting batch: {inputFiles.Count} file(s) -> {outputDirectory}");

        if (!File.Exists(ChromeExecutablePath))
        {
            Logger.Error($"Chrome was not found at '{ChromeExecutablePath}'. Set GstBatchRunner.ChromeExecutablePath to the correct location.");
            return results;
        }

        using IPlaywright playwright = await Playwright.CreateAsync().ConfigureAwait(false);

        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            ExecutablePath = ChromeExecutablePath
        }).ConfigureAwait(false);

        IPage page = await browser.NewPageAsync().ConfigureAwait(false);

        try
        {
            for (int i = 0; i < inputFiles.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Logger.Warning($"Cancelled - {inputFiles.Count - i} file(s) not processed.");
                    break;
                }

                string inputFile = inputFiles[i];
                string outputFile = InputFiles.BuildOutputPath(inputFile, outputDirectory);

                var scraper = new GstScraper(page, ui, inputFile, outputFile, i + 1, inputFiles.Count);

                try
                {
                    results.Add(await scraper.RunAsync(cancellationToken).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    // One bad file must not abandon the rest of the batch.
                    Logger.Error($"'{inputFile}' failed and was skipped.", ex);
                    results.Add(new ScrapeFileResult(inputFile, outputFile, 0, false, ex.Message));
                }
            }
        }
        finally
        {
            try
            {
                await browser.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Debug($"Closing the browser failed: {ex.Message}");
            }
        }

        LogSummary(results);
        return results;
    }

    private static void LogSummary(IReadOnlyList<ScrapeFileResult> results)
    {
        int saved = 0;
        foreach (ScrapeFileResult result in results)
        {
            if (result.Saved) saved++;
        }

        Logger.Info($"Batch finished: {saved} of {results.Count} file(s) written.");

        foreach (ScrapeFileResult result in results)
        {
            if (!result.Saved)
            {
                Logger.Warning($"  not written: {result.InputFile} ({result.FailureReason ?? "unknown reason"})");
            }
        }
    }
}
