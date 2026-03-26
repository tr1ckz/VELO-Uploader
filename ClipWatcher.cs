using System.Text.RegularExpressions;

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

        if (string.IsNullOrWhiteSpace(_settings.WatchFolder))
        {
            Logger.Warn("Watch folder is not set.");
            return;
        }

        // Auto-create the watch folder if it doesn't exist (ShadowPlay may not have created it yet)
        if (!Directory.Exists(_settings.WatchFolder))
        {
            try
            {
                Directory.CreateDirectory(_settings.WatchFolder);
                Logger.Info($"Created watch folder: {_settings.WatchFolder}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Cannot create watch folder: {_settings.WatchFolder}", ex);
                return;
            }
        }

        _watcher = new FileSystemWatcher(_settings.WatchFolder)
        {
            IncludeSubdirectories = _settings.WatchSubfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;

        Logger.Info($"Started watching: {_settings.WatchFolder} (subfolders: {_settings.WatchSubfolders})");
        if (_settings.IgnoredFolders.Count > 0)
            Logger.Info($"Ignored folders: {string.Join(", ", _settings.IgnoredFolders)}");
        if (_settings.IgnoredPatterns.Count > 0)
            Logger.Info($"Ignored patterns: {string.Join(", ", _settings.IgnoredPatterns)}");
    }

    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            Logger.Info("Stopped watching for clips.");
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e) => HandleNewFile(e.FullPath);
    private void OnFileRenamed(object sender, RenamedEventArgs e) => HandleNewFile(e.FullPath);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Logger.Error("File watcher error", e.GetException());
        // Try to restart the watcher
        try { Start(); }
        catch (Exception ex) { Logger.Error("Failed to restart watcher", ex); }
    }

    private async void HandleNewFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return;

        var fileName = Path.GetFileName(filePath);

        // Check ignored folders
        if (IsInIgnoredFolder(filePath))
        {
            Logger.Debug($"Skipped (ignored folder): {fileName}");
            return;
        }

        // Check ignored filename patterns
        if (MatchesIgnoredPattern(fileName))
        {
            Logger.Debug($"Skipped (ignored pattern): {fileName}");
            return;
        }

        // Check max file size limit
        if (_settings.MaxFileSizeMB > 0)
        {
            try
            {
                var fi = new FileInfo(filePath);
                if (fi.Exists && fi.Length > (long)_settings.MaxFileSizeMB * 1024 * 1024)
                {
                    Logger.Warn($"Skipped (too large: {fi.Length / 1024 / 1024}MB > {_settings.MaxFileSizeMB}MB limit): {fileName}");
                    return;
                }
            }
            catch { /* file may not be ready yet, proceed */ }
        }

        // Avoid processing the same file twice
        lock (_processing)
        {
            if (!_processing.Add(filePath))
                return;
        }

        Logger.Info($"New clip detected: {fileName}");

        try
        {
            // Wait for the file to be fully written (ShadowPlay writes progressively)
            Logger.Debug($"Waiting for file to finish writing: {fileName}");
            await WaitForFileReady(filePath);

            // Re-check file size after it's fully written
            if (_settings.MaxFileSizeMB > 0)
            {
                var fi = new FileInfo(filePath);
                if (fi.Exists && fi.Length > (long)_settings.MaxFileSizeMB * 1024 * 1024)
                {
                    Logger.Warn($"Skipped (too large after write: {fi.Length / 1024 / 1024}MB > {_settings.MaxFileSizeMB}MB limit): {fileName}");
                    return;
                }
            }

            Logger.Info($"File ready, starting upload: {fileName}");
            _onNewClip(filePath);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error handling clip {fileName}", ex);
        }
        finally
        {
            lock (_processing)
            {
                _processing.Remove(filePath);
            }
        }
    }

    private bool IsInIgnoredFolder(string filePath)
    {
        if (_settings.IgnoredFolders.Count == 0) return false;

        var dir = Path.GetDirectoryName(filePath) ?? "";
        foreach (var ignored in _settings.IgnoredFolders)
        {
            if (string.IsNullOrWhiteSpace(ignored)) continue;
            var trimmed = ignored.Trim();

            // Check if any folder in the path matches (case-insensitive)
            if (dir.Contains(Path.DirectorySeparatorChar + trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || dir.EndsWith(Path.DirectorySeparatorChar + trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private bool MatchesIgnoredPattern(string fileName)
    {
        if (_settings.IgnoredPatterns.Count == 0) return false;

        foreach (var pattern in _settings.IgnoredPatterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var trimmed = pattern.Trim();

            try
            {
                // Support simple wildcards: * and ?
                var regex = "^" + Regex.Escape(trimmed).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                if (Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase))
                    return true;
            }
            catch
            {
                // If pattern is invalid, try simple contains
                if (fileName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
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

        Logger.Warn($"Timed out waiting for file to be ready: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// Scans the watch folder for existing video files and triggers the upload callback for each.
    /// </summary>
    public void ScanExistingFiles()
    {
        if (string.IsNullOrWhiteSpace(_settings.WatchFolder) || !Directory.Exists(_settings.WatchFolder))
            return;

        var searchOption = _settings.WatchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        Logger.Info($"Scanning existing files in: {_settings.WatchFolder}");
        int found = 0;

        foreach (var ext in VideoExtensions)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(_settings.WatchFolder, "*" + ext, searchOption); }
            catch (Exception ex) { Logger.Error($"Error scanning for {ext} files", ex); continue; }

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                if (IsInIgnoredFolder(file)) continue;
                if (MatchesIgnoredPattern(fileName)) continue;

                if (_settings.MaxFileSizeMB > 0)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length > (long)_settings.MaxFileSizeMB * 1024 * 1024) continue;
                    }
                    catch { continue; }
                }

                found++;
                HandleNewFile(file);
            }
        }

        Logger.Info($"Scan complete — {found} existing clip(s) queued for upload.");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
