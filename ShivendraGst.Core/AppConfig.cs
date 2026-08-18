using System.Collections.Generic;

namespace ShivendraGst.Core;

/// <summary>
/// Settings loaded from configFile.json, shared by every front end.
///
/// These used to be static fields on the console app's Program class; they moved here
/// when the engine was split into this library so the WinForms app reads exactly the
/// same configuration.
/// </summary>
public static class AppConfig
{
    private static readonly object _sync = new();
    private static bool _loaded;

    /// <summary>Input file or folder used when the operator does not supply one.</summary>
    public static string InputPath = string.Empty;

    /// <summary>Output workbook name from config. Only meaningful for single-file runs.</summary>
    public static string OutputFileName = string.Empty;

    /// <summary>Suffix appended to a generated workbook, for example "-Entries.xlsx".</summary>
    public static string DefaultFileSuffix = string.Empty;

    /// <summary>Default output directory from config, null until configuration is read.</summary>
    public static string? OutputPath;

    public static readonly string[] SupportedOutputExcelFormats = [".xlsx", ".xlsm", ".xltx"];

    /// <summary>Column name to 1-based worksheet column, in the order given by config.</summary>
    public static readonly Dictionary<string, int> ColumnNum = new();

    /// <summary>Seconds a front end waits for an answer before skipping an invalid GSTIN.</summary>
    public static int TimeoutForInvalidId = 5;

    /// <summary>Per-character delay when typing a GSTIN into the site.</summary>
    public static int TypingDelay = 50;

    public static double FixedColumnWidth = 25;

    public static double FixedRowHeight = 15;

    /// <summary>
    /// Starts logging and reads configFile.json once per process. Safe to call from any
    /// entry point; later calls are no-ops.
    /// </summary>
    public static void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_loaded) return;
            _loaded = true;

            Logger.Initialize();
            ConfigReader.UpdateConfig();
        }
    }
}
