using ShivendraGst.Core;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGstWinApp;

/// <summary>
/// Adapts <see cref="MainForm"/> to the engine's <see cref="IScrapeUi"/> contract. All the
/// thread marshalling lives on the form; this is just the wiring.
/// </summary>
internal sealed class FormScrapeUi : IScrapeUi
{
    private readonly MainForm _form;

    internal FormScrapeUi(MainForm form) => _form = form;

    public void ReportProgress(ScrapeProgress progress) => _form.ShowProgress(progress);

    public Task<bool> ConfirmSkipInvalidIdAsync(string gstin, string errorText, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(true);
        }

        return _form.AskSkipAsync(gstin, errorText);
    }

    public Task<bool> RetrySaveAsync(string outputFile, string reason, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        return _form.AskRetrySaveAsync(outputFile, reason);
    }
}
