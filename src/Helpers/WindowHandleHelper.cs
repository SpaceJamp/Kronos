using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace Kronos.Helpers;

/// <summary>
/// The owner window handle for the Win32 file and folder dialogs in <see cref="FileSystemHelper"/>.
/// </summary>
/// <remarks>
/// Every picker in Kronos used to get its owner from <c>WinRT.Interop.WindowNative.GetWindowHandle</c>.
/// On Windows App SDK that is the window's HWND. On Uno it returns 0x1 - not a window - and
/// IFileDialog.Show rejects that with E_INVALIDARG before showing anything. Each caller catches and logs
/// the exception, so the result was every picker in the app silently doing nothing: adding a game,
/// choosing a cover, importing DLLs, exporting, and the translation toolbox.
///
/// Uno's real HWND is on <c>Window.NativeWindow</c>, which is internal, so it is not used here. Instead:
/// <list type="number">
/// <item>The framework's own answer, when it is a real window of this process. Correct on WinAppSDK,
/// and correct on Uno too if it ever starts returning one.</item>
/// <item>The active window on the calling thread. A picker opens from a click, on the UI thread, so the
/// active window is the window that was clicked - which is exactly the owner wanted, including for the
/// translation toolbox rather than the main window.</item>
/// <item>The process's main window.</item>
/// <item>No owner. The dialog still works; it just is not modal to Kronos. Far better than not opening.</item>
/// </list>
/// </remarks>
internal static class WindowHandleHelper
{
    internal static nint GetOwnerHandle(Window? window)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return 0;
        }

        if (window is not null)
        {
            nint fromFramework;
            try
            {
                fromFramework = WinRT.Interop.WindowNative.GetWindowHandle(window);
            }
            catch (Exception)
            {
                fromFramework = 0;
            }

            if (IsWindowOfThisProcess(fromFramework))
            {
                Logger.Verbose($"Dialog owner 0x{fromFramework:X} from WindowNative.");
                return fromFramework;
            }
        }

        var active = Win32.GetActiveWindow();
        if (IsWindowOfThisProcess(active))
        {
            Logger.Verbose($"Dialog owner 0x{active:X} from GetActiveWindow.");
            return active;
        }

        using var process = Process.GetCurrentProcess();
        var main = process.MainWindowHandle;
        if (IsWindowOfThisProcess(main))
        {
            Logger.Verbose($"Dialog owner 0x{main:X} from the process's main window.");
            return main;
        }

        Logger.Verbose("No dialog owner found; the dialog will open unowned.");
        return 0;
    }

    /// <summary>
    /// The native handle of a specific window, or 0 if it cannot be found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For work on that exact window - saving and restoring its placement, repainting it - rather than for
    /// owning a dialog, where any window of ours will do. <see cref="GetOwnerHandle"/> falls back to the
    /// active or main window, which is the wrong window here.
    /// </para>
    /// <para>
    /// On Uno, WindowNative returns 0x1 and the real handle is internal, so this looks among the top-level
    /// windows of the calling thread for Uno's window class with this window's title. Call it on the UI
    /// thread, after the title is set. A title shared by two windows on the thread is ambiguous, and 0 is
    /// returned rather than guessing.
    /// </para>
    /// </remarks>
    internal static nint FindNativeWindow(Window window)
    {
        if (OperatingSystem.IsWindows() == false)
        {
            return 0;
        }

        nint fromFramework;
        try
        {
            fromFramework = WinRT.Interop.WindowNative.GetWindowHandle(window);
        }
        catch (Exception)
        {
            fromFramework = 0;
        }

        if (IsWindowOfThisProcess(fromFramework))
        {
            return fromFramework;
        }

        var title = window.Title ?? string.Empty;
        nint match = 0;
        var matches = 0;
        nint onlyUnoWindow = 0;
        var unoWindows = 0;
        Win32.EnumThreadWindows(Win32.GetCurrentThreadId(), (hWnd, _) =>
        {
            var className = new System.Text.StringBuilder(256);
            Win32.GetClassName(hWnd, className, className.Capacity);
            if (className.ToString() != UnoWindowClassName)
            {
                return true;
            }

            onlyUnoWindow = hWnd;
            unoWindows++;

            var text = new System.Text.StringBuilder(512);
            Win32.GetWindowText(hWnd, text, text.Capacity);
            if (text.ToString() == title)
            {
                match = hWnd;
                matches++;
            }
            return true;
        }, IntPtr.Zero);

        if (matches == 1)
        {
            return match;
        }

        // The title is data-bound and localised, so the native text can lag behind Window.Title while
        // the window is being set up. With a single Uno window on the thread there is nothing to confuse
        // it with.
        if (matches == 0 && unoWindows == 1)
        {
            return onlyUnoWindow;
        }

        Logger.Verbose(matches == 0
            ? $"No native window found for '{title}'."
            : $"{matches} native windows are titled '{title}'; not guessing which one is meant.");
        return 0;
    }

    /// <summary>The window class Uno's Win32 host registers for an application window.</summary>
    const string UnoWindowClassName = "UnoPlatformRegularWindow";

    /// <summary>
    /// A real window, and one of ours. The second half matters: an owner from another process would make
    /// the picker modal to someone else's window.
    /// </summary>
    internal static bool IsWindowOfThisProcess(nint hWnd)
    {
        if (hWnd == 0 || Win32.IsWindow(hWnd) == false)
        {
            return false;
        }

        Win32.GetWindowThreadProcessId(hWnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }
}
