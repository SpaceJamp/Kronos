using Kronos.Data;
using Kronos.Data.ManuallyAdded;
#if WINDOWS
using Kronos.Data.Steam;
using Kronos.Data.GOG;
using Kronos.Data.EpicGamesStore;
using Kronos.Data.UbisoftConnect;
using Kronos.Data.Xbox;
using Kronos.Data.BattleNet;
using Kronos.Data.EAApp;
#endif
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kronos.Interfaces;

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

    /// <summary>
    /// Discovers the installed games this store knows about and returns them.
    /// </summary>
    /// <remarks>
    /// Cross-platform rather than Windows-only: enumeration is the one part of a store integration
    /// that can be written against any platform's on-disk layout, and keeping it on the interface
    /// is what lets the Linux CLI drive a library at all.
    /// </remarks>
    Task<List<Game>> ListGamesAsync(bool forceNeedsProcessing);

    /// <summary>Rehydrates this library's games from the database, without scanning.</summary>
    Task LoadGamesFromCacheAsync();

#if WINDOWS
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
#else
    /// <summary>
    /// The library implementation for a store on this platform, or null if that store is not
    /// discoverable here.
    /// </summary>
    /// <remarks>
    /// Only ManuallyAdded has an implementation on Linux. Every other store is discovered through
    /// the Windows registry, which has no equivalent, so those return null rather than throwing -
    /// callers enumerate enabled libraries in a loop, and an exception here would abandon the
    /// libraries that do work instead of skipping the ones that cannot.
    ///
    /// Porting a store means adding its library here and writing the discovery against that
    /// platform's layout (Steam's libraryfolders.vdf, for instance) rather than a registry key.
    /// </remarks>
    static IGameLibrary? GetGameLibrary(GameLibrary gameLibrary)
    {
        return gameLibrary switch
        {
            GameLibrary.ManuallyAdded => ManuallyAddedLibrary.Instance,
            _ => null,
        };
    }
#endif

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
