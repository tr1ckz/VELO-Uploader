namespace VeloUploader;

/// <summary>
/// A custom dark-themed toast notification that slides in from the bottom-right
/// and auto-dismisses. Much nicer than the default Windows balloon tips.
/// </summary>
public class ToastNotification : Form
{
    static readonly Color C_BG = Color.FromArgb(22, 22, 28);
    static readonly Color C_BORDER = Color.FromArgb(124, 58, 237);
    static readonly Color C_TITLE = Color.FromArgb(240, 240, 245);
    static readonly Color C_BODY = Color.FromArgb(170, 170, 180);
    static readonly Color C_SUBTITLE = Color.FromArgb(124, 58, 237);

    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly System.Windows.Forms.Timer _closeTimer;
    private double _opacity = 0;
    private bool _closing;
    private readonly string? _clipboardText;

    public ToastNotification(string title, string body, string? subtitle = null, string? copyToClipboard = null, int durationMs = 4000)
    {
        _clipboardText = copyToClipboard;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = C_BG;
        Size = new Size(320, subtitle != null ? 90 : 72);
        Opacity = 0;
        DoubleBuffered = true;
        Cursor = copyToClipboard != null ? Cursors.Hand : Cursors.Default;

        // Position in bottom-right corner above taskbar
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(workArea.Right - Width - 16, workArea.Bottom - Height - 16);

        // Draw the toast
        Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Border
            using var borderPen = new Pen(C_BORDER, 1);
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            // Left accent strip
            using var accentBrush = new SolidBrush(C_BORDER);
            g.FillRectangle(accentBrush, 0, 0, 4, Height);

            // Icon area (play button triangle)
            var iconX = 16;
            var iconY = Height / 2 - 12;
            using var iconBrush = new SolidBrush(C_BORDER);
            g.FillPolygon(iconBrush, new Point[] {
                new(iconX, iconY),
                new(iconX + 20, iconY + 12),
                new(iconX, iconY + 24),
            });

            // Title
            var textX = 46;
            using var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(C_TITLE);
            g.DrawString(title, titleFont, titleBrush, textX, 10);

            // Body
            using var bodyFont = new Font("Segoe UI", 8.5f);
            using var bodyBrush = new SolidBrush(C_BODY);
            var bodyY = 30;
            g.DrawString(body, bodyFont, bodyBrush, new RectangleF(textX, bodyY, Width - textX - 12, 36));

            // Subtitle (e.g. "Click to copy link")
            if (subtitle != null)
            {
                using var subFont = new Font("Segoe UI", 7.5f);
                using var subBrush = new SolidBrush(C_SUBTITLE);
                g.DrawString(subtitle, subFont, subBrush, textX, Height - 22);
            }
        };

        // Click to copy URL
        Click += (_, _) =>
        {
            if (_clipboardText != null)
            {
                try { Clipboard.SetText(_clipboardText); } catch { }
            }
            StartFadeOut();
        };

        // Fade in
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _fadeTimer.Tick += (_, _) =>
        {
            if (!_closing)
            {
                _opacity = Math.Min(1.0, _opacity + 0.08);
                Opacity = _opacity;
                if (_opacity >= 1.0) _fadeTimer.Stop();
            }
            else
            {
                _opacity = Math.Max(0, _opacity - 0.06);
                Opacity = _opacity;
                if (_opacity <= 0) { _fadeTimer.Stop(); Close(); }
            }
        };

        // Auto-close after duration
        _closeTimer = new System.Windows.Forms.Timer { Interval = durationMs };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            StartFadeOut();
        };

        Shown += (_, _) =>
        {
            _fadeTimer.Start();
            _closeTimer.Start();
        };
    }

    void StartFadeOut()
    {
        _closing = true;
        _closeTimer.Stop();
        _fadeTimer.Start();
    }

    // Prevent stealing focus
    protected override bool ShowWithoutActivation => true;

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>
    /// Show a toast notification on the UI thread. Safe to call from any thread.
    /// </summary>
    public static void Show(string title, string body, string? subtitle = null, string? copyToClipboard = null, int durationMs = 4000)
    {
        // Must run on a STA thread with a message pump
        if (Application.OpenForms.Count > 0)
        {
            var form = Application.OpenForms[0];
            if (form != null && !form.IsDisposed)
            {
                form.Invoke(() =>
                {
                    var toast = new ToastNotification(title, body, subtitle, copyToClipboard, durationMs);
                    toast.Show();
                });
                return;
            }
        }

        // Fallback: create on a new STA thread
        var thread = new Thread(() =>
        {
            var toast = new ToastNotification(title, body, subtitle, copyToClipboard, durationMs);
            toast.Show();
            Application.Run(toast);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }
}
