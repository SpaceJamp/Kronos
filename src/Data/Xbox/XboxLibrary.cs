using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos.Data.Xbox;

internal class XboxLibrary : IGameLibrary
{
    public GameLibrary GameLibrary => GameLibrary.XboxApp;
    public string Name => "Xbox App";

    public Type GameType => typeof(XboxGame);

    static XboxLibrary? instance;
    public static XboxLibrary Instance => instance ??= new XboxLibrary();

    GameLibrarySettings? _gameLibrarySettings;
    public GameLibrarySettings? GameLibrarySettings => _gameLibrarySettings ??= GameManager.Instance.GetGameLibrarySettings(GameLibrary);

    private XboxLibrary()
    {

    }

    /// <summary>The Xbox app's own package family.</summary>
    const string XboxAppPackageFamily = "Microsoft.GamingApp_8wekyb3d8bbwe";

    public bool IsInstalled()
    {
        // Win32 rather than WinRT's PackageManager, which Uno does not implement: see AppxPackages.
        return AppxPackages.FindInstalledPackages(XboxAppPackageFamily).Count > 0;
    }

    /// <summary>What MicrosoftGame.config says about a game, beyond its icons.</summary>
    sealed record GameIdentity(string Publisher, string DisplayName);

    readonly string[] _defaultHiddenGames = [];

