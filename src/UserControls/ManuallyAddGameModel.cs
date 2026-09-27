using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DLSS_Swapper.Helpers;
using DLSS_Swapper.Data.ManuallyAdded;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DLSS_Swapper.UserControls;

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
    }

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
            return;
        }

        _game.PromptToBrowseCustomCover();
    }
}
