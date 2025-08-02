using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraConsoleApp;

internal static class ReadWriteOperations
{
    internal static async Task<string[]> GetGstIdsAsync(string filePath)
    {
        if (!filePath.Split('\\').Last().EndsWith(".csv"))
        {
            ExcelManager.ConvertToCSV(filePath);
            filePath = "output.csv";
        }

        var inputs = await File.ReadAllTextAsync(filePath);
        return inputs.Split().Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }


    internal static void HandleFileUsedByProcessException(XLWorkbook workbook, CancellationToken token)
    {
        bool alreadPrompted = false;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!token.IsCancellationRequested)
                {
                    string output = (Program.OutputPath ?? Directory.GetCurrentDirectory()) + Program.OutputFileName;
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
}