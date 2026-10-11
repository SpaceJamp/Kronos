using System;
using Microsoft.Extensions.Logging;
using Uno.Resizetizer;

namespace Kronos.UnoSpike;

public partial class App : Application
{
    /// <summary>
    /// The running application.
    /// </summary>
    /// <remarks>
    /// Uno's <c>Application</c> does not expose a static <c>Current</c> the way WinAppSDK's does, so the
    /// probe reaches the instance through here rather than through the usual singleton. Another concrete
    /// difference between the two, and one Kronos uses in a lot of places via <c>App.CurrentApp</c>.
    /// </remarks>
    public static App Instance { get; } = new();

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        this.InitializeComponent();
    }

    protected Window? MainWindow { get; private set; }

    /// <summary>
    /// The main window, for the windowing probe.
    /// </summary>
    /// <remarks>
    /// Handed over rather than looked up. There is no <c>Application.Windows</c> collection on Uno - the
    /// probe's first attempt walked one and the compiler said so - so the only reliable way to reach the
    /// window is to keep the reference App already has.
    ///
    /// A real migration wants this anyway: Kronos's WindowManager keeps its own list of windows, so the
    /// pattern transfers rather than being probe-specific scaffolding.
    /// </remarks>
    public Window MainWindowForProbe => MainWindow
        ?? throw new InvalidOperationException("OnLaunched has not run yet, so there is no window.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window();
#if DEBUG
        MainWindow.UseStudio();
#endif


        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (MainWindow.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            // Place the frame in the current Window
            MainWindow.Content = rootFrame;

            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            // When the navigation stack isn't restored navigate to the first page,
            // configuring the new page by passing required information as a navigation
            // parameter
            rootFrame.Navigate(typeof(MainPage), args.Arguments);
        }

        MainWindow.SetWindowIcon();
        // Ensure the current window is active
        MainWindow.Activate();

        // The probe runs here rather than on the page's Loaded event.
        //
        // Loaded needs a rendered page, and this environment cannot render one: WSLg starts weston when a
        // GUI app launches, the window associates, and then there is no RDP client to present it to - so
        // the page never loads and a Loaded-based probe silently produces nothing. An earlier run of this
        // returned exit code 124 after 45 seconds with an empty log, which looked like the app working and
        // was actually the app never reaching the code.
        //
        // The windowing API does not need a loaded page. The Window exists the moment it is constructed,
        // so the checks belong next to it.
        RunWindowingProbe(MainWindow);
    }

    /// <summary>Runs the phase 0 windowing probe and records what it found.</summary>
    void RunWindowingProbe(Window window)
    {
        var probe = new WindowingProbe();

        try
        {
            probe.Run(window);
        }
        catch (Exception err)
        {
            // A probe that throws has still answered something, and swallowing it would hide which
            // capability broke rather than which one was missing.
            probe.Record("probe completed without throwing", false, err.GetType().Name + ": " + err.Message);
        }
        var path = Environment.GetEnvironmentVariable("KRONOS_PROBE_OUTPUT");

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        File.WriteAllText(path, string.Join("\n", probe.Report));

        // Exit once the answer is on disk. Without this the process sits there until a timeout kills it,
        // which makes every run take 45 seconds and hides the fact that it already finished.
        Exit();
    }

    /// <summary>
    /// Invoked when Navigation to a certain page fails
    /// </summary>
    /// <param name="sender">The Frame which failed navigation</param>
    /// <param name="e">Details about the navigation failure</param>
    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    /// <summary>
    /// Configures global Uno Platform logging
    /// </summary>
    public static void InitializeLogging()
    {
#if DEBUG
        // Logging is disabled by default for release builds, as it incurs a significant
        // initialization cost from Microsoft.Extensions.Logging setup. If startup performance
        // is a concern for your application, keep this disabled. If you're running on the web or
        // desktop targets, you can use URL or command line parameters to enable it.
        //
        // For more performance documentation: https://platform.uno/docs/articles/Uno-UI-Performance.html

        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());

            // Log to the Visual Studio Debug console
            builder.AddConsole();
#else
            builder.AddConsole();
#endif

            // Exclude logs below this level
            builder.SetMinimumLevel(LogLevel.Information);

            // Default filters for Uno Platform namespaces
            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);

            // Generic Xaml events
            // builder.AddFilter("Microsoft.UI.Xaml", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.VisualStateGroup", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.StateTriggerBase", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.UIElement", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.FrameworkElement", LogLevel.Trace );

            // Layouter specific messages
            // builder.AddFilter("Microsoft.UI.Xaml.Controls", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Controls.Layouter", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Controls.Panel", LogLevel.Debug );

            // builder.AddFilter("Windows.Storage", LogLevel.Debug );

            // Binding related messages
            // builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );
            // builder.AddFilter("Microsoft.UI.Xaml.Data", LogLevel.Debug );

            // Binder memory references tracking
            // builder.AddFilter("Uno.UI.DataBinding.BinderReferenceHolder", LogLevel.Debug );

            // DevServer and HotReload related
            // builder.AddFilter("Uno.UI.RemoteControl", LogLevel.Information);

            // Debug JS interop
            // builder.AddFilter("Uno.Foundation.WebAssemblyRuntime", LogLevel.Debug );
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
