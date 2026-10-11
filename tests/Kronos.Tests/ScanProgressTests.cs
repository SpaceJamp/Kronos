using System;
using System.IO;
using System.Linq;
using Kronos.Data;
using Kronos.Pages;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the scan progress the games page now shows.
/// </summary>
/// <remarks>
/// Games were scanned with no indication of how many were left, while a game being scanned refuses to open
/// ("Game Currently Processing"). A large library therefore looked like an unexplained wait, with no way to
/// tell a nearly-finished load from a stuck one.
///
/// The counters live on Game because ProcessGame is the single funnel every store library goes through -
/// Steam, GOG, Epic, Xbox, BattleNet, Ubisoft, EA and manual all call it - so counting there cannot miss a
/// library that a manager-side count would have had to be taught about separately.
///
/// In the shared collection because the counters are static and these tests reset them. Another class
/// reading them at the same moment would see a mid-test value.
/// </remarks>
[Collection(StorageCollection.Name)]
public class ScanProgressTests
{
    [Fact]
    public void TheCountersStartAtZero()
    {
        Game.ResetScanCounters();

        Assert.Equal(0, Game.ScansQueued);
        Assert.Equal(0, Game.ScansFinished);
    }

    [Fact]
    public void QueuingAndFinishingAreCountedSeparately()
    {
        Game.ResetScanCounters();

        Game.CountScanQueued();
        Game.CountScanQueued();
        Game.CountScanQueued();
        Game.CountScanFinished();
        Game.CountScanFinished();

        Assert.Equal(3, Game.ScansQueued);
        Assert.Equal(2, Game.ScansFinished);
    }

    [Fact]
    public void ResettingClearsBothCountersTogether()
    {
        Game.ResetScanCounters();
        Game.CountScanQueued();
        Game.CountScanQueued();
        Game.CountScanFinished();

        Game.ResetScanCounters();

        Assert.Equal(0, Game.ScansQueued);
        Assert.Equal(0, Game.ScansFinished);
    }

    // ------------------------------------------------------------------ the text

    [Theory]
    [InlineData(true, 340, 0, "Scanning 0 of 340")]
    [InlineData(true, 340, 12, "Scanning 12 of 340")]
    [InlineData(true, 340, 340, "Scanning 340 of 340")]
    public void TheTextSaysHowFarAlongTheScanIs(bool isLoading, int queued, int finished, string expected)
    {
        Assert.Equal(expected, GameGridPageModel.FormatScanStatus(isLoading, queued, finished));
    }

    [Fact]
    public void FinishedIsNeverReportedAboveQueued()
    {
        // It legitimately can be. The counters reset at the start of each library load, and a refresh can
        // begin while the previous load's scans are still running, so the old scans' completions land
        // after the reset.
        //
        // "Scanning 400 of 340" would read as a fault in the app rather than as a refresh that overtook a
        // load, so the clamp is the display's contract.
        Assert.Equal("Scanning 340 of 340", GameGridPageModel.FormatScanStatus(true, 340, 400));
    }

    [Theory]
    [InlineData(false, 340, 12)]  // nothing loading: the count is stale, not wrong, and must not show
    [InlineData(true, 0, 0)]      // an empty library, or every store disabled: nothing was ever queued
    [InlineData(true, 0, 12)]     // defensive: a finish recorded for a scan queued before a reset
    [InlineData(true, -1, 0)]     // defensive: a counter is never negative, but the guard costs nothing
    public void NothingIsShownWhenThereIsNothingToReport(bool isLoading, int queued, int finished)
    {
        Assert.Equal(string.Empty, GameGridPageModel.FormatScanStatus(isLoading, queued, finished));
    }

    [Fact]
    public void TheTextIsEmptyOutsideALoadSoTheLabelCanBindStraightToItsOwnVisibility()
    {
        // No converter, and no empty label left sitting between operations.
        Assert.Equal(string.Empty, GameGridPageModel.FormatScanStatus(false, 0, 0));
        Assert.Equal(string.Empty, GameGridPageModel.FormatScanStatus(false, 12, 12));
    }

    // ------------------------------------------------------------------ placement

    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void AGameIsCountedAsQueuedOnlyAfterTheEarlyReturnsHavePassed()
    {
        // A game with an empty or missing install path is never scanned. Counting it would leave the page
        // reporting a scan that could never complete - a count that stalls below the total forever.
        var source = Source("src", "Data", "Game.cs");

        var method = source.Substring(source.IndexOf("public void ProcessGame", StringComparison.Ordinal));
        var queuedAt = method.IndexOf("CountScanQueued()", StringComparison.Ordinal);
        var firstEarlyReturn = method.IndexOf("if (Directory.Exists(InstallPath) == false)", StringComparison.Ordinal);
        var taskRun = method.IndexOf("_ = Task.Run", StringComparison.Ordinal);

        Assert.True(queuedAt > 0, "ProcessGame should count the scan it queues");
        Assert.True(firstEarlyReturn > 0, "the install path checks should still be there");
        Assert.True(queuedAt > firstEarlyReturn, "a game with a missing install folder must not join the queue");
        Assert.True(queuedAt < taskRun, "and it must be counted before the work is handed to the pool");
    }

