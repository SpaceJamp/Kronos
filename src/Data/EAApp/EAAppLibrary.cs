using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kronos.Helpers;
using Kronos.Interfaces;
using Microsoft.Win32;

namespace Kronos.Data.EAApp;

internal class EAAppLibrary : IGameLibrary
{
    static EAAppLibrary? instance;
    public static EAAppLibrary Instance => instance ??= new EAAppLibrary();

    public GameLibrary GameLibrary => GameLibrary.EAApp;

    GameLibrarySettings? _gameLibrarySettings;
    public GameLibrarySettings? GameLibrarySettings => _gameLibrarySettings ??= GameManager.Instance.GetGameLibrarySettings(GameLibrary);

    public string Name => "EA App";

    public Type GameType => typeof(EAAppGame);

    readonly FrozenSet<GameSearchResult> _gameSearchResults = [];

    private EAAppLibrary()
    {
        // The best way to get covers from EA apps is a static list from the EAAppGameListBuilder tool.
        try
        {
            // Resolved against the assembly's own directory rather than the process working directory.
            // A relative path is resolved against whatever the user happened to launch from, so a
            // shortcut with a different "Start in" - or a portable build run from elsewhere - silently
            // failed to find the file and every EA App game lost its cover with no way to tell that
            // apart from "no match in the list". Path.Combine also keeps this correct off Windows,
            // where the old hardcoded backslash was not a separator.
            var eaAppTitlesJsonPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ea_app_titles.json");
            if (File.Exists(eaAppTitlesJsonPath) == true)
            {
                using (var fileStream = File.OpenRead(eaAppTitlesJsonPath))
                {
                    var gameSearchResults = JsonSerializer.Deserialize(fileStream, SourceGenerationContext.Default.ListGameSearchResult);
                    if (gameSearchResults is null || gameSearchResults.Count == 0)
                    {
                        throw new Exception($"{eaAppTitlesJsonPath} is empty or invalid.");
                    }
                    _gameSearchResults = gameSearchResults.ToFrozenSet();
                }
            }
            else
            {
                throw new Exception($"{eaAppTitlesJsonPath} not found.");
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Unable to load ea_app_titles.json.");
        }
    }

    public bool IsInstalled()
    {
        // Guarded because IsInstalled is called at the top of ListGamesAsync, so an escaping
        // PlatformNotSupportedException off Windows (or a SecurityException on a locked-down machine)
        // would cost the user every EA App game instead of just reporting the launcher as absent.
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var eaDesktopKey = hklm.OpenSubKey(@"SOFTWARE\Electronic Arts\EA Desktop");

            if (eaDesktopKey is null)
            {
                return false;
            }

            var installPath = eaDesktopKey.GetValue("InstallLocation")?.ToString();
            if (string.IsNullOrWhiteSpace(installPath))
            {
                return false;
            }

            return Directory.Exists(installPath);
        }
        catch (Exception err) when (err is PlatformNotSupportedException or SecurityException or UnauthorizedAccessException)
        {
            Logger.Error(err, "Unable to read the EA Desktop registry key. Assuming EA App is not installed.");
            return false;
        }
    }

    public async Task<List<Game>> ListGamesAsync(bool forceNeedsProcessing)
    {
        if (IsInstalled() == false)
        {
            return new List<Game>();
        }

        var games = new List<Game>();
        var cachedGames = GameManager.Instance.GetGames<EAAppGame>();

        // I had no idea how to discover install EA App games until I had come across Flow.Launcher.Plugin.GamesLauncher
        // repo by KrystianLesniak.
        // https://github.com/KrystianLesniak/Flow.Launcher.Plugin.GamesLauncher
        // It works by looking at all installed applications and looking at the ones that have "EAInstaller" and "Cleanup.exe"
        // in the install path. Below is heavily based off their implementation.


        var registryHives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        var registryViews = new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 };

        var uninstallRootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
        var gameListLock = new Lock();

