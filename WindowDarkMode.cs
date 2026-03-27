using System.Runtime.InteropServices;

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
    /// Enable dark title bar for the given window
    /// </summary>
    public static void EnableDarkMode(IntPtr hwnd)
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
}
