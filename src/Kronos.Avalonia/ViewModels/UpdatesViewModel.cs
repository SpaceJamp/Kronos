using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Abstractions;

namespace Kronos.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    [ObservableProperty]
    private string _currentVersion = "1.50";

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private bool _updateAvailable = false;

    [ObservableProperty]
    private bool _isChecking = false;

    [ObservableProperty]
    private bool _isDownloading = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    public UpdatesViewModel()
    {
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsChecking = true;
        try
        {
            var info = await Platform.Updates.CheckForUpdatesAsync();
            if (info != null)
            {
                LatestVersion = info.Version;
                ReleaseNotes = info.ReleaseNotes;
                UpdateAvailable = true;
            }
            else
            {
                UpdateAvailable = false;
                LatestVersion = CurrentVersion;
            }
        }
        catch (Exception ex)
        {
            await Platform.Dialogs.ShowErrorAsync("Update Check Failed", ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (!UpdateAvailable) return;

        IsDownloading = true;
        DownloadProgress = 0;
        try
        {
            var info = new UpdateInfo
            {
                Version = LatestVersion,
                ReleaseNotes = ReleaseNotes,
                FileSize = 0
            };

            var success = await Platform.Updates.ApplyUpdateAsync(info, progress =>
            {
                DownloadProgress = progress;
            });

            if (success)
            {
                await Platform.Dialogs.ShowInformationAsync("Update Applied",
                    "Update has been applied. Please restart Kronos.");
                UpdateAvailable = false;
                CurrentVersion = LatestVersion;
            }
            else
            {
                await Platform.Dialogs.ShowErrorAsync("Update Failed", "Failed to apply update.");
            }
        }
        catch (Exception ex)
        {
            await Platform.Dialogs.ShowErrorAsync("Update Failed", ex.Message);
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
        }
    }
}