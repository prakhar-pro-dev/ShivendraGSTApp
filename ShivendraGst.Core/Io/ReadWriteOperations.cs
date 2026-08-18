using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

internal static class ReadWriteOperations
{
    /// <summary>
    /// Reads the GSTINs out of an input file. Every supported format is handled in-process:
    /// .xlsx/.xlsm with ClosedXML, legacy .xls with ExcelDataReader, .csv as plain text.
    ///
    /// This replaced a shell-out to Convert_To_CSV.py, which made Python plus pandas, xlrd
    /// and openpyxl a prerequisite on every machine, wrote a shared output.csv beside the
    /// executable, and failed in ways that were hard to attribute.
    /// </summary>
    internal static async Task<string[]> GetGstIdsAsync(string filePath)
    {
        string extension = Path.GetExtension(filePath);

        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            return ReadIdsFromWorkbook(filePath);
        }

        if (extension.Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return ReadIdsFromLegacyWorkbook(filePath);
        }

        var inputs = await File.ReadAllTextAsync(filePath);
        return inputs.Split().Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }

    /// <summary>
    /// Reads a legacy BIFF .xls, which ClosedXML cannot open. Same cell-by-cell approach as
    /// the modern path, so the two formats yield the same ids.
    /// </summary>
    private static string[] ReadIdsFromLegacyWorkbook(string filePath)
    {
        // .xls predates UTF-8; without this ExcelDataReader cannot resolve the legacy
        // codepages those files declare.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        using FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using ExcelDataReader.IExcelDataReader reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);

        var ids = new List<string>();

        // First worksheet only, matching the modern path.
        do
        {
            while (reader.Read())
            {
                for (int column = 0; column < reader.FieldCount; column++)
                {
                    string value = reader.GetValue(column)?.ToString()?.Trim() ?? string.Empty;

                    if (!string.IsNullOrEmpty(value))
                    {
                        ids.Add(value);
                    }
                }
            }

            break;
        }
        while (reader.NextResult());

        Logger.Debug($"Read {ids.Count} value(s) from legacy '{Path.GetFileName(filePath)}' with ExcelDataReader.");
        return ids.ToArray();
    }

    /// <summary>
    /// Pulls every non-empty cell out of the first worksheet, row by row.
    ///
    /// This mirrors what the Python path produced: pandas wrote the sheet to CSV and the
    /// whole file was then split on whitespace, so every cell became a candidate id
    /// regardless of which column it sat in. Header text was included there too, and still
    /// is here, so behaviour does not change with the format.
    /// </summary>
    private static string[] ReadIdsFromWorkbook(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);

        IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            Logger.Warning($"'{filePath}' has no worksheets.");
            return Array.Empty<string>();
        }

        IXLRange? used = sheet.RangeUsed();
        if (used is null)
        {
            Logger.Warning($"'{filePath}' worksheet '{sheet.Name}' is empty.");
            return Array.Empty<string>();
        }

        var ids = new List<string>();

        foreach (IXLRangeRow row in used.Rows())
        {
            foreach (IXLRangeColumn column in used.Columns())
            {
                string value = row.Cell(column.ColumnNumber() - used.RangeAddress.FirstAddress.ColumnNumber + 1)
                    .GetString()
                    .Trim();

                if (!string.IsNullOrEmpty(value))
                {
                    ids.Add(value);
                }
            }
        }

        Logger.Debug($"Read {ids.Count} value(s) from '{Path.GetFileName(filePath)}' worksheet '{sheet.Name}' with ClosedXML.");
        return ids.ToArray();
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
