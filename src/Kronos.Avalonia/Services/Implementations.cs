using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Kronos.Data;
using Kronos.Models;

namespace Kronos.Services;

public class DialogService : IDialogService
{
    public async Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            new Button { Content = "No", IsCancel = true },
                            new Button { Content = "Yes", IsDefault = true, Classes = { "Accent" } }
                        }
                    }
                }
            }
        };

        var result = await dialog.ShowDialog<bool>(Avalonia.Application.Current!.MainWindow!);
        return result;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Avalonia.Media.Brushes.Red },
                    new Button { Content = "OK", IsDefault = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };

        await dialog.ShowDialog(Avalonia.Application.Current!.MainWindow!);
    }

    public async Task ShowInformationAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new Button { Content = "OK", IsDefault = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };

        await dialog.ShowDialog(Avalonia.Application.Current!.MainWindow!);
    }
}

public class GameLibraryService : IGameLibraryService
{
    public async Task<List<GameInfo>> GetAllGamesAsync()
    {
        // On Linux, we only have manually added games
        // This would use the Database from the core Kronos library
        var games = new List<GameInfo>();

        try
        {
            // Initialize database
            Database.Instance.Init();

            using (await Database.Instance.Mutex.LockAsync())
            {
                var manuallyAdded = await Database.Instance.Connection.Table<ManuallyAddedGame>().ToListAsync();

                foreach (var game in manuallyAdded)
                {
                    if (!Directory.Exists(game.InstallPath)) continue;

                    var gameInfo = new GameInfo
                    {
                        Id = game.ID,
                        Title = game.Title,
                        InstallPath = game.InstallPath,
                        CoverImage = game.CoverImage,
                        IsHidden = game.IsHidden ?? false
                    };

                    // Load DLL versions from database
                    var assets = await Database.Instance.Connection.Table<GameAsset>()
                        .Where(a => a.Id == game.ID).ToListAsync();

                    foreach (var asset in assets)
                    {
                        SetDllInfo(gameInfo, asset);
                    }

                    games.Add(gameInfo);
                }
            }
        }
        catch (Exception ex)
        {
            Kronos.Logger.Error(ex, "Failed to load games");
        }

        return games;
    }

    private void SetDllInfo(GameInfo gameInfo, GameAsset asset)
    {
        var version = asset.DisplayName ?? "Unknown";
        var status = asset.LocalRecord != null ? DllStatus.Swapped : DllStatus.Original;

        switch (asset.AssetType)
        {
            case GameAssetType.DLSS:
                gameInfo.DlssVersion = version;
                gameInfo.DlssStatus = status;
                break;
            case GameAssetType.DLSS_G:
                gameInfo.DlssGVersion = version;
                gameInfo.DlssGStatus = status;
                break;
            case GameAssetType.DLSS_D:
                gameInfo.DlssDVersion = version;
                gameInfo.DlssDStatus = status;
                break;
            case GameAssetType.FSR_31_DX12:
                gameInfo.Fsr31Dx12Version = version;
                gameInfo.Fsr31Dx12Status = status;
                break;
            case GameAssetType.FSR_31_VK:
                gameInfo.Fsr31VkVersion = version;
                gameInfo.Fsr31VkStatus = status;
                break;
            case GameAssetType.XeSS:
                gameInfo.XessVersion = version;
                gameInfo.XessStatus = status;
                break;
            case GameAssetType.XeSS_FG:
                gameInfo.XessFgVersion = version;
                gameInfo.XessFgStatus = status;
                break;
            case GameAssetType.XeSS_DX11:
                gameInfo.XessDx11Version = version;
                gameInfo.XessDx11Status = status;
                break;
            case GameAssetType.XeLL:
                gameInfo.XellVersion = version;
                gameInfo.XellStatus = status;
                break;
        }
    }

    public async Task<GameInfo?> GetGameAsync(string gameId)
    {
        var games = await GetAllGamesAsync();
        return games.FirstOrDefault(g => g.Id == gameId);
    }

    public async Task AddGameAsync(string title, string installPath)
    {
        if (!Directory.Exists(installPath))
            throw new DirectoryNotFoundException($"Install path does not exist: {installPath}");

        var game = new ManuallyAddedGame
        {
            PlatformId = Path.GetFileName(installPath),
            Title = title,
            InstallPath = installPath,
            IsHidden = false
        };

        game.SetID();

        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.InsertAsync(game);
        }

