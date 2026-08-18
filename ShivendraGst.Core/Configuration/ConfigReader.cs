using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace ShivendraGst.Core;

internal static class ConfigReader
{
    private const string configFile = "configFile.json";

    /// <summary>
    /// Resolves configFile.json next to the executable, falling back to the working
    /// directory.
    ///
    /// It used to look only in the working directory, which is fine for a console app
    /// started from its own folder but wrong for a GUI launched from a shortcut, the
    /// debugger, or anywhere else - the config was silently missed and every setting fell
    /// back to its default.
    /// </summary>
    private static string ResolveConfigPath()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, configFile);
        if (File.Exists(beside)) return beside;

        string working = Path.Combine(Directory.GetCurrentDirectory(), configFile);
        if (File.Exists(working)) return working;

        // Neither exists - return the expected location so the error names the right path.
        return beside;
    }

    internal static void UpdateConfig()
    {
        try
        {
            var jsonNode = JsonNode.Parse(File.ReadAllText(ResolveConfigPath()));

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