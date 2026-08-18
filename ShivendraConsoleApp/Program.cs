using ShivendraGst.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraConsoleApp;

/// <summary>
/// Command-line front end. All the scraping now lives in ShivendraGst.Core, which the
/// WinForms app drives the same way, so the two front ends cannot drift apart.
///
/// The path prompt accepts a folder as well as a single file: a folder is expanded into
/// every supported input file inside it, and each one produces its own workbook.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        AppConfig.EnsureLoaded();

        using var cancellation = new CancellationTokenSource();

        // Ctrl+C stops the batch cleanly - the file in flight still saves what it collected -
        // rather than killing the process mid-write.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Logger.Warning("Cancellation requested - finishing the current id and stopping.");
            cancellation.Cancel();
        };

        try
        {
            string? path = args.Length > 0 ? args[0] : PromptForPath();

            if (string.IsNullOrWhiteSpace(path))
            {
                path = AppConfig.InputPath;

                if (string.IsNullOrWhiteSpace(path))
                {
                    Logger.Error("No input path was given and configFile.json has no inputPath.");
                    return 1;
                }

                Logger.Info($"No path entered, using the configured input path - {path}");
            }

            IReadOnlyList<string> inputFiles = InputFiles.Discover(path);

            if (inputFiles.Count == 0)
            {
                Logger.Error($"No supported input files ({string.Join(", ", InputFiles.SupportedInputExtensions)}) were found in '{path}'.");
                return 1;
            }

            Logger.Info($"{inputFiles.Count} input file(s) to process.");

            string outputDirectory = AppConfig.OutputPath ?? AppContext.BaseDirectory;

            IReadOnlyList<ScrapeFileResult> results = await GstBatchRunner
                .RunAsync(inputFiles, outputDirectory, new ConsoleScrapeUi(), cancellation.Token)
                .ConfigureAwait(false);

            foreach (ScrapeFileResult result in results)
            {
                if (!result.Saved) return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.Error("An error occurred in Main.", ex);
            return 1;
        }
        finally
        {
            Logger.Shutdown();
        }
    }

    private static string? PromptForPath()
    {
        Logger.Prompt("Enter a file or folder path - ");
        string? path = Console.ReadLine();
        Logger.PromptResponse(path);

        return path?.Trim().Trim('"');
    }
}
