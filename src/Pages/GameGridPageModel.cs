using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Builders;
using Kronos.Data;
using Kronos.Helpers;
using CommunityToolkit.Mvvm.Messaging;
using Kronos.Messages;
using Kronos.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Windows.System;
namespace Kronos.Pages;

public enum GameGridViewType
{
    GridView,
    ListView,
}

public partial class GameGridPageModel : ObservableObject
{
    GameGridPage gameGridPage;

    /// <summary>
    /// Mass update selection and flow, kept separate from the filtering and view logic below.
    /// </summary>
    /// <remarks>
    /// The games page already owns search, grouping, the grid and list toggle and per game actions.
    /// Folding a three stage mass update into it made both harder to follow, and the XAML for it
    /// would have been interleaved with the existing item templates.
    /// </remarks>
    public MassUpgradeViewModel MassUpgrade { get; }

    /// <summary>
    /// The XamlRoot dialogs are shown against, taken from the page.
    /// </summary>
    /// <remarks>
    /// Exposed for MassUpgradeViewModel, which cannot reach the page directly. The page's own XamlRoot
    /// is used rather than a window wide one so the dialogs are themed and positioned consistently
    /// with every other dialog on this page.
    /// </remarks>
    public XamlRoot? XamlRoot => gameGridPage?.XamlRoot;

    [ObservableProperty]
    public partial Game? SelectedGame { get; set; } = null;

