using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kronos.UnoSpike;

/// <summary>
/// Phase 3 probe: do the dialog and image primitives Kronos relies on behave on Uno?
/// </summary>
/// <remarks>
/// Phase 0 asked whether windowing compiles and behaves; this asks the same of the three things a swap
/// goes through on screen:
/// <list type="bullet">
/// <item>The window handle. Every file and folder picker in Kronos is a Win32 COM dialog
/// (FileSystemHelper) owned by <c>WindowNative.GetWindowHandle(MainWindow)</c>.</item>
/// <item>Cover images. Kronos binds <c>BitmapImage.UriSource</c> to a local PNG path turned into a
/// <c>Uri</c> by BitmapImageUriConverter, usually through an ImageBrush, with IgnoreImageCache.</item>
/// <item>ContentDialog: EasyContentDialog is a ContentDialog given a XamlRoot and a RequestedTheme,
/// shown with ShowAsync, and the DLL picker is shown inside one.</item>
/// </list>
/// Results, not inferences: each check does the thing and waits for the framework's own event.
/// </remarks>
public sealed class DialogsAndImagesProbe
{
    readonly List<string> _report = new();

    public IReadOnlyList<string> Report => _report;

    /// <summary>Where each line is also appended as it happens, so a check that hangs still leaves a trail.</summary>
    public string? ProgressPath { get; init; }

    void Note(string capability, bool supported, string detail = "")
    {
        var line = $"{(supported ? "YES" : "NO ")}  {capability}{(detail.Length > 0 ? "  -  " + detail : "")}";
        _report.Add(line);
        Progress(line);
    }

    void Progress(string line)
    {
        if (ProgressPath is not null)
        {
            File.AppendAllText(ProgressPath, line + Environment.NewLine);
        }
    }

    [DllImport("user32.dll")]
    static extern bool IsWindow(nint hWnd);

    public async Task RunAsync(Window window, Page page, Panel host)
    {
        await Check("window handle", () => CheckWindowHandle(window));
        await Check("window handle candidates", () => CheckWindowHandleCandidates(window));
        await Check("cover image via Image", () => CheckImageAsync(host, useBrush: false));
        await Check("cover image via ImageBrush", () => CheckImageAsync(host, useBrush: true));
        await Check("cover image, path with spaces", () => CheckImageAsync(host, useBrush: true, folderName: "cover cache with spaces"));
        await Check("missing cover fails cleanly", () => CheckMissingImageAsync(host));
        await Check("ContentDialog show and hide", () => CheckContentDialogAsync(page));
        await Check("ContentDialog twice in a row", () => CheckSequentialDialogsAsync(page));
        await Check("second ContentDialog while one is open", () => CheckOverlappingDialogsAsync(page));
        await Check("grouped games grid", () => new GroupedGridProbe((name, ok, detail) => Note(name, ok, detail)).RunAsync(host));
    }

    async Task Check(string name, Func<Task> check)
    {
        Progress($"...  {name}");
        try
        {
            await check();
        }
        catch (Exception err)
        {
            Note(name, false, $"{err.GetType().Name}: {err.Message}");
        }
    }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(nint hWnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int max);

