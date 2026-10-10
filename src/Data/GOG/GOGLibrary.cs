using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;
using Kronos.Helpers;
using Kronos.Interfaces;
using Microsoft.Win32;
using SQLite;

namespace Kronos.Data.GOG;

internal class GOGLibrary : IGameLibrary
{
    public GameLibrary GameLibrary => GameLibrary.GOG;
    public string Name => "GOG";

    public Type GameType => typeof(GOGGame);

    static GOGLibrary? instance;
    public static GOGLibrary Instance => instance ??= new GOGLibrary();

    GameLibrarySettings? _gameLibrarySettings;
    public GameLibrarySettings? GameLibrarySettings => _gameLibrarySettings ??= GameManager.Instance.GetGameLibrarySettings(GameLibrary);

    private GOGLibrary()
    {

    }

    public bool IsInstalled()
    {
        // We check for the registry key as offline installers will still make this, even if
        // the galaxy-2.0.db from GOG Galaxy is not found.
        //
        // Guarded: this has no try/catch, and OpenBaseKey throws PlatformNotSupportedException off
        // Windows as well as SecurityException on a locked-down machine. IsInstalled is called at the
        // top of ListGamesAsync, so an escaping exception there lost the entire GOG library.
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var registryKey = hklm.OpenSubKey(@"SOFTWARE\GOG.com\Games");

            return registryKey is not null;
        }
        catch (Exception err) when (err is PlatformNotSupportedException or SecurityException or UnauthorizedAccessException)
        {
            Logger.Error(err, "Unable to read the GOG registry key. Assuming GOG is not installed.");
            return false;
        }
    }

    public async Task<List<Game>> ListGamesAsync(bool forceNeedsProcessing = false)
    {
        if (IsInstalled() == false)
        {
            return new List<Game>();
        }

        var cachedGames = GameManager.Instance.GetGames<GOGGame>();

        var gogGames = new List<GOGGame>();

        using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        {
            using (var registryKey = hklm.OpenSubKey(@"SOFTWARE\GOG.com\Games"))
            {
                if (registryKey is null)
                {
                    // Something bad happened.
                    // TODO: Clear cache?
                    return new List<Game>();
                }

                // For each of the installed games, setup an initial GOG
                foreach (var subkey in registryKey.GetSubKeyNames())
                {
                    using (var gameKey = registryKey.OpenSubKey(subkey))
                    {
                        if (gameKey is null)
                        {
                            continue;
                        }

                        var gameId = gameKey.GetValue("gameID") as string;
                        var gameName = gameKey.GetValue("gameName") as string;
                        var gamePath = gameKey.GetValue("path") as string;

                        if (string.IsNullOrEmpty(gameId))
                        {
                            Logger.Error("Issue loading GOG Game, no gameId found.");
                            continue;
                        }


                        if (string.IsNullOrEmpty(gameName))
                        {
                            Logger.Error("Issue loading GOG Game, no gameName found.");
                            continue;
                        }


                        if (string.IsNullOrEmpty(gamePath))
                        {
                            Logger.Error("Issue loading GOG Game, no gamePath found.");
                            continue;
                        }


                        // If the entry is DLC we don't need to show it as an individual item.
                        var dependsOn = gameKey.GetValue("dependsOn") as string;
                        if (string.IsNullOrEmpty(dependsOn) == false)
                        {
                            continue;
                        }

                        var cachedGame = GameManager.Instance.GetGame<GOGGame>(gameId);
                        var activeGame = cachedGame ?? new GOGGame(gameId);
                        activeGame.Title = gameName;  // TODO: Will this be a problem if the game is already loaded
                        activeGame.InstallPath = PathHelpers.NormalizePath(gamePath);

                        if (activeGame.IsInIgnoredPath())
                        {
                            continue;
                        }

                        if (Directory.Exists(activeGame.InstallPath) == false)
                        {
                            Logger.Warning($"{Name} library could not load game {activeGame.Title} ({activeGame.PlatformId}) because install path does not exist: {activeGame.InstallPath}");
                            continue;
                        }

                        // If the game is not from cache, force processing
                        if (cachedGame is null)
                        {
                            activeGame.NeedsProcessing = true;
                        }

                        gogGames.Add(activeGame);
                    }
                }
            }
        }

        // No installed games found.
        if (gogGames.Count == 0)
        {
            // TODO: Flush cache?
            return new List<Game>();
        }


        // Now that we have games we attempt to load covers for them.


        // If GOG Galaxy is installed we can get images from it.
        var storageFileLocation = GetStorageFileLocation();
        if (string.IsNullOrWhiteSpace(storageFileLocation) == false && File.Exists(storageFileLocation) == true)
        {
            //await Task.Delay(1);
            var db = new SQLiteAsyncConnection(storageFileLocation, SQLiteOpenFlags.ReadOnly);

            // These two lookups used to sit outside any try, so a GOG Galaxy that was running (the
            // database is locked), or a schema that shipped without the table, threw out of
            // ListGamesAsync and cost the user every GOG game rather than just their cover art. Both
            // values are only used to pick nicer images, so a failure just means we fall back to the
            // hardcoded ids.
            // Default resource type for verticalCover images is 3. We default to this, but we also add try load it in case it changes.
            var webCacheResourceTypeId = 3;
            var gamePieceTypeId = 378;

            try
            {
                var webCacheResourceType = (await db.QueryAsync<WebCacheResourceType>("SELECT * FROM WebCacheResourceTypes WHERE type=?", "verticalCover").ConfigureAwait(false)).FirstOrDefault();
                if (webCacheResourceType is not null)
                {
                    webCacheResourceTypeId = webCacheResourceType.Id;
                }

                var gamePieceType = (await db.QueryAsync<GamePieceType>("SELECT * FROM GamePieceTypes WHERE type=?", "originalImages").ConfigureAwait(false)).FirstOrDefault();
                if (gamePieceType is not null)
                {
                    gamePieceTypeId = gamePieceType.Id;
                }
            }
            catch (Exception err)
            {
                Logger.Error(err, $"Could not read image metadata from {storageFileLocation}. Falling back to default image types.");
            }

            var programDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

            // var limitedDetails = await db.QueryAsync<LimitedDetail>("SELECT * FROM LimitedDetails").ConfigureAwait(false);
            // var installedBaseProducts = await db.QueryAsync<InstalledBaseProduct>("SELECT * FROM InstalledBaseProducts").ConfigureAwait(false);
            foreach (var gogGame in gogGames)
            {
                try
                {
                    /*
                    var installedBaseProduct = (await db.QueryAsync<InstalledBaseProduct>("SELECT * FRO< InstalledBaseProducts WHERE ProductId=? LIMIT 1", gogGame.Id).ConfigureAwait(false)).FirstOrDefault();
                    if (installedBaseProduct is null)
                    {
                        continue;
                    }
                    */

                    var limitedDetail = (await db.QueryAsync<LimitedDetail>("SELECT * FROM LimitedDetails WHERE ProductId=? LIMIT 1", gogGame.PlatformId).ConfigureAwait(false)).FirstOrDefault();
                    if (limitedDetail is null)
                    {
                        continue;
                    }

                    var localCoverImages = new List<string>();

                    var releaseKey = $"gog_{gogGame.PlatformId}";
                    var fallbackImage = limitedDetail.ImagesData?.Logo2x ?? string.Empty;
                    var gamePieces = (await db.QueryAsync<GamePiece>("SELECT * FROM GamePieces WHERE releaseKey=? AND gamePieceTypeId=?", releaseKey, gamePieceTypeId).ConfigureAwait(false));
                    if (gamePieces?.Any() == true)
                    {
                        foreach (var gamePiece in gamePieces)
                        {
                            var originalImages = gamePiece.GetValueAsOriginalImages();
                            if (string.IsNullOrEmpty(originalImages?.VerticalCover) == false)
                            {
                                fallbackImage = originalImages.VerticalCover;
                                break;
                            }
                        }
                    }

                    var webCaches = await db.QueryAsync<WebCache>("SELECT * FROM WebCache WHERE releaseKey=?", releaseKey).ConfigureAwait(false);
                    foreach (var webCache in webCaches)
                    {
                        var webCacheResource = (await db.QueryAsync<WebCacheResource>("SELECT * FROM WebCacheResources WHERE webCacheId=? AND webCacheResourceTypeId=? LIMIT 1", webCache.Id, webCacheResourceTypeId).ConfigureAwait(false)).FirstOrDefault();
                        if (webCacheResource is not null)
                        {
                            var localCoverImage = Path.Combine(programDataDirectory, "GOG.com", "Galaxy", "webcache", webCache.UserId.ToString(CultureInfo.InvariantCulture), "gog", gogGame.PlatformId, webCacheResource.Filename);
                            if (File.Exists(localCoverImage))
                            {
                                localCoverImages.Add(localCoverImage);
                            }
                        }
                    }

                    var productId = limitedDetail.ProductId.ToString(CultureInfo.InvariantCulture);
                    var currentGame = gogGames.FirstOrDefault<GOGGame>(game => game.PlatformId == productId);
                    if (currentGame is not null)
                    {
                        currentGame.FallbackHeaderUrl = fallbackImage;
                        currentGame.PotentialLocalHeaders.Clear();
                        currentGame.PotentialLocalHeaders.AddRange(localCoverImages);
                    }
                }
                catch (Exception err)
                {
                    Logger.Error(err, $"Could not load {gogGame.PlatformId}");
                }
            }

            await db.CloseAsync();
            db = null;
        }

        // Check for games that are installed locally, but not added to GOG Galaxy.
        foreach (var gogGame in gogGames)
        {
            if (string.IsNullOrEmpty(gogGame.FallbackHeaderUrl))
            {
                // Every failure in here is a *cover* failure. None of it may skip the game: the
                // previous code used `continue`, which dropped the loop body that saves the game to
                // the database and processes its DLLs, so a missing or corrupt webcache.zip meant an
                // installed, perfectly valid game never appeared in the app at all.
                await TrySetCoverFromWebCacheAsync(gogGame).ConfigureAwait(false);
            }

            await gogGame.SaveToDatabaseAsync();

            if (gogGame.NeedsProcessing == true || forceNeedsProcessing == true)
            {
                gogGame.ProcessGame(forceNeedsProcessing: forceNeedsProcessing);
            }
        }

        // Delete games that are no longer loaded, they are likely uninstalled
        foreach (var cachedGame in cachedGames)
        {
            // Game is to be deleted.
            if (gogGames.Contains(cachedGame) == false)
            {
                await cachedGame.DeleteAsync();
            }
        }

        return new List<Game>(gogGames);
    }

    /// <summary>
    /// Reads webcache.zip out of a GOG install and points the game at its vertical cover.
    /// </summary>
    /// <remarks>
    /// Best effort by design. GOG writes this file while the game is being installed or patched, so a
    /// truncated archive, a missing entry or unparseable JSON are all routine. Every failure is
    /// logged and swallowed rather than allowed to escape, because an escaping exception here would
    /// abandon the whole GOG library instead of just this game's artwork.
    /// </remarks>
    async Task TrySetCoverFromWebCacheAsync(GOGGame gogGame)
    {
        var webcachePath = Path.Combine(gogGame.InstallPath, "webcache.zip");
        if (File.Exists(webcachePath) == false)
        {
            Logger.Error($"Unable to get covers through any normal methods for {gogGame.PlatformId}.");
            return;
        }

        try
        {
            using var zip = ZipFile.OpenRead(webcachePath);

            var resourcesEntry = zip.GetEntry("resources.json");
            if (resourcesEntry is null)
            {
                Logger.Error($"Unable to load resources.json for {gogGame.PlatformId}.");
                return;
            }

            using var resourcesStream = resourcesEntry.Open();

            var limitedDetailImages = await JsonSerializer
                .DeserializeAsync(resourcesStream, SourceGenerationContext.Default.ResourceImages)
                .ConfigureAwait(false);

            if (limitedDetailImages is null || string.IsNullOrEmpty(limitedDetailImages.Logo))
            {
                Logger.Error($"resources.json for {gogGame.PlatformId} had no logo.");
                return;
            }

            var url = $"https://images.gog.com/{limitedDetailImages.Logo}";

            gogGame.FallbackHeaderUrl = url.Replace("glx_logo", "glx_vertical_cover");
        }
        catch (Exception err) when (err is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            Logger.Error(err, $"Unable to read {webcachePath}. The game will be listed without cover art.");
        }
    }

    /// <summary>
    /// This file only exists if GOG Galaxy is installed.
    /// </summary>
    /// <returns>galaxy-2.0.db location, or empty string if not found.</returns>
    string GetStorageFileLocation()
    {
        var programDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var storageFileLocation = Path.Combine(programDataDirectory, "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");
        if (File.Exists(storageFileLocation))
        {
            return storageFileLocation;
        }

        return string.Empty;
    }

    public async Task LoadGamesFromCacheAsync()
    {
        try
        {
            GOGGame[] games;
            using (await Database.Instance.Mutex.LockAsync())
            {
                games = await Database.Instance.Connection.Table<GOGGame>().ToArrayAsync().ConfigureAwait(false);
            }
            foreach (var game in games)
            {
                if (game.IsInIgnoredPath())
                {
                    continue;
                }

                if (Directory.Exists(game.InstallPath) == false)
                {
                    Logger.Warning($"{Name} library could not load game {game.Title} ({game.PlatformId}) from cache because install path does not exist: {game.InstallPath}");
                    // We remove the list of known game assets, but not the game itself.
                    // Removing the game will remove its history, notes, and other data.
                    // We don't want to do this in case it is just a temporary issue.
                    await game.RemoveGameAssetsFromCacheAsync().ConfigureAwait(false);
                    continue;
                }

                await game.LoadGameAssetsFromCacheAsync().ConfigureAwait(false);
                GameManager.Instance.AddGame(game);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
    }
}
