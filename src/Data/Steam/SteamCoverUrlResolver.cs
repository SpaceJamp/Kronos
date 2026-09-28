using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Chronos.Data.Steam.SteamAPI;

namespace Chronos.Data.Steam;

/// <summary>
/// Resolves the vertical library cover (the poster) for a Steam appid.
///
/// This is split out of <see cref="SteamGame"/> so that a manually added game which has been
/// linked to a store appid can fetch the same artwork, rather than duplicating the API dance.
/// </summary>
internal static class SteamCoverUrlResolver
{
    // There are 3 different CDNs, Steam does not tell us which one will serve the asset, so
    // callers are expected to try each URL in turn until one downloads.
    internal static readonly string[] Cdns =
    {
        "https://shared.fastly.steamstatic.com",
        "https://shared.steamstatic.com",
        "https://shared.akamai.steamstatic.com"
    };

    /// <summary>
    /// Returns one candidate cover url per CDN, in the order they should be tried.
    /// Returns an empty list if the appid is unknown or has no vertical cover, so callers
    /// should treat an empty result as "no artwork available" rather than an error.
    /// </summary>
    internal static async Task<string[]> GetLibraryCapsuleUrlsAsync(int appId)
    {
        try
        {
            var getItemsInput = new GetItemsInput();
            getItemsInput.Ids.Add(new StoreItemId() { AppId = appId });
            getItemsInput.DataRequest.IncludeAssets = true;

            var jsonPayload = JsonSerializer.Serialize(getItemsInput, SourceGenerationContext.Default.GetItemsInput);
            var payloadUrlEncoded = HttpUtility.UrlEncode(jsonPayload);

            using (var steamApiResponse = await App.CurrentApp.HttpClient.GetAsync($"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={payloadUrlEncoded}", System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                if (steamApiResponse.IsSuccessStatusCode == false)
                {
                    Logger.Error($"IStoreBrowseService returned {steamApiResponse.StatusCode} for appid {appId}.");
                    return Array.Empty<string>();
                }

                using (var responseStream = await steamApiResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var response = JsonSerializer.Deserialize(responseStream, SourceGenerationContext.Default.SteamAPIResponseGetItemsResponse);
                    if (response?.Response?.StoreItems.Count > 0 != true)
                    {
                        Logger.Error($"IStoreBrowseService returned no store items for appid {appId}.");
                        return Array.Empty<string>();
                    }

                    var assets = response.Response.StoreItems[0].Assets;
                    if (assets is null || string.IsNullOrWhiteSpace(assets.AssetUrlFormat))
                    {
                        Logger.Error($"IStoreBrowseService returned no asset url format for appid {appId}.");
                        return Array.Empty<string>();
                    }

                    // We are only checking LibraryCapsule2x, hopefully it exists for all games
                    if (string.IsNullOrWhiteSpace(assets.LibraryCapsule2x))
                    {
                        Logger.Error($"IStoreBrowseService returned no library capsule for appid {appId}.");
                        return Array.Empty<string>();
                    }

                    var filename = assets.LibraryCapsule2x;
                    return BuildCoverUrls(assets.AssetUrlFormat, filename);
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, $"Failed to resolve a Steam cover url for appid {appId}.");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Builds one cover url per CDN from the asset url format Steam returned. The format carries a
    /// ${FILENAME} placeholder and a cache busting query, both of which have to survive.
    /// </summary>
    internal static string[] BuildCoverUrls(string assetUrlFormat, string libraryCapsule2x)
    {
        if (string.IsNullOrWhiteSpace(assetUrlFormat) || string.IsNullOrWhiteSpace(libraryCapsule2x))
        {
            return Array.Empty<string>();
        }

        var assetPath = assetUrlFormat.Replace("${FILENAME}", libraryCapsule2x);
        var urls = new string[Cdns.Length];
        for (var i = 0; i < Cdns.Length; i++)
        {
            urls[i] = $"{Cdns[i]}/store_item_assets/{assetPath}";
        }

        return urls;
    }
}
