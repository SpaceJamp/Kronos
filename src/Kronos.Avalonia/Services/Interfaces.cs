using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kronos.Models;

namespace Kronos.Services;

public interface IDialogService
{
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task ShowErrorAsync(string title, string message);
    Task ShowInformationAsync(string title, string message);
}

public interface IGameLibraryService
{
    Task<List<GameInfo>> GetAllGamesAsync();
    Task<GameInfo?> GetGameAsync(string gameId);
    Task AddGameAsync(string title, string installPath);
    Task RemoveGameAsync(string gameId);
}

public interface IDllSwapService
{
    Task<SwapResult> SwapDllAsync(string gameId, DllType dllType);
    Task<SwapResult> ResetDllAsync(string gameId, DllType dllType);
}

public interface IUpdateService
{
    Task<UpdateInfo?> CheckForUpdatesAsync(IProgress<double>? progress = null);
    Task<bool> ApplyUpdateAsync(UpdateInfo updateInfo, IProgress<double>? progress = null);
}

public interface ISettingsService
{
    SettingsModel GetSettings();
    SettingsModel GetDefaultSettings();
    Task SaveSettingsAsync(SettingsModel settings);
}