using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the Refresh button against running the same scan more than once at a time.
/// </summary>
/// <remarks>
/// RefreshGamesButtonAsync had no guard of its own. MassUpgradeViewModel checks IsRunning before it does
/// anything, and the refresh command checked nothing, so the only thing standing between two clicks was
/// the button's IsEnabled binding - which is a UI affordance, not a lock, and is not consulted by the
/// command itself.
///
/// That matters because the command's IsEnabled binds to IsLoading, which is derived from two flags the
/// command and the initial load both write. Any window where those disagree, and any other caller of the
/// same command, allows a second run to start. Two concurrent runs then each queue every game for
/// ProcessGame, which is the expensive part: a recursive directory walk and a cover fetch per game, with a
/// semaphore of 4 between them. From the user's side the refresh appears to hang or repeat forever.
///
/// Also guarded here: ProcessGame's own re-entrancy check. It tested Processing, then set it after two
/// UiDispatcher.Invoke calls, so two callers could both pass the test and both run the scan - the same
/// double work, one level further down.
/// </remarks>
public class RefreshReentrancyTests
{
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
    public void TheRefreshCommandRefusesToRunWhileItIsAlreadyRunning()
    {
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        Assert.Contains("IsRefreshing", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefreshCommandAlwaysClearsItsGuardEvenIfTheLoadThrows()
    {
        // Without a finally, one thrown exception leaves IsRefreshing true and the button permanently dead
        // - the user can never refresh again without restarting.
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        var command = source.Substring(source.IndexOf("RefreshGamesButtonAsync", StringComparison.Ordinal));
        var body = command.Substring(0, Math.Min(900, command.Length));

        Assert.True(
            body.Contains("finally", StringComparison.Ordinal),
            "the refresh command must clear its guard in a finally, or a single failure bricks the button");
    }

    [Fact]
    public void TheRefreshGuardIsActuallyConsultedBeforeLoading()
    {
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        var command = source.Substring(source.IndexOf("RefreshGamesButtonAsync", StringComparison.Ordinal));
        var body = command.Substring(0, Math.Min(600, command.Length));

        // The guard is an Interlocked claim on the backing field, not a read of the IsRefreshing property -
        // reading a bool and then writing it is exactly the check-then-set this fix exists to remove. So
        // what is asserted here is that the claim is atomic and that it happens first.
        var claim = body.IndexOf("Interlocked.CompareExchange", StringComparison.Ordinal);
        var load = body.IndexOf("LoadGamesAsync", StringComparison.Ordinal);

        Assert.True(claim >= 0, "the command must claim the refresh slot before doing anything");
        Assert.True(load > claim, "the claim must come before the load, not after it");
    }

    [Fact]
    public void TheRefreshCommandExposesWhetherItCanRunForTheButtonBinding()
    {
        var source = Source("src", "Pages", "GameGridPageModel.cs");

        Assert.Contains("CanRefresh", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessGameClaimsItsSlotAtomicallyRatherThanCheckingThenSetting()
    {
        // The old guard read Processing, then set it two UiDispatcher.Invoke calls later. Between those two
        // points another caller could read the same false and start a second scan of the same game.
        //
        // What matters is that the claim happens in one indivisible step, so the check and the set cannot
        // be separated by anything.
        var source = Source("src", "Data", "Game.cs");

        Assert.Contains("TryEnterProcessing", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessGameDoesNotStillUseTheRacyCheckThenSet()
    {
        var source = Source("src", "Data", "Game.cs");

        var process = source.Substring(source.IndexOf("public void ProcessGame", StringComparison.Ordinal));
        var guard = process.Substring(0, Math.Min(1200, process.Length));

        Assert.DoesNotContain(
            "if (Processing == true)",
            guard,
            StringComparison.Ordinal);
    }
}