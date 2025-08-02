using ClosedXML.Excel;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraConsoleApp;

static class Program
{
    static Program()
    {
        DefaultFileSuffix = InputPath = OutputFileName = string.Empty;
        ConfigReader.UpdateConfig();

        // Column names
        foreach (var column in ColumnNum)
        {
            Sheet.Cell(1, column.Value).Value = column.Key;
        }
    }

    internal static string InputPath;
    internal static string OutputFileName;
    internal static string DefaultFileSuffix;
    internal static string? OutputPath = null;
    internal static readonly string[] SupportedOutputExcelFormats = [".xlsx", ".xlsm", ".xltx"];
    internal static readonly Dictionary<string, int> ColumnNum = new();
    internal static int typing_delay = 50;

    //private static int _row = 2;

    // Normalize: Set fixed width and height
    internal static double FixedColumnWidth = 25;
    internal static double FixedRowHeight = 15;

    #region Constants

    // site constants
    private const string SiteUrl = "https://services.gst.gov.in/services/searchtp";
    private const string InputGstid = "input[name='for_gstin']";
    private const string CaptchaInput = "input[name='cap']";

    // Column constants
    private const string GstinUin = "GSTIN/UIN";
    private const string AdministrativeOffice = "Administrative Office";
    private const string OtherOffice = "Other Office";
    private const string Commissionerate = "Commissionerate";
    private const string Division = "Division";
    private const string Range = "Range";
    private const string Jurisdiction = "JURISDICTION";
    private const string Center = "CENTER";
    private const string Goods = "Goods";
    private const string Services = "Services";

    #endregion

    private static readonly XLWorkbook Workbook = new XLWorkbook();
    private static readonly IXLWorksheet Sheet = Workbook.Worksheets.Add("Parsed HTML");

    public static async Task Main()
    {
        Console.Write("Enter file path - ");
        string? path = Console.ReadLine();

        if (string.IsNullOrEmpty(path))
        {
            path = InputPath;
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("Input path not valid");
                return;
            }

            path.Trim('"');
        }
        else
        {
            path = path.Trim('"');
            OutputFileName = path.Split("\\").Last().Split('.').First() + DefaultFileSuffix;
        }

        using var playwright = await Playwright.CreateAsync();

        var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

