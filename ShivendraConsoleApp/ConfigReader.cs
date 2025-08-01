using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace ShivendraConsoleApp;

internal static class ConfigReader
{
    private const string configFile = "configFile.json";
    internal static int TimeoutForInvalidId;

    internal static void UpdateConfig()
    {
        try
        {
            var jsonNode = JsonNode.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), configFile)));

            if (jsonNode!["columns"] is JsonArray jsonArray)
            {
                int col = 1;
                foreach (var item in jsonArray)
                {
                    var column = item?.ToString();
                    if (column is null) continue;
                    Program.ColumnNum[column] = col++;
                }
            }

            GSTPageContentLoader.MaxCaptchaTimeoutIteration = int.Parse(jsonNode["maxCaptchaTimeoutIteration"]!.ToString());
            GSTPageContentLoader.MaxGstIdInvalidIteration = int.Parse(jsonNode["maxGstIdInvalidIteration"]!.ToString());
            Program.InputPath =  jsonNode!["inputPath"]!.ToString();
            string? outputDirectory = jsonNode["outputPath"]?.ToString();
            TimeoutForInvalidId = int.Parse(jsonNode["invalid_id_timeout_in_seconds"]!.ToString());
            Program.typing_delay = int.Parse(jsonNode["typing_delay_in_milliseconds"]!.ToString());

            Program.DefaultFileSuffix = jsonNode["outputFileSuffix"]! + ".xlsx";
            
            Program.OutputFileName = null!;
            if (outputDirectory is not null)
            {
                string outputFileFormat = outputDirectory.Split('.').Last();
                foreach (var format in Program.SupportedOutputExcelFormats)
                {   
                    if (outputFileFormat.EndsWith(format))
                    {
                        Program.OutputFileName = outputDirectory.Split("\\").Last();
                        Program.OutputPath = outputDirectory.Substring(0, outputDirectory.Length - Program.OutputFileName.Length);
                        break;
                    }
                }

                // output file name not mentioned in config file
                Program.OutputPath ??= outputDirectory;
                if (Program.OutputFileName is null)
                {
                    Program.OutputFileName = Program.InputPath.Split("\\").Last().Split('.').First() + Program.DefaultFileSuffix;
                }
                else Program.OutputFileName = Program.OutputFileName.Split('.').First() + ".xlsx";
            }
            else
            {
                Program.OutputPath = Directory.GetCurrentDirectory() + "\\";
                Program.OutputFileName = Program.OutputFileName.Split('.').First() + Program.DefaultFileSuffix;
            }

            Program.OutputPath += "\\";

            if (Double.TryParse(jsonNode["columnWidth"]?.ToString(), out var width))
            {
                Program.FixedColumnWidth = width;
            }
            if (Double.TryParse(jsonNode["rowHeight"]?.ToString(), out var height))
            {
                Program.FixedRowHeight = height;
            }
        }
        catch
        {
            Console.WriteLine($"Invalid config file");
        }
    }
}