    [ObservableProperty]
    // CanRefresh is listed as well as IsLoading because the Refresh button binds to it, and it depends
    // on IsLoading. Without this the button is evaluated once - disabled, since both flags start true -
    // and never re-enabled: the initial load clears the flags, IsLoading fires, CanRefresh does not, and
    // the button stays dead for the rest of the session. Introduced in 1.58.1 when the binding was
    // changed from IsLoading to CanRefresh, and it is the reason this class follows MassUpgradeViewModel's
    // pattern of notifying every dependent property rather than only the immediate one.
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    public partial bool IsGameListLoading { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    public partial bool IsDLSSLoading { get; set; } = true;

    public bool IsLoading => (IsGameListLoading || IsDLSSLoading);

    [ObservableProperty]
    public partial ICollectionView? CurrentCollectionView { get; set; } = null;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridViewItemHeight))]
    public partial int GridViewItemWidth { get; set; } = Settings.Instance.GridViewItemWidth;

    public int GridViewItemHeight => (int)(GridViewItemWidth * 1.5);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameGridViewIcon))]
    public partial GameGridViewType GameGridViewType { get; set; } = Settings.Instance.GameGridViewType;

    public FontIcon GameGridViewIcon => GameGridViewType switch
    {
        GameGridViewType.GridView => new FontIcon() { Glyph = "\xF0E2" },
        GameGridViewType.ListView => new FontIcon() { Glyph = "\xE8FD" },
        _ => new FontIcon() { },
    };

    public GameGridPageModelTranslationProperties TranslationProperties { get; } = new GameGridPageModelTranslationProperties();

    public GameGridPageModel(GameGridPage gameGridPage)
    {
        // Not an async lambda. Two parameters makes it bind to Register<TMessage>(object,
        // Action<object, TMessage>), which the messenger invokes as async void - so an exception from
        // RemoveAllGames or InitialLoadAsync escaped to the SynchronizationContext and became an
        // unhandled exception, which since 1.59 means the user gets the crash window for what is a routine
        // background rescan. Logged instead, and the guard makes a second message a no-op rather than a
        // second concurrent reload.
        WeakReferenceMessenger.Default.Register<GameLibrariesStateChangedMessage>(this, (sender, message) =>
        {
            ReloadAfterLibraryChange().SafeFireAndForget((err) =>
            {
                Logger.Error(err, "Unable to reload games after a library change.");
            });
        });

        this.gameGridPage = gameGridPage;
        MassUpgrade = new MassUpgradeViewModel(this);
        ApplyGameGroupFilter();
    }

    public async Task InitialLoadAsync()
    {
        IsGameListLoading = true;
        IsDLSSLoading = true;

        // Armed here rather than from a property-changed hook on those flags, because they both start
        // true. The source generator's setter returns early when the new value equals the old one, so
        // assigning true to a flag that is already true raises nothing - and a hook on IsGameListLoading
        // would never have fired on the first load, which is the one load where the text is wanted most.
        StartScanProgressTimer();

        try
        {
            await GameManager.Instance.LoadGamesFromCacheAsync();

            IsGameListLoading = false;

            await GameManager.Instance.LoadGamesAsync(false);
        }
        finally
        {
            // Even if a game library throws, don't leave the UI stuck showing a loading spinner forever.
            // GameGridPage.xaml.cs's SafeFireAndForget call already logs the exception.
            IsGameListLoading = false;
            IsDLSSLoading = false;
            FinishScanProgress();
        }
    }

    /// <summary>
    /// Reloads the library after a store was enabled or disabled.
    /// </summary>
    /// <remarks>
    /// Guarded, because this can arrive while a load is already running. Toggling a library switch sends
    /// the message; the first load at startup is still adding games; and RemoveAllGames clears both lists
    /// while that load is mid-flight. The result is a list that never finishes repopulating, and whichever
    /// run's finally finishes first clears the loading flags for both - so the UI stops showing a spinner
    /// while the library is still empty.
    ///
    /// Same hazard, same remedy as RefreshGamesButtonAsync: claim before doing anything, and release in a
    /// finally so one failure does not leave the guard set.
    /// </remarks>
    async Task ReloadAfterLibraryChange()
    {
        if (Interlocked.CompareExchange(ref _reloading, 1, 0) != 0)
        {
            return;
        }

        try
        {
            GameManager.Instance.RemoveAllGames();
            await InitialLoadAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _reloading, 0);
        }
    }

    /// <summary>Guards against a library-change reload running on top of another one.</summary>
    int _reloading;

    public void SearchForGameEvent(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            throw new ArgumentException("Sender must be a TextBox");
        }

        if (string.IsNullOrEmpty(textBox.Text))
        {
            CurrentCollectionView = GameManager.Instance.GetGameCollection();
            return;
        }
        CurrentCollectionView = GameManager.Instance.GetGameCollection(textBox.Text);
    }

    [RelayCommand]
    async Task AddManualGameButtonAsync()
    {
        if (Settings.Instance.DontShowManuallyAddingGamesNotice == false)
        {
            var dontShowAgainCheckbox = new CheckBox()
            {
                Content = new TextBlock()
                {
                    Text = ResourceHelper.GetString("General_DontShowAgain"),
                },
            };

            var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("GamesPage_ManuallyAdding_NoteTitle"),
                PrimaryButtonText = ResourceHelper.GetString("GamesPage_AddGame"),
                SecondaryButtonText = ResourceHelper.GetString("General_ReportIssue"),
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new StackPanel()
                {
                    Children = {
                        new TextBlock()
                        {
                            TextWrapping = TextWrapping.Wrap,
                            Text = ResourceHelper.GetString("GamesPage_ManuallyAdding_NoteMessage"),
                        },
                        dontShowAgainCheckbox,
                    },
                    Orientation = Orientation.Vertical,
                    Spacing = 16,
                },
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.None)
            {
                return;
            }


            if (result == ContentDialogResult.Primary)
            {
                // Only dismiss the notice for good once the user has proceeded to add games.
                if (dontShowAgainCheckbox.IsChecked == true)
                {
                    Settings.Instance.DontShowManuallyAddingGamesNotice = true;
                }
                await AddGameManually();
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/beeradmoore/dlss-swapper/issues"));
            }
        }
        else
        {
            await AddGameManually();
        }
    }

    async Task AddGameManually()
    {
        TextBlockBuilder textBlockBuilder = new TextBlockBuilder(ResourceHelper.GetString("GamesPage_ManuallyAdding_InfoHtml"));

        if (Settings.Instance.HasShownAddGameFolderMessage == false)
        {
            var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("GamesPage_ManuallyAdding_AnotherNoteTitle"),
                PrimaryButtonText = ResourceHelper.GetString("GamesPage_AddGame"),
                CloseButtonText = ResourceHelper.GetString("General_Close"),
                DefaultButton = ContentDialogButton.Primary,
                Content = textBlockBuilder.Build()
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.None)
            {
                return;
            }

            Settings.Instance.HasShownAddGameFolderMessage = true;
        }

        var installPath = string.Empty;
        try
        {
            // Associate the HWND with the folder picker
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentApp.MainWindow);


            var folder = FileSystemHelper.OpenFolder(hWnd, okButtonLabel: ResourceHelper.GetString("GamesPage_ManuallyAdding_SelectGameFolder"));

            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            installPath = folder;

            // If top level directory throw error.
            if (installPath == Path.GetPathRoot(installPath))
            {
                var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
                {
                    CloseButtonText = ResourceHelper.GetString("General_Okay"),
                    DefaultButton = ContentDialogButton.Close,
                    Title = ResourceHelper.GetString("General_Error"),
                    Content = ResourceHelper.GetString("GamesPage_ManuallyAdding_TopLevelDirectoryNotSupported"),
                };
                await dialog.ShowAsync();
                return;
            }


            var gameFolderAlreadyExists = GameManager.Instance.CheckIfGameIsAdded(installPath);
            if (gameFolderAlreadyExists == true)
            {
                var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
                {
                    Title = ResourceHelper.GetString("GamesPage_ManuallyAdding_ErrorTitle"),
                    CloseButtonText = ResourceHelper.GetString("General_Close"),
                    Content = ResourceHelper.GetFormattedResourceTemplate("GamesPage_ManuallyAdding_PathExistsTemplate", installPath),
                };
                await dialog.ShowAsync();
                return;
            }

            var manuallyAddGameControl = new ManuallyAddGameControl(installPath);
            var addGameDialog = new FakeContentDialog() //XamlRoot
            {
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                PrimaryButtonText = ResourceHelper.GetString("GamesPage_AddGame"),
                DefaultButton = ContentDialogButton.Primary,
                Content = manuallyAddGameControl,
            };
            addGameDialog.Resources["ContentDialogMinWidth"] = 700;
            addGameDialog.Resources["ContentDialogMaxWidth"] = 700;

            var addGameResult = await addGameDialog.ShowAsync();
            if (manuallyAddGameControl.DataContext is ManuallyAddGameModel manuallyAddGameModel)
            {
                if (addGameResult == ContentDialogResult.Primary)
                {
                    var game = manuallyAddGameModel.Game;
                    await game.SaveToDatabaseAsync();
                    game.ProcessGame();
                    GameManager.Instance.AddGame(game, true);
                }
                else
                {
                    // Cleanup if user is going back.
                    await manuallyAddGameModel.Game.DeleteAsync();
                }
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, $"Attempted to manually add game from path \"{installPath}\" but got an error.");
            var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
            {
                Title = ResourceHelper.GetString("GamesPage_ManuallyAdding_ErrorTitle"),
                CloseButtonText = ResourceHelper.GetString("General_Close"),
                PrimaryButtonText = ResourceHelper.GetString("General_ReportIssue"),
                DefaultButton = ContentDialogButton.Primary,
                Content = $"{ResourceHelper.GetString("GamesPage_ManuallyAdding_CouldntAddError")}\n\n{ResourceHelper.GetString("General_ErrorMessage")}: {err.Message}",
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/beeradmoore/dlss-swapper/issues"));
            }
        }
    }

    /// <summary>
    /// Guards against two refreshes running at once.
    /// </summary>
    /// <remarks>
    /// A counter rather than a bool, read and written with Interlocked, because the guard is consulted from
    /// the UI thread and the command can be invoked from anywhere - a keyboard shortcut, or a caller that
    /// does not go through the button's IsEnabled binding.
    ///
    /// The disabled button is a UI affordance, not a guarantee. It used to bind to the negation of
    /// IsLoading, which is derived from two flags that both the initial load and this command write, so any
    /// disagreement between them left a window in which a second click reached the command. Two concurrent
    /// runs then each queued every game for ProcessGame - a recursive directory walk and a cover fetch per
    /// game - which reads from the outside as a refresh that never settles.
    /// </remarks>
    int _refreshing;

    /// <summary>Whether a refresh is running right now.</summary>
    public bool IsRefreshing => Volatile.Read(ref _refreshing) > 0;

    /// <summary>Whether the refresh command can run, for the button's enabled state.</summary>
    public bool CanRefresh => IsLoading == false && IsRefreshing == false;

    [RelayCommand]
    async Task RefreshGamesButtonAsync()
    {
        // Claim the slot before doing anything else, so two callers cannot both get past this point.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            IsDLSSLoading = true;
            OnPropertyChanged(nameof(CanRefresh));

            // A refresh re-queues every game, so it is a scan run and needs the same counter as the first
            // load. Started after the flags, for the same reason InitialLoadAsync does it there.
            StartScanProgressTimer();

            await GameManager.Instance.LoadGamesAsync(true);
        }
        finally
        {
            // The finally matters: without it, one thrown exception leaves IsRefreshing set and the button
            // permanently dead, and the user cannot refresh again without restarting the app.
            IsDLSSLoading = false;
            Interlocked.Exchange(ref _refreshing, 0);
            OnPropertyChanged(nameof(CanRefresh));
            FinishScanProgress();
        }
    }

    [RelayCommand]
    async Task FilterGamesButtonAsync()
    {
        var gameFilterControl = new GameFilterControl();

        var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("General_Filter"),
            PrimaryButtonText = ResourceHelper.GetString("General_Apply"),
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            Content = gameFilterControl,
        };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (gameFilterControl.DataContext is GameFilterControlViewModel gameFilterControlViewModel)
            {
                Settings.Instance.HideNonDLSSGames = gameFilterControlViewModel.HideNonSwappableGames;
                GameManager.Instance.ShowHiddenGames = gameFilterControlViewModel.ShowHiddenGames;
                Settings.Instance.GroupGameLibrariesTogether = gameFilterControlViewModel.GroupGameLibrariesTogether;
            }

            ApplyGameGroupFilter();
        }

    }

    void ApplyGameGroupFilter()
    {
        // TODO: Remove weird hack which otherwise causes MainGridView_SelectionChanged to fire when changing MainGridView.ItemsSource.
        //gameGridPage.MainGridView.SelectionChanged -= MainGridView_SelectionChanged;

        //MainGridView.ItemsSource = null;
        CurrentCollectionView = null;
        CurrentCollectionView = GameManager.Instance.GetGameCollection();
    }

    [RelayCommand]
    async Task UnknownAssetsFoundButtonAsync()
    {
        var newDllsControl = new NewDLLsControl();

        var dialog = new EasyContentDialog(gameGridPage.XamlRoot)
        {
            Title = ResourceHelper.GetString("GamesPage_NewDllsFound"),
            CloseButtonText = ResourceHelper.GetString("General_Close"),
            Content = newDllsControl,
        };
        dialog.Resources["ContentDialogMinWidth"] = 700;
        dialog.Resources["ContentDialogMaxWidth"] = 700;
        await dialog.ShowAsync();
    }

    [RelayCommand]
    void ChangeGameGridView(GameGridViewType gameGridView)
    {
        if (gameGridView == this.GameGridViewType)
        {
            return;
        }

        GameGridViewType = gameGridView;
        gameGridPage.ReloadMainContentControl();
        Settings.Instance.GameGridViewType = gameGridView;
    }

    // ------------------------------------------------------------------ scan progress

    /// <summary>How often the scan progress is re-read while something is loading.</summary>
    /// <remarks>
    /// Fast enough to feel live, slow enough that a library of several hundred games does not spend its
    /// time formatting a string. The text is three digits wide; nobody can read a number that updates
    /// faster than the eye.
    /// </remarks>
    static readonly TimeSpan ScanProgressInterval = TimeSpan.FromMilliseconds(400);

    // A System.Threading.Timer rather than Microsoft.UI.Dispatching.DispatcherQueueTimer.
    //
    // The WinUI timer takes its queue through a constructor overload that this SDK's projection does not
    // offer in the two obvious shapes, and this page already has a working timer of the other kind - the
    // one that persists the grid width - using exactly this pattern. Matching it keeps one idiom for
    // "do this again shortly" rather than two.
    //
    // Marshalled to the UI thread rather than written from the timer thread: ScanStatusText is an
    // ObservableProperty and setting a bound property off-thread is the same hazard the scroll timer
    // documents at length.
    Timer? _scanProgressTimer;

    /// <summary>
    /// Whether the progress timer should keep going.
    /// </summary>
    /// <remarks>
    /// A flag rather than just cancelling the timer, because a cancel cannot recall a callback that has
    /// already started. Without it, a load that finished while a tick was mid-flight would have that tick
    /// re-arm the timer afterwards, leaving it running against a page with nothing to report - and, on the
    /// games page, forever, since the timer stops only when something ends a load.
    ///
    /// Volatile because the callback runs on a pool thread while the flag is written from the UI thread.
    /// </remarks>
    volatile bool _scanProgressWanted;

    [ObservableProperty]
    public partial string ScanStatusText { get; private set; } = string.Empty;

    /// <summary>
    /// Recomputes "Scanning 12 of 340", or nothing when nothing is being scanned.
    /// </summary>
    /// <remarks>
    /// Must run on the UI thread; it writes a bound property. The formatting itself is
    /// <see cref="FormatScanStatus"/>, which is pure and separately tested - the rule that finished can
    /// never be reported above queued is the one worth pinning down, and it cannot be checked from a
    /// property that needs a loaded page to read.
    /// </remarks>
    void UpdateScanStatus()
    {
        ScanStatusText = FormatScanStatus(IsLoading, Game.ScansQueued, Game.ScansFinished);

        // Nothing left to report, so stop asking. Covers a load whose game list never queued anything -
        // an empty library, or every library disabled - where the timer would otherwise poll forever
        // against a page that has nothing to say.
        if (IsLoading == false || Game.ScansQueued == 0)
        {
            StopScanProgressTimer();
        }
    }

    /// <summary>
    /// Renders the scan counter. Pure, so it can be tested without a page.
    /// </summary>
    /// <param name="isLoading">Whether a load is in progress.</param>
    /// <param name="queued">Scans handed to a worker since the last reset.</param>
    /// <param name="finished">Of those, how many have run to completion.</param>
    /// <remarks>
    /// <paramref name="finished"/> is clamped down to <paramref name="queued"/>, never the other way
    /// round. The two counters are reset at the start of each library load, and a refresh can begin while
    /// the previous load's scans are still running, so finished can legitimately exceed queued. "Scanning
    /// 400 of 340" reads as a fault in the app rather than as a refresh that overtook a load.
    ///
    /// Empty unless there is a load in progress and something was queued, which is what lets the page
    /// bind it straight to visibility: no converter, and no empty label left behind between operations.
    /// </remarks>
    internal static string FormatScanStatus(bool isLoading, int queued, int finished)
    {
        if (isLoading == false || queued <= 0)
        {
            return string.Empty;
        }

        return $"Scanning {Math.Min(finished, queued)} of {queued}";
    }

    /// <summary>The timer callback. Marshals to the UI thread, then decides whether to run again.</summary>
    void OnScanProgressTick()
    {
        UiDispatcher.Invoke(UpdateScanStatus);

        // Re-armed only while still wanted, and re-armed from inside the callback rather than by
        // UpdateScanStatus. The one-shot design means the only thing that can restart a stopped timer is
        // a fresh StartScanProgressTimer, which is what makes "stop" mean stopped.
        if (_scanProgressWanted && IsLoading)
        {
            _scanProgressTimer?.Change(ScanProgressInterval, Timeout.InfiniteTimeSpan);
        }
    }

    void StartScanProgressTimer()
    {
        _scanProgressWanted = true;

        if (_scanProgressTimer is null)
        {
            _scanProgressTimer = new Timer(
                _ => OnScanProgressTick(),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        _scanProgressTimer.Change(ScanProgressInterval, Timeout.InfiniteTimeSpan);

        // Once, immediately, rather than waiting out the first interval. The queued count is usually
        // already non-zero by the time a load begins, so waiting 400ms to draw it makes the page look
        // like nothing is happening for the first fraction of a second of every load.
        UpdateScanStatus();
    }

    /// <summary>Stops the progress timer, leaving it usable for the next load.</summary>
    /// <remarks>
    /// Not disposed, so navigating back to the page reuses it rather than building another. Clearing the
    /// flag before cancelling is what stops an in-flight callback from re-arming it - see
    /// <see cref="_scanProgressWanted"/>.
    /// </remarks>
    public void StopScanProgressTimer()
    {
        _scanProgressWanted = false;
        _scanProgressTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Clears the text and stops the timer, for the end of a load.
    /// </summary>
    /// <remarks>
    /// Called from the same <c>finally</c> that clears the loading flags, rather than left to the timer's
    /// next tick. Waiting out an interval to clear a string that is no longer true leaves a stale count on
    /// screen during the gap, which is exactly the confusion this was added to remove.
    /// </remarks>
    void FinishScanProgress()
    {
        StopScanProgressTimer();
        ScanStatusText = string.Empty;
    }
}
