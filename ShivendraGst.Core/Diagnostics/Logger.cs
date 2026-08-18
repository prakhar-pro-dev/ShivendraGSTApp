using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace ShivendraGst.Core;

public enum LogLevel
{
    /// <summary>Written to the log file only. Use for high-volume retry/polling noise.</summary>
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

/// <summary>
/// Writes a fresh log file for every run of the application.
///
/// The file is truncated on the first write of each run, so it always describes the
/// latest run only. Info/Warning/Error also go to the console, preserving the
/// interactive output the app already had; Debug is file-only so retry loops can be
/// recorded in full without flooding the screen.
///
/// Logging never throws. If the log file cannot be opened or written, file logging
/// switches off for the rest of the run and the console keeps working - a logger that
/// throws from inside a catch block would hide the original error.
/// </summary>
public static class Logger
{
    private const string LogFolderName = "Logs";
    private const string LogFileName = "latest-run.log";

    private static readonly object _sync = new();

    private static StreamWriter? _writer;
    private static bool _initialised;
    private static bool _fileLoggingDisabled;
    private static bool _hooksRegistered;

    /// <summary>Messages below this level are dropped entirely.</summary>
    public static LogLevel MinimumLevel = LogLevel.Debug;

    /// <summary>Full path of the current run's log file, empty until the first write.</summary>
    public static string LogFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Raised for every message that reaches the console, so a GUI front end can show the
    /// same lines in a log pane. Debug messages are file-only and are not raised.
    ///
    /// Handlers run on whichever thread logged the message and inside the logger's lock,
    /// so they must not block - a WinForms handler should marshal to the UI thread with
    /// BeginInvoke rather than Invoke. Handler exceptions are swallowed: a broken log view
    /// must not take down the run.
    /// </summary>
    public static event Action<LogLevel, string>? MessageWritten;

