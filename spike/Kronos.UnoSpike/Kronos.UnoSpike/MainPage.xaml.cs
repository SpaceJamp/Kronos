using Microsoft.UI.Xaml;

namespace Kronos.UnoSpike;

public sealed partial class MainPage : Page
{
    readonly WindowingProbe _windowingProbe = new();

    public MainPage()
    {
        InitializeComponent();

        // Phase 3: dialogs and images. Needs a rendered page - a ContentDialog needs a XamlRoot and an
        // image only opens once it is in a live tree - so unlike the windowing probe it runs on Loaded.
        // Registered first: the handler below throws (see App.LaunchedWindow), and would stop this one.
        Loaded += async (_, _) => await RunDialogsAndImagesProbeAsync();

        // Loaded event rather than an OnLoaded override. WinUI's Page has a Loaded event; Uno's Page does
        // not expose an overridable OnLoaded, so the override compiles nowhere. Worth knowing, because
        // "is this an override or an event" is the sort of difference that is invisible until it fails.
        Loaded += (_, _) =>
        {
            _windowingProbe.Run(App.Instance.MainWindowForProbe);
            WriteReport();
        };

        // Kronos sets DataContext to its view model in the page constructor and binds with x:Bind, which
        // reads the property off the page rather than off DataContext. Set here for the same reason, so
        // the probe exercises the arrangement the real pages use rather than one they never take.
        ViewModel = new ProbeViewModel();
        DataContext = ViewModel;
    }

    /// <summary>The page's view model, which is what every x:Bind on this page resolves against.</summary>
    public ProbeViewModel ViewModel { get; }

    /// <summary>
    /// Writes the probe's findings to a file, if one was asked for.
    /// </summary>
    /// <remarks>
    /// To a file rather than only on screen, because the thing standing between this probe and a
    /// readable result is that the environment has no compositor: the app starts, but nothing is ever
    /// presented. The answers do not need a window to be produced, only to be read.
    /// </remarks>
    async Task RunDialogsAndImagesProbeAsync()
    {
        var path = Environment.GetEnvironmentVariable("KRONOS_PHASE3_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var probe = new DialogsAndImagesProbe { ProgressPath = path + ".progress" };
        File.WriteAllText(probe.ProgressPath, "loaded" + Environment.NewLine);
        var host = (Content as ScrollViewer)?.Content as Panel
            ?? throw new InvalidOperationException("MainPage's content is no longer a ScrollViewer around a Panel.");

        await probe.RunAsync(App.LaunchedWindow ?? throw new InvalidOperationException("No launched window."), this, host);

        File.WriteAllText(path, string.Join("\n", probe.Report));
        Application.Current.Exit();
    }

    void WriteReport()
    {
        var path = Environment.GetEnvironmentVariable("KRONOS_PROBE_OUTPUT");

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        File.WriteAllText(path, string.Join("\n", _windowingProbe.Report));
    }
}

