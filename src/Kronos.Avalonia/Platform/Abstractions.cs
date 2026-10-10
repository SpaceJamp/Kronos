using System;

namespace Kronos.Platform;

/// <summary>
/// Platform abstraction container. Provides platform-specific implementations
/// of dialogs, settings, game library, and update services.
/// </summary>
public static class Platform
{
    public static IDialogService Dialogs { get; private set; } = null!;
    public static ISettingsService Settings { get; private set; } = null!;
    public static IGameLibraryService GameLibrary { get; private set; } = null!;
    public static IUpdateService Updates { get; private set; } = null!;

    public static void Initialize(
        IDialogService dialogs,
        ISettingsService settings,
        IGameLibraryService gameLibrary,
        IUpdateService updates)
    {
        Dialogs = dialogs;
        Settings = settings;
        GameLibrary = gameLibrary;
        Updates = updates;
    }

    public static void Load()
    {
        // Backward compatibility - no-op
    }
}