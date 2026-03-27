namespace VeloUploader;

/// <summary>
/// Dialog prompt for installing FFmpeg via winget on first launch if not found.
/// </summary>
public class FFmpegInstallPrompt : Form
{
    private static readonly Color C_BG = Color.FromArgb(30, 30, 30);           // Dark background
    private static readonly Color C_T1 = Color.FromArgb(240, 240, 245);        // Primary text (white)
    private static readonly Color C_T3 = Color.FromArgb(136, 136, 136);        // Secondary text (gray)
    private static readonly Color C_ACCENT = Color.FromArgb(99, 102, 241);     // Accent (purple)
    private static readonly Color C_ACCENT_H = Color.FromArgb(85, 88, 224);    // Accent hover
    private static readonly Color C_BTN_BG = Color.FromArgb(42, 42, 42);       // Button background
    private static readonly Color C_BTN_BORDER = Color.FromArgb(68, 68, 68);   // Button border

    private System.Windows.Forms.Timer? _progressTimer;
    private int _progressValue = 0;
    private ProgressBar? _progressBar;
    private Label? _statusLabel;
    private Button? _installBtn;
    private Button? _skipBtn;

    public FFmpegInstallPrompt()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(500, 410);
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 10f);
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        Text = "FFmpeg Installation";

        // Title
        var titleLbl = new Label
        {
            Text = "Install FFmpeg for Video Compression?",
            Location = new Point(20, 20),
            Size = new Size(460, 30),
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = C_T1,
            BackColor = Color.Transparent,
        };
        Controls.Add(titleLbl);

        // Message
        var msgLbl = new Label
        {
            Text = "FFmpeg enables local video compression before upload, saving bandwidth and storage space.\n\nThe installation takes 1-2 minutes and includes full codec support with hardware acceleration.",
            Location = new Point(20, 60),
            Size = new Size(460, 110),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = C_T3,
            BackColor = Color.Transparent,
            AutoSize = false,
        };
        Controls.Add(msgLbl);

        // Progress bar (hidden initially)
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 185),
            Size = new Size(460, 8),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        Controls.Add(_progressBar);

        // Status label (hidden initially)
        _statusLabel = new Label
        {
            Text = "Installing FFmpeg...",
            Location = new Point(20, 200),
            Size = new Size(460, 20),
            Font = new Font("Segoe UI", 9f),
            ForeColor = C_T3,
            BackColor = Color.Transparent,
            Visible = false,
        };
        Controls.Add(_statusLabel);

        // Buttons
        _installBtn = new Button
        {
            Text = "Install FFmpeg",
            Location = new Point(300, 350),
            Size = new Size(180, 40),
            BackColor = C_ACCENT,
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _installBtn.FlatAppearance.BorderSize = 0;
        _installBtn.Click += InstallBtn_Click;
        _installBtn.MouseEnter += (_, _) => _installBtn.BackColor = C_ACCENT_H;
        _installBtn.MouseLeave += (_, _) => _installBtn.BackColor = C_ACCENT;
        Controls.Add(_installBtn);

        _skipBtn = new Button
        {
            Text = "Skip for Now",
            Location = new Point(20, 350),
            Size = new Size(120, 40),
            BackColor = C_BTN_BG,
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 10f),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
        };
        _skipBtn.FlatAppearance.BorderSize = 1;
        _skipBtn.FlatAppearance.BorderColor = C_BTN_BORDER;
        _skipBtn.Click += SkipBtn_Click;
        _skipBtn.MouseEnter += (_, _) => _skipBtn.BackColor = Color.FromArgb(53, 53, 53);
        _skipBtn.MouseLeave += (_, _) => _skipBtn.BackColor = C_BTN_BG;
        Controls.Add(_skipBtn);
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
            Invoke(() => Close());
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
