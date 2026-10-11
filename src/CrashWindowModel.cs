using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Kronos;

/// <summary>
/// Shows an unhandled exception to the user, with the log path and a way to open it.
/// </summary>
/// <remarks>
/// Modelled on FailToLaunchWindow, which is the same shape for the same reason: a standalone window, not a
/// dialog, because at the point this is shown there may be no page, no XamlRoot and no content to host a
/// ContentDialog. A ContentDialog needs a XamlRoot; the crash handler cannot assume one exists.
/// </remarks>
public partial class CrashWindowModel : ObservableObject
{
    public string ReportText { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string LogPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether the log path is worth showing. A portable build unpacked somewhere unwritable cannot create
    /// one, and a dead button labelled "open log folder" is worse than no button.
    /// </summary>
    public bool HasLogPath => string.IsNullOrWhiteSpace(LogPath) == false;

    public CrashWindowModelTranslationProperties TranslationProperties { get; } = new CrashWindowModelTranslationProperties();

    public CrashWindowModel(Exception exception)
    {
        // Resolved once and reused: the report text names the path, and the open-folder and copy-path
        // commands use the same one, so they cannot disagree about where the log is.
        LogPath = CrashReport.TryGetLogPathForDisplay(DateTime.Now) ?? string.Empty;
        ReportText = CrashReport.Build(exception, LogPath);
        Title = CrashReport.BuildTitle(exception);
    }

    [RelayCommand]
    void CopyText()
    {
        var package = new DataPackage();
        package.SetText(ReportText);
        Clipboard.SetContent(package);
    }

    [RelayCommand]
    void OpenLogFolder()
    {
        if (string.IsNullOrWhiteSpace(LogPath))
        {
            return;
        }

        try
        {
            // A directory opens as a folder; a file opens selected within it. Both are useful depending on
            // whether the log file for today exists yet.
            if (System.IO.Directory.Exists(LogPath))
            {
                FileSystemHelper.OpenFolderInExplorer(LogPath);
            }
            else
            {
                FileSystemHelper.OpenFolderInExplorerSelectFile(LogPath);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not open the log folder from the crash window.");
        }
    }

    [RelayCommand]
    void CopyLogPath()
    {
        if (string.IsNullOrWhiteSpace(LogPath))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(LogPath);
        Clipboard.SetContent(package);
    }
}