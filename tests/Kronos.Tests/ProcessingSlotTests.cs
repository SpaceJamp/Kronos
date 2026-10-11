using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the rule that a claimed processing slot is always released.
/// </summary>
/// <remarks>
/// The v1.58.1 refresh fix claimed a game's scan slot atomically, then released it in a `catch`. That was
/// wrong in a way its own comment contradicted: a `return` from inside a `try` does not run its `catch`, so
/// the two early returns - an empty install path, and an install path that does not exist - released
/// nothing.
///
/// The consequence is a game permanently marked as processing. It would not be scanned again for the rest of
/// the session: not by a reload, and not by a Refresh, because RefreshGamesButtonAsync calls into the same
/// path. A game whose folder had been moved or deleted - a common thing after a launcher verifies files -
/// would silently stop responding to every refresh for the rest of the run.
///
/// So this is asserted as the control-flow property it is: every path that returns after claiming must
/// release. That is checked against a miniature of the real structure rather than by reading the source,
/// because the mistake was invisible when reading the source - the comment said the opposite of what the
/// code did.
/// </remarks>
public class ProcessingSlotTests
{
    /// <summary>
    /// The shape of ProcessGame's set-up, reduced to what matters: claim, bail out or hand over.
    /// </summary>
    /// <remarks>
    /// Counters rather than a returned tuple, because a `return` from inside a `try` discards the tuple the
    /// caller would have seen - the counters are what the finally actually touches. That is not a subtlety
    /// being tested here; it is a trap this test walked into while being written, and the counts make the
    /// property visible regardless of where control leaves.
    /// </remarks>
    static (int Claimed, int Released) RunSetup(bool installPathIsEmpty, bool directoryMissing, bool throwsDuringSetup)
    {
        var claimed = 0;
        var released = 0;
        var processing = false;

        if (InterlockedCompareExchangeSlot(ref claimed) == false)
        {
            return (claimed, released);
        }

        try
        {
            if (throwsDuringSetup)
            {
                throw new InvalidOperationException("simulated failure during set-up");
            }

            if (installPathIsEmpty == false && directoryMissing == false)
            {
                // The only path that hands the slot to the background work.
                processing = true;
            }

            // The early returns of the real method are modelled by falling out of the try instead of
            // returning from inside it. A `return` would leave the method and discard these counters
            // entirely, which is a property of the test harness rather than of the code under test - and
            // exactly the sort of thing that makes a test of this kind lie.
        }
        catch
        {
            // Swallowed: the assertion is about the finally, and an escaping exception would hide the
            // counts for the throw case.
        }
        finally
        {
            if (processing == false)
            {
                released++;
            }
        }

        return (claimed, released);
    }

    /// <summary>Stands in for Game.TryEnterProcessing's atomic claim.</summary>
    static bool InterlockedCompareExchangeSlot(ref int flag)
    {
        var previous = flag;
        flag = 1;
        return previous == 0;
    }

    [Fact]
    public void AnEmptyInstallPathReleasesTheSlot()
    {
        var (claimed, released) = RunSetup(installPathIsEmpty: true, directoryMissing: false, throwsDuringSetup: false);

        Assert.Equal(1, claimed);
        Assert.Equal(released, claimed);
    }

    [Fact]
    public void AMissingInstallDirectoryReleasesTheSlot()
    {
        // A launcher verifying its files, or a game being moved or deleted, produces exactly this. It is
        // the common case, and it is what left games stuck.
        var (claimed, released) = RunSetup(installPathIsEmpty: false, directoryMissing: true, throwsDuringSetup: false);

        Assert.Equal(1, claimed);
        Assert.Equal(released, claimed);
    }

    [Fact]
    public void AFailureDuringSetUpReleasesTheSlot()
    {
        var (claimed, released) = RunSetup(installPathIsEmpty: false, directoryMissing: false, throwsDuringSetup: true);

        Assert.Equal(1, claimed);
        Assert.Equal(released, claimed);
    }

