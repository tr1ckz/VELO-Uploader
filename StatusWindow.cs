using System.Drawing.Drawing2D;

namespace VeloUploader;

/// <summary>
/// Beautiful real-time status dashboard showing uploads, stats, version, and system info.
/// </summary>
public class StatusWindow : Form
{
    private ProgressBar _taskProgressBar;
    private Label _taskStatusLabel;
    private Label _taskFileLabel;
    private Label _taskSpeedLabel;
    private RichTextBox _eventLogBox;
    private Label _statsUploadsLabel;
    private Label _statsSuccessLabel;
    private Label _statsSizeLabel;
    private Label _systemStatusLabel;
    private Label _versionLabel;
    private Button _updateCheckBtn;
    private Label _gpuStatusLabel;
    private Panel _currentTaskPanel;

    // Colors
    static readonly Color C_BG = Color.FromArgb(12, 12, 15);
    static readonly Color C_PANEL = Color.FromArgb(18, 18, 22);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_T1 = Color.FromArgb(240, 240, 245);
    static readonly Color C_T2 = Color.FromArgb(155, 155, 165);
    static readonly Color C_T3 = Color.FromArgb(90, 90, 100);
    static readonly Color C_ACCENT = Color.FromArgb(124, 58, 237);
    static readonly Color C_ACCENT_H = Color.FromArgb(139, 78, 245);
    static readonly Color C_GREEN = Color.FromArgb(74, 222, 128);
    static readonly Color C_RED = Color.FromArgb(248, 113, 113);
    static readonly Color C_BLUE = Color.FromArgb(59, 130, 246);
    static readonly Color C_ORANGE = Color.FromArgb(251, 146, 60);

    private int _sessionUploads = 0;
    private int _sessionSuccessful = 0;
    private long _sessionBytes = 0;

    public StatusWindow(object? unused)
    {
        SuspendLayout();

        Text = "VELO Uploader — Status";
        ClientSize = new Size(720, 600);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;
        MinimumSize = new Size(600, 400);

        // Load icon
        try
        {
            var stream = typeof(StatusWindow).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // ─────────────────────────────────────
        // HEADER
        // ─────────────────────────────────────
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = C_PANEL,
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_ACCENT, 2);
            e.Graphics.DrawLine(pen, 0, 59, Width, 59);
        };
        header.Controls.Add(MkLabel("📊 VELO Status Dashboard", 14, 10, new Font("Segoe UI", 13f, FontStyle.Bold), C_T1));
        Controls.Add(header);

