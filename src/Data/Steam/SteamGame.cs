using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kronos.Interfaces;
using SQLite;

namespace Kronos.Data.Steam;

[Table("steam_game")]
internal partial class SteamGame : Game
{
    public override GameLibrary GameLibrary => GameLibrary.Steam;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadyToPlay))]
    [Column("state_flags")]
    public partial SteamStateFlag StateFlags { get; set; }

    public override bool IsReadyToPlay
    {
        get
        {
            const SteamStateFlag allowedFlags = SteamStateFlag.StateFullyInstalled | SteamStateFlag.StateAppRunning;
            return StateFlags != 0 && (StateFlags & ~allowedFlags) == 0;
        }
    }

    public SteamGame()
    {

    }

    public SteamGame(string appId)
    {
        PlatformId = appId;
        SetID();
    }

    protected override async Task UpdateCacheImageAsync()
    {
        // Try get image from the local disk first.
        var localHeaderImagePath = Path.Combine(SteamLibrary.GetInstallPath(), "appcache", "librarycache", $"{PlatformId}_library_600x900.jpg");
        if (File.Exists(localHeaderImagePath))
        {
            using (var fileStream = File.Open(localHeaderImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await ResizeCoverAsync(fileStream).ConfigureAwait(false);
            }
            return;
        }

        // Special case for Steamworks redistributable. 
        if (PlatformId == "228980")
        {
            await DownloadCoverAsync($"https://steamcdn-a.akamaihd.net/steam/apps/{PlatformId}/header.jpg").ConfigureAwait(false);
            return;            
        }

        // Try download via IStoreBrowseService first.
        var didDownload = await DownloadCoverFromIStoreBrowseService();
        if (didDownload == false)
        {
            // Try the old cover system?
            didDownload = await DownloadCoverAsync($"https://steamcdn-a.akamaihd.net/steam/apps/{PlatformId}/library_600x900_2x.jpg").ConfigureAwait(false);

            if (didDownload == false)
            {
                Logger.Error($"Tried to get Steam cover for {PlatformId} but was unable to get it from both old and new Steam CDNs.");
            }
        }
    }

    async Task<bool> DownloadCoverFromIStoreBrowseService()
    {
        if (Int32.TryParse(PlatformId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
        {
            Logger.Error($"PlatformId '{PlatformId}' is not a valid appid, so could not get a Steam cover for it.");
            return false;
        }

        var coverUrls = await SteamCoverUrlResolver.GetLibraryCapsuleUrlsAsync(appId).ConfigureAwait(false);
        foreach (var coverUrl in coverUrls)
        {
            if (await DownloadCoverAsync(coverUrl).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (coverUrls.Length == 0)
        {
            Logger.Error($"Tried to get Steam cover for {PlatformId} from IStoreBrowseService but it had no vertical cover to offer.");
        }
        else
        {
            Logger.Error($"Tried all {coverUrls.Length} known CDNs to get Steam cover for {PlatformId} but all had failed.");
        }

        return false;
    }

    public override bool UpdateFromGame(Game game)
    {
        var didChange = ParentUpdateFromGame(game);

        if (game is SteamGame steamGame)
        {
            if (StateFlags != steamGame.StateFlags)
            {
                StateFlags = steamGame.StateFlags;
                didChange = true;
            }
        }

        return didChange;
    }
}
