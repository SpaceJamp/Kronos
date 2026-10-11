using Kronos.Interfaces;

namespace Kronos.Data;

internal class GameGroup
{
    public string Name { get; init; } = string.Empty;
    public GameLibrary? GameLibrary { get; init; }

    // No #if here any more. This file is compiled only for the Windows target - the net10.0 group
    // sets EnableDefaultCompileItems to false and does not list it - so the #else branch was
    // unreachable, and it named Avalonia.Collections.ICollectionView, which no referenced package
    // provides. Dead code that cannot compile is worse than no code.
    public CommunityToolkit.WinUI.Collections.AdvancedCollectionView? Games { get; init; }

    public GameGroup(string name, GameLibrary? gameLibrary, CommunityToolkit.WinUI.Collections.AdvancedCollectionView? games)
    {
        Name = name;
        GameLibrary = gameLibrary;
        Games = games;
    }
}