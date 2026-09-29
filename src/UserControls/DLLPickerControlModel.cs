using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Data;
using Kronos.Extensions;
using Kronos.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Kronos.UserControls;

public partial class DLLPickerControlModel : ObservableObject
{
    WeakReference<GameControl> _gameControlWeakReference;
    WeakReference<EasyContentDialog> _parentDialogWeakReference;

    /// <summary>
    /// Set once a warning has been shown for the current selection, cleared once the swap proceeds.
    /// </summary>
    /// <remarks>
    /// The warning is delivered through the picker's own InfoBar rather than a second
    /// <see cref="ContentDialog"/>, because the picker is itself a ContentDialog and WinUI allows only
    /// one at a time. Showing another from in here throws 0x80000019 and kills the process. An InfoBar
    /// needs no second dialog.
    ///
    /// That leaves the warning needing a second press of Swap to act on, which this flag tracks. It is
    /// cleared on the way through so a later, different selection warns again rather than inheriting
    /// consent for something the user was never shown.
    /// </remarks>
    bool _pendingWarningAccepted;
    WeakReference<DLLPickerControl> _dllPickerControlWeakReference;

    public Game Game { get; private set; }
    public GameAssetType GameAssetType { get; private set; }

    public List<DLLRecord> DLLRecords { get; private set; }

    [ObservableProperty]
    public partial DLLRecord? SelectedDLLRecord { get; set; } = null;

    [ObservableProperty]
    public partial bool CanSwap { get; set; } = false;

    [ObservableProperty]
    public partial bool AnyDLLsVisible { get; set; } = false;

    [ObservableProperty]
    public partial GameAsset? CurrentGameAsset { get; set; } = null;

    [ObservableProperty]
    public partial GameAsset? BackupGameAsset { get; set; } = null;

    public bool CanCloseParentDialog { get; set; }

    public DLLPickerControlModelTranslationProperties TranslationProperties { get; } = new DLLPickerControlModelTranslationProperties();

