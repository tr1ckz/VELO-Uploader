namespace VeloUploader.Editor;

using System.Drawing.Imaging;

/// <summary>
/// The sequence timeline: two video lanes with linked audio lanes, a time
/// ruler, track targeting badges, and drag interactions for move / trim /
/// slip / razor. The playhead and all callbacks work in sequence seconds
/// (trim callbacks report new source in/out times for the touched segment).
/// </summary>
internal sealed class SequenceTimelineView : Control
{
    private enum SegmentDragMode
    {
        None,
        Seek,
        Move,
        ResizeLeft,
        ResizeRight,
    }

    private const int TimelineLeft = 72;

    private static readonly Cursor RazorCursor = CreateRazorCursor();

    private readonly List<TimelineSegment> _segments = [];
    private readonly List<(Rectangle Rect, int Index)> _hitTargets = [];
    private int _selectedIndex = -1;
    private double _playheadSeconds;
    private double _zoom = 1;
    private SegmentDragMode _dragMode;
    private int _dragIndex = -1;
    private TimelineSegment? _dragOriginSegment;
    private int _previewTrack = 1;
    private double _previewDropSeconds = double.NaN;
    private Point _dragStartPoint;
    private bool _dragActivated;
    private TimelineTool _tool = TimelineTool.Select;
    private bool _snappingEnabled = true;
    private double _dragAnchorSequenceSeconds;
    private int _targetVideoTrack = 1;
    private int _targetAudioTrack = 1;
    private Rectangle _v1BadgeRect;
    private Rectangle _v2BadgeRect;
    private Rectangle _a1BadgeRect;
    private Rectangle _a2BadgeRect;
    private double _snapIndicatorSeconds = double.NaN;
    private string _snapIndicatorLabel = string.Empty;
    private readonly ContextMenuStrip _gapContextMenu = new();
    private double _gapContextSeconds;
    private int _gapContextTrack = 1;

    private Func<string, Image?>? _waveformProvider;
    private Func<string, IReadOnlyList<Image>>? _thumbnailProvider;

    public event Action<int>? SegmentClicked;
    public event Action<int, double, double>? SegmentTrimChanged;
    public event Action<int, double, int>? SegmentMoved;
    public event Action<int, double>? SegmentSplitRequested;
    public event Action<int, double>? GapDeleteRequested;
    public event Action<int, double>? SegmentSlipRequested;
    public event Action<int, int>? TrackTargetChanged;
    public event Action<double>? SeekRequested;
    public event Action<double>? ZoomDeltaRequested;

    public SequenceTimelineView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = EditorTheme.InsetBack;
        ForeColor = EditorTheme.TextPrimary;
        Cursor = Cursors.Hand;
        TabStop = true;

