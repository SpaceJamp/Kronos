using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Kronos.Data.BattleNet;
using Kronos.Data.Xbox;
using Kronos.Interfaces;
using Kronos.Messages;

#if WINDOWS
using CommunityToolkit.WinUI.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Windows.System;
#else
using Avalonia.Collections;
using System.ComponentModel;
#endif

namespace Kronos.Data;

internal partial class GameManager : ObservableObject
{
    public static GameManager Instance { get; private set; } = new GameManager();

    // Because access to _allGames should be done on the UI thread we have _synchronisedAllGames which
    // will be used for adding/removing/fetching games. _allGames gets updated which will then be reflected
    // to the user.
    List<Game> _synchronisedAllGames = new List<Game>();
    ObservableCollection<Game> _allGames { get; } = new ObservableCollection<Game>();

#if WINDOWS
    public CollectionViewSource GroupedGameCollectionViewSource { get; init; }
    public CollectionViewSource UngroupedGameCollectionViewSource { get; init; }
#else
    // Linux uses Avalonia's CollectionView
    public ICollectionView GroupedGameView { get; private set; }
    public ICollectionView UngroupedGameView { get; private set; }
#endif

    [ObservableProperty]
    public partial bool UnknownAssetsFound { get; set; } = false;

    List<UnknownGameAsset> _unknownGameAssets { get; } = new List<UnknownGameAsset>();

    [ObservableProperty]
    public partial bool ShowHiddenGames { get; set; } = false;

    object gameLock = new object();
    object unknownGameAsseetLock = new object();

    GameGroup allGamesGroup;
    GameGroup favouriteGamesGroup;

#if WINDOWS
    public AdvancedCollectionView AllGamesView { get; init; }
    public AdvancedCollectionView FavouriteGamesView { get; init; }
#else
    public ICollectionView AllGamesView { get; private set; }
    public ICollectionView FavouriteGamesView { get; private set; }
#endif

    Dictionary<GameLibrary, GameGroup> libraryGameGroups = new Dictionary<GameLibrary, GameGroup>();
#if WINDOWS
    Dictionary<GameLibrary, AdvancedCollectionView> libraryGamesView = new Dictionary<GameLibrary, AdvancedCollectionView>();
#else
    Dictionary<GameLibrary, ICollectionView> libraryGamesView = new Dictionary<GameLibrary, ICollectionView>();
#endif

    Predicate<object> GetPredicateForAllGames(bool hideNonDLSSGames, string? filterText = null)
    {
        return (obj) =>
        {
            var game = (Game)obj;

            if (ShowHiddenGames == false && game.IsHidden == true)
            {
                return false;
            }

            bool matchesText = string.IsNullOrEmpty(filterText) || game.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            return (!hideNonDLSSGames || game.HasSwappableItems) && matchesText;
        };
    }

    Predicate<object> GetPredicateForFavouriteGames(bool hideNonDLSSGames, string? filterText = null)
    {
        return (obj) =>
        {
            var game = (Game)obj;

            if (ShowHiddenGames == false && game.IsHidden == true)
            {
                return false;
            }

            bool matchesText = string.IsNullOrEmpty(filterText) || game.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            return game.IsFavourite && (!hideNonDLSSGames || game.HasSwappableItems) && matchesText;
        };
    }


    Predicate<object> GetPredicateForLibraryGames(GameLibrary library, bool hideNonDLSSGames, string? filterText = null)
    {
        return (obj) =>
        {
            var game = (Game)obj;

            if (ShowHiddenGames == false && game.IsHidden == true)
            {
                return false;
            }

            bool matchesText = string.IsNullOrEmpty(filterText) || game.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            return game.GameLibrary == library && (!hideNonDLSSGames || game.HasSwappableItems) && matchesText;
        };
    }

