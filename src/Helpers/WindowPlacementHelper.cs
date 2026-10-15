using System;
using Kronos.Data;
using Microsoft.UI.Windowing;

namespace Kronos.Helpers;

/// <summary>
/// Saves and restores the main window's size, position and state through the Win32 window placement.
/// </summary>
/// <remarks>
/// <para>
/// This used to go through AppWindow: Move and Resize to restore, Position and Size to save. On Windows
/// App SDK those all use physical pixels. On Uno they do not agree with each other. Measured on a
/// two-monitor machine with the primary at 150%: a window saved 2880 wide came back 4320 wide - 1.5 times
/// larger on every launch - a saved x of 418 came back at -3609, and one launch came up minimised with a
/// saved state of Restored.
/// </para>
/// <para>
/// GetWindowPlacement and SetWindowPlacement are the Win32 pair made for exactly this. They work on the
/// real window in one consistent coordinate space, and the normal rectangle is kept apart from the
/// minimised and maximised state, so a window closed while maximised or minimised still remembers its
/// restored size.
/// </para>
/// </remarks>
internal static class WindowPlacementHelper
{
    /// <summary>Smallest restored size worth restoring; anything smaller is treated as no saved size.</summary>
    internal const int MinimumRestoredSize = 512;

    /// <summary>
    /// Applies a saved placement to a window that has not been shown yet.
    /// </summary>
    /// <returns>Whether the saved state asks for the window to be maximised, which the caller applies.</returns>
    internal static bool Restore(nint hWnd, WindowPositionRect saved)
    {
        if (OperatingSystem.IsWindows() == false || hWnd == 0)
        {
            return false;
        }

        var maximise = saved.State == OverlappedPresenterState.Maximized;

        if (saved.Width < MinimumRestoredSize || saved.Height < MinimumRestoredSize)
        {
            return maximise;
        }

        var requested = new Win32.RECT
        {
            Left = saved.X,
            Top = saved.Y,
            Right = saved.X + saved.Width,
            Bottom = saved.Y + saved.Height,
        };

        var monitor = Win32.MonitorFromRect(ref requested, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (monitor == IntPtr.Zero || Win32.GetMonitorInfo(monitor, ref info) == false)
        {
            return maximise;
        }

        var normal = FitToWorkArea(requested, info.rcWork);

        var placement = new Win32.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Win32.WINDOWPLACEMENT>() };
        if (Win32.GetWindowPlacement(hWnd, ref placement) == false)
        {
            return maximise;
        }

        // The window is not shown yet, and showing it is Uno's job. SW_HIDE sets the normal rectangle
        // without showing it, so the window first appears where it belongs rather than jumping there.
        placement.rcNormalPosition = normal;
        placement.showCmd = Win32.IsWindowVisible(hWnd) ? Win32.SW_SHOWNORMAL : Win32.SW_HIDE;
        placement.flags = 0;
        if (Win32.SetWindowPlacement(hWnd, ref placement) == false)
        {
            Logger.Warning("Could not restore the window placement.");
        }

        return maximise;
    }

    /// <summary>
    /// Reads the window's placement for saving. A minimised window is saved as the state it will restore
    /// to, never as minimised: reopening minimised is never what anyone wants.
    /// </summary>
    internal static WindowPositionRect? Read(nint hWnd)
    {
        if (OperatingSystem.IsWindows() == false || hWnd == 0)
        {
            return null;
        }

        var placement = new Win32.WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<Win32.WINDOWPLACEMENT>() };
        if (Win32.GetWindowPlacement(hWnd, ref placement) == false)
        {
            return null;
        }

        var normal = placement.rcNormalPosition;
        var maximised = placement.showCmd == Win32.SW_SHOWMAXIMIZED
            || (placement.showCmd == Win32.SW_SHOWMINIMIZED && (placement.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0);

        return new WindowPositionRect(normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top)
        {
            State = maximised ? OverlappedPresenterState.Maximized : OverlappedPresenterState.Restored,
        };
    }

    /// <summary>
    /// Shrinks a rectangle to fit the work area if it is larger, then moves it fully inside.
    /// </summary>
    /// <remarks>
    /// The saved sizes written before this fix were already inflated, and a monitor can be unplugged or
    /// rescaled between launches. Either way a window larger than the screen, or partly off it, opens
    /// with its title bar out of reach.
    /// </remarks>
    internal static Win32.RECT FitToWorkArea(Win32.RECT rect, Win32.RECT workArea)
    {
        var workWidth = workArea.Right - workArea.Left;
        var workHeight = workArea.Bottom - workArea.Top;

        var width = Math.Min(rect.Right - rect.Left, workWidth);
        var height = Math.Min(rect.Bottom - rect.Top, workHeight);

        var left = Math.Clamp(rect.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(rect.Top, workArea.Top, workArea.Bottom - height);

        return new Win32.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }
}
