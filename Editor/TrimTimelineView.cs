namespace VeloUploader.Editor;

/// <summary>
/// Source trim strip: filmstrip + waveform for the loaded clip with draggable
/// IN/OUT handles, markers, snapping, and a playhead. All times are in source
/// clip seconds.
/// </summary>
internal sealed class TrimTimelineView : Control
{
    private enum DragMode
    {
        None,
        Seek,
        Start,
        End,
        Range,
    }

    private double _duration;
    private double _rangeStart;
    private double _rangeEnd;
    private double _playhead;
    private double _zoom = 1;
    private bool _snappingEnabled = true;
    private string _clipLabel = "No clip loaded";
    private readonly List<double> _markers = [];
    private readonly List<Image> _thumbnails = [];
    private Image? _waveformImage;
    private DragMode _dragMode;
    private double _dragOffsetSeconds;
    private double _snapIndicatorSeconds = double.NaN;

    public event Action<double>? SeekRequested;
    public event Action<double, double>? RangeChanged;
    public event Action<double>? ZoomDeltaRequested;

    public TrimTimelineView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = EditorTheme.InsetBack;
        ForeColor = EditorTheme.TextPrimary;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public void SetTimeline(double duration, double start, double end, double playhead, string? clipLabel = null)
    {
        _duration = Math.Max(0, duration);
        _rangeStart = Math.Clamp(start, 0, Math.Max(_duration, 0));
        _rangeEnd = Math.Clamp(Math.Max(end, _rangeStart), _rangeStart, Math.Max(_duration, _rangeStart));
        _playhead = Math.Clamp(playhead, 0, Math.Max(_duration, 0));
        if (!string.IsNullOrWhiteSpace(clipLabel))
            _clipLabel = clipLabel;
        Invalidate();
    }

    public void SetPlayhead(double playhead)
    {
        _playhead = Math.Clamp(playhead, 0, Math.Max(_duration, 0));
        Invalidate();
    }

    public void SetMarkers(IEnumerable<double> markers)
    {
        _markers.Clear();
        _markers.AddRange(markers.OrderBy(value => value));
        Invalidate();
    }

    public void SetThumbnails(IEnumerable<Image> thumbnails)
    {
        _thumbnails.Clear();
        _thumbnails.AddRange(thumbnails);
        Invalidate();
    }

    public void SetWaveform(Image? waveform)
    {
        _waveformImage = waveform;
        Invalidate();
    }

