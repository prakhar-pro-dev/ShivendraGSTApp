using System;
using System.Diagnostics;

namespace ShivendraConsoleApp;

internal static class ExcelManager
{
    internal static void ConvertToCSV(string filePath)
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

        Logger.Info($"Converted '{filePath}' to CSV using: {pythonPath} {args}");

        if (!string.IsNullOrWhiteSpace(output))
            Logger.Info("Output:\n" + output);

        if (!string.IsNullOrWhiteSpace(errors))
            Logger.Error("Errors:\n" + errors);

        if (process.ExitCode != 0)
            Logger.Error($"{scriptPath} exited with code {process.ExitCode}; the CSV may be missing or incomplete.");

    }
}