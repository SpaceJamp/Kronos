using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Abstractions;

namespace Kronos.ViewModels;

public partial class AddGameDialogViewModel : ObservableObject
{
    private readonly IGameLibraryService _libraryService;

    [ObservableProperty]
    private string _gameTitle = string.Empty;

    [ObservableProperty]
    private string _installPath = string.Empty;

    public AddGameDialogViewModel(IGameLibraryService libraryService)
    {
        _libraryService = libraryService;
    }

    [RelayCommand]
    private async Task BrowsePathAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Game Install Folder"
        };
        var result = await dialog.ShowAsync(Avalonia.Application.Current!.MainWindow!);
        if (!string.IsNullOrEmpty(result))
        {
            InstallPath = result;
            if (string.IsNullOrWhiteSpace(GameTitle))
            {
                GameTitle = Path.GetFileName(result);
            }
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (string.IsNullOrWhiteSpace(GameTitle))
        {
            await ShowErrorAsync("Missing Title", "Please enter a game title.");
            return;
        }

        if (string.IsNullOrWhiteSpace(InstallPath) || !Directory.Exists(InstallPath))
        {
            await ShowErrorAsync("Invalid Path", "Please select a valid install folder.");
            return;
        }

        try
        {
            await _libraryService.AddGameAsync(GameTitle, InstallPath);
            Close(true);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Failed to Add Game", ex.Message);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Close(false);
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
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

    private void Close(bool result)
    {
        if (this.VisualRoot is Window window)
        {
            window.Close(result);
        }
    }
}