    private GameManager()
    {
#if WINDOWS
        FavouriteGamesView = new AdvancedCollectionView(_allGames, true);
        FavouriteGamesView.Filter = GetPredicateForFavouriteGames(Settings.Instance.HideNonDLSSGames);
        FavouriteGamesView.ObserveFilterProperty(nameof(ShowHiddenGames));
        FavouriteGamesView.ObserveFilterProperty(nameof(Game.IsFavourite));
        FavouriteGamesView.ObserveFilterProperty(nameof(Game.HasSwappableItems));
        FavouriteGamesView.ObserveFilterProperty(nameof(Game.IsHidden));
        FavouriteGamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Title), SortDirection.Ascending));

        AllGamesView = new AdvancedCollectionView(_allGames, true);
        AllGamesView.Filter = GetPredicateForAllGames(Settings.Instance.HideNonDLSSGames);
        AllGamesView.ObserveFilterProperty(nameof(ShowHiddenGames));
        AllGamesView.ObserveFilterProperty(nameof(Game.HasSwappableItems));
        AllGamesView.ObserveFilterProperty(nameof(Game.IsHidden));
        AllGamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Title), SortDirection.Ascending));

        allGamesGroup = new GameGroup("All Games", null, AllGamesView);
        favouriteGamesGroup = new GameGroup("Favourites", null, FavouriteGamesView);

        // The grouped and ungrouped views are two lists of GameGroup over the SAME _allGames
        // collection. Each library's view filters _allGames down to that library, so adding a
        // game to _allGames is enough to make it appear in the matching group - the groups
        // themselves never own a copy of the games.
        var groupedList = new ObservableCollection<GameGroup>
        {
            favouriteGamesGroup
        };

        var ungroupedList = new List<GameGroup>
        {
            favouriteGamesGroup,
            allGamesGroup
        };

        foreach (var gameLibraryEnum in GetGameLibraries(false))
        {
            var gameLibrary = IGameLibrary.GetGameLibrary(gameLibraryEnum);

            var gameView = new AdvancedCollectionView(_allGames, true);
            gameView.Filter = GetPredicateForLibraryGames(gameLibraryEnum, Settings.Instance.HideNonDLSSGames);
            gameView.ObserveFilterProperty(nameof(ShowHiddenGames));
            gameView.ObserveFilterProperty(nameof(Game.HasSwappableItems));
            gameView.ObserveFilterProperty(nameof(Game.IsHidden));
            gameView.SortDescriptions.Add(new SortDescription(nameof(Game.Title), SortDirection.Ascending));

            libraryGamesView[gameLibraryEnum] = gameView;

            var gameGroup = new GameGroup(gameLibrary.Name, gameLibraryEnum, gameView);
            groupedList.Add(gameGroup);
            libraryGameGroups[gameLibraryEnum] = gameGroup;
        }

        GroupedGameCollectionViewSource = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = groupedList,
            ItemsPath = new PropertyPath("Games")
        };

        UngroupedGameCollectionViewSource = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = ungroupedList,
            ItemsPath = new PropertyPath("Games")
        };

        WeakReferenceMessenger.Default.Register<GameLibrariesOrderChangedMessage>(this, (sender, message) =>
        {
            var reordered = groupedList.ToList();

            groupedList.Clear();

            // Favourites is always first.
            groupedList.Add(reordered[0]);
            reordered.RemoveAt(0);

            // Then each library in the order the user set in settings.
            foreach (var gameLibrarySetting in Settings.Instance.GameLibrarySettings)
            {
                var groupedItem = reordered.FirstOrDefault(x => x.GameLibrary == gameLibrarySetting.GameLibrary);
                if (groupedItem is null)
                {
                    continue;
                }
                groupedList.Add(groupedItem);
                reordered.Remove(groupedItem);
            }

            if (reordered.Count > 0)
            {
                Logger.Error($"Somehow extra grouped items were left over. {string.Join(", ", reordered)}");
            }
        });
