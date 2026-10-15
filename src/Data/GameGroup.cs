using CommunityToolkit.WinUI.Collections;
using Kronos.Collections;
using Kronos.Interfaces;

namespace Kronos.Data;

internal class GameGroup
{
    public string Name { get; init; } = string.Empty;
    public GameLibrary? GameLibrary { get; init; }

    /// <summary>
    /// What the grid binds to: the group's games, through a wrapper that raises INotifyCollectionChanged.
    /// </summary>
    /// <remarks>
    /// GameManager's grouped CollectionViewSource reads this by name (ItemsPath "Games"). It used to be the
    /// AdvancedCollectionView itself, which on Uno left the Games page empty: see NotifyingCollectionView.
    /// </remarks>
    public NotifyingCollectionView? Games { get; init; }

    /// <summary>The filtered, sorted view underneath <see cref="Games"/>.</summary>
    public AdvancedCollectionView? View { get; init; }

    public GameGroup(string name, GameLibrary? gameLibrary, AdvancedCollectionView? games)
    {
        Name = name;
        GameLibrary = gameLibrary;
        View = games;
        // Coalesces a library load's many changes into one Reset per UI tick. Falls back to resetting
        // immediately when there is no window to queue on yet.
        Games = games is null ? null : new NotifyingCollectionView(games, action => App.CurrentApp?.EnqueueOnUIThread(action) ?? false);
    }
}
