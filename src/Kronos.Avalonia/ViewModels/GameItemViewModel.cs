using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Abstractions;

namespace Kronos.ViewModels;

public partial class GameItemViewModel : ObservableObject
{
    public string GameId { get; }
    public string Title { get; }
    public string InstallPath { get; }
    public bool IsHidden { get; }

    [ObservableProperty]
    private GameDllViewModel? _dlss;

    [ObservableProperty]
    private GameDllViewModel? _dlssG;

    [ObservableProperty]
    private GameDllViewModel? _dlssD;

    [ObservableProperty]
    private GameDllViewModel? _fsr31Dx12;

    [ObservableProperty]
    private GameDllViewModel? _fsr31Vk;

    [ObservableProperty]
    private GameDllViewModel? _xess;

    [ObservableProperty]
    private GameDllViewModel? _xessFg;

    [ObservableProperty]
    private GameDllViewModel? _xessDx11;

    [ObservableProperty]
    private GameDllViewModel? _xell;

    public GameItemViewModel(IGame game)
    {
        GameId = game.Id;
        Title = game.Title;
        InstallPath = game.InstallPath;
        IsHidden = game.IsHidden;

        UpdateDllViews(game);
    }

    private void UpdateDllViews(IGame game)
    {
        Dlss = CreateDllViewModel(DllType.DLSS, game.DlssVersion, game.DlssStatus);
        DlssG = CreateDllViewModel(DllType.DLSS_G, game.DlssGVersion, game.DlssGStatus);
        DlssD = CreateDllViewModel(DllType.DLSS_D, game.DlssDVersion, game.DlssDStatus);
        Fsr31Dx12 = CreateDllViewModel(DllType.FSR_31_DX12, game.Fsr31Dx12Version, game.Fsr31Dx12Status);
        Fsr31Vk = CreateDllViewModel(DllType.FSR_31_VK, game.Fsr31VkVersion, game.Fsr31VkStatus);
        Xess = CreateDllViewModel(DllType.XeSS, game.XessVersion, game.XessStatus);
        XessFg = CreateDllViewModel(DllType.XeSS_FG, game.XessFgVersion, game.XessFgStatus);
        XessDx11 = CreateDllViewModel(DllType.XeSS_DX11, game.XessDx11Version, game.XessDx11Status);
        Xell = CreateDllViewModel(DllType.XeLL, game.XellVersion, game.XellStatus);
    }

    private GameDllViewModel CreateDllViewModel(DllType type, string? currentVersion, DllStatus status)
    {
        if (currentVersion == null) return null;
        return new GameDllViewModel(type, currentVersion, status);
    }

    public async Task RefreshAsync()
    {
        var game = await Kronos.Abstractions.Platform.GameLibrary.GetGameAsync(GameId);
        if (game != null)
        {
            UpdateDllViews(game);
        }
    }
}

public partial class GameDllViewModel : ObservableObject
{
    public DllType Type { get; }
    public string CurrentVersion { get; }
    public DllStatus Status { get; }

    [ObservableProperty]
    private string _latestVersion = string.Empty;

    [ObservableProperty]
    private bool _canUpdate = false;

    [ObservableProperty]
    private bool _isUpdating = false;

    public string TypeDisplayName => Type switch
    {
        DllType.DLSS => "DLSS",
        DllType.DLSS_G => "DLSS-G (Frame Gen)",
        DllType.DLSS_D => "DLSS-D (DLAA)",
        DllType.FSR_31_DX12 => "FSR 3.1 (DX12)",
        DllType.FSR_31_VK => "FSR 3.1 (Vulkan)",
        DllType.XeSS => "XeSS",
        DllType.XeSS_FG => "XeSS Frame Gen",
        DllType.XeSS_DX11 => "XeSS (DX11)",
        DllType.XeLL => "XeLL",
        _ => Type.ToString()
    };

    public string StatusDisplay => Status switch
    {
        DllStatus.Original => "Original",
        DllStatus.Swapped => "Swapped",
        DllStatus.Unknown => "Unknown",
        DllStatus.Missing => "Missing",
        _ => Status.ToString()
    };

    public bool HasUpdate => CanUpdate && !string.IsNullOrEmpty(LatestVersion) &&
                             Version.Parse(LatestVersion) > Version.Parse(CurrentVersion);

    public GameDllViewModel(DllType type, string currentVersion, DllStatus status)
    {
        Type = type;
        CurrentVersion = currentVersion;
        Status = status;
    }

    [RelayCommand]
    private async Task SwapAsync()
    {
        if (IsUpdating || !CanUpdate) return;
        IsUpdating = true;
        try
        {
            // Note: This would need the parent game's GameId
            // For now, we'll just show a message
            await Kronos.Abstractions.Platform.Dialogs.ShowInformationAsync("Swap", $"Swap {TypeDisplayName} not fully implemented in cross-platform layer yet");
        }
        finally
        {
            IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        var confirmed = await Kronos.Abstractions.Platform.Dialogs.ShowConfirmationAsync(
            "Reset DLL",
            $"Reset {TypeDisplayName} to original version?");
        if (!confirmed) return;

        IsUpdating = true;
        try
        {
            await Kronos.Abstractions.Platform.Dialogs.ShowInformationAsync("Reset", $"Reset {TypeDisplayName} not fully implemented in cross-platform layer yet");
        }
        finally
        {
            IsUpdating = false;
        }
    }
}