using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Chronos.Data;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;


internal partial class GameLibrarySelectorControlModel : ObservableObject
{
    public ObservableCollection<GameLibrarySelector> GameLibraries { get; } = new ObservableCollection<GameLibrarySelector>();

    public GameLibrarySelectorControlModel()
    {
        var gameLibraryEnumList = GameManager.Instance.GetGameLibraries(false);
        foreach (var gameLibraryEnum in gameLibraryEnumList)
        {
            var gameLibrary = IGameLibrary.GetGameLibrary(gameLibraryEnum);
            GameLibraries.Add(new GameLibrarySelector(gameLibrary));
        }

        // Only add CollectionChanged after it has been loaded initially.
        GameLibraries.CollectionChanged += GameLibraries_CollectionChanged;

        LanguageManager.Instance.OnLanguageChanged += () =>
        {
            foreach (var gameLibrarySelector in GameLibraries)
            {
                gameLibrarySelector.ReloadLabels();
            }
        };
    }

    void GameLibraries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            var newGameLibrarySettings = new List<GameLibrarySettings>();
            foreach (var gameLibrarySelector in GameLibraries)
            {
                if (gameLibrarySelector is null)
                {
                    continue;
                }

                var settings = GameManager.Instance.GetGameLibrarySettings(gameLibrarySelector.GameLibrary.GameLibrary);
                if (settings is null)
                {
                    return;
                }

                newGameLibrarySettings.Add(settings);
            }

            Settings.Instance.GameLibrarySettings = newGameLibrarySettings.ToArray();
        }
    }
}
