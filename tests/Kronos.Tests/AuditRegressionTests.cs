using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the fixes from the audit that were silent failures rather than data loss.
/// </summary>
/// <remarks>
/// Each of these was a place where an exception or an absent precondition took down something the user
/// needed, with no message. They are asserted on by reading the source, because the behaviour in question
/// happens during startup and inside an OS-level API, neither of which a test can drive directly.
///
/// The alternative - no assertion at all - is what these had, and it is how the DLL library ended up
/// rendering empty and the app terminating with no window.
/// </remarks>
public class AuditRegressionTests
{
    static string Source(params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(relativeParts).ToArray()));

    static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void TheLegacyZipFolderCleanupCannotAbortTheDllLibraryLoad()
    {
        // Directory.GetFiles threw DirectoryNotFoundException or UnauthorizedAccessException from between
        // two try blocks, escaping CheckDllRecordForMigration_117 and then ProcessManifestsAsync, which has
        // no try of its own. LoadLocalRecords then never ran for any asset type, so every
        // DLLRecord.LocalRecord stayed null and the DLL list rendered empty. Tidying a folder we have
        // finished with must not be able to do that.
        var source = Source(Path.Combine("src", "Data", "DLLManager.cs"));

        Assert.Contains("Could not clean up", source, StringComparison.Ordinal);

        // The check and the delete now share one guarded block.
        var guardedStart = source.IndexOf("Could not clean up", StringComparison.Ordinal);
        var before = source.Substring(Math.Max(0, guardedStart - 900), Math.Min(900, guardedStart));

        Assert.Contains("try", before, StringComparison.Ordinal);
    }

    [Fact]
    public void LoggerStartupCannotThrow()
    {
        // Logger.Init runs from App's constructor, before UnhandledException is registered a few lines
        // later. Both the directory creation and the file sink used to throw for a portable build unpacked
        // somewhere unwritable, so the app died inside its own constructor: no window, no log.
        var source = Source(Path.Combine("src", "Logger.cs"));

        Assert.Contains("Could not create the log directory", source, StringComparison.Ordinal);
        Assert.Contains("Could not open the log file", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMainWindowBackstopAbortsRatherThanReturningAHalfBuiltWindow()
    {
        // A bare return skipped InitializeComponent, leaving ViewModel null, and App still showed the
        // window. The user got an empty frame and the first navigation threw on a null ViewModel.
        var source = Source(Path.Combine("src", "MainWindow.xaml.cs"));

        Assert.Contains("throw new NotSupportedException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("            return;\n        }\n\n        this.InitializeComponent();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMainWindowBackstopIsCaughtSoTheUserGetsAnExplanationWindow()
    {
        // Throwing is only better than returning if something handles it. The unhandled handler logs but
        // does not set e.Handled, so an uncaught NotSupportedException would simply kill the app.
        var source = Source(Path.Combine("src", "App.xaml.cs"));

        Assert.Contains("catch (NotSupportedException", source, StringComparison.Ordinal);
        Assert.Contains("new UnsupportedWindowsVersionWindow()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNativeTrustStateHandleIsReleasedEvenWhenVerificationThrows()
    {
        // WinVerifyTrust allocates a native state block released only by a second call with
        // dwStateAction = Close. That call sat at the end of the try, so any exception from the verify
        // leaked the handle - once per failed verification, and a mass update verifies many files.
        var source = Source(Path.Combine("src", "WinTrust.cs"));

        var closeIndex = source.IndexOf("WinTrustDataStateAction.Close", StringComparison.Ordinal);
        var finallyIndex = source.IndexOf("finally", StringComparison.Ordinal);

        Assert.True(closeIndex > 0, "the close call should still exist");
        Assert.True(
            finallyIndex > 0 && closeIndex > finallyIndex,
            "the close must live in the finally, after the try body, so it runs on the exception path too");
    }

    [Fact]
    public void DeletingALocalRecordClearsItsFlagsBeforeTidyingUpTheFolder()
    {
        // The flags were updated after a directory cleanup that could throw, so a failure there left a
        // record claiming to be downloaded for a file that had already been deleted.
        var source = Source(Path.Combine("src", "Data", "LocalRecord.cs"));

        var deleteIndex = source.IndexOf("File.Delete(ExpectedPath)", StringComparison.Ordinal);
        var flagIndex = source.IndexOf("IsDownloaded = false;", StringComparison.Ordinal);

        Assert.True(deleteIndex > 0, "the file delete should be found");
        Assert.True(
            flagIndex > deleteIndex,
            "IsDownloaded must be cleared straight after the delete, not after the folder cleanup that can throw");
    }

    [Fact]
    public void GameAssetRewritesRunInsideATransaction()
    {
        // Delete-then-insert as two statements left a game with no tracked DLLs at all if anything failed
        // between them, which is the half-state the backup chain and the reset button both depend on.
        var source = Source(Path.Combine("src", "Data", "Game.cs"));

        Assert.Contains("RewriteGameAssetsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync(\"BEGIN\")", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync(\"COMMIT\")", source, StringComparison.Ordinal);
        Assert.Contains("ExecuteAsync(\"ROLLBACK\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletingAndReinsertingAGamesAssetsAlwaysGoesThroughTheTransactionalHelper()
    {
        // One deliberate DELETE remains outside the helper: RemoveGameAssetsFromCacheAsync, which only
        // deletes and has no insert to pair with. Any other bare DELETE has come back.
        var source = Source(Path.Combine("src", "Data", "Game.cs"));

        var deleteSites = source
            .Split("DELETE FROM game_asset WHERE id")
            .Length - 1;

        // The helper itself plus RemoveGameAssetsFromCacheAsync's single delete.
        Assert.Equal(2, deleteSites);
    }

    [Fact]
    public void SwappingAndResettingWriteTheGameDllAtomically()
    {
        // File.Copy truncates the destination to zero before writing, so a kill, a full disk or antivirus
        // left a truncated nvngx_dlss.dll in the game folder - the exact state the backup chain exists to
        // prevent, with no UI anywhere reporting it.
        var source = Source(Path.Combine("src", "Data", "Game.cs"));

        Assert.DoesNotContain("File.Copy(dllRecord.LocalRecord.ExpectedPath, existingRecord.Path", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy(existingBackupRecord.Path, existingRecord.Path", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("AtomicFile.Replace").Length - 1 >= 2,
            "both the swap and the reset should go through AtomicFile.Replace");
    }

    [Fact]
    public void TheUnknownAssetListIsClearedUnderItsLock()
    {
        // LoadGamesAsync takes unknownGameAsseetLock for the identical clear. LoadGamesFromCacheAsync did
        // not, so a scan adding entries on a thread pool thread could race it.
        var source = Source(Path.Combine("src", "Data", "GameManager.cs"));

        // Every Clear of the list must be immediately preceded by taking its lock. Both call sites
        // used to be counted; one of them did not take the lock.
        var lines = source.Split('\n');
        var unguarded = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("_unknownGameAssets.Clear()"))
            {
                continue;
            }

            // Walk back over the guard block: the clear may sit a couple of lines inside it.
            var guarded = false;
            for (var j = i; j >= 0 && j > i - 5; j--)
            {
                if (lines[j].Contains("lock (unknownGameAsseetLock)"))
                {
                    guarded = true;
                    break;
                }
            }

            if (!guarded)
            {
                unguarded++;
            }
        }

        Assert.Equal(0, unguarded);
    }
}