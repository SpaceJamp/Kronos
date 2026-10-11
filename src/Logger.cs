using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Kronos;

public enum LoggingLevel : int
{
    Off = 0,
    Verbose = 10,
    Debug = 20,
    Info = 30,
    Warning = 40,
    Error = 50,
}

internal static class Logger
{
    public static string LogDirectory => Path.Combine(Storage.GetTemp(), "logs");
    static string loggingFile => Path.Combine(LogDirectory, "kronos_.log");
#if DEBUG
    static LoggingLevelSwitch levelSwitch = new LoggingLevelSwitch(LogEventLevel.Verbose);
#else
    static LoggingLevelSwitch levelSwitch = new LoggingLevelSwitch(LogEventLevel.Fatal);
#endif

    internal static void Init()
    {
        // Guarded. LogDirectory is under Storage.GetTemp(), and for a portable build that is
        // AppContext.BaseDirectory/StoredData/temp - inside the install folder. A portable build unpacked
        // somewhere the user cannot write (Program Files, a read-only share) makes Storage.GetTemp()'s own
        // directory creation fail, and then this one threw too. That is fatal: Init runs from App's
        // constructor, before the UnhandledException handler is registered a few lines later, so the app
        // dies inside its own constructor with no window and no log explaining why.
        //
        // If the directory cannot be made, carry on and let the file sink fail below. A missing log file
        // is a far better outcome than an app that will not start.
        try
        {
            if (Directory.Exists(LogDirectory) == false)
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }
        catch (Exception err)
        {
            System.Diagnostics.Debug.WriteLine($"Could not create the log directory {LogDirectory}: {err.Message}");
        }

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .Enrich.With(new ThreadAndSessionEnricher());

        // The file sink is optional, not required. WriteTo.File throws at configuration time if it cannot
        // create its file, so on a portable install that cannot be written this would take down startup
        // with the same fatal error as above - while the Debug sink below would have worked perfectly well
        // on its own. Losing the log file is survivable; not launching is not.
        try
        {
            loggerConfig = loggerConfig.WriteTo.File(
                loggingFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: OutputTemplate);
        }
        catch (Exception err)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open the log file {loggingFile}: {err.Message}");
        }

#if WINDOWS
        loggerConfig = loggerConfig.WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture);
#else
        loggerConfig = loggerConfig.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
