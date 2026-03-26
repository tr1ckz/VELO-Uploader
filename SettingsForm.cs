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
    private readonly AppSettings _settings;
    private readonly DarkTextBox _urlBox, _tokenBox, _watchBox, _addFolderBox, _addPatternBox;
    private readonly CheckBox _subfoldersBox, _notifyBox, _deleteBox, _startupBox, _scanOnLaunchBox, _localCompressBox, _compressionHardFailBox, _soundBox;
    private readonly DarkNumeric _retriesBox, _maxSizeBox;
    private readonly DarkListBox _foldersList, _patternsList, _historyList;
    private readonly DarkComboBox _presetBox;
    private readonly RichTextBox _logBox;
    private readonly Label _statusLabel;
    private readonly Button _testBtn;
    private readonly Panel[] _pages;
    private readonly Button[] _tabBtns;
    private int _activeTab;
    private List<UploadHistoryEntry> _historyEntries = [];

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
    static readonly Color C_ERR = Color.FromArgb(248, 113, 113);

    public SettingsForm(AppSettings settings, int initialTab = 0)
    {
        _settings = settings;
        _activeTab = initialTab;
        SuspendLayout();

        Text = "VELO Uploader";
        ClientSize = new Size(520, 560);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = C_BG;
        ForeColor = C_T1;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        // Load icon from embedded resource
        try
        {
            var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch { }

        // ── Header ──
        var header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = C_PANEL };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(C_ACCENT, 2);
            e.Graphics.DrawLine(pen, 0, 53, Width, 53);
        };
        Controls.Add(header);

        // Logo from embedded resource
        try
        {
            var logoStream = typeof(SettingsForm).Assembly.GetManifestResourceStream("logo.png");
            if (logoStream != null)
            {
                var img = Image.FromStream(logoStream);
                header.Controls.Add(new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, Bounds = new Rectangle(14, 7, 40, 40), BackColor = Color.Transparent });
            }
        }
        catch { }

        header.Controls.Add(MkLabel("VELO Uploader", 60, 6, new Font("Segoe UI", 13f, FontStyle.Bold), C_T1));
        header.Controls.Add(MkLabel("Auto-upload your game clips", 62, 30, new Font("Segoe UI", 8f), C_T3));

        // ── Custom tab bar ──
        var tabBar = new Panel { Location = new Point(0, 54), Size = new Size(520, 36), BackColor = C_PANEL };
        Controls.Add(tabBar);

        _tabBtns = new Button[4];
        string[] tabNames = ["General", "Filters", "Logs", "History"];
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = tabNames[i],
            Location = new Point(i * 120 + 16, 0),
            Size = new Size(110, 36),
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
        _pages = new Panel[4];
        for (int i = 0; i < 4; i++)
        {
            _pages[i] = new Panel
            {
                Location = new Point(0, 90),
                Size = new Size(520, 470),
                BackColor = C_BG,
                Visible = i == initialTab,
            };
            Controls.Add(_pages[i]);
        }

        // ═══════════════════════════════════════
        //  PAGE 0: GENERAL
        // ═══════════════════════════════════════
        var g = _pages[0];
        int y = 10, lx = 18, w = 482;

        Section(g, "CONNECTION", lx, y); y += 18;

        Lbl(g, "Server URL", lx, y);
        _urlBox = new DarkTextBox(settings.ServerUrl, "https://clips.example.com", lx, y + 16, w);
        g.Controls.Add(_urlBox);
        y += 50;

        Lbl(g, "API Token", lx, y);
        _tokenBox = new DarkTextBox(settings.ApiToken, "velo_...", lx, y + 16, w - 96);
        _tokenBox.UseSystemPasswordChar = true;
        g.Controls.Add(_tokenBox);
        _testBtn = MkBtn("Test", lx + w - 88, y + 16, 88, 28, C_ACCENT, C_ACCENT_H);
        _testBtn.Click += async (_, _) => await TestConnection();
        g.Controls.Add(_testBtn);
        y += 50;

        _statusLabel = new Label { Location = new Point(lx, y), Size = new Size(w, 14), ForeColor = C_T3, Font = new Font("Segoe UI", 7.5f) };
        g.Controls.Add(_statusLabel);
        y += 18;

        Section(g, "WATCH FOLDER", lx, y); y += 18;

        _watchBox = new DarkTextBox(settings.WatchFolder, @"D:\recordings", lx, y, w - 82);
        g.Controls.Add(_watchBox);
        var browseBtn = MkBtn("Browse", lx + w - 74, y, 74, 28, C_BTN, C_BTN_H);
        browseBtn.Click += (_, _) => { using var d = new FolderBrowserDialog { SelectedPath = _watchBox.Text }; if (d.ShowDialog() == DialogResult.OK) _watchBox.Text = d.SelectedPath; };
        g.Controls.Add(browseBtn);
        y += 34;

        _subfoldersBox = MkChk("Include subfolders", settings.WatchSubfolders, lx, y);
        g.Controls.Add(_subfoldersBox);
        y += 24;

        Section(g, "OPTIONS", lx, y); y += 18;

        _notifyBox = MkChk("Desktop notifications", settings.ShowNotifications, lx, y);
        g.Controls.Add(_notifyBox);
        _deleteBox = MkChk("Delete clip after upload", settings.DeleteAfterUpload, lx + 210, y);
        g.Controls.Add(_deleteBox);
        y += 24;

        _startupBox = MkChk("Start with Windows", StartupManager.IsRegistered(), lx, y);
        g.Controls.Add(_startupBox);
        _scanOnLaunchBox = MkChk("Upload existing clips on launch", settings.ScanOnLaunch, lx + 210, y);
        g.Controls.Add(_scanOnLaunchBox);
        y += 24;
        _localCompressBox = MkChk("Compress locally before upload (FFmpeg)", settings.LocalCompress, lx, y);
        g.Controls.Add(_localCompressBox);
        y += 24;
        _compressionHardFailBox = MkChk("Skip upload if compression fails", settings.StopOnCompressionFailure, lx, y);
        g.Controls.Add(_compressionHardFailBox);
        _soundBox = MkChk("Play success/failure sounds", settings.PlaySounds, lx + 210, y);
        g.Controls.Add(_soundBox);
        y += 24;
        Lbl(g, "Compression preset:", lx, y + 5);
        _presetBox = new DarkComboBox(lx + 110, y, 140, CompressionPreset.All, settings.CompressionPreset);
        g.Controls.Add(_presetBox);
        y += 34;
        Lbl(g, "Retries:", lx, y + 2);
        _retriesBox = new DarkNumeric(settings.MaxRetries, 1, 10, lx + 54, y - 2, 54);
        g.Controls.Add(_retriesBox);
        y += 34;

        var saveBtn = MkBtn("Save && Start Watching", lx + w - 190, y, 190, 38, C_ACCENT, C_ACCENT_H);
        saveBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        saveBtn.Click += (_, _) => SaveSettings();
        g.Controls.Add(saveBtn);

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

        SwitchTab(initialTab);
        ResumeLayout();
    }

    // ── Tab switching ──

    void SwitchTab(int idx)
    {
        _activeTab = idx;
        for (int i = 0; i < 4; i++)
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
        p.Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = C_ACCENT, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold) });
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

    // ── Logging ──

    void LoadExistingLogs() { foreach (var e in Logger.Entries) AppendLog(e); }

    void OnNewLog(LogEntry e)
    {
        if (IsDisposed) return;
        try { Invoke(() => AppendLog(e)); } catch { }
    }

    void AppendLog(LogEntry e)
    {
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

    // ── Actions ──

    void SaveSettings()
    {
        var url = _urlBox.Text.Trim();
        if (url.Length == 0) { Status("Server URL is required", true); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) { Status("Invalid URL", true); return; }
        if (_tokenBox.Text.Trim().Length == 0) { Status("API token is required", true); return; }

        _settings.ServerUrl = url;
        _settings.ApiToken = _tokenBox.Text.Trim();
        _settings.WatchFolder = _watchBox.Text.Trim();
        _settings.WatchSubfolders = _subfoldersBox.Checked;
        _settings.ShowNotifications = _notifyBox.Checked;
        _settings.DeleteAfterUpload = _deleteBox.Checked;
        _settings.MaxRetries = (int)_retriesBox.Inner.Value;
        _settings.ScanOnLaunch = _scanOnLaunchBox.Checked;
        _settings.LocalCompress = _localCompressBox.Checked;
        _settings.StopOnCompressionFailure = _compressionHardFailBox.Checked;
        _settings.PlaySounds = _soundBox.Checked;
        _settings.CompressionPreset = (_presetBox.Inner.SelectedItem?.ToString() ?? CompressionPreset.Balanced);
        _settings.Save();

        StartupManager.SetEnabled(_startupBox.Checked);

        Logger.Info("Settings saved.");
        Status("Saved!", false);
        Task.Delay(800).ContinueWith(_ => { if (!IsDisposed) Invoke(Close); });
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
        var url = _urlBox.Text.Trim().TrimEnd('/') + "/api/videos";
        var token = _tokenBox.Text.Trim();
        if (url.Length < 10 || token.Length == 0) { Status("Fill URL + token first", true); return; }

        _testBtn.Enabled = false;
        Status("Testing...", false);
        try
        {
            using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var r = await h.GetAsync(url);
            Status(r.IsSuccessStatusCode ? "Connected!" : $"HTTP {(int)r.StatusCode}", !r.IsSuccessStatusCode);
        }
        catch (Exception ex) { Status($"Failed: {ex.Message}", true); }
        finally { _testBtn.Enabled = true; }
    }

    void Status(string msg, bool err)
    {
        _statusLabel.ForeColor = err ? C_ERR : C_GREEN;
        _statusLabel.Text = msg;
    }

    void OnHistoryChanged()
    {
        if (IsDisposed) return;
        try { Invoke(LoadHistory); } catch { }
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
