using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Kronos.Data.Steam;
using Kronos.Interfaces;
using SQLite;

namespace Kronos.Data.ManuallyAdded;

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

    /// <summary>
    /// True when a store cover for the linked appid has been downloaded to the image cache. Used by
    /// the UI to tell the user when a link did not produce artwork, since the fetch itself only
    /// ever writes to the log.
    /// </summary>
    public bool HasCachedStoreCover => File.Exists(ExpectedCoverImage);

    /// <summary>
    /// Points this game at a store appid and drops any cached cover so the next processing run
    /// fetches artwork for the new link. Pass an empty string to unlink.
    /// </summary>
    /// <remarks>
    /// Deleting the cached cover is not optional. Covers are only refreshed once they are 7 days
    /// old, so linking after the fact would otherwise leave the tile blank until next week.
    /// </remarks>
    public void SetLinkedAppId(string appId)
    {
        LinkedAppId = (appId ?? string.Empty).Trim();
        DeleteCachedCoverImage();
    }

    public async Task ImportCoverImage(string imagePath)
    {
        using (var fileStream = File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ResizeCoverAsync(fileStream).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Downloads the store cover for the linked appid into the image cache. Does nothing when no
    /// usable appid is linked.
    /// </summary>
    /// <returns>True when a cover is now cached.</returns>
    /// <remarks>
    /// Public so the add-game dialog can fetch artwork while it is still open, which is what makes
    /// an automatic match visible to the user before they commit to it.
    /// </remarks>
    public async Task<bool> ImportStoreCoverAsync()
    {
        var appId = GetLinkedAppId();
        if (appId is null)
        {
            return false;
        }

        var coverUrls = await SteamCoverUrlResolver.GetLibraryCapsuleUrlsAsync(appId.Value).ConfigureAwait(false);
        foreach (var coverUrl in coverUrls)
        {
            if (await DownloadCoverAsync(coverUrl).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (coverUrls.Length == 0)
        {
            Logger.Error($"Manually added game {Title} is linked to appid {appId} but Steam offered no cover for it.");
        }
        else
        {
            Logger.Error($"Linked appid {appId} for \"{Title}\" resolved to {coverUrls.Length} cover urls but none could be downloaded.");
        }

        return false;
    }

    protected override async Task UpdateCacheImageAsync()
    {
        // A cover the user supplied always wins, and ImportCoverImage handles that case, so the
        // only thing left to do here is fetch store artwork for a linked appid. Without a link
        // there is nothing to fetch: the image is entirely manually managed by the user.
        await ImportStoreCoverAsync().ConfigureAwait(false);
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
