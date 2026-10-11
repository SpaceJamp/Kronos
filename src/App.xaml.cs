using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI;
using Kronos.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kronos;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public sealed partial class App : Application
{
    public ElementTheme GlobalElementTheme { get; set; }

    MainWindow? _mainWindow;
#pragma warning disable CS8603 // Possible null reference return.
    public MainWindow MainWindow => _mainWindow;
#pragma warning restore CS8603 // Possible null reference return.
    public WindowManager WindowManager { get; } = new WindowManager();

    public static App CurrentApp => (App)Application.Current;

    /// <summary>
    /// The shared HTTP client. Delegates to <see cref="Http.Client"/> so callers outside the UI
    /// layer - the file downloader, the cover URL resolver - do not have to reach through
    /// <c>App.CurrentApp</c> to get it, which is what previously stopped them compiling for Linux.
    /// </summary>
    public HttpClient HttpClient => Http.Client;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Logger.Init();

        var language = Settings.Instance.Language;

        // Language is not set, try to fetch from system.
        if (string.IsNullOrWhiteSpace(language))
        {
            // Try the language of the current thread.
            var currentLauguage = Thread.CurrentThread.CurrentCulture.Name;
            var knownLanguages = LanguageManager.Instance.GetKnownLanguages();
            foreach (var knownLanguage in knownLanguages)
            {
                if (string.Equals(currentLauguage, knownLanguage, StringComparison.InvariantCultureIgnoreCase))
                {
                    language = knownLanguage;
                    break;
                }
            }

            // TODO: Can we fallback to other languages? eg. Is fr-CA acceptable to fallback to fr-FR or does the app just default back to en-US?
        }

        // If we failed to fetch the users language, default to en-US.
        if (string.IsNullOrWhiteSpace(language))
        {
            language = "en-US";
        }
        Settings.Instance.Language = language;

        LanguageManager.Instance.ChangeLanguage(language);

        UnhandledException += App_UnhandledException;

        GlobalElementTheme = Settings.Instance.AppTheme;

        this.InitializeComponent();
    }

    internal void RegenerateHttpClient()
        => Http.Regenerate();

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Serilog.Log.Error(e.Exception, "UnhandledException");
        Serilog.Log.CloseAndFlush();
    }

    /// <summary>
    /// Invoked when the application is launched normally by the end user.  Other entry points
    /// will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // If this is the first instance launched, then register it as the "main" instance.
        // If this isn't the first instance launched, then "main" will already be registered,
        // so retrieve it.
        var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("main");

        // If the instance that's executing the OnLaunched handler right now
        // isn't the "main" instance.
        if (mainInstance.IsCurrent == false)
        {
            // Redirect the activation (and args) to the "main" instance, and exit.
            var activatedEventArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activatedEventArgs);
            Process.GetCurrentProcess().Kill();
            return;
        }

        // The floor is Windows 10 22H2. Checked here, before anything touches the disk, the database or
        // the network, and before the architecture check below - an out-of-date Windows is worth
        // explaining whether or not it is also 32-bit.
        //
        // Fails closed via WindowsVersionSupport: an unreadable version is refused, not allowed. A
        // check that can be defeated by making the version report as something unexpected is not a
        // check, and the alternative to failing closed is a launch path with a hole in it.
        if (WindowsVersionSupport.IsSupportedCurrentMachine() == false)
        {
            Logger.Error(WindowsVersionSupport.DescribeRefusal(Environment.OSVersion.Version));
            WindowManager.ShowWindow(new UnsupportedWindowsVersionWindow());
            return;
        }

        // This build is x64 only. A 32-bit Windows cannot load it, and the failure it produces is
        // an unhelpful loader error before any of our code runs, so say something useful instead of
        // letting the user stare at "This app can't run on your PC". Checked before anything else
        // touches the disk, database or network.
        if (Environment.Is64BitOperatingSystem == false)
        {
            Logger.Error("Refusing to launch: this is an x64 only build and the operating system is 32-bit.");
            WindowManager.ShowWindow(new UnsupportedArchitectureWindow());
            return;
        }

        if (Storage.StoragePath.Trim(Path.DirectorySeparatorChar).Contains(Environment.SystemDirectory, StringComparison.InvariantCultureIgnoreCase))
        {
            var failToLaunchWindow = new FailToLaunchWindow();
            WindowManager.ShowWindow(failToLaunchWindow);
            return;
        }

        var version = GetVersion();
        var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        Logger.Info($"App launch - v{versionString}", null);
        Logger.Info($"StoragePath: {Storage.StoragePath}");

        // Check if its the first launch of the app from a new version.
        var lastLaunchVersion = Settings.Instance.LastLaunchVersion;
        if (lastLaunchVersion != versionString)
        {
            try
            {
                var manifestPath = Storage.GetManifestPath();
                if (File.Exists(manifestPath))
                {
                    var fileInfo = new FileInfo(manifestPath);
                    using (var staticManifestStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kronos.Assets.static_manifest.json"))
                    {
                        if (staticManifestStream is not null)
                        {
                            // If the static manifest is larger than the file, we likely want to replace the current manifest.
                            if (staticManifestStream.Length >= fileInfo.Length)
                            {
                                using (var fileWriter = File.Create(manifestPath))
                                {
                                    // No length check here. It used to read fileWriter.Length into an
                                    // unused local, which was always 0 because File.Create has
                                    // already truncated the file by that point, and the size
                                    // comparison that actually matters is the one above against
                                    // fileInfo, taken before anything was opened for writing.
                                    staticManifestStream.CopyTo(fileWriter);
                                }
                            }
                        }
                    }
                }

                Settings.Instance.LastLaunchVersion = versionString;
            }
            catch (Exception err)
            {
                Logger.Error(err, "Unable to perform first launch duties.");
            }
        }

        Database.Instance.Init();

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
        }
        WindowManager.ShowWindow(_mainWindow);

