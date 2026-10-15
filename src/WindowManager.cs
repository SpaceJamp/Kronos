using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace Kronos;

public class WindowManager
{
    readonly List<Window> _windows = new List<Window>();

    public static ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    ThemeWatcher _themeWatcher;

    public WindowManager()
    {
        _themeWatcher = new ThemeWatcher();
        _themeWatcher.ThemeChanged += ThemeWatcher_ThemeChanged;
        _themeWatcher.Start();

    }

    public void ShowWindow(Window window)
    {
        foreach (var oldWindow in _windows)
        {
            if (oldWindow.GetType() == window.GetType())
            {
                oldWindow.Activate();
                return;
            }
        }

        window.Closed += Window_Closed;
        window.Activate();
        _windows.Add(window);
        // TOOD: This will update colours on all windows which is not ideal.
        UpdateColors(Settings.Instance.AppTheme);
    }

    void Window_Closed(object sender, WindowEventArgs args)
    {
        // Remove from watching list first.
        if (sender is Window window)
        {
            _windows.Remove(window);
            window.Closed -= Window_Closed;
        }

        // But if the sender is the main window we should close all the other windows.
        if (sender.GetType() == typeof(MainWindow))
        {
            // Need to clone the list as it is being modified elsewhere.
            var windowsToClose = new List<Window>(_windows);
            foreach (var windowToClose in windowsToClose)
            {
                windowToClose.Close();
            }
        }
    }

    internal void UpdateColors(ElementTheme theme)
    {
        ((App)Application.Current).GlobalElementTheme = theme;

        if (theme == ElementTheme.Light)
        {
            UpdateColorsLight();
        }
        else if (theme == ElementTheme.Dark)
        {
            UpdateColorsDark();
        }
        else
        {
            var osApplicationTheme = _themeWatcher.GetWindowsApplicationTheme();
            if (osApplicationTheme == ApplicationTheme.Light)
            {
                UpdateColorsLight();
            }
            else if (osApplicationTheme == ApplicationTheme.Dark)
            {
                UpdateColorsDark();
            }
        }
    }

    // The title bar is themed through the WindowCaption* resources only. WinAppSDK also offered
    // AppWindowTitleBar.Button*Color when AppWindowTitleBar.IsCustomizationSupported(), but Uno
    // throws NotImplementedException from all eight of those setters (spike/PHASE0-FINDINGS.md).
    void UpdateColorsLight()
    {
        CurrentTheme = ElementTheme.Light;

        var theme = App.CurrentApp.Resources.MergedDictionaries[1].ThemeDictionaries["Light"] as ResourceDictionary;

        if (theme is null)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            foreach (var window in _windows)
            {
                if (window.Content is FrameworkElement frameworkElement)
                {
                    frameworkElement.RequestedTheme = ElementTheme.Light;
                }
                var appResources = Application.Current.Resources;
                // Removes the tint on title bar
                appResources["WindowCaptionBackground"] = theme["WindowCaptionBackground"];
                appResources["WindowCaptionBackgroundDisabled"] = theme["WindowCaptionBackgroundDisabled"];
                // Sets the tint of the forground of the buttons
                appResources["WindowCaptionForeground"] = theme["WindowCaptionForeground"];
                appResources["WindowCaptionForegroundDisabled"] = theme["WindowCaptionForegroundDisabled"];

                appResources["WindowCaptionButtonBackgroundPointerOver"] = theme["WindowCaptionButtonBackgroundPointerOver"];

                RepaintWindow(window);
            }
            
        });
    }

    void UpdateColorsDark()
    {
        CurrentTheme = ElementTheme.Dark;

        var theme = App.CurrentApp.Resources.MergedDictionaries[1].ThemeDictionaries["Dark"] as ResourceDictionary;

        if (theme is null)
        {
            return;
        }

        App.CurrentApp.RunOnUIThread(() =>
        {
            foreach (var window in _windows)
            {
                if (window.Content is FrameworkElement frameworkElement)
                {
                    frameworkElement.RequestedTheme = ElementTheme.Dark;
                }

                var appResources = Application.Current.Resources;

                // Removes the tint on title bar
                appResources["WindowCaptionBackground"] = theme["WindowCaptionBackground"];
                appResources["WindowCaptionBackgroundDisabled"] = theme["WindowCaptionBackgroundDisabled"];
                // Sets the tint of the forground of the buttons
                appResources["WindowCaptionForeground"] = theme["WindowCaptionForeground"];
                appResources["WindowCaptionForegroundDisabled"] = theme["WindowCaptionForegroundDisabled"];

                appResources["WindowCaptionButtonBackgroundPointerOver"] = theme["WindowCaptionButtonBackgroundPointerOver"];

                RepaintWindow(window);
            }
        });
    }

    // to trigger repaint tracking task id 38044406
    void RepaintWindow(Window window)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var activeWindow = Win32.GetActiveWindow();
        if (hWnd == activeWindow)
        {
            Win32.SendMessage(hWnd, Win32.WM_ACTIVATE, Win32.WA_INACTIVE, IntPtr.Zero);
            Win32.SendMessage(hWnd, Win32.WM_ACTIVATE, Win32.WA_ACTIVE, IntPtr.Zero);
        }
        else
        {
            Win32.SendMessage(hWnd, Win32.WM_ACTIVATE, Win32.WA_ACTIVE, IntPtr.Zero);
            Win32.SendMessage(hWnd, Win32.WM_ACTIVATE, Win32.WA_INACTIVE, IntPtr.Zero);
        }
    }

    void ThemeWatcher_ThemeChanged(object? sender, ApplicationTheme e)
    {
        var globalTheme = ((App)Application.Current).GlobalElementTheme;

        if (globalTheme == ElementTheme.Default)
        {
            var osApplicationTheme = _themeWatcher.GetWindowsApplicationTheme();

            if (osApplicationTheme == ApplicationTheme.Light)
            {
                UpdateColorsLight();
            }
            else if (osApplicationTheme == ApplicationTheme.Dark)
            {
                UpdateColorsDark();
            }
        }
    }
}
