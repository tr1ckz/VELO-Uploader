namespace VeloUploader;

using System.Collections.Concurrent;

public enum LogLevel { Debug, Info, Warning, Error }

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
}

public static class Logger
{
    private static readonly List<LogEntry> _entries = [];
    private static readonly object _lock = new();
    private static readonly ConcurrentQueue<string> _pendingWrites = new();
    private static readonly SemaphoreSlim _writeSignal = new(0);
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VeloUploader", "logs");

    public const int MaxEntries = 5000;

    public static event Action<LogEntry>? OnLog;

    static Logger()
    {
        _ = Task.Run(ProcessWritesAsync);
    }

    public static IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warn(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);
    public static void Error(string message, Exception ex) => Log(LogLevel.Error, $"{message}: {ex.Message}");

    private static void Log(LogLevel level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message
        };

        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        OnLog?.Invoke(entry);
        QueueWrite(entry);
    }

    private static void QueueWrite(LogEntry entry)
    {
        _pendingWrites.Enqueue(entry.ToString());
        _writeSignal.Release();
    }

    private static async Task ProcessWritesAsync()
    {
        while (true)
        {
            try
            {
                await _writeSignal.WaitAsync().ConfigureAwait(false);

                Directory.CreateDirectory(LogDir);
                var logFile = Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
                using var stream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                using var writer = new StreamWriter(stream);

                while (_pendingWrites.TryDequeue(out var line))
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // Never let logging crash the app.
            }
        }
    }

    private static void WriteToFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var logFile = Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
            File.AppendAllText(logFile, entry.ToString() + Environment.NewLine);
        }
        catch { /* don't crash on log failure */ }
    }

    public static void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public static string GetLogFilePath() =>
        Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log");
}
