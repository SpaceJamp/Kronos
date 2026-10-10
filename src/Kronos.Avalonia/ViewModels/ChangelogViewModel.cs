using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Abstractions;

namespace Kronos.ViewModels;

public partial class ChangelogViewModel : ObservableObject
{
    public ObservableCollection<string> Features { get; } = new()
    {
        "Linux support: Native Avalonia GUI runs on Linux (x64/ARM64)",
        "Cross-platform core: Shared DLL management, database, storage, updater",
        "Platform abstraction layer: ISettings, IGameLibrary, IDialogService, IUpdateService, ICredentialStore, IThemeService",
        "Linux CLI: kronos update, kronos version, kronos self-update commands",
        "Avalonia GUI on Linux: Full featured GUI with game list, DLL management, settings, auto-updater",
        "GitHub Releases auto-updater: SHA256 verified, cross-platform (Windows portable zip, Linux tar.gz)",
        "Removed pre-built binaries/installers: Build from source policy"
    };

    public ObservableCollection<string> Fixes { get; } = new()
    {
        "Storage paths now use XDG Base Directory spec on Linux (~/.local/share/Kronos)",
        "WinTrust signature verification stubbed on Linux (logs warning, allows DLLs)",
        "Database and core DLL management now cross-platform",
        "Settings now use cross-platform ISettings abstraction"
    };

    public ObservableCollection<string> Improvements { get; } = new()
    {
        "Multi-targeting: net10.0 (Linux) + net10.0-windows10.0.26100.0 (Windows)",
        "Platform abstraction layer for settings, dialogs, updates, credentials, themes, game libraries",
        "Linux game library: Manual game addition (no Windows registry access)",
        "Avalonia 11.2 with Fluent theme for native look on Linux",
        "Self-contained builds: No .NET runtime required on target machine",
        "GitHub Actions workflow builds Windows + Linux x64/ARM64 artifacts"
    };

    public ObservableCollection<string> KnownIssues { get; } = new()
    {
        "Linux GUI: Game detection is manual only (no Steam/GOG/Epic auto-detection via registry)",
        "Linux GUI: DLL swapping for Proton/Wine games requires manual path configuration",
        "Cross-platform credential store: Linux uses encrypted files (DPAPI on Windows, libsecret not yet integrated)",
        "Theme detection on Linux: Basic GTK/GNOME/KDE detection, no live theme change events",
        "Some Windows-specific helpers (FileSystemHelper, SwapVersionAdvisor) not yet ported"
    };

    [RelayCommand]
    private void Close()
    {
        if (this.GetVisualRoot() is Window window)
        {
            window.Close();
        }
    }
}