#else
        // Linux: Use Avalonia's CollectionView
        FavouriteGamesView = new CollectionView(_allGames);
        FavouriteGamesView.Filter = GetPredicateForFavouriteGames(Settings.Instance.HideNonDLSSGames);
        FavouriteGamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Title), ListSortDirection.Ascending));

        AllGamesView = new CollectionView(_allGames);
        AllGamesView.Filter = GetPredicateForAllGames(Settings.Instance.HideNonDLSSGames);
        AllGamesView.SortDescriptions.Add(new SortDescription(nameof(Game.Title), ListSortDirection.Ascending));

        GroupedGameView = new CollectionView(_allGames);
        UngroupedGameView = new CollectionView(_allGames);

        allGamesGroup = new GameGroup("All Games", null, null);
        favouriteGamesGroup = new GameGroup("Favourites", null, null);

        foreach (var library in Enum.GetValues<GameLibrary>())
        {
            var view = new CollectionView(_allGames);
            view.Filter = GetPredicateForLibraryGames(library, Settings.Instance.HideNonDLSSGames);
            view.SortDescriptions.Add(new SortDescription(nameof(Game.Title), ListSortDirection.Ascending));
            libraryGamesView[library] = view;

            var group = new GameGroup(library.ToString(), library, null);
            libraryGameGroups[library] = group;
        }
