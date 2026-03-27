namespace VeloUploader;

using System.Drawing.Drawing2D;

public class UpdateProgressForm : Form
{
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Label _fileLabel;
    private readonly Label _speedLabel;
    private readonly Button _cancelBtn;
    private DateTime _startTime = DateTime.Now;
    private long _totalBytes;
    private long _downloadedBytes;

    // Dark theme colors
    static readonly Color C_BG = Color.FromArgb(12, 12, 15);
    static readonly Color C_PANEL = Color.FromArgb(18, 18, 22);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_T1 = Color.FromArgb(240, 240, 245);
    static readonly Color C_T2 = Color.FromArgb(155, 155, 165);
    static readonly Color C_T3 = Color.FromArgb(90, 90, 100);
    static readonly Color C_ACCENT = Color.FromArgb(124, 58, 237);
    static readonly Color C_ACCENT_H = Color.FromArgb(139, 78, 245);

    private CancellationTokenSource? _cts;

    public UpdateProgressForm(CancellationTokenSource cts)
    {
        _cts = cts;
        SuspendLayout();

        Text = "Updating VELO Uploader";
        ClientSize = new Size(480, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        // Load icon
        try
        {
            var stream = typeof(UpdateProgressForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // Header panel
        var header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(480, 50),
            BackColor = C_PANEL,
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_ACCENT, 2);
            e.Graphics.DrawLine(pen, 0, 49, Width, 49);
        };
        header.Controls.Add(new Label
        {
            Text = "⬇ Downloading update...",
            Location = new Point(16, 14),
            AutoSize = true,
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            BackColor = Color.Transparent,
        });
        Controls.Add(header);

        // Status label
        _statusLabel = new Label
        {
            Location = new Point(24, 66),
            Size = new Size(432, 20),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8.5f),
            Text = "Preparing download...",
        };
        Controls.Add(_statusLabel);

        // Filename label
        _fileLabel = new Label
        {
            Location = new Point(24, 88),
            Size = new Size(432, 18),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 8f),
            Text = "",
        };
        Controls.Add(_fileLabel);

        // Progress bar (custom dark style)
        _progressBar = new ProgressBar
        {
            Location = new Point(24, 114),
            Size = new Size(432, 12),
            Value = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            BackColor = C_PANEL,
            ForeColor = C_ACCENT,
        };
        Controls.Add(_progressBar);

        // Speed label
        _speedLabel = new Label
        {
            Location = new Point(24, 132),
            Size = new Size(432, 16),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 7.5f),
            Text = "",
            TextAlign = ContentAlignment.TopRight,
        };
        Controls.Add(_speedLabel);

        // Cancel button
        _cancelBtn = new Button
        {
            Text = "Cancel",
            Location = new Point(192, 164),
            Size = new Size(96, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = C_PANEL,
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
        };
        _cancelBtn.FlatAppearance.BorderSize = 1;
        _cancelBtn.FlatAppearance.BorderColor = C_BORDER;
        _cancelBtn.FlatAppearance.MouseOverBackColor = C_PANEL;
        _cancelBtn.Click += (_, _) =>
        {
            _cts?.Cancel();
            _cancelBtn.Enabled = false;
            _cancelBtn.Text = "Cancelling...";
        };
        Controls.Add(_cancelBtn);

        ResumeLayout();
    }

    public void SetFileName(string filename)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetFileName(filename));
            return;
        }

        _fileLabel.Text = $"📦 {Path.GetFileName(filename)}";
    }

    public void SetProgress(long downloadedBytes, long totalBytes)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetProgress(downloadedBytes, totalBytes));
            return;
        }

        _downloadedBytes = downloadedBytes;
        _totalBytes = totalBytes;

        var percent = totalBytes > 0 ? (int)((downloadedBytes * 100L) / totalBytes) : 0;
        _progressBar.Value = Math.Max(0, Math.Min(100, percent));

        var downloadedMB = downloadedBytes / (1024.0 * 1024.0);
        var totalMB = totalBytes / (1024.0 * 1024.0);
        var elapsed = DateTime.Now - _startTime;
        var elapsedSecs = Math.Max(1, elapsed.TotalSeconds);
        var speedMBps = downloadedMB / elapsedSecs;

        _statusLabel.Text = $"{percent}% • {downloadedMB:F1} MB of {totalMB:F1} MB";
        _speedLabel.Text = $"⚡ {speedMBps:F2} MB/s • ETA: {EstimateTimeRemaining(downloadedBytes, totalBytes, elapsedSecs)}";
    }

    private string EstimateTimeRemaining(long downloadedBytes, long totalBytes, double elapsedSecs)
    {
        if (downloadedBytes <= 0 || downloadedBytes >= totalBytes)
            return "";

        var remainingBytes = totalBytes - downloadedBytes;
        var speedBytesPerSec = downloadedBytes / elapsedSecs;
        var remainingSecs = (int)(remainingBytes / speedBytesPerSec);

        if (remainingSecs < 60)
            return $"{remainingSecs}s";
        else if (remainingSecs < 3600)
            return $"{remainingSecs / 60}m {remainingSecs % 60}s";
        else
            return $"{remainingSecs / 3600}h {(remainingSecs % 3600) / 60}m";
    }

    public void SetCompleting()
    {
        if (InvokeRequired)
        {
            Invoke(SetCompleting);
            return;
        }

        _cancelBtn.Enabled = false;
        _statusLabel.Text = "Finalizing update...";
        _progressBar.Value = 100;
        _speedLabel.Text = "Extracting and applying...";
        DialogResult = DialogResult.OK;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cancelBtn.Enabled)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // Draw custom progress bar outline
        using var pen = new Pen(C_ACCENT, 1.5f);
        e.Graphics.DrawRectangle(pen, _progressBar.Location.X - 1, _progressBar.Location.Y - 1, _progressBar.Width + 1, _progressBar.Height + 1);
    }
}
