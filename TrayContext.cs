namespace VeloUploader;

using System.Collections.Concurrent;
using System.Security.Cryptography;

public class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private ClipWatcher? _watcher;
    private int _uploadCount;
    private int _successCount;
    private long _totalBytes;
    private readonly CancellationTokenSource _cts = new();
    private bool _updateCheckInProgress;
    private readonly ClipProcessingQueue _processingQueue = new();
    private readonly ConcurrentQueue<string> _pendingQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly HashSet<string> _queuedSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _retryAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _queueWorkers = [];
    private bool _queueProcessingEnabled;
    private string? _activeQueueFile;
    private const int QueueWorkerCount = 3;

    private sealed record ProcessClipResult(bool Success, bool Retryable = false, TimeSpan? RetryAfter = null);

    public TrayContext()
    {
        _settings = AppSettings.Load();
        _queueProcessingEnabled = _settings.AutoProcessQueue;

        _ = Task.Run(() => PolicySyncService.TrySyncAsync(_settings, _cts.Token));

        Logger.Info("VELO Uploader started.");
        UploadService.Reconfigure(_settings);

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "VELO Uploader",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        for (var workerIndex = 0; workerIndex < QueueWorkerCount; workerIndex++)
            _queueWorkers.Add(Task.Run(() => ProcessPendingQueueLoop(_cts.Token)));

        if (_settings.EnableQueuePersistence)
        {
            var persisted = PendingUploadQueueStore.Load();
            foreach (var item in persisted)
                EnqueueClip(item, fromPersistence: true);
            if (persisted.Count > 0)
                Logger.Info($"Recovered {persisted.Count} pending upload(s) from previous session.");
        }

        Logger.Info($"Queue workers active: {QueueWorkerCount}");

        // Check and prompt for FFmpeg installation on first launch if needed
        if (_settings.LocalCompress && !FFmpegHelper.IsFFmpegAvailable())
        {
            CheckFFmpegInstallation();
        }

        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        if (IsConfigured())
            StartWatching();
        else
            ShowSettings();

        if (_settings.AutoCheckForUpdates)
            BeginStartupUpdateCheck();
    }

    private void CheckFFmpegInstallation()
    {
        using var prompt = new FFmpegInstallPrompt();
        prompt.ShowDialog();
        Logger.Info("FFmpeg installation prompt dismissed");
    }

    private static Icon LoadAppIcon()
    {
        // Try embedded resource first (works in single-file publish)
        try
        {
            var asm = typeof(TrayContext).Assembly;
            using var stream = asm.GetManifestResourceStream("velo.ico");
            if (stream != null)
                return new Icon(stream, 32, 32);
        }
        catch { }

        // Try file on disk
        var dir = AppContext.BaseDirectory;
        try
        {
            var icoPath = Path.Combine(dir, "velo.ico");
            if (File.Exists(icoPath))
                return new Icon(icoPath, 32, 32);
        }
        catch { }

        // Fall back to logo.png → convert to Icon at runtime
        try
        {
            using var pngStream = typeof(TrayContext).Assembly.GetManifestResourceStream("logo.png");
            if (pngStream != null)
            {
                using var bmp = new Bitmap(pngStream);
                // Create a proper 32x32 square icon
                using var icon32 = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(icon32);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                // Center-crop to square
                int sq = Math.Min(bmp.Width, bmp.Height);
                int sx = (bmp.Width - sq) / 2;
                g.DrawImage(bmp, new Rectangle(0, 0, 32, 32), new Rectangle(sx, 0, sq, sq), GraphicsUnit.Pixel);
                return Icon.FromHandle(icon32.GetHicon());
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    private bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(_settings.ServerUrl) &&
        !string.IsNullOrWhiteSpace(_settings.ApiToken);

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem(_watcher?.IsWatching == true ? "● Watching" : "○ Not watching")
        { Enabled = false };
        menu.Items.Add(statusItem);

        if (_uploadCount > 0)
        {
            var countItem = new ToolStripMenuItem($"  {_uploadCount} uploaded this session") { Enabled = false };
            menu.Items.Add(countItem);
        }

        if (_pendingQueue.Count > 0)
        {
            var queueItem = new ToolStripMenuItem($"  {_pendingQueue.Count} queued locally") { Enabled = false };
            menu.Items.Add(queueItem);
        }

        menu.Items.Add(new ToolStripSeparator());

        var toggleItem = new ToolStripMenuItem(_watcher?.IsWatching == true ? "Pause" : "Resume");
        toggleItem.Click += (_, _) =>
        {
            if (_watcher?.IsWatching == true)
                StopWatching();
            else
                StartWatching();
        };
        menu.Items.Add(toggleItem);

        var queueModeItem = new ToolStripMenuItem(_queueProcessingEnabled ? "Queue Only Mode (Pause Uploads)" : "Resume Upload Queue");
        queueModeItem.Click += (_, _) => SetQueueProcessingEnabled(!_queueProcessingEnabled);
        menu.Items.Add(queueModeItem);

        var processNowItem = new ToolStripMenuItem("Process Queued Files Now")
        {
            Enabled = _pendingQueue.Count > 0,
        };
        processNowItem.Click += (_, _) => SetQueueProcessingEnabled(true, flushExistingQueue: true);
        menu.Items.Add(processNowItem);

        var quickEditorItem = new ToolStripMenuItem("Video Editor...");
        quickEditorItem.Click += (_, _) => ShowQuickEditor();
        menu.Items.Add(quickEditorItem);

        var dashboardItem = new ToolStripMenuItem("Queue Dashboard...");
        dashboardItem.Click += (_, _) => ShowSettingsOnTab(0);
        menu.Items.Add(dashboardItem);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettingsOnTab(1);
        menu.Items.Add(settingsItem);

        var logsItem = new ToolStripMenuItem("Logs...");
        logsItem.Click += (_, _) => ShowSettingsOnTab(2);
        menu.Items.Add(logsItem);

        var updatesItem = new ToolStripMenuItem("Check for Updates...");
        updatesItem.Click += async (_, _) => await CheckForUpdatesAsync(silentIfUpToDate: false);
        menu.Items.Add(updatesItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Logger.Info("VELO Uploader exiting.");
            _watcher?.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void RefreshMenu()
    {
        if (_trayIcon == null)
            return;

        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = BuildMenu();
    }

    private bool _initialLaunch = true;

    private void StartWatching()
    {
        if (!IsConfigured())
        {
            ShowToast("VELO Uploader", "Please configure server URL and API token first.");
            ShowSettings();
            return;
        }

        _watcher?.Dispose();
        _watcher = new ClipWatcher(_settings, OnNewClip);
        _watcher.Start();

        // On first launch, scan existing files if enabled
        if (_initialLaunch && _settings.ScanOnLaunch)
        {
            _initialLaunch = false;
            _watcher.ScanExistingFiles();
        }
        else
        {
            _initialLaunch = false;
        }

        SetTrayText($"VELO Uploader — Watching {_settings.WatchFolder}");
        RefreshMenu();
        UpdateStatusWindow();
        ShowToast("VELO Uploader", $"Watching: {_settings.WatchFolder}", "Monitoring for new clips");
    }

    private void StopWatching()
    {
        _watcher?.Stop();
        SetTrayText("VELO Uploader — Paused");
        Logger.Info("Watching paused by user.");
        RefreshMenu();
        UpdateStatusWindow();
    }

    private void SetQueueProcessingEnabled(bool enabled, bool flushExistingQueue = true)
    {
        _queueProcessingEnabled = enabled;
        _settings.AutoProcessQueue = enabled;
        _settings.Save();

        if (enabled)
        {
            var queuedCount = _pendingQueue.Count;
            if (flushExistingQueue)
            {
                for (var i = 0; i < queuedCount; i++)
                    _queueSignal.Release();
            }

            Logger.Info($"Upload processing resumed ({queuedCount} queued item(s)).");
            ShowToast("Upload queue resumed", queuedCount > 0 ? $"{queuedCount} queued clip(s) will start now." : "Uploads are live again.");
            SetTrayText(_watcher?.IsWatching == true ? $"VELO Uploader — Watching {_settings.WatchFolder}" : "VELO Uploader — Ready");
        }
        else
        {
            Logger.Info("Queue-only mode enabled — new clips will stay queued locally until resumed.");
            ShowToast("Queue-only mode enabled", "New clips will be queued locally.", "Resume the queue when you want uploads to start.");
            SetTrayText("VELO Uploader — Queue-only mode");
        }

        RefreshMenu();
        UpdateStatusWindow();
    }

    private void BeginStartupUpdateCheck()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 4000 };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            await CheckForUpdatesAsync(silentIfUpToDate: true);
        };
        timer.Start();
    }

    private async Task CheckForUpdatesAsync(bool silentIfUpToDate)
    {
        if (_updateCheckInProgress)
            return;

        _updateCheckInProgress = true;
        try
        {
            var release = await GitHubUpdater.CheckForUpdateAsync(_cts.Token);
            if (release == null)
            {
                if (!silentIfUpToDate)
                    MessageBox.Show($"You are already on the latest version ({GitHubUpdater.GetCurrentVersion()}).", "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"A new version is available.\n\nCurrent: {GitHubUpdater.GetCurrentVersion()}\nLatest: {release.Version}\n\nDownload and apply the update now? The uploader will restart.",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result != DialogResult.Yes)
                return;

            // Show progress dialog
            using var progressForm = new UpdateProgressForm(_cts);
            progressForm.SetFileName(release.AssetName);

            var updateTask = Task.Run(async () =>
            {
                try
                {
                    await GitHubUpdater.DownloadAndApplyAsync(
                        release,
                        _cts.Token,
                        onProgress: (downloaded, total) => progressForm.SetProgress(downloaded, total)
                    );
                    
                    progressForm.SetCompleting();
                    await Task.Delay(800);
                    if (!progressForm.IsDisposed && progressForm.IsHandleCreated)
                        progressForm.BeginInvoke(() => progressForm.Close());
                }
                catch (OperationCanceledException)
                {
                    if (!progressForm.IsDisposed && progressForm.IsHandleCreated)
                        progressForm.BeginInvoke(() => progressForm.Close());
                }
                catch (Exception ex)
                {
                    Logger.Error("Update download failed", ex);
                    if (!progressForm.IsDisposed && progressForm.IsHandleCreated)
                    {
                        progressForm.BeginInvoke(() =>
                        {
                            MessageBox.Show($"Update failed:\n\n{ex.Message}", "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            progressForm.Close();
                        });
                    }
                }
            }, _cts.Token);

            var dialogResult = progressForm.ShowDialog();
            await updateTask;
            
            if (dialogResult == DialogResult.OK)
            {
                Logger.Info($"Applying update {release.TagName} - exiting app");
                _cts.Cancel();
                _watcher?.Dispose();
                _trayIcon.Visible = false;
                Application.Exit();
                // Give the app a moment to clean up, then force exit
                await Task.Delay(500);
                Environment.Exit(0);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("Update check failed", ex);
            if (!silentIfUpToDate)
                MessageBox.Show($"Update failed:\n\n{ex.Message}", "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _updateCheckInProgress = false;
        }
    }

    /// Safely set tray icon tooltip — Windows limits this to 63 characters.
    private void SetTrayText(string text)
    {
        if (_trayIcon == null)
            return;

        const int maxLen = 63;
        _trayIcon.Text = text.Length <= maxLen ? text : text[..maxLen];
    }

    private void OnNewClip(string filePath)
    {
        EnqueueClip(filePath);
    }

    private void EnqueueClip(string filePath, bool fromPersistence = false)
    {
        if (!File.Exists(filePath)) return;

        lock (_queuedSet)
        {
            if (!_queuedSet.Add(filePath)) return;
        }

        if (_settings.EnableQueuePersistence && !fromPersistence)
            PendingUploadQueueStore.Add(filePath);

        _pendingQueue.Enqueue(filePath);

        if (_queueProcessingEnabled)
        {
            _queueSignal.Release();
        }
        else
        {
            var fileName = Path.GetFileName(filePath);
            Logger.Info($"Queued locally (processing paused): {fileName}");
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.AddEventLog($"≡ Queued locally: {fileName}", Color.FromArgb(147, 197, 253));
            SetTrayText($"VELO Uploader — {_pendingQueue.Count} queued locally");
            RefreshMenu();
        }

        UpdateStatusWindow();
    }

    private async Task ProcessPendingQueueLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_queueProcessingEnabled)
            {
                try
                {
                    await Task.Delay(250, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                continue;
            }

            try
            {
                await _queueSignal.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_queueProcessingEnabled)
            {
                _queueSignal.Release();
                continue;
            }

            if (!_pendingQueue.TryDequeue(out var filePath))
                continue;

            _activeQueueFile = filePath;
            UpdateStatusWindow();

            ProcessClipResult result = new(false);
            try
            {
                result = await ProcessClip(filePath, ct);
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Clip processing cancelled (app shutting down)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Unexpected error processing clip: {Path.GetFileName(filePath)}", ex);
                SetTrayText("VELO Uploader — Error (see logs)");
                ShowToast("Error", Path.GetFileName(filePath), ex.Message);
                LocalCompressor.KillAll();
            }
            finally
            {
                lock (_queuedSet)
                {
                    _queuedSet.Remove(filePath);
                }

                if (string.Equals(_activeQueueFile, filePath, StringComparison.OrdinalIgnoreCase))
                    _activeQueueFile = null;

                if (result.Success)
                    _retryAttempts.TryRemove(filePath, out _);
                else if (result.Retryable)
                    ScheduleRetry(filePath, result.RetryAfter);

                if (_settings.EnableQueuePersistence && result.Success)
                    PendingUploadQueueStore.Remove(filePath);
                else if (_settings.EnableQueuePersistence && !result.Retryable)
                    PendingUploadQueueStore.Remove(filePath);

                UpdateStatusWindow();
            }
        }
    }

    private void ScheduleRetry(string filePath, TimeSpan? retryAfter)
    {
        if (!File.Exists(filePath))
        {
            if (_settings.EnableQueuePersistence)
                PendingUploadQueueStore.Remove(filePath);
            _retryAttempts.TryRemove(filePath, out _);
            return;
        }

        var attempt = _retryAttempts.AddOrUpdate(filePath, 1, (_, current) => current + 1);
        var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Min(300, 15 * Math.Pow(2, Math.Min(attempt - 1, 4))));
        var fileName = Path.GetFileName(filePath);

        Logger.Warn($"Will retry upload for {fileName} in {delay.TotalSeconds:0}s (attempt {attempt})");
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.AddEventLog($"↻ Queued for retry: {fileName} in {delay.TotalSeconds:0}s", Color.FromArgb(251, 191, 36));
            _settingsForm.ResetTask();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _cts.Token);
                if (!_cts.IsCancellationRequested)
                    EnqueueClip(filePath, fromPersistence: true);
            }
            catch (OperationCanceledException)
            {
            }
        }, _cts.Token);
    }

    private async Task<ProcessClipResult> ProcessClip(string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        var uploadPath = filePath;
        bool preCompressed = false;
        string? compressedTempFile = null;
        var originalSize = SafeFileLength(filePath);
        string? sourceHash = null;

        // Duplicate guard: skip upload if we've already uploaded identical content
        sourceHash = await ComputeFileHashAsync(filePath, ct);
        if (!string.IsNullOrWhiteSpace(sourceHash))
        {
            var duplicate = UploadHistoryManager.Load()
                .FirstOrDefault(e => e.Success && !string.IsNullOrWhiteSpace(e.FileHash) && string.Equals(e.FileHash, sourceHash, StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                Logger.Warn($"Skipped duplicate (same SHA-256 already uploaded): {fileName}");
                ShowToast("Duplicate skipped", fileName, "Already uploaded previously (same content hash)");

                HandleFileAfterUpload(filePath, _settings);

                UploadHistoryManager.Add(new UploadHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    FileName = fileName,
                    FileHash = sourceHash,
                    Success = true,
                    Url = duplicate.Url,
                    Error = "Skipped duplicate — same file hash already uploaded",
                    UsedCompression = false,
                    CompressionPreset = null,
                    SourceSizeBytes = originalSize,
                    UploadedSizeBytes = 0,
                });
                return new ProcessClipResult(true);
            }
        }

        // Quota check: bail out before compression or upload if user is over quota
        var quotaResult = await QuotaService.GetAsync(_settings, ct);
        if (quotaResult.Success && quotaResult.Quota!.WouldExceed(originalSize))
        {
            var quota = quotaResult.Quota!;
              var detail = $"Used {quota.UsedFormatted} / {quota.QuotaFormatted} — need {FormatBytes(originalSize)}, only {quota.FreeFormatted} free";
            Logger.Warn($"Quota exceeded, skipping: {fileName} — {detail}");
            SetTrayText("VELO Uploader — Quota exceeded");
            ShowToast("Quota exceeded", fileName, detail);
            SoundFeedback.PlayFailure(_settings.PlaySounds);
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.AddEventLog($"✗ Quota exceeded: {fileName} ({detail})", Color.FromArgb(248, 113, 113));
            UploadHistoryManager.Add(new UploadHistoryEntry
            {
                Timestamp = DateTime.Now,
                FileName = fileName,
                FileHash = sourceHash,
                Success = false,
                Error = detail,
                UsedCompression = false,
                SourceSizeBytes = originalSize,
                UploadedSizeBytes = 0,
            });
            return new ProcessClipResult(false);
        }

        // Local FFmpeg compression if enabled
        if (_settings.LocalCompress && LocalCompressor.IsAvailable())        {
            var compressionSlotHeld = false;
            try
            {
                // Limit concurrent compressions to 1 to prevent resource exhaustion
                await _processingQueue.WaitForCompressionSlotAsync(ct);
                compressionSlotHeld = true;
                
                ShowToast("New clip detected", $"Compressing: {fileName}", "Running local FFmpeg compression...");
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.UpdateTaskProgress(fileName, 0, $"Compressing locally ({_settings.CompressionPreset})...");
                    _settingsForm.AddEventLog($"⚙ Compressing: {fileName} ({_settings.CompressionPreset})", Color.FromArgb(59, 130, 246));
                }

                var compressProgress = new Progress<double>(p =>
                {
                    var percentage = (int)p;
                    SetTrayText($"VELO Uploader — Compressing {fileName} ({percentage}%)");
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                    {
                        _settingsForm.UpdateTaskProgress(fileName, percentage, $"Compressing locally ({_settings.CompressionPreset})...");
                    }
                });

                var lowImpact = _settings.AdaptiveCompressionWhenGaming && GameActivityDetector.IsLikelyGameRunning(filePath);
                if (lowImpact)
                    Logger.Info("Game activity detected — using low-impact compression mode.");

                var compressed = await LocalCompressor.CompressAsync(filePath, _settings.CompressionPreset, lowImpact, compressProgress, ct);
                if (compressed != null)
                {
                    uploadPath = compressed;
                    preCompressed = true;
                    compressedTempFile = compressed;
                    Logger.Info($"Using locally compressed file for upload: {Path.GetFileName(compressed)}");
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                    {
                        _settingsForm.UpdateTaskProgress(fileName, 100, "Compression complete - preparing upload...");
                        _settingsForm.AddEventLog($"✓ Compression complete: {fileName}", Color.FromArgb(74, 222, 128));
                    }

                }
                else
                {
                    if (_settings.StopOnCompressionFailure)
                    {
                        Logger.Error("Local compression failed — hard-stop enabled, skipping upload");
                        SetTrayText("VELO Uploader — Compression failed");
                        ShowToast("Compression failed", fileName, "Upload skipped because hard-stop is enabled");
                        SoundFeedback.PlayFailure(_settings.PlaySounds);
                        if (_settingsForm != null && !_settingsForm.IsDisposed)
                        {
                            _settingsForm.AddEventLog($"✗ Compression failed: {fileName}", Color.FromArgb(248, 113, 113));
                            _settingsForm.ResetTask();
                        }
                        UploadHistoryManager.Add(new UploadHistoryEntry
                        {
                            Timestamp = DateTime.Now,
                            FileName = fileName,
                            Success = false,
                            Error = "Compression failed; upload skipped by hard-stop setting",
                            UsedCompression = true,
                            CompressionPreset = _settings.CompressionPreset,
                            SourceSizeBytes = originalSize,
                            UploadedSizeBytes = 0,
                        });
                        return new ProcessClipResult(false);
                    }

                    Logger.Warn("Local compression failed, uploading original file");
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                    {
                        _settingsForm.AddEventLog($"⚠ Compression failed, uploading original: {fileName}", Color.FromArgb(251, 191, 36));
                    }
                }
            }
            finally
            {
                if (compressionSlotHeld)
                    _processingQueue.ReleaseCompressionSlot();
            }
        }
        else if (_settings.LocalCompress && !LocalCompressor.IsAvailable())
        {
            if (_settings.StopOnCompressionFailure)
            {
                Logger.Error("Local compression enabled but FFmpeg not found — hard-stop enabled, skipping upload");
                SetTrayText("VELO Uploader — FFmpeg not found");
                ShowToast("Compression unavailable", fileName, "FFmpeg not found; upload skipped");
                SoundFeedback.PlayFailure(_settings.PlaySounds);
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.AddEventLog($"✗ Compression unavailable: {fileName} (FFmpeg not found)", Color.FromArgb(248, 113, 113));
                    _settingsForm.ResetTask();
                }
                UploadHistoryManager.Add(new UploadHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    FileName = fileName,
                    Success = false,
                    Error = "FFmpeg not found; upload skipped by hard-stop setting",
                    UsedCompression = true,
                    CompressionPreset = _settings.CompressionPreset,
                    SourceSizeBytes = originalSize,
                    UploadedSizeBytes = 0,
                });
                return new ProcessClipResult(false);
            }

            Logger.Warn("Local compression enabled but FFmpeg not found — uploading original");
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.AddEventLog($"⚠ FFmpeg not found, uploading original: {fileName}", Color.FromArgb(251, 191, 36));
            }
        }

        ShowToast("Uploading", $"{fileName}", preCompressed ? "Uploading pre-compressed video..." : "Streaming upload in progress...");

        var progress = new Progress<double>(p =>
        {
            var percentage = (int)p;
            SetTrayText($"VELO Uploader — Uploading {fileName} ({percentage}%)");
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.UpdateTaskProgress(fileName, percentage, preCompressed ? "Uploading pre-compressed..." : "Uploading...");
            }
        });

        // Limit concurrent uploads to 2 to prevent network/server overload
        var uploadSlotHeld = false;
        try
        {
            await _processingQueue.WaitForUploadSlotAsync(ct);
            uploadSlotHeld = true;
            var result = await UploadService.UploadAsync(
                _settings.ServerUrl, _settings.ApiToken, uploadPath, progress, ct,
                maxRetries: _settings.MaxRetries, preCompressed: preCompressed, requireChecksum: _settings.RequireUploadChecksum);

            if (result.Success)
            {
                _uploadCount++;
                _successCount++;
                _totalBytes += new FileInfo(uploadPath).Length;
                var url = $"{_settings.ServerUrl.TrimEnd('/')}/v/{result.Slug}";
                ShowToast(result.Duplicate ? "Duplicate matched" : "Upload complete", fileName, result.Duplicate ? "Matched existing upload" : "Click to copy link", url, 6000);
                SoundFeedback.PlaySuccess(_settings.PlaySounds);
                SetTrayText($"VELO Uploader — {_uploadCount} uploaded this session");
                RefreshMenu();
                UpdateStatusWindow();
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.AddEventLog($"✓ {(result.Duplicate ? "Matched duplicate" : "Uploaded")}: {fileName}" + (string.IsNullOrWhiteSpace(result.TraceId) ? "" : $" (trace {result.TraceId})"), Color.FromArgb(74, 222, 128));
                    _settingsForm.ResetTask();
                }
                // Quota will have changed — invalidate cache and refresh the status label
                QuotaService.Invalidate();
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                    _settingsForm.RefreshQuotaLabel();
                UploadHistoryManager.Add(new UploadHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    FileName = fileName,
                    FileHash = sourceHash,
                    Success = true,
                    Url = url,
                    UsedCompression = preCompressed,
                    CompressionPreset = preCompressed ? _settings.CompressionPreset : null,
                    SourceSizeBytes = originalSize,
                    UploadedSizeBytes = SafeFileLength(uploadPath),
                });

                // Handle original after upload if compression wasn't used
                // (if compression was used, original was already handled right after compression)
                if (!preCompressed)
                {
                    HandleFileAfterUpload(filePath, _settings);
                }

                // Clean up compressed temp file
                if (compressedTempFile != null)
                {
                    try { File.Delete(compressedTempFile); }
                    catch { }
                }
                HandleFileAfterUpload(filePath, _settings);
                return new ProcessClipResult(true);
            }
            else
            {
                ShowToast("Upload failed", $"{fileName}", result.Error);
                SoundFeedback.PlayFailure(_settings.PlaySounds);
                SetTrayText("VELO Uploader — Watching");
                UpdateStatusWindow();
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.AddEventLog($"✗ Failed: {fileName} ({result.Error})", Color.FromArgb(248, 113, 113));
                    _settingsForm.ResetTask();
                }
                UploadHistoryManager.Add(new UploadHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    FileName = fileName,
                    FileHash = sourceHash,
                    Success = false,
                    Error = result.Error,
                    UsedCompression = preCompressed,
                    CompressionPreset = preCompressed ? _settings.CompressionPreset : null,
                    SourceSizeBytes = originalSize,
                    UploadedSizeBytes = preCompressed ? SafeFileLength(uploadPath) : originalSize,
                });

                // Clean up compressed temp file on failure too
                if (compressedTempFile != null)
                {
                    try { File.Delete(compressedTempFile); }
                    catch { }
                }
                return new ProcessClipResult(false, result.Retryable, result.RetryAfter);
            }
        }
        finally
        {
            if (uploadSlotHeld)
                _processingQueue.ReleaseUploadSlot();
        }
    }

    private void ShowToast(string title, string body, string? subtitle = null, string? copyToClipboard = null, int durationMs = 4000)
    {
        if (!_settings.ShowNotifications) return;
        try
        {
            ToastNotification.Show(title, body, subtitle, copyToClipboard, durationMs);
        }
        catch
        {
            if (_trayIcon == null)
                return;

            _trayIcon.BalloonTipTitle = title;
            _trayIcon.BalloonTipText = body;
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private SettingsForm? _settingsForm;
    private QuickEditForm? _quickEditForm;

    private void ShowSettings() => ShowSettingsOnTab(0);

    private void ShowQuickEditor()
    {
        if (!FFmpegHelper.IsFFmpegAvailable())
        {
            CheckFFmpegInstallation();
            if (!FFmpegHelper.IsFFmpegAvailable())
            {
                MessageBox.Show(FFmpegHelper.GetFFmpegNotFoundMessage(), "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (_quickEditForm != null && !_quickEditForm.IsDisposed)
        {
            _quickEditForm.BringToFront();
            _quickEditForm.Focus();
            return;
        }

        _quickEditForm = new QuickEditForm(_settings.WatchFolder);
        _quickEditForm.FormClosed += (_, _) => _quickEditForm = null;
        _quickEditForm.Show();
    }

    private void ShowSettingsOnTab(int tabIndex)
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.ShowTab(tabIndex);
            _settingsForm.BringToFront();
            UpdateStatusWindow();
            return;
        }

        _settingsForm = new SettingsForm(_settings, tabIndex, SetQueueProcessingEnabled, ShowQuickEditor);
        
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            _queueProcessingEnabled = _settings.AutoProcessQueue;
            // Restart watcher with potentially new settings
            if (IsConfigured() && _watcher?.IsWatching != true)
                StartWatching();
            else if (IsConfigured())
                StartWatching(); // Restart with new folder
        };

        _settingsForm.Show();
        UpdateStatusWindow();
    }

    private void UpdateStatusWindow()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.UpdateStats(_uploadCount, _successCount, _totalBytes);
            _settingsForm.UpdateSystemStatus(_watcher?.IsWatching == true);
            _settingsForm.UpdateQueueStatus(_queueProcessingEnabled, GetPendingQueueSnapshot());
        }
    }

    private List<string> GetPendingQueueSnapshot()
    {
        var pending = new List<string>();

        if (!string.IsNullOrWhiteSpace(_activeQueueFile))
            pending.Add(_activeQueueFile);

        foreach (var file in _pendingQueue.ToArray())
        {
            if (!pending.Contains(file, StringComparer.OrdinalIgnoreCase))
                pending.Add(file);
        }

        lock (_queuedSet)
        {
            foreach (var file in _queuedSet)
            {
                if (!pending.Contains(file, StringComparer.OrdinalIgnoreCase))
                    pending.Add(file);
            }
        }

        if (_settings.EnableQueuePersistence)
        {
            foreach (var file in PendingUploadQueueStore.Load())
            {
                if (!pending.Contains(file, StringComparer.OrdinalIgnoreCase))
                    pending.Add(file);
            }
        }

        return pending;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _queueSignal.Dispose();
            LocalCompressor.KillAll();
            _watcher?.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private static long SafeFileLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    private static void HandleFileAfterUpload(string filePath, AppSettings settings)
    {
        if (settings.MoveAfterUpload && !string.IsNullOrWhiteSpace(settings.MoveToFolder))
        {
            try
            {
                Directory.CreateDirectory(settings.MoveToFolder);
                var fileName = Path.GetFileName(filePath);
                var destPath = Path.Combine(settings.MoveToFolder, fileName);
                
                // If file already exists at destination, add timestamp or number suffix
                if (File.Exists(destPath))
                {
                    var name = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var newName = $"{name}_{timestamp}{ext}";
                    destPath = Path.Combine(settings.MoveToFolder, newName);
                }
                
                File.Move(filePath, destPath, overwrite: false);
                Logger.Info($"Moved after upload: {fileName} → {destPath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not move file to archive folder: {ex.Message}");
            }
        }
        else if (settings.DeleteAfterUpload)
        {
            try
            {
                File.Delete(filePath);
                Logger.Info($"Deleted after upload: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to delete after upload: {Path.GetFileName(filePath)}", ex);
            }
        }
    }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576L) return $"{bytes / 1_048_576.0:F1} MB";
            return $"{bytes / 1024.0:F1} KB";
        }

    private static async Task<string?> ComputeFileHashAsync(string filePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            using var sha = SHA256.Create();
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, true);
            var hashBytes = await sha.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hashBytes);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to compute SHA-256 hash for {Path.GetFileName(filePath)}: {ex.Message}");
            return null;
        }
    }
}
