using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

/// <summary>
/// Per-lookup state shared by the two racing watchers below. One instance per GSTIN, so
/// nothing leaks between ids or between files in a batch - these used to be static fields,
/// which is what made batching impossible.
/// </summary>
internal sealed class PageLoadState
{
    /// <summary>Set once the taxpayer details actually render.</summary>
    internal bool Success;

    /// <summary>Guards against prompting the operator twice for the same id.</summary>
    internal bool AlreadyPromptedError;
}

/// <summary>
/// After a GSTIN is submitted the page either renders taxpayer details or shows an error
/// banner. These two watchers race; whichever resolves first decides what happens to the id.
/// </summary>
internal static class GSTPageContentLoader
{
    internal static int MaxCaptchaTimeoutIteration = 10;
    internal static int MaxGstIdInvalidIteration = 10;

    /// <summary>
    /// Waits for the taxpayer details to appear, retrying while the operator works through
    /// the captcha.
    /// </summary>
    internal static async Task LoadPageContents(PageLoadState state, IPage page, CancellationToken token)
    {
        int iteration = 0;

        while (!state.Success && !token.IsCancellationRequested)
        {
            try
            {
                await page.WaitForSelectorAsync("strong[data-ng-bind='trans.LBL_LEAGAL_NAME_BUSI']");
                state.Success = true;
                return;
            }
            catch (Exception ex)
            {
                // File-only: this fires about once a second while the captcha is still
                // unsolved, so console output would be flooded. The give-up below is
                // what the operator actually needs to see.
                Logger.Debug($"Waiting for page contents (attempt {iteration + 1} of {MaxCaptchaTimeoutIteration}) - {ex.Message}");

                if (++iteration >= MaxCaptchaTimeoutIteration)
                {
                    Logger.Warning($"Gave up waiting for page contents after {MaxCaptchaTimeoutIteration} attempts.");
                    return;
                }

                try
                {
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Waits for the site's "not found" banner and, the first time it appears for this id,
    /// asks the front end whether to move on. The front end owns how the question is asked
    /// and how long it waits, so the console and the GUI can differ.
    /// </summary>
    internal static async Task InvalidGstIdHandler(
        PageLoadState state,
        IPage page,
        string gstin,
        IScrapeUi ui,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            IElementHandle? errorElement;

            try
            {
                errorElement = await page.WaitForSelectorAsync("span.err", new()
                {
                    State = WaitForSelectorState.Visible, // Wait until it's actually visible
                    Timeout = 5000
                });
            }
            catch (Exception ex)
            {
                // No banner within the timeout - either the lookup is still running or it
                // succeeded, in which case the other watcher wins the race.
                Logger.Debug($"No error banner yet for {gstin} - {ex.Message}");
                await Task.Yield();
                continue;
            }

            if (state.AlreadyPromptedError) return;
            state.AlreadyPromptedError = true;

            string errorText = errorElement is null
                ? string.Empty
                : await errorElement.InnerTextAsync();

            await ui.ConfirmSkipInvalidIdAsync(gstin, errorText, token).ConfigureAwait(false);
            return;
        }
    }
}
