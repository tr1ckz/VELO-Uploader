using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VeloUploader;

/// <summary>
/// Helper for enabling dark mode on Windows 10+ window frames
/// </summary>
internal static class WindowDarkMode
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>
    /// Forces dark mode on the window title bar regardless of system theme.
    /// </summary>
    public static void ApplyDarkMode(IntPtr hwnd)
    {
        try
        {
            int value = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
            // Silently fail on older Windows versions or if API not available
        }
    }

    /// <summary>
    /// Applies Windows system light/dark preference to this window title bar.
    /// </summary>
    public static void ApplyForSystemTheme(IntPtr hwnd)
    {
        try
        {
            int value = IsSystemUsingDarkMode() ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
            // Silently fail on older Windows versions or if API not available
        }
    }

    private static bool IsSystemUsingDarkMode()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
            );
            var appsUseLightTheme = personalizeKey?.GetValue("AppsUseLightTheme");
            if (appsUseLightTheme is int i)
                return i == 0;
        }
        catch
        {
            // Fall back to dark if registry is unavailable.
        }

        return true;
    }
}
