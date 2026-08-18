using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ShivendraGst.Core;

/// <summary>
/// Works out which files a run should process. A run is given either a single input file
/// or a directory; a directory is expanded into every supported input file directly
/// inside it.
/// </summary>
public static class InputFiles
{
    /// <summary>Input formats the app can read. Anything else in the folder is ignored.</summary>
    public static readonly string[] SupportedInputExtensions = [".csv", ".xlsx", ".xls", ".xlsm"];

    /// <summary>
    /// Expands <paramref name="path"/> into the files to process.
    ///
    /// A file yields itself. A directory yields its supported files, sorted by name so a
    /// batch runs in a predictable order, and is NOT searched recursively - dropping a
    /// folder of results inside the input folder should not silently feed them back in.
    /// Previously generated output files are skipped so re-running over the same folder
    /// does not re-process its own results.
    /// </summary>
    /// <exception cref="FileNotFoundException">The path does not exist.</exception>
    public static IReadOnlyList<string> Discover(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new FileNotFoundException("No input path was supplied.", path ?? string.Empty);
        }

        string trimmed = path.Trim().Trim('"');

        if (File.Exists(trimmed))
        {
            return new[] { Path.GetFullPath(trimmed) };
        }

        if (!Directory.Exists(trimmed))
        {
            throw new FileNotFoundException(
                $"'{trimmed}' is neither an existing file nor a folder.", trimmed);
        }

        return Directory.EnumerateFiles(trimmed, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupported)
            .Where(file => !IsGeneratedOutput(file))
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .ToArray();
    }

    /// <summary>True when the path points at a directory rather than a single file.</summary>
    public static bool IsDirectory(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path.Trim().Trim('"'));
    }

    /// <summary>
    /// Builds the output workbook path for one input file: the input's name with the
    /// configured suffix, placed in <paramref name="outputDirectory"/>.
    /// </summary>
    public static string BuildOutputPath(string inputFile, string outputDirectory)
    {
        string name = Path.GetFileNameWithoutExtension(inputFile);
        string suffix = string.IsNullOrEmpty(AppConfig.DefaultFileSuffix) ? ".xlsx" : AppConfig.DefaultFileSuffix;

        return Path.Combine(outputDirectory, name + suffix);
    }

    private static bool IsSupported(string file)
    {
        string extension = Path.GetExtension(file);
        return SupportedInputExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Recognises this app's own output by the configured suffix, so an output folder that
    /// happens to be the input folder does not create a feedback loop.
    /// </summary>
    private static bool IsGeneratedOutput(string file)
    {
        string suffix = AppConfig.DefaultFileSuffix;
        if (string.IsNullOrEmpty(suffix)) return false;

        string suffixName = Path.GetFileNameWithoutExtension(suffix);
        if (string.IsNullOrEmpty(suffixName)) return false;

        return Path.GetFileNameWithoutExtension(file)
            .EndsWith(suffixName, StringComparison.OrdinalIgnoreCase);
    }
}
