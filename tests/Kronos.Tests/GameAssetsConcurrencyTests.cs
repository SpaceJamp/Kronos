using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kronos.Data;
using Kronos.Data.ManuallyAdded;
using Kronos.UserControls;
using Xunit.Abstractions;

namespace Kronos.Tests;

/// <summary>
/// Tests for the locking around <c>Game.GameAssets</c>.
/// </summary>
/// <remarks>
/// The list is mutated by two thread pool threads: <c>Game.ProcessGame</c> replaces it wholesale at the
/// end of a scan, and <c>Game.UpdateDllAsync</c> and <c>Game.ResetDllAsync</c> edit it in place. With a
/// mass update running, both happen at once.
///
/// Unlike the WinUI threading bugs, this one is testable, because it is plain managed code and needs
/// no live <c>Application</c>. That makes it worth testing properly rather than asserting on source
/// text.
/// </remarks>
public class GameAssetsConcurrencyTests
{
    readonly ITestOutputHelper _output;

    public GameAssetsConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    static GameAsset Asset(string path) => new() { Path = path, Version = "1.0.0.0" };

    [Fact]
    public void AddingAndReplacingBehaveAsTheListDid()
    {
        var game = new ManuallyAddedGame { ID = "g1", Title = "G" };

        game.AddGameAssets(new[] { Asset("a"), Asset("b") });
        Assert.Equal(2, game.GameAssets.Count);

        game.RemoveGameAsset(game.GameAssets[0]);
        Assert.Single(game.GameAssets);

        game.ReplaceGameAssets(new[] { Asset("c") });
        Assert.Equal("c", game.GameAssets[0].Path);

        game.ClearGameAssets();
        Assert.Empty(game.GameAssets);
    }

    [Fact]
    public void ReplacingSwapsTheWholeContentsRatherThanMerging()
    {
        // ProcessGame depends on this: the old records are gone and only the new scan's remain.
        var game = new ManuallyAddedGame { ID = "g1" };
        game.AddGameAssets(new[] { Asset("old1"), Asset("old2") });

        game.ReplaceGameAssets(new[] { Asset("new") });

        Assert.Single(game.GameAssets);
        Assert.Equal("new", game.GameAssets[0].Path);
    }

    [Fact]
    public void NullAndEmptyInputsAreTolerated()
    {
        // A swap with nothing to add, or a scan that produced nothing, must not throw.
        var game = new ManuallyAddedGame { ID = "g1" };

        game.AddGameAssets(null!);
        game.ReplaceGameAssets(null!);
        game.RemoveGameAsset(null!);

        Assert.Empty(game.GameAssets);
    }

    [Fact]
    public void ASnapshotIsIndependentOfLaterChanges()
    {
        // The point of the snapshot. A caller iterating one must not see the list change under it.
        var game = new ManuallyAddedGame { ID = "g1" };
        game.AddGameAssets(new[] { Asset("a"), Asset("b") });

        var snapshot = game.GetGameAssetsSnapshot();
        game.ClearGameAssets();

        Assert.Equal(2, snapshot.Count);
        Assert.Empty(game.GameAssets);
    }

