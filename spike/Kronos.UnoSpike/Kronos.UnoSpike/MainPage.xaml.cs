namespace Kronos.UnoSpike;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();

        // Kronos sets DataContext to its view model in the page constructor and binds with x:Bind, which
        // reads the property off the page rather than off DataContext. Set here for the same reason, so
        // the probe exercises the arrangement the real pages use rather than one they never take.
        ViewModel = new ProbeViewModel();
        DataContext = ViewModel;
    }

    /// <summary>The page's view model, which is what every x:Bind on this page resolves against.</summary>
    public ProbeViewModel ViewModel { get; }
}