    /// <summary>
    /// Starts the log file and writes the run header. Optional - any log call
    /// initialises the logger on demand - but calling it first makes the header the
    /// first thing in the file.
    /// </summary>
    public static void Initialize()
    {
        lock (_sync)
        {
            EnsureInitialised();
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);

    public static void Info(string message) => Write(LogLevel.Info, message, null);

    public static void Warning(string message) => Write(LogLevel.Warning, message, null);

    public static void Error(string message) => Write(LogLevel.Error, message, null);

    /// <summary>
    /// Logs an exception. The console gets the message plus <see cref="Exception.Message"/>;
    /// the log file gets the full exception including stack trace and inner exceptions.
    /// </summary>
    public static void Error(string message, Exception exception) => Write(LogLevel.Error, message, exception);

    /// <summary>
    /// Logs a warning that carries an exception, for recoverable failures such as an
    /// exhausted retry loop.
    /// </summary>
    public static void Warning(string message, Exception exception) => Write(LogLevel.Warning, message, exception);

    /// <summary>
    /// Writes an interactive prompt to the console without a trailing newline and
    /// records it in the log, so the log shows what the user was asked.
    /// </summary>
    public static void Prompt(string message)
    {
        lock (_sync)
        {
            EnsureInitialised();
            Console.Write(message);
            WriteToFile(Format(LogLevel.Info, "[prompt] " + message));
        }
    }

    /// <summary>Records what the user typed at a prompt (or that the prompt timed out).</summary>
    public static void PromptResponse(string? response)
    {
        string text = string.IsNullOrWhiteSpace(response) ? "<no response / timed out>" : response;
        lock (_sync)
        {
            EnsureInitialised();
            WriteToFile(Format(LogLevel.Info, "[prompt response] " + text));
        }
    }

    /// <summary>Writes the run footer and closes the log file.</summary>
    public static void Shutdown()
    {
        lock (_sync)
        {
            if (_writer is null) return;

            WriteToFile(new string('-', 78));
            WriteToFile(Format(LogLevel.Info, "Run finished."));

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (IOException)
            {
                // Nothing useful left to do while shutting down.
            }
            catch (ObjectDisposedException)
            {
            }

            _writer = null;
        }
    }

    private static void Write(LogLevel level, string message, Exception? exception)
    {
        if (level < MinimumLevel) return;

        lock (_sync)
        {
            EnsureInitialised();

            // Console keeps the plain wording the app already used; only the file
            // carries timestamps, levels and full exception detail.
            if (level > LogLevel.Debug)
            {
                string consoleText = exception is null
                    ? message
                    : message + " Error - " + exception.Message;

                WriteToConsole(level, consoleText);
                RaiseMessageWritten(level, consoleText);
            }

            WriteToFile(Format(level, message));

            if (exception is not null)
            {
                WriteToFile(exception.ToString());
            }
        }
    }

    private static void RaiseMessageWritten(LogLevel level, string text)
    {
        Action<LogLevel, string>? handler = MessageWritten;
        if (handler is null) return;

        try
        {
            handler(level, text);
        }
        catch (Exception ex)
        {
            // A failing log view must never break the scrape, and must never recurse
            // back into Write - go straight to the file.
            WriteToFile(Format(LogLevel.Warning, $"A log listener threw and was ignored: {ex.Message}"));
        }
    }

    private static string Format(LogLevel level, string message)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-7}] {2}",
            DateTime.Now,
            level.ToString().ToUpperInvariant(),
            message);
    }

    private static void WriteToConsole(LogLevel level, string text)
    {
        ConsoleColor? colour = level switch
        {
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => null
        };

        if (colour is null)
        {
            Console.WriteLine(text);
            return;
        }

        ConsoleColor original = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = colour.Value;
            Console.WriteLine(text);
        }
        finally
        {
            Console.ForegroundColor = original;
        }
    }

    /// <summary>Callers must hold <see cref="_sync"/>.</summary>
    private static void WriteToFile(string line)
    {
        if (_writer is null) return;

        try
        {
            _writer.WriteLine(line);
        }
        catch (IOException ex)
        {
            DisableFileLogging(ex);
        }
        catch (ObjectDisposedException ex)
        {
            DisableFileLogging(ex);
        }
    }

    /// <summary>Callers must hold <see cref="_sync"/>.</summary>
    private static void DisableFileLogging(Exception ex)
    {
        _writer = null;
        _fileLoggingDisabled = true;
        Console.WriteLine($"Logging to '{LogFilePath}' stopped: {ex.Message}");
    }

    /// <summary>Callers must hold <see cref="_sync"/>.</summary>
    private static void EnsureInitialised()
    {
        if (_initialised) return;
        _initialised = true;

        RegisterProcessHooks();

        if (_fileLoggingDisabled) return;

        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, LogFolderName);
            Directory.CreateDirectory(folder);
            LogFilePath = Path.Combine(folder, LogFileName);

            // append: false truncates any previous run's file, so the log always
            // describes the latest run only.
            _writer = new StreamWriter(LogFilePath, append: false)
            {
                AutoFlush = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _writer = null;
            _fileLoggingDisabled = true;
            Console.WriteLine($"Could not create the log file: {ex.Message}");
            return;
        }

        WriteHeader();
        Console.WriteLine($"Logging this run to {LogFilePath}");
    }

    /// <summary>Callers must hold <see cref="_sync"/>.</summary>
    private static void WriteHeader()
    {
        Assembly assembly = typeof(Logger).Assembly;

        // InformationalVersion is the <Version> from the csproj verbatim (the SDK also
        // appends the source revision when building in a git repo), so the log header
        // names the exact build. Fall back to the numeric assembly version if it is
        // somehow absent.
        string name = assembly.GetName().Name ?? "ShivendraGst";
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString()
                         ?? "unknown";

        WriteToFile(new string('=', 78));
        WriteToFile($"{name} v{version}");
        WriteToFile($"Run started : {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
        WriteToFile($"Machine     : {Environment.MachineName}");
        WriteToFile($"User        : {Environment.UserName}");
        WriteToFile($"Base folder : {AppContext.BaseDirectory}");
        WriteToFile(new string('=', 78));
    }

    /// <summary>
    /// Makes sure the log is flushed and closed however the process ends, including the
    /// Environment.Exit call in the page-load handler, and captures failures that escape
    /// the try/catch blocks - notably from async event handlers, whose exceptions would
    /// otherwise vanish.
    /// Callers must hold <see cref="_sync"/>.
    /// </summary>
    private static void RegisterProcessHooks()
    {
        if (_hooksRegistered) return;
        _hooksRegistered = true;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                Error("Unhandled exception.", ex);
            }
            else
            {
                Error($"Unhandled non-exception error: {args.ExceptionObject}");
            }

            Shutdown();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };
    }
}
