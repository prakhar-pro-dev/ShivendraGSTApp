using ClosedXML.Excel;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

/// <summary>
/// Scrapes one input file into one workbook.
///
/// A fresh instance is created per input file, which is what makes batching possible: the
/// workbook, worksheet and per-lookup flags used to be static on the console app's Program
/// class, so a process could only ever produce a single output file.
///
/// The driver is still event-based - the site is re-submitted by reloading the page, and
/// each load picks up the next id - because that is the flow the operator's captcha work
/// fits into. What changed is how it finishes: instead of Environment.Exit killing the
/// process when ids run out, the run completes a task so the caller can move to the next
/// file.
/// </summary>
internal sealed class GstScraper
{
    #region Constants

    // site constants
    private const string SiteUrl = "https://services.gst.gov.in/services/searchtp";
    private const string InputGstid = "input[name='for_gstin']";
    private const string gap = " ";

    // Column constants
    private const string GstinUin = "GSTIN/UIN";
    private const string AdministrativeOffice = "Administrative Office";
    private const string OtherOffice = "Other Office";
    private const string MainOffice = "Center / State";
    private const string Central_ = "Central ";
    private const string Zone = "Zone";
    private const string Commissionerate = "Commissionerate";
    private const string Division = "Division";
    private const string Range = "Range";
    private const string Jurisdiction = "JURISDICTION";
    private const string Center = "CENTER";
    private const string State = "State";
    private const string Charge = "Charge";
    private const string Circle = "Circle";
    private const string Ward = "Ward";
    private const string Sector = "Sector";
    private const string Unit = "Unit";
    private const string District = "District";
    private const string Headquarter = "Headquarter";
    private const string AC_or_CTO_Ward = "AC / CTO Ward";
    private const string LOCAL_GST_Office = "LOCAL GST Office";
    private const string Goods = "Goods";
    private const string Services = "Services";

    #endregion

    // Column lists
    private static readonly string[] Zone_Commissionerate = new[] { Zone, Commissionerate };

    private static readonly string[] Division_level = new[] { Division };

    private static readonly string[] Sub_division = new[]
        { Range, Circle, Ward, Unit, Charge, Sector, District, Headquarter, LOCAL_GST_Office, AC_or_CTO_Ward };

    private readonly IPage _page;
    private readonly IScrapeUi _ui;
    private readonly string _inputFile;
    private readonly string _outputFile;
    private readonly int _fileNumber;
    private readonly int _fileCount;

    private string[] _gstIds = Array.Empty<string>();
    private Task<IResponse?>? _pageLoadTask;

    internal GstScraper(IPage page, IScrapeUi ui, string inputFile, string outputFile, int fileNumber, int fileCount)
    {
        _page = page;
        _ui = ui;
        _inputFile = inputFile;
        _outputFile = outputFile;
        _fileNumber = fileNumber;
        _fileCount = fileCount;
    }

    /// <summary>
    /// Processes every GSTIN in the input file, saving after each one so a crash or a
    /// cancelled run still leaves the ids collected so far on disk.
    /// </summary>
    internal async Task<ScrapeFileResult> RunAsync(CancellationToken cancellationToken)
    {
        Logger.Info($"[{_fileNumber}/{_fileCount}] Reading {_inputFile}");

        try
        {
            _gstIds = await ReadWriteOperations.GetGstIdsAsync(_inputFile).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"Could not read GST ids from '{_inputFile}'.", ex);
            return new ScrapeFileResult(_inputFile, _outputFile, 0, false, ex.Message);
        }

        if (_gstIds.Length == 0)
        {
            Logger.Warning($"No GST ids found in '{_inputFile}' - skipping it.");
            return new ScrapeFileResult(_inputFile, _outputFile, 0, false, "No GST ids found.");
        }

        Logger.Info($"Found {_gstIds.Length} GST id(s); writing to {_outputFile}");

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add("Parsed HTML");
        WriteHeaderRow(sheet);

