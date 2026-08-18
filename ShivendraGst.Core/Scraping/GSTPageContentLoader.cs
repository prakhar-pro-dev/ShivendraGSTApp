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
        bool loggedWait = false;

        while (!token.IsCancellationRequested)
        {
            // Once the page or browser is gone, WaitForSelectorAsync throws instantly
            // rather than honouring its timeout, so the retry below becomes a hot loop.
            // That is how a two-minute run wrote a 5 MB log of the same line.
            if (page.IsClosed)
            {
                Logger.Debug($"Page closed while watching for the error banner for {gstin}.");
                return;
            }

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
                // Normally a 5s timeout: the lookup is still running, or it succeeded and
                // the other watcher wins the race. Either way, retry.
                //
                // Logged once per id rather than per attempt - it repeats every few seconds
                // while the operator works the captcha - and the delay means that even an
                // exception thrown instantly (a closing page, a dead driver) costs four
                // iterations a second rather than a full CPU core.
                if (!loggedWait)
                {
                    loggedWait = true;
                    Logger.Debug($"No error banner yet for {gstin} - {ex.Message}");
                }

                try
                {
                    await Task.Delay(250, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

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
