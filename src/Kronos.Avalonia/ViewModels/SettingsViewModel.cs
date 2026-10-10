using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Platform;
using Kronos.Views;

namespace Kronos.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings = Platform.Settings;

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _allowUntrusted = false;

    [ObservableProperty]
    private bool _checkSignatures = true;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private int _maxConcurrentDownloads = 4;

    [ObservableProperty]
    private ObservableCollection<string> _ignoredPaths = new();

    [ObservableProperty]
    private string _newIgnoredPath = string.Empty;

    public SettingsViewModel()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _settings.GetSettings();
        DownloadPath = settings.DownloadPath;
        AllowUntrusted = settings.AllowUntrusted;
        CheckSignatures = settings.CheckSignatures;
        AutoCheckUpdates = settings.AutoCheckUpdates;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        foreach (var path in settings.IgnoredPaths)
        {
            IgnoredPaths.Add(path);
        }
    }

    [RelayCommand]
    private async Task BrowseDownloadPathAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Download Folder"
        };
        var result = await dialog.ShowAsync(Avalonia.Application.Current!.MainWindow!);
        if (!string.IsNullOrEmpty(result))
        {
            DownloadPath = result;
        }
    }

    [RelayCommand]
    private void AddIgnoredPath()
    {
        if (string.IsNullOrWhiteSpace(NewIgnoredPath)) return;
        if (IgnoredPaths.Contains(NewIgnoredPath)) return;

        if (Directory.Exists(NewIgnoredPath))
        {
            IgnoredPaths.Add(NewIgnoredPath);
            NewIgnoredPath = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveIgnoredPath(string path)
    {
        IgnoredPaths.Remove(path);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new SettingsModel
        {
            DownloadPath = DownloadPath,
            AllowUntrusted = AllowUntrusted,
            CheckSignatures = CheckSignatures,
            AutoCheckUpdates = AutoCheckUpdates,
            MaxConcurrentDownloads = MaxConcurrentDownloads,
            IgnoredPaths = IgnoredPaths.ToList()
        };

        await _settings.SaveSettingsAsync(settings);
        await Platform.Dialogs.ShowInformationAsync("Settings Saved", "Settings have been saved successfully.");
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var confirmed = await Platform.Dialogs.ShowConfirmationAsync(
            "Reset Settings",
            "Reset all settings to defaults?");
        if (!confirmed) return;

        var defaults = _settings.GetDefaultSettings();
        DownloadPath = defaults.DownloadPath;
        AllowUntrusted = defaults.AllowUntrusted;
        CheckSignatures = defaults.CheckSignatures;
        AutoCheckUpdates = defaults.AutoCheckUpdates;
        MaxConcurrentDownloads = defaults.MaxConcurrentDownloads;
        IgnoredPaths.Clear();
    }

    [RelayCommand]
    private void ShowChangelog()
    {
        var window = new ChangelogWindow
        {
            DataContext = new ChangelogViewModel()
        };
        window.ShowDialog(Avalonia.Application.Current!.MainWindow!);
    }
}