        IdIterator.Configure(_gstIds);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each page load cancels the previous id's work, matching the original behaviour
        // where an operator reloading the page abandons the in-flight lookup.
        CancellationTokenSource idCts = new();

        async void OnLoad(object? sender, IPage loadedPage)
        {
            CancellationTokenSource previous = idCts;
            idCts = new CancellationTokenSource();
            CancellationToken token = idCts.Token;

            try
            {
                previous.Cancel();
                previous.Dispose();

                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetResult(false);
                    return;
                }

                int? idx = IdIterator.GetCurrentIdx();

                if (idx is null)
                {
                    // Every id in this file is done. Previously this called
                    // Environment.Exit(0), which made a batch impossible.
                    completion.TrySetResult(true);
                    return;
                }

                string input = _gstIds[idx.Value].Trim();

                if (string.IsNullOrEmpty(input))
                {
                    IdIterator.Complete(token);
                    if (!token.IsCancellationRequested) await _page.ReloadAsync();
                    return;
                }

                Logger.Info($"Processing GST id {idx.Value + 1} of {_gstIds.Length} - {input}");
                _ui.ReportProgress(new ScrapeProgress(
                    _inputFile, _fileNumber, _fileCount, input, idx.Value + 1, _gstIds.Length));

                int waitForSiteOpen = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_pageLoadTask is not null) await _pageLoadTask;

                        await _page.FocusAsync(InputGstid);
#pragma warning disable CS0612 // Type or member is obsolete
                        await _page.FillAsync(InputGstid, ""); // This will replace existing text
                        await _page.TypeAsync(InputGstid, input, new() { Delay = AppConfig.TypingDelay });
#pragma warning restore CS0612 // Type or member is obsolete

                        await _page.Keyboard.PressAsync("Tab"); // Simulates global tab key press
                        await GetDataInXml(sheet, input, idx.Value + 2, token);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (++waitForSiteOpen >= 10)
                        {
                            Logger.Warning($"Website took too long to load for GST id - {input}");
                            break;
                        }

                        Logger.Debug($"Retrying GST id {input} (attempt {waitForSiteOpen}) - {ex.Message}");

