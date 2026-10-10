using Kronos.Interfaces;

namespace Kronos.Data;

internal class GameGroup
{
    public string Name { get; init; } = string.Empty;
    public GameLibrary? GameLibrary { get; init; }

#if WINDOWS
    public CommunityToolkit.WinUI.Collections.AdvancedCollectionView? Games { get; init; }

    public GameGroup(string name, GameLibrary? gameLibrary, CommunityToolkit.WinUI.Collections.AdvancedCollectionView? games)
#else
    public Avalonia.Collections.ICollectionView? Games { get; init; }

    public GameGroup(string name, GameLibrary? gameLibrary, Avalonia.Collections.ICollectionView? games)
#endif
    {
        Name = name;
        GameLibrary = gameLibrary;
        Games = games;
    }
}