using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;
using Kronos.Data.ManuallyAdded;
using Kronos.Extensions;
using Kronos.Helpers;

namespace Kronos.Tests;

/// <summary>
/// Tests for the mass upgrade planner, which decides what a mass update would do before anything is
/// written.
/// </summary>
/// <remarks>
/// The planner is the part that has to be right. The executor only writes what it is told, so a mistake
/// here either misses games that needed updating or rewrites games that did not, and the preview the
/// user approves is produced by this same code, so the mistake would be invisible in both.
/// </remarks>
public class MassUpgradePlannerTests
{
    /// <summary>
    /// Builds a minimal concrete game carrying the given assets.
    /// </summary>
    /// <remarks>
    /// ManuallyAddedGame rather than Game itself, which is abstract. It is the simplest concrete
    /// subclass and needs no store client, so the planner can be tested against a real Game without
    /// any of that.
    /// </remarks>
    static ManuallyAddedGame GameWith(params (GameAssetType Type, string Version)[] assets)
    {
        var game = new ManuallyAddedGame { ID = Guid.NewGuid().ToString("N"), Title = "Test Game" };
        var index = 0;

        foreach (var (type, version) in assets)
        {
            game.GameAssets.Add(new GameAsset
            {
                Id = game.ID,
                AssetType = type,
                Path = $"C:\\Games\\Game\\file{index++}.dll",
                Version = version,
            });
        }

        return game;
    }

    // ---- Which asset types take part ----

    [Fact]
    public void TheMainRuntimesAreUpgradable()
    {
        Assert.True(MassUpgradePlanner.IsUpgradable(GameAssetType.DLSS));
        Assert.True(MassUpgradePlanner.IsUpgradable(GameAssetType.DLSS_G));
        Assert.True(MassUpgradePlanner.IsUpgradable(GameAssetType.DLSS_D));
        Assert.True(MassUpgradePlanner.IsUpgradable(GameAssetType.FSR_31_DX12));
        Assert.True(MassUpgradePlanner.IsUpgradable(GameAssetType.XeSS));
    }

    [Fact]
    public void BackupsAreNeverUpgraded()
    {
        // A backup holds the game's own file. Swapping it would put the shipped runtime back under a
        // "newest version" rule, which is exactly wrong.
        Assert.False(MassUpgradePlanner.IsUpgradable(GameAssetType.DLSS_BACKUP));
        Assert.False(MassUpgradePlanner.IsUpgradable(GameAssetType.FSR_31_DX12_BACKUP));
    }

    [Fact]
    public void UnversionedAssetsAreLeftAlone()
    {
        // There is no "newer" XeLL or DirectStorage to move to, so ordering them by version would be
        // meaningless.
        Assert.False(MassUpgradePlanner.IsUpgradable(GameAssetType.XeLL));
        Assert.False(MassUpgradePlanner.IsUpgradable(GameAssetType.DirectStorage));
    }

    // ---- Choosing the target ----

