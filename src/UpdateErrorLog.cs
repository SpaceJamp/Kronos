using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Kronos.Helpers;

namespace Kronos;

/// <summary>
/// Reads and summarises the log a partially-applied update leaves behind.
/// </summary>
/// <remarks>
/// A portable update writes <c>update-errors.log</c> beside the executable when it could not replace every
/// file, naming each one. That was added in 1.55 so a partial update would not be silent - and it was, in
/// the worst way, because nothing ever read the file back. The log existed to be seen and no one saw it.
///
/// This is the reader. It is deliberately tolerant: the log is written by a PowerShell script running after
/// the app has exited, so it can be truncated, empty, malformed, or absent, and none of those is an error
/// worth reporting to the user. The only question worth asking is whether the file is there and says
/// anything.
///
/// The log is not deleted when read. A user who reports a problem should be able to open it again, and the
/// next update overwrites it anyway - so deleting it would only make a diagnostic harder to reproduce.
/// </remarks>
internal static class UpdateErrorLog
{
    /// <summary>File name, matching what PortableSelfUpdate's generated script writes.</summary>
    internal const string FileName = "update-errors.log";

    /// <summary>
    /// A short summary of a previous update that did not fully apply, or null when there is nothing to
    /// report.
    /// </summary>
    /// <param name="path">Full path of the log, for opening in Explorer.</param>
    /// <param name="fileCount">How many files could not be replaced, or null if it could not be parsed.</param>
    /// <param name="details">The raw contents, for a dialog that shows the lot.</param>
    public static (string Path, int? FileCount, string Details)? TryRead(string? directory = null)
    {
        try
        {
            var folder = directory ?? AppContext.BaseDirectory;
            var path = Path.Combine(folder, FileName);

            if (File.Exists(path) == false)
            {
                return null;
            }

            var contents = File.ReadAllText(path);

            // An empty file is not a report. The script writes a summary line before the file list, but a
            // run interrupted between creating and writing it leaves a zero-byte file, and treating that as
            // "your update failed" would cry wolf on every launch after it.
            if (string.IsNullOrWhiteSpace(contents))
            {
                return null;
            }

            return (path, TryParseCount(contents), contents);
        }
        catch (Exception err)
        {
            // Reading a diagnostic file must never itself become the crash.
            Logger.Error(err);
            return null;
        }
    }

    /// <summary>
    /// Pulls the file count out of the summary line the update script writes.
    /// </summary>
    /// <remarks>
    /// The line looks like: <c>3 file(s) could not be replaced; the rest of the update was applied.</c>
    /// Returns null when that line is absent or unparseable, because the detail list below it is still worth
    /// showing even if the count is not - a slightly worse report beats no report.
    /// </remarks>
    internal static int? TryParseCount(string contents)
    {
        foreach (var line in contents.Split('\n'))
        {
            var trimmed = line.Trim();

            // The summary line is the only one containing "file(s)", and it starts with the count:
            // "3 file(s) could not be replaced; ...".
            if (IsSummaryLine(trimmed) == false)
            {
                continue;
            }

            var digits = new string(trimmed.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                return count;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a line is the prose summary rather than a file path.
    /// </summary>
    /// <remarks>
    /// The discriminator is the literal "file(s)" from the update script's format string, which is the one
    /// thing in the file that cannot also be a filename.
    ///
    /// Two earlier attempts at this were wrong and are recorded so they are not retried. Matching a prefix
    /// of "could not be replaced" never matched anything, because the summary line begins with the count:
    /// "3 file(s) could not be replaced". Testing whether the line starts with a digit then misclassified
    /// every numbered filename - Locked1.dll, 2_temp.dll - as the summary, so real failures were dropped
    /// from the list.
    /// </remarks>
    internal static bool IsSummaryLine(string line)
    {
        return line.Contains("file(s)", StringComparison.Ordinal);
    }

    /// <summary>
    /// The file names from the log, which is the part a user can act on.
    /// </summary>
    internal static string[] TryParseFailedFiles(string contents)
    {
        try
        {
            return contents
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                // The summary line is prose; everything after it is a path. Matching on "could not be
                // replaced" as a prefix never worked, because that line begins with the count - "3 file(s)
                // could not be replaced" - not with the words.
                .Where(line => IsSummaryLine(line) == false)
                // A relative path, so it may be a bare file name like Kronos.dll as well as one with
                // directories in it. Requiring a separator threw away every failure at the root of the
                // install, which is where the DLLs and the app's own files are - exactly the ones a user
                // most needs to be told about.
                .Where(line => line.Contains('.', StringComparison.Ordinal))
                .ToArray();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            return [];
        }
    }

    /// <summary>
    /// Builds the sentence shown to the user. English only, and deliberately not a translation string:
    /// this is read at startup before the language is known to be applied, and it reports a rare failure
    /// where a mistranslation would be worse than plain English.
    /// </summary>
    public static string Describe((string Path, int? FileCount, string Details) report)
    {
        var count = report.FileCount;
        var files = TryParseFailedFiles(report.Details);

        var headline = count.HasValue
            ? $"The last update could not replace {count.Value} file{(count.Value == 1 ? string.Empty : "s")}."
            : "The last update did not finish.";

        if (files.Length == 0)
        {
            return headline + " The rest of the update was applied.";
        }

        var shown = files.Take(5).ToArray();
        var listing = string.Join(
            Environment.NewLine,
            shown.Select(file => "  " + Path.GetFileName(file)));

        var remainder = files.Length - shown.Length;

        return headline + Environment.NewLine
            + "These files were left as they were:" + Environment.NewLine
            + listing
            + (remainder > 0 ? Environment.NewLine + $"  ...and {remainder} more." : string.Empty)
            + Environment.NewLine
            + "Kronos updated successfully otherwise. This is usually a file held open by another program - "
            + "closing Kronos and running the update again usually sorts it.";
    }
}