namespace VeloUploader.Editor;

using System.Runtime.InteropServices;

/// <summary>
/// Central palette and control factory for the editor so every panel, button,
/// and custom-drawn surface shares one look.
/// </summary>
internal static class EditorTheme
{
    // Surfaces (darkest → lightest).
    public static readonly Color WindowBack = Color.FromArgb(15, 15, 17);
    public static readonly Color PanelBack = Color.FromArgb(22, 22, 26);
    public static readonly Color InsetBack = Color.FromArgb(17, 17, 20);
    public static readonly Color RailBack = Color.FromArgb(30, 30, 35);
    public static readonly Color FieldBack = Color.FromArgb(28, 28, 33);

    // Lines.
    public static readonly Color Border = Color.FromArgb(48, 48, 56);
    public static readonly Color BorderSoft = Color.FromArgb(38, 38, 45);

    // Text.
    public static readonly Color TextPrimary = Color.FromArgb(235, 235, 240);
    public static readonly Color TextSecondary = Color.FromArgb(160, 160, 172);
    public static readonly Color TextMuted = Color.FromArgb(110, 110, 124);

    // Accents.
    public static readonly Color Accent = Color.FromArgb(99, 102, 241);        // indigo — selection, primary actions
    public static readonly Color AccentSoft = Color.FromArgb(70, 99, 102, 241);
    public static readonly Color ClipV1 = Color.FromArgb(45, 90, 160);
    public static readonly Color ClipV2 = Color.FromArgb(22, 128, 148);
    public static readonly Color ClipSelected = Color.FromArgb(124, 90, 220);
    public static readonly Color Playhead = Color.FromArgb(240, 90, 90);
    public static readonly Color Marker = Color.FromArgb(240, 185, 60);
    public static readonly Color Snap = Color.FromArgb(52, 200, 220);
    public static readonly Color Waveform = Color.FromArgb(125, 211, 252);

    public static readonly Font BaseFont = new("Segoe UI", 9f);
    public static readonly Font SmallFont = new("Segoe UI", 8f);
    public static readonly Font SmallBoldFont = new("Segoe UI", 8f, FontStyle.Bold);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 8.5f);
    public static readonly Font MonoFont = new("Consolas", 9f);
    public static readonly Font TinyFont = new("Segoe UI", 6.5f);
    public static readonly Font TinyBoldFont = new("Segoe UI", 6.5f, FontStyle.Bold);

    public static Label SectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        Font = SectionFont,
        ForeColor = TextMuted,
        Margin = new Padding(0, 8, 0, 2),
    };

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = SmallFont,
        ForeColor = TextSecondary,
        Margin = new Padding(0, 4, 0, 1),
    };

    public static Label HintLabel(string text, int width) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(width, 30),
        Font = SmallFont,
        ForeColor = TextMuted,
    };

    public static TextBox TextField(string text = "") => new()
    {
        Text = text,
        BackColor = FieldBack,
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = BaseFont,
    };

    public static NumericUpDown NumericField(decimal value, decimal min, decimal max, int decimals = 2)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = decimals > 0 ? 0.25M : 1M,
            BackColor = FieldBack,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = BaseFont,
        };
        numeric.Value = Math.Clamp(value, min, max);
        return numeric;
    }

    public static Button ToolButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = FieldBack,
            ForeColor = TextPrimary,
            Font = SmallFont,
            Height = 26,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 38, 46);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(50, 50, 60);
        button.Click += onClick;
        return button;
    }

    public static Button PrimaryButton(string text, EventHandler onClick)
    {
        var button = ToolButton(text, onClick);
        button.BackColor = Color.FromArgb(48, 50, 92);
        button.ForeColor = Color.White;
        button.Font = SmallBoldFont;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 60, 110);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(70, 72, 128);
        return button;
    }

    /// <summary>Restyles a toggle-style button to reflect an active/inactive state.</summary>
    public static void SetToggleState(Button button, bool active)
    {
        button.BackColor = active ? Color.FromArgb(58, 60, 110) : FieldBack;
        button.ForeColor = active ? Color.White : TextPrimary;
        button.FlatAppearance.BorderColor = active ? Accent : Border;
    }

    /// <summary>Applies the dark Explorer scrollbar theme once the handle exists.</summary>
    public static void ApplyDarkScrollbars(Control control)
    {
        void Apply()
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

        control.HandleCreated += (_, _) => Apply();
        Apply();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);
}

/// <summary>Dark renderer for menu / tool / status strips.</summary>
internal sealed class EditorStripRenderer : ToolStripProfessionalRenderer
{
    public EditorStripRenderer() : base(new EditorStripColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = EditorTheme.TextSecondary;
        base.OnRenderArrow(e);
    }

    private sealed class EditorStripColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => EditorTheme.PanelBack;
        public override Color MenuStripGradientEnd => EditorTheme.PanelBack;
        public override Color ToolStripGradientBegin => EditorTheme.PanelBack;
        public override Color ToolStripGradientMiddle => EditorTheme.PanelBack;
        public override Color ToolStripGradientEnd => EditorTheme.PanelBack;
        public override Color StatusStripGradientBegin => EditorTheme.PanelBack;
        public override Color StatusStripGradientEnd => EditorTheme.PanelBack;
        public override Color ToolStripDropDownBackground => EditorTheme.PanelBack;
        public override Color ImageMarginGradientBegin => EditorTheme.PanelBack;
        public override Color ImageMarginGradientMiddle => EditorTheme.PanelBack;
        public override Color ImageMarginGradientEnd => EditorTheme.PanelBack;
        public override Color MenuItemSelected => Color.FromArgb(45, 45, 58);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(45, 45, 58);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(45, 45, 58);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(52, 52, 66);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(52, 52, 66);
        public override Color MenuItemBorder => EditorTheme.Border;
        public override Color MenuBorder => EditorTheme.Border;
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(45, 45, 58);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(45, 45, 58);
        public override Color ButtonSelectedBorder => EditorTheme.Border;
        public override Color ButtonPressedGradientBegin => Color.FromArgb(52, 52, 66);
        public override Color ButtonPressedGradientEnd => Color.FromArgb(52, 52, 66);
        public override Color ButtonCheckedGradientBegin => Color.FromArgb(58, 60, 110);
        public override Color ButtonCheckedGradientEnd => Color.FromArgb(58, 60, 110);
        public override Color SeparatorDark => EditorTheme.Border;
        public override Color SeparatorLight => EditorTheme.PanelBack;
        public override Color GripDark => EditorTheme.Border;
        public override Color GripLight => EditorTheme.PanelBack;
    }
}
