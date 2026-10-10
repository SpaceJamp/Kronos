using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Abstractions;

namespace Kronos.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<GameItemViewModel> _games = new();

    [ObservableProperty]
    private GameItemViewModel? _selectedGame;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showHidden = false;

    [ObservableProperty]
    private int _visibleGameCount = 0;

    public LibraryViewModel()
    {
    }

    public async Task LoadGamesAsync()
    {
        try
        {
            var games = await Platform.GameLibrary.GetAllGamesAsync();
            Games.Clear();
            foreach (var game in games.OrderBy(g => g.Title))
            {
                Games.Add(new GameItemViewModel(game));
            }
            UpdateVisibleCount();
        }
        catch (Exception ex)
        {
            await Platform.Dialogs.ShowErrorAsync("Failed to Load Games", ex.Message);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        UpdateVisibleCount();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        UpdateVisibleCount();
    }

    private void UpdateVisibleCount()
    {
        var filtered = Games.Where(g =>
            (ShowHidden || !g.IsHidden) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             g.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        VisibleGameCount = filtered.Count;
    }

    [RelayCommand]
    private async Task SwapDllAsync(GameItemViewModel game, DllType dllType)
    {
        if (game == null) return;

        var result = await Platform.GameLibrary.SwapDllAsync(game.GameId, dllType);
        if (result.Success)
        {
            await game.RefreshAsync();
        }
        else
        {
            await Platform.Dialogs.ShowErrorAsync("Swap Failed", result.Message);
        }
    }

    [RelayCommand]
    private async Task ResetDllAsync(GameItemViewModel game, DllType dllType)
    {
        if (game == null) return;

        var confirmed = await Platform.Dialogs.ShowConfirmationAsync(
            "Reset DLL",
            $"Reset {dllType} to original version for {game.Title}?");
        if (!confirmed) return;

        var result = await Platform.GameLibrary.ResetDllAsync(game.GameId, dllType);
        if (result.Success)
        {
            await game.RefreshAsync();
        }
        else
        {
            await Platform.Dialogs.ShowErrorAsync("Reset Failed", result.Message);
        }
    }

    [RelayCommand]
    private async Task OpenGameFolderAsync(GameItemViewModel game)
    {
        if (game == null) return;
        try
        {
            var path = game.InstallPath;
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }
        catch (Exception ex)
        {
            await Platform.Dialogs.ShowErrorAsync("Failed to Open Folder", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveGameAsync(GameItemViewModel game)
    {
        if (game == null) return;

        var confirmed = await Platform.Dialogs.ShowConfirmationAsync(
            "Remove Game",
            $"Remove {game.Title} from Kronos? This will not delete the game files.");
        if (!confirmed) return;

        await Platform.GameLibrary.RemoveGameAsync(game.GameId);
        Games.Remove(game);
        UpdateVisibleCount();
    }
}