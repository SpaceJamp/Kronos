using System;
using Kronos.Helpers;
using Microsoft.UI.Xaml;

namespace Kronos;

/// <summary>
/// Shown instead of the main window when Windows is older than this build supports.
/// </summary>
/// <remarks>
/// Deliberately has no ViewModel and no translation properties, for the same reason as
/// <see cref="UnsupportedArchitectureWindow"/>: a single fixed explanation, shown once, in a situation
/// where there is nothing to configure and nothing useful to copy into a bug report.
///
/// The machine's own version is interpolated rather than omitted. "Your Windows is too old" leaves the
/// reader to go and find out how old; "you are on 10.0.19042" lets them act on it.
/// </remarks>
public sealed partial class UnsupportedWindowsVersionWindow : Window
{
    public UnsupportedWindowsVersionWindow()
    {
        this.InitializeComponent();

        var os = Environment.OSVersion.Version;

        RequirementText.Text =
            $"Kronos needs {WindowsVersionSupport.MinimumSupportedDisplay}. " +
            $"This machine is running Windows {os.Major}.{os.Minor}.{os.Build}.";
    }
}