    public async Task<List<Game>> ListGamesAsync(bool forceNeedsProcessing = false)
    {
        var games = new List<Game>();

        if (IsInstalled() == false)
        {
            return games;
        }

        var cachedGames = GameManager.Instance.GetGames<XboxGame>();

        var gameNamesToFindPackages = new Dictionary<string, List<string>>();
        var gameIdentities = new Dictionary<string, GameIdentity>();

        var drives = DriveInfo.GetDrives();
        foreach (var drive in drives)
        {
            // Skip network drives and CDRom drives.
            if (drive.DriveType == DriveType.Unknown || drive.DriveType == DriveType.Network || drive.DriveType == DriveType.CDRom)
            {
                continue;
            }

            var gamingRootFile = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
            if (File.Exists(gamingRootFile))
            {
                var fileBytes = File.ReadAllBytes(gamingRootFile);
                var mystring = Encoding.Unicode.GetString(fileBytes);
                // Validate file header.
                //RGBX
                if (fileBytes.Length > 5 && fileBytes[0] == 'R' && fileBytes[1] == 'G' && fileBytes[2] == 'B' && fileBytes[3] == 'X')
                {
                    var stringBuilder = new StringBuilder();
                    stringBuilder.Append(drive.RootDirectory.FullName);
                    for (var i = 5; i < fileBytes.Length; i++)
                    {
                        // Ignore bytes that are 0
                        if (fileBytes[i] != 0)
                        {
                            stringBuilder.Append((char)fileBytes[i]);
                        }
                    }

                    var targetDir = stringBuilder.ToString();
                    if (Directory.Exists(targetDir))
                    {
                        var gameDirectories = Directory.GetDirectories(targetDir);
                        foreach (var gameDirectory in gameDirectories)
                        {
                            var configFile = Path.Combine(gameDirectory, "Content", "MicrosoftGame.config");
                            if (File.Exists(configFile))
                            {
                                // MicrosoftGame.config is written while a game is installing or patching, so a
                                // truncated one is routine. An unguarded Load threw XmlException out of
                                // ListGamesAsync and cost the user every Xbox game.
                                try
                                {
                                    var xmlDocument = new XmlDocument();
                                    xmlDocument.Load(configFile);

                                var gameNode = xmlDocument.DocumentElement?.SelectSingleNode("/Game");
                                if (gameNode is null)
                                {
                                    continue;
                                }
                                var configVersion = gameNode.Attributes?["configVersion"];
                                if (configVersion is null)
                                {
                                    continue;
                                }

                                // Skip if we potentially don't know about this config version.
                                if (configVersion.Value != "0" && configVersion.Value != "1")
                                {
                                    Logger.Error($"Unknown configVersion in {configFile}, {configVersion.Value}");
                                    continue;
                                }


                                var identityNode = gameNode.SelectSingleNode("Identity");
                                if (identityNode is null)
                                {
                                    continue;
                                }

                                var identityNodeName = identityNode.Attributes?["Name"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(identityNodeName) == true)
                                {
                                    continue;
                                }

                                var shellVisualsNode = gameNode.SelectSingleNode("ShellVisuals");
                                if (shellVisualsNode?.Attributes is null)
                                {
                                    continue;
                                }

                                // The publisher is what turns the package name into a family name, and the
                                // display name is plain text here, where the package manifest's is often an
                                // ms-resource reference.
                                var publisher = identityNode.Attributes?["Publisher"]?.Value ?? string.Empty;
                                var displayName = shellVisualsNode.Attributes?["DefaultDisplayName"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(publisher) == false &&
                                    (gameIdentities.TryGetValue(identityNodeName, out var knownIdentity) == false ||
                                     string.IsNullOrEmpty(knownIdentity.DisplayName)))
                                {
                                    gameIdentities[identityNodeName] = new GameIdentity(publisher, displayName);
                                }

                                var potentialIcons = new List<string>();

                                // These are added in order as these are the way we wish to use them.
                                var splashScreenImage = shellVisualsNode.Attributes?["SplashScreenImage"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(splashScreenImage) == false)
                                {
                                    potentialIcons.Add(splashScreenImage);
                                }

                                var square480x480Logo = shellVisualsNode.Attributes?["Square480x480Logo"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(square480x480Logo) == false)
                                {
                                    potentialIcons.Add(square480x480Logo);
                                }

                                var square150x150Logo = shellVisualsNode.Attributes?["Square150x150Logo"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(square150x150Logo) == false)
                                {
                                    potentialIcons.Add(square150x150Logo);
                                }

                                var storeLogo = shellVisualsNode.Attributes?["StoreLogo"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(storeLogo) == false)
                                {
                                    potentialIcons.Add(storeLogo);
                                }

                                var square44x44Logo = shellVisualsNode.Attributes?["Square44x44Logo"]?.Value ?? string.Empty;
                                if (string.IsNullOrEmpty(square44x44Logo) == false)
                                {
                                    potentialIcons.Add(square44x44Logo);
                                }

                                // Merge rather than replace. A title installed in two places - an
                                // internal drive and a game drive - is enumerated once per location, and
                                // the second MicrosoftGame.config can legitimately carry no image
                                // attributes. Assigning over the first result replaced a good icon list
                                // with an empty one and the game lost its cover.
                                if (gameNamesToFindPackages.TryGetValue(identityNodeName, out var existingIcons))
                                {
                                    foreach (var icon in potentialIcons)
                                    {
                                        if (existingIcons.Contains(icon) == false)
                                        {
                                            existingIcons.Add(icon);
                                        }
                                    }
                                }
                                else
                                {
                                    gameNamesToFindPackages[identityNodeName] = potentialIcons;
                                }
                            }
                            catch (Exception err) when (err is XmlException or IOException or UnauthorizedAccessException)
                            {
                                Logger.Error(err, $"Unable to parse {configFile}. Skipping this game directory.");
                            }
                        }
                    }
                    }
                }
                else
                {
                    Logger.Error($"Unable to parse data of .GamingRoot file - {BitConverter.ToString(fileBytes)}");
                }
            }
        }

        // Each game found on a gaming root is looked up by its own package family, through the Win32
        // packaging API. This used to list every package for the user through WinRT's PackageManager and
        // match names, which on Uno threw NotImplementedException before reaching a single game. Asking
        // for the family directly also only ever considers main packages installed for this user, which
        // is what the old IsFramework / IsResourcePackage filter was approximating.
        foreach (var (packageName, localCoverImages) in gameNamesToFindPackages)
        {
            if (gameIdentities.TryGetValue(packageName, out var identity) == false)
            {
                Logger.Warning($"{Name} library: {packageName} has no publisher in its MicrosoftGame.config, so its package cannot be looked up.");
                continue;
            }

            var familyName = AppxPackages.FamilyNameFromIdentity(packageName, identity.Publisher);
            if (string.IsNullOrEmpty(familyName))
            {
                continue;
            }

            var fullNames = AppxPackages.FindInstalledPackages(familyName);
            if (fullNames.Count == 0)
            {
                // On a gaming root but not installed for this user - another account's, or mid-uninstall.
                continue;
            }

            // Does the install path exist?
            var installedPath = AppxPackages.GetInstalledPath(fullNames[0]);
            if (string.IsNullOrEmpty(installedPath) || Directory.Exists(installedPath) == false)
            {
                continue;
            }

            // Does the product have app list entries?
            if (AppxPackages.HasAppListEntry(installedPath) == false)
            {
                continue;
            }

            try
            {
                var cachedGame = GameManager.Instance.GetGame<XboxGame>(familyName);
                var activeGame = cachedGame ?? new XboxGame(familyName);

                if (activeGame.IsHidden is null && _defaultHiddenGames.Contains(activeGame.PlatformId))
                {
                    activeGame.IsHidden = true;
                }

                activeGame.Title = string.IsNullOrEmpty(identity.DisplayName) ? packageName : identity.DisplayName;  // TODO: Will this be a problem if the game is already loaded
                activeGame.InstallPath = PathHelpers.NormalizePath(installedPath);

                if (activeGame.IsInIgnoredPath())
                {
                    continue;
                }

                if (Directory.Exists(activeGame.InstallPath) == false)
                {
                    Logger.Warning($"{Name} library could not load game {activeGame.Title} ({activeGame.PlatformId}) because install path does not exist: {activeGame.InstallPath}");
                    continue;
                }

                activeGame.LoadApplicationId();

                await activeGame.SetLocalHeaderImagesAsync(localCoverImages);
                //await game.UpdateCacheImageAsync();
                await activeGame.SaveToDatabaseAsync();

                // If the game is not from cache, force processing
                if (cachedGame is null)
                {
                    activeGame.NeedsProcessing = true;
                }

                if (activeGame.NeedsProcessing == true || forceNeedsProcessing == true)
                {
                    activeGame.ProcessGame(forceNeedsProcessing: forceNeedsProcessing);
                }
                games.Add(activeGame);
            }
            catch (Exception err)
            {
                Logger.Error(err);
            }
        }

        games.Sort();

        // Delete games that are no longer loaded, they are likely uninstalled
        foreach (var cachedGame in cachedGames)
        {
            // Game is to be deleted.
            if (games.Contains(cachedGame) == false)
            {
                await cachedGame.DeleteAsync();
            }
        }

        return games;
    }

    public async Task LoadGamesFromCacheAsync()
    {
        try
        {
            XboxGame[] games;
            using (await Database.Instance.Mutex.LockAsync())
            {
                games = await Database.Instance.Connection.Table<XboxGame>().ToArrayAsync().ConfigureAwait(false);
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