        _gapContextMenu.Renderer = new EditorStripRenderer();
        _gapContextMenu.BackColor = EditorTheme.PanelBack;
        _gapContextMenu.ForeColor = EditorTheme.TextPrimary;
        var rippleDeleteItem = new ToolStripMenuItem("Ripple Delete Gap") { ForeColor = EditorTheme.TextPrimary };
        rippleDeleteItem.Click += (_, _) => GapDeleteRequested?.Invoke(_gapContextTrack, _gapContextSeconds);
        _gapContextMenu.Items.Add(rippleDeleteItem);
    }

    public void SetSegments(IEnumerable<TimelineSegment> segments, int selectedIndex)
    {
        _segments.Clear();
        _segments.AddRange(segments);
        _selectedIndex = selectedIndex;
        Invalidate();
    }

    public void SetSelectedIndex(int index)
    {
        _selectedIndex = index;
        Invalidate();
    }

    /// <summary>Sets the playhead in sequence seconds.</summary>
    public void SetPlayhead(double sequenceSeconds)
    {
        _playheadSeconds = Math.Max(0, sequenceSeconds);
        Invalidate();
    }

    public void SetWaveformProvider(Func<string, Image?>? provider)
    {
        _waveformProvider = provider;
        Invalidate();
    }

    public void SetThumbnailProvider(Func<string, IReadOnlyList<Image>>? provider)
    {
        _thumbnailProvider = provider;
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

    public void SetTool(TimelineTool tool)
    {
        _tool = tool;
        Cursor = tool switch
        {
            TimelineTool.Razor => RazorCursor,
            TimelineTool.Slip => Cursors.VSplit,
            _ => Cursors.Hand,
        };
        Invalidate();
    }

    public void SetTargetTracks(int videoTrack, int audioTrack)
    {
        _targetVideoTrack = Math.Clamp(videoTrack, 1, 2);
        _targetAudioTrack = Math.Clamp(audioTrack, 1, 2);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Left)
        {
            if (_v1BadgeRect.Contains(e.Location) || _v2BadgeRect.Contains(e.Location))
            {
                _targetVideoTrack = _v2BadgeRect.Contains(e.Location) ? 2 : 1;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }

            if (_a1BadgeRect.Contains(e.Location) || _a2BadgeRect.Contains(e.Location))
            {
                _targetAudioTrack = _a2BadgeRect.Contains(e.Location) ? 2 : 1;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }
        }

        var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(e.Location));
        if (hit.Rect == Rectangle.Empty)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragMode = SegmentDragMode.Seek;
                UpdatePlayheadFromPoint(e.Location);
            }

            return;
        }

        _selectedIndex = hit.Index;
        _dragStartPoint = e.Location;
        _dragActivated = false;
        Invalidate();

        if (e.Button != MouseButtons.Left)
            return;

        if (_tool == TimelineTool.Razor)
        {
            var segment = _segments[hit.Index];
            var ratio = Math.Clamp((e.X - hit.Rect.Left) / (double)Math.Max(1, hit.Rect.Width), 0, 1);
            var splitSeconds = segment.StartSec + (segment.Duration * ratio);
            SegmentSplitRequested?.Invoke(hit.Index, splitSeconds);
            return;
        }

        const int handleWidth = 8;
        var leftHandle = new Rectangle(hit.Rect.Left - 4, hit.Rect.Top, handleWidth, hit.Rect.Height);
        var rightHandle = new Rectangle(hit.Rect.Right - 4, hit.Rect.Top, handleWidth, hit.Rect.Height);
        if (leftHandle.Contains(e.Location) || rightHandle.Contains(e.Location))
        {
            _dragMode = leftHandle.Contains(e.Location) ? SegmentDragMode.ResizeLeft : SegmentDragMode.ResizeRight;
            _dragIndex = hit.Index;
            _dragOriginSegment = _segments[hit.Index];
        }
        else
        {
            _dragMode = SegmentDragMode.Move;
            _dragIndex = hit.Index;
            _dragOriginSegment = _segments[hit.Index];
            _dragAnchorSequenceSeconds = _dragOriginSegment.SequenceStartSec + (_dragOriginSegment.Duration * Math.Clamp((e.X - hit.Rect.Left) / (double)Math.Max(1, hit.Rect.Width), 0, 1));
            _previewTrack = _segments[hit.Index].SafeTrack;
            _previewDropSeconds = _dragOriginSegment.SequenceStartSec;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragMode == SegmentDragMode.None)
        {
            UpdateCursor(e.Location);
            return;
        }

        if (_dragMode == SegmentDragMode.Seek)
        {
            UpdatePlayheadFromPoint(e.Location);
            return;
        }

        if (_dragOriginSegment == null || _dragIndex < 0 || _dragIndex >= _segments.Count)
            return;

        if (!_dragActivated && (Math.Abs(e.X - _dragStartPoint.X) > 4 || Math.Abs(e.Y - _dragStartPoint.Y) > 4))
            _dragActivated = true;

        var totalDuration = TotalDuration();
        var timelineWidth = GetTimelineWidth();
        var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
        var absoluteSeconds = visibleStart + (((e.X - TimelineLeft) / (double)Math.Max(1, timelineWidth)) * visibleDuration);
        absoluteSeconds = SnapToBoundary(absoluteSeconds, totalDuration, out var snapped);
        var localPosition = absoluteSeconds - _dragOriginSegment.SequenceStartSec;
        _snapIndicatorSeconds = snapped ? absoluteSeconds : double.NaN;
        _snapIndicatorLabel = snapped
            ? (_dragMode == SegmentDragMode.Move
                ? _tool switch { TimelineTool.Ripple => "RIPPLE", TimelineTool.Slip => "SLIP", _ => "OVR" }
                : (_tool == TimelineTool.Rolling ? "ROLL" : "TRIM"))
            : string.Empty;

        if (_dragMode == SegmentDragMode.ResizeLeft)
        {
            var newStart = Math.Clamp(_dragOriginSegment.StartSec + localPosition, 0, _dragOriginSegment.EndSec - 0.10);
            SegmentTrimChanged?.Invoke(_dragIndex, newStart, _dragOriginSegment.EndSec);
            return;
        }

        if (_dragMode == SegmentDragMode.ResizeRight)
        {
            var newEnd = Math.Max(_dragOriginSegment.StartSec + 0.10, _dragOriginSegment.StartSec + localPosition);
            SegmentTrimChanged?.Invoke(_dragIndex, _dragOriginSegment.StartSec, newEnd);
            return;
        }

        if (_dragMode == SegmentDragMode.Move && _tool == TimelineTool.Slip)
        {
            // Report the incremental delta and re-anchor, so repeated events don't compound.
            var slipDelta = EditorTime.SnapToFrame(absoluteSeconds - _dragAnchorSequenceSeconds);
            if (Math.Abs(slipDelta) > 0.0001)
            {
                _dragAnchorSequenceSeconds = absoluteSeconds;
                SegmentSlipRequested?.Invoke(_dragIndex, slipDelta);
            }

            return;
        }

        var grabOffset = _dragAnchorSequenceSeconds - _dragOriginSegment.SequenceStartSec;
        _previewDropSeconds = Math.Max(0, absoluteSeconds - grabOffset);
        _previewTrack = GetTrackForY(e.Y);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            ShowGapContextMenu(e.Location);
            return;
        }

        if (_dragMode == SegmentDragMode.Move && _dragIndex >= 0 && _tool != TimelineTool.Slip)
        {
            if (_dragActivated)
                SegmentMoved?.Invoke(_dragIndex, double.IsNaN(_previewDropSeconds) ? _segments[_dragIndex].SequenceStartSec : _previewDropSeconds, _previewTrack);
            else
                SegmentClicked?.Invoke(_dragIndex);
        }
        else if (_dragMode == SegmentDragMode.Seek)
            UpdatePlayheadFromPoint(e.Location);
        else if (_dragIndex >= 0 && !_dragActivated)
            SegmentClicked?.Invoke(_dragIndex);

        _dragMode = SegmentDragMode.None;
        _dragIndex = -1;
        _dragOriginSegment = null;
        _previewDropSeconds = double.NaN;
        _dragActivated = false;
        _snapIndicatorSeconds = double.NaN;
        _snapIndicatorLabel = string.Empty;
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
        _hitTargets.Clear();

        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        using var borderPen = new Pen(EditorTheme.Border);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        var totalDuration = TotalDuration();
        var timelineWidth = GetTimelineWidth();
        var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
        var rulerTop = 8;
        var laneTop = 30;
        var laneGap = 4;
        var laneAreaHeight = Math.Max(88, Height - laneTop - 10);
        var videoLaneHeight = Math.Clamp((int)Math.Round(laneAreaHeight * 0.32), 26, 46);
        var audioLaneHeight = Math.Clamp((laneAreaHeight - (videoLaneHeight * 2) - (laneGap * 3)) / 2, 12, 20);
        var v1 = new Rectangle(TimelineLeft, laneTop, timelineWidth, videoLaneHeight);
        var v2 = new Rectangle(TimelineLeft, v1.Bottom + laneGap, timelineWidth, videoLaneHeight);
        var a1 = new Rectangle(TimelineLeft, v2.Bottom + laneGap, timelineWidth, audioLaneHeight);
        var a2 = new Rectangle(TimelineLeft, a1.Bottom + laneGap, timelineWidth, audioLaneHeight);

        var rulerRect = new Rectangle(TimelineLeft, rulerTop, timelineWidth, 16);
        using var rulerBrush = new SolidBrush(EditorTheme.WindowBack);
        using var labelBadgeBrush = new SolidBrush(EditorTheme.PanelBack);
        using var labelBorderPen = new Pen(EditorTheme.Border);
        using var activeBadgeBrush = new SolidBrush(EditorTheme.Accent);
        e.Graphics.FillRectangle(rulerBrush, rulerRect);
        e.Graphics.DrawRectangle(borderPen, rulerRect);

        void DrawLaneHeader(string label, Rectangle lane, bool active, bool audio, ref Rectangle badgeRect)
        {
            var headerRect = new Rectangle(8, lane.Top, 56, lane.Height);
            using var headerBrush = new SolidBrush(active
                ? Color.FromArgb(60, EditorTheme.Accent)
                : (audio ? EditorTheme.InsetBack : EditorTheme.PanelBack));
            e.Graphics.FillRectangle(headerBrush, headerRect);
            e.Graphics.DrawRectangle(labelBorderPen, headerRect);
            TextRenderer.DrawText(e.Graphics, label, EditorTheme.TinyBoldFont, new Rectangle(headerRect.Left + 6, headerRect.Top + 1, 18, headerRect.Height - 2), EditorTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            badgeRect = new Rectangle(headerRect.Right - 28, headerRect.Top + Math.Max(1, (headerRect.Height - 14) / 2), 22, 14);
            e.Graphics.FillRectangle(active ? activeBadgeBrush : labelBadgeBrush, badgeRect);
            e.Graphics.DrawRectangle(labelBorderPen, badgeRect);
            TextRenderer.DrawText(e.Graphics, active ? "ON" : "--", EditorTheme.TinyBoldFont, badgeRect, active ? Color.White : EditorTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        DrawLaneHeader("V1", v1, _targetVideoTrack == 1, false, ref _v1BadgeRect);
        DrawLaneHeader("V2", v2, _targetVideoTrack == 2, false, ref _v2BadgeRect);
        DrawLaneHeader("A1", a1, _targetAudioTrack == 1, true, ref _a1BadgeRect);
        DrawLaneHeader("A2", a2, _targetAudioTrack == 2, true, ref _a2BadgeRect);

        using var videoRailBrush = new SolidBrush(EditorTheme.RailBack);
        using var audioRailBrush = new SolidBrush(Color.FromArgb(26, 26, 30));
        e.Graphics.FillRectangle(videoRailBrush, v1);
        e.Graphics.FillRectangle(videoRailBrush, v2);
        e.Graphics.FillRectangle(audioRailBrush, a1);
        e.Graphics.FillRectangle(audioRailBrush, a2);
        e.Graphics.DrawRectangle(borderPen, v1);
        e.Graphics.DrawRectangle(borderPen, v2);
        e.Graphics.DrawRectangle(borderPen, a1);
        e.Graphics.DrawRectangle(borderPen, a2);

        if (_segments.Count == 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                "Timeline is empty — mark IN/OUT on a source clip, then press , (insert) or . (overwrite).",
                EditorTheme.SmallFont,
                new Rectangle(v1.Left + 10, v1.Top, v1.Width - 20, v1.Height),
                EditorTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        using var minorPen = new Pen(Color.FromArgb(30, 30, 36));
        using var secondPen = new Pen(Color.FromArgb(42, 42, 48));
        using var majorPen = new Pen(Color.FromArgb(68, 68, 78));
        var tickStep = visibleDuration <= 12 ? 0.5d : 1d;
        var tickStart = Math.Floor(visibleStart / tickStep) * tickStep;
        var tickEnd = Math.Ceiling((visibleStart + visibleDuration) / tickStep) * tickStep;
        for (var tick = tickStart; tick <= tickEnd + 0.001d; tick += tickStep)
        {
            var ratio = (tick - visibleStart) / Math.Max(0.001, visibleDuration);
            if (ratio < 0 || ratio > 1)
                continue;

            var x = TimelineLeft + (int)Math.Round(ratio * timelineWidth);
            var isSecondTick = Math.Abs(tick % 1d) < 0.001d;
            var isMajor = Math.Abs(tick % 5d) < 0.001d;
            e.Graphics.DrawLine(isMajor ? majorPen : (isSecondTick ? secondPen : minorPen), x, isSecondTick ? rulerTop + 8 : rulerTop + 11, x, a2.Bottom);

            if (isSecondTick && (isMajor || visibleDuration <= 18))
            {
                TextRenderer.DrawText(e.Graphics, EditorTime.Format(tick), EditorTheme.TinyFont, new Point(Math.Max(TimelineLeft, x - 16), rulerTop - 1), EditorTheme.TextMuted);
            }
        }

        for (var index = 0; index < _segments.Count; index++)
        {
            var segment = _segments[index];
            if (segment.SequenceEndSec < visibleStart || segment.SequenceStartSec > visibleStart + visibleDuration)
                continue;

            var startRatio = (segment.SequenceStartSec - visibleStart) / visibleDuration;
            var endRatio = (segment.SequenceEndSec - visibleStart) / visibleDuration;
            var startX = TimelineLeft + (int)Math.Round(startRatio * timelineWidth);
            var endX = TimelineLeft + (int)Math.Round(endRatio * timelineWidth);
            var blockWidth = Math.Max(38, endX - startX);
            var videoLane = segment.SafeTrack == 2 ? v2 : v1;
            var audioLane = segment.SafeTrack == 2 ? a2 : a1;
            var rect = new Rectangle(Math.Max(videoLane.Left, startX), videoLane.Top + 2, Math.Min(blockWidth, videoLane.Right - Math.Max(videoLane.Left, startX) - 1), Math.Max(18, videoLane.Height - 4));
            var audioBlock = new Rectangle(rect.Left, audioLane.Top + 1, rect.Width, Math.Max(8, audioLane.Height - 2));

            var fill = index == _selectedIndex
                ? EditorTheme.ClipSelected
                : (segment.SafeTrack == 2 ? EditorTheme.ClipV2 : EditorTheme.ClipV1);
            using var shadowBrush = new SolidBrush(Color.FromArgb(46, 0, 0, 0));
            using var blockBrush = new System.Drawing.Drawing2D.LinearGradientBrush(rect, ControlPaint.Light(fill, 0.05f), fill, 90f);
            using var audioBrush = new SolidBrush(Color.FromArgb(Math.Max(0, fill.R - 24), Math.Max(0, fill.G - 24), Math.Max(0, fill.B - 24)));
            using var activePen = new Pen(index == _selectedIndex ? Color.FromArgb(221, 214, 254) : Color.FromArgb(120, 120, 150), index == _selectedIndex ? 2 : 1);
            using var handleBrush = new SolidBrush(Color.FromArgb(245, 245, 250));

            e.Graphics.FillRectangle(shadowBrush, new Rectangle(rect.Left + 2, rect.Top + 2, rect.Width, rect.Height));
            e.Graphics.FillRectangle(blockBrush, rect);
            if (_thumbnailProvider?.Invoke(segment.SourceFile) is { Count: > 0 } filmstripFrames)
                DrawFilmstrip(e.Graphics, filmstripFrames, rect);
            e.Graphics.FillRectangle(audioBrush, audioBlock);
            if (_waveformProvider?.Invoke(segment.SourceFile) is Image waveform)
                DrawImageWithOpacity(e.Graphics, waveform, audioBlock, 0.55f);
            e.Graphics.DrawRectangle(activePen, rect);
            e.Graphics.DrawRectangle(activePen, audioBlock);

            if (index == _selectedIndex)
            {
                var leftGrip = new Rectangle(rect.Left - 3, rect.Top + 2, 6, rect.Height - 4);
                var rightGrip = new Rectangle(rect.Right - 3, rect.Top + 2, 6, rect.Height - 4);
                e.Graphics.FillRectangle(handleBrush, leftGrip);
                e.Graphics.FillRectangle(handleBrush, rightGrip);
                e.Graphics.DrawRectangle(Pens.Black, leftGrip);
                e.Graphics.DrawRectangle(Pens.Black, rightGrip);
            }

            var label = Path.GetFileName(segment.SourceFile);
            var labelRect = new Rectangle(rect.Left + 4, rect.Top + 3, Math.Max(24, rect.Width - 8), 12);
            using var labelBackBrush = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            e.Graphics.FillRectangle(labelBackBrush, labelRect);
            TextRenderer.DrawText(e.Graphics, label, EditorTheme.TinyBoldFont, labelRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (!segment.SafeTransform.IsDefault)
            {
                var fxRect = new Rectangle(rect.Right - 20, rect.Top + 3, 16, 12);
                using var fxBrush = new SolidBrush(Color.FromArgb(200, EditorTheme.Marker));
                e.Graphics.FillRectangle(fxBrush, fxRect);
                TextRenderer.DrawText(e.Graphics, "fx", EditorTheme.TinyBoldFont, fxRect, Color.FromArgb(30, 30, 30), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            _hitTargets.Add((rect, index));
        }

        if (_dragMode == SegmentDragMode.Move && _dragActivated && _dragOriginSegment != null && !double.IsNaN(_previewDropSeconds))
        {
            var insertRatio = Math.Clamp((_previewDropSeconds - visibleStart) / Math.Max(0.001, visibleDuration), 0, 1);
            var insertX = TimelineLeft + (int)Math.Round(insertRatio * timelineWidth);
            var targetLane = _previewTrack == 2 ? v2 : v1;
            var targetAudioLane = _previewTrack == 2 ? a2 : a1;

            var ghostWidth = Math.Max(40, (int)Math.Round((_dragOriginSegment.Duration / Math.Max(0.001, visibleDuration)) * timelineWidth));
            ghostWidth = Math.Min(ghostWidth, Math.Max(40, targetLane.Width - 4));
            var ghostLeft = Math.Clamp(insertX + 2, targetLane.Left + 1, Math.Max(targetLane.Left + 1, targetLane.Right - ghostWidth - 1));
            var ghostRect = new Rectangle(ghostLeft, targetLane.Top + 4, ghostWidth, targetLane.Height - 8);
            var ghostAudioRect = new Rectangle(ghostLeft, targetAudioLane.Top + 2, ghostWidth, targetAudioLane.Height - 4);
            using var ghostBrush = new SolidBrush(Color.FromArgb(72, 235, 235, 240));
            using var ghostAudioBrush = new SolidBrush(Color.FromArgb(56, 180, 196, 214));
            using var ghostBorder = new Pen(Color.FromArgb(190, 248, 250, 252), 1)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            };
            e.Graphics.FillRectangle(ghostBrush, ghostRect);
            e.Graphics.FillRectangle(ghostAudioBrush, ghostAudioRect);
            e.Graphics.DrawRectangle(ghostBorder, ghostRect);
            e.Graphics.DrawRectangle(ghostBorder, ghostAudioRect);

            var indicatorColor = _tool == TimelineTool.Ripple ? EditorTheme.Marker : EditorTheme.Playhead;
            using var insertPen = new Pen(indicatorColor, 2);
            using var insertGlowPen = new Pen(Color.FromArgb(120, indicatorColor), 6);
            e.Graphics.DrawLine(insertGlowPen, insertX, targetLane.Top - 4, insertX, a2.Bottom + 4);
            e.Graphics.DrawLine(insertPen, insertX, targetLane.Top - 4, insertX, a2.Bottom + 4);
        }

        if (!double.IsNaN(_snapIndicatorSeconds))
        {
            var snapX = TimelineLeft + (int)Math.Round(((Math.Clamp(_snapIndicatorSeconds, visibleStart, visibleStart + visibleDuration) - visibleStart) / visibleDuration) * timelineWidth);
            using var snapPen = new Pen(EditorTheme.Snap, 2);
            using var snapGlowPen = new Pen(Color.FromArgb(120, EditorTheme.Snap), 6);
            using var snapBrush = new SolidBrush(EditorTheme.Snap);
            e.Graphics.DrawLine(snapGlowPen, snapX, rulerTop + 10, snapX, a2.Bottom + 6);
            e.Graphics.DrawLine(snapPen, snapX, rulerTop + 10, snapX, a2.Bottom + 6);
            if (!string.IsNullOrEmpty(_snapIndicatorLabel))
            {
                var snapRect = new Rectangle(Math.Max(TimelineLeft, snapX - 24), rulerTop - 10, 48, 14);
                e.Graphics.FillRectangle(snapBrush, snapRect);
                TextRenderer.DrawText(e.Graphics, _snapIndicatorLabel, EditorTheme.TinyBoldFont, snapRect, Color.FromArgb(15, 23, 42), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        var playheadX = TimelineLeft + (int)Math.Round(((Math.Clamp(_playheadSeconds, visibleStart, visibleStart + visibleDuration) - visibleStart) / visibleDuration) * timelineWidth);
        using var playheadPen = new Pen(EditorTheme.Playhead, 2);
        using var playheadBrush = new SolidBrush(EditorTheme.Playhead);
        e.Graphics.DrawLine(playheadPen, playheadX, rulerTop + 10, playheadX, a2.Bottom + 8);
        e.Graphics.FillPolygon(playheadBrush,
        [
            new Point(playheadX, rulerTop + 6),
            new Point(playheadX - 8, rulerTop - 4),
            new Point(playheadX + 8, rulerTop - 4),
        ]);
    }

    private void ShowGapContextMenu(Point location)
    {
        if (_segments.Count == 0)
            return;

        var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(location));
        if (hit.Rect != Rectangle.Empty || location.X < TimelineLeft || location.Y < 30)
            return;

        var totalDuration = TotalDuration();
        var timelineWidth = GetTimelineWidth();
        var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
        var ratio = Math.Clamp((location.X - TimelineLeft) / (double)Math.Max(1, timelineWidth), 0, 1);
        _gapContextSeconds = visibleStart + (ratio * visibleDuration);
        _gapContextTrack = GetTrackForY(location.Y);
        if (_gapContextMenu.Items[0] is ToolStripMenuItem item)
            item.Text = $"Ripple Delete Gap on V{_gapContextTrack}";
        _gapContextMenu.Show(this, location);
    }

    private void UpdateCursor(Point location)
    {
        if (_tool == TimelineTool.Razor)
        {
            Cursor = RazorCursor;
            return;
        }

        var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(location));
        if (hit.Rect == Rectangle.Empty)
        {
            Cursor = Cursors.Hand;
            return;
        }

        const int handleWidth = 8;
        var leftHandle = new Rectangle(hit.Rect.Left - 4, hit.Rect.Top, handleWidth, hit.Rect.Height);
        var rightHandle = new Rectangle(hit.Rect.Right - 4, hit.Rect.Top, handleWidth, hit.Rect.Height);
        Cursor = leftHandle.Contains(location) || rightHandle.Contains(location)
            ? Cursors.SizeWE
            : (_tool == TimelineTool.Slip ? Cursors.VSplit : Cursors.SizeAll);
    }

    private void UpdatePlayheadFromPoint(Point location)
    {
        var totalDuration = TotalDuration();
        var timelineWidth = GetTimelineWidth();
        var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
        var ratio = Math.Clamp((location.X - TimelineLeft) / (double)Math.Max(1, timelineWidth), 0, 1);
        _playheadSeconds = Math.Clamp(visibleStart + (ratio * visibleDuration), 0, totalDuration);
        SeekRequested?.Invoke(_playheadSeconds);
        Invalidate();
    }

    private double TotalDuration() => _segments.Count == 0 ? 0.1 : Math.Max(0.1, _segments.Max(s => s.SequenceEndSec));

    private int GetTimelineWidth() => Math.Max(120, Width - TimelineLeft - 12);

    private static void DrawImageWithOpacity(Graphics graphics, Image image, Rectangle destination, float opacity)
    {
        if (destination.Width <= 1 || destination.Height <= 1)
            return;

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    private static void DrawFilmstrip(Graphics graphics, IReadOnlyList<Image> frames, Rectangle rect)
    {
        if (frames.Count == 0 || rect.Width <= 2 || rect.Height <= 2)
            return;

        var thumbWidth = Math.Max(28, rect.Height);
        var x = rect.Left;
        var frameIndex = 0;
        while (x < rect.Right)
        {
            var width = Math.Min(thumbWidth, rect.Right - x);
            var frameRect = new Rectangle(x, rect.Top, width, rect.Height);
            graphics.DrawImage(frames[frameIndex % frames.Count], frameRect);
            x += width;
            frameIndex++;
        }
    }

    private static Cursor CreateRazorCursor()
    {
        try
        {
            using var bitmap = new Bitmap(32, 32);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            TextRenderer.DrawText(graphics, "✂", new Font("Segoe UI Emoji", 16f, FontStyle.Bold), new Rectangle(0, 0, 28, 28), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var iconHandle = bitmap.GetHicon();
            return new Cursor(iconHandle);
        }
        catch
        {
            return Cursors.Cross;
        }
    }

    private (double Start, double Duration) GetVisibleRange(double totalDuration)
    {
        var visibleDuration = Math.Min(totalDuration, Math.Max(3, totalDuration / Math.Max(1, _zoom)));
        var start = Math.Clamp(_playheadSeconds - (visibleDuration / 2), 0, Math.Max(0, totalDuration - visibleDuration));
        return (start, Math.Max(0.001, visibleDuration));
    }

    private double SnapToBoundary(double absoluteSeconds, double totalDuration, out bool snapped)
    {
        if (!_snappingEnabled)
        {
            snapped = false;
            return EditorTime.SnapToFrame(absoluteSeconds);
        }

        var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
        var threshold = Math.Max(0.02, visibleDuration * 0.01);
        var snapPoints = new List<double> { 0, totalDuration, _playheadSeconds };
        foreach (var segment in _segments)
        {
            snapPoints.Add(segment.SequenceStartSec);
            snapPoints.Add(segment.SequenceEndSec);
        }

        for (var tick = Math.Ceiling(visibleStart); tick <= visibleStart + visibleDuration; tick += 1d)
            snapPoints.Add(tick);

        var nearest = snapPoints.OrderBy(value => Math.Abs(value - absoluteSeconds)).FirstOrDefault();
        snapped = Math.Abs(nearest - absoluteSeconds) <= threshold;
        return EditorTime.SnapToFrame(snapped ? nearest : absoluteSeconds);
    }

    private int GetTrackForY(int y)
    {
        var midpoint = Math.Max(60, Height / 2);
        return y <= midpoint ? 1 : 2;
    }
}
