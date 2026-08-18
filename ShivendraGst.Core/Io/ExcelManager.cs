using System;
using System.Diagnostics;
using System.IO;

namespace ShivendraGst.Core;

internal static class ExcelManager
{
    internal static void ConvertToCSV(string filePath)
    {
        string pythonPath = "python"; // Or use full path like @"C:\Python311\python.exe"

        // The script lives in Resources\ in the repo but is deployed beside the executable.
        // Resolve it against the executable rather than the working directory: the GUI can
        // be launched from anywhere, and the script also writes output.csv relative to the
        // process's working directory, so both ends are pinned here.
        string scriptPath = Path.Combine(AppContext.BaseDirectory, "Convert_To_CSV.py");

        if (!File.Exists(scriptPath))
        {
            Logger.Error($"Convert_To_CSV.py was not found at '{scriptPath}'. Spreadsheet input cannot be converted; supply a .csv instead.");
            return;
        }

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
            WorkingDirectory = AppContext.BaseDirectory,
        };

        using var process = Process.Start(psi);
        string output = process!.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Logger.Info($"Converted '{filePath}' to CSV using: {pythonPath} {args}");

        if (!string.IsNullOrWhiteSpace(output))
            Logger.Info("Output:\n" + output);

        if (!string.IsNullOrWhiteSpace(errors))
            Logger.Error("Errors:\n" + errors);

        if (process.ExitCode != 0)
            Logger.Error($"{scriptPath} exited with code {process.ExitCode}; the CSV may be missing or incomplete.");

    }
}