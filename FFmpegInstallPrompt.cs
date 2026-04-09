namespace VeloUploader;

/// <summary>
/// Dialog prompt for installing FFmpeg via winget on first launch if not found.
/// </summary>
public class FFmpegInstallPrompt : Form
{
    private static readonly Color C_BG = Color.FromArgb(12, 12, 15);
    private static readonly Color C_PANEL = Color.FromArgb(18, 18, 22);
    private static readonly Color C_T1 = Color.FromArgb(240, 240, 245);
    private static readonly Color C_T2 = Color.FromArgb(155, 155, 165);
    private static readonly Color C_T3 = Color.FromArgb(90, 90, 100);
    private static readonly Color C_ACCENT = Color.FromArgb(124, 58, 237);
    private static readonly Color C_ACCENT_H = Color.FromArgb(139, 78, 245);
    private static readonly Color C_BTN_BG = Color.FromArgb(38, 38, 46);
    private static readonly Color C_BTN_BORDER = Color.FromArgb(40, 40, 50);

    private System.Windows.Forms.Timer? _progressTimer;
    private int _progressValue = 0;
    private ProgressBar? _progressBar;
    private Label? _statusLabel;
    private Button? _installBtn;
    private Button? _skipBtn;
    private Label? _titleLabel;
    private Label? _messageLabel;

    public FFmpegInstallPrompt()
    {
        InitializeComponent();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowDarkMode.ApplyForSystemTheme(Handle);
    }

    private void InitializeComponent()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 360);
        MinimumSize = new Size(520, 360);
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "VELO Uploader • Local Compression";

        var header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 60),
            BackColor = C_PANEL,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_ACCENT, 2);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        Controls.Add(header);

        _titleLabel = new Label
        {
            Text = "ENABLE LOCAL COMPRESSION",
            Location = new Point(18, 12),
            Size = new Size(340, 18),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = C_T1,
            BackColor = Color.Transparent,
        };
        header.Controls.Add(_titleLabel);

        header.Controls.Add(new Label
        {
            Text = "INSTALL FFMPEG ONCE TO UNLOCK LIGHTER, FASTER UPLOADS.",
            Location = new Point(18, 31),
            Size = new Size(470, 16),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = C_T3,
            BackColor = Color.Transparent,
        });

        _messageLabel = new Label
        {
            Text = "FFmpeg powers local transcoding, filmstrips, exports, and bandwidth-friendly uploads before clips ever hit the server.",
            Location = new Point(20, 78),
            Size = new Size(480, 38),
            Font = new Font("Segoe UI", 8.75f),
            ForeColor = C_T2,
            BackColor = Color.Transparent,
        };
        Controls.Add(_messageLabel);

        var benefitsPanel = new Panel
        {
            Location = new Point(20, 128),
            Size = new Size(480, 96),
            BackColor = C_PANEL,
        };
        benefitsPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BTN_BORDER, 1);
            e.Graphics.DrawRectangle(pen, 0, 0, benefitsPanel.Width - 1, benefitsPanel.Height - 1);
        };
        benefitsPanel.Controls.Add(new Label
        {
            Text = "WHAT YOU GET",
            Location = new Point(12, 10),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = C_ACCENT,
            BackColor = Color.Transparent,
        });
        benefitsPanel.Controls.Add(new Label
        {
            Text = "• lower upload size with CPU/GPU presets\n• smoother editor previews and timeline exports\n• one-time install through winget with no manual setup",
            Location = new Point(12, 30),
            Size = new Size(452, 52),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = C_T2,
            BackColor = Color.Transparent,
        });
        Controls.Add(benefitsPanel);

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 238),
            Size = new Size(480, 10),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        Controls.Add(_progressBar);

        _statusLabel = new Label
        {
            Text = "Installing FFmpeg...",
            Location = new Point(20, 252),
            Size = new Size(480, 18),
            Font = new Font("Segoe UI", 8f),
            ForeColor = C_T3,
            BackColor = Color.Transparent,
            Visible = false,
        };
        Controls.Add(_statusLabel);

        _installBtn = new Button
        {
            Text = "Install FFmpeg",
            Location = new Point(348, 300),
            Size = new Size(152, 32),
            BackColor = C_ACCENT,
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _installBtn.FlatAppearance.BorderSize = 1;
        _installBtn.FlatAppearance.BorderColor = C_BTN_BORDER;
        _installBtn.Click += InstallBtn_Click;
        _installBtn.MouseEnter += (_, _) => _installBtn.BackColor = C_ACCENT_H;
        _installBtn.MouseLeave += (_, _) => _installBtn.BackColor = C_ACCENT;
        Controls.Add(_installBtn);

        _skipBtn = new Button
        {
            Text = "Skip",
            Location = new Point(20, 300),
            Size = new Size(92, 32),
            BackColor = C_BTN_BG,
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _skipBtn.FlatAppearance.BorderSize = 1;
        _skipBtn.FlatAppearance.BorderColor = C_BTN_BORDER;
        _skipBtn.Click += SkipBtn_Click;
        _skipBtn.MouseEnter += (_, _) => _skipBtn.BackColor = Color.FromArgb(52, 52, 62);
        _skipBtn.MouseLeave += (_, _) => _skipBtn.BackColor = C_BTN_BG;
        Controls.Add(_skipBtn);

        SizeChanged += (_, _) =>
        {
            header.Width = ClientSize.Width;
            var contentWidth = Math.Max(280, ClientSize.Width - 40);
            _messageLabel!.Size = new Size(contentWidth, 38);
            benefitsPanel.Size = new Size(contentWidth, benefitsPanel.Height);
            _progressBar!.Size = new Size(contentWidth, _progressBar.Height);
            _statusLabel!.Size = new Size(contentWidth, _statusLabel.Height);
            var btnY = Math.Max(280, ClientSize.Height - 20 - _installBtn!.Height);
            _skipBtn!.Location = new Point(20, btnY);
            _installBtn.Location = new Point(ClientSize.Width - 20 - _installBtn.Width, btnY);
        };
    }

    private void InstallBtn_Click(object? sender, EventArgs e)
    {
        _installBtn!.Enabled = false;
        _skipBtn!.Enabled = false;
        _progressBar!.Visible = true;
        _statusLabel!.Visible = true;

        // Start progress animation
        _progressTimer = new System.Windows.Forms.Timer();
        _progressTimer.Interval = 100;
        _progressTimer.Tick += (_, _) =>
        {
            _progressValue += 5;
            if (_progressValue > 100) _progressValue = 100;
            _progressBar.Value = _progressValue;

            if (_progressValue >= 100)
            {
                _progressTimer.Stop();
                _statusLabel.Text = "Installation complete! Restart VELO to use compression.";
            }
        };
        _progressTimer.Start();

        // Run installation in background
        Task.Run(() =>
        {
            try
            {
                if (FFmpegHelper.TryInstallFFmpeg())
                {
                    Logger.Info("FFmpeg installation completed or already installed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("FFmpeg installation error", ex);
            }

            // Wait a bit then close
            Thread.Sleep(2000);
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(() => Close());
        });
    }

    private void SkipBtn_Click(object? sender, EventArgs e)
    {
        Logger.Info("User skipped FFmpeg installation");
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _progressTimer?.Dispose();
        base.OnFormClosed(e);
    }
}
