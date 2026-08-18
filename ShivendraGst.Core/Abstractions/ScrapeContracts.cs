using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

/// <summary>
/// Everything the scraping engine needs from whichever front end is driving it:
/// progress to display and the two questions it has to ask a human.
///
/// Logging is not part of this contract - the engine writes to <see cref="Logger"/>
/// directly, and a front end that wants to show those lines subscribes to
/// <see cref="Logger.MessageWritten"/>.
/// </summary>
public interface IScrapeUi
{
    /// <summary>Called as the run advances. Implementations must not block.</summary>
    void ReportProgress(ScrapeProgress progress);

    /// <summary>
    /// The site reported that a GSTIN could not be found. Return true to move on to the
    /// next id. Implementations are expected to give the operator a bounded amount of
    /// time to react and then continue on their own, so an unattended run cannot stall.
    /// </summary>
    Task<bool> ConfirmSkipInvalidIdAsync(string gstin, string errorText, CancellationToken cancellationToken);

    /// <summary>
    /// The output workbook could not be written, almost always because it is open in
    /// Excel. Return true to try again, false to give up on this file.
    /// </summary>
    Task<bool> RetrySaveAsync(string outputFile, string reason, CancellationToken cancellationToken);
}

/// <summary>A snapshot of how far a batch has got. Immutable, safe to marshal to a UI thread.</summary>
public sealed class ScrapeProgress
{
    public ScrapeProgress(string inputFile, int fileNumber, int fileCount, string currentId, int idNumber, int idCount)
    {
        InputFile = inputFile;
        FileNumber = fileNumber;
        FileCount = fileCount;
        CurrentId = currentId;
        IdNumber = idNumber;
        IdCount = idCount;
    }

    /// <summary>Input file currently being processed.</summary>
    public string InputFile { get; }

    /// <summary>1-based position of that file in the batch.</summary>
    public int FileNumber { get; }

    /// <summary>Number of input files in the batch.</summary>
    public int FileCount { get; }

    /// <summary>GSTIN currently being looked up, empty between ids.</summary>
    public string CurrentId { get; }

    /// <summary>1-based position of that id within the current file.</summary>
    public int IdNumber { get; }

    /// <summary>Number of ids in the current file.</summary>
    public int IdCount { get; }

    /// <summary>Overall completion across the whole batch, 0 to 100.</summary>
    public int OverallPercent
    {
        get
        {
            if (FileCount <= 0) return 0;

            double perFile = 1d / FileCount;
            double withinFile = IdCount > 0 ? (double)IdNumber / IdCount : 0d;
            double done = ((FileNumber - 1) * perFile) + (withinFile * perFile);

            return Math.Clamp((int)Math.Round(done * 100), 0, 100);
        }
    }
}

/// <summary>Outcome of one input file.</summary>
public sealed class ScrapeFileResult
{
    public ScrapeFileResult(string inputFile, string outputFile, int idCount, bool saved, string? failureReason)
    {
        InputFile = inputFile;
        OutputFile = outputFile;
        IdCount = idCount;
        Saved = saved;
        FailureReason = failureReason;
    }

    public string InputFile { get; }

    public string OutputFile { get; }

    public int IdCount { get; }

    /// <summary>True when the workbook reached disk.</summary>
    public bool Saved { get; }

    /// <summary>Why the file did not complete, or null when it did.</summary>
    public string? FailureReason { get; }
}