    public void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 1, 6);
        Invalidate();
    }

    public void SetSnappingEnabled(bool enabled)
    {
        _snappingEnabled = enabled;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_duration <= 0)
            return;

        var rail = GetRailRect();
        var startX = SecondsToX(_rangeStart, rail);
        var endX = SecondsToX(_rangeEnd, rail);
        var startHandle = new Rectangle(startX - 6, rail.Top - 4, 12, rail.Height + 8);
        var endHandle = new Rectangle(endX - 6, rail.Top - 4, 12, rail.Height + 8);
        var selectedRange = Rectangle.FromLTRB(startX, rail.Top, endX, rail.Bottom);

        if (startHandle.Contains(e.Location))
            _dragMode = DragMode.Start;
        else if (endHandle.Contains(e.Location))
            _dragMode = DragMode.End;
        else if (selectedRange.Contains(e.Location))
        {
            _dragMode = DragMode.Range;
            _dragOffsetSeconds = XToSeconds(e.X, rail) - _rangeStart;
        }
        else
            _dragMode = DragMode.Seek;

        HandleDrag(e.Location);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMode == DragMode.None)
        {
            UpdateCursor(e.Location);
            return;
        }

        HandleDrag(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode != DragMode.None)
            HandleDrag(e.Location);
        _dragMode = DragMode.None;
        _snapIndicatorSeconds = double.NaN;
        UpdateCursor(e.Location);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Alt) == Keys.Alt)
            ZoomDeltaRequested?.Invoke(e.Delta > 0 ? 0.25 : -0.25);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        using var borderPen = new Pen(EditorTheme.Border);
        using var railBrush = new SolidBrush(EditorTheme.RailBack);
        using var clipBrush = new SolidBrush(Color.FromArgb(36, EditorTheme.Accent));
        using var wasteBrush = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var playheadPen = new Pen(EditorTheme.Playhead, 1);

        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        var rail = GetRailRect();

        TextRenderer.DrawText(e.Graphics, _clipLabel, EditorTheme.SmallFont, new Rectangle(10, 6, Width - 220, 16), EditorTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        if (_duration <= 0)
        {
            TextRenderer.DrawText(e.Graphics, "Load a clip from the media bin to trim it here.", EditorTheme.SmallFont, rail, EditorTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        var summary = $"IN {EditorTime.Format(_rangeStart)}   OUT {EditorTime.Format(_rangeEnd)}   LEN {EditorTime.Format(Math.Max(0, _rangeEnd - _rangeStart))}";
        TextRenderer.DrawText(e.Graphics, summary, EditorTheme.TinyFont, new Rectangle(Width - 300, 6, 290, 16), EditorTheme.TextSecondary, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        e.Graphics.FillRectangle(railBrush, rail);
        e.Graphics.DrawRectangle(borderPen, rail);

        var (visibleStart, visibleDuration) = GetVisibleRange();
        using var tickPen = new Pen(EditorTheme.BorderSoft);
        for (var tick = 0; tick <= 6; tick++)
        {
            var tickX = rail.Left + (int)Math.Round((tick / 6d) * rail.Width);
            e.Graphics.DrawLine(tickPen, tickX, rail.Top + 1, tickX, rail.Bottom - 1);
        }

        if (_thumbnails.Count > 0)
        {
            for (var i = 0; i < _thumbnails.Count; i++)
            {
                var thumbStart = (_duration * i) / Math.Max(1, _thumbnails.Count);
                var thumbEnd = (_duration * (i + 1)) / Math.Max(1, _thumbnails.Count);
                if (thumbEnd < visibleStart || thumbStart > visibleStart + visibleDuration)
                    continue;

                var thumbLeft = SecondsToX(thumbStart, rail);
                var thumbRight = SecondsToX(thumbEnd, rail);
                var thumbRect = new Rectangle(Math.Min(thumbLeft, thumbRight), rail.Top + 2, Math.Max(18, Math.Abs(thumbRight - thumbLeft)), Math.Max(14, (rail.Height / 2) - 4));
                if (thumbRect.Width > 2)
                    e.Graphics.DrawImage(_thumbnails[i], thumbRect);
            }
        }

        if (_waveformImage != null && _duration > 0)
        {
            var sourceX = (int)Math.Round((visibleStart / _duration) * _waveformImage.Width);
            var sourceWidth = Math.Max(1, (int)Math.Round((visibleDuration / _duration) * _waveformImage.Width));
            var waveformRect = new Rectangle(rail.Left + 1, rail.Top + (rail.Height / 2), rail.Width - 2, Math.Max(10, (rail.Height / 2) - 2));
            e.Graphics.DrawImage(_waveformImage, waveformRect, sourceX, 0, Math.Min(sourceWidth, _waveformImage.Width - sourceX), _waveformImage.Height, GraphicsUnit.Pixel);
        }

        var rangeLeft = SecondsToX(_rangeStart, rail);
        var rangeRight = SecondsToX(_rangeEnd, rail);
        var selectedRect = Rectangle.FromLTRB(Math.Min(rangeLeft, rangeRight), rail.Top + 1, Math.Max(rangeLeft, rangeRight), rail.Bottom - 1);
        e.Graphics.FillRectangle(clipBrush, selectedRect);
        if (selectedRect.Left > rail.Left + 1)
            e.Graphics.FillRectangle(wasteBrush, new Rectangle(rail.Left + 1, rail.Top + 1, selectedRect.Left - rail.Left - 1, rail.Height - 2));
        if (selectedRect.Right < rail.Right - 1)
            e.Graphics.FillRectangle(wasteBrush, new Rectangle(selectedRect.Right, rail.Top + 1, rail.Right - selectedRect.Right - 1, rail.Height - 2));
        using var selectedBorder = new Pen(EditorTheme.Accent, 1);
        e.Graphics.DrawRectangle(selectedBorder, selectedRect);

        if (!double.IsNaN(_snapIndicatorSeconds))
        {
            var snapX = SecondsToX(_snapIndicatorSeconds, rail);
            using var snapPen = new Pen(EditorTheme.Snap, 2);
            e.Graphics.DrawLine(snapPen, snapX, rail.Top - 6, snapX, rail.Bottom + 6);
        }

        using var markerPen = new Pen(EditorTheme.Marker, 2);
        using var markerBrush = new SolidBrush(EditorTheme.Marker);
        foreach (var marker in _markers)
        {
            var markerX = SecondsToX(marker, rail);
            e.Graphics.DrawLine(markerPen, markerX, rail.Top - 2, markerX, rail.Bottom + 2);
            e.Graphics.FillPolygon(markerBrush,
            [
                new Point(markerX, rail.Top - 8),
                new Point(markerX - 4, rail.Top - 2),
                new Point(markerX + 4, rail.Top - 2),
            ]);
        }

        var startHandle = new Rectangle(selectedRect.Left - 6, rail.Top - 4, 12, rail.Height + 8);
        var endHandle = new Rectangle(selectedRect.Right - 6, rail.Top - 4, 12, rail.Height + 8);
        DrawTrimHandle(e.Graphics, startHandle, "I", EditorTheme.Accent);
        DrawTrimHandle(e.Graphics, endHandle, "O", EditorTheme.ClipSelected);

        var playheadX = SecondsToX(_playhead, rail);
        e.Graphics.DrawLine(playheadPen, playheadX, rail.Top - 8, playheadX, Height - 4);
    }

    private void HandleDrag(Point location)
    {
        var rail = GetRailRect();
        var seconds = SnapTime(XToSeconds(location.X, rail));

        switch (_dragMode)
        {
            case DragMode.Start:
                _rangeStart = Math.Clamp(seconds, 0, _rangeEnd);
                RangeChanged?.Invoke(_rangeStart, _rangeEnd);
                break;
            case DragMode.End:
                _rangeEnd = Math.Clamp(seconds, _rangeStart, _duration);
                RangeChanged?.Invoke(_rangeStart, _rangeEnd);
                break;
            case DragMode.Range:
                var length = Math.Max(0.01, _rangeEnd - _rangeStart);
                var start = Math.Clamp(seconds - _dragOffsetSeconds, 0, Math.Max(0, _duration - length));
                _rangeStart = start;
                _rangeEnd = start + length;
                RangeChanged?.Invoke(_rangeStart, _rangeEnd);
                break;
            default:
                _playhead = seconds;
                SeekRequested?.Invoke(seconds);
                break;
        }

        Invalidate();
    }

    private void UpdateCursor(Point location)
    {
        var rail = GetRailRect();
        var startX = SecondsToX(_rangeStart, rail);
        var endX = SecondsToX(_rangeEnd, rail);
        var startHandle = new Rectangle(startX - 6, rail.Top - 4, 12, rail.Height + 8);
        var endHandle = new Rectangle(endX - 6, rail.Top - 4, 12, rail.Height + 8);
        var selectedRange = Rectangle.FromLTRB(startX, rail.Top, endX, rail.Bottom);

        Cursor = startHandle.Contains(location) || endHandle.Contains(location)
            ? Cursors.SizeWE
            : selectedRange.Contains(location)
                ? Cursors.SizeAll
                : Cursors.Hand;
    }

    private Rectangle GetRailRect()
    {
        var railHeight = Math.Clamp(Height - 40, 32, 60);
        return new Rectangle(10, 26, Math.Max(120, Width - 20), railHeight);
    }

    private (double Start, double Duration) GetVisibleRange()
    {
        if (_duration <= 0)
            return (0, 1);

        var visibleDuration = Math.Min(_duration, Math.Max(2, _duration / Math.Max(1, _zoom)));
        var start = Math.Clamp(_playhead - (visibleDuration / 2), 0, Math.Max(0, _duration - visibleDuration));
        return (start, Math.Max(0.001, visibleDuration));
    }

    private double SnapTime(double seconds)
    {
        if (_duration <= 0)
            return 0;

        if (!_snappingEnabled)
        {
            _snapIndicatorSeconds = double.NaN;
            return EditorTime.SnapToFrame(seconds);
        }

        var (visibleStart, visibleDuration) = GetVisibleRange();
        var snapCandidates = new List<double> { 0, _duration, _playhead, _rangeStart, _rangeEnd };
        snapCandidates.AddRange(_markers);
        for (var tick = Math.Ceiling(visibleStart); tick <= visibleStart + visibleDuration; tick += 1d)
            snapCandidates.Add(tick);

        var threshold = Math.Max(0.02, visibleDuration * 0.01);
        var nearest = snapCandidates.OrderBy(value => Math.Abs(value - seconds)).FirstOrDefault();
        var snapped = Math.Abs(nearest - seconds) <= threshold;
        _snapIndicatorSeconds = snapped ? nearest : double.NaN;
        return EditorTime.SnapToFrame(snapped ? nearest : seconds);
    }

    private int SecondsToX(double seconds, Rectangle rail)
    {
        if (_duration <= 0)
            return rail.Left;
        var (visibleStart, visibleDuration) = GetVisibleRange();
        var ratio = Math.Clamp((seconds - visibleStart) / visibleDuration, 0, 1);
        return rail.Left + (int)Math.Round(ratio * rail.Width);
    }

    private double XToSeconds(int x, Rectangle rail)
    {
        var (visibleStart, visibleDuration) = GetVisibleRange();
        var ratio = Math.Clamp((x - rail.Left) / (double)Math.Max(1, rail.Width), 0, 1);
        return visibleStart + (ratio * visibleDuration);
    }

    private static void DrawTrimHandle(Graphics graphics, Rectangle rect, string label, Color accent)
    {
        using var handleBrush = new SolidBrush(Color.FromArgb(245, 248, 250));
        using var accentBrush = new SolidBrush(accent);
        using var borderPen = new Pen(Color.FromArgb(30, 41, 59));
        graphics.FillRectangle(handleBrush, rect);
        graphics.DrawRectangle(borderPen, rect);
        graphics.FillRectangle(accentBrush, new Rectangle(rect.Left, rect.Top, 3, rect.Height));

        for (var grip = 0; grip < 3; grip++)
        {
            var gripX = rect.Left + 4 + (grip * 2);
            graphics.DrawLine(borderPen, gripX, rect.Top + 5, gripX, rect.Bottom - 5);
        }

        var labelRect = new Rectangle(rect.Left - 1, rect.Top - 16, rect.Width + 2, 12);
        TextRenderer.DrawText(graphics, label, EditorTheme.TinyBoldFont, labelRect, accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
