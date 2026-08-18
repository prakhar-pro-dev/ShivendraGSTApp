using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace ShivendraGst.Core;

internal static class ConfigReader
{
    private const string configFile = "configFile.json";

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
                    AppConfig.ColumnNum[column] = col++;
                }
            }

            GSTPageContentLoader.MaxCaptchaTimeoutIteration = int.Parse(jsonNode["maxCaptchaTimeoutIteration"]!.ToString());
            GSTPageContentLoader.MaxGstIdInvalidIteration = int.Parse(jsonNode["maxGstIdInvalidIteration"]!.ToString());
            AppConfig.InputPath =  jsonNode!["inputPath"]!.ToString();
            string? outputDirectory = jsonNode["outputPath"]?.ToString();
            AppConfig.TimeoutForInvalidId = int.Parse(jsonNode["invalid_id_timeout_in_seconds"]!.ToString());
            AppConfig.TypingDelay = int.Parse(jsonNode["typing_delay_in_milliseconds"]!.ToString());

            AppConfig.DefaultFileSuffix = jsonNode["outputFileSuffix"]! + ".xlsx";
            
            AppConfig.OutputFileName = null!;
            if (outputDirectory is not null)
            {
                string outputFileFormat = outputDirectory.Split('.').Last();
                foreach (var format in AppConfig.SupportedOutputExcelFormats)
                {   
                    if (outputFileFormat.EndsWith(format))
                    {
                        AppConfig.OutputFileName = outputDirectory.Split("\\").Last();
                        AppConfig.OutputPath = outputDirectory.Substring(0, outputDirectory.Length - AppConfig.OutputFileName.Length);
                        break;
                    }
                }

                // output file name not mentioned in config file
                AppConfig.OutputPath ??= outputDirectory;
                if (AppConfig.OutputFileName is null)
                {
                    AppConfig.OutputFileName = AppConfig.InputPath.Split("\\").Last().Split('.').First() + AppConfig.DefaultFileSuffix;
                }
                else AppConfig.OutputFileName = AppConfig.OutputFileName.Split('.').First() + ".xlsx";
            }
            else
            {
                AppConfig.OutputPath = Directory.GetCurrentDirectory() + "\\";
                AppConfig.OutputFileName = AppConfig.OutputFileName.Split('.').First() + AppConfig.DefaultFileSuffix;
            }

            AppConfig.OutputPath += "\\";

            if (Double.TryParse(jsonNode["columnWidth"]?.ToString(), out var width))
            {
                AppConfig.FixedColumnWidth = width;
            }
            if (Double.TryParse(jsonNode["rowHeight"]?.ToString(), out var height))
            {
                AppConfig.FixedRowHeight = height;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Invalid config file '{configFile}'.", ex);
        }
    }
}