    [Fact]
    public async Task ConcurrentReplacementAndSnapshottingNeverThrowsOrLosesRows()
    {
        // THE REGRESSION. Without the lock this throws "Collection was modified" from a List enumerator
        // during a snapshot, or produces a torn copy. Run it enough times that an unsynchronised
        // version would fail.
        var game = new ManuallyAddedGame { ID = "g1", Title = "G" };
        const int iterations = 400;

        using var cts = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                game.ReplaceGameAssets(new[] { Asset($"a{i}"), Asset($"b{i}") });
                game.AddGameAssets(new[] { Asset($"c{i}") });
                game.RemoveGameAsset(game.GameAssets.LastOrDefault()!);
            }
        });

        var reader = Task.Run(() =>
        {
            var failures = 0;

            for (var i = 0; i < iterations * 2; i++)
            {
                try
                {
                    // Snapshot and enumerate, which is the combination that throws on an unsynchronised
                    // List.
                    foreach (var asset in game.GetGameAssetsSnapshot())
                    {
                        _ = asset.Path;
                    }
                }
                catch (Exception)
                {
                    failures++;
                }
            }

            return failures;
        });

        await Task.WhenAll(writer, reader);

        var enumerationFailures = await reader;

        Assert.Equal(0, enumerationFailures);
    }

    [Fact]
    public async Task ConcurrentWritersDoNotLoseEntries()
    {
        // Two independent writers, each adding a distinct set, must end up with both sets present.
        // A lost update here means a game silently forgets a dll it has, which then cannot be swapped.
        var game = new ManuallyAddedGame { ID = "g1", Title = "G" };
        const int perWriter = 300;

        var writerA = Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                game.AddGameAssets(new[] { Asset($"a{i}") });
            }
        });

        var writerB = Task.Run(() =>
        {
            for (var i = 0; i < perWriter; i++)
            {
                game.AddGameAssets(new[] { Asset($"b{i}") });
            }
        });

        await Task.WhenAll(writerA, writerB);

        var snapshot = game.GetGameAssetsSnapshot();

        _output.WriteLine($"expected {perWriter * 2}, got {snapshot.Count}");

        Assert.Equal(perWriter * 2, snapshot.Count);
        Assert.Equal(perWriter, snapshot.Count(x => x.Path.StartsWith("a", StringComparison.Ordinal)));
        Assert.Equal(perWriter, snapshot.Count(x => x.Path.StartsWith("b", StringComparison.Ordinal)));
    }
}

/// <summary>
/// Tests for the history model, which previously never populated the grid at all.
/// </summary>
/// <remarks>
/// Two separate faults, and only one of them was about threading. The list was a plain
/// <see cref="List{T}"/>, so <c>AddRange</c> raised nothing and the DataGrid's <c>Mode=OneTime</c>
/// binding had already resolved to an empty collection. Fixing only the threading would have left it
/// broken, and fixing only the collection would have left it throwing 0x8001010E from a pool thread.
/// </remarks>
public class GameHistoryModelTests
{
    [Fact]
    public void TheHistoryRowsRaiseChangeNotifications()
    {
        // A plain List does not, which is why the grid stayed empty. An ObservableCollection does, which
        // is the whole reason for the change.
        //
        // Asserted against a real instance rather than a stand in, constructed without its GameHistory-
        // Control dependency. The model's load path needs a live database and a WinUI control, neither
        // of which exists in a test, so what is verified here is the container's notification behaviour,
        // which is the part that was broken.
        var model = new GameHistoryControlModel();

        var notifications = 0;
        model.HistoryRows.CollectionChanged += (_, _) => notifications++;

        model.HistoryRows.Add(new GameHistory { EventType = GameHistoryEventType.DLLSwapped });

        Assert.Equal(1, notifications);
        Assert.Single(model.HistoryRows);
    }

    [Fact]
    public void TheHistoryBindingIsOneWaySoLateRowsArrive()
    {
        // OneTime resolves ItemsSource once, when the control is created, which is before the query has
        // run. Asserted against the XAML so a revert to OneTime fails rather than passing quietly.
        var xaml = TestFileReader.ReadSourceFile("GameHistoryControl.xaml", "UserControls");

        Assert.Contains("ViewModel.HistoryRows, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.HistoryRows, Mode=OneTime", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHistoryBindingIsNotTwoWay()
    {
        // The grid is read only. TwoWay would ask the source to push edits back into rows that came
        // from the database, and is not wanted.
        var xaml = TestFileReader.ReadSourceFile("GameHistoryControl.xaml", "UserControls");

        Assert.DoesNotContain("ViewModel.HistoryRows, Mode=TwoWay", xaml, StringComparison.Ordinal);
    }
}

/// <summary>
/// Locates source files relative to the repository, for the tests that assert on XAML or source text.
/// </summary>
internal static class TestFileReader
{
    internal static string ReadSourceFile(string fileName, params string[] pathSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var segments = new List<string> { dir!.FullName, "src" };
        segments.AddRange(pathSegments);
        segments.Add(fileName);

        var path = Path.Combine(segments.ToArray());
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }
}