                        try
                        {
                            await Task.Delay(100, token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }

                await ReadWriteOperations.SaveWorkbookAsync(workbook, _outputFile, _ui, token).ConfigureAwait(false);

                IdIterator.Complete(token);

                if (!token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    await _page.ReloadAsync();
                }
            }
            catch (Exception ex)
            {
                // This runs as an async void event handler, so an escaping exception would
                // otherwise be lost (or crash the process). Hand it to the awaiting caller.
                Logger.Error($"Unable to process '{_inputFile}'.", ex);
                completion.TrySetException(ex);
            }
        }

        _page.Load += OnLoad;

        bool completed;
        string? failure = null;

        try
        {
            using (cancellationToken.Register(() => completion.TrySetResult(false)))
            {
                _pageLoadTask = _page.GotoAsync(SiteUrl);
                completed = await completion.Task.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            completed = false;
            failure = ex.Message;
        }
        finally
        {
            _page.Load -= OnLoad;
            idCts.Cancel();
            idCts.Dispose();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            failure ??= "Cancelled.";
        }

        // Always attempt a final save: a cancelled or failed run should still keep whatever
        // was collected before it stopped.
        bool saved = await ReadWriteOperations
            .SaveWorkbookAsync(workbook, _outputFile, _ui, CancellationToken.None)
            .ConfigureAwait(false);

        if (saved && completed)
        {
            Logger.Info($"✅ Extracted visible content saved to {_outputFile}");
        }

        return new ScrapeFileResult(_inputFile, _outputFile, _gstIds.Length, saved, failure);
    }

    private static void WriteHeaderRow(IXLWorksheet sheet)
    {
        foreach (var column in AppConfig.ColumnNum)
        {
            sheet.Cell(1, column.Value).Value = column.Key;
        }
    }

    private async Task GetDataInXml(IXLWorksheet sheet, string originalId, int _row, CancellationToken token)
    {
        var state = new PageLoadState();

        // The two watchers race; linking to the caller's token means cancelling the run
        // also unblocks whichever one is still waiting.
        using var raceCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        var tsk = Task.Run(async () => await GSTPageContentLoader.LoadPageContents(state, _page, raceCts.Token));
        var tsk2 = Task.Run(async () => await GSTPageContentLoader.InvalidGstIdHandler(state, _page, originalId, _ui, raceCts.Token));

        await Task.WhenAny(tsk, tsk2);
        raceCts.Cancel();

        if (!state.Success)
        {
            sheet.Cell(_row, AppConfig.ColumnNum[GstinUin]).Value = originalId;
            return;
        }

        string gstId = await _page.InnerTextAsync("div.col-sm-6 > h4");
        gstId = gstId.Split(":").Last().Trim();

        if (string.IsNullOrEmpty(gstId)) gstId = originalId;

        var strongElements = await _page.QuerySelectorAllAsync("strong");

        var data = new Dictionary<string, string>();

        foreach (var column in AppConfig.ColumnNum.Keys) data[column] = string.Empty;
        data[GstinUin] = gstId;

        foreach (var element in strongElements)
        {
            string value = await element.InnerTextAsync();

            // Get the parent <p> of <strong>
            var parentP = await element.EvaluateHandleAsync("el => el.parentElement");
            var nextP = await parentP.EvaluateHandleAsync("el => el.nextElementSibling");

            try
            {
                var jsHandle = await nextP.EvaluateHandleAsync(@"el => {
                    // Adjust selector as needed (e.g., 'li', 'div', 'tr td', etc.)
                    return Array.from(el.querySelectorAll(':scope > *')).map(child => child.textContent.trim());
                }");

                StringBuilder sb = new();

                string[] list = await jsHandle.JsonValueAsync<string[]>();

                if (list.Length > 0)
                {
                    if (value.Equals(AdministrativeOffice) || value.Equals(OtherOffice))
                    {
                        string[] strs = list[0].Split('(', '-', ')').Where(s => !s.Equals(string.Empty)).ToArray();

                        if (value.Equals(AdministrativeOffice))
                        {
                            data[MainOffice] = list.Where(entry
                                    => string.Equals(Jurisdiction, entry.Split('(', '-', ')')
                                                    .First(s => !s.Equals(string.Empty)).Trim(),
                                                    StringComparison.OrdinalIgnoreCase))
                                .Select(s
                                    => s.Split('(', '-', ')').Last(s => !s.Equals(string.Empty)))
                                .First()?.Trim()!;
                        }

                        if (strs[0].Trim().Equals(Jurisdiction) && strs[^1].Trim().Equals(Center))
                        {
                            foreach (var str in list)
                            {
                                string? title = str.Split('(', '-', ')').FirstOrDefault(s => !s.Equals(string.Empty))?.Trim();

                                if (title is null) continue;

                                if (string.Equals(Zone, title, StringComparison.OrdinalIgnoreCase))
                                {
                                    data[Central_ + Zone] = str.Substring(7);
                                }
                                else if (string.Equals(Commissionerate, title, StringComparison.OrdinalIgnoreCase))
                                {
                                    data[Central_ + Commissionerate] = str.Substring(17);
                                }
                                else if (string.Equals(Division, title, StringComparison.OrdinalIgnoreCase))
                                {
                                    data[Central_ + Division] = str.Substring(11);
                                }
                                else if (string.Equals(Range, title, StringComparison.OrdinalIgnoreCase))
                                {
                                    data[Central_ + Range] = str.Substring(8);
                                }
                            }
                        }
                        else
                        {
                            foreach (var item in list)
                            {
                                string str = item.Trim();
                                string? val = Helper.GetFieldValue(str, State);
                                if (val is not null)
                                {
                                    data[State] = data[State] + Environment.NewLine + val;
                                    continue;
                                }

                                val = Helper.GetFieldValue(str, Zone_Commissionerate);
                                if (val is not null)
                                {
                                    data[State + gap + Zone] = data[State + gap + Zone] + Environment.NewLine + val;
                                    continue;
                                }

                                val = Helper.GetFieldValue(str, Division_level);
                                if (val is not null)
                                {
                                    data[State + gap + Division] = data[State + gap + Division] + Environment.NewLine + val;
                                    continue;
                                }

                                val = Helper.GetFieldValue(str, Sub_division);
                                if (val is not null)
                                {
                                    data[State + gap + Charge] = data[State + gap + Charge] + Environment.NewLine + val;
                                }
                            }
                        }
                    }

                    foreach (var item in list)
                    {
                        sb.AppendLine(item);
                    }

                    var value2 = sb.ToString();
                    if (!string.IsNullOrEmpty(value2))
                    {
                        data[value] = value2;
                    }
                }
                else
                {
                    string value2 = "";
                    if (nextP is IElementHandle elementHandle)
                    {
                        value2 = await elementHandle.InnerTextAsync();

                        if (value == "GSTIN / UIN Status")
                        {
                            nextP = await nextP.EvaluateHandleAsync("el => el.nextElementSibling");
                            if (nextP is IElementHandle _element)
                            {
                                value2 += Environment.NewLine + await _element.InnerTextAsync();
                            }
                        }

                        data[value] = value2;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception for Title - {value}.", ex);
            }
        }

        var element2 = await _page.QuerySelectorAsync("div[ng-if='!goodServErrMsg']");
        if (element2 is null) return;

        var table = await element2.QuerySelectorAsync("table");

        if (table == null)
        {
            CommitDataToSheet(sheet, data, _row);
            Logger.Warning($"Goods/services table not found for GST id - {originalId}");
            if (!tsk.IsCompleted) await tsk;
            else await tsk2;
            return;
        }

        // Get all rows (both thead and tbody)
        var rowsQuery = await table.QuerySelectorAllAsync("tr");

        StringBuilder goods = new(), services = new();
        for (int i = 0; i < rowsQuery.Count; i++)
        {
            if (i <= 1) continue;

            var rowQuery = rowsQuery[i];
            var cells = await rowQuery.QuerySelectorAllAsync("th, td"); // handle both header and data cells

            bool mergeTwoCol = false, isGoods = true;
            string colVal = "";
            foreach (var cell in cells)
            {
                var text = await cell.InnerTextAsync();

                if (mergeTwoCol)
                {
                    if (isGoods)
                    {
                        if (!string.IsNullOrEmpty(colVal + text))
                            goods.AppendLine(colVal + " : " + text);
                        isGoods = false;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(colVal + text))
                            services.AppendLine(colVal + " : " + text);
                        isGoods = true;
                    }
                    mergeTwoCol = false;
                }
                else
                {
                    colVal = text;
                    mergeTwoCol = true;
                }
            }
        }

        data[Goods] = goods.ToString();
        data[Services] = services.ToString();

        CommitDataToSheet(sheet, data, _row);

        if (!tsk.IsCompleted) await tsk;
        else await tsk2;
    }

    private static void CommitDataToSheet(IXLWorksheet sheet, Dictionary<string, string> data, int _row)
    {
        // update column values for different gstin/uin
        foreach (var dataPair in AppConfig.ColumnNum)
        {
            int currentCol = dataPair.Value;

            sheet.Cell(_row, currentCol).Value = data[dataPair.Key].Trim(' ', '-');
        }

        // Apply to used range only
        foreach (var column2 in sheet.ColumnsUsed())
            column2.Width = AppConfig.FixedColumnWidth;

        foreach (var row2 in sheet.RowsUsed())
            row2.Height = AppConfig.FixedRowHeight;
    }
}
