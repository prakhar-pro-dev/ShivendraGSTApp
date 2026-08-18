using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShivendraGst.Core;

/// <summary>One thing the app needs that is not installed, and how to get it.</summary>
public sealed class MissingPrerequisite
{
    public MissingPrerequisite(string name, string reason, string howToInstall)
    {
        Name = name;
        Reason = reason;
        HowToInstall = howToInstall;
    }

    /// <summary>What is missing, for example "Google Chrome".</summary>
    public string Name { get; }

    /// <summary>Why this run needs it.</summary>
    public string Reason { get; }

    /// <summary>A command the operator can actually run.</summary>
    public string HowToInstall { get; }

    public override string ToString() => $"{Name} - {Reason}  Install with: {HowToInstall}";
}

/// <summary>
/// Checks what a run needs before it starts, so a missing dependency is one clear message
/// rather than a failure deep inside Playwright.
///
/// Every input format is now read in-process - ClosedXML for .xlsx/.xlsm, ExcelDataReader
/// for legacy .xls - so the only external dependencies left are Chrome and the .NET runtime
/// the app is already running on.
///
/// Installing is deliberately not done here - see tools\Install-Prerequisites.ps1. The app
/// cannot install the .NET runtime it is already running on, and a GUI quietly installing
/// system-wide software is harder to trust and to debug than a script you can read.
/// </summary>
public static class Prerequisites
{
    private const string BootstrapHint = @"powershell -ExecutionPolicy Bypass -File tools\Install-Prerequisites.ps1";

    /// <summary>Where Chrome usually lives, in the order worth trying.</summary>
    private static readonly string[] ChromeCandidates =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Google\Chrome\Application\chrome.exe")
    ];

    /// <summary>The command that runs the bootstrap script.</summary>
    public static string BootstrapCommand => BootstrapHint;

    /// <summary>
    /// Finds Chrome in the usual places. Returns null when it is genuinely not installed.
    /// </summary>
    public static string? FindChrome()
    {
        foreach (string candidate in ChromeCandidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>
    /// Checks everything the given batch needs. An empty list means the run can start.
    /// </summary>
    /// <param name="inputFiles">Files about to be processed; reported for context.</param>
    public static IReadOnlyList<MissingPrerequisite> Check(IEnumerable<string> inputFiles)
    {
        var missing = new List<MissingPrerequisite>();

        int fileCount = inputFiles is ICollection<string> collection ? collection.Count : inputFiles.Count();
        Logger.Debug($"Checking prerequisites for {fileCount} input file(s).");

        // Chrome. Adopt whatever we find so the operator does not have to configure a path
        // just because Chrome is installed somewhere other than Program Files.
        string? chrome = FindChrome();
        if (chrome is null)
        {
            missing.Add(new MissingPrerequisite(
                "Google Chrome",
                "the GST portal is driven through a real Chrome window",
                "winget install --id Google.Chrome"));
        }
        else if (!string.Equals(chrome, GstBatchRunner.ChromeExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info($"Using Chrome at {chrome}");
            GstBatchRunner.ChromeExecutablePath = chrome;
        }

        // Playwright's node driver ships in the build output; without it nothing can launch.
        string driver = Path.Combine(AppContext.BaseDirectory, ".playwright");
        if (!Directory.Exists(driver))
        {
            missing.Add(new MissingPrerequisite(
                "Playwright driver",
                $"the browser driver folder is missing from '{AppContext.BaseDirectory}'",
                "rebuild or re-publish the app so the Microsoft.Playwright files are deployed"));
        }

        return missing;
    }

    /// <summary>Writes the outcome to the log and says whether the run can proceed.</summary>
    public static bool LogResult(IReadOnlyList<MissingPrerequisite> missing)
    {
        if (missing.Count == 0)
        {
            Logger.Debug("All prerequisites satisfied.");
            return true;
        }

        Logger.Error($"{missing.Count} prerequisite(s) missing:");

        foreach (MissingPrerequisite item in missing)
        {
            Logger.Error($"  - {item.Name}: {item.Reason}");
            Logger.Error($"      install: {item.HowToInstall}");
        }

        Logger.Error($"Or install everything at once: {BootstrapHint}");
        return false;
    }
}