#endif
    }

    public async Task LoadGamesFromCacheAsync()
    {
        UnknownAssetsFound = false;
        _unknownGameAssets.Clear();

        foreach (var gameLibraryEnum in GameManager.Instance.GetGameLibraries(true))
        {
            var gameLibrary = IGameLibrary.GetGameLibrary(gameLibraryEnum);
            if (gameLibrary.IsEnabled)
            {
                await gameLibrary.LoadGamesFromCacheAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task LoadGamesAsync(bool forceNeedsProcessing = false)
    {
        var tasks = new List<Task<List<Game>>>();
        if (forceNeedsProcessing == true)
        {
            lock (unknownGameAsseetLock)
            {
                _unknownGameAssets.Clear();
            }
        }
        foreach (var gameLibraryEnum in GameManager.Instance.GetGameLibraries(true))
        {
            var gameLibrary = IGameLibrary.GetGameLibrary(gameLibraryEnum);
            if (gameLibrary.IsEnabled)
            {
                tasks.Add(gameLibrary.ListGamesAsync(forceNeedsProcessing));
            }
        }

        // Add games to the game library when the tasks is completed.
        while (tasks.Any())
        {
            var completedTask = await Task.WhenAny(tasks);
            tasks.Remove(completedTask);

            // Task.WhenAny returns whichever task finished first, including one that finished
            // faulted. Reading .Result on a faulted task rethrows, which used to abandon this loop
            // and throw out of LoadGamesAsync, so a single misbehaving store library cost the user
            // every game from every other library as well. A library that cannot be read is a
            // normal condition, not a reason to discard the rest.
            List<Game> completedGames;
            try
            {
                completedGames = completedTask.Result;
            }
            catch (Exception err)
            {
                Logger.Error(err, "A game library failed to load. The other libraries are unaffected.");
                continue;
            }

            foreach (var game in completedGames)
            {
                AddGame(game);
            }
        }
    }

    public ICollectionView GetGameCollection(string? filterText = null)
    {
#if WINDOWS
        // Refresh all filters.
        using (FavouriteGamesView.DeferRefresh())
        {
            FavouriteGamesView.Filter = GetPredicateForFavouriteGames(Settings.Instance.HideNonDLSSGames, filterText);
        }

        using (AllGamesView.DeferRefresh())
        {
            AllGamesView.Filter = GetPredicateForAllGames(Settings.Instance.HideNonDLSSGames, filterText);
        }

        if (Settings.Instance.GroupGameLibrariesTogether)
        {
            // Only refresh libraries when we are going to the grouped view.
            foreach (var keyValuePair in libraryGamesView)
            {
                using (keyValuePair.Value.DeferRefresh())
                {
                    keyValuePair.Value.Filter = GetPredicateForLibraryGames(keyValuePair.Key, Settings.Instance.HideNonDLSSGames, filterText);
                }
            }

            return GroupedGameCollectionViewSource.View;
        }
        else
        {
            return UngroupedGameCollectionViewSource.View;
        }
#else
        // Linux: Avalonia's CollectionView doesn't have DeferRefresh, just set filter directly
        FavouriteGamesView.Filter = GetPredicateForFavouriteGames(Settings.Instance.HideNonDLSSGames, filterText);
        AllGamesView.Filter = GetPredicateForAllGames(Settings.Instance.HideNonDLSSGames, filterText);

        if (Settings.Instance.GroupGameLibrariesTogether)
        {
            foreach (var keyValuePair in libraryGamesView)
            {
                keyValuePair.Value.Filter = GetPredicateForLibraryGames(keyValuePair.Key, Settings.Instance.HideNonDLSSGames, filterText);
            }

            return GroupedGameView;
        }
        else
        {
            return UngroupedGameView;
        }
#endif
    }

    public List<Game> GetSynchronisedGamesListCopy()
    {
        lock (gameLock)
        {
            var list = new List<Game>(_synchronisedAllGames);
            return list;
        }
    }


    public Game AddGame(Game game, bool scrollIntoView = false)
    {
        lock (gameLock)
        {
            if (_synchronisedAllGames.Contains(game) == true)
            {
                // This probably checks the game collection twice looking for the game.
                // We could do away with this, but in theory this if is never hit
                var oldGame = _synchronisedAllGames.First(x => x.Equals(game));

#if WINDOWS
                App.CurrentApp.RunOnUIThread(() =>
                {
                    oldGame.UpdateFromGame(game);
                });
#else
                // Linux: Direct update since we're on the same thread
                oldGame.UpdateFromGame(game);
#endif

                Debug.WriteLine($"Reusing old game: {game.Title}");
                return oldGame;
            }
            else
            {
                Debug.WriteLine($"Adding new game: {game.Title}");

                _synchronisedAllGames.Add(game);

#if WINDOWS
                App.CurrentApp.RunOnUIThread(() =>
                {
                    _allGames.Add(game);

                    if (scrollIntoView)
                    {
                        App.CurrentApp.MainWindow.GameGridPage?.ScrollToGame(game);
                    }
                });
#else
                // Linux: Direct update
                _allGames.Add(game);
#endif

                return game;
            }
        }
    }

    public void RemoveGame(Game game)
    {
        lock (gameLock)
        {
            _synchronisedAllGames.Remove(game);

#if WINDOWS
            App.CurrentApp.RunOnUIThread(() =>
            {
                _allGames.Remove(game);
            });
#else
            _allGames.Remove(game);
#endif
        }
    }

    public void RemoveAllGames()
    {
        lock (gameLock)
        {
            // TODO: Cancel loading of games here
            _synchronisedAllGames.Clear();

#if WINDOWS
            App.CurrentApp.RunOnUIThread(() =>
            {
                _allGames.Clear();
            });
#else
            _allGames.Clear();
#endif
        }
    }

    public TGame? GetGame<TGame>(string platformId) where TGame : Game
    {
        lock (gameLock)
        {
            foreach (var game in _synchronisedAllGames)
            {
                if (game is TGame platformGame)
                {
                    if (game.PlatformId == platformId)
                    {
                        return platformGame;
                    }
                }
            }

            return null;
        }
    }

    public List<TGame> GetGames<TGame>() where TGame : Game
    {
        lock (gameLock)
        {
            var games = new List<TGame>();
            foreach (var game in _synchronisedAllGames)
            {
                if (game is TGame tGame)
                {
                    games.Add(tGame);
                }
            }
            return games;
        }
    }


    public bool CheckIfGameIsAdded(string installPath)
    {
        lock (gameLock)
        {
            foreach (var game in _synchronisedAllGames)
            {
                if (game.InstallPath?.Equals(installPath, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }
        return false;
    }


    public void AddUnknownGameAssets(GameLibrary gameLibrary, string gameTitle, List<GameAsset> gameAssets)
    {
        lock (unknownGameAsseetLock)
        {
            if (UnknownAssetsFound == false)
            {
#if WINDOWS
                App.CurrentApp.RunOnUIThread(() =>
                {
                    UnknownAssetsFound = true;
                });
#else
                UnknownAssetsFound = true;
#endif
            }

            foreach (var gameAsset in gameAssets)
            {
                _unknownGameAssets.Add(new UnknownGameAsset(gameLibrary, gameTitle, gameAsset));
            }
        }
    }

    public List<UnknownGameAsset> GetUnknownGameAssets()
    {
        var unknownGameAssets = new List<UnknownGameAsset>();

        lock (unknownGameAsseetLock)
        {
            unknownGameAssets.AddRange(_unknownGameAssets);
        }

        return unknownGameAssets;
    }

    public GameLibrarySettings? GetGameLibrarySettings(GameLibrary gameLibrary)
    {
        return Settings.Instance.GameLibrarySettings.FirstOrDefault(x => x.GameLibrary == gameLibrary);
    }

    public List<GameLibrary> GetGameLibraries(bool onlyEnabled)
    {
        var gameLibrariesToReturn = new List<GameLibrary>();

        foreach (var gameLibrarySetting in Settings.Instance.GameLibrarySettings)
        {
            if (gameLibrarySetting.IsEnabled == false && onlyEnabled == true)
            {
                continue;
            }

            gameLibrariesToReturn.Add(gameLibrarySetting.GameLibrary);
        }

        return gameLibrariesToReturn;
    }


    public bool CanLaunchGame(Game game)
    {
        // Xbox App games are only valid if ApplicationId is loaded.
        if (game.GameLibrary == GameLibrary.XboxApp)
        {
            if (game is XboxGame xboxGame && string.IsNullOrWhiteSpace(xboxGame.ApplicationId) == false)
            {
                return true;
            }
        }

        // We can only launch Battle.net games if Battle.net client is installed
        if (game.GameLibrary == GameLibrary.BattleNet)
        {
            if (game is BattleNetGame battleNetGame)
            {
                if (string.IsNullOrWhiteSpace(battleNetGame.LauncherId) == false)
                {
                    return true;
                }
            }
        }

        return game.GameLibrary switch
        {
            GameLibrary.Steam => true,
            GameLibrary.EpicGamesStore => true,
            GameLibrary.EAApp => true,
            _ => false,
        };
    }

    public async Task LaunchGameAsync(Game game)
    {
        if (CanLaunchGame(game) == false)
        {
            Logger.Error($"Cannot launch game {game.Title} from {game.GameLibrary}");
            return;
        }

#if WINDOWS
        if (game.GameLibrary == GameLibrary.Steam)
        {
            await Launcher.LaunchUriAsync(new Uri($"steam://rungameid/{game.PlatformId}"));
        }
        else if (game.GameLibrary == GameLibrary.EpicGamesStore)
        {
            var installPathString = Uri.EscapeDataString(game.InstallPath);
            await Launcher.LaunchUriAsync(new Uri($"com.epicgames.launcher://apps/{installPathString}?action=launch&silent=true"));
        }
        else if (game.GameLibrary == GameLibrary.EAApp)
        {
            await Launcher.LaunchUriAsync(new Uri($"origin2://game/launch?offerIds={game.PlatformId}"));
        }
        else if (game.GameLibrary == GameLibrary.XboxApp)
        {
            if (game is XboxGame xboxGame)
            {
                var launchCode = $"shell:appsFolder\\{xboxGame.PlatformId}!{xboxGame.ApplicationId}";
                Process.Start(new ProcessStartInfo("explorer.exe", launchCode) { UseShellExecute = true });
            }
        }
        else if (game.GameLibrary == GameLibrary.BattleNet)
        {
            if (game is BattleNetGame battleNetGame && File.Exists(BattleNetLibrary.Instance.ClientPath))
            {
                Process.Start(new ProcessStartInfo(BattleNetLibrary.Instance.ClientPath,  $"--exec=\"launch {battleNetGame.LauncherId}\"") { UseShellExecute = true });
            }
        }
#else
        // Linux: Use xdg-open or steam protocol
        if (game.GameLibrary == GameLibrary.Steam)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"steam://rungameid/{game.PlatformId}",
                    UseShellExecute = true
                }
            };
            process.Start();
        }
        else if (game.GameLibrary == GameLibrary.EpicGamesStore)
        {
            // Epic doesn't have a Linux protocol handler by default
            Logger.Warning("Epic Games Store launch not implemented on Linux");
        }
        else if (game.GameLibrary == GameLibrary.EAApp)
        {
            // EA App doesn't run on Linux
            Logger.Warning("EA App launch not implemented on Linux");
        }
        else
        {
            Logger.Warning($"Cannot launch game {game.Title} from {game.GameLibrary} on Linux");
        }
#endif
    }
}