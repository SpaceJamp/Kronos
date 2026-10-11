using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ByteSizeLib;
using CommunityToolkit.WinUI.Controls;
using Kronos.Extensions;
using Kronos.Helpers;
using Kronos.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Kronos.Data.GitHub;

/// <summary>
/// The outcome of an update check.
/// </summary>
/// <remarks>
/// Added because "no update" and "the check failed" both used to come back as a null
/// <c>GitHubRelease</c>, and the settings page turned both into "No new updates available". A dead
/// endpoint therefore looked exactly like being up to date, which is why a check pointing at a
/// repository that no longer exists was reported as working and doing nothing.
/// </remarks>
internal enum UpdateCheckResult
{
    /// <summary>No newer version than the running one.</summary>
    UpToDate,

    /// <summary>A newer version exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not be completed, so nothing is known either way.</summary>
    Failed,
}

/// <summary>
/// Helper class to be notified of updates of the app (which is Debug and Release builds)
/// </summary>
internal class GitHubUpdater
{
    /// <summary>
    /// The repository releases are read from.
    /// </summary>
    /// <remarks>
    /// In one place, because it was previously repeated in two string literals and a private or
    /// renamed repository 404s without saying so anywhere the user could see.
    ///
    /// This has now been wrong twice. It was SpaceJamp/unofficial-dlss-swapper, left over from when
    /// this was a fork and never changed when it stopped being one. It was then changed to
    /// SpaceJamp/kronos-dlss-swapper, which does not exist either. The repository is SpaceJamp/Kronos.
    ///
    /// Neither stale name showed up, because GitHub redirects the old path for git operations, so a
    /// push still succeeds and the wrong name stays invisible until something reads it without
    /// credentials - which is exactly what this code does. A 404 here surfaces as a failed check, so
    /// the update check has simply never worked, with an error the user cannot connect to a name.
    ///
    /// A test now compares this against the repository's own origin remote, so a rename breaks the
    /// build rather than the updater.
    ///
    /// The repository is public. GitHub answers 404 rather than 403 for a private repository read
    /// unauthenticated, so a change back to private would look exactly as this did.
    /// </remarks>
    internal const string DefaultRepository = "SpaceJamp/Kronos";

    /// <summary>
    /// The API root for a repository's releases.
    /// </summary>
    internal static string GetLatestReleaseApiUrl(string? repository = null)
    {
        return $"https://api.github.com/repos/{NormaliseRepository(repository)}/releases/latest";
    }

    /// <summary>
    /// The API root for one tag of a repository.
    /// </summary>
    internal static string GetReleaseByTagApiUrl(string tag, string? repository = null)
    {
        return $"https://api.github.com/repos/{NormaliseRepository(repository)}/releases/tags/{tag}";
    }

    /// <summary>
    /// Falls back to <see cref="DefaultRepository"/> for an empty or whitespace value.
    /// </summary>
    /// <remarks>
    /// An empty repository string would otherwise produce a URL like "repos//releases/latest", which
    /// fails in a way that looks like a network problem rather than a misconfiguration.
    /// </remarks>
    internal static string NormaliseRepository(string? repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? DefaultRepository
            : repository.Trim().Trim('/');
    }