    [Fact]
    public void AFinishedScanIsCountedBesideTheSlotBeingReleased()
    {
        // Same finally, adjacent. Anything that stopped calling ExitProcessing is exactly the change that
        // would also stop the progress advancing - so the two cannot drift apart without it showing.
        var source = Source("src", "Data", "Game.cs");

        var exitAt = source.IndexOf("ExitProcessing();", source.IndexOf("public void ProcessGame", StringComparison.Ordinal), StringComparison.Ordinal);
        var finishedAt = source.IndexOf("CountScanFinished();", StringComparison.Ordinal);

        Assert.True(finishedAt > 0, "a completed scan should be counted");
        Assert.True(finishedAt > exitAt, "and counted at the same point the slot is released, not before");
        Assert.True(
            source.IndexOf("finally", finishedAt, StringComparison.Ordinal) > exitAt,
            "both should sit inside the finally that guarantees they run");
    }

    [Fact]
    public void TheCountersAreResetBeforeAnyLibraryIsAskedForItsGames()
    {
        // After enumerating would be too late: the first library can queue its scans on another thread
        // while LoadGamesAsync is still walking the rest, and a reset afterwards would discard them.
        var source = Source("src", "Data", "GameManager.cs");

        var method = source.Substring(source.IndexOf("public async Task LoadGamesAsync", StringComparison.Ordinal));
        var resetAt = method.IndexOf("Game.ResetScanCounters()", StringComparison.Ordinal);

        // The loop that asks each library for its games, rather than the name of the call: the
        // explanatory comment above the reset also mentions ListGamesAsync, and matching on the name would
        // have found that and concluded the reset came second.
        var enumerateAt = method.IndexOf("foreach (var gameLibraryEnum", StringComparison.Ordinal);

        Assert.True(resetAt > 0, "a library load should reset the counters");
        Assert.True(enumerateAt > 0, "the libraries should still be enumerated in this method");
        Assert.True(resetAt < enumerateAt, "and the reset must happen before any library queues a scan");
    }

    [Fact]
    public void BothLoadsArmTheTimerAndBothClearItWhenTheyEnd()
    {
        // Arming from a property-changed hook was the first attempt and would not have worked: both
        // IsGameListLoading and IsDLSSLoading start true, and the source generator's setter returns early
        // when the value is unchanged, so assigning true to a flag that is already true raises nothing.
        // The hook would never have fired on the first load - the load where the text is wanted most.
        //
        // So the timer is armed explicitly, and cleared from each load's finally rather than left to the
        // timer's next tick, which would leave a stale count on screen for an interval after the load
        // ended - the exact confusion this was added to remove.
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        Assert.Equal(2, CountOccurrences(source, "StartScanProgressTimer();"));
        Assert.Equal(2, CountOccurrences(source, "FinishScanProgress();"));
    }

    [Fact]
    public void AStoppedTimerCannotBeRearmedByATickAlreadyInFlight()
    {
        // A cancel cannot recall a callback that has already started. Without the flag, a load that ended
        // mid-tick would have that tick re-arm the timer afterwards, leaving it running against a page with
        // nothing to report - and on the games page, indefinitely.
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        Assert.Contains("volatile bool _scanProgressWanted", source, StringComparison.Ordinal);
        Assert.Contains("_scanProgressWanted = false;", source, StringComparison.Ordinal);
        Assert.Contains("if (_scanProgressWanted && IsLoading)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageStopsTheTimerWhenItIsUnloaded()
    {
        // Otherwise a timer and its callback stay alive against a page the user has navigated away from,
        // and can fire during shutdown - the same hazard the scroll timer on this page already had.
        var source = Source("src", "Pages", "GameGridPage.xaml.cs");

        var unloaded = source.Substring(source.IndexOf("void Page_Unloaded", StringComparison.Ordinal));
        var block = unloaded.Substring(0, Math.Min(900, unloaded.Length));

        Assert.Contains("StopScanProgressTimer()", block, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusTextIsBoundToTheSameVisibilityAsTheSpinner()
    {
        // The page already declares StringVisibilityConverter, so the label collapses to nothing when the
        // text is empty - no new converter, and nothing left on screen between loads.
        var xaml = Source("src", "Pages", "GameGridPage.xaml");

        Assert.Contains("Text=\"{x:Bind ViewModel.ScanStatusText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource StringVisibilityConverter}", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Bind ViewModel.ScanStatusText, Mode=OneWay, Converter={StaticResource StringVisibilityConverter}",
            xaml,
            StringComparison.Ordinal);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
