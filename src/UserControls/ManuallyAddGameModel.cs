using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kronos.Helpers;
using Kronos.Data.ManuallyAdded;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kronos.UserControls;

internal partial class ManuallyAddGameModel : ObservableObject
{
    ManuallyAddedGame _game;
    public ManuallyAddedGame Game => _game;

    WeakReference<ManuallyAddGameControl> _manuallyAddGameControlWeakReference;

    public ManuallyAddGameModelTranslationProperties TranslationProperties { get; } = new ManuallyAddGameModelTranslationProperties();

    public ManuallyAddGameModel(ManuallyAddGameControl manuallyAddGameControl, string installPath) : base()
    {
        _manuallyAddGameControlWeakReference = new WeakReference<ManuallyAddGameControl>(manuallyAddGameControl);

        _game = new ManuallyAddedGame(Guid.NewGuid().ToString("D"))
        {
            Title = Path.GetFileName(installPath),
            InstallPath = PathHelpers.NormalizePath(installPath),
        };

        // Pre-tick the repack box when the folder carries markers of repack or emulation tooling.
        // This is only a suggestion the user can untick: detection cannot be reliable enough to
        // decide this on its own, and the stored flag is what the rest of the app acts on.
        var detection = RepackDetector.Detect(_game.InstallPath);
        if (detection.IsLikelyRepack)
        {
            _game.IsRepack = true;
        }

        RepackDetection = detection;

        _ = DetectStoreAppIdAsync();
    }

    /// <summary>
    /// Looks up the Steam appid for this folder and pre-fills it, so a repack gets a cover without
    /// the user having to know or type the id.
    /// </summary>
    /// <remarks>
    /// Deliberately best-effort and deliberately allowed to be wrong: a failure just leaves the box
    /// empty for the user to fill in, and a wrong guess is visible rather than silent because the
    /// cover preview next to the field shows whatever was matched. That is why the cover is fetched
    /// here, while the dialog is still open, instead of only after the game is added.
    /// </remarks>
    async Task DetectStoreAppIdAsync()
    {
        try
        {
            // NOTE: deliberately no ConfigureAwait(false) on either await. This starts on the UI
            // thread from the add game dialog, and the continuations below raise PropertyChanged
            // for the appid box and the cover preview. Letting them resume on the thread pool means
            // the bindings are updated off the UI thread, which WinUI does not support, and the
            // preview silently never appears. The lookups themselves still do their own internal
            // ConfigureAwait(false), so nothing is blocked waiting on the UI thread.
            var match = await SteamAppIdLookup.FindBestMatchAsync(_game.Title);
            if (match is null)
            {
                return;
            }

            // Don't stamp over something the user already typed or picked while we were searching.
            if (string.IsNullOrWhiteSpace(_game.LinkedAppId) == false)
            {
                return;
            }

            _game.SetLinkedAppId(match.Id.ToString(CultureInfo.InvariantCulture));

            LinkedAppIdWasDetected = true;
            OnPropertyChanged(nameof(LinkedAppIdWasDetected));
            OnPropertyChanged(nameof(Game));

            // Pull the artwork now so the dialog can preview it. The user can see a wrong match and
            // correct the id before committing, which is the whole safety net for auto-detection.
            await _game.ImportStoreCoverAsync();
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(HasCoverImage));
        }
        catch (Exception err)
        {
            // Never let a lookup problem stop the game being added.
            Logger.Error(err, "Failed to auto-detect a Steam app id for a manually added game.");
        }
    }

    /// <summary>
    /// True when the appid in the box was found by searching rather than typed, so the UI can say
    /// where it came from.
    /// </summary>
    public bool LinkedAppIdWasDetected { get; private set; }

    /// <summary>
    /// True when the preview has artwork, so the "add cover image" placeholder can be hidden.
    /// </summary>
    public bool HasCoverImage => string.IsNullOrWhiteSpace(_game.CoverImage) == false;

    RepackDetectionResult _repackDetection = RepackDetectionResult.NotARepack;

    /// <summary>
    /// What <see cref="RepackDetector"/> found in the chosen folder, so the UI can explain why the
    /// repack box was pre-ticked.
    /// </summary>
    public RepackDetectionResult RepackDetection
    {
        get => _repackDetection;
        private set => SetProperty(ref _repackDetection, value);
    }

    [RelayCommand]
    async Task AddCoverImageAsync()
    {
        if (_game.CoverImage == _game.ExpectedCustomCoverImage)
        {
            await _game.PromptToRemoveCustomCover();
        }
        else
        {
            _game.PromptToBrowseCustomCover();
        }

        // Picking or removing an image changes whether the placeholder should be showing.
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(HasCoverImage));
    }
}