    /// <summary>
    /// Queries GitHub and returns the latest GitHubRelease object, or null if the request failed.
    /// </summary>
    /// <returns>Latest GitHubRelease object, or null if the request failed</returns>
    internal async Task<GitHubRelease?> FetchLatestRelease(bool forceCheck, string? repository = null)
    {
        var shouldDownload = true;
        var releasesFile = Storage.GetReleasesPath();
        if (File.Exists(releasesFile))
        {
            var fileInfo = new FileInfo(releasesFile);
            var lastModifiedTime = DateTime.Now - fileInfo.LastWriteTime;
            if (lastModifiedTime.TotalMinutes < 30)
            {
                shouldDownload = false;

                // If we are not downloading and we are not forced to check then return the existing object.
                if (forceCheck == false)
                {
                    using (var fileStream = File.OpenRead(releasesFile))
                    {
                        var githubRelease = JsonSerializer.Deserialize(fileStream, SourceGenerationContext.Default.GitHubRelease);
                        if (githubRelease is not null)
                        {
                            return githubRelease;
                        }
                    }
                }
            }
        }

        if (shouldDownload == true || forceCheck == true)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    var fileDownloader = new FileDownloader(GetLatestReleaseApiUrl(repository), 0);
                    await fileDownloader.DownloadFileToStreamAsync(memoryStream).ConfigureAwait(false);

                    memoryStream.Position = 0;

                    var githubRelease = JsonSerializer.Deserialize(memoryStream, SourceGenerationContext.Default.GitHubRelease);
                    if (githubRelease is null)
                    {
                        throw new Exception("Could not load GitHub release data.");
                    }

                    memoryStream.Position = 0;

                    // If we did load the json, save it to disk.
                    using (var fileStream = File.Create(releasesFile))
                    {
                        await memoryStream.CopyToAsync(fileStream).ConfigureAwait(false);
                    }

                    return githubRelease;
                }
            }
            catch (Exception err)
            {
                // NOOP
                Logger.Error(err);
                return null;
            }
        }

        return null;
    }

    internal async Task<GitHubRelease?> GetReleaseFromTag(string tag)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            {
                var fileDownloader = new FileDownloader(GetReleaseByTagApiUrl(tag), 0);
                await fileDownloader.DownloadFileToStreamAsync(memoryStream).ConfigureAwait(false);
                memoryStream.Position = 0;
                var githubRelease = JsonSerializer.Deserialize(memoryStream, SourceGenerationContext.Default.GitHubRelease);
                if (githubRelease is null)
                {
                    throw new Exception("Could not load GitHub release data.");
                }

                return githubRelease;
            }
        }
        catch (Exception err)
        {
            // NOOP
            Logger.Error(err);
            Debugger.Break();
            return null;
        }
    }

    /// <summary>
    /// Queries GitHub and returns a GitHubRelease only if a newer version was detected, otherwise null
    /// </summary>
    /// <returns>GitHubRelease object if an update is available, otherwise null.</returns>
    internal async Task<GitHubRelease?> CheckForNewGitHubRelease(bool forceCheck)
    {
        var latestRelease = await FetchLatestRelease(forceCheck).ConfigureAwait(false);
        if (latestRelease is null)
        {
            return null;
        }

        var latestVersion = latestRelease.GetVersionNumber();
        var version = App.CurrentApp.GetVersion();
        var currentVersion = ((ulong)version.Major << 48) +
            ((ulong)version.Minor << 32) +
            ((ulong)version.Build << 16) +
            ((ulong)version.Revision);

        // New version is available.
        if (latestVersion > currentVersion)
        {
            return latestRelease;
        }

        return null;
    }

    /// <summary>
    /// Checks for an update and says which of the three things happened.
    /// </summary>
    /// <remarks>
    /// This is the method the UI should call. <see cref="CheckForNewGitHubRelease"/> cannot express
    /// the difference between "you are up to date" and "the check did not complete", because both are
    /// a null release, and the settings page reported that null as "No new updates available".
    ///
    /// A private repository, a renamed repository, a rate limited request and a genuine outage all land
    /// in <see cref="UpdateCheckResult.Failed"/>, and all of them previously looked like success.
    /// </remarks>
    internal async Task<(UpdateCheckResult Result, GitHubRelease? Release)> CheckForUpdateAsync(
        bool forceCheck,
        string? repository = null)
    {
        try
        {
            var release = await FetchLatestRelease(forceCheck, repository).ConfigureAwait(false);

            if (release is null)
            {
                return (UpdateCheckResult.Failed, null);
            }

            var isNewer = release.GetVersionNumber() > GetCurrentVersionNumber();

            return isNewer
                ? (UpdateCheckResult.UpdateAvailable, release)
                : (UpdateCheckResult.UpToDate, release);
        }
        catch (Exception err)
        {
            // FetchLatestRelease already swallows its own failures and returns null, so this only sees
            // something unexpected. Logged rather than shown, because the caller reports it.
            Logger.Error(err);

            return (UpdateCheckResult.Failed, null);
        }
    }

    /// <summary>
    /// The running version, packed for comparison against a release.
    /// </summary>
    /// <remarks>
    /// Extracted from CheckForNewGitHubRelease, which computed it inline. It also needed reading from
    /// a test, where App.CurrentApp is null because there is no Application, so it takes the version as
    /// a parameter rather than fetching it.
    /// </remarks>
    internal static ulong GetCurrentVersionNumber(System.Version? version = null)
    {
        version ??= App.CurrentApp.GetVersion();

        return ((ulong)version.Major << 48) +
               ((ulong)version.Minor << 32) +
               ((ulong)version.Build << 16) +
               ((ulong)version.Revision);
    }

    /// <summary>
    /// Whether a release is newer than a given version.
    /// </summary>
    /// <remarks>
    /// The comparison the update check rests on, separated so it can be tested directly. It is strictly
    /// greater than, so a release of the same version does not prompt, and it compares packed numbers
    /// so 1.10.0.0 correctly sorts above 1.9.0.0, which a string comparison would not.
    /// </remarks>
    internal static bool IsNewerThan(GitHubRelease release, System.Version currentVersion)
    {
        if (release is null)
        {
            return false;
        }

        return release.GetVersionNumber() > GetCurrentVersionNumber(currentVersion);
    }


    internal bool HasPromptedBefore(GitHubRelease gitHubRelease)
    {
        var thisVersion = gitHubRelease.GetVersionNumber();
        var lastVersionPromptedFor = Settings.Instance.LastPromptWasForVersion;

        if (lastVersionPromptedFor == 0)
        {
            return false;
        }
        else if (thisVersion > lastVersionPromptedFor)
        {
            return false;
        }

        return true;
    }

    internal async Task DisplayNewUpdateDialog(GitHubRelease gitHubRelease, XamlRoot xamlRoot)
    {
        // Update settings so we won't auto prompt for this version (or lower) ever again.
        var versionNumber = gitHubRelease.GetVersionNumber();
        if (versionNumber > Settings.Instance.LastPromptWasForVersion)
        {
            Settings.Instance.LastPromptWasForVersion = versionNumber;
        }


        var currentVerion = App.CurrentApp.GetVersionString();

        var yourVersion = ResourceHelper.GetFormattedResourceTemplate("GitHubUpdater_CurrentVersionIsActualTemplate", currentVerion);
        var contentUpdate = new MarkdownTextBlock()
        {
            Text = $"{yourVersion}\n\n{gitHubRelease.Body}",
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Config = new MarkdownConfig(),
        };

        await App.CurrentApp.RunOnUIThreadAsync(async () =>
        {
            var dialog = new EasyContentDialog(xamlRoot)
            {
                Title = $"{ResourceHelper.GetString("GitHubUpdater_UpdateAvailable")} - {gitHubRelease.Name}",
                SecondaryButtonText = ResourceHelper.GetString("GitHubUpdater_ViewUpdate"),
                DefaultButton = ContentDialogButton.Secondary,
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                Content = new ScrollViewer()
                {
                    Content = contentUpdate,
                },
            };

            GitHubReleaseAsset? installerAsset = null;

#if PORTABLE
            // A portable build has no installer to hand off to. The portable archive is downloaded,
            // verified against the digest GitHub publishes for it, and copied over the running app by
            // a detached helper once this process exits - see PortableSelfUpdate.
            installerAsset = Helpers.PortableSelfUpdate.FindPortableAsset(gitHubRelease.Assets);
#else
            // Only show the update button if we could fetch the update that is ready to install.
            foreach (var gitHubAsset in gitHubRelease.Assets)
            {
                // Check all the strings we want to use exist.
                if (string.IsNullOrWhiteSpace(gitHubAsset.Name) ||
                    string.IsNullOrWhiteSpace(gitHubAsset.ContentType) ||
                    string.IsNullOrWhiteSpace(gitHubAsset.State) ||
                    string.IsNullOrWhiteSpace(gitHubAsset.Digest))
                {
                    continue;
                }

                // Check that we are looking at a exe file.
                if (gitHubAsset.ContentType.Equals("application/x-msdownload", StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                // Check that the state is uploaded.
                if (gitHubAsset.State.Equals("uploaded", StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                // Check if we are looking at something like "DLSS.Swapper-a.b.c.d-installer.exe"
                if (gitHubAsset.Name.EndsWith("-installer.exe", StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (installerAsset is not null)
                {
                    // Something happened, we found TWO installer assets. Because we don't know what one should be used we will use none and auto-update will be disabled.
                    installerAsset = null;
                    break;
                }

                installerAsset = gitHubAsset;
            }
#endif

            // If the asset is found we add the update button and make it the primary response. For a
            // portable build that asset is the zip we will replace ourselves with; for a packaged build
            // it is the installer we will launch.
            if (installerAsset is not null)
            {
                dialog.PrimaryButtonText = ResourceHelper.GetString("General_Update");
                dialog.DefaultButton = ContentDialogButton.Primary;
            }

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && installerAsset is not null)
            {
                await DownloadAndInstallAsync(gitHubRelease, installerAsset, xamlRoot);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await Launcher.LaunchUriAsync(new Uri(gitHubRelease.HtmlUrl));
            }
        });
    }

    async Task DownloadAndInstallAsync(GitHubRelease gitHubRelease, GitHubReleaseAsset gitHubAsset, XamlRoot xamlRoot)
    {
#if PORTABLE
        await DownloadAndReplacePortableAsync(gitHubRelease, gitHubAsset, xamlRoot);
#else
        var filesProgressBar = new ProgressBar()
        {
            IsIndeterminate = true
        };
        var progressTextBlock = new TextBlock()
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        progressTextBlock.Inlines.Add(new Run()
        {
            Text = $"{ResourceHelper.GetString("GitHubUpdater_DownloadProgress")}: "
        });
        var progressRun = new Run() { Text = "-" };
        progressTextBlock.Inlines.Add(progressRun);
        var progressStackPanel = new StackPanel()
        {
            Spacing = 16,
            Orientation = Orientation.Vertical,
            Children =
            {
                filesProgressBar,
                progressTextBlock,
            }
        };




        var updatesFolder = Storage.GetUpdatesFolder();
        var tempDownloadFile = Path.Combine(updatesFolder, gitHubAsset.Name);
        if (Directory.Exists(updatesFolder) == false)
        {
            Directory.CreateDirectory(updatesFolder);
        }

        var shouldDownload = true;
        if (File.Exists(tempDownloadFile))
        {
            using (FileStream fileStream = File.OpenRead(tempDownloadFile))
            {
                var hash = fileStream.GetSha256Hash();
                if (gitHubAsset.Digest.Equals($"sha256:{hash}", StringComparison.OrdinalIgnoreCase))
                {
                    shouldDownload = false;
                }
            }
        }


        if (shouldDownload)
        {
            var cancellationTokenSource = new CancellationTokenSource();

            var downloadingDialog = new EasyContentDialog(xamlRoot)
            {
                Title = ResourceHelper.GetString("GitHubUpdater_DownloadingUpdate_Title"),
                Content = progressStackPanel,
                CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            };
            downloadingDialog.CloseButtonClick += (sender, args) =>
            {
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch (Exception)
                {
                    // NOOP
                }
            };
            _ = downloadingDialog.ShowAsync();

            var totalSizeString = ByteSize.FromBytes(gitHubAsset.Size).ToString("MB", CultureInfo.CurrentCulture);
            var fileDownloader = new FileDownloader(gitHubAsset.BrowserDownloadUrl);

            try
            {
                using (var fileStream = File.Create(tempDownloadFile))
                {
                    var downloaderTask = fileDownloader.DownloadFileToStreamAsync(fileStream, cancellationTokenSource.Token, progressCallback: (downloadedBytes, totalBytes, percent) =>
                    {
                        var displayPercent = percent * 100;
                        progressRun.Text = $"{ByteSize.FromBytes(downloadedBytes).MegaBytes.ToString("F2", CultureInfo.CurrentCulture)} / {totalSizeString} ({percent:F1}%)";
                        filesProgressBar.IsIndeterminate = false;
                        filesProgressBar.Value = percent;
                    });


                    var didDownload = await downloaderTask;
                    if (didDownload == false)
                    {
                        throw new Exception("DownloadFileToStreamAsync returned false.");
                    }

                    downloadingDialog.Hide();
                }

            }
            catch (TaskCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                // User cancelled.
                downloadingDialog.Hide();
                return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                downloadingDialog.Hide();

                var downloadErrorDialog = new EasyContentDialog(xamlRoot)
                {
                    Title = ResourceHelper.GetString("General_Error"),
                    Content = ResourceHelper.GetString("GitHubUpdater_UpdateDownloadFailed"),
                    PrimaryButtonText = ResourceHelper.GetString("GitHubUpdater_ViewUpdate"),
                    CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                var downloadErrorResult = await downloadErrorDialog.ShowAsync();
                if (downloadErrorResult == ContentDialogResult.Primary)
                {
                    await Launcher.LaunchUriAsync(new Uri(gitHubRelease.HtmlUrl));
                }

                return;
            }
        }


        var installDialog = new EasyContentDialog(xamlRoot)
        {
            Title = ResourceHelper.GetString("GitHubUpdater_DownloadComplete_Title"),
            Content = ResourceHelper.GetString("GitHubUpdater_UpdateReadyToInstall"),
            PrimaryButtonText = ResourceHelper.GetString("GitHubUpdater_Install"),
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var installDialogResult = await installDialog.ShowAsync();

        if (installDialogResult == ContentDialogResult.Primary)
        {

            var updatingDialog = new EasyContentDialog(xamlRoot)
            {
                Title = ResourceHelper.GetString("GitHubUpdater_Updating_Title"),
                Content = new ProgressRing() { IsIndeterminate = true },
            };
            _ = updatingDialog.ShowAsync();

            // Give the popup time to show.
            await Task.Delay(500);

            try
            {
                var processStartInfo = new ProcessStartInfo()
                {
                    FileName = tempDownloadFile,
                    UseShellExecute = true,
                };
                var installerProcess = Process.Start(processStartInfo);
                if (installerProcess is null)
                {
                    throw new Exception("Could not launch installer");
                }

                // Close Kronos so the installer can install
                Application.Current.Exit();
            }
            catch (Exception err)
            {
                Logger.Error(err);

                updatingDialog.Hide();

                var errorDialog = new EasyContentDialog(xamlRoot)
                {
                    Title = ResourceHelper.GetString("General_Error"),
                    Content = ResourceHelper.GetString("GitHubUpdater_CouldNotRunInstaller"),
                    PrimaryButtonText = ResourceHelper.GetString("GitHubUpdater_ViewUpdate"),
                    CloseButtonText = ResourceHelper.GetString("General_Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                };
                var errorDialogResult = await errorDialog.ShowAsync();

                if (errorDialogResult == ContentDialogResult.Primary)
                {
                    await Launcher.LaunchUriAsync(new Uri(gitHubRelease.HtmlUrl));
                }
            }
        }
#endif
    }

    /// <summary>
    /// Downloads a portable release, verifies it, and hands the replacement to a detached helper.
    /// </summary>
    /// <remarks>
    /// The verification is not optional. The helper replaces the running application with whatever it
    /// is given, so an unverified download here would be code execution with no gate at all. If
    /// GitHub has not published a digest for the asset, the update is refused rather than accepted
    /// unverified - <see cref="Helpers.PortableSelfUpdate.FindPortableAsset"/> already filters those
    /// out, and this re-checks rather than trusting one caller to have done it.
    ///
    /// Nothing is deleted until the new copy is verified and the helper is running, so a failure at
    /// any point leaves the working installation exactly as it was.
    /// </remarks>
#if PORTABLE
    async Task DownloadAndReplacePortableAsync(GitHubRelease gitHubRelease, GitHubReleaseAsset gitHubAsset, XamlRoot xamlRoot)
    {
        if (string.IsNullOrWhiteSpace(gitHubAsset.Digest))
        {
            await ReportPortableUpdateFailure(gitHubRelease, xamlRoot, "This update has no published checksum, so it cannot be verified and will not be installed.");
            return;
        }

        var updatesFolder = Storage.GetUpdatesFolder();
        Directory.CreateDirectory(updatesFolder);
        var archivePath = Path.Combine(updatesFolder, gitHubAsset.Name);

        var progressBar = new ProgressBar() { IsIndeterminate = true };
        var progressText = new TextBlock { Text = string.Empty, HorizontalAlignment = HorizontalAlignment.Left };
        progressText.Inlines.Add(new Run() { Text = $"{ResourceHelper.GetString("GitHubUpdater_DownloadProgress")}: " });
        var progressRun = new Run() { Text = "-" };
        progressText.Inlines.Add(progressRun);

        var progressPanel = new StackPanel
        {
            Spacing = 16,
            Orientation = Orientation.Vertical,
            Children = { progressBar, progressText },
        };

        var cancellation = new CancellationTokenSource();
        var progressDialog = new EasyContentDialog(xamlRoot)
        {
            Title = ResourceHelper.GetString("GitHubUpdater_DownloadingUpdate_Title"),
            Content = progressPanel,
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
        };
        progressDialog.CloseButtonClick += (sender, args) =>
        {
            try { cancellation.Cancel(); }
            catch (Exception) { /* cancellation is best effort */ }
        };
        _ = progressDialog.ShowAsync();

        try
        {
            var totalSizeString = ByteSize.FromBytes(gitHubAsset.Size).ToString("MB", CultureInfo.CurrentCulture);
            var downloader = new FileDownloader(gitHubAsset.BrowserDownloadUrl);

            using (var fileStream = File.Create(archivePath))
            {
                var downloaded = await downloader.DownloadFileToStreamAsync(
                    fileStream,
                    cancellation.Token,
                    progressCallback: (downloadedBytes, totalBytes, percent) =>
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = percent;
                        progressRun.Text = $"{ByteSize.FromBytes(downloadedBytes).MegaBytes.ToString("F2", CultureInfo.CurrentCulture)} / {totalSizeString} ({percent:F1}%)";
                    });

                if (downloaded == false)
                {
                    throw new IOException("The download did not complete.");
                }
            }

            progressDialog.Hide();

            // Verified before anything is handed off, and before the app is closed.
            string actualHash;
            using (var fileStream = File.OpenRead(archivePath))
            {
                actualHash = fileStream.GetSha256Hash();
            }

            var expected = gitHubAsset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? gitHubAsset.Digest["sha256:".Length..]
                : gitHubAsset.Digest;

            if (actualHash.Equals(expected, StringComparison.OrdinalIgnoreCase) == false)
            {
                File.Delete(archivePath);
                await ReportPortableUpdateFailure(
                    gitHubRelease,
                    xamlRoot,
                    "The download did not match the checksum GitHub published for it, so it was discarded and nothing was changed.");
                return;
            }

            var targetDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)!;

            var updatingDialog = new EasyContentDialog(xamlRoot)
            {
                Title = ResourceHelper.GetString("GitHubUpdater_Updating_Title"),
                Content = new ProgressRing() { IsIndeterminate = true },
            };
            _ = updatingDialog.ShowAsync();

            // Give the dialog a moment to appear before the window goes away.
            await Task.Delay(500);

            Helpers.PortableSelfUpdate.PrepareHandoff(archivePath, targetDirectory, relaunch: true);

            Application.Current.Exit();
        }
        catch (TaskCanceledException) when (cancellation.IsCancellationRequested)
        {
            progressDialog.Hide();
            TryDeleteQuietly(archivePath);
        }
        catch (Exception err)
        {
            Logger.Error(err);
            progressDialog.Hide();
            TryDeleteQuietly(archivePath);

            await ReportPortableUpdateFailure(
                gitHubRelease,
                xamlRoot,
                $"{ResourceHelper.GetString("GitHubUpdater_UpdateDownloadFailed")}\n\n{err.Message}");
        }
    }

    async Task ReportPortableUpdateFailure(GitHubRelease gitHubRelease, XamlRoot xamlRoot, string message)
    {
        var dialog = new EasyContentDialog(xamlRoot)
        {
            Title = ResourceHelper.GetString("General_Error"),
            Content = message,
            PrimaryButtonText = ResourceHelper.GetString("GitHubUpdater_ViewUpdate"),
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Launcher.LaunchUriAsync(new Uri(gitHubRelease.HtmlUrl));
        }
    }

    static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover download is not worth failing an update over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
#endif

    internal async Task DisplayWhatsNewDialog(GitHubRelease gitHubRelease, XamlRoot xamlRoot)
    {
        var contentUpdate = new MarkdownTextBlock()
        {
            Text = gitHubRelease.Body,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Config = new MarkdownConfig(),
        };

        var dialog = new EasyContentDialog(xamlRoot)
        {
            Title = $"{ResourceHelper.GetString("GitHubUpdater_DlssSwapperUpdated")} - {gitHubRelease.Name}",
            CloseButtonText = ResourceHelper.GetString("General_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer()
            {
                Content = contentUpdate,
            },
        };
        await dialog.ShowAsync();
    }
}
