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

    public HttpClient HttpClient { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        Logger.Init();

        HttpClient = GenerateNewHttpClient();

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
    {
        HttpClient = GenerateNewHttpClient();
    }


    HttpClient GenerateNewHttpClient()
    {
        // Setup HttpClient.
        var version = GetVersion();
        var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        var httpClientHandler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
        };

        Settings.ProxySettings.LoadIfNeeded();

        if (string.IsNullOrWhiteSpace(Settings.ProxySettings.Server) == false)
        {
            try
            {
                var server = Settings.ProxySettings.Server;
                if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var proxy = new WebProxy
                    {
                        BypassProxyOnLocal = false,
                        UseDefaultCredentials = false,
                        Address = new Uri(server),
                    };

                    if (string.IsNullOrWhiteSpace(Settings.ProxySettings.Username) == false && string.IsNullOrWhiteSpace(Settings.ProxySettings.Password) == false)
                    {
                        proxy.Credentials = new NetworkCredential(Settings.ProxySettings.Username, Settings.ProxySettings.Password);
                    }

                    httpClientHandler.UseProxy = true;
                    httpClientHandler.Proxy = proxy;
                }
                else
                {
                    Logger.Error($"Tried to set proxy with server address \"{server}\"");
                }               
            }
            catch (Exception ex)
            {
                Logger.Error("Unable to set proxy for HttpClient");
                Logger.Error(ex);
            }
        }

        var newHttpClient = new HttpClient(httpClientHandler);
        newHttpClient.DefaultRequestHeaders.Add("User-Agent", $"dlss-swapper/{versionString}");
        newHttpClient.Timeout = TimeSpan.FromMinutes(30);
        newHttpClient.DefaultRequestVersion = new Version(2, 0);
        newHttpClient.DefaultRequestHeaders.ConnectionClose = true;
        return newHttpClient;
    }


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
                                    var length = fileWriter.Length;
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
        var calculateInstallSizeThread = new Thread(CalculateInstallSize);
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

            using (var kronosRegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kronos", true))
            {
                var installLocation = kronosRegistryKey?.GetValue("InstallLocation") as string;
                if (string.IsNullOrEmpty(installLocation) == false && Directory.Exists(installLocation) == true)
                {
                    installSize += CalculateDirectorySize(installLocation);
                }

                if (installSize <= 0)
                {
                    return;
                }

                var installSizeKB = (int)(installSize / 1000);

                // Only write when it actually changed. This ran on every single launch and wrote
                // unconditionally, so the value in Apps & features churned on every start and the
                // registry saw a write for a number that almost never moved.
                if (kronosRegistryKey is not null &&
                    kronosRegistryKey.GetValue("EstimatedSize") is int existingSize &&
                    existingSize == installSizeKB)
                {
                    return;
                }

                kronosRegistryKey?.SetValue("EstimatedSize", installSizeKB, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
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
