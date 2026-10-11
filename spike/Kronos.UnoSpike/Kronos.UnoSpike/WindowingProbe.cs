using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Kronos.UnoSpike;

/// <summary>
/// Phase 0 probe: can Uno express what <c>WindowManager</c> does?
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the responsibilities of <c>src/WindowManager.cs</c> (243 lines) and
/// <c>src/Data/WindowPositionRect.cs</c> one for one, so a compile answers the question rather than an
/// opinion. Nothing here is production code; the spike's job is to fail loudly and early on anything
/// that would turn out to be a redesign in month three.
/// </para>
/// <para>
/// The single most useful thing found so far: Uno's <see cref="Window"/> exposes <c>AppWindow</c>
/// directly. WinAppSDK's own <c>Window</c> does not - <c>WindowManager.GetAppWindowForWindow</c> exists
/// precisely because of that, and goes through <c>WindowNative.GetWindowHandle</c> and
/// <c>Win32Interop</c> to get there. On Uno that whole HWND chain collapses to one property.
/// </para>
/// </remarks>
public partial class WindowingProbe
{
    readonly List<string> _report = new();

    /// <summary>What the probe managed to do, and what it could not. Rendered into the page.</summary>
    public IReadOnlyList<string> Report => _report;

    void Note(string capability, bool supported, string detail = "")
    {
        _report.Add($"{(supported ? "YES" : "NO ")}  {capability}{(detail.Length > 0 ? "  -  " + detail : "")}");
    }

    /// <summary>Records an outcome from outside a check, such as an exception that ended the run.</summary>
    public void Record(string capability, bool supported, string detail)
    {
        _report.Add($"{(supported ? "YES" : "NO ")}  {capability}{(detail.Length > 0 ? "  -  " + detail : "")}");
    }

    /// <summary>Runs every check. Called once the page is loaded, so the Window exists.</summary>
    public void Run(Window window)
    {
        _report.Clear();

        // ---------------------------------------------------------------- window registry
        // WindowManager keeps a list, de-duplicates by type and cascades Close. Its own bookkeeping,
        // over the framework's own Window/Closed/Activate. Nothing framework-specific.
        Note("window.Closed / Activate / Close", true, "registry and cascade-close need no porting");

        // ---------------------------------------------------------------- AppWindow
        var appWindow = window.AppWindow;
        Note("window.AppWindow", appWindow is not null,
            appWindow is null ? "" : appWindow.GetType().FullName ?? appWindow.GetType().Name);

        if (appWindow is null)
        {
            return;
        }

        // ---------------------------------------------------------------- position and size
        // WindowPositionRect.UpdateFromAppWindow reads Size and Position; MainWindow writes them back.
        try
        {
            var size = appWindow.Size;
            var position = appWindow.Position;
            Note("AppWindow.Size / .Position read", true, $"{size.Width}x{size.Height} at {position.X},{position.Y}");
        }
        catch (Exception err)
        {
            Note("AppWindow.Size / .Position read", false, err.GetType().Name);
        }

        try
        {
            appWindow.Resize(new SizeInt32 { Width = 900, Height = 700 });
            Note("AppWindow.Resize", appWindow.Size.Width == 900, $"now {appWindow.Size.Width}x{appWindow.Size.Height}");

            appWindow.Move(new PointInt32 { X = 140, Y = 140 });
            Note("AppWindow.Move", appWindow.Position.X == 140, $"now {appWindow.Position.X},{appWindow.Position.Y}");

            appWindow.MoveAndResize(new RectInt32 { X = 160, Y = 160, Width = 1000, Height = 760 });
            Note("AppWindow.MoveAndResize", appWindow.Size.Width == 1000, $"now {appWindow.Size.Width}x{appWindow.Size.Height}");
        }
        catch (Exception err)
        {
            Note("AppWindow position/size writes", false, err.GetType().Name);
        }

        // ---------------------------------------------------------------- presenter state
        // OverlappedPresenterState round-trips through settings.json as LastWindowSizeAndPosition.
        try
        {
            if (appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.Maximize();
                Note("OverlappedPresenter.Maximize", true, $"state={overlapped.State}");

                overlapped.Minimize();
                Note("OverlappedPresenter.Minimize", true, $"state={overlapped.State}");

                overlapped.Restore();
                Note("OverlappedPresenter.Restore", true, $"state={overlapped.State}");
            }
            else
            {
                Note("OverlappedPresenter", false, $"presenter is {appWindow.Presenter?.GetType().Name ?? "null"}");
            }
        }
        catch (Exception err)
        {
            Note("OverlappedPresenter", false, err.GetType().Name);
        }

        // ---------------------------------------------------------------- title bar colours
        // The biggest block of WindowManager: eight AppWindowTitleBar colour properties driven from the
        // theme dictionary, plus a legacy fallback that pokes WindowCaption* resources and forces a
        // repaint with Win32 SendMessage.
        try
        {
            var titleBar = appWindow.TitleBar;
            if (titleBar is null)
            {
                Note("AppWindow.TitleBar", false, "null");
            }
            else
            {
                var before = titleBar.ButtonBackgroundColor;

                titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Purple;
                titleBar.ButtonForegroundColor = Microsoft.UI.Colors.White;
                titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Indigo;
                titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Gray;
                titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.LightGray;
                titleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Navy;
                titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;

                Note("AppWindowTitleBar.Button*Color (all 8)", true, $"{before} -> {titleBar.ButtonBackgroundColor}");
            }
        }
        catch (Exception err)
        {
            Note("AppWindowTitleBar colours", false, err.GetType().Name);
        }

        // The legacy fallback path: WindowManager.IsCustomizationSupported decides whether the above is
        // even available. There is no equivalent to ask on Uno, which is itself the finding.
        Note("AppWindowTitleBar.IsCustomizationSupported()", false,
            "no such API on Uno - so the Win32 RepaintWindow fallback is dead code on Linux, not a port");

        // ---------------------------------------------------------------- change notifications
        // MainWindow.xaml.cs subscribes to AppWindow.Changed to persist position on move.
        try
        {
            AppWindow? captured = appWindow;
            appWindow.Changed += (_, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange)
                {
                    _report.Add($"      changed: position={args.DidPositionChange} size={args.DidSizeChange}");
                }
            };

            Note("AppWindow.Changed", true, $"subscribed; window id = {captured.Id}");
        }
        catch (Exception err)
        {
            Note("AppWindow.Changed", false, err.GetType().Name);
        }

        // ---------------------------------------------------------------- theme
        // WindowManager propagates ElementTheme by setting RequestedTheme on each window's content.
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme.Dark;
            Note("FrameworkElement.RequestedTheme", root.RequestedTheme == ElementTheme.Dark);

            root.RequestedTheme = ElementTheme.Light;
            Note("  ...and back to Light", root.RequestedTheme == ElementTheme.Light);
        }
        else
        {
            Note("FrameworkElement.RequestedTheme", false, "window.Content is not a FrameworkElement");
        }
    }
}
