using Chronos.Data;
using Chronos.Data.EpicGamesStore;
using Chronos.Data.GOG;
using Chronos.Data.Steam;
using Chronos.Data.UbisoftConnect;
using Chronos.Data.Xbox;
using Chronos.Data.ManuallyAdded;
using Chronos.Data.BattleNet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Chronos.Data.EAApp;

namespace Chronos.Interfaces;

[Flags]
public enum GameLibrary : uint
{
    Steam = 1,
    GOG = 2,
    EpicGamesStore = 4,
    UbisoftConnect = 8,
    XboxApp = 16,
    ManuallyAdded = 32,
    BattleNet = 64,
    EAApp = 128,
};

public interface IGameLibrary
{
    GameLibrary GameLibrary { get; }
    GameLibrarySettings? GameLibrarySettings { get; }
    string Name { get; }
    Type GameType { get; }

    Task<List<Game>> ListGamesAsync(bool forceNeedsProcessing);
    Task LoadGamesFromCacheAsync();
    bool IsInstalled();

    static IGameLibrary GetGameLibrary(GameLibrary gameLibrary)
    {
        return gameLibrary switch
        {
            GameLibrary.Steam => SteamLibrary.Instance,
            GameLibrary.GOG => GOGLibrary.Instance,
            GameLibrary.EpicGamesStore => EpicGamesStoreLibrary.Instance,
            GameLibrary.UbisoftConnect => UbisoftConnectLibrary.Instance,
            GameLibrary.XboxApp => XboxLibrary.Instance,
            GameLibrary.BattleNet => BattleNetLibrary.Instance,
            GameLibrary.EAApp => EAAppLibrary.Instance,
            GameLibrary.ManuallyAdded => ManuallyAddedLibrary.Instance,
            _ => throw new Exception($"Could not load game library {gameLibrary}."),
        };
    }

    public bool IsEnabled
    {
        get
        {
            return GameLibrarySettings?.IsEnabled ?? false;
        }
    }

    public void Disable()
    {
        if (GameLibrarySettings is not null)
        {
            if (GameLibrarySettings.IsEnabled == true)
            {
                GameLibrarySettings.IsEnabled = false;
                Settings.Instance.SaveJson();
            }
        }
    }

    public void Enable()
    {
        if (GameLibrarySettings is not null)
        {
            if (GameLibrarySettings.IsEnabled == false)
            {
                GameLibrarySettings.IsEnabled = true;
                Settings.Instance.SaveJson();
            }
        }
    }
}
