using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

internal static class ReadWriteOperations
{
    /// <summary>
    /// Reads the GSTINs out of an input file, converting spreadsheets to CSV first.
    /// </summary>
    internal static async Task<string[]> GetGstIdsAsync(string filePath)
    {
        if (!Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            // Convert_To_CSV.py always writes beside the executable as output.csv, so the
            // conversion is only safe one file at a time. A batch runs sequentially, and
            // the CSV is fully read below before the next file is converted.
            ExcelManager.ConvertToCSV(filePath);
            filePath = Path.Combine(AppContext.BaseDirectory, "output.csv");

            if (!File.Exists(filePath))
            {
                // Older behaviour looked in the working directory; keep that as a fallback
                // so running from a different folder still finds the converted file.
                filePath = "output.csv";
            }
        }

        var inputs = await File.ReadAllTextAsync(filePath);
        return inputs.Split().Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }

    /// <summary>
    /// Saves the workbook, asking the front end whether to retry when the file is locked -
    /// almost always because it is open in Excel.
    ///
    /// This replaces a loop that retried forever with no way out, which would have hung a
    /// GUI with no visible cause.
    /// </summary>
    /// <returns>True when the workbook reached disk.</returns>
    internal static async Task<bool> SaveWorkbookAsync(
        XLWorkbook workbook,
        string outputFile,
        IScrapeUi ui,
        CancellationToken token)
    {
        bool announced = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                string? directory = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                workbook.SaveAs(outputFile);

                if (!announced)
                {
                    Logger.Debug($"Saved progress to {outputFile}");
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                announced = true;

                bool retry = await ui.RetrySaveAsync(outputFile, ex.Message, token).ConfigureAwait(false);
                if (!retry)
                {
                    Logger.Error($"Gave up saving '{outputFile}'.", ex);
                    return false;
                }

                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected failure saving '{outputFile}'.", ex);
                return false;
            }
        }

        return false;
    }
}
