using System.Text.Json;

namespace VeloUploader;

public static class PendingUploadQueueStore
{
    private static readonly object _lock = new();
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "pending-uploads.json");

    public static List<string> Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(StorePath)) return [];
                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<List<string>>(json)?.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public static void Add(string filePath)
    {
        lock (_lock)
        {
            var items = Load();
            if (!items.Contains(filePath, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(filePath);
                Persist(items);
            }
        }
    }

    public static void Remove(string filePath)
    {
        lock (_lock)
        {
            var items = Load();
            items.RemoveAll(x => string.Equals(x, filePath, StringComparison.OrdinalIgnoreCase));
            Persist(items);
        }
    }

    private static void Persist(List<string> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch
        {
            // Non-fatal persistence failure.
        }
    }
}