    [Fact]
    public void TheSlotIsNotReleasedOnceTheBackgroundWorkOwnsIt()
    {
        // The other half. Releasing here as well as in the background work's own finally would let a second
        // scan start while the first is still running - the precise bug the slot was introduced to prevent.
        var claimed = 0;
        var released = 0;
        var processing = false;

        if (InterlockedCompareExchangeSlot(ref claimed) == false)
        {
            return;
        }

        try
        {
            processing = true;
        }
        finally
        {
            if (processing == false)
            {
                released++;
            }
        }

        Assert.Equal(0, released);
        Assert.Equal(1, claimed);
    }

    [Fact]
    public void ProcessGameReleasesTheSlotInAFinallyRatherThanACatch()
    {
        // The structural guard. A catch does not run on a return, which is precisely the bug: the code read
        // correctly and the comment above it even described the correct behaviour, while the mechanism did
        // the opposite. Asserted on the source because the defect is a language property, not a value.
        var source = ReadGameSource();

        var start = source.IndexOf("public void ProcessGame", StringComparison.Ordinal);
        Assert.True(start > 0, "ProcessGame should be found");

        // The whole method, not a fixed window. The hand-off to Task.Run sits past 3300 characters into it,
        // so a 3000-character slice truncated the very anchor the assertion needed - which produced a
        // failure that looked like a code problem and was a test problem.
        var nextMethod = source.IndexOf("void LoadBackupForGameAsset", start, StringComparison.Ordinal);
        var body = source.Substring(start, (nextMethod > start ? nextMethod : source.Length) - start);

        // Anchored on the real code, not on comment text.
        //
        // Getting these anchors wrong took three attempts, and the near-misses are the point: "Task.Run"
        // appears in the comment above the set-up, before the code it describes; the first
        // "UiDispatcher.Invoke" and the first "finally" are both inside comments. A source-scanning test
        // whose anchors match the surrounding prose will quietly assert nothing useful.
        var installPathCheck = body.IndexOf("string.IsNullOrEmpty(InstallPath)", StringComparison.Ordinal);
        var directoryCheck = body.IndexOf("Directory.Exists(InstallPath) == false", StringComparison.Ordinal);
        var handoff = body.IndexOf("Processing = true", StringComparison.Ordinal);
        var backgroundWork = body.IndexOf("_ = Task.Run", StringComparison.Ordinal);

        Assert.True(installPathCheck > 0, "the install path check should be found");
        Assert.True(directoryCheck > installPathCheck, "both early returns should precede the hand-off");
        Assert.True(handoff > directoryCheck, "Processing = true marks the hand-off to the background work");
        Assert.True(backgroundWork > handoff, "Task.Run comes after the hand-off");

        // The span that matters: from the first early return through the hand-off and the release. The finally
        // that does the releasing sits just after Processing = true, guarded by a check so it only fires
        // while this method still owns the slot.
        var setup = body.Substring(directoryCheck, backgroundWork - directoryCheck);

        Assert.Contains("finally", setup, StringComparison.Ordinal);
        Assert.Contains("ExitProcessing", setup, StringComparison.Ordinal);
        Assert.Contains("if (Processing == false)", setup, StringComparison.Ordinal);

        // Both early returns must be inside that try, not after it. A return placed outside the try would
        // leave the finally behind and leak, which is the original bug in a different position.
        var tryStart = body.LastIndexOf("try", directoryCheck, StringComparison.Ordinal);
        Assert.True(tryStart > 0 && tryStart < directoryCheck, "both early returns should sit inside one try");

        // And the mistake must not be back in the shape it first took: a catch releasing the slot, with no
        // finally anywhere beside it.
        Assert.DoesNotContain("} catch {", setup, StringComparison.Ordinal);
    }

    static string ReadGameSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Data", "Game.cs"));
    }
}