        // Process the game to detect DLLs
        var baseGame = (Game)game;
        baseGame.ProcessGame();
    }

    public async Task RemoveGameAsync(string gameId)
    {
        using (await Database.Instance.Mutex.LockAsync())
        {
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM manually_added_game WHERE id = ?", gameId);
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_asset WHERE id = ?", gameId);
            await Database.Instance.Connection.ExecuteAsync("DELETE FROM game_history WHERE game_id = ?", gameId);
        }
    }
}

public class DllSwapService : IDllSwapService
{
    public async Task<SwapResult> SwapDllAsync(string gameId, DllType dllType)
    {
        try
        {
            var game = await GetGameFromId(gameId);
            if (game == null)
                return new SwapResult { Success = false, Message = "Game not found" };

            var assetType = MapDllType(dllType);
            var dllManager = DLLManager.Instance;

            // Get available DLL records for this type
            var records = dllManager.GetRecordsForType(assetType);
            if (records.Count == 0)
                return new SwapResult { Success = false, Message = $"No {dllType} versions available" };

            // For now, swap to latest
            var latestRecord = records.OrderByDescending(r => r.VersionNumber).First();
            var result = await game.UpdateDllAsync(latestRecord);

            return new SwapResult { Success = result.Success, Message = result.Message };
        }
        catch (Exception ex)
        {
            Kronos.Logger.Error(ex, "Swap failed");
            return new SwapResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<SwapResult> ResetDllAsync(string gameId, DllType dllType)
    {
        try
        {
            var game = await GetGameFromId(gameId);
            if (game == null)
                return new SwapResult { Success = false, Message = "Game not found" };

            var assetType = MapDllType(dllType);
            var result = await game.ResetDllAsync(assetType);

            return new SwapResult { Success = result.Success, Message = result.Message };
        }
        catch (Exception ex)
        {
            Kronos.Logger.Error(ex, "Reset failed");
            return new SwapResult { Success = false, Message = ex.Message };
        }
    }

    private Task<Game?> GetGameFromId(string gameId)
    {
        var game = GameManager.Instance.GetGame(gameId);
        return Task.FromResult(game);
    }

    private GameAssetType MapDllType(DllType type) => type switch
    {
        DllType.DLSS => GameAssetType.DLSS,
        DllType.DLSS_G => GameAssetType.DLSS_G,
        DllType.DLSS_D => GameAssetType.DLSS_D,
        DllType.FSR_31_DX12 => GameAssetType.FSR_31_DX12,
        DllType.FSR_31_VK => GameAssetType.FSR_31_VK,
        DllType.XeSS => GameAssetType.XeSS,
        DllType.XeSS_FG => GameAssetType.XeSS_FG,
        DllType.XeSS_DX11 => GameAssetType.XeSS_DX11,
        DllType.XeLL => GameAssetType.XeLL,
        _ => throw new ArgumentException($"Unknown DLL type: {type}")
    };
}

public class UpdateService : IUpdateService
{
    public async Task<UpdateInfo?> CheckForUpdatesAsync(IProgress<double>? progress = null)
    {
        return await Updater.CheckForUpdateAsync(
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");
    }

    public async Task<bool> ApplyUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
    {
        return await Updater.ApplyUpdateAsync(updateInfo, progress);
    }
}

public class SettingsService : ISettingsService
{
    private const string SettingsFileName = "settings.json";
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = Path.Combine(Storage.GetDynamicJsonFolder(), SettingsFileName);
    }

    public SettingsModel GetSettings()
    {
        if (!File.Exists(_settingsPath))
            return GetDefaultSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<SettingsModel>(json) ?? GetDefaultSettings();
        }
        catch
        {
            return GetDefaultSettings();
        }
    }

    public SettingsModel GetDefaultSettings() => new()
    {
        DownloadPath = Path.Combine(Storage.GetStorageFolder(), "downloads"),
        AllowUntrusted = false,
        CheckSignatures = true,
        AutoCheckUpdates = true,
        MaxConcurrentDownloads = 4,
        IgnoredPaths = new()
    };

    public async Task SaveSettingsAsync(SettingsModel settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsPath, json);
    }
}