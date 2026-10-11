using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kronos;

/// <summary>
/// Builds what the user is shown when the app hits an unhandled exception.
/// </summary>
/// <remarks>
/// The handler used to log the exception, close Serilog, and let the process die. That is honest but
/// useless: the app vanishes, the user has no idea whether anything was saved, and the log it just wrote is
/// somewhere they will never look.
///
/// So the crash is reported in the app's own terms - what happened, and where the log is - and the handler
/// marks the exception handled so the user keeps whatever window they already had open. Losing an operation
/// mid-flight is recoverable; losing the library and having to re-scan is not, and closing the whole app
/// because something in a background task threw is a poor trade.
///
/// This is a separate, static, string-only class so it can be asserted on without a running application,
/// which is what App_UnhandledException could never be. A crash reporter with no tests is how it got here.
/// </remarks>
internal static class CrashReport
{
    /// <summary>
    /// The full text shown in the crash dialog: what happened, the log's location, and what to do next.
    /// </summary>
    /// <param name="exception">The exception that was not handled.</param>
    /// <param name="logPath">Where the log file is, so the user can attach it to a bug report.</param>
    public static string Build(Exception exception, string? logPath)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Kronos hit a problem it could not recover from, and has closed the operation that was running.");
        builder.AppendLine();
        builder.AppendLine("Anything already saved is safe. The log below records what happened and is worth attaching to a bug report.");
        builder.AppendLine();

        if (string.IsNullOrWhiteSpace(logPath) == false)
        {
            builder.AppendLine("Log file:");
            builder.AppendLine(logPath);
            builder.AppendLine();
        }

        builder.AppendLine("Error:");
        builder.AppendLine(exception?.GetType().Name ?? "Unknown");
        builder.AppendLine(exception?.Message ?? string.Empty);

        if (exception?.StackTrace is { Length: > 0 } stack)
        {
            builder.AppendLine();
            builder.AppendLine(stack);
        }

        // An AggregateException is what Task.WhenAll and async machinery wrap things in, and its own message
        // is usually the least useful part. The inner exceptions are what actually went wrong.
        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"({aggregate.InnerExceptions.Count} nested error{(aggregate.InnerExceptions.Count == 1 ? string.Empty : "s")}):");

            foreach (var inner in aggregate.InnerExceptions.Take(5))
            {
                builder.AppendLine($"  {inner.GetType().Name}: {inner.Message}");
            }
        }

        // A very long stack is still useful, but a dialog that cannot be read past its own scrollbar is not,
        // so it is capped with the omission stated rather than silently truncated.
        var text = builder.ToString();
        const int maxLength = 6000;

        if (text.Length <= maxLength)
        {
            return text;
        }

        return string.Concat(
            text.AsSpan(0, maxLength),
            $"{Environment.NewLine}... truncated. The full text is in the log file.");
    }

    /// <summary>
    /// The one-line version, for a title bar or a notification.
    /// </summary>
    public static string BuildTitle(Exception exception)
    {
        var name = exception?.GetType().Name ?? "Unknown";
        var message = exception?.Message;

        if (string.IsNullOrWhiteSpace(message))
        {
            return $"{name} - Kronos";
        }

        // Newlines would break a title bar, and a full stack trace in one is unreadable at that size.
        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (singleLine.Length > 120)
        {
            singleLine = string.Concat(singleLine.AsSpan(0, 117), "...");
        }

        return $"{name}: {singleLine}";
    }

    /// <summary>
    /// Whether an exception is worth interrupting the user for.
    /// </summary>
    /// <remarks>
    /// Cancellation is not a failure. A cancelled download or a closed dialog throws
    /// OperationCanceledException / TaskCanceledException, and treating that as a crash would mean every
    /// user who closed a dialog mid-download got an error dialog about it.
    ///
    /// OutOfMemoryException is excluded for the opposite reason: by the time it is thrown the process is
    /// usually in no state to show a dialog, and attempting to build one allocates strings. It is still
    /// logged, but not surfaced.
    /// </remarks>
    public static bool ShouldReport(Exception exception)
    {
        if (exception is null)
        {
            return false;
        }

        if (exception is OperationCanceledException or TaskCanceledException)
        {
            return false;
        }

        if (exception is OutOfMemoryException or StackOverflowException)
        {
            // StackOverflowException cannot be caught at all, so naming it here is only about the other two.
            return false;
        }

        return true;
    }

    /// <summary>
    /// The current log file's directory, or null when it cannot be determined.
    /// </summary>
    /// <remarks>
    /// Serilog writes to a rolling daily file, so the file the user needs is today's. The directory is what
    /// matters for opening it, and is stable regardless of which day's file is current.
    /// </remarks>
    public static string? TryGetLogDirectory()
    {
        try
        {
            return Logger.LogDirectory;
        }
        catch (Exception err)
        {
            // Reporting a crash must not itself throw. Logger.LogDirectory resolves a storage path, which
            // touches the filesystem, and this is running while the app is already failing.
            System.Diagnostics.Debug.WriteLine($"Could not resolve the log directory: {err.Message}");
            return null;
        }
    }

    /// <summary>
    /// The path to name in the crash dialog: today's log file if it can be found, else the directory.
    /// </summary>
    internal static string? TryGetLogPathForDisplay(DateTime date)
    {
        var directory = TryGetLogDirectory();
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            // Matches Serilog's rollingInterval: Day, with the default kronos_ prefix and .log extension.
            var expected = Path.Combine(directory, $"kronos_{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

            return File.Exists(expected) ? expected : directory;
        }
        catch (Exception err)
        {
            System.Diagnostics.Debug.WriteLine($"Could not resolve the log file: {err.Message}");
            return directory;
        }
    }
}