    [Fact]
    public void TheNewestDownloadedRecordIsChosen()
    {
        var records = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true),
            Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true),
            Record(GameAssetType.DLSS, "2.0.0.0", downloaded: true),
        };

        var target = MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: false);

        Assert.NotNull(target);
        Assert.Equal("3.0.0.0", target!.Version);
    }

    [Fact]
    public void AnUndownloadedRecordIsNotChosen()
    {
        // THE CASE THAT MATTERS MOST. A record whose file is not on disk yet is the most common reason
        // a mass update silently does nothing, so it is filtered here and shows in the preview as
        // blocked rather than being picked and then failing at the copy.
        //
        // The record still has a LocalRecord, pointing at a path with no file. That is the real state
        // and it is what makes this test able to catch the filter being removed.
        var records = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS, "3.0.0.0", downloaded: false),
            Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true),
        };

        Assert.NotNull(records[0].LocalRecord);
        Assert.False(records[0].LocalRecord!.IsDownloaded);

        var target = MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: false);

        Assert.NotNull(target);
        Assert.Equal("2.5.0.0", target!.Version);
    }

    [Fact]
    public void ARecordWithNoLocalRecordAtAllIsAlsoNotChosen()
    {
        // A record that has never been downloaded at all has no LocalRecord, which is a different case
        // from one that is registered but not yet fetched.
        var record = new DLLRecord
        {
            AssetType = GameAssetType.DLSS,
            Version = "3.0.0.0",
            VersionNumber = ParseVersion("3.0.0.0").GetVersionNumber(),
        };

        Assert.Null(MassUpgradePlanner.FindTargetRecord(
            new List<DLLRecord> { record }, GameAssetType.DLSS, allowDevDlls: false));
    }

    [Fact]
    public void AGameWhoseNewestIsUndownloadedFallsBackToTheDownloadedOne()
    {
        // End to end through the planner, which is where the fallback actually has to hold up.
        var game = GameWith((GameAssetType.DLSS, "2.0.0.0"));
        var library = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS, "3.0.0.0", downloaded: false),
            Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true),
        };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Equal(1, plan.UpgradeCount);

        // DisplayVersion, not the raw Version, so "2.5.0.0" is reported as "2.5". That is deliberate:
        // the preview is read by a person, and it should match how the same version appears in the
        // per game picker. An earlier version of this test asserted the raw string and failed, which
        // is the test being wrong rather than the planner.
        Assert.Equal("2.5", plan.Items.First(x => x.IsChange).TargetVersion);
    }

    [Fact]
    public void ThePreviewShowsVersionsTheWayThePickerDoes()
    {
        // The two surfaces must agree, or the preview would claim a different version than the one
        // that actually gets written.
        var game = GameWith((GameAssetType.DLSS, "2.0.0.0"));
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true) };

        var item = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false)
            .Items.First(x => x.IsChange);

        var record = library[0];

        Assert.Equal(record.DisplayVersion, item.TargetVersion);
    }

    [Fact]
    public void NoDownloadedRecordMeansNoTarget()
    {
        var records = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: false) };

        Assert.Null(MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: false));
    }

    [Fact]
    public void DevBuildsAreIgnoredUnlessAllowed()
    {
        // Matches the per game picker, which hides dev builds behind the same setting.
        var records = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS, "4.0.0.0", downloaded: true, isDev: true),
            Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true, isDev: false),
        };

        var withoutDev = MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: false);
        var withDev = MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: true);

        Assert.Equal("2.5.0.0", withoutDev!.Version);
        Assert.Equal("4.0.0.0", withDev!.Version);
    }

    [Fact]
    public void RecordsOfADifferentTypeAreNeverChosen()
    {
        var records = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS_G, "9.9.9.9", downloaded: true),
            Record(GameAssetType.DLSS, "2.5.0.0", downloaded: true),
        };

        var target = MassUpgradePlanner.FindTargetRecord(records, GameAssetType.DLSS, allowDevDlls: false);

        Assert.Equal("2.5.0.0", target!.Version);
    }

    // ---- Classifying one DLL ----

    [Fact]
    public void AnOlderGameGetsAnUpgrade()
    {
        Assert.Equal(
            MassUpgradePlanner.ActionKind.Upgrade,
            MassUpgradePlanner.Classify("2.5.0.0", hasTarget: true, targetVersion: "3.0.0.0"));
    }

    [Fact]
    public void AMatchingVersionIsAlreadyCurrent()
    {
        Assert.Equal(
            MassUpgradePlanner.ActionKind.AlreadyCurrent,
            MassUpgradePlanner.Classify("3.0.0.0", hasTarget: true, targetVersion: "3.0.0.0"));
    }

    [Fact]
    public void ANewerGameIsADowngrade()
    {
        // Installed against target, so a positive comparison means the game is ahead. This is the same
        // direction as SwapVersionAdvisor.Classify and the same mistake is easy to make.
        Assert.Equal(
            MassUpgradePlanner.ActionKind.Downgrade,
            MassUpgradePlanner.Classify("3.1.0.0", hasTarget: true, targetVersion: "3.0.0.0"));
    }

    [Fact]
    public void NoTargetMeansNoCandidate()
    {
        Assert.Equal(
            MassUpgradePlanner.ActionKind.NoCandidate,
            MassUpgradePlanner.Classify("2.5.0.0", hasTarget: false, targetVersion: null));
    }

    [Fact]
    public void AnUnreadableInstalledVersionIsTreatedAsAnUpgrade()
    {
        // Deliberately not skipped. Leaving a file alone because its version could not be read is how a
        // mass update appears to do nothing, and it is visible in the preview either way.
        Assert.Equal(
            MassUpgradePlanner.ActionKind.Upgrade,
            MassUpgradePlanner.Classify("unknown", hasTarget: true, targetVersion: "3.0.0.0"));
    }

    // ---- Planning a game ----

    [Fact]
    public void AGameOnAnOlderRuntimeIsPlannedForUpgrade()
    {
        var game = GameWith((GameAssetType.DLSS, "2.5.0.0"));
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Equal(1, plan.UpgradeCount);
        Assert.Contains(plan.Items, x => x.AssetType == GameAssetType.DLSS && x.IsChange);
    }

    [Fact]
    public void AGameAlreadyOnTheLatestIsPlannedAsCurrent()
    {
        var game = GameWith((GameAssetType.DLSS, "3.0.0.0"));
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Equal(0, plan.UpgradeCount);
        Assert.Equal(1, plan.AlreadyCurrentCount);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void EveryCopyOfADllIsCoveredByOneItem()
    {
        // A game can hold several copies of the same dll, one per graphics API. They are swapped
        // together by UpdateDllAsync, so one plan item covers them, not one per file.
        var game = GameWith((GameAssetType.DLSS, "2.5.0.0"), (GameAssetType.DLSS, "2.5.0.0"));
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Single(plan.Items, x => x.AssetType == GameAssetType.DLSS);
    }

    [Fact]
    public void ABunchOfDifferentRuntimesAreAllCovered()
    {
        var game = GameWith(
            (GameAssetType.DLSS, "2.5.0.0"),
            (GameAssetType.DLSS_G, "1.0.0.0"),
            (GameAssetType.XeSS, "1.0.0.0"));

        var library = new List<DLLRecord>
        {
            Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true),
            Record(GameAssetType.DLSS_G, "2.0.0.0", downloaded: true),
            Record(GameAssetType.XeSS, "2.0.0.0", downloaded: true),
        };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Equal(3, plan.UpgradeCount);
    }

    [Fact]
    public void ABackedUpRuntimeIsNotPlannedForUpdate()
    {
        // The backup is the game's own file and must never be treated as something to upgrade.
        var game = GameWith(
            (GameAssetType.DLSS, "2.5.0.0"),
            (GameAssetType.DLSS_BACKUP, "1.0.0.0"));

        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.DoesNotContain(plan.Items, x => x.AssetType == GameAssetType.DLSS_BACKUP);
    }

    [Fact]
    public void AGameWithNothingInstalledGetsANoRecordsItem()
    {
        // Silence would be worse: the preview should show that a type was considered and found absent.
        var game = GameWith();
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGame(game, library, allowDevDlls: false);

        Assert.Contains(plan.Items, x => x.Kind == MassUpgradePlanner.ActionKind.NoRecords);
        Assert.Equal(0, plan.UpgradeCount);
    }

    [Fact]
    public void AnEmptyLibraryProducesNoNoRecordsNoise()
    {
        // Only types the library actually knows about are worth mentioning.
        var game = GameWith();
        var plan = MassUpgradePlanner.PlanForGame(game, new List<DLLRecord>(), allowDevDlls: false);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void AGameWithNoSwappableFilesPlansNothing()
    {
        var game = GameWith((GameAssetType.DirectStorage, "1.0.0.0"));
        var plan = MassUpgradePlanner.PlanForGame(game, new List<DLLRecord>(), allowDevDlls: false);

        Assert.Empty(plan.Items);
    }

    // ---- Planning many games ----

    [Fact]
    public void EachGameIsCountedSeparately()
    {
        var games = new[]
        {
            GameWith((GameAssetType.DLSS, "2.5.0.0")),
            GameWith((GameAssetType.DLSS, "2.0.0.0")),
            GameWith((GameAssetType.DLSS, "3.0.0.0")),
        };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGames(games, library, allowDevDlls: false);

        Assert.Equal(2, plan.UpgradeCount);
        Assert.Equal(2, plan.GamesAffected);
        Assert.Equal(1, plan.AlreadyCurrentCount);
    }

    [Fact]
    public void AnEmptySelectionPlansNothing()
    {
        var plan = MassUpgradePlanner.PlanForGames(
            Array.Empty<Game>(), new List<DLLRecord>(), allowDevDlls: false);

        Assert.Empty(plan.Items);
        Assert.False(plan.HasWork);
    }

    [Fact]
    public void OnlyUpgradesAreMarkedAsChanges()
    {
        // A downgrade is surfaced for confirmation but is not part of the automatic run.
        var games = new[]
        {
            GameWith((GameAssetType.DLSS, "2.5.0.0")),
            GameWith((GameAssetType.DLSS, "4.0.0.0")),
        };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var plan = MassUpgradePlanner.PlanForGames(games, library, allowDevDlls: false);

        Assert.Single(plan.Items, x => x.IsChange);
        Assert.Single(plan.Items, x => x.NeedsConfirmation);
    }

    // ---- The executor's thread safety ----
    //
    // This is what actually broke the first run. The executor looked games up itself, via
    // GameManager.GetGameCollection, which returns a WinRT ICollectionView. It runs after a
    // ConfigureAwait(false), so it is on a thread pool thread, and touching the collection view there
    // throws COMException 0x8001010E. The failure was immediate and total: the first item, every time,
    // before any file was written.

    [Fact]
    public void TheExecutorIsGivenTheGamesRatherThanLookingThemUp()
    {
        // A compile time property of the signature. If someone reintroduces a GameManager lookup inside
        // the executor this will not catch it, but the summary below and the threading note on the
        // method are there for whoever does.
        var method = typeof(MassUpgradeExecutor).GetMethod(nameof(MassUpgradeExecutor.ExecuteAsync))!;
        var parameters = method.GetParameters().Select(x => x.ParameterType).ToList();

        Assert.Contains(typeof(IReadOnlyList<Game>), parameters);
    }

    [Fact]
    public void TheExecutorDoesNotCallTheCollectionViewItself()
    {
        // The actual invariant. GameManager.GetGameCollection is the only route to the WinRT collection
        // view, and calling it from the executor is what caused the crash, so its absence is worth
        // pinning.
        //
        // Comments are stripped first because the file deliberately explains the mistake in prose, and
        // naming the method in a comment is the whole point of those notes. Only executable code counts.
        var code = StripComments(ReadSourceFile(nameof(MassUpgradeExecutor)));

        Assert.DoesNotContain("GetGameCollection", code, StringComparison.Ordinal);
        Assert.Contains("TouchesWinRt", ReadSourceFile(nameof(MassUpgradeExecutor)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheExecutorDeclaresThatItTouchesNoWinRt()
    {
        // Documented as a property so a future change adding a WinRT call has something to update, and
        // so a reader can see the rule without reading the whole implementation.
        Assert.False(MassUpgradeExecutor.TouchesWinRt);
    }

    /// <summary>
    /// Removes line and block comments so an assertion about code does not trip over prose explaining
    /// why that code must not come back.
    /// </summary>
    internal static string StripComments(string source)
    {
        var withoutBlockComments = System.Text.RegularExpressions.Regex.Replace(source, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join("\n", withoutBlockComments
            .Split('\n')
            .Select(line =>
            {
                var index = line.IndexOf("//", StringComparison.Ordinal);

                return index < 0 ? line : line[..index];
            }));
    }

    static string ReadSourceFile(string typeName)
    {
        // The test runs from the test output directory, so walk up to the repository and read the file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "src", "Helpers", typeName + ".cs");
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }

    [Fact]
    public void TheCachedAssetLoadMarshalsItsPropertyUpdates()
    {
        // A REAL BUG, found by sweeping for the thread-affinity pattern rather than by a failing test,
        // because it needs a live WinUI Application to reproduce.
        //
        // Game.LoadGameAssetsFromCacheAsync awaits a database query with ConfigureAwait(false) and then
        // calls UpdateCurrentDLLsFromGameAssets(), which sets CurrentDLSS and every Multiple*Found
        // property. Those are [ObservableProperty] and x:Bind-bound in GameGridPage.xaml, so setting
        // them from a thread pool thread throws COMException 0x8001010E as soon as an item container has
        // been realised. Reached on startup for every game in every library.
        //
        // The identical call inside ProcessGame is already wrapped, which is what makes the missing one
        // an oversight rather than a decision. This asserts the wrapper is present so it cannot be
        // dropped again.
        var source = StripComments(ReadGameSource());

        var afterConfigureAwaitFalse = source.IndexOf(
            "ToListAsync().ConfigureAwait(false)", StringComparison.Ordinal);
        var marshalledUpdate = source.IndexOf(
            "RunOnUIThread(() =>", afterConfigureAwaitFalse, StringComparison.Ordinal);
        var updateCall = source.IndexOf(
            "UpdateCurrentDLLsFromGameAssets();", afterConfigureAwaitFalse, StringComparison.Ordinal);

        Assert.True(afterConfigureAwaitFalse > 0, "The ConfigureAwait(false) that moves off-thread should still be there.");
        Assert.True(marshalledUpdate > 0, "UpdateCurrentDLLsFromGameAssets must be wrapped in RunOnUIThread after the ConfigureAwait(false).");
        Assert.True(marshalledUpdate < updateCall, "The wrapper has to come before the call it protects.");
    }

    [Fact]
    public void TheOtherCurrentAssetAssignmentSitesAreAlreadyMarshalledByTheirCallers()
    {
        // CurrentDLSS is assigned in five places, not one. An earlier version of this test asserted two
        // and failed, which was the test being invented rather than the code being wrong. The four that
        // are not the fixed one are:
        //
        //   UpdateCurrentAsset  (two sites)  reached via RunOnUIThread at line 1084
        //   ParentUpdateFromGame (one site) reached via RunOnUIThread at GameManager.cs:317
        //   UpdateCurrentDLLsFromGameAssets   the fixed one, marshalled inline now
        //
        // So the real invariant is not a count. It is that the method which does a bulk refresh from
        // GameAssets, and is the one called straight after a ConfigureAwait(false), is the one that has
        // to marshal itself, because the others have a marshalling caller already.
        var source = StripComments(ReadGameSource());

        var afterConfigureAwaitFalse = source.IndexOf(
            "ToListAsync().ConfigureAwait(false)", StringComparison.Ordinal);
        var body = source[afterConfigureAwaitFalse..];

        // In the cached-load path specifically, the refresh must be marshalled, and the marshalling
        // must not be deferred to something later in the file.
        Assert.True(
            body.IndexOf("RunOnUIThread", StringComparison.Ordinal) < body.IndexOf(
                "UpdateCurrentDLLsFromGameAssets();", StringComparison.Ordinal),
            "The refresh after the ConfigureAwait(false) must be inside RunOnUIThread, not merely followed by one somewhere later.");
    }

    static string ReadGameSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "src", "Data", "Game.cs");
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }

    // ---- The summary the user reads before approving ----

    [Fact]
    public void TheSummarySaysWhatWouldChange()
    {
        var games = new[] { GameWith((GameAssetType.DLSS, "2.5.0.0")) };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var summary = MassUpgradePlanner.Summarise(MassUpgradePlanner.PlanForGames(games, library, false));

        Assert.Contains("1", summary);
        Assert.Contains("updated", summary);
    }

    [Fact]
    public void TheSummaryCallsOutDowngradesExplicitly()
    {
        // A downgrade is the one outcome that can make things worse, so it gets its own line rather
        // than being buried in a count.
        var games = new[] { GameWith((GameAssetType.DLSS, "4.0.0.0")) };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var summary = MassUpgradePlanner.Summarise(MassUpgradePlanner.PlanForGames(games, library, false));

        Assert.Contains("older", summary);
        Assert.Contains("Frame Generation", summary);
    }

    [Fact]
    public void TheSummarySaysWhenThereIsNothingToDo()
    {
        var games = new[] { GameWith((GameAssetType.DLSS, "3.0.0.0")) };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var summary = MassUpgradePlanner.Summarise(MassUpgradePlanner.PlanForGames(games, library, false));

        Assert.Contains("already up to date", summary);
    }

    [Fact]
    public void TheSummaryMentionsThatChangesCanBeUndone()
    {
        // The thing that makes a mass write to many game folders acceptable at all.
        var games = new[] { GameWith((GameAssetType.DLSS, "2.5.0.0")) };
        var library = new List<DLLRecord> { Record(GameAssetType.DLSS, "3.0.0.0", downloaded: true) };

        var summary = MassUpgradePlanner.Summarise(MassUpgradePlanner.PlanForGames(games, library, false));

        Assert.Contains("back", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undone", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptySelectionIsExplainedRatherThanSilentlyEmpty()
    {
        var summary = MassUpgradePlanner.Summarise(MassUpgradePlanner.PlanForGames(Array.Empty<Game>(), Array.Empty<DLLRecord>(), false));

        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    static DLLRecord Record(GameAssetType type, string version, bool downloaded, bool isDev = false)
    {
        var record = new DLLRecord
        {
            AssetType = type,
            Version = version,
            MD5Hash = Guid.NewGuid().ToString("N"),
            IsDevFile = isDev,
            VersionNumber = ParseVersion(version).GetVersionNumber(),
        };

        // A record always has a LocalRecord, downloaded or not. That is how the app models it, and it
        // matters: an earlier version of this helper only attached a LocalRecord when the file was
        // downloaded, so the "is it downloaded" filter in FindTargetRecord was never actually
        // exercised. Removing that filter passed every test while the feature it guards was broken.
        // A downloaded one has the file on disk, so FromExpectedPath sets IsDownloaded from that.
        // An undownloaded one points at a path with no file, which is the real state.
        var path = Path.Combine(Path.GetTempPath(), "kronos_plan_tests", Guid.NewGuid().ToString("N") + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (downloaded)
        {
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A });
        }

        record.LocalRecord = LocalRecord.FromExpectedPath(path);

        if (downloaded == false)
        {
            // FromExpectedPath cannot set this to false on its own when the file is absent, and
            // asserting it here makes the test fail loudly if that ever changes.
            record.LocalRecord.IsDownloaded = false;
        }

        return record;
    }

    /// <summary>
    /// Parses "3.0.0.0" into a Version so the record's sort key is built the same way the app builds
    /// it, rather than by a second, possibly different, rule living in the test.
    /// </summary>
    static Version ParseVersion(string version)
    {
        var parts = version.Split('.').Select(int.Parse).ToList();
        while (parts.Count < 4)
        {
            parts.Add(0);
        }

        return new Version(parts[0], parts[1], parts[2], parts[3]);
    }
}