        await Parallel.ForEachAsync(registryHives, async (hive, ct) =>
        {
            await Parallel.ForEachAsync(registryViews, async (view, ct) =>
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                {
                    using (var uninstallSubKey = baseKey.OpenSubKey(uninstallRootKey))
                    {
                        // Check if the uninstall sub key exists
                        if (uninstallSubKey is null)
                        {
                            return;
                        }

                        foreach (var programUninstallSubKeyName in uninstallSubKey.GetSubKeyNames())
                        {
                            var fullSubKey = $"{uninstallRootKey}{programUninstallSubKeyName}";
                            try
                            {
                                using (var programUninstallSubKey = baseKey.OpenSubKey(fullSubKey))
                                {
                                    if (programUninstallSubKey is null)
                                    {
                                        // Could not open program uninstall sub key
                                        continue;
                                    }

                                    var uninstallString = programUninstallSubKey.GetValue("UninstallString")?.ToString() ?? string.Empty;
                                    if (string.IsNullOrWhiteSpace(uninstallString))
                                    {
                                        // No uninstall string found
                                        continue;
                                    }

                                    if (uninstallString.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) == false ||
                                        uninstallString.Contains("Cleanup.exe", StringComparison.OrdinalIgnoreCase) == false)
                                    {
                                        continue;
                                    }


                                    var name = programUninstallSubKey.GetValue("DisplayName")?.ToString() ?? string.Empty;

                                    // Read every registry value before handing anything to another
                                    // thread. programUninstallSubKey is scoped to this loop body's
                                    // `using`, so the DisplayIcon read further down - which happens
                                    // inside a RunOnUIThreadAsync callback - only worked because the
                                    // await happened to complete before the key was disposed. Remove
                                    // that await and it becomes ObjectDisposedException.
                                    var rawInstallPath = programUninstallSubKey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
                                    var displayIconPath = programUninstallSubKey.GetValue("DisplayIcon")?.ToString()?.Trim('"') ?? string.Empty;

                                    if (string.IsNullOrWhiteSpace(rawInstallPath))
                                    {
                                        Logger.Error($"Install path was empty for {name} in key {fullSubKey}");
                                        continue;
                                    }

                                    // Normalised like every other library does. The EA registry value
                                    // carries a trailing separator, and Game.IsInIgnoredPath and
                                    // GameManager.CheckIfGameIsAdded both compare paths for exact
                                    // equality, so an untrimmed path made this the only library whose
                                    // games could not be matched against an ignore list or a
                                    // not-already-added check.
                                    var installPath = PathHelpers.NormalizePath(rawInstallPath);

                                    var installerDataPath = Path.Combine(installPath, "__Installer", "installerdata.xml");

                                    string contentId = string.Empty;
                                    if (File.Exists(installerDataPath))
                                    {
                                        try
                                        {
                                            var doc = XDocument.Load(installerDataPath);
                                            contentId = doc.Descendants("contentID").FirstOrDefault()?.Value ?? string.Empty;
                                        }
                                        catch (Exception err) when (err is System.Xml.XmlException or IOException)
                                        {
                                            // installerdata.xml is written by the EA installer and is
                                            // routinely truncated mid-update. One bad file should cost
                                            // this game's cover, not the whole scan.
                                            Logger.Error(err, $"Unable to read {installerDataPath} for {name}.");
                                        }
                                    }

                                    if (string.IsNullOrWhiteSpace(contentId))
                                    {
                                        Logger.Error($"contentID was empty for {name} in key {fullSubKey}, from installer data {installerDataPath}");
                                        continue;
                                    }

                                    var cachedGame = GameManager.Instance.GetGame<EAAppGame>(contentId);
                                    var activeGame = cachedGame ?? new EAAppGame(contentId);

                                    // These are UI-bound properties. Parallel.ForEachAsync above always schedules its
                                    // work on the thread pool, never the UI thread, so these must be marshalled
                                    // explicitly or WinUI throws RPC_E_WRONGTHREAD when a bound control is updated
                                    // from a background thread. Only the assignments are marshalled - the
                                    // registry value was read above, before this callback runs on another
                                    // thread.
                                    await App.CurrentApp.RunOnUIThreadAsync(() =>
                                    {
                                        activeGame.Title = name;
                                        activeGame.InstallPath = installPath;
                                        activeGame.DisplayIconPath = displayIconPath;

                                        return Task.CompletedTask;
                                    }).ConfigureAwait(false);

                                    if (activeGame.IsInIgnoredPath())
                                    {
                                        continue;
                                    }

                                    await activeGame.SaveToDatabaseAsync().ConfigureAwait(false);

                                    if (cachedGame is null)
                                    {
                                        activeGame.NeedsProcessing = true;
                                    }

                                    if (activeGame.NeedsProcessing == true || forceNeedsProcessing == true)
                                    {
                                        activeGame.ProcessGame(forceNeedsProcessing: forceNeedsProcessing);
                                    }

                                    lock (gameListLock)
                                    {
                                        games.Add(activeGame);
                                    }
                                }
                            }
                            catch (Exception err)
                            {
                                Logger.Error(err, $"Could not prcoess key {fullSubKey}.");
                            }
                        }
                    }
                }
            });
        });

        games.Sort();

        // Delete games that are no longer loaded, they are likely uninstalled
        foreach (var cachedGame in cachedGames)
        {
            // Game is to be deleted.
            if (games.Contains(cachedGame) == false)
            {
                await cachedGame.DeleteAsync().ConfigureAwait(false);
            }
        }

        return games;
    }


    public async Task LoadGamesFromCacheAsync()
    {
        try
        {
            EAAppGame[] games;
            using (await Database.Instance.Mutex.LockAsync())
            {
                games = await Database.Instance.Connection.Table<EAAppGame>().ToArrayAsync().ConfigureAwait(false);
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

    internal string SearchForCover(Game game)
    {
        // Use ExtractOne with a selector to match by the Name property
        var search = new GameSearchResult()
        {
            Title = game.Title,
        };
        var bestMatch = FuzzySharp.Process.ExtractOne(search, _gameSearchResults, g => g.Title);

        if (bestMatch is null || bestMatch.Score < 60)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(bestMatch.Value.PackArtImage?.Path) == false)
        {
            return bestMatch.Value.PackArtImage.Path;
        }

        if (string.IsNullOrWhiteSpace(bestMatch.Value.KeyArtImage?.Path) == false)
        {
            return bestMatch.Value.KeyArtImage.Path;
        }

        if (string.IsNullOrWhiteSpace(bestMatch.Value.LogoImage?.Path) == false)
        {
            return bestMatch.Value.LogoImage.Path;
        }

        return string.Empty;
    }
}
