using DLSS_Swapper.Data;
using DLSS_Swapper.Data.ManuallyAdded;
using DLSS_Swapper.Interfaces;

namespace DLSS_Swapper.Tests.Data;

/// <summary>
/// Tests for the equality contract on Game, GameAsset and LocalRecord.
/// </summary>
/// <remarks>
/// All three overrode Equals without overriding GetHashCode, which breaks the contract: two objects
/// that are Equals can then land in different buckets of a HashSet or be treated as distinct by
/// Distinct/GroupBy. Game was worse than that, because its Equals also ignored which library a game
/// came from.
/// </remarks>
public class EqualityTests
{
    // NOTE: SteamGame is used purely as a concrete Game. Its GameLibrary is fixed to Steam, so the
    // cross-library case is exercised through a small local subclass instead.
    sealed class FakeSteamGame : Game
    {
        public override GameLibrary GameLibrary => GameLibrary.Steam;
        public override bool IsReadyToPlay => true;
        protected override Task UpdateCacheImageAsync() => Task.CompletedTask;
        public override bool UpdateFromGame(Game game) => ParentUpdateFromGame(game);

        public FakeSteamGame(string platformId)
        {
            PlatformId = platformId;
            SetID();
        }
    }

    [Fact]
    public void Game_GamesWithTheSameIdAreEqual()
    {
        var a = new FakeSteamGame("1938090");
        var b = new FakeSteamGame("1938090");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Game_GamesWithDifferentIdsAreNotEqual()
    {
        var a = new FakeSteamGame("111");
        var b = new FakeSteamGame("222");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Game_IdComparisonIsCaseInsensitive()
    {
        var a = new FakeSteamGame("1938090");
        var b = new FakeSteamGame("1938090");
        typeof(Game).GetProperty(nameof(Game.ID))!.SetValue(b, b.ID.ToUpperInvariant());

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Game_EqualGamesHashIdentically_WhichIsTheContract()
    {
        // The specific guarantee that was missing. If Equals and GetHashCode disagree, HashSet,
        // Distinct and GroupBy all misbehave on these types.
        var games = new List<Game>
        {
            new FakeSteamGame("1"),
            new FakeSteamGame("1"),
            new FakeSteamGame("2"),
            new ManuallyAddedGame("1"),
        };

        foreach (var game in games)
        {
            foreach (var other in games)
            {
                if (game.Equals(other))
                {
                    Assert.True(game.GetHashCode() == other.GetHashCode(),
                        $"Equal games hashed differently: {game.ID} vs {other.ID}");
                }
            }
        }
    }

    [Fact]
    public void Game_DistinctDeduplicatesById()
    {
        var games = new List<Game>
        {
            new FakeSteamGame("1938090"),
            new FakeSteamGame("1938090"),
            new FakeSteamGame("228980"),
        };

        Assert.Equal(2, games.Distinct().Count());
    }

    [Fact]
    public void Game_ManuallyAddedAndSteamWithSameNumericIdAreNotEqual()
    {
        // The cross-library collision. PlatformId is only unique within a library, so before the
        // fix these compared equal and GameManager.AddGame would silently drop one of them.
        var steam = new FakeSteamGame("12345");
        var manual = new ManuallyAddedGame("12345");

        Assert.Equal("steam_12345", steam.ID);
        Assert.Equal("manuallyadded_12345", manual.ID);
        // Compared as Game, which is the type GameManager.AddGame actually operates on.
        Assert.NotEqual<Game>(steam, manual);
    }

    [Fact]
    public void Game_EqualsNullIsFalse()
    {
        Assert.False(new FakeSteamGame("1").Equals(null));
    }

    [Fact]
    public void Game_CompareToAgreesWithEquals()
    {
        // CompareTo used to order by Title, so two different games that both had no title compared
        // as 0, i.e. equal. That breaks List<Game>.Sort and Comparer<Game>.Default.
        var a = new FakeSteamGame("111");
        var b = new FakeSteamGame("222");

        Assert.NotEqual<Game>(a, b);
        Assert.NotEqual(0, a.CompareTo(b));
    }

    [Fact]
    public void Game_CompareToReturnsZeroOnlyForEqualGames()
    {
        var a = new FakeSteamGame("1938090");
        var b = new FakeSteamGame("1938090");
        var c = new FakeSteamGame("228980");

        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        Assert.NotEqual(0, a.CompareTo(c));
    }

    [Fact]
    public void Game_SortingDoesNotDropGamesWithIdenticalTitles()
    {
        // The practical consequence of the CompareTo bug: sorting a list of games that all have the
        // same (here, empty) title treated them as duplicates.
        var games = new List<Game>
        {
            new FakeSteamGame("1") { Title = string.Empty },
            new FakeSteamGame("2") { Title = string.Empty },
            new FakeSteamGame("3") { Title = string.Empty },
        };

        var sorted = new List<Game>(games);
        sorted.Sort();

        Assert.Equal(3, sorted.Distinct().Count());
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void Game_CompareToNullIsGreater()
    {
        Assert.Equal(1, new FakeSteamGame("1").CompareTo(null));
    }

    [Fact]
    public void GameAsset_EqualAssetsHashIdentically()
    {
        var a = new GameAsset { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "h" };
        var b = new GameAsset { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "h" };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GameAsset_DistinctDeduplicatesIdenticalAssets()
    {
        GameAsset Make() => new() { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "h" };

        var assets = new List<GameAsset> { Make(), Make(), Make() };

        Assert.Single(assets.Distinct());
    }

    [Fact]
    public void GameAsset_AssetsDifferingInAnyFieldAreNotEqual()
    {
        GameAsset Make() => new() { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "h" };

        var differing = new List<GameAsset>
        {
            new() { Id = "other", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "h" },
            new() { Id = "g", AssetType = GameAssetType.DLSS_G, Path = @"C:\a.dll", Version = "1", Hash = "h" },
            new() { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\b.dll", Version = "1", Hash = "h" },
            new() { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "2", Hash = "h" },
            new() { Id = "g", AssetType = GameAssetType.DLSS, Path = @"C:\a.dll", Version = "1", Hash = "other" },
        };

        foreach (var other in differing)
        {
            Assert.NotEqual<GameAsset>(Make(), other);
        }
    }

    [Fact]
    public void LocalRecord_EqualRecordsHashIdentically()
    {
        var a = LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false);
        var b = LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void LocalRecord_PathComparisonIsCaseInsensitive()
    {
        var a = LocalRecord.FromExpectedPath(@"C:\DLSS\A\nvngx_dlss.dll", false);
        var b = LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void LocalRecord_DifferentPathsAreNotEqual()
    {
        var a = LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false);
        var b = LocalRecord.FromExpectedPath(@"C:\dlss\b\nvngx_dlss.dll", false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void LocalRecord_EmptyPathNeverMatchesAnything()
    {
        var empty = LocalRecord.FromExpectedPath(string.Empty, false);
        var alsoEmpty = LocalRecord.FromExpectedPath("   ", false);
        var real = LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false);

        Assert.False(empty.Equals(alsoEmpty));
        Assert.False(empty.Equals(real));
        Assert.False(real.Equals(empty));
    }

    [Fact]
    public void LocalRecord_DistinctDeduplicatesByPath()
    {
        var records = new List<LocalRecord>
        {
            LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false),
            LocalRecord.FromExpectedPath(@"C:\dlss\a\nvngx_dlss.dll", false),
            LocalRecord.FromExpectedPath(@"C:\dlss\b\nvngx_dlss.dll", false),
        };

        Assert.Equal(2, records.Distinct().Count());
    }
}