    public DLLPickerControlModel(GameControl gameControl, EasyContentDialog parentDialog, DLLPickerControl dllPickerControl, Game game, GameAssetType gameAssetType) : base()
    {
        _gameControlWeakReference = new WeakReference<GameControl>(gameControl);
        _parentDialogWeakReference = new WeakReference<EasyContentDialog>(parentDialog);
        _dllPickerControlWeakReference = new WeakReference<DLLPickerControl>(dllPickerControl);

        parentDialog.Closing += (ContentDialog sender, ContentDialogClosingEventArgs args) =>
        {
            if (args.Result == ContentDialogResult.Primary)
            {
                if (CanCloseParentDialog == false)
                {
                    args.Cancel = true;
                }
            }
        };
        Game = game;
        GameAssetType = gameAssetType;
        parentDialog.PrimaryButtonCommand = SwapDllCommand;
        parentDialog.SecondaryButtonCommand = ResetDllCommand;

        // NOTE: DLL type
        switch (GameAssetType)
        {
            case GameAssetType.DLSS:
                DLLRecords = [.. DLLManager.Instance.DLSSRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentDLSS?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.DLSS_G:
                DLLRecords = [.. DLLManager.Instance.DLSSGRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentDLSS_G?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.DLSS_D:
                DLLRecords = [.. DLLManager.Instance.DLSSDRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentDLSS_D?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.FSR_31_DX12:
                DLLRecords = [.. DLLManager.Instance.FSR31DX12Records];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentFSR_31_DX12?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.FSR_31_VK:
                DLLRecords = [.. DLLManager.Instance.FSR31VKRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentFSR_31_VK?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.XeSS:
                DLLRecords = [.. DLLManager.Instance.XeSSRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentXeSS?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.XeSS_FG:
                DLLRecords = [.. DLLManager.Instance.XeSSFGRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentXeSS_FG?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.XeSS_DX11:
                DLLRecords = [.. DLLManager.Instance.XeSSDX11Records];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentXeSS_DX11?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            case GameAssetType.XeLL:
                DLLRecords = [.. DLLManager.Instance.XeLLRecords];
                if (Settings.Instance.OnlyShowDownloadedDlls == true)
                {
                    _ = DLLRecords.RemoveAll(x => x.MD5Hash != Game.CurrentXeLL?.Hash && x.LocalRecord?.IsDownloaded is false);
                }
                break;

            default:
                DLLRecords = [];
                break;
        }

        if (Settings.Instance.AllowDebugDlls == false)
        {
            DLLRecords.RemoveAll(x => x.IsDevFile == true);
        }

        // Prevent DLSS 1.0 showing up with DLSS 2/3 and vice versa
        if (GameAssetType == GameAssetType.DLSS)
        {
            var dlssRecords = Game.GameAssets.Where(x => x.AssetType == GameAssetType.DLSS).ToList();
            if (dlssRecords.Count > 0)
            {
                if (dlssRecords[0].Version.StartsWith("1."))
                {
                    DLLRecords.RemoveAll(x => x.Version.StartsWith("1.") == false);
                }
                else
                {
                    DLLRecords.RemoveAll(x => x.Version.StartsWith("1.") == true);
                }
            }
        }

        AnyDLLsVisible = DLLRecords.Count > 0;

        ResetSelection();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(SelectedDLLRecord))
        {
            if (SelectedDLLRecord is null)
            {
                CanSwap = false;
            }
            else if (SelectedDLLRecord.LocalRecord is null)
            {
                // This should never happen
                CanSwap = false;
            }
            else
            {
                CanSwap = true;
            }
        }
        else if (e.PropertyName == nameof(CanSwap))
        {
            if (_parentDialogWeakReference.TryGetTarget(out var dialog))
            {
                dialog.IsPrimaryButtonEnabled = CanSwap;
            }
        }
    }

    [RelayCommand]
    async Task SwapDllAsync()
    {
        if (SelectedDLLRecord?.LocalRecord is null)
        {
            return;
        }

        if (SelectedDLLRecord.LocalRecord.FileDownloader is not null)
        {
            ShowTempInfoBar(string.Empty, ResourceHelper.GetString("GamePage_DllPicker_WaitToDownloadBeforeSwapping"));
            return;
        }
        else if (SelectedDLLRecord.LocalRecord.IsDownloaded == false)
        {
            ShowTempInfoBar(string.Empty, ResourceHelper.GetString("GamePage_DllPicker_StartingDownload"));
            SelectedDLLRecord.DownloadAsync().SafeFireAndForget();
            return;
        }

        // Warnings are shown in the picker's own InfoBar, never in a second ContentDialog.
        //
        // This crashed the app with COMException 0x80000019, "Only a single ContentDialog can be open
        // at any time", from inside this method. The DLL picker *is* a ContentDialog: GameControlModel
        // builds one, hands it to DLLPickerControl, and awaits its ShowAsync, so SwapDllAsync runs
        // while that dialog is still open. WinUI permits exactly one, so any ShowAsync from in here
        // throws and, being unhandled, takes the process down.
        //
        // The pre-existing repack warning had the same latent bug and would have crashed the same way
        // on a repack swap. It simply was not hit, because repack swaps are rare. The downgrade warning
        // added for 1.46 hit it on the first use, because swapping an older Call of Duty runtime is
        // exactly the case that triggers a downgrade.
        //
        // The InfoBar is part of the picker's own content, so showing a message in it needs no second
        // dialog and cannot collide with anything.
        var warningText = SwapVersionAdvisor.ComposeWarning(
            Game.IsRepack,
            BuildDowngradeWarning(SelectedDLLRecord));

        if (ShouldShowWarningAndStop(warningText is not null, ref _pendingWarningAccepted))
        {
            // Show it and stop. The second press of Swap proceeds, so the warning cannot be missed and
            // cannot be dismissed by accident.
            ShowTempInfoBar(
                ResourceHelper.GetString("General_Warning"),
                warningText!,
                duration: 30.0,
                severity: InfoBarSeverity.Warning);

            return;
        }

        var didUpdate = await Game.UpdateDllAsync(SelectedDLLRecord);

        if (didUpdate.Success == false)
        {
            ShowTempInfoBar(ResourceHelper.GetString("General_Error"), didUpdate.Message, severity: InfoBarSeverity.Error);
            return;
        }

        // Allow the dialog to close
        CanCloseParentDialog = true;

        if (_parentDialogWeakReference.TryGetTarget(out var dialog) == true)
        {
            // Is the dialog already closing when we call this?
            dialog.Hide();
        }
    }

    void ShowTempInfoBar(string title, string message, double duration = 5.0, InfoBarSeverity severity = InfoBarSeverity.Informational, int gridIndex = 2)
    {
        if (_dllPickerControlWeakReference.TryGetTarget(out var dllPickerControl) == true)
        {
            if (dllPickerControl.Content is Grid grid)
            {
                var infoBar = new InfoBar();
                infoBar.Message = message;
                infoBar.Severity = severity;
                infoBar.IsOpen = true;
                infoBar.IsClosable = true;

                // Temp workaround until InfoBar has a solid color by default.
                // https://github.com/microsoft/microsoft-ui-xaml/issues/5741
                if (App.Current.Resources.TryGetValue("InfoBarInformationalSeverityBackgroundBrush", out var infoBarInformationalSeverityBackground) && infoBarInformationalSeverityBackground is SolidColorBrush infoBarInformationalSeverityBackgroundBrush)
                {
                    // Temp fix to make download indicator visibile in dark mode.
                    if (WindowManager.CurrentTheme == ElementTheme.Dark)
                    {
                        infoBar.Background = new SolidColorBrush(Color.FromArgb(255, 23, 23, 23));
                    }
                    else
                    {
                        infoBarInformationalSeverityBackgroundBrush.Color = Color.FromArgb(255, infoBarInformationalSeverityBackgroundBrush.Color.R, infoBarInformationalSeverityBackgroundBrush.Color.G, infoBarInformationalSeverityBackgroundBrush.Color.B);
                        infoBar.Background = infoBarInformationalSeverityBackgroundBrush;
                    }
                }

                Grid.SetRow(infoBar, gridIndex);
                grid.Children.Add(infoBar);

                var dispatcherTimer = new DispatcherTimer();
                dispatcherTimer.Tick += (object? sender, object e) =>
                {
                    // If the page has gone away, parent should be null and this should not cause problems
                    if (infoBar?.Parent is Grid parentGrid)
                    {
                        infoBar.IsOpen = false;
                        parentGrid.Children.Remove(infoBar);
                    }

                    if (sender is DispatcherTimer timer)
                    {
                        timer.Stop();
                    }
                };
                dispatcherTimer.Interval = TimeSpan.FromSeconds(duration);
                dispatcherTimer.Start();
            }
        }

    }

    /// <summary>
    /// Whether a warning is waiting for a second press of Swap to be accepted.
    /// </summary>
    /// <remarks>
    /// Exposed so the "warn, then proceed on the second press" rule can be tested without a UI, which
    /// is the part that actually matters: the crash came from this path, and the sequencing is easy to
    /// get subtly wrong.
    /// </remarks>
    internal bool IsWaitingForWarningAcceptance => _pendingWarningAccepted;

    /// <summary>
    /// Decides whether a swap should stop and show a warning, given the current state.
    /// </summary>
    /// <remarks>
    /// <paramref name="hasWarning"/> is false when there is nothing to warn about, in which case the
    /// swap proceeds and the pending state is cleared so it cannot leak into a later swap.
    /// </remarks>
    internal static bool ShouldShowWarningAndStop(bool hasWarning, ref bool warningAccepted)
    {
        if (hasWarning == false)
        {
            warningAccepted = false;

            return false;
        }

        if (warningAccepted)
        {
            warningAccepted = false;

            return false;
        }

        warningAccepted = true;

        return true;
    }

    /// <summary>
    /// Builds the "this is an older runtime" warning for a candidate dll, or null when there is
    /// nothing to say.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="SwapDllAsync"/> so the logic is one call away from a test. It needs no
    /// UI, so it can be exercised directly; presenting the result cannot be.
    /// </remarks>
    internal string? BuildDowngradeWarning(DLLRecord dllRecord)
    {
        if (dllRecord is null || dllRecord.LocalRecord is null)
        {
            return null;
        }

        // The version of the file on disk right now, not what the game shipped with. After a swap
        // these differ, and the interesting comparison is against what is actually running, because
        // that is what will change.
        var installedVersions = SwapVersionAdvisor.GetInstalledVersions(
            Game.GameAssets, dllRecord.AssetType);

        if (installedVersions.Count == 0)
        {
            return null;
        }

        var incomingVersion = dllRecord.DisplayVersion;
        var incoming = VersionExtensions.TryParseVersionNumber(incomingVersion);
        if (incoming is null)
        {
            return null;
        }

        // Compare against the newest installed version, because that is the one that would regress.
        // A game can hold several copies of the same dll, one per graphics API or per install
        // directory, and if any of them is newer than what is being swapped in then something the
        // user relies on is about to get worse. Warning for the oldest instead would be the more
        // conservative choice, but it would also warn when the swap is a straight improvement for
        // every file that is actually newer, which is noise.
        var newestInstalled = installedVersions
            .Select(VersionExtensions.TryParseVersionNumber)
            .Where(x => x is not null)
            .Max(x => x!.Value);

        // Newest installed first, so a positive comparison means the game has something newer than
        // what is being swapped in. See SwapVersionAdvisor.Classify for why the direction matters.
        var advice = SwapVersionAdvisor.Classify(newestInstalled.CompareTo(incoming.Value));

        return SwapVersionAdvisor.BuildWarning(advice, installedVersions, incomingVersion);
    }

    [RelayCommand]
    void OpenDllPath()
    {
        if (CurrentGameAsset is null)
        {
            return;
        }

        try
        {
            if (File.Exists(CurrentGameAsset.Path))
            {
                Process.Start("explorer.exe", $"/select,{CurrentGameAsset.Path}");
            }
            else
            {
                var dllPath = Path.GetDirectoryName(CurrentGameAsset.Path) ?? string.Empty;
                if (Directory.Exists(dllPath))
                {
                    Process.Start("explorer.exe", dllPath);
                }
                else
                {
                    throw new Exception(ResourceHelper.GetFormattedResourceTemplate("GamePage_DllPicker_CouldNotFindFileTemplate", CurrentGameAsset.Path));
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
            ShowTempInfoBar(ResourceHelper.GetString("General_Error"), err.Message, severity: InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    async Task ResetDllAsync()
    {
        var didReset = await Game.ResetDllAsync(GameAssetType);

        if (didReset.Success == true)
        {
            if (_parentDialogWeakReference.TryGetTarget(out var parentDialog) == true)
            {
                parentDialog.Hide();
            }
        }
        else
        {
            ShowTempInfoBar(ResourceHelper.GetString("General_Error"), didReset.Message, severity: InfoBarSeverity.Error, gridIndex: 0);
        }
    }

    void ResetSelection()
    {
        // If there are backup records it means we can reset.
        var backupRecordType = DLLManager.Instance.GetAssetBackupType(GameAssetType);
        var existingBackupRecords = Game.GameAssets.Where(x => x.AssetType == backupRecordType).ToList();
        BackupGameAsset = existingBackupRecords.FirstOrDefault();

        // Select the default record
        var existingRecords = Game.GameAssets.Where(x => x.AssetType == GameAssetType).ToList();
        CurrentGameAsset = existingRecords.FirstOrDefault();

        if (CurrentGameAsset is not null)
        {
            SelectedDLLRecord = DLLRecords.FirstOrDefault(x => x.MD5Hash == CurrentGameAsset.Hash);
        }
    }
}
