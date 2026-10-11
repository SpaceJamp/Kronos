using System;
using Microsoft.UI.Xaml;

namespace Kronos;

/// <summary>
/// A standalone window showing an unhandled exception.
/// </summary>
/// <remarks>
/// Its own window rather than a ContentDialog because it cannot assume anything about the app's state. By
/// the time an unhandled exception reaches here there may be no page, no XamlRoot and no content - a
/// ContentDialog constructed without one throws, which would replace the real error with a confusing one.
///
/// Deliberately not modal-blocking and deliberately not asking anything. Its job is to get the information
/// in front of the user while leaving the rest of the app alone.
/// </remarks>
public sealed partial class CrashWindow : Window
{
    public CrashWindowModel ViewModel { get; private set; }

    public CrashWindow(Exception exception)
    {
        ViewModel = new CrashWindowModel(exception);

        this.InitializeComponent();

        Closed += OnCurrentWindowClosed;
    }

    private void OnCurrentWindowClosed(object sender, WindowEventArgs args)
    {
        ViewModel.TranslationProperties.Dispose();
    }
}