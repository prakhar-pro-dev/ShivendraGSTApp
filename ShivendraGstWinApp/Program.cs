using ShivendraGst.Core;
using System;
using System.Windows.Forms;

namespace ShivendraGstWinApp;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Starts the log file and reads configFile.json before any UI appears, so a bad
        // config shows up in the log rather than as an empty form.
        AppConfig.EnsureLoaded();

        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            Logger.Shutdown();
        }
    }
}
