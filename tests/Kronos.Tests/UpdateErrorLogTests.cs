using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the reader for the partial-update log.
/// </summary>
/// <remarks>
/// A portable update writes update-errors.log when it cannot replace every file, naming each one. It was
/// added in 1.55 so a partial update would not be silent - and it was, because nothing read the file back.
/// The log existed to be seen and nobody saw it.
///
/// The writer is a PowerShell script running after the app has exited, so the contents cannot be assumed to
/// be well formed. Every shape below is something that file can genuinely contain, including the ones that
/// mean "do not tell the user anything".
///
/// The fixtures here are the exact strings PortableSelfUpdate emits, so a change to the writer's wording
/// shows up as a failing test rather than as a sentence that reads oddly.
/// </remarks>
[Collection(StorageCollection.Name)]
public class UpdateErrorLogTests : IDisposable
{
    readonly string _root;

    public UpdateErrorLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kronos-updlog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    void WriteLog(string contents) => File.WriteAllText(Path.Combine(_root, UpdateErrorLog.FileName), contents);

    /// <summary>The exact two lines the generated update script writes.</summary>
    const string RealisticLog =
        "3 file(s) could not be replaced; the rest of the update was applied.\r\n" +
        "Kronos.dll\r\n" +
        "Microsoft.UI.Xaml.dll\r\n" +
        "Assets\\icon.ico\r\n";

    [Fact]
    public void NoLogMeansNothingToReport()
    {
        Assert.Null(UpdateErrorLog.TryRead(_root));
    }

    [Fact]
    public void ARealLogIsReported()
    {
        WriteLog(RealisticLog);

        var report = UpdateErrorLog.TryRead(_root);

        Assert.NotNull(report);
        Assert.Equal(3, report!.Value.FileCount);
    }

    [Fact]
    public void AnEmptyLogIsNotReportedAsAFailure()
    {
        // The script creates the file before writing to it, so a run killed in between leaves a zero-byte
        // file. Reporting that on every launch would cry wolf.
        WriteLog(string.Empty);

        Assert.Null(UpdateErrorLog.TryRead(_root));
    }

    [Fact]
    public void AWhitespaceOnlyLogIsNotReported()
    {
        WriteLog("   \r\n  \r\n");

        Assert.Null(UpdateErrorLog.TryRead(_root));
    }

    [Fact]
    public void ATruncatedLogIsStillReportedEvenThoughTheCountIsLost()
    {
        // The script writes the summary line first and appends the file list after it, so a run killed
        // partway through can leave a summary with no entries, or entries with no summary. The latter has no
        // count to show, but the names are the useful part, so it must not be thrown away.
        WriteLog("Kronos.dll\r\nMicrosoft.UI.Xaml.dll\r\n");

        var report = UpdateErrorLog.TryRead(_root);

        Assert.NotNull(report);
        Assert.Null(report!.Value.FileCount);

        var files = UpdateErrorLog.TryParseFailedFiles(report.Value.Details);
        Assert.Equal(2, files.Length);
        Assert.Contains("Kronos.dll", files);

        // And it still describes itself honestly rather than inventing a count.
        var description = UpdateErrorLog.Describe(report.Value);
        Assert.Contains("did not finish", description, StringComparison.Ordinal);
    }

    [Fact]
    public void ASummaryWithNoEntriesUnderItIsStillReported()
    {
        // The other truncation: summary written, list not.
        WriteLog("2 file(s) could not be replaced; the rest of the update was applied.\r\n");

        var report = UpdateErrorLog.TryRead(_root);

        Assert.NotNull(report);
        Assert.Equal(2, report!.Value.FileCount);
        Assert.Empty(UpdateErrorLog.TryParseFailedFiles(report.Value.Details));
    }

    [Fact]
    public void TheFailedFileNamesAreExtracted()
    {
        var files = UpdateErrorLog.TryParseFailedFiles(RealisticLog);

        Assert.Equal(3, files.Length);
        Assert.Contains("Kronos.dll", files);
        Assert.Contains("Assets\\icon.ico", files);
    }

    [Fact]
    public void TheSummaryLineIsNotMistakenForAFileName()
    {
        // The only place the two could be confused: the summary is prose, and the entries below it are paths.
        var files = UpdateErrorLog.TryParseFailedFiles(RealisticLog);

        Assert.DoesNotContain(files, f => f.Contains("could not be replaced", StringComparison.Ordinal));
    }

    [Fact]
    public void ASingleFileIsDescribedInTheSingular()
    {
        WriteLog("1 file(s) could not be replaced; the rest of the update was applied.\r\nKronos.dll\r\n");

        var description = UpdateErrorLog.Describe(UpdateErrorLog.TryRead(_root)!.Value);

        Assert.Contains("1 file.", description, StringComparison.Ordinal);
        Assert.DoesNotContain("1 files.", description, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDescriptionNamesTheFilesAndSaysTheUpdateOtherwiseWorked()
    {
        WriteLog(RealisticLog);

        var description = UpdateErrorLog.Describe(UpdateErrorLog.TryRead(_root)!.Value);

        Assert.Contains("Kronos.dll", description, StringComparison.Ordinal);
        Assert.Contains("updated successfully", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALongListIsTruncatedRatherThanDumpingEveryPath()
    {
        // A whole game folder's worth of locked files should not produce a wall of text in a dialog.
        var many = new[] { "2 file(s) could not be replaced; the rest of the update was applied." }
            .Concat(Enumerable.Range(1, 40).Select(i => $"Locked{i}.dll"))
            .ToArray();
        WriteLog(string.Join(Environment.NewLine, many));

        var description = UpdateErrorLog.Describe(UpdateErrorLog.TryRead(_root)!.Value);

        Assert.Contains("and 35 more", description, StringComparison.Ordinal);
        Assert.DoesNotContain("Locked40.dll", description, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableDirectoryDoesNotThrow()
    {
        // Pointed at a path that cannot exist. Reading a diagnostic must never become the crash it exists
        // to report on.
        Assert.Null(UpdateErrorLog.TryRead(Path.Combine(_root, "does", "not", "exist")));
    }
}