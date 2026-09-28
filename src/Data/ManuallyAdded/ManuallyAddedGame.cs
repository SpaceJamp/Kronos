using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Chronos.Data.Steam;
using Chronos.Interfaces;
using SQLite;

namespace Chronos.Data.ManuallyAdded;

[Table("manually_added_game")]
public class ManuallyAddedGame : Game
{
    public override GameLibrary GameLibrary => GameLibrary.ManuallyAdded;

    public override bool IsReadyToPlay => true;

    /// <summary>
    /// Optional Steam appid this game is a copy of, eg. a repack of a store game. When set the
    /// store cover is downloaded so the tile is not blank, since a manually added game has no
    /// store page of its own to fetch artwork from.
    /// </summary>
    [Column("linked_app_id")]
    public string LinkedAppId { get; set; } = string.Empty;

    public ManuallyAddedGame()
    {

    }
    public ManuallyAddedGame(string id)
    {
        PlatformId = id;
        SetID();
    }

    /// <summary>
    /// The store appid as a number, or null when <see cref="LinkedAppId"/> is not a valid appid.
    /// </summary>
    public int? GetLinkedAppId()
    {
        // Int32.TryParse accepts negatives and leading signs, and 0, none of which are appids.
        // Without this a value like "-1" would parse fine and then be sent to Steam as a real
        // request, so keep the validity check in one place.
        if (Int32.TryParse(LinkedAppId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) && appId > 0)
        {
            return appId;
        }

        return null;
    }

    /// <summary>
    /// True when <see cref="LinkedAppId"/> holds something that can be used as a Steam appid.
    /// </summary>
    public bool HasValidLinkedAppId => GetLinkedAppId() is not null;

    public async Task ImportCoverImage(string imagePath)
    {
        using (var fileStream = File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ResizeCoverAsync(fileStream).ConfigureAwait(false);
        }
    }

    protected override async Task UpdateCacheImageAsync()
    {
        // A cover the user supplied always wins, and ImportCoverImage handles that case, so the
        // only thing left to do here is fetch store artwork for a linked appid. Without a link
        // there is nothing to fetch: the image is entirely manually managed by the user.
        var appId = GetLinkedAppId();
        if (appId is null)
        {
            return;
        }

        var coverUrls = await SteamCoverUrlResolver.GetLibraryCapsuleUrlsAsync(appId.Value).ConfigureAwait(false);
        foreach (var coverUrl in coverUrls)
        {
            if (await DownloadCoverAsync(coverUrl).ConfigureAwait(false))
            {
                return;
            }
        }

        if (coverUrls.Length == 0)
        {
            Logger.Error($"Manually added game {Title} is linked to appid {appId} but Steam offered no cover for it.");
        }
    }

    public override bool UpdateFromGame(Game game)
    {
        var didChange = ParentUpdateFromGame(game);

        if (game is ManuallyAddedGame manuallyAddedGame)
        {
            if (LinkedAppId != manuallyAddedGame.LinkedAppId)
            {
                LinkedAppId = manuallyAddedGame.LinkedAppId;
                didChange = true;

                // The cached cover belongs to the old link. Covers are only refreshed once they
                // are 7 days old, so without dropping it here the tile would keep showing the
                // previous game's artwork for a week after the user corrected the appid.
                DeleteCachedCoverImage();
            }
        }

        return didChange;
    }

    void DeleteCachedCoverImage()
    {
        try
        {
            if (File.Exists(ExpectedCoverImage))
            {
                File.Delete(ExpectedCoverImage);
            }
        }
        catch (Exception err)
        {
            // A stale cover is a cosmetic problem, so never let it break the update.
            Logger.Error(err, $"Could not delete cached cover {ExpectedCoverImage} for {Title}");
        }
    }
}
