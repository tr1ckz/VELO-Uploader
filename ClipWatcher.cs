namespace VeloUploader;

public class ClipWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly AppSettings _settings;
    private readonly Action<string> _onNewClip;
    private readonly HashSet<string> _processing = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".mov", ".avi"];

    public bool IsWatching => _watcher?.EnableRaisingEvents == true;

    public ClipWatcher(AppSettings settings, Action<string> onNewClip)
    {
        _settings = settings;
        _onNewClip = onNewClip;
    }

    public void Start()
    {
        Stop();

        if (string.IsNullOrWhiteSpace(_settings.WatchFolder) || !Directory.Exists(_settings.WatchFolder))
            return;

        _watcher = new FileSystemWatcher(_settings.WatchFolder)
        {
            IncludeSubdirectories = _settings.WatchSubfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e) => HandleNewFile(e.FullPath);
    private void OnFileRenamed(object sender, RenamedEventArgs e) => HandleNewFile(e.FullPath);

    private async void HandleNewFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return;

        // Avoid processing the same file twice
        lock (_processing)
        {
            if (!_processing.Add(filePath))
                return;
        }

        try
        {
            // Wait for the file to be fully written (ShadowPlay writes progressively)
            await WaitForFileReady(filePath);
            _onNewClip(filePath);
        }
        finally
        {
            lock (_processing)
            {
                _processing.Remove(filePath);
            }
        }
    }

    private static async Task WaitForFileReady(string path, int timeoutSeconds = 120)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        // Initial delay — ShadowPlay hasn't finished writing yet
        await Task.Delay(2000);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return; // File is ready
            }
            catch (IOException)
            {
                await Task.Delay(1000);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(1000);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
