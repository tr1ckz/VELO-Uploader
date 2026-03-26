namespace VeloUploader;

public class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AppSettings _settings;
    private ClipWatcher? _watcher;
    private int _uploadCount;

    public TrayContext()
    {
        _settings = AppSettings.Load();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "VELO Uploader",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        if (IsConfigured())
            StartWatching();
        else
            ShowSettings();
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

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _watcher?.Dispose();
            _trayIcon.Visible = false;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void RefreshMenu()
    {
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = BuildMenu();
    }

    private void StartWatching()
    {
        if (!IsConfigured())
        {
            ShowNotification("VELO Uploader", "Please configure your server URL and API token first.");
            ShowSettings();
            return;
        }

        _watcher?.Dispose();
        _watcher = new ClipWatcher(_settings, OnNewClip);
        _watcher.Start();

        _trayIcon.Text = $"VELO Uploader — Watching {_settings.WatchFolder}";
        RefreshMenu();
        ShowNotification("VELO Uploader", $"Watching for new clips in:\n{_settings.WatchFolder}");
    }

    private void StopWatching()
    {
        _watcher?.Stop();
        _trayIcon.Text = "VELO Uploader — Paused";
        RefreshMenu();
    }

    private async void OnNewClip(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        ShowNotification("New clip detected", $"Uploading: {fileName}");

        var progress = new Progress<double>(p =>
        {
            _trayIcon.Text = $"VELO Uploader — Uploading {fileName} ({p:F0}%)";
        });

        var result = await UploadService.UploadAsync(
            _settings.ServerUrl, _settings.ApiToken, filePath, progress);

        if (result.Success)
        {
            _uploadCount++;
            var url = $"{_settings.ServerUrl.TrimEnd('/')}/v/{result.Slug}";
            ShowNotification("Upload complete", $"{fileName}\n{url}");
            _trayIcon.Text = $"VELO Uploader — {_uploadCount} uploaded this session";
        }
        else
        {
            ShowNotification("Upload failed", $"{fileName}\n{result.Error}");
            _trayIcon.Text = "VELO Uploader — Watching";
        }
    }

    private void ShowNotification(string title, string message)
    {
        if (!_settings.ShowNotifications) return;
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(3000);
    }

    private SettingsForm? _settingsForm;

    private void ShowSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.BringToFront();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            // Restart watcher with potentially new settings
            if (IsConfigured() && _watcher?.IsWatching != true)
                StartWatching();
            else if (IsConfigured())
                StartWatching(); // Restart with new folder
        };
        _settingsForm.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher?.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
