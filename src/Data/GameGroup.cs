using CommunityToolkit.WinUI.Collections;
using Chronos.Interfaces;

namespace Chronos.Data;

internal class GameGroup
{
    public string Name { get; init; } = string.Empty;
    public GameLibrary? GameLibrary { get; init; }
    public AdvancedCollectionView Games { get; init; }

    public GameGroup(string name, GameLibrary? gameLibrary, AdvancedCollectionView games)
    {
        Name = name;
        GameLibrary = gameLibrary;
        Games = games;
    }
}
