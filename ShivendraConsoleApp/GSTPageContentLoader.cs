using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShivendraConsoleApp;

internal static class GSTPageContentLoader
{
    internal static int _loadContentIteration;
    internal static int _gstIdHandlerIteration;
    internal static int MaxCaptchaTimeoutIteration = 10;
    internal static int MaxGstIdInvalidIteration = 10;

    internal static async Task LoadPageContents(IPage page, CancellationToken token)
    {
        _loadContentIteration = 0;
        while (!Program.PageLoadSuccess && !token.IsCancellationRequested)
        {
            try
            {
                await page.WaitForSelectorAsync("strong[data-ng-bind='trans.LBL_LEAGAL_NAME_BUSI']");
                Program.PageLoadSuccess = true;
                return;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Error - {ex.Message}");
                if (++_loadContentIteration >= MaxCaptchaTimeoutIteration)
                    return;
                await Task.Delay(1000);
            }
        }
    }

    internal static bool alreadyPromptedError;
    internal static async Task InvalidGstIdHandler(IPage page, string gstin, CancellationToken token)
    {
        _gstIdHandlerIteration = 0;
        while (true)
        {
            if (token.IsCancellationRequested) return;

            try
            {
                var errorElement = await page.WaitForSelectorAsync("span.err", new()
                {
                    State = WaitForSelectorState.Visible, // Wait until it's actually visible
                    Timeout = 5000 // Optional: timeout in ms
                });

                if (alreadyPromptedError) return;

                ++_gstIdHandlerIteration;
                alreadyPromptedError = true;

                if (_gstIdHandlerIteration == 1)
                {
                    string errorText = await errorElement!.InnerTextAsync();
                    Console.WriteLine($"GSTIN Not Found for id - {gstin} /t Error - " + errorText);
                    Console.WriteLine("Do you want to skip? [y/n]");

                    var timerTask = Task.Run(async () =>
                    {
                        await Task.Delay(ConfigReader.TimeoutForInvalidId * 1000, token);
                        return string.Empty;
                    });
                    
                    var tsk = await Task.WhenAny(
                        Task.Run(async () =>
                        {
                            await Task.CompletedTask;
                            return Console.ReadLine()!;
                        }),
                        timerTask
                        );

                    string? input = await tsk;

                    return;
                }
            }
            catch
            {
                await Task.Yield();
            }
        }
    }

}