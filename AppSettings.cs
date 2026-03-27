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
    public List<string> IgnoredFolders { get; set; } = [];
    public List<string> IgnoredPatterns { get; set; } = [];
    public int MaxFileSizeMB { get; set; } = 0; // 0 = no limit
    public bool DeleteAfterUpload { get; set; } = false;
    public int MaxRetries { get; set; } = 3;
    public bool ScanOnLaunch { get; set; } = false;
    public bool LocalCompress { get; set; } = false;
    public bool StopOnCompressionFailure { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public string CompressionPreset { get; set; } = global::VeloUploader.CompressionPreset.Balanced;
    public bool AllowSelfSignedCerts { get; set; } = false;
    public string TrustedCertPath { get; set; } = "";

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

public static class CompressionPreset
{
    public const string Balanced = "Balanced";
    public const string Quality = "Quality";
    public const string Aggressive = "Aggressive";
    public const string Discord = "Discord";

    public static readonly string[] All = [Balanced, Quality, Aggressive, Discord];
}
