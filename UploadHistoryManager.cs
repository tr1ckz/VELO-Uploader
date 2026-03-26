using System.Text.Json;

namespace VeloUploader;

public class UploadHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string FileName { get; set; } = "";
    public string? FileHash { get; set; }
    public bool Success { get; set; }
    public string? Url { get; set; }
    public string? Error { get; set; }
    public bool UsedCompression { get; set; }
    public string? CompressionPreset { get; set; }
    public long SourceSizeBytes { get; set; }
    public long UploadedSizeBytes { get; set; }

    public override string ToString()
    {
        var status = Success ? "OK" : "FAIL";
        var compression = UsedCompression && !string.IsNullOrWhiteSpace(CompressionPreset)
            ? $" [{CompressionPreset}]"
            : "";
        return $"[{Timestamp:MM-dd HH:mm}] {status} {FileName}{compression}";
    }
}

public static class UploadHistoryManager
{
    private const int MaxEntries = 200;
    private static readonly object _lock = new();
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "upload-history.json");

    public static event Action? Changed;

    public static IReadOnlyList<UploadHistoryEntry> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(HistoryPath)) return [];
                var json = File.ReadAllText(HistoryPath);
                return JsonSerializer.Deserialize<List<UploadHistoryEntry>>(json) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public static void Add(UploadHistoryEntry entry)
    {
        lock (_lock)
        {
            var items = Load().ToList();
            items.Insert(0, entry);
            if (items.Count > MaxEntries)
                items = items.Take(MaxEntries).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryPath, json);
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            }
            catch { }
        }

        Changed?.Invoke();
    }
}