#endif

        Log.Logger = loggerConfig.CreateLogger();

        ChangeLoggingLevel(Settings.Instance.LoggingLevel);
        WriteSessionHeader();
    }

    /// <summary>
    /// Adds the thread name, thread id and session id to every event.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than taken from Serilog.Enrichers, which is a separate package that is
    /// not referenced here. Three fields did not seem worth a new dependency, and a dependency on an
    /// unreferenced package is how the build breaks the next time Serilog is updated.
    ///
    /// The thread name matters because crashes arrive on background threads, and the install size
    /// measurement thread is deliberately named for exactly this reason. Without it, a stack trace on
    /// a thread pool thread says nothing about which work item was running.
    /// </remarks>
    internal sealed class ThreadAndSessionEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent is null || propertyFactory is null)
            {
                return;
            }

            var thread = Thread.CurrentThread;

            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadName", thread.Name ?? "unnamed"));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadId", thread.ManagedThreadId));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Session", SessionId));
        }
    }

    /// <summary>
    /// Identifies this run, so lines from separate launches are not mistaken for one session.
    /// </summary>
    internal static string SessionId { get; } = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture)
        + "-" + Guid.NewGuid().ToString("N")[..6];

    /// <summary>
    /// Line format: timestamp, level, thread, short file:line, then the message.
    /// </summary>
    /// <remarks>
    /// The thread name and id were added because crashes arrive on background threads, and a stack
    /// trace alone does not say which work item was running. The install size measurement thread and
    /// the async swap command are exactly the kind of thing that used to be indistinguishable.
    ///
    /// The message is last and unbounded, so multi line exception details stay readable instead of
    /// being truncated per field.
    /// </remarks>
    internal const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {ThreadName}/{ThreadId} {Properties:j} {SourceContext} - {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Writes a summary of this build and environment at the start of every session.
    /// </summary>
    /// <remarks>
    /// A log that does not say which build produced it is close to useless for reporting a crash,
    /// because the first question is always "are you on the version with the fix". The exact
    /// informational version matters most here: it carries the commit hash, so it distinguishes two
    /// locally built binaries that both claim to be 1.46. That distinction is what identified a
    /// stale install during the COMException investigation, after the stack trace had already run out.
    /// </remarks>
    internal static void WriteSessionHeader()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly();
            var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            var version = assembly?.GetName().Version?.ToString() ?? "unknown";

            Log.Information("---- Kronos session {0} starting ----", SessionId);
            Log.Information("  version            {0}", version);
            Log.Information("  informational      {0}", informational);
            Log.Information("  commit             {0}", ExtractCommit(informational));
            Log.Information("  location           {0}", SafeLocation());
            Log.Information("  os                 {0}", Environment.OSVersion.VersionString);
            Log.Information("  64-bit process     {0}", Environment.Is64BitProcess);
            Log.Information("  portable           {0}", IsPortable());
            Log.Information("  install mode       {0}", SafeInstallMode());
            Log.Information("  log directory      {0}", LogDirectory);
            Log.Information("  log level          {0}", Settings.Instance.LoggingLevel);
            Log.Information("---- session {0} header complete ----", SessionId);
        }
        catch (Exception err)
        {
            // A header that throws must not stop the app starting, and must not stop the logger
            // working, since this is the code that runs before anything else does.
            Log.Warning("Could not write the session header: {0}", err.Message);
        }
    }

    /// <summary>
    /// Pulls the short commit hash out of an informational version.
    /// </summary>
    /// <remarks>
    /// The SDK appends "+" plus the full hash to <see cref="AssemblyInformationalVersionAttribute"/>.
    /// Shortened to 8 characters because the full 40 is noise in a log header nobody reads closely,
    /// and 8 is enough to identify a commit.
    /// </remarks>
    internal static string ExtractCommit(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "unknown";
        }

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0 || plus + 1 >= informationalVersion.Length)
        {
            return "none (not a git build)";
        }

        var hash = informationalVersion[(plus + 1)..];

        return hash.Length <= 8 ? hash : hash[..8];
    }

    /// <summary>
    /// Whether this is a portable build, which changes where data and the database live.
    /// </summary>
    internal static bool IsPortable()
    {
#if PORTABLE
        return true;
#else
        return false;
#endif
    }

    static string SafeLocation()
    {
        try
        {
            return AppContext.BaseDirectory;
        }
        catch
        {
            return "unavailable";
        }
    }

    /// <summary>
    /// Whether this install is per user or machine wide, read from where the app is running.
    /// </summary>
    /// <remarks>
    /// Derived from the path rather than by reading the uninstall registry, because this is called
    /// during startup and must not fail or block. Program Files means machine wide, anything else
    /// means per user, which is right for both of the scopes the installer offers.
    /// </remarks>
    internal static string SafeInstallMode()
    {
        try
        {
            var location = AppContext.BaseDirectory;
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (string.IsNullOrEmpty(programFiles) == false && location.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            {
                return "all users (Program Files)";
            }

            return "per user";
        }
        catch
        {
            return "unknown";
        }
    }

    public static string GetCurrentLogPath()
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(loggingFile);
        var justExtension = Path.GetExtension(loggingFile);
        return Path.Combine(LogDirectory, $"{withoutExtension}{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}{justExtension}");
    }

    public static void ChangeLoggingLevel(LoggingLevel loggingLevel)
    {
        // This used to read Settings.Instance.LoggingLevel instead of the parameter, which made
        // the argument dead code. Serilog has no level below Fatal, so "Off" maps to Fatal --
        // if we ever need true silence we should dispose/replace the logger instead.
        levelSwitch.MinimumLevel = loggingLevel switch
        {
            LoggingLevel.Verbose => LogEventLevel.Verbose,
            LoggingLevel.Debug => LogEventLevel.Debug,
            LoggingLevel.Info => LogEventLevel.Information,
            LoggingLevel.Warning => LogEventLevel.Warning,
            LoggingLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Fatal,
        };
    }


    public static void Verbose(string message, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log.Verbose(FormatLine(message, memberName, sourceFilePath, sourceLineNumber));
    }

    public static void Debug(string message, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log.Debug(FormatLine(message, memberName, sourceFilePath, sourceLineNumber));
    }

    public static void Info(string message, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log.Information(FormatLine(message, memberName, sourceFilePath, sourceLineNumber));
    }

    public static void Warning(string message, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log.Warning(FormatLine(message, memberName, sourceFilePath, sourceLineNumber));
    }

    public static void Error(string message, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        Log.Error(FormatLine(message, memberName, sourceFilePath, sourceLineNumber));
    }

    public static void Error(Exception exception, string? message = null, [CallerMemberName] string? memberName = null, [CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Log.Error(FormatLine($"{exception}\n{exception.StackTrace}", memberName, sourceFilePath, sourceLineNumber));
        }
        else
        {
            Log.Error(FormatLine($"{message}\n{exception}\n{exception.StackTrace}", memberName, sourceFilePath, sourceLineNumber));
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static string FormatLine(string message, string? memberName, string? sourceFilePath, int sourceLineNumber)
    {
        if (memberName is null || sourceFilePath is null || sourceLineNumber == 0)
        {
            return message;
        }

        return $"{Path.GetFileName(sourceFilePath)}:{sourceLineNumber} {memberName} - {message}";
    }
}