    Task CheckWindowHandleCandidates(Window window)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return Task.CompletedTask;
        }

        // What the process's own visible top-level windows actually are.
        var pid = (uint)Environment.ProcessId;
        var mine = new List<string>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner == pid && IsWindowVisible(hWnd))
            {
                var cls = new System.Text.StringBuilder(256);
                var text = new System.Text.StringBuilder(256);
                GetClassName(hWnd, cls, cls.Capacity);
                GetWindowText(hWnd, text, text.Capacity);
                mine.Add($"0x{hWnd:X} class={cls} title='{text}'");
            }
            return true;
        }, 0);
        Note("visible top-level windows of this process", mine.Count > 0, string.Join(" | ", mine));

        var appWindowId = window.AppWindow.Id.Value;
        Note("AppWindow.Id.Value is an HWND", IsWindow((nint)appWindowId), $"0x{appWindowId:X}");

        var mainHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        Note("Process.MainWindowHandle", IsWindow(mainHandle), $"0x{mainHandle:X}");

        try
        {
            var fromId = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
            Note("Win32Interop.GetWindowFromWindowId", IsWindow(fromId), $"0x{fromId:X}");
        }
        catch (Exception err)
        {
            Note("Win32Interop.GetWindowFromWindowId", false, err.GetType().Name);
        }

        var nativeWindowProperty = typeof(Window).GetProperty("NativeWindow");
        Note("Window.NativeWindow is public", nativeWindowProperty?.GetMethod?.IsPublic == true);

        foreach (var property in window.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (property.Name.Contains("Native", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("Handle", StringComparison.OrdinalIgnoreCase))
            {
                object? value = null;
                try { value = property.GetValue(window); } catch { }
                Note($"Window.{property.Name}", true, $"{property.PropertyType.Name} = {value?.GetType().FullName ?? "null"}");
            }
        }

        return Task.CompletedTask;
    }

    Task CheckWindowHandle(Window window)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var valid = hWnd != 0 && (OperatingSystem.IsWindows() == false || IsWindow(hWnd));
        Note("WindowNative.GetWindowHandle", valid, $"0x{hWnd:X}" + (OperatingSystem.IsWindows() ? $", IsWindow={IsWindow(hWnd)}" : ""));
        return Task.CompletedTask;
    }

    // A 2x2 opaque PNG.
    static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    async Task CheckImageAsync(Panel host, bool useBrush, string folderName = "covers")
    {
        var folder = Path.Combine(Path.GetTempPath(), "kronos-phase3-probe", folderName);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}_400_600.png");
        await File.WriteAllBytesAsync(path, TinyPng);

        // Exactly what BitmapImageUriConverter does with Game.CoverImage.
        var bitmap = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
        var outcome = new TaskCompletionSource<string>();

        FrameworkElement element;
        if (useBrush)
        {
            var brush = new ImageBrush();
            brush.ImageOpened += (_, _) => outcome.TrySetResult("opened");
            brush.ImageFailed += (_, e) => outcome.TrySetResult("failed: " + e.ErrorMessage);
            brush.ImageSource = bitmap;
            element = new Border { Width = 40, Height = 60, Background = brush };
        }
        else
        {
            var image = new Image { Width = 40, Height = 60 };
            image.ImageOpened += (_, _) => outcome.TrySetResult("opened");
            image.ImageFailed += (_, e) => outcome.TrySetResult("failed: " + e.ErrorMessage);
            image.Source = bitmap;
            element = image;
        }

        host.Children.Add(element);
        bitmap.UriSource = new Uri(path);

        var finished = await Task.WhenAny(outcome.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        var result = finished == outcome.Task ? outcome.Task.Result : "timed out (no ImageOpened or ImageFailed)";
        Note($"BitmapImage from file Uri ({(useBrush ? "ImageBrush" : "Image")}, {folderName})", result == "opened",
            $"{result}; uri={bitmap.UriSource}; pixel={bitmap.PixelWidth}x{bitmap.PixelHeight}");

        host.Children.Remove(element);
    }

    async Task CheckMissingImageAsync(Panel host)
    {
        var bitmap = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
        var outcome = new TaskCompletionSource<string>();
        var image = new Image { Width = 40, Height = 60 };
        image.ImageOpened += (_, _) => outcome.TrySetResult("opened");
        image.ImageFailed += (_, e) => outcome.TrySetResult("failed: " + e.ErrorMessage);
        image.Source = bitmap;
        host.Children.Add(image);
        bitmap.UriSource = new Uri(Path.Combine(Path.GetTempPath(), "kronos-phase3-probe", "does-not-exist.png"));

        var finished = await Task.WhenAny(outcome.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        var result = finished == outcome.Task ? outcome.Task.Result : "timed out";
        // Either failing or simply not opening is fine; throwing would not be.
        Note("missing file raises ImageFailed rather than throwing", result.StartsWith("failed", StringComparison.Ordinal), result);
        host.Children.Remove(image);
    }

    static ContentDialog NewDialog(Page page, string title) => new()
    {
        // The EasyContentDialog shape.
        XamlRoot = page.XamlRoot,
        RequestedTheme = ElementTheme.Dark,
        Title = title,
        Content = new TextBlock { Text = "phase 3 probe" },
        CloseButtonText = "Cancel",
        PrimaryButtonText = "OK",
        DefaultButton = ContentDialogButton.Primary,
    };

    async Task CheckContentDialogAsync(Page page)
    {
        var dialog = NewDialog(page, "probe 1");
        var showing = dialog.ShowAsync().AsTask();
        await Task.Delay(750);

        var open = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).Count;
        Note("ContentDialog.ShowAsync opens a popup", open > 0, $"open popups={open}, loaded={dialog.IsLoaded}");

        dialog.Hide();
        var finished = await Task.WhenAny(showing, Task.Delay(TimeSpan.FromSeconds(5)));
        Note("ContentDialog.Hide completes ShowAsync", finished == showing,
            finished == showing ? $"result={showing.Result}" : "ShowAsync never completed");
    }

    async Task CheckSequentialDialogsAsync(Page page)
    {
        // Kronos chains dialogs constantly: an error after a confirmation, the DLL picker then a result.
        for (var i = 0; i < 2; i++)
        {
            var dialog = NewDialog(page, $"sequential {i}");
            var showing = dialog.ShowAsync().AsTask();
            await Task.Delay(400);
            dialog.Hide();
            var finished = await Task.WhenAny(showing, Task.Delay(TimeSpan.FromSeconds(5)));
            if (finished != showing)
            {
                Note("two dialogs in sequence", false, $"dialog {i} never completed");
                return;
            }
        }

        Note("two dialogs in sequence", true);
    }

    async Task CheckOverlappingDialogsAsync(Page page)
    {
        // WinUI throws here ("Only a single ContentDialog can be open at any time"). Kronos has code
        // written around that; worth knowing whether Uno agrees.
        var first = NewDialog(page, "first");
        var firstShowing = first.ShowAsync().AsTask();
        await Task.Delay(400);

        string outcome;
        try
        {
            var second = NewDialog(page, "second");
            var secondShowing = second.ShowAsync().AsTask();
            await Task.Delay(400);
            second.Hide();
            await Task.WhenAny(secondShowing, Task.Delay(TimeSpan.FromSeconds(3)));
            outcome = "allowed (WinUI would throw)";
        }
        catch (Exception err)
        {
            outcome = $"throws {err.GetType().Name} (same as WinUI)";
        }

        first.Hide();
        await Task.WhenAny(firstShowing, Task.Delay(TimeSpan.FromSeconds(3)));
        Note("second dialog while one is open", true, outcome);
    }
}
