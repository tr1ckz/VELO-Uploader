using System.IO;
using System.Text.Json;

namespace VeloUploader;

public class UploadResumeSession
{
    public string Key { get; set; } = "";
    public string UploadId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public long FileSize { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public bool PreCompressed { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class UploadResumeStore
{
    private static readonly object _lock = new();
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "resume-sessions.json");

    public static string BuildKey(string serverUrl, string filePath, FileInfo fileInfo, bool preCompressed)
    {
        return string.Join("|",
            serverUrl.TrimEnd('/').ToLowerInvariant(),
            filePath,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks,
            preCompressed);
    }

    public static UploadResumeSession? Get(string key)
    {
        lock (_lock)
        {
            return LoadAll().FirstOrDefault(x => x.Key == key);
        }
    }

    public static void Save(UploadResumeSession session)
    {
        lock (_lock)
        {
            var sessions = LoadAll();
            sessions.RemoveAll(x => x.Key == session.Key);
            session.UpdatedAtUtc = DateTime.UtcNow;
            sessions.Add(session);
            Persist(sessions);
        }
    }

    public static void Remove(string key)
    {
        lock (_lock)
        {
            var sessions = LoadAll();
            sessions.RemoveAll(x => x.Key == key);
            Persist(sessions);
        }
    }

    private static List<UploadResumeSession> LoadAll()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<UploadResumeSession>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Persist(List<UploadResumeSession> sessions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var json = JsonSerializer.Serialize(sessions, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StorePath, json);
    }
}