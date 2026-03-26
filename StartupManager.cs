using Microsoft.Win32;

namespace VeloUploader;

public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "VeloUploader";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public static void Register()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
            Logger.Info("Registered to start with Windows.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to register startup", ex);
        }
    }

    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, false);
            Logger.Info("Removed from Windows startup.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to unregister startup", ex);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Register();
        else Unregister();
    }
}
