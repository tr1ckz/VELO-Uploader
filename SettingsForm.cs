using System.Drawing.Drawing2D;

namespace VeloUploader;

// ── Borderless dark text box  ──
class DarkTextBox : Panel
{
    public readonly TextBox Inner;
    private bool _focused;

    static readonly Color C_BG = Color.FromArgb(14, 14, 18);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_FOCUS = Color.FromArgb(124, 58, 237);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkTextBox(string text, string placeholder, int x, int y, int w)
    {
        Location = new Point(x, y);
        Size = new Size(w, 28);
        BackColor = C_BG;

        Inner = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            Location = new Point(6, 4),
            Size = new Size(w - 12, 20),
            BackColor = C_BG,
            ForeColor = C_FG,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
        };
        Inner.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        Inner.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        Controls.Add(Inner);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public new string Text { get => Inner.Text; set => Inner.Text = value; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool UseSystemPasswordChar { get => Inner.UseSystemPasswordChar; set => Inner.UseSystemPasswordChar = value; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_focused ? C_FOCUS : C_BORDER, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

// ── Borderless dark listbox ──
class DarkListBox : Panel
{
    public readonly ListBox Inner;

    static readonly Color C_BG = Color.FromArgb(14, 14, 18);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkListBox(int x, int y, int w, int h)
    {
        Location = new Point(x, y);
        Size = new Size(w, h);
        BackColor = C_BG;

        Inner = new ListBox
        {
            Location = new Point(1, 1),
            Size = new Size(w - 2, h - 2),
            BackColor = C_BG,
            ForeColor = C_FG,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 8.5f),
        };
        Controls.Add(Inner);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(C_BORDER, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

// ── Borderless dark numeric ──
class DarkNumeric : Panel
{
    public readonly NumericUpDown Inner;

    static readonly Color C_BG = Color.FromArgb(14, 14, 18);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkNumeric(int val, int min, int max, int x, int y, int w)
    {
        Location = new Point(x, y);
        Size = new Size(w, 28);
        BackColor = C_BG;

        Inner = new NumericUpDown
        {
            Value = Math.Clamp(val, min, max),
            Minimum = min,
            Maximum = max,
            Location = new Point(1, 1),
            Size = new Size(w - 2, 26),
            BackColor = C_BG,
            ForeColor = C_FG,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9f),
        };
        Controls.Add(Inner);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(C_BORDER, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

class DarkComboBox : Panel
{
    public readonly ComboBox Inner;

    static readonly Color C_BG = Color.FromArgb(14, 14, 18);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkComboBox(int x, int y, int w, IEnumerable<string> items, string selected)
    {
        Location = new Point(x, y);
        Size = new Size(w, 28);
        BackColor = C_BG;

        Inner = new ComboBox
        {
            Location = new Point(1, 1),
            Size = new Size(w - 2, 26),
            BackColor = C_BG,
            ForeColor = C_FG,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        foreach (var item in items) Inner.Items.Add(item);
        Inner.SelectedItem = Inner.Items.Contains(selected) ? selected : Inner.Items[0];
        Controls.Add(Inner);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(C_BORDER, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}


public class SettingsForm : Form
{
    private const int MaxUiLogChars = 50000;
    private const int MaxUiEventLogChars = 20000;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_THEMECHANGED = 0x031A;

    private readonly AppSettings _settings;
    private readonly DarkTextBox _urlBox, _tokenBox, _watchBox, _addFolderBox, _addPatternBox, _moveToBox;
    private readonly CheckBox _subfoldersBox, _notifyBox, _deleteBox, _moveBox, _startupBox, _scanOnLaunchBox, _localCompressBox, _compressionHardFailBox, _soundBox, _selfSignedBox, _autoUpdateBox;
    private readonly CheckBox _queuePersistenceBox, _autoProcessQueueBox, _gameCompressionBox, _requireChecksumBox, _policySyncBox;
    private readonly DarkTextBox _certPathBox;
    private readonly Label _certInfoLabel;
    private readonly DarkNumeric _retriesBox, _maxSizeBox;
    private readonly DarkListBox _foldersList, _patternsList, _historyList;
    private readonly DarkComboBox _presetBox;
    private readonly RichTextBox _logBox;
    private readonly Label _statusLabel;
    private readonly Button _testBtn, _testTlsBtn;
    private readonly Panel[] _pages;
    private readonly Button[] _tabBtns;
    private int _activeTab;
    private List<UploadHistoryEntry> _historyEntries = [];
    
    // Status Dashboard UI components
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
    private Label _serverStatusLabel;
    private Panel _currentTaskPanel;
    private System.Windows.Forms.Timer _statusRefreshTimer;
    private Label _queueModeLabel;
    private Label _queueSummaryLabel;
    private ListBox _pendingQueueList;
    private Button _queueToggleBtn;
    private Button _queueProcessNowBtn;
    private Button _quickEditorBtn;
    private readonly Action<bool, bool>? _setQueueProcessing;
    private readonly Action? _openQuickEditor;

    // Palette
    static readonly Color C_BG = Color.FromArgb(12, 12, 15);
    static readonly Color C_PANEL = Color.FromArgb(18, 18, 22);
    static readonly Color C_INPUT = Color.FromArgb(14, 14, 18);
    static readonly Color C_BORDER = Color.FromArgb(40, 40, 50);
    static readonly Color C_T1 = Color.FromArgb(240, 240, 245);
    static readonly Color C_T2 = Color.FromArgb(155, 155, 165);
    static readonly Color C_T3 = Color.FromArgb(90, 90, 100);
    static readonly Color C_ACCENT = Color.FromArgb(124, 58, 237);
    static readonly Color C_ACCENT_H = Color.FromArgb(139, 78, 245);
    static readonly Color C_BTN = Color.FromArgb(38, 38, 46);
    static readonly Color C_BTN_H = Color.FromArgb(52, 52, 62);
    static readonly Color C_RED = Color.FromArgb(160, 38, 38);
    static readonly Color C_GREEN = Color.FromArgb(74, 222, 128);
    static readonly Color C_ORANGE = Color.FromArgb(251, 146, 60);
    static readonly Color C_ERR = Color.FromArgb(248, 113, 113);

    public SettingsForm(AppSettings settings, int initialTab = 0, Action<bool, bool>? setQueueProcessing = null, Action? openQuickEditor = null)
    {
        _settings = settings;
        _activeTab = initialTab;
        _setQueueProcessing = setQueueProcessing;
        _openQuickEditor = openQuickEditor;
        SuspendLayout();

        Text = "VELO Uploader";
        ClientSize = new Size(680, 900);
        MinimumSize = new Size(700, 760);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        const int pageTop = 102;
        const int footerHeight = 34;
        int pageHeight = ClientSize.Height - pageTop - footerHeight;

        // Load icon from embedded resource
        try
        {
            var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // ── Header ──
        var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = C_PANEL };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_ACCENT, 2);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, Width, header.Height - 1);
        };
        Controls.Add(header);

        // Logo from embedded resource
        try
        {
            var logoStream = typeof(SettingsForm).Assembly.GetManifestResourceStream("logo.png");
            if (logoStream != null)
            {
                var img = Image.FromStream(logoStream);
                header.Controls.Add(new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(14, 12, 40, 40), BackColor = Color.Transparent });
            }
        }
        catch { }

        var headerTitle = MkLabel("VELO Uploader", 60, 9, new Font("Segoe UI", 14f, FontStyle.Bold), C_T1);
        var headerSubtitle = MkLabel("Auto-upload your game clips", 62, 39, new Font("Segoe UI", 10f, FontStyle.Bold), C_T3);
        var headerVersion = MkLabel($"v{GitHubUpdater.GetCurrentVersion()}", 0, 11, new Font("Segoe UI", 8f, FontStyle.Bold), C_ACCENT);
        headerVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(headerTitle);
        header.Controls.Add(headerSubtitle);
        header.Controls.Add(headerVersion);

        void LayoutHeader()
        {
            headerVersion.Location = new Point(Math.Max(12, header.ClientSize.Width - headerVersion.PreferredWidth - 14), 11);
        }

        header.SizeChanged += (_, _) => LayoutHeader();
        LayoutHeader();

        // ── Custom tab bar ──
        var tabBar = new Panel { Location = new Point(0, header.Height), Size = new Size(ClientSize.Width, 36), BackColor = C_PANEL, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        Controls.Add(tabBar);

        _tabBtns = new Button[5];
        string[] tabNames = ["General", "Filters", "Logs", "History", "Video Processor"];
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = tabNames[i],
            Location = new Point(i * 120 + 20, 0),
            Size = new Size(120, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = C_T3,
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 30, 38);
            btn.Click += (_, _) => SwitchTab(idx);
            tabBar.Controls.Add(btn);
            _tabBtns[i] = btn;
        }

        // Tab underline painted on tabBar
        tabBar.Paint += (_, e) =>
        {
            var btn = _tabBtns[_activeTab];
            using var brush = new SolidBrush(C_ACCENT);
            e.Graphics.FillRectangle(brush, btn.Left + 10, 33, btn.Width - 20, 3);
        };

        // ── Pages ──
        _pages = new Panel[5];
        for (int i = 0; i < 5; i++)
        {
            _pages[i] = new Panel
            {
                Location = new Point(0, pageTop),
                Size = new Size(680, pageHeight),
                BackColor = C_BG,
                Visible = i == initialTab,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
            };
            Controls.Add(_pages[i]);
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = footerHeight, BackColor = C_PANEL };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BORDER, 1);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        Controls.Add(footer);

        var footerText = new Label
        {
            AutoSize = true,
            Text = $"© {DateTime.Now.Year} VELO Uploader • Created by",
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 8f),
            BackColor = Color.Transparent,
        };
        footer.Controls.Add(footerText);

        var footerLink = new LinkLabel
        {
            AutoSize = true,
            Text = "tr1ck",
            LinkColor = C_ACCENT,
            ActiveLinkColor = C_ACCENT_H,
            VisitedLinkColor = C_ACCENT,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            BackColor = Color.Transparent,
            TabStop = true,
        };
        footerLink.LinkClicked += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/tr1ckz")
                {
                    UseShellExecute = true,
                });
            }
            catch { }
        };
        footer.Controls.Add(footerLink);

        void LayoutFooter()
        {
            int spacing = 6;
            int totalWidth = footerText.PreferredWidth + spacing + footerLink.PreferredWidth;
            int startX = Math.Max(12, (footer.Width - totalWidth) / 2);
            footerText.Location = new Point(startX, 9);
            footerLink.Location = new Point(startX + footerText.PreferredWidth + spacing, 9);
        }

        footer.SizeChanged += (_, _) => LayoutFooter();
        LayoutFooter();

        void LayoutShell()
        {
            tabBar.Location = new Point(0, header.Height);
            tabBar.Width = ClientSize.Width;

            int dynamicPageTop = tabBar.Bottom;
            int dynamicPageHeight = Math.Max(120, ClientSize.Height - dynamicPageTop - footerHeight);
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Location = new Point(0, dynamicPageTop);
                _pages[i].Size = new Size(ClientSize.Width, dynamicPageHeight);
            }
        }

        SizeChanged += (_, _) => LayoutShell();
        LayoutShell();

        // ═══════════════════════════════════════
        //  PAGE 0: GENERAL
        // ═══════════════════════════════════════
        var g = _pages[0];
        int y = 14, lx = 22, w = 636;

        // ─────────────────────────────────────
        // SECTION: CONNECTION
        // ─────────────────────────────────────
        Section(g, "CONNECTION", lx, y); y += 18;

        Lbl(g, "Server URL", lx, y);
        _urlBox = new DarkTextBox(settings.ServerUrl, "https://clips.example.com", lx, y + 16, w);
        g.Controls.Add(_urlBox);
        y += 50;

        Lbl(g, "API Token", lx, y);
        _tokenBox = new DarkTextBox(settings.ApiToken, "velo_...", lx, y + 16, w - 180);
        _tokenBox.UseSystemPasswordChar = true;
        g.Controls.Add(_tokenBox);
        _testBtn = MkBtn("Test API", lx + w - 172, y + 16, 80, 28, C_ACCENT, C_ACCENT_H);
        _testBtn.Click += async (_, _) => await TestConnection();
        g.Controls.Add(_testBtn);
        y += 50;

        _statusLabel = new Label
        {
            Location = new Point(lx, y),
            Size = new Size(w, 30),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        g.Controls.Add(_statusLabel);
        y += 22;

        // ─────────────────────────────────────
        // SECTION: SECURITY
        // ─────────────────────────────────────
        Section(g, "SECURITY", lx, y); y += 18;

        _selfSignedBox = MkChk("Allow self-signed / untrusted server certificate", settings.AllowSelfSignedCerts, lx, y);
        g.Controls.Add(_selfSignedBox);
        y += 28;

        Lbl(g, "Pinned certificate (optional)", lx, y);
        y += 16;
        _certPathBox = new DarkTextBox(settings.TrustedCertPath, "Trusted .crt/.cer/.pem file for certificate pinning", lx, y, w);
        g.Controls.Add(_certPathBox);
        y += 34;

        _certInfoLabel = new Label
        {
            Location = new Point(lx, y),
            Size = new Size(w - 272, 28),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 7.5f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        g.Controls.Add(_certInfoLabel);

        var certBrowseBtn = MkBtn("Browse", lx + w - 264, y, 72, 28, C_BTN, C_BTN_H);
        certBrowseBtn.Click += (_, _) =>
        {
            using var d = new OpenFileDialog
            {
                Filter = "Certificates|*.crt;*.pem;*.cer|All files|*.*",
                Title = "Select trusted server certificate",
            };
            if (d.ShowDialog() == DialogResult.OK)
            {
                _certPathBox.Text = d.FileName;
                UpdateTlsUi();
            }
        };
        g.Controls.Add(certBrowseBtn);
        var genCertBtn = MkBtn("Generate", lx + w - 188, y, 78, 28, C_BTN, C_BTN_H);
        genCertBtn.Click += (_, _) => GenerateCert();
        g.Controls.Add(genCertBtn);
        _testTlsBtn = MkBtn("Test TLS", lx + w - 104, y, 96, 28, C_ACCENT, C_ACCENT_H);
        _testTlsBtn.Click += async (_, _) => await TestTlsConnection();
        g.Controls.Add(_testTlsBtn);
        y += 42;

        // ─────────────────────────────────────
        // SECTION: RECORDINGS
        // ─────────────────────────────────────
        Section(g, "RECORDINGS", lx, y); y += 18;

        Lbl(g, "Watch folder", lx, y);
        _watchBox = new DarkTextBox(settings.WatchFolder, @"D:\recordings", lx, y + 16, w - 78);
        g.Controls.Add(_watchBox);
        var browseBtn = MkBtn("Browse", lx + w - 72, y + 16, 72, 28, C_BTN, C_BTN_H);
        browseBtn.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = _watchBox.Text }; if (d.ShowDialog() == DialogResult.OK) _watchBox.Text = d.SelectedPath; };
        g.Controls.Add(browseBtn);
        y += 50;

        _subfoldersBox = MkChk("Include subfolders", settings.WatchSubfolders, lx, y);
        g.Controls.Add(_subfoldersBox);
        _deleteBox = MkChk("Delete clip after upload", settings.DeleteAfterUpload, lx + 340, y);
        g.Controls.Add(_deleteBox);
        y += 28;

        _moveBox = MkChk("Move clip after upload", settings.MoveAfterUpload, lx, y);
        g.Controls.Add(_moveBox);
        y += 24;
        Lbl(g, "Destination folder", lx, y);
        _moveToBox = new DarkTextBox(settings.MoveToFolder, @"D:\archived-clips", lx, y + 16, w - 78);
        g.Controls.Add(_moveToBox);
        var moveBrowseBtn = MkBtn("Browse", lx + w - 72, y + 16, 72, 28, C_BTN, C_BTN_H);
        moveBrowseBtn.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = _moveToBox.Text }; if (d.ShowDialog() == DialogResult.OK) _moveToBox.Text = d.SelectedPath; };
        g.Controls.Add(moveBrowseBtn);
        // Update move destination box visibility based on checkbox
        _moveBox.CheckedChanged += (_, _) => { _moveToBox.Inner.Enabled = _moveBox.Checked; moveBrowseBtn.Enabled = _moveBox.Checked; };
        _moveToBox.Inner.Enabled = _moveBox.Checked;
        moveBrowseBtn.Enabled = _moveBox.Checked;

        // Delete and move are mutually exclusive actions after upload.
        _deleteBox.CheckedChanged += (_, _) =>
        {
            if (_deleteBox.Checked)
                _moveBox.Checked = false;
            _moveToBox.Inner.Enabled = _moveBox.Checked;
            moveBrowseBtn.Enabled = _moveBox.Checked;
        };
        _moveBox.CheckedChanged += (_, _) =>
        {
            if (_moveBox.Checked)
                _deleteBox.Checked = false;
            _moveToBox.Inner.Enabled = _moveBox.Checked;
            moveBrowseBtn.Enabled = _moveBox.Checked;
        };

        if (_moveBox.Checked)
            _deleteBox.Checked = false;
        else if (_deleteBox.Checked)
            _moveBox.Checked = false;
        y += 50;

        _scanOnLaunchBox = MkChk("Upload existing clips on launch", settings.ScanOnLaunch, lx, y);
        g.Controls.Add(_scanOnLaunchBox);
        _notifyBox = MkChk("Desktop notifications", settings.ShowNotifications, lx + 340, y);
        g.Controls.Add(_notifyBox);
        y += 42;

        // ─────────────────────────────────────
        // SECTION: COMPRESSION
        // ─────────────────────────────────────
        Section(g, "COMPRESSION", lx, y); y += 18;

        Lbl(g, "Compression preset:", lx, y + 5);
        
        // Show GPU presets if available, otherwise CPU only
        var gpuAvailable = LocalCompressor.IsGPUAvailable();
        var presetOptions = gpuAvailable ? CompressionPreset.All : CompressionPreset.AllCPU;
        _presetBox = new DarkComboBox(lx + 120, y, 140, presetOptions, settings.CompressionPreset);
        g.Controls.Add(_presetBox);

        // Show GPU status label
        var gpuStatus = gpuAvailable ? "GPU ready ✓" : "CPU only";
        var gpuStatusColor = gpuAvailable ? C_GREEN : C_T3;
        g.Controls.Add(MkLabel(gpuStatus, lx + w - 120, y + 5, new Font("Segoe UI", 7.5f, FontStyle.Bold), gpuStatusColor));

        Lbl(g, "Retries:", lx + 340, y + 5);
        _retriesBox = new DarkNumeric(settings.MaxRetries, 1, 10, lx + 400, y, 60);
        g.Controls.Add(_retriesBox);
        y += 38;

        _localCompressBox = MkChk("Compress locally before upload (FFmpeg)", settings.LocalCompress, lx, y);
        g.Controls.Add(_localCompressBox);
        _compressionHardFailBox = MkChk("Skip upload if compression fails", settings.StopOnCompressionFailure, lx + 340, y);
        g.Controls.Add(_compressionHardFailBox);
        y += 42;

        // ─────────────────────────────────────
        // SECTION: SYSTEM
        // ─────────────────────────────────────
        Section(g, "SYSTEM", lx, y); y += 18;

        _startupBox = MkChk("Start with Windows", StartupManager.IsRegistered(), lx, y);
        g.Controls.Add(_startupBox);
        _soundBox = MkChk("Play success/failure sounds", settings.PlaySounds, lx + 340, y);
        g.Controls.Add(_soundBox);
        y += 28;

        _autoUpdateBox = MkChk("Check GitHub for app updates on launch", settings.AutoCheckForUpdates, lx, y);
        g.Controls.Add(_autoUpdateBox);
        y += 42;

        // ─────────────────────────────────────
        // SECTION: UPLOAD BEHAVIOR
        // ─────────────────────────────────────
        Section(g, "UPLOAD BEHAVIOR", lx, y); y += 18;

        _queuePersistenceBox = MkChk("Persist upload queue across restarts", settings.EnableQueuePersistence, lx, y);
        g.Controls.Add(_queuePersistenceBox);
        _requireChecksumBox = MkChk("Require checksum validation on upload", settings.RequireUploadChecksum, lx + 340, y);
        g.Controls.Add(_requireChecksumBox);
        y += 28;

        _autoProcessQueueBox = MkChk("Start uploads immediately when new clips arrive", settings.AutoProcessQueue, lx, y);
        g.Controls.Add(_autoProcessQueueBox);
        _gameCompressionBox = MkChk("Use low-impact compression while gaming", settings.AdaptiveCompressionWhenGaming, lx + 340, y);
        g.Controls.Add(_gameCompressionBox);
        _policySyncBox = MkChk("Sync upload settings from server on launch", settings.EnablePolicySync, lx + 340, y);
        g.Controls.Add(_policySyncBox);
        y += 42;

        var saveBtn = MkBtn("Save && Start Watching", lx + w - 208, y, 208, 38, C_ACCENT, C_ACCENT_H);
        saveBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        saveBtn.Click += (_, _) => SaveSettings();
        g.Controls.Add(saveBtn);

        _selfSignedBox.CheckedChanged += (_, _) => UpdateTlsUi();
        _certPathBox.Inner.TextChanged += (_, _) => UpdateTlsUi();
        UpdateTlsUi();

        // ═══════════════════════════════════════
        //  PAGE 1: FILTERS
        // ═══════════════════════════════════════
        var f = _pages[1];
        y = 10;

        Section(f, "IGNORED FOLDERS", lx, y);
        f.Controls.Add(MkLabel("Clips in these folder names are skipped", lx + 130, y + 1, new Font("Segoe UI", 7.5f), C_T3));
        y += 18;

        _foldersList = new DarkListBox(lx, y, w - 90, 78);
        foreach (var fol in settings.IgnoredFolders) _foldersList.Inner.Items.Add(fol);
        f.Controls.Add(_foldersList);
        var rmF = MkBtn("Remove", lx + w - 82, y, 82, 26, C_RED, Color.FromArgb(190, 50, 50));
        rmF.Click += (_, _) => { if (_foldersList.Inner.SelectedIndex >= 0) _foldersList.Inner.Items.RemoveAt(_foldersList.Inner.SelectedIndex); };
        f.Controls.Add(rmF);
        y += 84;

        _addFolderBox = new DarkTextBox("", "Folder name (e.g. Desktop)", lx, y, w - 172);
        f.Controls.Add(_addFolderBox);
        var addF = MkBtn("Add", lx + w - 164, y, 50, 28, C_BTN, C_BTN_H);
        addF.Click += (_, _) => { var t = _addFolderBox.Text.Trim(); if (t.Length > 0 && !_foldersList.Inner.Items.Contains(t)) { _foldersList.Inner.Items.Add(t); _addFolderBox.Text = ""; } };
        f.Controls.Add(addF);
        var brF = MkBtn("Browse", lx + w - 106, y, 70, 28, C_BTN, C_BTN_H);
        brF.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog() == DialogResult.OK) { var n = new DirectoryInfo(d.SelectedPath).Name; if (!_foldersList.Inner.Items.Contains(n)) _foldersList.Inner.Items.Add(n); } };
        f.Controls.Add(brF);
        y += 38;

        Section(f, "IGNORED PATTERNS", lx, y);
        f.Controls.Add(MkLabel("Wildcards: * any, ? single", lx + 140, y + 1, new Font("Segoe UI", 7.5f), C_T3));
        y += 18;

        _patternsList = new DarkListBox(lx, y, w - 90, 68);
        foreach (var p in settings.IgnoredPatterns) _patternsList.Inner.Items.Add(p);
        f.Controls.Add(_patternsList);
        var rmP = MkBtn("Remove", lx + w - 82, y, 82, 26, C_RED, Color.FromArgb(190, 50, 50));
        rmP.Click += (_, _) => { if (_patternsList.Inner.SelectedIndex >= 0) _patternsList.Inner.Items.RemoveAt(_patternsList.Inner.SelectedIndex); };
        f.Controls.Add(rmP);
        y += 74;

        _addPatternBox = new DarkTextBox("", "e.g. *_temp.mp4", lx, y, w - 100);
        f.Controls.Add(_addPatternBox);
        var addP = MkBtn("Add", lx + w - 92, y, 56, 28, C_BTN, C_BTN_H);
        addP.Click += (_, _) => { var t = _addPatternBox.Text.Trim(); if (t.Length > 0 && !_patternsList.Inner.Items.Contains(t)) { _patternsList.Inner.Items.Add(t); _addPatternBox.Text = ""; } };
        f.Controls.Add(addP);
        y += 38;

        Section(f, "MAX FILE SIZE", lx, y); y += 18;
        _maxSizeBox = new DarkNumeric(settings.MaxFileSizeMB, 0, 99999, lx, y, 80);
        f.Controls.Add(_maxSizeBox);
        Lbl(f, "MB  (0 = no limit)", lx + 90, y + 5);
        y += 38;

        var saveFilt = MkBtn("Save Filters", lx + w - 120, y, 120, 34, C_ACCENT, C_ACCENT_H);
        saveFilt.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        saveFilt.Click += (_, _) => SaveFilters();
        f.Controls.Add(saveFilt);

        // ═══════════════════════════════════════
        //  PAGE 2: LOGS
        // ═══════════════════════════════════════
        var l = _pages[2];

        _logBox = new RichTextBox
        {
            Location = new Point(lx, 10),
            Size = new Size(w, 340),
            BackColor = C_INPUT,
            ForeColor = C_T2,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        l.Controls.Add(_logBox);

        y = 358;
        var clr = MkBtn("Clear", lx, y, 70, 28, C_BTN, C_BTN_H);
        clr.Click += (_, _) => { Logger.Clear(); _logBox.Clear(); };
        l.Controls.Add(clr);

        var opn = MkBtn("Open File", lx + 78, y, 80, 28, C_BTN, C_BTN_H);
        opn.Click += (_, _) =>
        {
            var p = Logger.GetLogFilePath();
            if (File.Exists(p)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = p, UseShellExecute = true });
        };
        l.Controls.Add(opn);

        var cpy = MkBtn("Copy All", lx + 166, y, 80, 28, C_BTN, C_BTN_H);
        cpy.Click += (_, _) => { if (_logBox.Text.Length > 0) Clipboard.SetText(_logBox.Text); };
        l.Controls.Add(cpy);

        LoadExistingLogs();
        Logger.OnLog += OnNewLog;
        FormClosed += (_, _) =>
        {
            _statusRefreshTimer?.Stop();
            _statusRefreshTimer?.Dispose();
            Logger.OnLog -= OnNewLog;
            UploadHistoryManager.Changed -= OnHistoryChanged;
        };

        // ═══════════════════════════════════════
        //  PAGE 3: HISTORY
        // ═══════════════════════════════════════
        var h = _pages[3];
        _historyList = new DarkListBox(lx, 10, w, 360);
        h.Controls.Add(_historyList);

        var copyHistory = MkBtn("Copy URL", lx, 380, 80, 28, C_BTN, C_BTN_H);
        copyHistory.Click += (_, _) =>
        {
            var entry = SelectedHistoryEntry();
            if (!string.IsNullOrWhiteSpace(entry?.Url))
            {
                try { Clipboard.SetText(entry.Url); } catch { }
            }
        };
        h.Controls.Add(copyHistory);

        var clearHistory = MkBtn("Clear", lx + 88, 380, 70, 28, C_RED, Color.FromArgb(190, 50, 50));
        clearHistory.Click += (_, _) => UploadHistoryManager.Clear();
        h.Controls.Add(clearHistory);

        var historyHint = new Label
        {
            Location = new Point(lx, 416),
            Size = new Size(w, 44),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8f),
            Text = "Select an entry to inspect it. Successful entries can be copied back to the clipboard.",
        };
        h.Controls.Add(historyHint);

        LoadHistory();
        _historyList.Inner.SelectedIndexChanged += (_, _) =>
        {
            var entry = SelectedHistoryEntry();
            if (entry == null)
            {
                historyHint.Text = "Select an entry to inspect it. Successful entries can be copied back to the clipboard.";
                return;
            }

            var status = entry.Success ? "Success" : $"Failed: {entry.Error}";
            var compression = entry.UsedCompression ? $"Compressed ({entry.CompressionPreset})" : "Original upload";
            historyHint.Text = $"{status} • {compression} • {FormatSize(entry.SourceSizeBytes)} -> {FormatSize(entry.UploadedSizeBytes)}";
        };
        UploadHistoryManager.Changed += OnHistoryChanged;

        // ═══════════════════════════════════════
        //  PAGE 4: VIDEO PROCESSOR
        // ═══════════════════════════════════════
        var s = _pages[4];
        int sy = 14;

        MkSectionLabel(s, "QUEUE & PROCESSOR", lx, sy); sy += 22;

        _queueModeLabel = MkLabel("Queue mode: Live upload", lx, sy, new Font("Segoe UI", 8.5f, FontStyle.Bold), C_GREEN);
        s.Controls.Add(_queueModeLabel);

        _queueSummaryLabel = MkLabel("Pending local videos: 0", lx + 240, sy, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_queueSummaryLabel);
        sy += 24;

        _queueToggleBtn = MkBtn("Pause Uploads (Queue Only)", lx, sy, 170, 30, C_ACCENT, C_ACCENT_H);
        _queueToggleBtn.Enabled = _setQueueProcessing != null;
        _queueToggleBtn.Click += (_, _) => _setQueueProcessing?.Invoke(_queueToggleBtn.Text.Contains("Resume", StringComparison.OrdinalIgnoreCase), true);
        s.Controls.Add(_queueToggleBtn);

        _queueProcessNowBtn = MkBtn("Process Queued Now", lx + 180, sy, 150, 30, C_BTN, C_BTN_H);
        _queueProcessNowBtn.Enabled = _setQueueProcessing != null;
        _queueProcessNowBtn.Click += (_, _) => _setQueueProcessing?.Invoke(true, true);
        s.Controls.Add(_queueProcessNowBtn);

        _quickEditorBtn = MkBtn("Open Video Editor", lx + 340, sy, 140, 30, C_BTN, C_BTN_H);
        _quickEditorBtn.Enabled = _openQuickEditor != null;
        _quickEditorBtn.Click += (_, _) => _openQuickEditor?.Invoke();
        s.Controls.Add(_quickEditorBtn);
        sy += 40;

        _pendingQueueList = new ListBox
        {
            Location = new Point(lx, sy),
            Size = new Size(w, 110),
            BackColor = C_PANEL,
            ForeColor = C_T1,
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true,
        };
        _pendingQueueList.Items.Add("No pending local videos.");
        s.Controls.Add(_pendingQueueList);
        sy += 122;

        // Current Task Section
        MkSectionLabel(s, "CURRENT TASK", lx, sy); sy += 22;

        _currentTaskPanel = new Panel
        {
            Location = new Point(lx, sy),
            Size = new Size(w, 90),
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
            Size = new Size(w - 20, 18),
            ForeColor = C_T1,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Text = "Idle — waiting for clips...",
        };
        _currentTaskPanel.Controls.Add(_taskFileLabel);

        _taskStatusLabel = new Label
        {
            Location = new Point(10, 28),
            Size = new Size(w - 20, 16),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8f),
            Text = "",
        };
        _currentTaskPanel.Controls.Add(_taskStatusLabel);

        _taskProgressBar = new ProgressBar
        {
            Location = new Point(10, 46),
            Size = new Size(w - 20, 8),
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
            Size = new Size(w - 20, 14),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 7.5f),
            TextAlign = ContentAlignment.TopRight,
            Text = "",
        };
        _currentTaskPanel.Controls.Add(_taskSpeedLabel);

        s.Controls.Add(_currentTaskPanel);
        sy += 100;

        // Stats Section (compact - single line each)
        MkSectionLabel(s, "SESSION STATISTICS", lx, sy); sy += 18;

        _statsUploadsLabel = MkLabel("📤 Uploads: 0", lx, sy, new Font("Segoe UI", 8.5f, FontStyle.Bold), C_GREEN);
        s.Controls.Add(_statsUploadsLabel);
        sy += 20;

        _statsSuccessLabel = MkLabel("✓ Success rate: N/A", lx + 200, sy - 20, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_statsSuccessLabel);

        _statsSizeLabel = MkLabel("📦 Total size: 0 MB", lx, sy, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_statsSizeLabel);
        sy += 22;

        // System Status Section (2-column compact layout)
        MkSectionLabel(s, "SYSTEM STATUS", lx, sy); sy += 18;

        _systemStatusLabel = MkLabel("● Watching", lx, sy, new Font("Segoe UI", 8.5f, FontStyle.Bold), C_GREEN);
        s.Controls.Add(_systemStatusLabel);

        _gpuStatusLabel = MkLabel("GPU: Checking...", lx + 300, sy, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_gpuStatusLabel);
        sy += 20;

        var ffmpegStatus = LocalCompressor.IsAvailable() ? "✓ FFmpeg available" : "✗ FFmpeg not found";
        var ffmpegColor = LocalCompressor.IsAvailable() ? C_GREEN : C_RED;
        var ffmpegLabel = MkLabel(ffmpegStatus, lx, sy, new Font("Segoe UI", 8.5f), ffmpegColor);
        s.Controls.Add(ffmpegLabel);

        _serverStatusLabel = MkLabel("Server: Checking...", lx + 300, sy, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_serverStatusLabel);
        sy += 20;

        _quotaStatusLabel = MkLabel("Storage: Checking...", lx, sy, new Font("Segoe UI", 8.5f), C_T2);
        s.Controls.Add(_quotaStatusLabel);
        sy += 24;

        // Version Section
        MkSectionLabel(s, "APPLICATION", lx, sy); sy += 18;

        _versionLabel = MkLabel($"v{GitHubUpdater.GetCurrentVersion()}", lx, sy, new Font("Segoe UI", 8.5f, FontStyle.Bold), C_ACCENT);
        s.Controls.Add(_versionLabel);

        _updateCheckBtn = new Button
        {
            Location = new Point(lx + 200, sy - 2),
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
        _updateCheckBtn.Click += async (_, _) => 
        {
            _updateCheckBtn.Enabled = false;
            try
            {
                var release = await GitHubUpdater.CheckForUpdateAsync();
                if (release == null)
                {
                    MessageBox.Show($"You are already on the latest version ({GitHubUpdater.GetCurrentVersion()}).", "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var result = MessageBox.Show(
                        $"A new version is available.\n\nCurrent: {GitHubUpdater.GetCurrentVersion()}\nLatest: {release.Version}\n\nDownload and apply the update now? The uploader will restart.",
                        "Update available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        var cts = new CancellationTokenSource();
                        using var progressForm = new UpdateProgressForm(cts);
                        progressForm.SetFileName(release.AssetName);

                        var updateTask = Task.Run(async () =>
                        {
                            try
                            {
                                await GitHubUpdater.DownloadAndApplyAsync(
                                    release,
                                    cts.Token,
                                    onProgress: (downloaded, total) => progressForm.SetProgress(downloaded, total)
                                );
                                
                                // SetCompleting sets DialogResult = OK which auto-closes the ShowDialog form
                                progressForm.SetCompleting();
                            }
                            catch (Exception ex)
                            {
                                if (progressForm.IsHandleCreated)
                                {
                                    progressForm.BeginInvoke(() =>
                                    {
                                        var openPage = MessageBox.Show(
                                            $"Auto-update failed: {ex.Message}\n\nOpen the GitHub release page instead?",
                                            "VELO Uploader",
                                            MessageBoxButtons.YesNo,
                                            MessageBoxIcon.Error);
                                        if (openPage == DialogResult.Yes)
                                        {
                                            try
                                            {
                                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.ReleaseUrl)
                                                {
                                                    UseShellExecute = true,
                                                });
                                            }
                                            catch
                                            {
                                            }
                                        }
                                        progressForm.DialogResult = DialogResult.Cancel;
                                    });
                                }
                            }
                        });

                        var dialogResult = progressForm.ShowDialog();
                        // Swallow any Invoke errors from task (form already closed)
                        try { await updateTask; } catch { }

                        if (dialogResult == DialogResult.OK)
                        {
                            Logger.Info($"Applying update {release.TagName} - exiting app from SettingsForm");
                            await Task.Delay(300);
                            Application.Exit();
                            await Task.Delay(500);
                            Environment.Exit(0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Check failed: {ex.Message}", "VELO Uploader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _updateCheckBtn.Enabled = true;
            }
        };
        s.Controls.Add(_updateCheckBtn);

        // Event Log Section (compact in remaining space)
        sy += 36;
        MkSectionLabel(s, "EVENT LOG", lx, sy); sy += 18;

        _eventLogBox = new RichTextBox
        {
            Location = new Point(lx, sy),
            Size = new Size(w, 120),
            BackColor = C_PANEL,
            ForeColor = C_T2,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8f),
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        s.Controls.Add(_eventLogBox);

        // Setup refresh timer (every 15 seconds)
        _statusRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 15000, // 15 seconds
        };
        _statusRefreshTimer.Tick += async (_, _) =>
        {
            await CheckServerStatusAsync();
            _ = RefreshQuotaAsync();
        };

        // Check GPU availability
        CheckGPUStatus();
        // Defer server status check until after handle is created and start refresh timer
        Load += async (_, _) =>
        {
            WindowDarkMode.ApplyForSystemTheme(Handle);
            await CheckServerStatusAsync();
            await RefreshQuotaAsync();
            _statusRefreshTimer.Start();
        };

        SwitchTab(initialTab);
        ResumeLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowDarkMode.ApplyForSystemTheme(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg == WM_SETTINGCHANGE || m.Msg == WM_THEMECHANGED) && IsHandleCreated)
            WindowDarkMode.ApplyForSystemTheme(Handle);
    }

    // ── Tab switching ──

    public void ShowTab(int idx) => SwitchTab(idx);

    void SwitchTab(int idx)
    {
        _activeTab = idx;
        for (int i = 0; i < 5; i++)
        {
            _pages[i].Visible = i == idx;
            _tabBtns[i].ForeColor = i == idx ? C_T1 : C_T3;
            _tabBtns[i].Font = new Font("Segoe UI", 9f, i == idx ? FontStyle.Bold : FontStyle.Regular);
        }
        // Repaint tab bar to move underline
        _tabBtns[0].Parent?.Invalidate();
    }

    // ── Factory helpers ──

    static Label MkLabel(string text, int x, int y, Font font, Color color)
    {
        return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent };
    }

    static void Section(Control p, string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = C_ACCENT,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            UseMnemonic = false,
        };
        p.Controls.Add(lbl);
        var lineX = x + lbl.PreferredWidth + 8;
        var lineW = Math.Max(0, p.Width - lineX - x);
        if (lineW > 0)
            p.Controls.Add(new Panel { Location = new Point(lineX, y + 6), Size = new Size(lineW, 1), BackColor = C_BORDER });
    }

    static void Lbl(Control p, string text, int x, int y)
    {
        p.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = C_T2, Font = new Font("Segoe UI", 8f) });
    }

    static Button MkBtn(string text, int x, int y, int w, int h, Color bg, Color hover)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f),
            Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        return b;
    }

    static CheckBox MkChk(string text, bool v, int x, int y)
    {
        return new CheckBox
        {
            Text = text,
            Checked = v,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand,
        };
    }

    static void MkSectionLabel(Control parent, string title, int x, int y)
    {
        var lbl = new Label
        {
            Text = title,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = C_ACCENT,
            BackColor = Color.Transparent,
            UseMnemonic = false,
        };
        parent.Controls.Add(lbl);
        var lineX = x + lbl.PreferredWidth + 8;
        var lineW = Math.Max(0, parent.Width - lineX - x);
        if (lineW > 0)
            parent.Controls.Add(new Panel { Location = new Point(lineX, y + 6), Size = new Size(lineW, 1), BackColor = C_BORDER });
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

    private async Task CheckServerStatusAsync()
    {
        var baseUrl = _urlBox.Text.Trim();
        if (string.IsNullOrEmpty(baseUrl) || baseUrl.Length < 10)
        {
            InvokeIfNeeded(() =>
            {
                _serverStatusLabel.Text = "Server: Not configured";
                _serverStatusLabel.ForeColor = C_T3;
            });
            return;
        }

        try
        {
            using var handler = TlsCertHelper.CreateHandler(BuildTlsProbeSettings());
            using var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            var url = baseUrl.TrimEnd('/') + "/api/health";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = await h.GetAsync(url);
            sw.Stop();
            var ok = resp.IsSuccessStatusCode;
            InvokeIfNeeded(() =>
            {
                _serverStatusLabel.Text = ok
                    ? $"● Server online ({sw.ElapsedMilliseconds} ms)"
                    : $"⚠ Server error ({(int)resp.StatusCode})";
                _serverStatusLabel.ForeColor = ok ? C_GREEN : C_ORANGE;
            });
        }
        catch
        {
            InvokeIfNeeded(() =>
            {
                _serverStatusLabel.Text = "✗ Server unreachable";
                _serverStatusLabel.ForeColor = C_ERR;
            });
        }
    }

        private Label? _quotaStatusLabel;

        internal void RefreshQuotaLabel()
        {
            if (_quotaStatusLabel == null) return;
            _ = RefreshQuotaAsync();
        }

        private async Task RefreshQuotaAsync()
        {
            QuotaService.Invalidate();
            var quotaResult = await QuotaService.GetAsync(_settings);
            InvokeIfNeeded(() =>
            {
                if (_quotaStatusLabel == null) return;
                if (!quotaResult.Success)
                {
                    _quotaStatusLabel.Text = quotaResult.Status switch
                    {
                        QuotaFetchStatus.ServerOutdated => "Storage: server needs update",
                        QuotaFetchStatus.Unauthorized => "Storage: API token unauthorized",
                        QuotaFetchStatus.NotConfigured => "Storage: uploader not configured",
                        _ => "Storage: unavailable",
                    };
                    _quotaStatusLabel.ForeColor = quotaResult.Status == QuotaFetchStatus.ServerOutdated || quotaResult.Status == QuotaFetchStatus.Unauthorized
                        ? C_ORANGE
                        : C_T3;
                    return;
                }
                var quota = quotaResult.Quota!;
                if (!quota.HasQuota)
                {
                    _quotaStatusLabel.Text = $"Storage: {quota.UsedFormatted} used (unlimited)";
                    _quotaStatusLabel.ForeColor = C_T2;
                    return;
                }
                var quotaBytes = quota.QuotaBytes!.Value;
                var pct = (int)Math.Min(100, quota.UsedBytes * 100L / quotaBytes);
                var color = pct >= 90 ? C_ERR : pct >= 75 ? C_ORANGE : C_GREEN;
                _quotaStatusLabel.Text = $"Storage: {quota.UsedFormatted} / {quota.QuotaFormatted} ({pct}% used, {quota.FreeFormatted} free)";
                _quotaStatusLabel.ForeColor = color;
            });
        }

    private void InvokeIfNeeded(Action action)
    {
        try
        {
            if (IsHandleCreated && InvokeRequired)
                BeginInvoke(action);
            else if (IsHandleCreated)
                action();
        }
        catch
        {
            // Silently ignore invoke errors if window is being closed/disposed
        }
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

    public void UpdateQueueStatus(bool autoProcessing, IReadOnlyCollection<string> pendingFiles)
    {
        InvokeIfNeeded(() =>
        {
            var files = (pendingFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();

            _queueModeLabel.Text = autoProcessing
                ? "Queue mode: Live upload"
                : "Queue mode: Queue only (uploads paused)";
            _queueModeLabel.ForeColor = autoProcessing ? C_GREEN : C_ORANGE;
            _queueSummaryLabel.Text = $"Pending local videos: {files.Count}";
            _queueToggleBtn.Text = autoProcessing ? "Pause Uploads (Queue Only)" : "Resume Upload Queue";
            _queueProcessNowBtn.Enabled = (_setQueueProcessing != null) && files.Count > 0;

            _pendingQueueList.BeginUpdate();
            _pendingQueueList.Items.Clear();
            if (files.Count == 0)
            {
                _pendingQueueList.Items.Add("No pending local videos.");
            }
            else
            {
                foreach (var file in files)
                {
                    _pendingQueueList.Items.Add($"{Path.GetFileName(file)}   —   {file}");
                }
            }
            _pendingQueueList.EndUpdate();
        });
    }

    public void AddEventLog(string message, Color color)
    {
        InvokeIfNeeded(() =>
        {
            TrimRichTextBox(_eventLogBox, MaxUiEventLogChars);
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

    // ── Logging ──

    void LoadExistingLogs() { foreach (var e in Logger.Entries) AppendLog(e); }

    void OnNewLog(LogEntry e)
    {
        if (IsDisposed) return;
        try { InvokeIfNeeded(() => AppendLog(e)); } catch { }
    }

    void AppendLog(LogEntry e)
    {
        TrimRichTextBox(_logBox, MaxUiLogChars);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = e.Level switch
        {
            LogLevel.Error => C_ERR,
            LogLevel.Warning => Color.FromArgb(251, 191, 36),
            LogLevel.Info => C_T1,
            _ => C_T3,
        };
        _logBox.AppendText(e.ToString() + "\n");
        _logBox.ScrollToCaret();
    }

    static void TrimRichTextBox(RichTextBox box, int maxChars)
    {
        if (box.TextLength < maxChars)
            return;

        var trimTo = Math.Max(maxChars / 4, 1024);
        box.Select(0, trimTo);
        box.SelectedText = string.Empty;
    }

    // ── Actions ──

    void SaveSettings()
    {
        var url = _urlBox.Text.Trim();
        if (url.Length == 0) { Status("Server URL is required", true); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) { Status("Invalid URL", true); return; }
        if (_tokenBox.Text.Trim().Length == 0) { Status("API token is required", true); return; }
        if (!_selfSignedBox.Checked && _certPathBox.Text.Trim().Length > 0 && !File.Exists(_certPathBox.Text.Trim()))
        {
            Status("Pinned certificate file was not found", true);
            return;
        }

        _settings.ServerUrl = url;
        _settings.ApiToken = _tokenBox.Text.Trim();
        _settings.WatchFolder = _watchBox.Text.Trim();
        _settings.WatchSubfolders = _subfoldersBox.Checked;
        _settings.ShowNotifications = _notifyBox.Checked;
        _settings.MoveAfterUpload = _moveBox.Checked;
        _settings.DeleteAfterUpload = _deleteBox.Checked && !_settings.MoveAfterUpload;
        _settings.MoveToFolder = _moveToBox.Text.Trim();
        _settings.MaxRetries = (int)_retriesBox.Inner.Value;
        _settings.ScanOnLaunch = _scanOnLaunchBox.Checked;
        _settings.LocalCompress = _localCompressBox.Checked;
        _settings.StopOnCompressionFailure = _compressionHardFailBox.Checked;
        _settings.PlaySounds = _soundBox.Checked;
        _settings.AutoCheckForUpdates = _autoUpdateBox.Checked;
        _settings.CompressionPreset = (_presetBox.Inner.SelectedItem?.ToString() ?? CompressionPreset.Balanced);
        _settings.AllowSelfSignedCerts = _selfSignedBox.Checked;
        _settings.TrustedCertPath = _certPathBox.Text.Trim();
        _settings.EnableQueuePersistence = _queuePersistenceBox.Checked;
        _settings.AutoProcessQueue = _autoProcessQueueBox.Checked;
        _settings.RequireUploadChecksum = _requireChecksumBox.Checked;
        _settings.AdaptiveCompressionWhenGaming = _gameCompressionBox.Checked;
        _settings.EnablePolicySync = _policySyncBox.Checked;
        _settings.Save();
        UploadService.Reconfigure(_settings);

        StartupManager.SetEnabled(_startupBox.Checked);

        Logger.Info("Settings saved.");
        Status("Saved!", false);
        _ = Task.Delay(800).ContinueWith(_ =>
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke(Close);
        }, TaskScheduler.Default);
    }

    void GenerateCert()
    {
        // Use the server URL hostname as the CN so the cert matches the host being connected to.
        var subjectName = "velo-server";
        if (Uri.TryCreate(_urlBox.Text.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            subjectName = uri.Host;

        try
        {
            var (pfxPath, crtPath) = TlsCertHelper.GenerateSelfSignedCert(subjectName);
            _certPathBox.Text = crtPath;
            UpdateTlsUi();
            MessageBox.Show(
                $"Certificate generated successfully.\n\n" +
                $"Public cert (uploader trust):  {crtPath}\n" +
                $"Server cert + key (VELO HTTPS): {pfxPath}\n\n" +
                $"Copy {Path.GetFileName(pfxPath)} to your VELO server and configure it as the HTTPS certificate.",
                "Certificate generated",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to generate certificate:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void SaveFilters()
    {
        _settings.IgnoredFolders = [.. _foldersList.Inner.Items.Cast<string>()];
        _settings.IgnoredPatterns = [.. _patternsList.Inner.Items.Cast<string>()];
        _settings.MaxFileSizeMB = (int)_maxSizeBox.Inner.Value;
        _settings.Save();
        Logger.Info($"Filters saved — folders: {_settings.IgnoredFolders.Count}, patterns: {_settings.IgnoredPatterns.Count}");
        Status("Filters saved!", false);
    }

    async Task TestConnection()
    {
        var baseUrl = _urlBox.Text.Trim();
        var url = baseUrl.TrimEnd('/') + "/api/videos";
        var token = _tokenBox.Text.Trim();
        if (url.Length < 10 || token.Length == 0) { Status("Fill URL + token first", true); return; }
        if (!ValidatePinnedCertPath()) return;

        _testBtn.Enabled = false;
        _testTlsBtn.Enabled = false;
        Status("Testing API token against authenticated upload endpoint...", false);
        try
        {
            using var handler = TlsCertHelper.CreateHandler(BuildTlsProbeSettings());
            using var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var content = new ByteArrayContent([]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            var r = await h.PostAsync(url, content);
            var body = await r.Content.ReadAsStringAsync();

            if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized || r.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                Status("API token rejected by server", true);
                return;
            }

            // This endpoint is authenticated first, then validates the upload payload.
            // Any non-auth response proves the token was accepted.
            if ((int)r.StatusCode >= 400 && (int)r.StatusCode < 500)
            {
                Status($"API token OK • Server auth passed ({(int)r.StatusCode})", false);
                return;
            }

            Status($"API token OK • HTTP {(int)r.StatusCode}", false);
        }
        catch (Exception ex) { Status($"API test failed: {ex.Message}", true); }
        finally { _testBtn.Enabled = true; _testTlsBtn.Enabled = true; }
    }

    async Task TestTlsConnection()
    {
        var baseUrl = _urlBox.Text.Trim();
        if (baseUrl.Length < 10) { Status("Fill Server URL first", true); return; }
        if (!ValidatePinnedCertPath()) return;

        _testBtn.Enabled = false;
        _testTlsBtn.Enabled = false;
        Status("Testing TLS handshake and certificate validation...", false);
        try
        {
            using var handler = TlsCertHelper.CreateHandler(BuildTlsProbeSettings());
            using var h = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/");
            using var response = await h.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            var mode = _selfSignedBox.Checked
                ? "self-signed allowed"
                : _certPathBox.Text.Trim().Length > 0
                    ? "pinned cert"
                    : "system trust";
            Status($"TLS OK • {mode} • HTTP {(int)response.StatusCode}", false);
        }
        catch (Exception ex)
        {
            Status($"TLS failed: {ex.Message}", true);
        }
        finally
        {
            _testBtn.Enabled = true;
            _testTlsBtn.Enabled = true;
        }
    }

    AppSettings BuildTlsProbeSettings() => new()
    {
        AllowSelfSignedCerts = _selfSignedBox.Checked,
        TrustedCertPath = _certPathBox.Text.Trim(),
    };

    bool ValidatePinnedCertPath()
    {
        var certPath = _certPathBox.Text.Trim();
        if (_selfSignedBox.Checked || certPath.Length == 0)
            return true;

        if (!File.Exists(certPath))
        {
            Status("Pinned certificate file was not found", true);
            return false;
        }

        return true;
    }

    void UpdateTlsUi()
    {
        var selfSigned = _selfSignedBox.Checked;
        _certPathBox.Enabled = !selfSigned;

        var certPath = _certPathBox.Text.Trim();
        if (selfSigned)
        {
            _certInfoLabel.Text = "Using relaxed TLS validation. Pinned cert is ignored.";
            _certInfoLabel.ForeColor = C_T3;
            return;
        }

        if (certPath.Length == 0)
        {
            _certInfoLabel.Text = "Using Windows system trust store.";
            _certInfoLabel.ForeColor = C_T3;
            return;
        }

        if (!File.Exists(certPath))
        {
            _certInfoLabel.Text = "Pinned certificate file not found.";
            _certInfoLabel.ForeColor = C_ERR;
            return;
        }

        var info = TlsCertHelper.GetCertInfo(certPath);
        _certInfoLabel.Text = info.Length > 0 ? info : "Pinned certificate loaded.";
        _certInfoLabel.ForeColor = info.Length > 0 ? C_T2 : C_GREEN;
    }

    void Status(string msg, bool err)
    {
        _statusLabel.ForeColor = err ? C_ERR : C_GREEN;
        _statusLabel.Text = msg;
    }

    void OnHistoryChanged()
    {
        if (IsDisposed) return;
        try { InvokeIfNeeded(LoadHistory); } catch { }
    }

    void LoadHistory()
    {
        _historyEntries = [.. UploadHistoryManager.Load()];
        _historyList.Inner.Items.Clear();
        foreach (var entry in _historyEntries)
            _historyList.Inner.Items.Add(entry.ToString());
    }

    UploadHistoryEntry? SelectedHistoryEntry()
    {
        var index = _historyList.Inner.SelectedIndex;
        return index >= 0 && index < _historyEntries.Count ? _historyEntries[index] : null;
    }

    static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }
}
