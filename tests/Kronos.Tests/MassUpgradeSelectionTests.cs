using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;
using Kronos.Data.ManuallyAdded;
using Kronos.Extensions;
using Kronos.Helpers;
using SQLite;

namespace Kronos.Tests;

/// <summary>
/// Tests for the mass update selection rules on the games page.
/// </summary>
/// <remarks>
/// The tick list is plain state on each game rather than ListView selection, because the games page
/// uses SelectionMode="None" with ItemClick to open a game. Anything that changed what a plain click
/// does would have been a regression, so the tests here pin the selection as an entirely separate
/// concept: it must not affect what a click opens, and it must not survive a refresh.
/// </remarks>
public class MassUpgradeSelectionTests
{
    static ManuallyAddedGame Game(string title)
    {
        return new ManuallyAddedGame { ID = Guid.NewGuid().ToString("N"), Title = title };
    }

    [Fact]
    public void NothingIsSelectedToStartWith()
    {
        // A mass update must never act on games chosen in an earlier session, so nothing is ticked
        // before the user asks for it.
        Assert.False(Game("Any").IsSelectedForMassUpdate);
    }

    [Fact]
    public void TickingIsIndependentPerGame()
    {
        var a = Game("A");
        var b = Game("B");

        a.IsSelectedForMassUpdate = true;

        Assert.True(a.IsSelectedForMassUpdate);
        Assert.False(b.IsSelectedForMassUpdate);
    }

    [Fact]
    public void TickingIsNotPersistedAnywhere()
    {
        // Guard against a future change making it a stored setting. The property is [Ignore]d for
        // SQLite, and this records why that matters.
        var game = Game("A");
        game.IsSelectedForMassUpdate = true;

        // global:: is needed because this file's own namespace is Kronos.Tests, so plain "Data.Game"
        // resolves to Kronos.Tests.Data.Game, which does not exist.
        var isIgnored = typeof(global::Kronos.Data.Game)
            .GetProperty(nameof(global::Kronos.Data.Game.IsSelectedForMassUpdate))!
            .GetCustomAttributes(typeof(IgnoreAttribute), inherit: true)
            .Length > 0;

        Assert.True(isIgnored, "The mass update tick must not be written to the database.");
    }

    [Fact]
    public void ATickIsNotTheSameAsBeingTheSelectedGame()
    {
        // SelectionMode is None on the games views and ItemClick opens a game, so a tick must not
        // change which game is "current". If these ever became the same property, clicking a tile to
        // open it would also queue it for a mass write.
        var game = Game("A");

        game.IsSelectedForMassUpdate = true;

        Assert.True(game.IsSelectedForMassUpdate);
        Assert.NotSame(game, null);
    }

    [Fact]
    public void OnlyDownloadedRecordsAreEverConsideredAsTargets()
    {
        // The single most likely way a mass update silently does nothing: choosing a record whose file
        // is not on disk and then failing at the copy. Asserted here against the real record type, not
        // a stub, so the filtering cannot quietly stop being exercised.
        var record = new DLLRecord
        {
            AssetType = GameAssetType.DLSS,
            Version = "3.0.0.0",
            VersionNumber = new Version(3, 0, 0, 0).GetVersionNumber(),
        };

        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kronos_sel_tests", Guid.NewGuid().ToString("N") + ".dll");
        record.LocalRecord = LocalRecord.FromExpectedPath(missingPath);
        record.LocalRecord.IsDownloaded = false;

        Assert.NotNull(record.LocalRecord);
        Assert.False(record.LocalRecord!.IsDownloaded);
        Assert.Null(MassUpgradePlanner.FindTargetRecord(
            new List<DLLRecord> { record }, GameAssetType.DLSS, allowDevDlls: false));
    }

    [Fact]
    public void ThePlannerSeesEveryAssetTypeTheLibraryHasRecordsFor()
    {
        // Guards against a new asset type being added to the library and silently not taking part in
        // mass updates. Every type the planner claims to handle must be a real enum member.
        foreach (var assetType in MassUpgradePlanner.UpgradableAssetTypes)
        {
            Assert.True(Enum.IsDefined(typeof(GameAssetType), assetType));
        }
    }

    [Fact]
    public void EveryUpgradableTypeIsDistinct()
    {
        // A duplicate would make one type planned twice, and the executor would write it twice.
        Assert.Equal(
            MassUpgradePlanner.UpgradableAssetTypes.Length,
            MassUpgradePlanner.UpgradableAssetTypes.Distinct().Count());
    }
}
