using System.ComponentModel;
using CommunityToolkit.WinUI.Collections;
using Kronos.Collections;
using Kronos.Interfaces;

namespace Kronos.Data;

internal class GameGroup : INotifyPropertyChanged
{
    public string Name { get; init; } = string.Empty;
    public GameLibrary? GameLibrary { get; init; }

    /// <summary>
    /// What the grid binds to: the group's games, through a wrapper that raises INotifyCollectionChanged.
    /// </summary>
    /// <remarks>
    /// GameManager's grouped CollectionViewSource reads this by name (ItemsPath "Games"), and the games page
    /// binds each library section's tiles to it directly. It used to be the AdvancedCollectionView itself,
    /// which on Uno left the Games page empty: see NotifyingCollectionView.
    /// </remarks>
    public NotifyingCollectionView? Games { get; init; }

    /// <summary>The filtered, sorted view underneath <see cref="Games"/>.</summary>
    public AdvancedCollectionView? View { get; init; }

    /// <summary>
    /// Whether the group has any games to show, so the games page can hide an empty library section.
    /// </summary>
    /// <remarks>
    /// GroupStyle.HidesIfEmpty did this when the GridView laid the groups out itself. On Uno it cannot, so
    /// the page draws the sections and needs to be told when one empties or fills.
    /// </remarks>
    public bool HasGames => Games?.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GameGroup(string name, GameLibrary? gameLibrary, AdvancedCollectionView? games)
    {
        Name = name;
        GameLibrary = gameLibrary;
        View = games;
        // Coalesces a library load's many changes into one Reset per UI tick. Falls back to resetting
        // immediately when there is no window to queue on yet.
        Games = games is null ? null : new NotifyingCollectionView(games, action => App.CurrentApp?.EnqueueOnUIThread(action) ?? false);

        if (Games is not null)
        {
            var hadGames = HasGames;
            Games.CollectionChanged += (_, _) =>
            {
                if (HasGames != hadGames)
                {
                    hadGames = HasGames;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGames)));
                }
            };
        }
    }
}
