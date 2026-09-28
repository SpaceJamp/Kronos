using Microsoft.UI.Xaml;

namespace Kronos;

/// <summary>
/// Shown instead of the main window when the operating system cannot run this build, which in
/// practice means 32-bit Windows.
/// </summary>
/// <remarks>
/// Deliberately has no ViewModel and no translation properties. It is a single fixed explanation
/// shown once, in the one situation where there is nothing for the user to configure and nothing to
/// copy to a bug report, and adding a resource string per language for a message that only ever
/// appears on hardware that cannot run the app anyway would be noise.
/// </remarks>
public sealed partial class UnsupportedArchitectureWindow : Window
{
    public UnsupportedArchitectureWindow()
    {
        this.InitializeComponent();
    }
}
