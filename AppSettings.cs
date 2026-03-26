using System.Text.Json;

namespace VeloUploader;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "settings.json");

    public string ServerUrl { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string WatchFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
    public bool WatchSubfolders { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }
}
