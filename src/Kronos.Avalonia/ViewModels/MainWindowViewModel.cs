using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Platform;

namespace Kronos.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex = 0;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _progressText = string.Empty;

    public LibraryViewModel LibraryViewModel { get; }
    public UpdatesViewModel UpdatesViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        UpdatesViewModel updatesViewModel,
        SettingsViewModel settingsViewModel)
    {
        LibraryViewModel = libraryViewModel;
        UpdatesViewModel = updatesViewModel;
        SettingsViewModel = settingsViewModel;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = "Refreshing game list...";
        try
        {
            await LibraryViewModel.LoadGamesAsync();
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            await Platform.Dialogs.ShowErrorAsync("Refresh Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        StatusText = "Checking for updates...";
        ProgressValue = 0;
        ProgressText = "Checking...";

        try
        {
            var updateInfo = await Platform.Updates.CheckForUpdatesAsync(progress =>
            {
                ProgressValue = progress * 50;
                ProgressText = $"Downloading: {progress:P0}";
            });

            if (updateInfo != null)
            {
                var result = await Platform.Dialogs.ShowConfirmationAsync(
                    "Update Available",
                    $"Version {updateInfo.Version} is available ({updateInfo.FileSize:N0} bytes).\n\n{updateInfo.ReleaseNotes}\n\nDownload and apply now?");

                if (result)
                {
                    var success = await Platform.Updates.ApplyUpdateAsync(updateInfo, progress =>
                    {
                        ProgressValue = 50 + progress * 50;
                        ProgressText = $"Applying: {progress:P0}";
                    });

                    if (success)
                    {
                        await Platform.Dialogs.ShowInformationAsync("Update Applied",
                            "Update has been applied. Please restart Kronos.");
                    }
                    else
                    {
                        await Platform.Dialogs.ShowErrorAsync("Update Failed", "Failed to apply update. Check logs for details.");
                    }
                }
            }
            else
            {
                await Platform.Dialogs.ShowInformationAsync("No Updates", "You are running the latest version.");
            }
            StatusText = "Ready";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            await Platform.Dialogs.ShowErrorAsync("Update Check Failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = string.Empty;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedTabIndex = 2; // Settings tab
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        var dialog = new AddGameDialog
        {
            DataContext = new AddGameDialogViewModel(Platform.GameLibrary)
        };
        var result = await dialog.ShowDialog<bool>(this.GetVisualRoot() as Window);
        if (result == true)
        {
            await LibraryViewModel.LoadGamesAsync();
        }
    }
}