        var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            ExecutablePath = chromePath
        });

        var page = await browser.NewPageAsync();

        string[] gstIds = await GetGstIdsAsync(path);
        IdIterator.Configure(gstIds);

        var pageLoadtsk = page.GotoAsync(SiteUrl);

        CancellationTokenSource cts = new();
        page.Load += async (_, _) =>
        {
            cts.Cancel();
            cts = new();
            var token = cts.Token;

            int? idx = IdIterator.GetCurrentIdx();

            if (idx is null)
            {
                Environment.Exit(0);
                return;
            }

            string id = gstIds[idx.Value];
            var input = id.Trim();
            if (string.IsNullOrEmpty(input))
            {
                IdIterator.Complete(token);
                if (!token.IsCancellationRequested) 
                    await page.ReloadAsync();
                return;
            }

            try
            {
                int waitForSiteOpen = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await pageLoadtsk;
                        await page.FocusAsync(InputGstid);
#pragma warning disable CS0612 // Type or member is obsolete
                        await page.FillAsync(InputGstid, ""); // This will replace existing text
                        await page.TypeAsync(InputGstid, input, new() { Delay = typing_delay });

                        await page.Keyboard.PressAsync("Tab"); // Simulates global tab key press
                        await GetDataInXml(page, input, idx.Value + 2, token);
#pragma warning restore CS0612 // Type or member is obsolete
                        break;
                    }
                    catch
                    {
                        if (++waitForSiteOpen >= 10)
                        {
                            Console.WriteLine("Website took too long to load");
                            break;
                        }

                        try
                        {
                            await Task.Delay(100, token);
                        }
                        catch
                        {

                        }
                    }
                }

                HandleFileUsedByProcessException(Workbook, token);
                IdIterator.Complete(token);
                if (!token.IsCancellationRequested) 
                    await page.ReloadAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Unable to process data for GSTID - {input}");
                Console.WriteLine($"Error - {e.Message}");
                throw;
            }
        };

        Console.ReadKey();
        await browser.CloseAsync();
    }

    internal static bool PageLoadSuccess;

    public static async Task GetDataInXml(IPage page, string originalId, int _row, CancellationToken token)
    {
        PageLoadSuccess = false;
        var cts = new CancellationTokenSource();

        GSTPageContentLoader.alreadyPromptedError = false;

        var tsk = Task.Run(async () => await GSTPageContentLoader.LoadPageContents(page, cts.Token));
        var tsk2 = Task.Run(async () => await GSTPageContentLoader.InvalidGstIdHandler(page, originalId, cts.Token));

        await Task.WhenAny(tsk, tsk2);
        cts.Cancel();

        if (!PageLoadSuccess)
        {
            Sheet.Cell(_row, ColumnNum[GstinUin]).Value = originalId;
            return;
        }

        string gstId = await page.InnerTextAsync("div.col-sm-6 > h4");
        gstId = gstId.Split(":").Last().Trim();
        
        if (string.IsNullOrEmpty(gstId)) gstId = originalId;

        var strongElements = await page.QuerySelectorAllAsync("strong");

        var data = new Dictionary<string, string>();

        data[GstinUin] = gstId;

        int col = 2;
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
                        if (strs[0].Trim().Equals(Jurisdiction) && strs[^1].Trim().Equals(Center))
                        {
                            data[Commissionerate] = list[^3].Substring(17);
                            data[Division] = list[^2].Substring(11);
                            data[Range] = list[^1].Substring(8);
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
                        data[value] = value2;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception for Title - {value}. Error - {ex.Message}");
            }
        }

        var element2 = await page.QuerySelectorAsync("div[ng-if='!goodServErrMsg']");
        if (element2 is null) return;

        var table = await element2.QuerySelectorAsync("table");

        if (table == null)
        {
            CommitDataToSheet(data, _row);
            Console.WriteLine("Table not found.");
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

            int colIdx = col;
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

        CommitDataToSheet(data, _row);

        if (!tsk.IsCompleted) await tsk;
        else await tsk2;
    }

    private static void CommitDataToSheet(Dictionary<string, string> data, int _row)
    {
        // update column values for different gstin/uin
        foreach (var dataPair in ColumnNum)
        {
            int currentCol = dataPair.Value;
            data.TryGetValue(dataPair.Key, out var value);
            value ??= string.Empty;

            Sheet.Cell(_row, currentCol).Value = value;
        }

        // Apply to used range only
        foreach (var column2 in Sheet.ColumnsUsed())
            column2.Width = FixedColumnWidth;

        foreach (var row2 in Sheet.RowsUsed())
            row2.Height = FixedRowHeight;
    }

    private static async Task<string[]> GetGstIdsAsync(string filePath)
    {
        if (!@filePath.Split('\\').Last().EndsWith(".csv"))
        {
            ConvertToCSV(filePath);
            filePath = "output.csv";
        }

        var inputs = await File.ReadAllTextAsync(filePath);
        return inputs.Split().Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }


    public static void HandleFileUsedByProcessException(XLWorkbook workbook, CancellationToken token)
    {
        bool alreadPrompted = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!token.IsCancellationRequested)
                {
                    string output = (OutputPath ?? Directory.GetCurrentDirectory()) + OutputFileName;
                    workbook.SaveAs(output);
                    Console.WriteLine($"✅ Extracted visible content saved to {output}");
                }
                return;
            }
            catch
            {
                if (!alreadPrompted) Console.WriteLine($"Close the open excel file");
                alreadPrompted = true;
            }
        }
    }

    private static void ConvertToCSV(string filePath)
    {
        string pythonPath = "python"; // Or use full path like @"C:\Python311\python.exe"
        string scriptPath = "Convert_To_CSV.py";

        // Quote the argument if it contains spaces
        string args = $"\"{scriptPath}\" \"{filePath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        string output = process!.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine("Output:\n" + output);
        if (!string.IsNullOrWhiteSpace(errors))
            Console.WriteLine("Errors:\n" + errors);

    }
}