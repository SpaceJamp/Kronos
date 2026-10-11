using Kronos.Data;
using Kronos.Helpers;
using Kronos.Pages;
using Kronos.UserControls;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.System;

namespace Kronos;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindowModel ViewModel { get; private set; }

    IntPtr _windowIcon;

    readonly WindowPositionRect _trackedWindow = new WindowPositionRect();

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, string iconPath, ref IntPtr index);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int DestroyIcon(IntPtr hIcon);

    public MainWindow()
    {
        // Backstop for the same check App.OnLaunched performs. Deliberately duplicated rather than
        // left as a single check: the launch gate is one `if` in one method, and a requirement meant to
        // be hard to get past should not be defeatable by deleting one line from one file. If this ever
        // fires, the launch gate was bypassed - which is exactly when a second gate is worth having.
        //
        // Not a substitute for the launch gate, which still runs first, before the disk, database and
        // network are touched at all.
        if (WindowsVersionSupport.IsSupportedCurrentMachine() == false)
        {
            Logger.Error("MainWindow constructed on an unsupported Windows despite the launch check: " +
                         WindowsVersionSupport.DescribeRefusal(Environment.OSVersion.Version));

            // Throws rather than returning. A bare return left the constructor half-finished - no
            // InitializeComponent, ViewModel null - and App still handed that object to WindowManager, which
            // activated and registered it. The user got an empty window, and the first navigation threw a
            // NullReferenceException on a null ViewModel, which the unhandled handler turns into an app
            // that disappears. UnsupportedArchitectureWindow, shown just above, is what the user should be
            // looking at instead.
            //
            // This path is unreachable while the launch gate in App.OnLaunched is intact, and that is the
            // point: it is a backstop for the case where someone deleted one line from one file.
            throw new NotSupportedException(
                "Kronos cannot run on this version of Windows. " +
                WindowsVersionSupport.DescribeRefusal(Environment.OSVersion.Version));
        }

        this.InitializeComponent();
        ViewModel = new MainWindowModel();

        RegisterSettingsAccelerator();

        if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
        {
            var lastWindowSizeAndPosition = Settings.Instance.LastWindowSizeAndPosition;
            _trackedWindow = new WindowPositionRect(lastWindowSizeAndPosition);

            if (lastWindowSizeAndPosition.Width > 512 && lastWindowSizeAndPosition.Height > 512)
            {
                AppWindow.MoveAndResize(lastWindowSizeAndPosition.GetRectInt32());
            }
            if (lastWindowSizeAndPosition.State == OverlappedPresenterState.Maximized)
            {
                overlappedPresenter.Maximize();
            }
        }

        AppWindow?.Changed += (AppWindow sender, AppWindowChangedEventArgs args) =>
        {
            if (args.DidPositionChange)
            {
                if (sender.Presenter is OverlappedPresenter presenter)
                {
                    var isCurrentlyMinimizedOrMaximized =
                        presenter.State == OverlappedPresenterState.Minimized ||
                        presenter.State == OverlappedPresenterState.Maximized;

                    if (isCurrentlyMinimizedOrMaximized == false)
                    {
                        _trackedWindow.UpdatePosition(sender.Position);
                    }
                }
            }
        };

        SizeChanged += (object sender, WindowSizeChangedEventArgs args) =>
        {
            if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
            {
                var currentState = overlappedPresenter.State;
                var isTransitioningToMaximized =
                    currentState == OverlappedPresenterState.Maximized &&
                    _trackedWindow.State != OverlappedPresenterState.Maximized;

                if (isTransitioningToMaximized == false && currentState != OverlappedPresenterState.Maximized)
                {
                    _trackedWindow.UpdateFromAppWindow(AppWindow);
                }

                _trackedWindow.State = overlappedPresenter.State;
            }
        };

        Closed += (object sender, WindowEventArgs args) =>
        {
            if (AppWindow?.Presenter is OverlappedPresenter overlappedPresenter)
            {
                Settings.Instance.LastWindowSizeAndPosition = new WindowPositionRect(_trackedWindow);
            }

            // Release the icon.
            if (_windowIcon != IntPtr.Zero)
            {
                DestroyIcon(_windowIcon);
                _windowIcon = IntPtr.Zero;
            }
        };

        if (WindowManager.IsCustomizationSupported)
        {
            var appWindow = App.CurrentApp.WindowManager.GetAppWindowForWindow(this);
            var appWindowTitleBar = appWindow.TitleBar;
            appWindowTitleBar.ExtendsContentIntoTitleBar = true;
            RootGrid.RowDefinitions[0].Height = new GridLength(32);
        }
        else
        {
            RootGrid.RowDefinitions[0].Height = new GridLength(28);
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        SetIcon();

        // Update settings text when language changes.
        LanguageManager.Instance.OnLanguageChanged += () =>
        {
            if (MainNavigationView.SettingsItem is NavigationViewItem settingsNavigationViewItem)
            {
                settingsNavigationViewItem.Content = ResourceHelper.GetString("SettingsPage_Title");
            }
        };
    }



    /// <summary>
    /// Default the Window Icon to the icon stored in the .exe, if any.
    ///
    /// The Icon can be overriden by callers by calling SetIcon themselves.
    /// </summary>
    /// via this MAUI PR https://github.com/dotnet/maui/pull/6900
    void SetIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            var index = IntPtr.Zero; // 0 = first icon in resources
            _windowIcon = ExtractAssociatedIcon(IntPtr.Zero, processPath, ref index);
            if (_windowIcon != IntPtr.Zero)
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

                var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
                if (appWindow is not null)
                {
                    var iconId = Win32Interop.GetIconIdFromIcon(_windowIcon);
                    appWindow.SetIcon(iconId);
                }
            }
        }
    }



    void MainNavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            GoToPage(SettingsPage.PageTag);
        }
        else if (args.InvokedItemContainer.Tag is string invokedItem)
        {
            GoToPage(invokedItem);
        }
        else
        {
            Logger.Error($"MainNavigationView_ItemInvoked for a page that was not found. ({args.InvokedItemContainer?.Tag}, {args.InvokedItem})");
        }
    }

    /// <summary>
    /// VK_OEM_COMMA - the physical comma key.
    /// </summary>
    /// <remarks>
    /// Named from the Win32 virtual-key code rather than from a <see cref="VirtualKey"/> member, because
    /// there is no member for it: the projection in this SDK has no Oem-prefixed names at all, so both
    /// <c>VirtualKey.OemComma</c> and <c>Key="OemComma"</c> in XAML fail to compile. The numeric value
    /// is stable and is what every Windows app means by it.
    ///
    /// The physical key, rather than the comma character, is the point: the shortcut is Ctrl plus the key
    /// the user's layout puts there - a comma on a US layout and something else entirely on several
    /// others - which is what every other Windows app does.
    /// </remarks>
    const VirtualKey CommaKey = (VirtualKey)0xBE;

    /// <summary>Ctrl+, - the platform convention for Settings, from any page.</summary>
    /// <remarks>
    /// Registered in code rather than declared in XAML, for the reason above: there is no markup spelling
    /// of this accelerator that compiles.
    ///
    /// Attached to the root grid rather than to a games page toolbar, because navigation is the
    /// window's business and the shortcut has to work from every page.
    /// </remarks>
    void RegisterSettingsAccelerator()
    {
        var settingsAccelerator = new KeyboardAccelerator
        {
            Key = CommaKey,
            Modifiers = VirtualKeyModifiers.Control,
        };

        settingsAccelerator.Invoked += SettingsAccelerator_Invoked;

        RootGrid.KeyboardAccelerators.Add(settingsAccelerator);
    }

    void SettingsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Handled rather than left to bubble. Without this the accelerator fires once here and once again
        // for anything further up the tree that also handles it, which is how a single keypress ends up
        // navigating twice.
        args.Handled = true;

        GoToPage(SettingsPage.PageTag);
    }


    GameGridPage? gameGridPage;
    LibraryPage? libraryPage;
    SettingsPage? settingsPage;

    public GameGridPage? GameGridPage => gameGridPage;

    void GoToPage(string page)
    {
        ViewModel.AcknowledgementsVisibility = Visibility.Collapsed;

        if (page == GameGridPage.PageTag)
        {
            if (ContentFrame.Content is null || ContentFrame.Content as Page != gameGridPage)
            {
                ContentFrame.Content = gameGridPage ??= new GameGridPage();
            }
        }
        else if (page == LibraryPage.PageTag)
        {
            if (ContentFrame.Content is null || ContentFrame.Content as Page != libraryPage)
            {
                ContentFrame.Content = libraryPage ??= new LibraryPage();
            }
        }
        else if (page == SettingsPage.PageTag)
        {
            if (ContentFrame.Content is null || ContentFrame.Content as Page != settingsPage)
            {
                ContentFrame.Content = settingsPage ??= new SettingsPage();
            }
        }
        else if (page ==  AcknowledgementsPage.PageTag)
        {
            if (ContentFrame.Content is null || ContentFrame.Content is not AcknowledgementsPage)
            {
                ViewModel.AcknowledgementsVisibility = Visibility.Visible;
                ContentFrame.Content = new AcknowledgementsPage();
            }
        }
        else
        {
            Logger.Error($"Attempting to navigate to a page that was not found, {page}");
            return;
        }

        // Only try manually set selected item if is not already selected.
        if (MainNavigationView.SelectedItem is null || (MainNavigationView.SelectedItem is NavigationViewItem selectedItem && selectedItem.Tag.ToString() != page))
        {
            foreach (NavigationViewItem navigationViewItem in MainNavigationView.MenuItems)
            {
                if (navigationViewItem.Tag.ToString() == page)
                {
                    MainNavigationView.SelectedItem = navigationViewItem;
                    break;
                }
            }
        }
    }

    internal void GoToAcknowledgements()
    {
        GoToPage(AcknowledgementsPage.PageTag);
    }

    async void MainNavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationView navigationView && navigationView.SettingsItem is NavigationViewItem settingsNavigationViewItem)
        {
            settingsNavigationViewItem.Tag = SettingsPage.PageTag;
            settingsNavigationViewItem.Content = ResourceHelper.GetString("SettingsPage_Title");
        }


        // TODO: Disabled because CommunityToolkit.WinUI.Helpers.SystemInformation.Instance.IsAppUpdated throws exceptions for unpackaged apps.
        /*
        // If this is a new build, fetch updates to display to the user.
        Task<Data.GitHub.GitHubRelease> releaseNotesTask = null;
        if (CommunityToolkit.WinUI.Helpers.SystemInformation.Instance.IsAppUpdated)
        {
            var currentAppVersion = App.CurrentApp.GetVersion();
            releaseNotesTask = gitHubUpdater.GetReleaseFromTag($"v{currentAppVersion.Major}.{currentAppVersion.Minor}.{currentAppVersion.Build}.{currentAppVersion.Revision}");
        }
        */

        var gitHubUpdater = new Data.GitHub.GitHubUpdater();

        // If this is a GitHub build check if there is a new version.
        var newUpdateTask = gitHubUpdater.CheckForNewGitHubRelease(false);

        await DLLManager.Instance.LoadManifestsAsync();


        // A partial update leaves update-errors.log naming the files it could not replace. Surfacing it here
        // is the whole point of the file: it was introduced in 1.55 so a partial update would not be
        // silent, and it was, because nothing ever read it back.
        //
        // Before the other startup prompts, because a half-applied update is the thing most likely to
        // explain whatever the user is about to be told next.
        await ShowUpdateErrorReportIfPresentAsync();

        if (Settings.Instance.HasShownMultiplayerWarning == false)
        {
            var dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
            {
                Title = ResourceHelper.GetString("MainWindow_NoteForMultiplayerGames_Title"),
                CloseButtonText = ResourceHelper.GetString("General_Okay"),
                DefaultButton = ContentDialogButton.Close,
                Content = ResourceHelper.GetString("MainWindow_NoteForMultiplayerGames_Message"),
            };
            var result = await dialog.ShowAsync();

            Settings.Instance.HasShownMultiplayerWarning = true;
        }


        if (DLLManager.Instance.HasLoadedManifest() == false)
        {
            var dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
            {
                Title = ResourceHelper.GetString("General_Error"),
                CloseButtonText = ResourceHelper.GetString("General_Close"),
                PrimaryButtonText = ResourceHelper.GetString("MainWindow_ManifestCouldNotBeLoaded_GitHubIssues"),
                SecondaryButtonText = ResourceHelper.GetString("MainWindow_ManifestCouldNotBeLoaded_UpdateManifest"),
                DefaultButton = ContentDialogButton.Primary,
                Content = ResourceHelper.GetString("MainWindow_ManifestCouldNotBeLoaded_Message"),
            };
            var shouldClose = true;

            var response = await dialog.ShowAsync();
            if (response == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri("https://github.com/beeradmoore/dlss-swapper/issues"));
            }
            else if (response is ContentDialogResult.Secondary)
            {
                dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
                {
                    Title = ResourceHelper.GetString("MainWindow_AttemptingManifestUpdate"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = new ProgressRing()
                    {
                        IsActive = true,
                        IsIndeterminate = true,
                    },
                };

                var updateTask = DLLManager.Instance.UpdateManifestAsync();
                _ = dialog.ShowAsync();
                await updateTask;
                dialog.Hide();

                if (DLLManager.Instance.HasLoadedManifest() == true)
                {
                    shouldClose = false;
                }
            }

            if (shouldClose)
            {
                dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
                {
                    Title = ResourceHelper.GetString("MainWindow_DlssSwapperMustClose"),
                    CloseButtonText = ResourceHelper.GetString("General_Close"),
                    DefaultButton = ContentDialogButton.Close,
                    Content = ResourceHelper.GetString("MainWindow_DlssSwapperCloseDueToManifest"),
                };
                await dialog.ShowAsync();

                Close();

                // Returned, and this is load-bearing rather than tidiness.
                //
                // This handler is async void, so a missing return does not stop it - the next await
                // resumes normally and carries on building dialogs against MainNavigationView.XamlRoot on a
                // window that has been closed. That throws out of an async void, which lands in
                // App_UnhandledException, which shows the crash window while the app is shutting down. So a
                // user whose manifest failed to load got a crash report on top of a deliberate exit.
                //
                // Everything past this point needs a live window: the imported-manifest dialog, hiding the
                // loading panel, GoToPage, and the update prompt.
                return;
            }
        }

        if (DLLManager.Instance.ImportedManifest is null)
        {
            var dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
            {
                Title = ResourceHelper.GetString("LibraryPage_CouldNotLoadImportedDlls"),
                DefaultButton = ContentDialogButton.Close,
                Content = new ImportSystemDisabledView(),
                CloseButtonText = ResourceHelper.GetString("General_Close"),
            };
            await dialog.ShowAsync();
        }

        //FilterDLLRecords();

        // Yeet this into the void and let it load in the background.
        _ = DLLManager.Instance.UpdateManifestAsync();

        // We are now ready to show the games list.
        LoadingStackPanel.Visibility = Visibility.Collapsed;

        GoToPage(GameGridPage.PageTag);

        // TODO: Disabled because CommunityToolkit.WinUI.Helpers.SystemInformation.Instance.IsAppUpdated throws exceptions for unpackaged apps.
        /*
        if (releaseNotesTask is not null)
        {
            await releaseNotesTask;
            if (releaseNotesTask.Result is not null)
            {
                gitHubUpdater?.DisplayWhatsNewDialog(releaseNotesTask.Result, MainNavigationView);
            }
        }
        */

        // TODO: What happens if you have no internet
        await newUpdateTask;
        if (newUpdateTask.Result is not null)
        {
            if (gitHubUpdater.HasPromptedBefore(newUpdateTask.Result) == false)
            {
                await gitHubUpdater.DisplayNewUpdateDialog(newUpdateTask.Result, MainNavigationView.XamlRoot);
            }
        }
    }

    /// <summary>
    /// Tells the user when the last update did not fully apply, and offers to show them which files.
    /// </summary>
    /// <remarks>
    /// Reads update-errors.log, which the portable update writes when it cannot replace every file. It was
    /// added in 1.55 so a partial update would not be silent, and it was - nothing read it back, so the log
    /// existed to be seen and no one saw it.
    ///
    /// The log is not deleted afterwards. A user who wants to report the problem should still be able to
    /// open it, and the next update overwrites it anyway, so removing it would only make the report harder
    /// to reproduce. The user can delete it themselves from the folder this opens.
    ///
    /// Every failure path here is swallowed. A startup dialog is the last thing that should be able to stop
    /// the app opening, and the file this reads is written by a PowerShell script after the app has exited -
    /// it can be absent, truncated, or locked by something.
    /// </remarks>
    private async Task ShowUpdateErrorReportIfPresentAsync()
    {
        try
        {
            var report = UpdateErrorLog.TryRead();
            if (report is null)
            {
                return;
            }

            var description = UpdateErrorLog.Describe(report.Value);

            var dialog = new EasyContentDialog(MainNavigationView.XamlRoot)
            {
                Title = ResourceHelper.GetString("MainWindow_UpdateIncomplete_Title"),
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = description,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            // The path, verbatim. If someone is going to send this in a bug report - and
                            // this is the sort of thing they will - the exact location matters.
                            Text = report.Value.Path,
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.7,
                            FontSize = 12,
                        },
                    },
                },
                PrimaryButtonText = ResourceHelper.GetString("MainWindow_UpdateIncomplete_ShowLog"),
                CloseButtonText = ResourceHelper.GetString("General_Close"),
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                FileSystemHelper.OpenFolderInExplorerSelectFile(report.Value.Path);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not report a partial update.");
        }
    }

    /// <summary>
    ///
    /// </summary>
    // Previously: FilterDLSSRecords
    internal void FilterDLLRecords()
    {
        // TODO: Reimplement
        /*
        var newDlssRecordsList = new List<DLLRecord>();
        if (Settings.Instance.AllowUntrusted)
        {
            newDlssRecordsList.AddRange(App.CurrentApp.Manifest.DLSS);
            newDlssRecordsList.AddRange(App.CurrentApp.ImportedManifest.DLSS);
        }
        else
        {
            newDlssRecordsList.AddRange(App.CurrentApp.Manifest.DLSS.Where(x => x.IsSignatureValid == true));
            newDlssRecordsList.AddRange(App.CurrentApp.ImportedManifest.DLSS.Where(x => x.IsSignatureValid == true));
        }

        newDlssRecordsList.Sort();
        CurrentDLSSRecords.Clear();
        CurrentDLSSRecords.AddRange(newDlssRecordsList);
        */

    }
}