#if !PORTABLE
        // No need to calculate this for portable app.
        var calculateInstallSizeThread = CreateInstallSizeThread(CalculateInstallSize);
        calculateInstallSizeThread.Start();
#endif

        // Delete updates folder
        var updatesFolder = Storage.GetUpdatesFolder();
        if (Directory.Exists(updatesFolder))
        {
            try
            {
                Directory.Delete(updatesFolder, true);
            }
            catch (Exception err)
            {
                // If we failed 
                Logger.Error(err);
            }
        }
    }

#if !PORTABLE
    void CalculateInstallSize()
    {
        try
        {
            // An elevated process gets a *different* HKEY_CURRENT_USER: the administrator's hive,
            // not the user's. This app explicitly offers to relaunch as administrator whenever it
            // cannot write to a game folder, so that path is easy to reach, and the write then
            // lands in someone else's registry instead of the one the install is registered in.
            // The size is a nicety, so skip it rather than corrupt another account's entry.
            if (IsAdminUser())
            {
                Logger.Verbose("Skipping the install size update because this is an elevated process, which would write to a different user's registry hive.");
                return;
            }

            long installSize = 0;
            installSize += CalculateDirectorySize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kronos"));

            // Find this install's own uninstall entry by looking for it, rather than assuming a name
            // and a hive. All three of the obvious assumptions were wrong:
            //
            //  * The subkey name is the installer's choice, not ours. The NSIS installer wrote
            //    ...\Uninstall\Kronos. Inno Setup writes ...\Uninstall\{AppId}_is1, confirmed against
            //    a real install, where it came out as {64E9E8E5-...}_is1. A hard coded name silently
            //    stops matching the moment the installer changes.
            //
            //  * The hive depends on the scope the installer ran at: per user goes to HKCU, all
            //    users to HKLM.
            //
            //  * A 32-bit installer writes to the WOW6432Node view. The previous NSIS script was a
            //    32-bit program, so an install made by it can sit in the 32-bit view while this
            //    64-bit app reads the 64-bit one. Both views are searched.
            var entry = FindInstallUninstallEntry();
            if (entry is null)
            {
                return;
            }

            using var key = OpenUninstallEntry(entry.Value, false);
            if (key is not null)
            {
                var installLocation = key.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(installLocation) == false && Directory.Exists(installLocation) == true)
                {
                    installSize += CalculateDirectorySize(installLocation);
                }

                if (installSize <= 0)
                {
                    return;
                }

                var installSizeKB = (int)(installSize / 1000);
                var existingSize = key.GetValue("EstimatedSize") as int?;

                // HKLM is writable only by an elevated process, and this one deliberately runs
                // unelevated: the IsAdminUser check at the top of this method already returned if it
                // was, because an elevated process's HKEY_CURRENT_USER is the administrator's hive
                // rather than the user's, so a write would land in someone else's account.
                //
                // So there is deliberately no "|| IsAdminUser()" here. It would always be false by
                // this point, and it read as though elevated HKLM writes were supported. They are not,
                // and adding them back would reintroduce the cross account write the early return
                // exists to prevent.
                var hiveIsWritable = entry.Value.Hive == Microsoft.Win32.RegistryHive.CurrentUser;
                if (ShouldWriteInstallSize(existingSize, installSizeKB, hiveIsWritable) == false)
                {
                    return;
                }

                using var writeKey = OpenUninstallEntry(entry.Value, true);
                writeKey?.SetValue("EstimatedSize", installSizeKB, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
    }

    /// <summary>
    /// Builds the thread that measures the install size, wrapping the given work.
    /// </summary>
    /// <remarks>
    /// The work is passed in and this is static so that <see cref="Thread.IsBackground"/> can be
    /// asserted in a test. It cannot be an instance method reached through <c>App.CurrentApp</c>,
    /// because that is <c>(App)Application.Current</c> and is null in a test process.
    ///
    /// The thread has to be a background one. The work walks the whole install directory, 642 files
    /// and roughly 300 MB on a real install, so it is easily still running when the user closes the
    /// window. A foreground thread, which is what <see cref="Thread"/> defaults to, keeps the
    /// process alive until it finishes, so closing Kronos appeared to hang. Nothing else in the
    /// codebase catches that, so this method exists mainly to be pinned by a test.
    /// </remarks>
    internal static Thread CreateInstallSizeThread(ThreadStart work)
    {
        return new Thread(work)
        {
            IsBackground = true,
            Name = "Kronos install size measurement",
        };
    }

    /// <summary>
    /// Root of the Windows uninstall entries, which is where Apps &amp; features reads from.
    /// </summary>
    internal const string UninstallKeyRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// The DisplayName this product is registered under, matched by <see cref="IsThisProduct"/>.
    /// </summary>
    internal const string ProductDisplayName = "Kronos";

    /// <summary>
    /// Identifies one uninstall entry: the hive and registry view it lives in, plus its subkey.
    /// </summary>
    internal readonly record struct UninstallEntryRef(
        Microsoft.Win32.RegistryHive Hive,
        Microsoft.Win32.RegistryView View,
        string SubKey);

    /// <summary>
    /// Hives and registry views searched for the uninstall entry, in the order they are tried.
    /// </summary>
    /// <remarks>
    /// HKCU is searched first because that is where a per user install is registered, and it is the
    /// only one of the four an unelevated process can write to. The 64-bit view of each is tried
    /// before the 32-bit one for the same reason.
    /// </remarks>
    internal static readonly (Microsoft.Win32.RegistryHive Hive, Microsoft.Win32.RegistryView View)[] SearchedHives =
    [
        (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64),
        (Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry32),
        (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64),
        (Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32),
    ];

    /// <summary>
    /// Locates this install's own uninstall entry, or null when it is not registered anywhere.
    /// </summary>
    /// <remarks>
    /// Identifies the entry by its DisplayName. See the remarks on <c>CalculateInstallSize</c> for
    /// why neither the subkey name, the hive, nor the registry view can be assumed.
    /// </remarks>
    internal static UninstallEntryRef? FindInstallUninstallEntry()
    {
        foreach (var (hive, view) in SearchedHives)
        {
            try
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                using var root = baseKey.OpenSubKey(UninstallKeyRoot);
                if (root is null)
                {
                    continue;
                }

                foreach (var subKey in root.GetSubKeyNames())
                {
                    using var candidate = root.OpenSubKey(subKey);
                    if (IsThisProduct(candidate?.GetValue("DisplayName") as string))
                    {
                        return new UninstallEntryRef(hive, view, subKey);
                    }
                }
            }
            catch (Exception err)
            {
                // One unreadable hive or view must not stop the others being searched.
                Logger.Error(err);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an uninstall entry's DisplayName identifies this product.
    /// </summary>
    /// <remarks>
    /// Inno Setup appends a version, and wrote "Kronos version 1.45" on a real install, while the
    /// NSIS installer wrote a bare "Kronos". Only those two forms are accepted.
    ///
    /// This deliberately does not match on a plain prefix. A test in this project asserted that
    /// "Kronos Manager" must not match, and it failed, because "Kronos " is a prefix of it. The
    /// version allowance rests on how one installer happens to format its DisplayName, which is not
    /// a contract, so a loose match would risk reading and rewriting a different product's uninstall
    /// entry. Two words is narrow enough to be safe and wide enough to cover both installers.
    /// </remarks>
    internal static bool IsThisProduct(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        var trimmed = displayName.Trim();
        if (string.Equals(trimmed, ProductDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return trimmed.StartsWith(ProductDisplayName + " version", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opens a previously located uninstall entry for reading or writing.
    /// </summary>
    internal static Microsoft.Win32.RegistryKey? OpenUninstallEntry(UninstallEntryRef entry, bool writable)
    {
        // The base key has to be disposed, and it cannot be disposed here, because the subkey
        // returned below is a child of it and becomes invalid once the parent closes. That is why
        // this returns the subkey rather than opening it for the caller: the caller owns the
        // lifetime via its own using.
        //
        // The previous version left the base key undisposed, leaking a registry handle on every
        // launch, twice over, since this is called for the read and again for the write.
        using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(entry.Hive, entry.View);
        return baseKey.OpenSubKey($@"{UninstallKeyRoot}\{entry.SubKey}", writable);
    }

    /// <summary>
    /// Picks the hive that holds this install's uninstall entry, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Kept alongside <see cref="FindInstallUninstallEntry"/> because the writability rule depends on
    /// which hive was found, and pinning the preference is worth a test of its own.
    /// </remarks>
    internal static Microsoft.Win32.RegistryHive? SelectInstallRegistryHive(bool perUserExists, bool allUsersExists)
    {
        // HKCU is preferred when both somehow exist, because it is the one an unelevated process can
        // actually write back to.
        if (perUserExists)
        {
            return Microsoft.Win32.RegistryHive.CurrentUser;
        }

        if (allUsersExists)
        {
            return Microsoft.Win32.RegistryHive.LocalMachine;
        }

        return null;
    }    /// <summary>
    /// Whether the freshly measured size is worth writing back to the uninstall entry.
    /// </summary>
    /// <remarks>
    /// Extracted so the rules can be tested: skip an unchanged value, and skip a hive this process
    /// has no rights to write. Writing unconditionally is what made the value churn in Apps &amp;
    /// features on every launch.
    /// </remarks>
    internal static bool ShouldWriteInstallSize(int? existingSizeKB, int newSizeKB, bool hiveIsWritable)
    {
        if (hiveIsWritable == false)
        {
            return false;
        }

        return existingSizeKB != newSizeKB;
    }
#endif

    /// <summary>
    /// Total size of every file under a directory, in bytes. Returns 0 for a path that does not
    /// exist.
    /// </summary>
    /// <remarks>
    /// Outside the #if !PORTABLE block on purpose, even though its only caller is inside it. It is a
    /// pure function of the filesystem with nothing portable specific about it, and keeping it behind
    /// the guard meant it did not exist in the portable configurations, so the tests that reference it
    /// could not compile there.
    /// <br/><br/>
    /// Deliberately walks one directory at a time instead of
    /// <c>EnumerateFiles("*", SearchOption.AllDirectories)</c>. The all directories overload throws
    /// the moment it meets a subdirectory it cannot read, and that exception propagated all the way
    /// out of here, abandoning the whole calculation. A single locked folder in the image cache or an
    /// unreadable directory under Program Files therefore stopped EstimatedSize being updated at all,
    /// permanently and silently. Now one bad directory only costs its own files.
    /// </remarks>
    internal static long CalculateDirectorySize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path) == false)
        {
            return 0L;
        }

        long directorySize = 0L;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] files;
            string[] subdirectories;
            try
            {
                files = Directory.GetFiles(current);
                subdirectories = Directory.GetDirectories(current);
            }
            catch (Exception err)
            {
                Logger.Error(err, $"Could not read directory \"{current}\" while measuring the install size. Its contents are excluded from the total.");
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    directorySize += new FileInfo(file).Length;
                }
                catch (Exception err)
                {
                    // A file deleted or locked between listing and measuring is not worth failing over.
                    Logger.Verbose($"Could not measure \"{file}\" while calculating the install size: {err.Message}");
                }
            }

            foreach (var subdirectory in subdirectories)
            {
                pending.Push(subdirectory);
            }
        }

        return directorySize;
    }

    public bool IsAdminUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void RestartAsAdmin()
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Assembly.GetExecutingAssembly().GetName().Name,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
            Logger.Info("Restarting as admin.");
        }
        catch (Win32Exception)
        {
            Logger.Warning("User refused the elevation.");
            return;
        }

        App.CurrentApp.Exit();
    }

    /*
    // Disabled as I am unsure how to prompt to run as admin.
    internal void RelaunchAsAdministrator()
    {
        //var currentExe = Process.GetCurrentProcess().MainModule.FileName;

        //var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        //executingAssembly.FullName;
        
        // So this does prompt UAC, this was temporarily used to copy files in UpdateDll and ResetDll
        // but it would prompt for every action. 
        //var startInfo = new ProcessStartInfo()
        //{
        //    WindowStyle = ProcessWindowStyle.Hidden,
        //    FileName = "cmd.exe",
        //    Arguments = $"/C copy \"{dll}\" \"{targetDllPath}\"",
        //    UseShellExecute = true,
        //    Verb = "runas",
        //};
        //Process.Start(startInfo);

        MainWindow.Close();
        //Logger.Error(System.Reflection.Assembly.GetExecutingAssembly().Location);
    }
    */

    public Version GetVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version();
    }

    public string GetVersionString()
    {
        var version = GetVersion();
        if (version.Build == 0 && version.Revision == 0)
        {
            return $"{version.Major}.{version.Minor}";
        }
        else if (version.Revision == 0)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    /// <summary>
    /// Gets the dispatcher queue of the main window, if it is available.
    /// </summary>
    DispatcherQueue? _dispatcherQueue => _mainWindow?.DispatcherQueue;

    public bool RunOnUIThread(Action action)
    {
        var dispatcherQueue = _dispatcherQueue;

        // HasThreadAccess is the supported way to test for the UI thread. Testing
        // Environment.CurrentManagedThreadId == 1 is unreliable for unpackaged apps and
        // caused us to marshal asynchronously even when we were already on the UI thread.
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        if (dispatcherQueue.TryEnqueue(new DispatcherQueueHandler(action)))
        {
            return true;
        }

        // TryEnqueue failed. Previously this was swallowed (and the action was never run),
        // which left objects such as Game.Processing stuck at true forever. Running inline is
        // the best remaining option; there is no UI left to marshal to at this point.
        Logger.Error("Could not enqueue action to the UI thread. Running it inline instead.");
        action();
        return false;
    }


    public Task RunOnUIThreadAsync(Func<Task> function)
    {
        var dispatcherQueue = _dispatcherQueue;

        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            return function();
        }

        // Never silently drop the callback. Callers use this in finally blocks to reset state
        // (e.g. Game.Processing), so dropping it would permanently break the game entry.
        try
        {
            return dispatcherQueue.EnqueueAsync(function);
        }
        catch (Exception err)
        {
            Logger.Error(err, "Could not enqueue async function to the UI thread. Running it inline instead.");
            return function();
        }
    }

}