        // ─────────────────────────────────────
        // MAIN SCROLL
        // ─────────────────────────────────────
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = C_BG,
            AutoScroll = true,
        };

        int y = 14;

        // Current Task Section
        MkSection(scroll, "CURRENT TASK", 14, y); y += 22;

        _currentTaskPanel = new Panel
        {
            Location = new Point(14, y),
            Size = new Size(692, 90),
            BackColor = C_PANEL,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _currentTaskPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BORDER, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _currentTaskPanel.Width - 1, _currentTaskPanel.Height - 1);
        };

        _taskFileLabel = new Label
        {
            Location = new Point(10, 8),
            Size = new Size(672, 18),
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Text = "Idle — waiting for clips...",
        };
        _currentTaskPanel.Controls.Add(_taskFileLabel);

        _taskStatusLabel = new Label
        {
            Location = new Point(10, 28),
            Size = new Size(672, 16),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8f),
            Text = "",
        };
        _currentTaskPanel.Controls.Add(_taskStatusLabel);

        _taskProgressBar = new ProgressBar
        {
            Location = new Point(10, 46),
            Size = new Size(672, 8),
            Value = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            BackColor = C_PANEL,
            ForeColor = C_ACCENT,
        };
        _currentTaskPanel.Controls.Add(_taskProgressBar);

        _taskSpeedLabel = new Label
        {
            Location = new Point(10, 58),
            Size = new Size(672, 14),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 7.5f),
            TextAlign = ContentAlignment.TopRight,
            Text = "",
        };
        _currentTaskPanel.Controls.Add(_taskSpeedLabel);

        scroll.Controls.Add(_currentTaskPanel);
        y += 100;

        // Stats Section
        MkSection(scroll, "SESSION STATISTICS", 14, y); y += 22;

        _statsUploadsLabel = MkLabel("📤 Uploads: 0", 14, y, new Font("Segoe UI", 9f, FontStyle.Bold), C_GREEN);
        scroll.Controls.Add(_statsUploadsLabel);
        y += 24;

        _statsSuccessLabel = MkLabel("✓ Success rate: N/A", 14, y, new Font("Segoe UI", 9f), C_T2);
        scroll.Controls.Add(_statsSuccessLabel);
        y += 24;

        _statsSizeLabel = MkLabel("📦 Total size: 0 MB", 14, y, new Font("Segoe UI", 9f), C_T2);
        scroll.Controls.Add(_statsSizeLabel);
        y += 28;

        // System Status Section
        MkSection(scroll, "SYSTEM STATUS", 14, y); y += 22;

        _systemStatusLabel = MkLabel("● Watching", 14, y, new Font("Segoe UI", 9f, FontStyle.Bold), C_GREEN);
        scroll.Controls.Add(_systemStatusLabel);
        y += 24;

        _gpuStatusLabel = MkLabel("GPU: Checking...", 14, y, new Font("Segoe UI", 9f), C_T2);
        scroll.Controls.Add(_gpuStatusLabel);
        y += 24;

        var ffmpegStatus = LocalCompressor.IsAvailable() ? "✓ FFmpeg available" : "✗ FFmpeg not found";
        var ffmpegColor = LocalCompressor.IsAvailable() ? C_GREEN : C_RED;
        var ffmpegLabel = MkLabel(ffmpegStatus, 14, y, new Font("Segoe UI", 9f), ffmpegColor);
        scroll.Controls.Add(ffmpegLabel);
        y += 28;

        // Version Section
        MkSection(scroll, "APPLICATION", 14, y); y += 22;

        _versionLabel = MkLabel($"v{GitHubUpdater.GetCurrentVersion()}", 14, y, new Font("Segoe UI", 9f, FontStyle.Bold), C_ACCENT);
        scroll.Controls.Add(_versionLabel);

        _updateCheckBtn = new Button
        {
            Location = new Point(200, y - 2),
            Size = new Size(120, 28),
            Text = "Check Updates",
            FlatStyle = FlatStyle.Flat,
            BackColor = C_ACCENT,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f),
            Cursor = Cursors.Hand,
        };
        _updateCheckBtn.FlatAppearance.BorderSize = 0;
        _updateCheckBtn.FlatAppearance.MouseOverBackColor = C_ACCENT_H;
        _updateCheckBtn.Click += (_, _) => OnCheckUpdatesClicked?.Invoke();
        scroll.Controls.Add(_updateCheckBtn);
        y += 32;

        // Event Log Section
        MkSection(scroll, "EVENT LOG", 14, y); y += 22;

        _eventLogBox = new RichTextBox
        {
            Location = new Point(14, y),
            Size = new Size(692, 200),
            BackColor = C_PANEL,
            ForeColor = C_T2,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8f),
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        scroll.Controls.Add(_eventLogBox);

        Controls.Add(scroll);

        // Check GPU availability
        CheckGPUStatus();

        ResumeLayout();
    }

    private void InvokeIfNeeded(Action action)
    {
        try
        {
            // Only invoke if the window handle has been created and we're not on the UI thread
            if (IsHandleCreated && InvokeRequired)
                BeginInvoke(action);
            else if (IsHandleCreated)
                action();
            // If handle not created yet, skip the update (will be called again later)
        }
        catch
        {
            // Silently ignore invoke errors if window is being closed/disposed
        }
    }

    private void CheckGPUStatus()
    {
        Task.Run(() =>
        {
            var gpuAvail = LocalCompressor.IsGPUAvailable();
            InvokeIfNeeded(() =>
            {
                _gpuStatusLabel.Text = gpuAvail ? "✓ GPU (NVIDIA NVENC) available" : "○ GPU not available (CPU mode)";
                _gpuStatusLabel.ForeColor = gpuAvail ? C_GREEN : C_T3;
            });
        });
    }

    private static Label MkLabel(string text, int x, int y, Font font, Color color)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = font,
            ForeColor = color,
            BackColor = Color.Transparent,
        };
    }

    private static void MkSection(Control parent, string title, int x, int y)
    {
        var lbl = new Label
        {
            Text = title,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = C_ACCENT,
            BackColor = Color.Transparent,
        };
        parent.Controls.Add(lbl);
    }

    public void UpdateTaskProgress(string fileName, int progress, string status = "")
    {
        InvokeIfNeeded(() =>
        {
            _taskFileLabel.Text = $"📁 {fileName}";
            _taskProgressBar.Value = Math.Max(0, Math.Min(100, progress));
            _taskStatusLabel.Text = status;
            if (progress == 100)
                _taskSpeedLabel.Text = "✓ Complete";
        });
    }

    public void UpdateTaskSpeed(double speedMBps, string eta)
    {
        InvokeIfNeeded(() =>
        {
            _taskSpeedLabel.Text = $"⚡ {speedMBps:F2} MB/s   ETA: {eta}";
        });
    }

    public void UpdateStats(int uploads, int successful, long bytes)
    {
        InvokeIfNeeded(() =>
        {
            _sessionUploads = uploads;
            _sessionSuccessful = successful;
            _sessionBytes = bytes;

            _statsUploadsLabel.Text = $"📤 Uploads: {uploads}";
            var successRate = uploads > 0 ? (successful * 100 / uploads) : 0;
            _statsSuccessLabel.Text = $"✓ Success rate: {successRate}% ({successful}/{uploads})";
            var sizeMB = bytes / (1024.0 * 1024.0);
            _statsSizeLabel.Text = $"📦 Total size: {sizeMB:F1} MB";
        });
    }

    public void UpdateSystemStatus(bool isWatching)
    {
        InvokeIfNeeded(() =>
        {
            if (isWatching)
            {
                _systemStatusLabel.Text = "● Watching";
                _systemStatusLabel.ForeColor = C_GREEN;
            }
            else
            {
                _systemStatusLabel.Text = "⏸ Paused";
                _systemStatusLabel.ForeColor = C_ORANGE;
            }
        });
    }

    public void AddEventLog(string message, Color color)
    {
        InvokeIfNeeded(() =>
        {
            _eventLogBox.SelectionStart = _eventLogBox.TextLength;
            _eventLogBox.SelectionLength = 0;
            _eventLogBox.SelectionColor = color;
            _eventLogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            _eventLogBox.ScrollToCaret();
        });
    }

    public void ResetTask()
    {
        InvokeIfNeeded(() =>
        {
            _taskFileLabel.Text = "Idle — waiting for clips...";
            _taskProgressBar.Value = 0;
            _taskStatusLabel.Text = "";
            _taskSpeedLabel.Text = "";
        });
    }

    public event Action? OnCheckUpdatesClicked;
}
