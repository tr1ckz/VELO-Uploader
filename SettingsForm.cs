using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VeloUploader;

// ── Borderless dark text box  ──
class DarkTextBox : Panel
{
    public readonly TextBox Inner;
    private bool _focused;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DrawRightBorder { get; set; } = true;

    static readonly Color C_BG = Color.FromArgb(26, 26, 26);
    static readonly Color C_BORDER = Color.FromArgb(45, 45, 45);
    static readonly Color C_FOCUS = Color.FromArgb(139, 92, 246);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkTextBox(string text, string placeholder, int x, int y, int w)
    {
        Location = new Point(x, y);
        Size = new Size(w, 24);
        BackColor = C_BG;

        Inner = new TextBox
        {
            Text = text,
            PlaceholderText = placeholder,
            Location = new Point(6, 3),
            Size = new Size(w - 12, 18),
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
        e.Graphics.DrawLine(pen, 0, 0, Width - 1, 0);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
        e.Graphics.DrawLine(pen, 0, 0, 0, Height - 1);
        if (DrawRightBorder)
            e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height - 1);
    }
}

// ── Borderless dark listbox ──
class DarkListBox : Panel
{
    public readonly ListBox Inner;

    static readonly Color C_BG = Color.FromArgb(26, 26, 26);
    static readonly Color C_BORDER = Color.FromArgb(45, 45, 45);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);
    static readonly Color C_SEL = Color.FromArgb(34, 31, 48);
    static readonly Color C_ACCENT = Color.FromArgb(139, 92, 246);

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
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 18,
        };
        Inner.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= Inner.Items.Count)
                return;

            var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var fillBrush = new SolidBrush(isSelected ? C_SEL : C_BG);
            e.Graphics.FillRectangle(fillBrush, e.Bounds);

            if (isSelected)
            {
                using var accentBrush = new SolidBrush(C_ACCENT);
                e.Graphics.FillRectangle(accentBrush, e.Bounds.X, e.Bounds.Y + 1, 3, Math.Max(0, e.Bounds.Height - 2));
            }

            var textBounds = Rectangle.Inflate(e.Bounds, -8, 0);
            TextRenderer.DrawText(
                e.Graphics,
                Inner.Items[e.Index]?.ToString() ?? string.Empty,
                Inner.Font,
                textBounds,
                C_FG,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (isSelected)
                e.DrawFocusRectangle();
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

    static readonly Color C_BG = Color.FromArgb(26, 26, 26);
    static readonly Color C_BORDER = Color.FromArgb(45, 45, 45);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkNumeric(int val, int min, int max, int x, int y, int w)
    {
        Location = new Point(x, y);
        Size = new Size(w, 24);
        BackColor = C_BG;

        Inner = new NumericUpDown
        {
            Value = Math.Clamp(val, min, max),
            Minimum = min,
            Maximum = max,
            Location = new Point(1, 1),
            Size = new Size(w - 2, 22),
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

    static readonly Color C_BG = Color.FromArgb(26, 26, 26);
    static readonly Color C_BORDER = Color.FromArgb(45, 45, 45);
    static readonly Color C_FG = Color.FromArgb(240, 240, 245);

    public DarkComboBox(int x, int y, int w, IEnumerable<string> items, string selected)
    {
        Location = new Point(x, y);
        Size = new Size(w, 24);
        BackColor = C_BG;

        Inner = new ComboBox
        {
            Location = new Point(1, 1),
            Size = new Size(w - 2, 22),
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

class AccentCheckBox : CheckBox
{
    static readonly Color C_BOX_BG = Color.FromArgb(17, 17, 17);
    static readonly Color C_BOX_BORDER = Color.FromArgb(68, 68, 68);
    static readonly Color C_TEXT = Color.FromArgb(155, 155, 165);
    static readonly Color C_CHECK = Color.FromArgb(139, 92, 246);

    public AccentCheckBox()
    {
        AutoSize = false;
        Height = 20;
        ForeColor = C_TEXT;
        Font = new Font("Segoe UI", 8f);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Color.FromArgb(12, 12, 15));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var box = new Rectangle(0, Math.Max(0, (Height - 14) / 2), 14, 14);
        using (var back = new SolidBrush(C_BOX_BG))
            e.Graphics.FillRectangle(back, box);
        using (var pen = new Pen(C_BOX_BORDER, 1))
            e.Graphics.DrawRectangle(pen, box);

        if (Checked)
        {
            using var pen = new Pen(C_CHECK, 2f);
            e.Graphics.DrawLines(pen,
            [
                new Point(box.Left + 3, box.Top + 7),
                new Point(box.Left + 6, box.Bottom - 3),
                new Point(box.Right - 3, box.Top + 3),
            ]);
        }

        var textRect = new Rectangle(box.Right + 8, 0, Math.Max(0, Width - box.Right - 8), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, C_TEXT, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, textRect, C_TEXT, Color.Transparent);
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
    private DarkListBox _pendingQueueList;
    private Button _queueToggleBtn;
    private Button _queueProcessNowBtn;
    private Button _quickEditorBtn;
    private readonly Action<bool, bool>? _setQueueProcessing;
    private readonly Action<AppSettings?>? _openQuickEditor;

    // Palette
    static readonly Color C_BG = Color.FromArgb(12, 12, 12);
    static readonly Color C_PANEL = Color.FromArgb(18, 18, 18);
    static readonly Color C_INPUT = Color.FromArgb(26, 26, 26);
    static readonly Color C_BORDER = Color.FromArgb(45, 45, 45);
    static readonly Color C_T1 = Color.FromArgb(240, 240, 245);
    static readonly Color C_T2 = Color.FromArgb(155, 155, 165);
    static readonly Color C_T3 = Color.FromArgb(105, 105, 112);
    static readonly Color C_ACCENT = Color.FromArgb(139, 92, 246);
    static readonly Color C_ACCENT_H = Color.FromArgb(155, 117, 248);
    static readonly Color C_BTN = Color.FromArgb(42, 42, 42);
    static readonly Color C_BTN_H = Color.FromArgb(58, 58, 58);
    static readonly Color C_RED = Color.FromArgb(160, 38, 38);
    static readonly Color C_GREEN = Color.FromArgb(74, 222, 128);
    static readonly Color C_ORANGE = Color.FromArgb(251, 146, 60);
    static readonly Color C_ERR = Color.FromArgb(248, 113, 113);

    public SettingsForm(AppSettings settings, int initialTab = 0, Action<bool, bool>? setQueueProcessing = null, Action<AppSettings?>? openQuickEditor = null)
    {
        _settings = settings;
        _activeTab = Math.Clamp(initialTab, 0, 3);
        _setQueueProcessing = setQueueProcessing;
        _openQuickEditor = openQuickEditor;
        SuspendLayout();

        Text = "VELO Uploader Control Center";
        ClientSize = new Size(680, 900);
        MinimumSize = new Size(700, 760);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        const int pageTop = 34;
        const int footerHeight = 42;
        int pageHeight = ClientSize.Height - pageTop - footerHeight;

        // Load icon from embedded resource
        try
        {
            var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // ── Header ──
        var header = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = C_PANEL };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BORDER, 1);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        Controls.Add(header);

        var headerVersion = MkLabel($"v{GitHubUpdater.GetCurrentVersion()}", 0, 10, new Font("Consolas", 7f, FontStyle.Bold), C_T3);
        headerVersion.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        header.Controls.Add(headerVersion);

        // ── Compact tab bar ──
        var tabBar = new Panel
        {
            Location = new Point(8, 2),
            Size = new Size(Math.Max(360, ClientSize.Width - 96), 28),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        header.Controls.Add(tabBar);

        _tabBtns = new Button[4];
        string[] tabNames = ["QUEUE", "SETTINGS", "LOGS", "STATUS"];
        for (int i = 0; i < _tabBtns.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = tabNames[i],
                Location = new Point(i * 82 + 4, 0),
                Size = new Size(78, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = C_T3,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false,
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 30, 30);
            btn.Click += (_, _) => SwitchTab(idx);
            tabBar.Controls.Add(btn);
            _tabBtns[i] = btn;
        }

        tabBar.Paint += (_, e) =>
        {
            var tabIndex = Math.Clamp(_activeTab, 0, _tabBtns.Length - 1);
            var btn = _tabBtns[tabIndex];
            using var brush = new SolidBrush(C_ACCENT);
            e.Graphics.FillRectangle(brush, btn.Left + 6, 22, btn.Width - 12, 2);
        };

        void LayoutHeader()
        {
            tabBar.Location = new Point(8, 2);
            tabBar.Width = Math.Max(360, header.ClientSize.Width - 96);
            headerVersion.Location = new Point(Math.Max(12, header.ClientSize.Width - headerVersion.PreferredWidth - 8), 10);
        }

        header.SizeChanged += (_, _) => LayoutHeader();
        LayoutHeader();

        // ── Pages ──
        _pages = new Panel[4];
        for (int i = 0; i < _pages.Length; i++)
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

        var footer = new Panel { Dock = DockStyle.Bottom, Height = footerHeight, BackColor = C_PANEL, Padding = new Padding(12, 8, 12, 8) };
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

        var footerSaveBtn = MkBtn("Save & Start Watching", 0, 8, 188, 24, C_BTN, C_BTN_H);
        footerSaveBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        footerSaveBtn.Click += (_, _) => SaveSettings();
        footer.Controls.Add(footerSaveBtn);

        void LayoutFooter()
        {
            int spacing = 6;
            footerText.Location = new Point(12, 11);
            footerLink.Location = new Point(12 + footerText.PreferredWidth + spacing, 11);
            footerSaveBtn.Location = new Point(Math.Max(footer.Width - footerSaveBtn.Width - 12, 12), 8);
        }

        footer.SizeChanged += (_, _) => LayoutFooter();
        LayoutFooter();

        void LayoutShell()
        {
            int dynamicPageTop = header.Bottom;
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
        //  PAGE 0: GENERAL (queue + recent videos)
        // ═══════════════════════════════════════
        var home = _pages[0];
        int y = 10, lx = 22, w = 636;

        MkSectionLabel(home, "QUEUE + RECENT VIDEOS", lx, y); y += 16;

        _queueModeLabel = MkLedLabel("LIVE UPLOAD", lx, y, C_GREEN);
        home.Controls.Add(_queueModeLabel);
        _queueSummaryLabel = MkLabel("PENDING LOCAL VIDEOS: 0", lx + 198, y + 1, new Font("Consolas", 8f, FontStyle.Bold), C_T2);
        home.Controls.Add(_queueSummaryLabel);
        y += 22;

        _quickEditorBtn = MkBtn("Open Video Editor", lx, y, 128, 24, C_BTN, C_ACCENT_H);
        _quickEditorBtn.Enabled = _openQuickEditor != null;
        _quickEditorBtn.Click += (_, _) => OpenQuickEditorFromCurrentSettings();
        home.Controls.Add(_quickEditorBtn);

        _queueProcessNowBtn = MkBtn("Process Queue", lx + 132, y, 102, 24, C_BTN, C_ACCENT_H);
        _queueProcessNowBtn.Enabled = _setQueueProcessing != null;
        _queueProcessNowBtn.Click += (_, _) => _setQueueProcessing?.Invoke(true, true);
        home.Controls.Add(_queueProcessNowBtn);

        _queueToggleBtn = MkBtn("Queue Only", lx + 238, y, 96, 24, C_BTN, C_ACCENT_H);
        _queueToggleBtn.Enabled = _setQueueProcessing != null;
        _queueToggleBtn.Click += (_, _) => _setQueueProcessing?.Invoke(_queueToggleBtn.Text.Contains("Resume", StringComparison.OrdinalIgnoreCase), true);
        home.Controls.Add(_queueToggleBtn);

        var openSettingsBtn = MkBtn("Open Settings", lx + 338, y, 102, 24, C_BTN, C_ACCENT_H);
        openSettingsBtn.Click += (_, _) => ShowTab(1);
        home.Controls.Add(openSettingsBtn);

        var openLogsBtn = MkBtn("Open Logs", lx + 444, y, 88, 24, C_BTN, C_ACCENT_H);
        openLogsBtn.Click += (_, _) => ShowTab(2);
        home.Controls.Add(openLogsBtn);
        y += 34;

        MkSectionLabel(home, "PENDING QUEUE", lx, y); y += 18;
        _pendingQueueList = new DarkListBox(lx, y, w, 160);
        _pendingQueueList.Inner.HorizontalScrollbar = true;
        _pendingQueueList.Inner.Items.Add("No pending local videos.");
        home.Controls.Add(_pendingQueueList);
        y += 176;

        MkSectionLabel(home, "RECENT VIDEO ACTIVITY", lx, y); y += 18;
        _historyList = new DarkListBox(lx, y, w, 240);
        home.Controls.Add(_historyList);

        var copyHistory = MkBtn("Copy URL", lx, y + 248, 88, 28, C_BTN, C_BTN_H);
        copyHistory.Click += (_, _) =>
        {
            var entry = SelectedHistoryEntry();
            if (!string.IsNullOrWhiteSpace(entry?.Url))
            {
                try { Clipboard.SetText(entry.Url); } catch { }
            }
        };
        home.Controls.Add(copyHistory);

        var clearHistory = MkBtn("Clear", lx + 96, y + 248, 72, 28, C_RED, Color.FromArgb(190, 50, 50));
        clearHistory.Click += (_, _) => UploadHistoryManager.Clear();
        home.Controls.Add(clearHistory);

        var historyHint = new Label
        {
            Location = new Point(lx, y + 282),
            Size = new Size(w, 48),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8f),
            Text = "Select a recent video to inspect what happened. Successful uploads can be copied back to the clipboard.",
        };
        home.Controls.Add(historyHint);

        LoadHistory();
        _historyList.Inner.SelectedIndexChanged += (_, _) =>
        {
            var entry = SelectedHistoryEntry();
            if (entry == null)
            {
                historyHint.Text = "Select a recent video to inspect what happened. Successful uploads can be copied back to the clipboard.";
                return;
            }

            var status = entry.Success ? "Success" : $"Failed: {entry.Error}";
            var compression = entry.UsedCompression ? $"Compressed ({entry.CompressionPreset})" : "Original upload";
            historyHint.Text = $"{status} • {compression} • {FormatSize(entry.SourceSizeBytes)} -> {FormatSize(entry.UploadedSizeBytes)}";
        };
        UploadHistoryManager.Changed += OnHistoryChanged;

        // ═══════════════════════════════════════
        //  PAGE 1: SETTINGS
        // ═══════════════════════════════════════
        var g = _pages[1];
        y = 10;
        int colB = lx + 168;
        int colBW = w - 168;

        // ─── QUICK ACTIONS ───
        Section(g, "QUICK ACTIONS", lx, y); y += 18;

        var openEditorBtn = MkBtn("Open Video Editor", lx, y, 118, 24, C_BTN, C_ACCENT_H);
        openEditorBtn.Click += (_, _) => OpenQuickEditorFromCurrentSettings();
        openEditorBtn.Enabled = _openQuickEditor != null;
        g.Controls.Add(openEditorBtn);

        var openQueueBtn = MkBtn("Open General", lx + 122, y, 92, 24, C_BTN, C_ACCENT_H);
        openQueueBtn.Click += (_, _) => ShowTab(0);
        g.Controls.Add(openQueueBtn);

        var processNowBtn = MkBtn("Process Queue", lx + 218, y, 96, 24, C_BTN, C_ACCENT_H);
        processNowBtn.Click += (_, _) => _setQueueProcessing?.Invoke(true, true);
        processNowBtn.Enabled = _setQueueProcessing != null;
        g.Controls.Add(processNowBtn);

        var pauseQueueBtn = MkBtn("Queue Only", lx + 318, y, 86, 24, C_BTN, C_ACCENT_H);
        pauseQueueBtn.Click += (_, _) => _setQueueProcessing?.Invoke(false, false);
        pauseQueueBtn.Enabled = _setQueueProcessing != null;
        g.Controls.Add(pauseQueueBtn);
        y += 32;

        // ─── CONNECTION ───
        Section(g, "CONNECTION", lx, y); y += 18;

        Lbl(g, "Server URL", lx, y);
        _urlBox = new DarkTextBox(settings.ServerUrl, "https://clips.example.com", colB, y, colBW);
        _urlBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_urlBox);
        y += 28;

        Lbl(g, "API Token", lx, y);
        _tokenBox = new DarkTextBox(settings.ApiToken, "velo_...", colB, y, colBW - 80);
        _tokenBox.UseSystemPasswordChar = true;
        _tokenBox.DrawRightBorder = false;
        _tokenBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_tokenBox);
        _testBtn = MkBtn("Test API", lx + w - 80, y, 80, 24, C_ACCENT, C_ACCENT_H, true);
        _testBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _testBtn.Click += async (_, _) => await TestConnection();
        g.Controls.Add(_testBtn);
        y += 28;

        _statusLabel = new Label
        {
            Location = new Point(colB, y),
            Size = new Size(colBW, 18),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        g.Controls.Add(_statusLabel);
        y += 22;

        // ─── SECURITY ───
        Section(g, "SECURITY", lx, y); y += 18;

        _selfSignedBox = MkChk("Allow self-signed / untrusted server certificate", settings.AllowSelfSignedCerts, lx, y);
        g.Controls.Add(_selfSignedBox);
        y += 26;

        Lbl(g, "Pinned Cert", lx, y);
        _certPathBox = new DarkTextBox(settings.TrustedCertPath, "Trusted .crt/.cer/.pem file", colB, y, colBW - 228);
        _certPathBox.DrawRightBorder = false;
        _certPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_certPathBox);

        var certBrowseBtn = MkBtn("Browse", lx + w - 228, y, 76, 24, C_BTN, C_BTN_H, true);
        certBrowseBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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
        var genCertBtn = MkBtn("Generate", lx + w - 152, y, 76, 24, C_BTN, C_BTN_H, true);
        genCertBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        genCertBtn.Click += (_, _) => GenerateCert();
        g.Controls.Add(genCertBtn);
        _testTlsBtn = MkBtn("Test TLS", lx + w - 76, y, 76, 24, C_BTN, C_BTN_H, true);
        _testTlsBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _testTlsBtn.Click += async (_, _) => await TestTlsConnection();
        g.Controls.Add(_testTlsBtn);
        y += 28;

        _certInfoLabel = new Label
        {
            Location = new Point(colB, y),
            Size = new Size(colBW, 18),
            ForeColor = C_T3,
            Font = new Font("Segoe UI", 7.5f),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        g.Controls.Add(_certInfoLabel);

        y += 22;

        // ─── RECORDINGS ───
        Section(g, "RECORDINGS", lx, y); y += 18;

        Lbl(g, "Watch Folder", lx, y);
        _watchBox = new DarkTextBox(settings.WatchFolder, @"D:\recordings", colB, y, colBW - 72);
        _watchBox.DrawRightBorder = false;
        _watchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_watchBox);
        var browseBtn = MkBtn("Browse", lx + w - 72, y, 72, 24, C_BTN, C_BTN_H, true);
        browseBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        browseBtn.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = _watchBox.Text }; if (d.ShowDialog() == DialogResult.OK) _watchBox.Text = d.SelectedPath; };
        g.Controls.Add(browseBtn);
        y += 28;

        _subfoldersBox = MkChk("Include subfolders", settings.WatchSubfolders, lx, y);
        g.Controls.Add(_subfoldersBox);
        _deleteBox = MkChk("Delete clip after upload", settings.DeleteAfterUpload, lx + 340, y);
        g.Controls.Add(_deleteBox);
        y += 24;

        _moveBox = MkChk("Move clip after upload", settings.MoveAfterUpload, lx, y);
        g.Controls.Add(_moveBox);
        y += 26;
        Lbl(g, "Destination", lx, y);
        _moveToBox = new DarkTextBox(settings.MoveToFolder, @"D:\archived-clips", colB, y, colBW - 72);
        _moveToBox.DrawRightBorder = false;
        _moveToBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_moveToBox);
        var moveBrowseBtn = MkBtn("Browse", lx + w - 72, y, 72, 24, C_BTN, C_BTN_H, true);
        moveBrowseBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
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
        y += 28;

        _scanOnLaunchBox = MkChk("Upload existing clips on launch", settings.ScanOnLaunch, lx, y);
        g.Controls.Add(_scanOnLaunchBox);
        _notifyBox = MkChk("Desktop notifications", settings.ShowNotifications, lx + 340, y);
        g.Controls.Add(_notifyBox);
        y += 24;

        // ─── COMPRESSION ───
        Section(g, "COMPRESSION", lx, y); y += 18;

        Lbl(g, "Preset", lx, y + 2);
        
        var gpuAvailable = LocalCompressor.IsGPUAvailable();
        var presetOptions = gpuAvailable ? CompressionPreset.All : CompressionPreset.AllCPU;
        _presetBox = new DarkComboBox(colB, y, 140, presetOptions, settings.CompressionPreset);
        g.Controls.Add(_presetBox);

        // GPU status
        g.Controls.Add(MkLedLabel(gpuAvailable ? "GPU READY" : "CPU ONLY", lx + w - 132, y + 4, gpuAvailable ? C_GREEN : C_T3));

        Lbl(g, "Retries", colB + 200, y + 2);
        _retriesBox = new DarkNumeric(settings.MaxRetries, 1, 10, colB + 260, y, 60);
        g.Controls.Add(_retriesBox);
        y += 28;

        _localCompressBox = MkChk("Compress locally before upload (FFmpeg)", settings.LocalCompress, lx, y);
        g.Controls.Add(_localCompressBox);
        _compressionHardFailBox = MkChk("Skip upload if compression fails", settings.StopOnCompressionFailure, lx + 340, y);
        g.Controls.Add(_compressionHardFailBox);
        y += 24;

        // ─── SYSTEM ───
        Section(g, "SYSTEM", lx, y); y += 18;

        _startupBox = MkChk("Start with Windows", StartupManager.IsRegistered(), lx, y);
        g.Controls.Add(_startupBox);
        _soundBox = MkChk("Play success/failure sounds", settings.PlaySounds, lx + 340, y);
        g.Controls.Add(_soundBox);
        y += 24;

        _autoUpdateBox = MkChk("Check GitHub for app updates on launch", settings.AutoCheckForUpdates, lx, y);
        g.Controls.Add(_autoUpdateBox);
        y += 24;

        // ─── UPLOAD BEHAVIOR ───
        Section(g, "UPLOAD BEHAVIOR", lx, y); y += 18;

        _queuePersistenceBox = MkChk("Persist upload queue across restarts", settings.EnableQueuePersistence, lx, y);
        g.Controls.Add(_queuePersistenceBox);
        _requireChecksumBox = MkChk("Require checksum validation on upload", settings.RequireUploadChecksum, lx + 340, y);
        g.Controls.Add(_requireChecksumBox);
        y += 24;

        _autoProcessQueueBox = MkChk("Start uploads immediately when new clips arrive", settings.AutoProcessQueue, lx, y);
        g.Controls.Add(_autoProcessQueueBox);
        _gameCompressionBox = MkChk("Use low-impact compression while gaming", settings.AdaptiveCompressionWhenGaming, lx + 340, y);
        g.Controls.Add(_gameCompressionBox);
        _policySyncBox = MkChk("Sync upload settings from server on launch", settings.EnablePolicySync, lx + 340, y);
        g.Controls.Add(_policySyncBox);
        y += 24;

        _selfSignedBox.CheckedChanged += (_, _) => UpdateTlsUi();
        _certPathBox.Inner.TextChanged += (_, _) => UpdateTlsUi();
        UpdateTlsUi();

        // ─── RULES + FILTERS ───
        Section(g, "RULES + FILTERS", lx, y); y += 22;

        Section(g, "IGNORED FOLDERS", lx, y);
        g.Controls.Add(MkLabel("Clips in these folder names are skipped", lx + 130, y + 1, new Font("Segoe UI", 7.5f), C_T3));
        y += 22;

        _foldersList = new DarkListBox(lx, y, w - 82, 78);
        _foldersList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        foreach (var fol in settings.IgnoredFolders) _foldersList.Inner.Items.Add(fol);
        g.Controls.Add(_foldersList);
        var rmF = MkBtn("Remove", lx + w - 82, y, 82, 24, C_RED, Color.FromArgb(190, 50, 50));
        rmF.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        rmF.Click += (_, _) => { if (_foldersList.Inner.SelectedIndex >= 0) _foldersList.Inner.Items.RemoveAt(_foldersList.Inner.SelectedIndex); };
        g.Controls.Add(rmF);
        y += 88;

        _addFolderBox = new DarkTextBox("", "Folder name (e.g. Desktop)", lx, y, w - 120);
        _addFolderBox.DrawRightBorder = false;
        _addFolderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_addFolderBox);
        var addF = MkBtn("Add", lx + w - 120, y, 50, 24, C_BTN, C_BTN_H, true);
        addF.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addF.Click += (_, _) => { var t = _addFolderBox.Text.Trim(); if (t.Length > 0 && !_foldersList.Inner.Items.Contains(t)) { _foldersList.Inner.Items.Add(t); _addFolderBox.Text = ""; } };
        g.Controls.Add(addF);
        var brF = MkBtn("Browse", lx + w - 70, y, 70, 24, C_BTN, C_BTN_H, true);
        brF.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        brF.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog() == DialogResult.OK) { var n = new DirectoryInfo(d.SelectedPath).Name; if (!_foldersList.Inner.Items.Contains(n)) _foldersList.Inner.Items.Add(n); } };
        g.Controls.Add(brF);
        y += 42;

        Section(g, "IGNORED PATTERNS", lx, y);
        g.Controls.Add(MkLabel("Wildcards: * any, ? single", lx + 140, y + 1, new Font("Segoe UI", 7.5f), C_T3));
        y += 22;

        _patternsList = new DarkListBox(lx, y, w - 82, 68);
        _patternsList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        foreach (var p in settings.IgnoredPatterns) _patternsList.Inner.Items.Add(p);
        g.Controls.Add(_patternsList);
        var rmP = MkBtn("Remove", lx + w - 82, y, 82, 24, C_RED, Color.FromArgb(190, 50, 50));
        rmP.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        rmP.Click += (_, _) => { if (_patternsList.Inner.SelectedIndex >= 0) _patternsList.Inner.Items.RemoveAt(_patternsList.Inner.SelectedIndex); };
        g.Controls.Add(rmP);
        y += 78;

        _addPatternBox = new DarkTextBox("", "e.g. *_temp.mp4", lx, y, w - 56);
        _addPatternBox.DrawRightBorder = false;
        _addPatternBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        g.Controls.Add(_addPatternBox);
        var addP = MkBtn("Add", lx + w - 56, y, 56, 24, C_BTN, C_BTN_H, true);
        addP.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addP.Click += (_, _) => { var t = _addPatternBox.Text.Trim(); if (t.Length > 0 && !_patternsList.Inner.Items.Contains(t)) { _patternsList.Inner.Items.Add(t); _addPatternBox.Text = ""; } };
        g.Controls.Add(addP);
        y += 36;

        Lbl(g, "Max File Size", lx, y);
        _maxSizeBox = new DarkNumeric(settings.MaxFileSizeMB, 0, 99999, colB, y, 80);
        g.Controls.Add(_maxSizeBox);
        g.Controls.Add(new Label { Text = "MB  (0 = no limit)", Location = new Point(colB + 88, y + 4), AutoSize = true, ForeColor = C_T3, Font = new Font("Segoe UI", 7.5f), BackColor = Color.Transparent });
        y += 32;

        var saveFilt = MkBtn("Save Filters", lx + w - 108, y, 108, 24, C_BTN, C_BTN_H);
        saveFilt.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        saveFilt.Click += (_, _) => SaveFilters();
        g.Controls.Add(saveFilt);

        // ═══════════════════════════════════════
        //  PAGE 2: LOGS (activity + raw logs)
        // ═══════════════════════════════════════
        var l = _pages[2];
        int ly = 10;

        MkSectionLabel(l, "ACTIVITY", lx, ly); ly += 18;
        _eventLogBox = new RichTextBox
        {
            Location = new Point(lx, ly),
            Size = new Size(w, 120),
            BackColor = C_PANEL,
            ForeColor = C_T2,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8f),
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        l.Controls.Add(_eventLogBox);
        ly += 132;

        MkSectionLabel(l, "RAW LOG", lx, ly); ly += 18;
        _logBox = new RichTextBox
        {
            Location = new Point(lx, ly),
            Size = new Size(w, 240),
            BackColor = C_INPUT,
            ForeColor = C_T2,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 8.5f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };
        l.Controls.Add(_logBox);

        y = ly + 248;
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
        //  PAGE 3: STATUS
        // ═══════════════════════════════════════
        var s = _pages[3];
        int sy = 14;

        // Current Task Section
        MkSectionLabel(s, "CURRENT TASK", lx, sy); sy += 22;

        _currentTaskPanel = new Panel
        {
            Location = new Point(lx, sy),
            Size = new Size(w, 90),
            BackColor = C_PANEL,
            BorderStyle = BorderStyle.None,
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

        _systemStatusLabel = MkLedLabel("WATCHING", lx, sy, C_GREEN);
        s.Controls.Add(_systemStatusLabel);

        _gpuStatusLabel = MkLedLabel("GPU CHECKING", lx + 300, sy, C_T2);
        s.Controls.Add(_gpuStatusLabel);
        sy += 20;

        var ffmpegStatus = LocalCompressor.IsAvailable() ? "FFMPEG READY" : "FFMPEG MISSING";
        var ffmpegColor = LocalCompressor.IsAvailable() ? C_GREEN : C_RED;
        var ffmpegLabel = MkLedLabel(ffmpegStatus, lx, sy, ffmpegColor);
        s.Controls.Add(ffmpegLabel);

        _serverStatusLabel = MkLedLabel("SERVER CHECKING", lx + 300, sy, C_T2);
        s.Controls.Add(_serverStatusLabel);
        sy += 20;

        _quotaStatusLabel = MkLedLabel("STORAGE CHECKING", lx, sy, C_T2);
        s.Controls.Add(_quotaStatusLabel);
        sy += 24;

        // Version Section
        MkSectionLabel(s, "APPLICATION", lx, sy); sy += 18;

        _versionLabel = MkLabel($"v{GitHubUpdater.GetCurrentVersion()}", lx, sy, new Font("Consolas", 8f, FontStyle.Bold), C_T2);
        s.Controls.Add(_versionLabel);

        _updateCheckBtn = new Button
        {
            Location = new Point(lx + 200, sy - 2),
            Size = new Size(120, 24),
            Text = "Check Updates",
            FlatStyle = FlatStyle.Flat,
            BackColor = C_BTN,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _updateCheckBtn.FlatAppearance.BorderSize = 1;
        _updateCheckBtn.FlatAppearance.BorderColor = C_BORDER;
        _updateCheckBtn.FlatAppearance.MouseOverBackColor = C_BTN_H;
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

        sy += 36;
        var statusHint = new Label
        {
            Location = new Point(lx, sy),
            Size = new Size(w, 44),
            ForeColor = C_T2,
            Font = new Font("Segoe UI", 8f),
            Text = "Need activity details? Open the Logs tab. Status stays focused on health, uptime checks, storage, and updates.",
        };
        s.Controls.Add(statusHint);

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
            WindowDarkMode.ApplyDarkMode(Handle);
            await CheckServerStatusAsync();
            await RefreshQuotaAsync();
            _statusRefreshTimer.Start();
        };

        foreach (var page in _pages)
            ApplyObsidianScrollbarTheme(page);
        ApplyObsidianScrollbarTheme(_pendingQueueList.Inner);
        ApplyObsidianScrollbarTheme(_historyList.Inner);
        ApplyObsidianScrollbarTheme(_foldersList.Inner);
        ApplyObsidianScrollbarTheme(_patternsList.Inner);
        ApplyObsidianScrollbarTheme(_eventLogBox);
        ApplyObsidianScrollbarTheme(_logBox);

        SwitchTab(initialTab);
        ResumeLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowDarkMode.ApplyDarkMode(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if ((m.Msg == WM_SETTINGCHANGE || m.Msg == WM_THEMECHANGED) && IsHandleCreated)
            WindowDarkMode.ApplyDarkMode(Handle);
    }

    // ── Tab switching ──

    public void ShowTab(int idx) => SwitchTab(Math.Clamp(idx, 0, _tabBtns.Length - 1));

    void SwitchTab(int idx)
    {
        _activeTab = Math.Clamp(idx, 0, _tabBtns.Length - 1);

        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == _activeTab;
        }

        for (int i = 0; i < _tabBtns.Length; i++)
        {
            _tabBtns[i].ForeColor = i == _activeTab ? C_T1 : C_T3;
            _tabBtns[i].Font = new Font("Segoe UI", 7.5f, i == _activeTab ? FontStyle.Bold : FontStyle.Regular);
        }

        _tabBtns[0].Parent?.Invalidate();
    }

    // ── Factory helpers ──

    static Label MkLabel(string text, int x, int y, Font font, Color color)
    {
        return new Label { Text = text, Location = new Point(x, y), AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent };
    }

    static Label MkLedLabel(string text, int x, int y, Color color)
    {
        return new Label
        {
            Text = $"● {text}",
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Consolas", 8f, FontStyle.Bold),
            ForeColor = color,
            BackColor = Color.Transparent,
        };
    }

    static void SetLedStatus(Label label, string text, Color color)
    {
        label.Text = $"● {text}";
        label.ForeColor = color;
    }

    static Panel AddSectionCard(Control parent, int x, int y, int width, int height, bool drawTopBorder = true)
    {
        var card = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.FromArgb(22, 22, 22),
            Margin = Padding.Empty,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BORDER, 1);
            if (drawTopBorder)
                e.Graphics.DrawLine(pen, 0, 0, card.Width - 1, 0);
            e.Graphics.DrawLine(pen, 0, card.Height - 1, card.Width - 1, card.Height - 1);
            e.Graphics.DrawLine(pen, 0, 0, 0, card.Height - 1);
            e.Graphics.DrawLine(pen, card.Width - 1, 0, card.Width - 1, card.Height - 1);
        };
        parent.Controls.Add(card);
        card.SendToBack();
        return card;
    }

    static Image CreateMonochromeImage(Image source)
    {
        var bitmap = new Bitmap(source.Width, source.Height);
        using var graphics = Graphics.FromImage(bitmap);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { 0.30f, 0.30f, 0.30f, 0f, 0f },
            new[] { 0.59f, 0.59f, 0.59f, 0f, 0f },
            new[] { 0.11f, 0.11f, 0.11f, 0f, 0f },
            new[] { 0f,    0f,    0f,    1f, 0f },
            new[] { 0.08f, 0.08f, 0.08f, 0f, 1f },
        }));
        graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    static Panel AddHeaderBar(Control parent, string title, int x, int y, bool filled = true)
    {
        var bar = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(Math.Max(120, parent.ClientSize.Width - x - 12), filled ? 14 : 14),
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Margin = Padding.Empty,
        };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(34, 34, 34), 1);
            e.Graphics.DrawLine(pen, 0, 0, bar.Width - 1, 0);
        };

        var lbl = new Label
        {
            Text = title,
            Location = new Point(0, 1),
            AutoSize = true,
            ForeColor = C_T3,
            Font = new Font("Consolas", 7f, FontStyle.Bold),
            UseMnemonic = false,
            BackColor = Color.Transparent,
        };
        bar.Controls.Add(lbl);
        parent.Controls.Add(bar);
        return bar;
    }

    static void Section(Control p, string text, int x, int y)
    {
        AddHeaderBar(p, text, Math.Max(0, x - 10), y, false);
    }

    static void Lbl(Control p, string text, int x, int y)
    {
        p.Controls.Add(new Label { Text = text.ToUpperInvariant(), Location = new Point(x, y + 4), AutoSize = true, ForeColor = Color.FromArgb(136, 136, 136), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), BackColor = Color.Transparent });
    }

    static Button MkBtn(string text, int x, int y, int w, int h, Color bg, Color hover, bool joinLeft = false)
    {
        var pressedBg = Color.FromArgb(
            Math.Max(0, bg.R - 18),
            Math.Max(0, bg.G - 18),
            Math.Max(0, bg.B - 18));
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            UseMnemonic = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = pressedBg;
        b.MouseDown += (_, _) => b.Padding = new Padding(0, 1, 0, 0);
        b.MouseUp += (_, _) => b.Padding = Padding.Empty;
        b.MouseLeave += (_, _) => b.Padding = Padding.Empty;
        b.Paint += (_, e) =>
        {
            using var pen = new Pen(C_BORDER, 1);
            e.Graphics.DrawLine(pen, 0, 0, b.Width - 1, 0);
            e.Graphics.DrawLine(pen, 0, b.Height - 1, b.Width - 1, b.Height - 1);
            if (!joinLeft)
                e.Graphics.DrawLine(pen, 0, 0, 0, b.Height - 1);
            e.Graphics.DrawLine(pen, b.Width - 1, 0, b.Width - 1, b.Height - 1);
        };
        return b;
    }

    static CheckBox MkChk(string text, bool v, int x, int y)
    {
        return new AccentCheckBox
        {
            Text = text,
            Checked = v,
            Location = new Point(x, y),
            Width = 300,
            Height = 20,
            BackColor = Color.Transparent,
        };
    }

    static void MkSectionLabel(Control parent, string title, int x, int y)
    {
        AddHeaderBar(parent, title, Math.Max(0, x - 10), y);
    }

    private void ApplyObsidianScrollbarTheme(Control control)
    {
        void ApplyTheme()
        {
            try
            {
                if (control.IsHandleCreated)
                    SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
            }
        }

        control.HandleCreated += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    private void CheckGPUStatus()
    {
        Task.Run(() =>
        {
            var gpuAvail = LocalCompressor.IsGPUAvailable();
            InvokeIfNeeded(() =>
            {
                SetLedStatus(_gpuStatusLabel, gpuAvail ? "GPU READY" : "GPU OFFLINE", gpuAvail ? C_GREEN : C_T3);
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
                SetLedStatus(_serverStatusLabel, "SERVER NOT CONFIGURED", C_T3);
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
                SetLedStatus(_serverStatusLabel, ok
                    ? $"SERVER ONLINE {sw.ElapsedMilliseconds}MS"
                    : $"SERVER ERROR {(int)resp.StatusCode}", ok ? C_GREEN : C_ORANGE);
            });
        }
        catch
        {
            InvokeIfNeeded(() =>
            {
                SetLedStatus(_serverStatusLabel, "SERVER UNREACHABLE", C_ERR);
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
                    SetLedStatus(_quotaStatusLabel, quotaResult.Status switch
                    {
                        QuotaFetchStatus.ServerOutdated => "STORAGE SERVER NEEDS UPDATE",
                        QuotaFetchStatus.Unauthorized => "STORAGE TOKEN UNAUTHORIZED",
                        QuotaFetchStatus.NotConfigured => "STORAGE NOT CONFIGURED",
                        _ => "STORAGE UNAVAILABLE",
                    }, quotaResult.Status == QuotaFetchStatus.ServerOutdated || quotaResult.Status == QuotaFetchStatus.Unauthorized
                        ? C_ORANGE
                        : C_T3);
                    return;
                }
                var quota = quotaResult.Quota!;
                if (!quota.HasQuota)
                {
                    SetLedStatus(_quotaStatusLabel, $"STORAGE {quota.UsedFormatted} USED (UNLIMITED)", C_T2);
                    return;
                }
                var quotaBytes = quota.QuotaBytes!.Value;
                var pct = (int)Math.Min(100, quota.UsedBytes * 100L / quotaBytes);
                var color = pct >= 90 ? C_ERR : pct >= 75 ? C_ORANGE : C_GREEN;
                SetLedStatus(_quotaStatusLabel, $"STORAGE {quota.UsedFormatted} / {quota.QuotaFormatted} ({pct}% USED, {quota.FreeFormatted} FREE)", color);
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
                SetLedStatus(_systemStatusLabel, "WATCHING", C_GREEN);
            }
            else
            {
                SetLedStatus(_systemStatusLabel, "PAUSED", C_ORANGE);
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

            SetLedStatus(_queueModeLabel, autoProcessing ? "LIVE UPLOAD" : "QUEUE ONLY", autoProcessing ? C_GREEN : C_ORANGE);
            _queueSummaryLabel.Text = $"PENDING LOCAL VIDEOS: {files.Count}";
            _queueToggleBtn.Text = autoProcessing ? "Pause Uploads (Queue Only)" : "Resume Upload Queue";
            _queueProcessNowBtn.Enabled = (_setQueueProcessing != null) && files.Count > 0;

            _pendingQueueList.Inner.BeginUpdate();
            _pendingQueueList.Inner.Items.Clear();
            if (files.Count == 0)
            {
                _pendingQueueList.Inner.Items.Add("No pending local videos.");
            }
            else
            {
                foreach (var file in files)
                {
                    _pendingQueueList.Inner.Items.Add($"{Path.GetFileName(file)}   —   {file}");
                }
            }
            _pendingQueueList.Inner.EndUpdate();
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

    AppSettings BuildEditorProbeSettings() => new()
    {
        ServerUrl = _urlBox.Text.Trim(),
        ApiToken = _tokenBox.Text.Trim(),
        WatchFolder = string.IsNullOrWhiteSpace(_watchBox.Text) ? _settings.WatchFolder : _watchBox.Text.Trim(),
        AllowSelfSignedCerts = _selfSignedBox.Checked,
        TrustedCertPath = _certPathBox.Text.Trim(),
    };

    void OpenQuickEditorFromCurrentSettings()
    {
        if (_openQuickEditor == null)
            return;

        if (!ValidatePinnedCertPath())
            return;

        _openQuickEditor(BuildEditorProbeSettings());
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
        var token = _tokenBox.Text.Trim();
        if (baseUrl.Length < 10 || token.Length == 0) { Status("Fill URL + token first", true); return; }
        if (!ValidatePinnedCertPath()) return;

        _testBtn.Enabled = false;
        _testTlsBtn.Enabled = false;
        Status("Testing API token against authenticated upload endpoint...", false);
        try
        {
            var probeSettings = BuildTlsProbeSettings();
            probeSettings.ServerUrl = baseUrl;
            probeSettings.ApiToken = token;

            var validation = await UploadService.ValidateApiTokenAsync(probeSettings);
            Status(validation.Message, !validation.IsValid);
        }
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
