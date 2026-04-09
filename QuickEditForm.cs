namespace VeloUploader;

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms.Integration;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;

public sealed class QuickEditForm : Form
{
    private readonly ListBox _filesList;
    private readonly FlowLayoutPanel _mediaThumbStrip;
    private readonly ElementHost _playerHost;
    private readonly WpfControls.MediaElement _mediaElement;
    private readonly EditorPreviewBox _sourcePreview;
    private readonly PictureBox _outputPreview;
    private readonly TrimTimelineView _trimTimelineView;
    private readonly TrackBar _timelineBar;
    private readonly Button _playPauseButton;
    private readonly Button _jumpBackButton;
    private readonly Button _jumpForwardButton;
    private readonly Button _selectToolButton;
    private readonly Button _rippleToolButton;
    private readonly Button _rollingToolButton;
    private readonly Button _slipToolButton;
    private readonly Button _razorToolButton;
    private readonly Label _timelineModeLabel;
    private readonly Label _previewTimeLabel;
    private readonly Label _videoInfoLabel;
    private readonly Label _playerStatusLabel;
    private readonly NumericUpDown _startBox;
    private readonly NumericUpDown _endBox;
    private readonly NumericUpDown _cropXBox;
    private readonly NumericUpDown _cropYBox;
    private readonly NumericUpDown _cropWBox;
    private readonly NumericUpDown _cropHBox;
    private readonly NumericUpDown _positionXBox;
    private readonly NumericUpDown _positionYBox;
    private readonly NumericUpDown _scaleBox;
    private readonly NumericUpDown _opacityBox;
    private readonly Button _positionKeyframeButton;
    private readonly Button _scaleKeyframeButton;
    private readonly Button _opacityKeyframeButton;
    private readonly Label _cropInfoLabel;
    private readonly TextBox _projectSearchBox;
    private readonly TextBox _outputNameBox;
    private readonly TextBox _outputFolderBox;
    private readonly ListBox _sequenceList;
    private readonly SequenceTimelineView _sequenceTimelineView;
    private readonly Label _sequenceSummaryLabel;
    private readonly Label _sequenceHintLabel;
    private readonly ListBox _markerList;
    private readonly Label _markerHintLabel;
    private readonly Button _addMarkerButton;
    private readonly Button _splitPlayheadButton;
    private readonly FlowLayoutPanel _workspaceBadgeStrip;
    private readonly Label _inspectorHeaderLabel;
    private readonly Label _inspectorHintLabel;
    private readonly Label _statusLabel;
    private readonly Button _trimButton;
    private readonly Button _cropButton;
    private readonly Button _mergeButton;
    private readonly Button _addCutButton;
    private readonly Button _overwriteCutButton;
    private readonly Button _undoEditButton;
    private readonly Button _exportSequenceButton;
    private readonly Button _refreshPreviewButton;
    private readonly CheckBox _enableCropBox;
    private readonly System.Windows.Forms.Timer _previewDebounceTimer;
    private readonly System.Windows.Forms.Timer _playerTimer;

    private string? _selectedFile;
    private bool _isPlaying;
    private bool _timelineUpdateFromPlayer;
    private double _videoDuration;
    private Size _videoSize = Size.Empty;
    private bool _updatingCropFields;
    private bool _updatingTrimRange;
    private bool _updatingTransformFields;
    private double _requestedPreviewTime;
    private TimelineEditMode _timelineEditMode = TimelineEditMode.Select;
    private int _targetVideoTrack = 1;
    private int _targetAudioTrack = 1;
    private int _transportDirection;
    private int _transportSpeedLevel;
    private CancellationTokenSource? _previewCts;
    private readonly List<TimelineSegment> _sequenceSegments = [];
    private readonly Dictionary<string, Image> _mediaThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Image>> _trimThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _waveformCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<double>> _clipMarkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<List<TimelineSegment>> _sequenceUndoStack = [];
    private double _timelineZoom = 1;
    private bool _snappingEnabled = true;

    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    private sealed record VideoDetails(double Duration, int Width, int Height);
    private sealed record TransformKeyframe(double SequenceLocalSec, float PositionX, float PositionY, float Scale, float Opacity);
    private sealed record ClipTransform(
        float PositionX = 0f,
        float PositionY = 0f,
        float Scale = 1f,
        float Opacity = 1f,
        bool PositionKeyframed = false,
        bool ScaleKeyframed = false,
        bool OpacityKeyframed = false,
        IReadOnlyList<TransformKeyframe>? Keyframes = null)
    {
        public IReadOnlyList<TransformKeyframe> SafeKeyframes => Keyframes ?? [];
    }

    private sealed record TimelineSegment(string SourceFile, double StartSec, double EndSec, int Track = 1, double SequenceStartSec = 0, ClipTransform? Transform = null)
    {
        public int SafeTrack => Math.Clamp(Track, 1, 2);
        public double Duration => Math.Max(0, EndSec - StartSec);
        public double SequenceEndSec => SequenceStartSec + Duration;
        public ClipTransform SafeTransform => Transform ?? new();
        public override string ToString() => $"[V{SafeTrack} @ {FormatTime(SequenceStartSec)}] {Path.GetFileName(SourceFile)}  •  {FormatTime(StartSec)} → {FormatTime(EndSec)}  ({FormatTime(Duration)})";
    }

    private enum TimelineEditMode
    {
        Select,
        Ripple,
        Rolling,
        Slip,
        Razor,
    }

    public QuickEditForm(string defaultOutputFolder)
    {
        var outputFolder = Directory.Exists(defaultOutputFolder)
            ? defaultOutputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Text = "VELO NLE Studio";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(12, 12, 12);
        ForeColor = Color.FromArgb(240, 240, 245);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        HandleCreated += (_, _) => WindowDarkMode.ApplyDarkMode(Handle);

        try
        {
            using var stream = typeof(QuickEditForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch
        {
        }

        var topBar = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 24),
            BackColor = Color.FromArgb(18, 18, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(topBar);

        var title = new Label
        {
            Text = "PREMIERE-STYLE TIMELINE EDITOR",
            AutoSize = true,
            Location = new Point(8, 4),
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            ForeColor = Color.FromArgb(136, 136, 136),
        };
        topBar.Controls.Add(title);

        _workspaceBadgeStrip = new FlowLayoutPanel
        {
            Location = new Point(230, 1),
            Size = new Size(1140, 20),
            AutoSize = false,
            WrapContents = false,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        _workspaceBadgeStrip.Controls.Add(BuildHeaderBadge("PROJECT", Color.Empty, Color.Empty));
        _workspaceBadgeStrip.Controls.Add(BuildHeaderBadge("SOURCE", Color.Empty, Color.Empty));
        _workspaceBadgeStrip.Controls.Add(BuildHeaderBadge("PROGRAM", Color.Empty, Color.Empty));
        _workspaceBadgeStrip.Controls.Add(BuildHeaderBadge("TIMELINE", Color.Empty, Color.Empty));
        _workspaceBadgeStrip.Controls.Add(BuildHeaderBadge("EXPORT", Color.Empty, Color.Empty));
        topBar.Controls.Add(_workspaceBadgeStrip);

        var leftPanel = new Panel
        {
            Location = new Point(0, 24),
            Size = new Size(280, 680),
            BackColor = Color.FromArgb(16, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };
        Controls.Add(leftPanel);

        var leftSplitter = new Panel
        {
            Size = new Size(1, 680),
            BackColor = Color.FromArgb(51, 51, 51),
            Cursor = Cursors.Default,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };
        Controls.Add(leftSplitter);

        leftPanel.Controls.Add(BuildSectionLabel("Project / media bin", 12, 12));
        leftPanel.Controls.Add(BuildSmallLabel("Import clips, search fast, then patch ranges into the timeline.", 12, 34, 248));

        _projectSearchBox = BuildTextBox(string.Empty, 12, 68, 144);
        _projectSearchBox.PlaceholderText = "Search clips...";
        _projectSearchBox.TextChanged += (_, _) => ApplyProjectBinSearch();
        leftPanel.Controls.Add(_projectSearchBox);

        var projectImportButton = BuildButton("Import", 160, 66, 88, (_, _) => AddFiles());
        leftPanel.Controls.Add(projectImportButton);

        _mediaThumbStrip = new FlowLayoutPanel
        {
            Location = new Point(12, 104),
            Size = new Size(236, 76),
            BackColor = Color.FromArgb(17, 17, 17),
            BorderStyle = BorderStyle.FixedSingle,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(2),
            Margin = Padding.Empty,
        };
        leftPanel.Controls.Add(_mediaThumbStrip);

        _filesList = new ListBox
        {
            Location = new Point(12, 190),
            Size = new Size(236, 382),
            HorizontalScrollbar = false,
            SelectionMode = SelectionMode.MultiExtended,
            AllowDrop = true,
            BackColor = Color.FromArgb(17, 17, 17),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 92,
            MultiColumn = true,
            ColumnWidth = 122,
            IntegralHeight = false,
            ScrollAlwaysVisible = true,
        };
        _filesList.DrawItem += DrawMediaBinItem;
        _filesList.SelectedIndexChanged += async (_, _) => await HandleSelectionChangedAsync();
        _filesList.DragEnter += OnFilesListDragEnter;
        _filesList.DragDrop += OnFilesListDragDrop;
        leftPanel.Controls.Add(_filesList);

        leftPanel.Controls.Add(BuildButton("Import files...", 12, 584, 112, (_, _) => AddFiles()));
        leftPanel.Controls.Add(BuildButton("Import folder", 132, 584, 116, (_, _) => ImportFolder()));
        leftPanel.Controls.Add(BuildButton("Watch", 12, 620, 62, (_, _) => LoadExistingClipsFromFolder(outputFolder)));
        leftPanel.Controls.Add(BuildButton("Remove", 82, 620, 72, (_, _) => RemoveSelected()));
        leftPanel.Controls.Add(BuildButton("Up", 162, 620, 38, (_, _) => MoveSelected(-1)));
        leftPanel.Controls.Add(BuildButton("Down", 208, 620, 40, (_, _) => MoveSelected(1)));

        var centerPanel = new Panel
        {
            Location = new Point(281, 24),
            Size = new Size(760, 340),
            BackColor = Color.FromArgb(15, 17, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(centerPanel);

        var timelinePanel = new Panel
        {
            Location = new Point(281, 364),
            Size = new Size(760, 340),
            BackColor = Color.FromArgb(15, 17, 20),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(timelinePanel);

        var rightSplitter = new Panel
        {
            Size = new Size(1, 680),
            BackColor = Color.FromArgb(51, 51, 51),
            Cursor = Cursors.Default,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
        };
        Controls.Add(rightSplitter);

        var sourceMonitorLabel = BuildSectionLabel("Source monitor", 14, 12);
        centerPanel.Controls.Add(sourceMonitorLabel);
        var programMonitorLabel = BuildSectionLabel("Program monitor", 390, 12);
        centerPanel.Controls.Add(programMonitorLabel);
        _videoInfoLabel = BuildSmallLabel("Select one clip to scrub and mark. The program monitor plays it back while the source monitor lets you crop visually.", 14, 34, 720);
        centerPanel.Controls.Add(_videoInfoLabel);

        _mediaElement = new WpfControls.MediaElement
        {
            LoadedBehavior = WpfControls.MediaState.Manual,
            UnloadedBehavior = WpfControls.MediaState.Manual,
            ScrubbingEnabled = true,
            Stretch = WpfMedia.Stretch.Uniform,
            Volume = 0.45,
        };
        _mediaElement.MediaOpened += (_, _) => OnMediaOpened();
        _mediaElement.MediaEnded += (_, _) => StopPlayback(resetToStart: true);
        _mediaElement.MediaFailed += (_, e) =>
        {
            StopPlayback(resetToStart: false);
            _playerStatusLabel!.Text = $"Playback error: {e.ErrorException?.Message ?? "unknown"}";
            _statusLabel!.Text = _playerStatusLabel.Text;
        };

        _sourcePreview = new EditorPreviewBox
        {
            Location = new Point(14, 64),
            Size = new Size(356, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _sourcePreview.CropChanged += OnCropSelectionChanged;
        centerPanel.Controls.Add(_sourcePreview);

        _playerHost = new ElementHost
        {
            Location = new Point(390, 64),
            Size = new Size(354, 200),
            Child = _mediaElement,
            BackColor = Color.FromArgb(10, 10, 12),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        centerPanel.Controls.Add(_playerHost);

        _previewTimeLabel = BuildSmallLabel("Playhead: 00:00.000", 14, 274, 220);
        _previewTimeLabel.Font = new Font("Consolas", 10f, FontStyle.Bold);
        _previewTimeLabel.ForeColor = Color.FromArgb(226, 232, 240);
        centerPanel.Controls.Add(_previewTimeLabel);

        _playerStatusLabel = BuildSmallLabel("Load a clip to start playback.", 390, 274, 354);
        _playerStatusLabel.Font = new Font("Segoe UI Semibold", 8.5f);
        _playerStatusLabel.ForeColor = Color.FromArgb(191, 219, 254);
        centerPanel.Controls.Add(_playerStatusLabel);

        var trimSectionLabel = BuildSectionLabel("Source trim / insert", 14, 302);
        centerPanel.Controls.Add(trimSectionLabel);
        var trimHint = BuildSmallLabel("Drag the IN and OUT handles on the clip to trim it, and use the mouse wheel over either timeline to zoom in or out.", 14, 324, 720);
        centerPanel.Controls.Add(trimHint);

        _trimTimelineView = new TrimTimelineView
        {
            Location = new Point(14, 356),
            Size = new Size(730, 86),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _trimTimelineView.SeekRequested += seconds => SeekToTime(seconds, refreshPreview: true);
        _trimTimelineView.RangeChanged += (start, end) => ApplyTrimRangeFromTimeline(start, end);
        _trimTimelineView.ZoomDeltaRequested += AdjustTimelineZoom;
        centerPanel.Controls.Add(_trimTimelineView);

        _timelineBar = new TrackBar
        {
            Location = new Point(14, 436),
            Size = new Size(632, 36),
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _timelineBar.Scroll += (_, _) => QueuePreviewRefreshFromSlider();
        centerPanel.Controls.Add(_timelineBar);

        _refreshPreviewButton = BuildButton("Refresh frame", 652, 434, 92, async (_, _) => await RefreshPreviewAsync(GetCurrentPreviewTime()));
        _refreshPreviewButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        centerPanel.Controls.Add(_refreshPreviewButton);

        _playPauseButton = BuildActionButton("▶ Play", 14, 468, 84, (_, _) => TogglePlayback());
        centerPanel.Controls.Add(_playPauseButton);

        _jumpBackButton = BuildButton("⏪ 5s", 98, 468, 64, (_, _) => SkipSeconds(-5));
        centerPanel.Controls.Add(_jumpBackButton);

        _jumpForwardButton = BuildButton("5s ⏩", 170, 468, 64, (_, _) => SkipSeconds(5));
        centerPanel.Controls.Add(_jumpForwardButton);

        _selectToolButton = BuildButton("⌖ Select (V)", 238, 468, 96, (_, _) => SetTimelineEditMode(TimelineEditMode.Select));
        centerPanel.Controls.Add(_selectToolButton);

        _rippleToolButton = BuildButton("⇄ Ripple (B)", 340, 468, 100, (_, _) => SetTimelineEditMode(TimelineEditMode.Ripple));
        centerPanel.Controls.Add(_rippleToolButton);

        _rollingToolButton = BuildButton("⥮ Roll (N)", 446, 468, 94, (_, _) => SetTimelineEditMode(TimelineEditMode.Rolling));
        centerPanel.Controls.Add(_rollingToolButton);

        _slipToolButton = BuildButton("⇆ Slip (Y)", 546, 468, 92, (_, _) => SetTimelineEditMode(TimelineEditMode.Slip));
        centerPanel.Controls.Add(_slipToolButton);

        _razorToolButton = BuildButton("✂ Razor (C)", 644, 468, 96, (_, _) => SetTimelineEditMode(TimelineEditMode.Razor));
        centerPanel.Controls.Add(_razorToolButton);

        _timelineModeLabel = BuildSmallLabel("Pro tools active — B Ripple, N Roll, Y Slip, C Razor, JKL transport, Ctrl+Z undo.", 14, 0, 720);
        centerPanel.Controls.Add(_timelineModeLabel);

        var sequenceSectionLabel = BuildSectionLabel("Sequence timeline", 14, 512);
        centerPanel.Controls.Add(sequenceSectionLabel);
        var timelineHint = BuildSmallLabel("Premiere-style flow: patch a target track, Insert/Overwrite from the Source monitor, B ripple, N roll, Y slip, C razor, and JKL for transport.", 14, 534, 720);
        centerPanel.Controls.Add(timelineHint);

        _sequenceTimelineView = new SequenceTimelineView
        {
            Location = new Point(14, 566),
            Size = new Size(730, 120),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        _sequenceTimelineView.SegmentClicked += async index => await LoadSequenceSegmentAsync(index);
        _sequenceTimelineView.SegmentTrimChanged += (index, start, end) => ApplySequenceTrimFromTimeline(index, start, end);
        _sequenceTimelineView.SegmentMoved += (fromIndex, toIndex, track) => MoveSequenceSegmentTo(fromIndex, toIndex, track);
        _sequenceTimelineView.SegmentSplitRequested += (index, splitTime) => SplitSelectedSegmentAtPlayhead(index, splitTime);
        _sequenceTimelineView.RippleDeleteRequested += (insertIndex, track) => RippleDeleteGapAt(insertIndex, track);
        _sequenceTimelineView.SegmentSlipRequested += (index, deltaSeconds) => ApplySequenceSlipFromTimeline(index, deltaSeconds);
        _sequenceTimelineView.TrackTargetChanged += (videoTrack, audioTrack) =>
        {
            _targetVideoTrack = Math.Clamp(videoTrack, 1, 2);
            _targetAudioTrack = Math.Clamp(audioTrack, 1, 2);
            if (_statusLabel is not null)
                _statusLabel.Text = $"Source patching armed — V{_targetVideoTrack} / A{_targetAudioTrack} targeted.";
        };
        _sequenceTimelineView.SeekRequested += async seconds => await ScrubSequenceToTimeAsync(seconds);
        _sequenceTimelineView.ZoomDeltaRequested += AdjustTimelineZoom;
        _sequenceTimelineView.SetWaveformProvider(file => _waveformCache.TryGetValue(file, out var waveform) ? waveform : null);
        _sequenceTimelineView.SetThumbnailProvider(file =>
        {
            if (_trimThumbCache.TryGetValue(file, out var thumbs) && thumbs.Count > 0)
                return thumbs;
            if (_mediaThumbCache.TryGetValue(file, out var thumb))
                return [thumb];
            return [];
        });
        centerPanel.Controls.Add(_sequenceTimelineView);

        timelinePanel.Controls.Add(trimSectionLabel);
        timelinePanel.Controls.Add(trimHint);
        timelinePanel.Controls.Add(_trimTimelineView);
        timelinePanel.Controls.Add(_timelineBar);
        timelinePanel.Controls.Add(_refreshPreviewButton);
        timelinePanel.Controls.Add(_playPauseButton);
        timelinePanel.Controls.Add(_jumpBackButton);
        timelinePanel.Controls.Add(_jumpForwardButton);
        timelinePanel.Controls.Add(_selectToolButton);
        timelinePanel.Controls.Add(_rippleToolButton);
        timelinePanel.Controls.Add(_razorToolButton);
        timelinePanel.Controls.Add(_timelineModeLabel);
        timelinePanel.Controls.Add(sequenceSectionLabel);
        timelinePanel.Controls.Add(timelineHint);
        timelinePanel.Controls.Add(_sequenceTimelineView);

        var rightPanel = new Panel
        {
            Location = new Point(1120, 24),
            Size = new Size(280, 680),
            BackColor = Color.FromArgb(16, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            AutoScroll = true,
        };
        Controls.Add(rightPanel);

        int y = 12;
        _inspectorHeaderLabel = BuildSectionLabel("Inspector / export", 14, y);
        rightPanel.Controls.Add(_inspectorHeaderLabel);
        y += 22;
        _inspectorHintLabel = BuildSmallLabel("Select a clip to see its properties here, or leave nothing selected to use export settings.", 14, y, 260);
        rightPanel.Controls.Add(_inspectorHintLabel);
        y += 38;

        _outputPreview = new PictureBox
        {
            Location = new Point(14, y),
            Size = new Size(260, 146),
            BackColor = Color.FromArgb(10, 10, 12),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
        };
        rightPanel.Controls.Add(_outputPreview);
        y += 158;

        rightPanel.Controls.Add(BuildLabel("Name", 14, y));
        y += 18;
        _outputNameBox = BuildTextBox("Leave blank to auto-name", 14, y, 260);
        rightPanel.Controls.Add(_outputNameBox);
        y += 38;

        rightPanel.Controls.Add(BuildLabel("Folder", 14, y));
        y += 18;
        _outputFolderBox = BuildTextBox(outputFolder, 14, y, 180);
        rightPanel.Controls.Add(_outputFolderBox);
        rightPanel.Controls.Add(BuildButton("Browse", 202, y - 1, 72, (_, _) => PickOutputFolder()));
        y += 46;

        rightPanel.Controls.Add(BuildSectionLabel("Trim", 14, y));
        y += 22;
        rightPanel.Controls.Add(BuildLabel("Start (sec)", 14, y));
        rightPanel.Controls.Add(BuildLabel("End (sec)", 150, y));
        y += 18;
        _startBox = BuildNumeric(0, 0, 86400, 14, y, 124);
        _endBox = BuildNumeric(30, 0, 86400, 150, y, 124);
        rightPanel.Controls.Add(_startBox);
        rightPanel.Controls.Add(_endBox);
        _startBox.ValueChanged += (_, _) => SyncTrimRangeFromFields();
        _endBox.ValueChanged += (_, _) => SyncTrimRangeFromFields();
        y += 36;
        rightPanel.Controls.Add(BuildButton("Mark IN at playhead", 14, y, 124, (_, _) => SetTrimBoundary(true)));
        rightPanel.Controls.Add(BuildButton("Mark OUT at playhead", 150, y, 124, (_, _) => SetTrimBoundary(false)));
        y += 38;
        _trimButton = BuildActionButton("Save clip from IN/OUT range", 14, y, 260, async (_, _) => await RunTrimAsync());
        rightPanel.Controls.Add(_trimButton);
        y += 36;

        _markerHintLabel = BuildSmallLabel("Source first: mark IN/OUT, then Insert or Overwrite at the playhead. B = Ripple, N = Roll, Y = Slip, C = Razor, Ctrl+Z = undo.", 14, y, 260);
        rightPanel.Controls.Add(_markerHintLabel);
        y += 42;

        _addCutButton = BuildButton("⤶ Insert", 14, y, 124, (_, _) => AddCurrentCutToSequence(overwrite: false));
        rightPanel.Controls.Add(_addCutButton);
        _overwriteCutButton = BuildButton("⤼ Overwrite", 150, y, 124, (_, _) => AddCurrentCutToSequence(overwrite: true));
        rightPanel.Controls.Add(_overwriteCutButton);
        y += 36;

        _splitPlayheadButton = BuildButton("✂ Cut at playhead", 14, y, 124, (_, _) => SplitSelectedSegmentAtPlayhead());
        rightPanel.Controls.Add(_splitPlayheadButton);
        _undoEditButton = BuildButton("↶ Undo edit", 150, y, 124, (_, _) => UndoLastSequenceEdit());
        rightPanel.Controls.Add(_undoEditButton);
        y += 36;

        _addMarkerButton = BuildButton("Add marker at playhead", 14, y, 180, (_, _) => AddMarkerAtPlayhead());
        rightPanel.Controls.Add(_addMarkerButton);
        rightPanel.Controls.Add(BuildButton("Remove", 202, y, 72, (_, _) => RemoveSelectedMarker()));
        y += 36;

        _markerList = new ListBox
        {
            Location = new Point(14, y),
            Size = new Size(260, 78),
            BackColor = Color.FromArgb(14, 14, 18),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true,
        };
        _markerList.DoubleClick += (_, _) => JumpToSelectedMarker();
        rightPanel.Controls.Add(_markerList);
        y += 90;

        rightPanel.Controls.Add(BuildSectionLabel("Frame crop (optional)", 14, y));
        y += 22;
        _enableCropBox = new CheckBox
        {
            Text = "Enable frame crop",
            Checked = true,
            AutoSize = true,
            Location = new Point(14, y),
            ForeColor = Color.FromArgb(220, 220, 230),
            BackColor = Color.Transparent,
        };
        _enableCropBox.CheckedChanged += (_, _) =>
        {
            _sourcePreview.ShowCropOverlay = _enableCropBox.Checked;
            _ = RefreshPreviewAsync(GetCurrentPreviewTime());
        };
        rightPanel.Controls.Add(_enableCropBox);
        y += 26;

        _cropInfoLabel = BuildSmallLabel("Crop: full frame", 14, y, 260);
        rightPanel.Controls.Add(_cropInfoLabel);
        y += 34;

        rightPanel.Controls.Add(BuildButton("16:9", 14, y, 46, (_, _) => ApplyAspectCrop(16, 9)));
        rightPanel.Controls.Add(BuildButton("9:16", 66, y, 46, (_, _) => ApplyAspectCrop(9, 16)));
        rightPanel.Controls.Add(BuildButton("1:1", 118, y, 46, (_, _) => ApplyAspectCrop(1, 1)));
        rightPanel.Controls.Add(BuildButton("4:5", 170, y, 46, (_, _) => ApplyAspectCrop(4, 5)));
        rightPanel.Controls.Add(BuildButton("Center", 222, y, 52, (_, _) => CenterCropSelection()));
        y += 38;

        rightPanel.Controls.Add(BuildLabel("X", 14, y));
        rightPanel.Controls.Add(BuildLabel("Y", 80, y));
        rightPanel.Controls.Add(BuildLabel("W", 146, y));
        rightPanel.Controls.Add(BuildLabel("H", 212, y));
        y += 18;
        _cropXBox = BuildNumeric(0, 0, 10000, 14, y, 56);
        _cropYBox = BuildNumeric(0, 0, 10000, 80, y, 56);
        _cropWBox = BuildNumeric(1920, 1, 10000, 146, y, 56);
        _cropHBox = BuildNumeric(1080, 1, 10000, 212, y, 56);
        rightPanel.Controls.Add(_cropXBox);
        rightPanel.Controls.Add(_cropYBox);
        rightPanel.Controls.Add(_cropWBox);
        rightPanel.Controls.Add(_cropHBox);
        y += 38;

        _cropXBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropYBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropWBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropHBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();

        rightPanel.Controls.Add(BuildButton("Use full frame", 14, y, 124, (_, _) => ResetCropToFullFrame()));
        rightPanel.Controls.Add(BuildButton("Preview crop", 150, y, 124, async (_, _) => await RefreshPreviewAsync(GetCurrentPreviewTime())));
        y += 38;
        _cropButton = BuildActionButton("Save cropped clip", 14, y, 260, async (_, _) => await RunCropAsync());
        rightPanel.Controls.Add(_cropButton);
        y += 50;

        rightPanel.Controls.Add(BuildSectionLabel("Motion / keyframes", 14, y));
        y += 22;
        rightPanel.Controls.Add(BuildLabel("Position", 14, y));
        _positionKeyframeButton = BuildButton("⏱", 202, y - 1, 32, (_, _) => ToggleKeyframingForSelection("position"));
        rightPanel.Controls.Add(_positionKeyframeButton);
        y += 18;
        _positionXBox = BuildNumeric(0, -4000, 4000, 14, y, 124);
        _positionYBox = BuildNumeric(0, -4000, 4000, 150, y, 124);
        rightPanel.Controls.Add(_positionXBox);
        rightPanel.Controls.Add(_positionYBox);
        y += 36;

        rightPanel.Controls.Add(BuildLabel("Scale %", 14, y));
        _scaleKeyframeButton = BuildButton("⏱", 202, y - 1, 32, (_, _) => ToggleKeyframingForSelection("scale"));
        rightPanel.Controls.Add(_scaleKeyframeButton);
        y += 18;
        _scaleBox = BuildNumeric(100, 10, 400, 14, y, 124);
        rightPanel.Controls.Add(_scaleBox);
        y += 36;

        rightPanel.Controls.Add(BuildLabel("Opacity %", 14, y));
        _opacityKeyframeButton = BuildButton("⏱", 202, y - 1, 32, (_, _) => ToggleKeyframingForSelection("opacity"));
        rightPanel.Controls.Add(_opacityKeyframeButton);
        y += 18;
        _opacityBox = BuildNumeric(100, 0, 100, 14, y, 124);
        rightPanel.Controls.Add(_opacityBox);
        y += 36;

        rightPanel.Controls.Add(BuildButton("Set/Update keyframe", 14, y, 124, (_, _) => CaptureTransformKeyframeAtPlayhead()));
        rightPanel.Controls.Add(BuildButton("Reset motion", 150, y, 124, (_, _) => ResetSelectedMotion()));
        y += 46;

        _positionXBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _positionYBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _scaleBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _opacityBox.ValueChanged += (_, _) => SyncTransformFromFields();

        rightPanel.Controls.Add(BuildSectionLabel("Timeline / sequence", 14, y));
        y += 22;
        _sequenceHintLabel = BuildSmallLabel("Click V1/V2 or A1/A2 in the track headers to patch the target tracks. B ripple-edits, N rolls, Y slips, and JKL drives playback.", 14, y, 260);
        rightPanel.Controls.Add(_sequenceHintLabel);
        y += 34;

        _sequenceList = new ListBox
        {
            Location = new Point(14, y),
            Size = new Size(260, 120),
            BackColor = Color.FromArgb(14, 14, 18),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true,
        };
        _sequenceList.SelectedIndexChanged += (_, _) =>
        {
            _sequenceTimelineView.SetSelectedIndex(_sequenceList.SelectedIndex);
            _splitPlayheadButton.Enabled = _sequenceList.SelectedIndex >= 0;
            UpdateTransformInspectorUi();
        };
        _sequenceList.DoubleClick += async (_, _) => await LoadSequenceSegmentAsync(_sequenceList.SelectedIndex);
        rightPanel.Controls.Add(_sequenceList);
        y += 128;

        rightPanel.Controls.Add(BuildButton("Ripple", 14, y, 70, (_, _) => RemoveSelectedSequenceSegment()));
        rightPanel.Controls.Add(BuildButton("Up", 92, y, 42, (_, _) => MoveSelectedSequenceSegment(-1)));
        rightPanel.Controls.Add(BuildButton("Down", 142, y, 52, (_, _) => MoveSelectedSequenceSegment(1)));
        rightPanel.Controls.Add(BuildButton("Clear all", 202, y, 72, (_, _) => ClearSequence()));
        y += 38;

        _sequenceSummaryLabel = BuildSmallLabel("Timeline empty — insert a range to start cutting.", 14, y, 260);
        _sequenceSummaryLabel.Font = new Font("Consolas", 8.5f, FontStyle.Bold);
        _sequenceSummaryLabel.ForeColor = Color.FromArgb(226, 232, 240);
        rightPanel.Controls.Add(_sequenceSummaryLabel);
        y += 36;

        _exportSequenceButton = BuildActionButton("Save full timeline as video", 14, y, 260, async (_, _) => await ExportSequenceAsync());
        rightPanel.Controls.Add(_exportSequenceButton);
        y += 40;

        rightPanel.Controls.Add(BuildSectionLabel("Quick merge", 14, y));
        y += 22;
        rightPanel.Controls.Add(BuildSmallLabel("Or merge the currently-selected clips directly in their listed order.", 14, y, 260));
        y += 40;
        _mergeButton = BuildActionButton("Auto-merge selected clips", 14, y, 260, async (_, _) => await RunMergeAsync());
        rightPanel.Controls.Add(_mergeButton);

        _statusLabel = new Label
        {
            Text = "Ready — save one clip with IN/OUT, or save the full timeline below.",
            AutoSize = false,
            Size = new Size(1240, 36),
            Location = new Point(20, 726),
            ForeColor = Color.FromArgb(226, 232, 240),
            BackColor = Color.FromArgb(10, 12, 15),
            Font = new Font("Consolas", 9f, FontStyle.Bold),
            Padding = new Padding(8, 5, 0, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Controls.Add(_statusLabel);

        var leftPanelWidth = 270;
        var rightPanelWidth = 292;
        var resizingLeftPanel = false;
        var resizingRightPanel = false;
        var resizeOriginX = 0;
        var resizeOriginWidth = 0;

        leftSplitter.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            resizingLeftPanel = true;
            resizeOriginX = Cursor.Position.X;
            resizeOriginWidth = leftPanelWidth;
        };
        leftSplitter.MouseMove += (_, _) =>
        {
            if (!resizingLeftPanel) return;
            leftPanelWidth = Math.Clamp(resizeOriginWidth + (Cursor.Position.X - resizeOriginX), 220, 420);
            LayoutWorkspace();
        };
        leftSplitter.MouseUp += (_, _) => resizingLeftPanel = false;

        rightSplitter.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            resizingRightPanel = true;
            resizeOriginX = Cursor.Position.X;
            resizeOriginWidth = rightPanelWidth;
        };
        rightSplitter.MouseMove += (_, _) =>
        {
            if (!resizingRightPanel) return;
            rightPanelWidth = Math.Clamp(resizeOriginWidth - (Cursor.Position.X - resizeOriginX), 250, 420);
            LayoutWorkspace();
        };
        rightSplitter.MouseUp += (_, _) => resizingRightPanel = false;
        MouseUp += (_, _) =>
        {
            resizingLeftPanel = false;
            resizingRightPanel = false;
        };

        void LayoutWorkspace()
        {
            const int navHeight = 24;
            const int separator = 1;
            const int sideWidth = 280;
            const int footerHeight = 24;

            topBar.Bounds = new Rectangle(0, 0, ClientSize.Width, navHeight);
            _workspaceBadgeStrip.Location = new Point(Math.Min(220, Math.Max(150, title.Right + 12)), 1);
            _workspaceBadgeStrip.Size = new Size(Math.Max(120, topBar.ClientSize.Width - _workspaceBadgeStrip.Left - 6), 20);

            var top = navHeight;
            var panelHeight = Math.Max(520, ClientSize.Height - top - footerHeight);
            var centerLeft = sideWidth + separator;
            var centerWidth = Math.Max(520, ClientSize.Width - (sideWidth * 2) - (separator * 2));
            var topRowHeight = Math.Max(220, panelHeight / 2);
            var bottomRowHeight = Math.Max(220, panelHeight - topRowHeight);

            leftPanel.Bounds = new Rectangle(0, top, sideWidth, panelHeight);
            leftSplitter.Bounds = new Rectangle(leftPanel.Right, top, separator, panelHeight);
            centerPanel.Bounds = new Rectangle(centerLeft, top, centerWidth, topRowHeight);
            timelinePanel.Bounds = new Rectangle(centerLeft, centerPanel.Bottom, centerWidth, bottomRowHeight);
            rightSplitter.Bounds = new Rectangle(ClientSize.Width - sideWidth - separator, top, separator, panelHeight);
            rightPanel.Bounds = new Rectangle(ClientSize.Width - sideWidth, top, sideWidth, panelHeight);

            _projectSearchBox.Bounds = new Rectangle(8, 64, Math.Max(120, leftPanel.ClientSize.Width - 84), 24);
            projectImportButton.Location = new Point(leftPanel.ClientSize.Width - 68, 64);
            projectImportButton.Size = new Size(60, 24);
            _mediaThumbStrip.Bounds = new Rectangle(8, 96, leftPanel.ClientSize.Width - 16, Math.Max(140, leftPanel.ClientSize.Height - 180));
            _filesList.Bounds = new Rectangle(-2000, -2000, 1, 1);
            _filesList.Visible = false;

            const int margin = 8;
            const int monitorGap = 8;
            var availableWidth = Math.Max(320, centerPanel.ClientSize.Width - (margin * 2));
            var sourceWidth = Math.Clamp((int)Math.Round((availableWidth - monitorGap) * 0.36), 220, 360);
            var programX = margin + sourceWidth + monitorGap;
            var programWidth = Math.Max(260, availableWidth - sourceWidth - monitorGap);
            var monitorHeight = Math.Max(120, centerPanel.ClientSize.Height - 86);

            sourceMonitorLabel.Location = new Point(margin, 8);
            programMonitorLabel.Location = new Point(programX, 8);
            _videoInfoLabel.Location = new Point(margin, 24);
            _videoInfoLabel.Size = new Size(centerPanel.ClientSize.Width - (margin * 2), 22);

            _sourcePreview.Bounds = new Rectangle(margin, 48, sourceWidth, monitorHeight);
            _playerHost.Bounds = new Rectangle(programX, 48, programWidth, monitorHeight);

            _previewTimeLabel.Location = new Point(margin, _sourcePreview.Bottom + 4);
            _previewTimeLabel.Size = new Size(sourceWidth, 18);
            _playerStatusLabel.Location = new Point(programX, _playerHost.Bottom + 4);
            _playerStatusLabel.Size = new Size(programWidth, 18);

            trimSectionLabel.Location = new Point(margin, 8);
            trimHint.Location = new Point(margin, 24);
            trimHint.Size = new Size(timelinePanel.ClientSize.Width - (margin * 2), 22);

            _trimTimelineView.Bounds = new Rectangle(margin, 48, timelinePanel.ClientSize.Width - (margin * 2), 82);
            _refreshPreviewButton.Location = new Point(timelinePanel.ClientSize.Width - margin - _refreshPreviewButton.Width, _trimTimelineView.Bottom + 6);
            _timelineBar.Bounds = new Rectangle(margin, _trimTimelineView.Bottom + 6, Math.Max(220, _refreshPreviewButton.Left - margin - 6), 24);

            var transportY = _timelineBar.Bottom + 6;
            _playPauseButton.Location = new Point(margin, transportY);
            _jumpBackButton.Location = new Point(_playPauseButton.Right + 6, transportY);
            _jumpForwardButton.Location = new Point(_jumpBackButton.Right + 6, transportY);
            _selectToolButton.Location = new Point(_jumpForwardButton.Right + 10, transportY);
            _rippleToolButton.Location = new Point(_selectToolButton.Right + 6, transportY);
            _rollingToolButton.Location = new Point(_rippleToolButton.Right + 6, transportY);
            _slipToolButton.Location = new Point(_rollingToolButton.Right + 6, transportY);
            _razorToolButton.Location = new Point(_slipToolButton.Right + 6, transportY);
            _timelineModeLabel.Location = new Point(margin, _selectToolButton.Bottom + 6);
            _timelineModeLabel.Size = new Size(Math.Max(120, timelinePanel.ClientSize.Width - (margin * 2)), 18);

            var toolRowBottom = Math.Max(_timelineModeLabel.Bottom, Math.Max(_razorToolButton.Bottom, _playPauseButton.Bottom));
            sequenceSectionLabel.Location = new Point(margin, toolRowBottom + 8);
            timelineHint.Location = new Point(margin, sequenceSectionLabel.Bottom + 1);
            timelineHint.Size = new Size(timelinePanel.ClientSize.Width - (margin * 2), 22);
            _sequenceTimelineView.Bounds = new Rectangle(margin, timelineHint.Bottom + 2, timelinePanel.ClientSize.Width - (margin * 2), Math.Max(120, timelinePanel.ClientSize.Height - timelineHint.Bottom - 8));

            _statusLabel.Location = new Point(8, ClientSize.Height - 20);
            _statusLabel.Size = new Size(ClientSize.Width - 16, 16);
        }

        leftPanel.Paint += (_, e) =>
        {
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(8, 8, leftPanel.ClientSize.Width - 8, _mediaThumbStrip.Bottom + 8), "PROJECT BIN", "media gallery", Color.FromArgb(124, 58, 237));
        };

        centerPanel.Paint += (_, e) =>
        {
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(_sourcePreview.Left - 8, sourceMonitorLabel.Top - 6, _sourcePreview.Right + 8, _sourcePreview.Bottom + 8), "SOURCE", "mark in / out", Color.FromArgb(124, 58, 237));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(_playerHost.Left - 8, programMonitorLabel.Top - 6, _playerHost.Right + 8, _playerHost.Bottom + 8), "PROGRAM", "review output", Color.FromArgb(59, 130, 246));
        };

        timelinePanel.Paint += (_, e) =>
        {
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(_trimTimelineView.Left - 8, trimSectionLabel.Top - 6, _trimTimelineView.Right + 8, _timelineBar.Bottom + 8), "SOURCE PATCH", "insert / overwrite range", Color.FromArgb(20, 184, 166));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(_playPauseButton.Left - 8, _playPauseButton.Top - 8, _timelineModeLabel.Right + 8, Math.Max(_playPauseButton.Bottom, Math.Max(_rippleToolButton.Bottom, _timelineModeLabel.Bottom)) + 8), "TOOLS", "transport + edit mode", Color.FromArgb(251, 191, 36));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(_sequenceTimelineView.Left - 8, sequenceSectionLabel.Top - 6, _sequenceTimelineView.Right + 8, _sequenceTimelineView.Bottom + 8), "TIMELINE", "ripple / razor / reorder", Color.FromArgb(139, 92, 246));
        };

        rightPanel.Paint += (_, e) =>
        {
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(8, 8, rightPanel.ClientSize.Width - 8, _outputPreview.Bottom + 10), "INSPECTOR", "preview + export", Color.FromArgb(34, 197, 94));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(8, _startBox.Top - 34, rightPanel.ClientSize.Width - 8, _markerList.Bottom + 8), "EDIT CONTROLS", "insert / overwrite / cut", Color.FromArgb(59, 130, 246));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(8, _enableCropBox.Top - 26, rightPanel.ClientSize.Width - 8, _cropButton.Bottom + 8), "CROP", "optional framing", Color.FromArgb(236, 72, 153));
            DrawWorkspaceCard(e.Graphics, Rectangle.FromLTRB(8, _sequenceList.Top - 34, rightPanel.ClientSize.Width - 8, _mergeButton.Bottom + 8), "SEQUENCE", "timeline operations", Color.FromArgb(124, 58, 237));
        };

        SizeChanged += (_, _) => LayoutWorkspace();
        centerPanel.SizeChanged += (_, _) => LayoutWorkspace();
        timelinePanel.SizeChanged += (_, _) => LayoutWorkspace();
        KeyDown += OnEditorKeyDown;
        LayoutWorkspace();
        UpdateTimelineZoom();
        ApplyObsidianScrollbarTheme(_mediaThumbStrip);
        ApplyObsidianScrollbarTheme(_sequenceList);
        ApplyObsidianScrollbarTheme(_markerList);
        ApplyObsidianScrollbarTheme(rightPanel);
        SetTimelineEditMode(TimelineEditMode.Select);
        _sequenceTimelineView.SetTargetTracks(_targetVideoTrack, _targetAudioTrack);

        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 280 };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer.Stop();
            await RefreshPreviewAsync(_requestedPreviewTime);
        };

        _playerTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _playerTimer.Tick += (_, _) => StepTransportPlayback();

        UpdateSequenceUi();
        UpdateMarkerUi();
        UpdateInspectorModeUi();
        LoadExistingClipsFromFolder(outputFolder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _playerTimer.Stop();
            _playerTimer.Dispose();
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Dispose();
            _previewCts?.Cancel();
            StopPlayback(resetToStart: false);
            ReplacePicture(_outputPreview, null);
            _sourcePreview.DisposePreviewImage();
            foreach (var image in _mediaThumbCache.Values)
                image.Dispose();
            _mediaThumbCache.Clear();
            foreach (var images in _trimThumbCache.Values)
                foreach (var image in images)
                    image.Dispose();
            _trimThumbCache.Clear();
            foreach (var image in _waveformCache.Values)
                image.Dispose();
            _waveformCache.Clear();
        }

        base.Dispose(disposing);
    }

    private static Label BuildSectionLabel(string text, int x, int y) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
        ForeColor = Color.FromArgb(136, 136, 136),
    };

    private static Label BuildHeaderBadge(string text, Color background, Color foreground) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        Margin = new Padding(0, 0, 4, 0),
        Padding = new Padding(8, 2, 8, 2),
        BackColor = Color.FromArgb(22, 22, 26),
        ForeColor = Color.FromArgb(136, 136, 136),
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static Label BuildLabel(string text, int x, int y) => new()
    {
        Text = text.ToUpperInvariant(),
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
        ForeColor = Color.FromArgb(136, 136, 136),
    };

    private static Label BuildSmallLabel(string text, int x, int y, int width) => new()
    {
        Text = text.ToUpperInvariant(),
        Location = new Point(x, y),
        Size = new Size(width, 28),
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
        ForeColor = Color.FromArgb(136, 136, 136),
    };

    private static TextBox BuildTextBox(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 24),
        BackColor = Color.FromArgb(14, 14, 18),
        ForeColor = Color.FromArgb(230, 230, 235),
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
    };

    private static NumericUpDown BuildNumeric(decimal value, decimal min, decimal max, int x, int y, int width)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = 2,
            Increment = 0.25M,
            Location = new Point(x, y),
            Size = new Size(width, 24),
            BackColor = Color.FromArgb(14, 14, 18),
            ForeColor = Color.FromArgb(230, 230, 235),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 8.25f, FontStyle.Regular),
        };

        numeric.Value = Math.Clamp(value, min, max);
        return numeric;
    }

    private static Button BuildButton(string text, int x, int y, int width, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 24),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(24, 24, 28),
            ForeColor = Color.FromArgb(225, 225, 228),
            Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(51, 51, 51);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 32, 36);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 42, 48);
        button.Click += onClick;
        return button;
    }

    private static Button BuildActionButton(string text, int x, int y, int width, EventHandler onClick)
    {
        var button = BuildButton(text, x, y, width, onClick);
        button.BackColor = Color.FromArgb(30, 33, 38);
        button.FlatAppearance.BorderColor = Color.FromArgb(84, 84, 90);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 42, 48);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 48, 54);
        return button;
    }

    private static void DrawWorkspaceCard(Graphics graphics, Rectangle rect, string title, string subtitle, Color accent)
    {
        return;
    }

    private static void StyleToolButton(Button button, bool active)
    {
        button.BackColor = active ? Color.FromArgb(44, 48, 54) : Color.FromArgb(24, 24, 28);
        button.ForeColor = active ? Color.White : Color.FromArgb(200, 200, 204);
        button.FlatAppearance.BorderColor = active ? Color.FromArgb(104, 104, 112) : Color.FromArgb(51, 51, 51);
    }

    private void SelectMediaBinFile(string file, Keys modifiers)
    {
        var index = _filesList.Items.IndexOf(file);
        if (index < 0)
            return;

        if ((modifiers & Keys.Control) == Keys.Control)
            _filesList.SetSelected(index, !_filesList.GetSelected(index));
        else
        {
            _filesList.ClearSelected();
            _filesList.SelectedIndex = index;
        }

        if (_filesList.SelectedIndices.Count > 0)
            _filesList.TopIndex = Math.Max(0, _filesList.SelectedIndices[0]);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void DrawMediaBinItem(object? sender, DrawItemEventArgs e)
    {
        using var surfaceBrush = new SolidBrush(Color.FromArgb(17, 17, 17));
        e.Graphics.FillRectangle(surfaceBrush, e.Bounds);

        if (e.Index < 0 || e.Index >= _filesList.Items.Count)
            return;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var filePath = _filesList.Items[e.Index]?.ToString() ?? string.Empty;
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tileRect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 4, Math.Max(90, e.Bounds.Width - 8), Math.Max(82, e.Bounds.Height - 8));
        var thumbRect = new Rectangle(tileRect.Left + 4, tileRect.Top + 4, tileRect.Width - 8, 54);
        var textRect = new Rectangle(tileRect.Left + 4, thumbRect.Bottom + 4, tileRect.Width - 8, tileRect.Bottom - thumbRect.Bottom - 8);

        using var tileBrush = new SolidBrush(Color.FromArgb(20, 20, 22));
        using var borderPen = new Pen(isSelected ? Color.FromArgb(59, 130, 246) : Color.FromArgb(51, 51, 51), 1);
        using var thumbBack = new SolidBrush(Color.Black);
        e.Graphics.FillRectangle(tileBrush, tileRect);
        e.Graphics.FillRectangle(thumbBack, thumbRect);
        e.Graphics.DrawRectangle(borderPen, tileRect);
        e.Graphics.DrawRectangle(Pens.DimGray, thumbRect);

        if (!string.IsNullOrWhiteSpace(filePath) && _mediaThumbCache.TryGetValue(filePath, out var cachedThumb))
            e.Graphics.DrawImage(cachedThumb, thumbRect);
        else
            TextRenderer.DrawText(e.Graphics, "VIDEO", new Font("Segoe UI", 7f, FontStyle.Bold), thumbRect, Color.FromArgb(160, 160, 170), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var label = string.IsNullOrWhiteSpace(filePath) ? "EMPTY" : Path.GetFileName(filePath);
        TextRenderer.DrawText(
            e.Graphics,
            label,
            new Font("Segoe UI", 7.5f, FontStyle.Bold),
            textRect,
            isSelected ? Color.White : Color.FromArgb(210, 210, 215),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);

        e.DrawFocusRectangle();
    }

    private void OnFilesListDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnFilesListDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        var added = AddMediaFiles(files.Where(File.Exists));
        _statusLabel.Text = added == 0
            ? "Those clips are already in the media bin."
            : $"Added {added} dragged clip(s) to the editor.";
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private int AddMediaFiles(IEnumerable<string> files)
    {
        var added = 0;
        foreach (var file in files.Where(File.Exists))
        {
            if (!_filesList.Items.Contains(file))
            {
                _filesList.Items.Add(file);
                added++;
            }
        }
        return added;
    }

    private void ApplyProjectBinSearch()
    {
        var query = _projectSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return;

        var matchIndex = _filesList.FindString(query);
        if (matchIndex >= 0)
        {
            _filesList.SelectedIndex = matchIndex;
            _filesList.TopIndex = matchIndex;
        }
    }

    private void UpdateInspectorModeUi()
    {
        var hasClipSelection = !string.IsNullOrWhiteSpace(_selectedFile) || (_sequenceList?.SelectedIndex ?? -1) >= 0;
        if (hasClipSelection)
        {
            _inspectorHeaderLabel.Text = "CLIP INSPECTOR";
            _inspectorHintLabel.Text = "TRANSFORM, KEYFRAMES, CROP, AND SOURCE-LINKED DETAILS FOR THE SELECTED CLIP LIVE HERE.";
        }
        else
        {
            _inspectorHeaderLabel.Text = "EXPORT INSPECTOR";
            _inspectorHintLabel.Text = "NO CLIP SELECTED — SET YOUR OUTPUT NAME, FOLDER, AND RENDER/EXPORT CONTROLS HERE.";
        }

        UpdateTransformInspectorUi();
    }

    private double GetSequenceTotalDuration()
    {
        return _sequenceSegments.Count == 0 ? 0 : _sequenceSegments.Max(segment => segment.SequenceEndSec);
    }

    private List<TimelineSegment> GetActiveSegmentsAtTime(double sequenceSeconds)
    {
        return _sequenceSegments
            .Where(segment => sequenceSeconds >= segment.SequenceStartSec - 0.0001 && sequenceSeconds <= segment.SequenceEndSec + 0.0001)
            .OrderBy(segment => segment.SafeTrack)
            .ThenBy(segment => segment.SequenceStartSec)
            .ToList();
    }

    private TimelineSegment? GetTopVisibleSegmentAtTime(double sequenceSeconds)
    {
        return GetActiveSegmentsAtTime(sequenceSeconds)
            .OrderByDescending(segment => segment.SafeTrack)
            .ThenByDescending(segment => segment.SequenceStartSec)
            .FirstOrDefault();
    }

    private void UpdateTransformInspectorUi()
    {
        if (_positionXBox is null)
            return;

        var selectedIndex = _sequenceList?.SelectedIndex ?? -1;
        var hasTimelineSelection = selectedIndex >= 0 && selectedIndex < _sequenceSegments.Count;

        _positionXBox.Enabled = hasTimelineSelection;
        _positionYBox.Enabled = hasTimelineSelection;
        _scaleBox.Enabled = hasTimelineSelection;
        _opacityBox.Enabled = hasTimelineSelection;
        _positionKeyframeButton.Enabled = hasTimelineSelection;
        _scaleKeyframeButton.Enabled = hasTimelineSelection;
        _opacityKeyframeButton.Enabled = hasTimelineSelection;

        if (!hasTimelineSelection)
        {
            _updatingTransformFields = true;
            try
            {
                _positionXBox.Value = 0;
                _positionYBox.Value = 0;
                _scaleBox.Value = 100;
                _opacityBox.Value = 100;
            }
            finally
            {
                _updatingTransformFields = false;
            }

            StyleToolButton(_positionKeyframeButton, false);
            StyleToolButton(_scaleKeyframeButton, false);
            StyleToolButton(_opacityKeyframeButton, false);
            return;
        }

        var segment = _sequenceSegments[selectedIndex];
        var transform = EvaluateTransformAtTime(segment, GetCurrentSequencePlayhead());
        _updatingTransformFields = true;
        try
        {
            _positionXBox.Value = Math.Clamp((decimal)transform.PositionX, _positionXBox.Minimum, _positionXBox.Maximum);
            _positionYBox.Value = Math.Clamp((decimal)transform.PositionY, _positionYBox.Minimum, _positionYBox.Maximum);
            _scaleBox.Value = Math.Clamp((decimal)(transform.Scale * 100f), _scaleBox.Minimum, _scaleBox.Maximum);
            _opacityBox.Value = Math.Clamp((decimal)(transform.Opacity * 100f), _opacityBox.Minimum, _opacityBox.Maximum);
        }
        finally
        {
            _updatingTransformFields = false;
        }

        StyleToolButton(_positionKeyframeButton, segment.SafeTransform.PositionKeyframed);
        StyleToolButton(_scaleKeyframeButton, segment.SafeTransform.ScaleKeyframed);
        StyleToolButton(_opacityKeyframeButton, segment.SafeTransform.OpacityKeyframed);
    }

    private static float Lerp(float start, float end, double ratio)
    {
        return (float)(start + ((end - start) * Math.Clamp(ratio, 0, 1)));
    }

    private ClipTransform EvaluateTransformAtTime(TimelineSegment segment, double sequenceTime)
    {
        var transform = segment.SafeTransform;
        var keyframes = transform.SafeKeyframes.OrderBy(frame => frame.SequenceLocalSec).ToList();
        if (keyframes.Count == 0)
            return transform;

        var localTime = Math.Clamp(sequenceTime - segment.SequenceStartSec, 0, Math.Max(0.001, segment.Duration));
        var previous = keyframes.LastOrDefault(frame => frame.SequenceLocalSec <= localTime) ?? keyframes[0];
        var next = keyframes.FirstOrDefault(frame => frame.SequenceLocalSec >= localTime) ?? keyframes[^1];
        var ratio = Math.Abs(next.SequenceLocalSec - previous.SequenceLocalSec) < 0.0001
            ? 0
            : (localTime - previous.SequenceLocalSec) / (next.SequenceLocalSec - previous.SequenceLocalSec);

        return transform with
        {
            PositionX = transform.PositionKeyframed ? Lerp(previous.PositionX, next.PositionX, ratio) : transform.PositionX,
            PositionY = transform.PositionKeyframed ? Lerp(previous.PositionY, next.PositionY, ratio) : transform.PositionY,
            Scale = transform.ScaleKeyframed ? Lerp(previous.Scale, next.Scale, ratio) : transform.Scale,
            Opacity = transform.OpacityKeyframed ? Lerp(previous.Opacity, next.Opacity, ratio) : transform.Opacity,
        };
    }

    private ClipTransform UpsertKeyframe(TimelineSegment segment, ClipTransform transform)
    {
        var localTime = Math.Clamp(GetCurrentSequencePlayhead() - segment.SequenceStartSec, 0, Math.Max(0.001, segment.Duration));
        var keyframes = transform.SafeKeyframes.ToList();
        var keyframe = new TransformKeyframe(localTime, transform.PositionX, transform.PositionY, transform.Scale, transform.Opacity);
        var existingIndex = keyframes.FindIndex(frame => Math.Abs(frame.SequenceLocalSec - localTime) <= (1d / 30d));
        if (existingIndex >= 0)
            keyframes[existingIndex] = keyframe;
        else
            keyframes.Add(keyframe);

        keyframes = keyframes.OrderBy(frame => frame.SequenceLocalSec).ToList();
        return transform with { Keyframes = keyframes };
    }

    private void SyncTransformFromFields()
    {
        if (_updatingTransformFields)
            return;

        var selectedIndex = _sequenceList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var segment = _sequenceSegments[selectedIndex];
        var transform = segment.SafeTransform with
        {
            PositionX = (float)_positionXBox.Value,
            PositionY = (float)_positionYBox.Value,
            Scale = Math.Max(0.1f, (float)_scaleBox.Value / 100f),
            Opacity = Math.Clamp((float)_opacityBox.Value / 100f, 0f, 1f),
        };

        if (transform.PositionKeyframed || transform.ScaleKeyframed || transform.OpacityKeyframed)
            transform = UpsertKeyframe(segment, transform);

        _sequenceSegments[selectedIndex] = segment with { Transform = transform };
        UpdateSequenceUi(selectedIndex);
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void ToggleKeyframingForSelection(string propertyName)
    {
        var selectedIndex = _sequenceList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _sequenceSegments.Count)
        {
            _statusLabel.Text = "Select a timeline clip first, then enable its stopwatch in the Inspector.";
            return;
        }

        PushSequenceUndoState();
        var segment = _sequenceSegments[selectedIndex];
        var transform = segment.SafeTransform;
        transform = propertyName switch
        {
            "position" => transform with { PositionKeyframed = !transform.PositionKeyframed },
            "scale" => transform with { ScaleKeyframed = !transform.ScaleKeyframed },
            "opacity" => transform with { OpacityKeyframed = !transform.OpacityKeyframed },
            _ => transform,
        };

        if (transform.PositionKeyframed || transform.ScaleKeyframed || transform.OpacityKeyframed)
            transform = UpsertKeyframe(segment, transform);

        _sequenceSegments[selectedIndex] = segment with { Transform = transform };
        UpdateSequenceUi(selectedIndex);
        _statusLabel.Text = $"{propertyName.ToUpperInvariant()} keyframing {(propertyName switch { _ when transform.PositionKeyframed || transform.ScaleKeyframed || transform.OpacityKeyframed => "armed", _ => "updated" })} for the selected clip.";
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void CaptureTransformKeyframeAtPlayhead()
    {
        var selectedIndex = _sequenceList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _sequenceSegments.Count)
        {
            _statusLabel.Text = "Select a timeline clip first, then set a keyframe.";
            return;
        }

        var segment = _sequenceSegments[selectedIndex];
        var transform = segment.SafeTransform;
        if (!transform.PositionKeyframed && !transform.ScaleKeyframed && !transform.OpacityKeyframed)
        {
            _statusLabel.Text = "Enable at least one stopwatch before adding a keyframe.";
            return;
        }

        PushSequenceUndoState();
        transform = UpsertKeyframe(segment, transform);
        _sequenceSegments[selectedIndex] = segment with { Transform = transform };
        UpdateSequenceUi(selectedIndex);
        _statusLabel.Text = $"Keyframe captured at {FormatTime(GetCurrentSequencePlayhead())}.";
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void ResetSelectedMotion()
    {
        var selectedIndex = _sequenceList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var segment = _sequenceSegments[selectedIndex];
        _sequenceSegments[selectedIndex] = segment with { Transform = new ClipTransform() };
        UpdateSequenceUi(selectedIndex);
        _statusLabel.Text = "Reset Position, Scale, and Opacity for the selected clip.";
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private async Task RefreshMediaBinThumbnailsAsync()
    {
        if (IsDisposed)
            return;

        var files = _filesList.Items
            .Cast<object>()
            .Select(item => item?.ToString())
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Cast<string>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedFile) && files.Remove(_selectedFile))
            files.Insert(0, _selectedFile);

        foreach (var file in files.Take(24))
        {
            if (!_mediaThumbCache.ContainsKey(file))
            {
                try
                {
                    using var thumb = await ExtractPreviewImageAsync(file, 0, CancellationToken.None);
                    _mediaThumbCache[file] = new Bitmap(thumb, new Size(192, 108));
                }
                catch
                {
                    var fallback = new Bitmap(192, 108);
                    using var g = Graphics.FromImage(fallback);
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, "VIDEO", new Font("Segoe UI", 8f, FontStyle.Bold), new Rectangle(0, 40, 192, 20), Color.FromArgb(190, 190, 205), TextFormatFlags.HorizontalCenter);
                    _mediaThumbCache[file] = fallback;
                }
            }

            if (!_trimThumbCache.ContainsKey(file))
            {
                var strip = new List<Image>();
                foreach (var sampleTime in new[] { 0d, 5d, 10d })
                {
                    try
                    {
                        using var frame = await ExtractPreviewImageAsync(file, sampleTime, CancellationToken.None);
                        strip.Add(new Bitmap(frame, new Size(96, 54)));
                    }
                    catch
                    {
                    }
                }

                if (strip.Count > 0)
                    _trimThumbCache[file] = strip;
            }
        }

        _filesList.Invalidate();
        _mediaThumbStrip.SuspendLayout();
        try
        {
            foreach (Control control in _mediaThumbStrip.Controls)
                control.Dispose();
            _mediaThumbStrip.Controls.Clear();

            if (files.Count == 0)
            {
                _mediaThumbStrip.Controls.Add(new Label
                {
                    AutoSize = false,
                    Size = new Size(Math.Max(120, _mediaThumbStrip.ClientSize.Width - 12), 72),
                    Text = "IMPORT CLIPS OR LOAD YOUR WATCH FOLDER TO POPULATE THE MEDIA BIN.",
                    ForeColor = Color.FromArgb(150, 150, 160),
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                });
                return;
            }

            var selectedCard = default(Control);
            var gap = 4;
            var tileWidth = Math.Max(108, ((_mediaThumbStrip.ClientSize.Width - 12) / 2) - gap);
            var thumbHeight = Math.Max(60, (int)Math.Round(tileWidth * 9d / 16d));

            foreach (var file in files)
            {
                var isSelected = string.Equals(file, _selectedFile, StringComparison.OrdinalIgnoreCase);
                var card = new Panel
                {
                    Size = new Size(tileWidth, thumbHeight + 28),
                    Margin = new Padding(2),
                    BackColor = Color.FromArgb(17, 17, 17),
                    Tag = file,
                };

                card.Paint += (_, e) =>
                {
                    using var borderPen = new Pen(isSelected ? Color.FromArgb(59, 130, 246) : Color.FromArgb(51, 51, 51), 1);
                    e.Graphics.DrawRectangle(borderPen, 0, 0, card.Width - 1, card.Height - 1);
                };

                var thumbBox = new PictureBox
                {
                    Location = new Point(1, 1),
                    Size = new Size(tileWidth - 2, thumbHeight),
                    BackColor = Color.Black,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Tag = file,
                };

                var nameLabel = new Label
                {
                    Location = new Point(4, thumbHeight + 4),
                    Size = new Size(tileWidth - 8, 18),
                    Text = Path.GetFileName(file),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    AutoEllipsis = true,
                    Tag = file,
                    TextAlign = ContentAlignment.TopCenter,
                };

                void SelectCard(object? _, EventArgs __) => SelectMediaBinFile(file, ModifierKeys);
                card.Click += SelectCard;
                thumbBox.Click += SelectCard;
                nameLabel.Click += SelectCard;

                if (_mediaThumbCache.TryGetValue(file, out var cachedThumb))
                    thumbBox.Image = cachedThumb;

                card.Controls.Add(thumbBox);
                card.Controls.Add(nameLabel);
                _mediaThumbStrip.Controls.Add(card);

                if (isSelected)
                    selectedCard = card;
            }

            if (selectedCard != null)
                _mediaThumbStrip.ScrollControlIntoView(selectedCard);
        }
        finally
        {
            _mediaThumbStrip.ResumeLayout();
        }
    }

    private void OnMediaOpened()
    {
        if (_mediaElement.NaturalDuration.HasTimeSpan)
        {
            _videoDuration = Math.Max(_videoDuration, _mediaElement.NaturalDuration.TimeSpan.TotalSeconds);
        }

        _playerStatusLabel.Text = "Paused — drag the timeline or hit Play.";
        _playPauseButton.Text = "▶ Play";
        _isPlaying = false;
        UpdatePlayerPositionFromPlayback();
    }

    private void TogglePlayback()
    {
        if (_mediaElement.Source == null)
        {
            _statusLabel.Text = "Select a clip first.";
            return;
        }

        if (_transportDirection != 0 || _isPlaying)
        {
            StopPlayback(resetToStart: false);
            return;
        }

        _transportDirection = 1;
        _transportSpeedLevel = 1;
        StartTransportPlayback();
    }

    private void HandleTransportKey(Keys keyCode)
    {
        if (_mediaElement.Source == null)
        {
            _statusLabel.Text = "Select a clip first before using J/K/L transport.";
            return;
        }

        switch (keyCode)
        {
            case Keys.K:
                StopPlayback(resetToStart: false);
                return;
            case Keys.L:
                if (_transportDirection == 1)
                    _transportSpeedLevel = Math.Min(3, _transportSpeedLevel + 1);
                else
                {
                    _transportDirection = 1;
                    _transportSpeedLevel = 1;
                }
                StartTransportPlayback();
                return;
            case Keys.J:
                if (_transportDirection == -1)
                    _transportSpeedLevel = Math.Min(3, _transportSpeedLevel + 1);
                else
                {
                    _transportDirection = -1;
                    _transportSpeedLevel = 1;
                }
                StartTransportPlayback();
                return;
        }
    }

    private void StartTransportPlayback()
    {
        var speed = _transportSpeedLevel switch
        {
            >= 3 => 4.0,
            2 => 2.0,
            _ => 1.0,
        };

        try
        {
            if (_transportDirection >= 0)
            {
                _mediaElement.SpeedRatio = speed;
                _mediaElement.Play();
                _isPlaying = true;
                _playPauseButton.Text = speed > 1 ? $"▶ {speed:0}x" : "❚❚ Pause";
                _playerStatusLabel.Text = speed > 1 ? $"Playing forward {speed:0}x (L to ramp, K to stop)." : "Playing…";
            }
            else
            {
                _mediaElement.Pause();
                _isPlaying = false;
                _playPauseButton.Text = $"◀ {speed:0}x";
                _playerStatusLabel.Text = $"Playing backward {speed:0}x (J to ramp, K to stop).";
            }

            _playerTimer.Start();
        }
        catch (Exception ex)
        {
            _playerStatusLabel.Text = $"Playback failed: {ex.Message}";
            _statusLabel.Text = _playerStatusLabel.Text;
        }
    }

    private void StopPlayback(bool resetToStart)
    {
        try
        {
            _mediaElement.SpeedRatio = 1.0;
            _mediaElement.Pause();
        }
        catch
        {
        }

        if (resetToStart)
        {
            try
            {
                _mediaElement.Position = TimeSpan.Zero;
            }
            catch
            {
            }
        }

        _transportDirection = 0;
        _transportSpeedLevel = 0;
        _isPlaying = false;
        _playerTimer.Stop();
        _playPauseButton.Text = "▶ Play";
        _playerStatusLabel.Text = _mediaElement.Source == null
            ? "Load a clip to start playback."
            : "Paused — drag the timeline or hit Play.";

        if (resetToStart)
        {
            _timelineUpdateFromPlayer = true;
            _timelineBar.Value = 0;
            _timelineUpdateFromPlayer = false;
            _previewTimeLabel.Text = "Playhead: 00:00.000";
            _trimTimelineView.SetPlayhead(0);
            _sequenceTimelineView.SetPlayhead(0);
        }
    }

    private void StepTransportPlayback()
    {
        if (_transportDirection < 0)
        {
            var speed = _transportSpeedLevel switch
            {
                >= 3 => 4.0,
                2 => 2.0,
                _ => 1.0,
            };
            var target = GetCurrentPreviewTime() - (0.12 * speed);
            if (target <= 0.01)
            {
                SeekToTime(0, refreshPreview: true);
                StopPlayback(resetToStart: false);
                return;
            }

            SeekToTime(target, refreshPreview: true);
            return;
        }

        if (_transportDirection > 0)
            UpdatePlayerPositionFromPlayback();
    }

    private void SkipSeconds(double deltaSeconds)
    {
        var target = GetCurrentPreviewTime() + deltaSeconds;
        SeekToTime(target, refreshPreview: true);
    }

    private void SeekToTime(double seconds, bool refreshPreview)
    {
        var clamped = Math.Clamp(seconds, 0, Math.Max(_videoDuration, 0));

        try
        {
            if (_mediaElement.Source != null)
                _mediaElement.Position = TimeSpan.FromSeconds(clamped);
        }
        catch
        {
        }

        _timelineUpdateFromPlayer = true;
        _timelineBar.Value = _videoDuration <= 0
            ? 0
            : Math.Clamp((int)Math.Round((clamped / _videoDuration) * _timelineBar.Maximum), 0, _timelineBar.Maximum);
        _timelineUpdateFromPlayer = false;
        _previewTimeLabel.Text = $"Playhead: {FormatTime(clamped)}";
        _trimTimelineView.SetPlayhead(clamped);
        _sequenceTimelineView.SetPlayhead(clamped);
        UpdateTransformInspectorUi();

        if (refreshPreview)
        {
            _requestedPreviewTime = clamped;
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }
    }

    private void UpdatePlayerPositionFromPlayback()
    {
        if (_mediaElement.Source == null)
            return;

        try
        {
            var position = _mediaElement.Position.TotalSeconds;
            _timelineUpdateFromPlayer = true;
            _timelineBar.Value = _videoDuration <= 0
                ? 0
                : Math.Clamp((int)Math.Round((position / Math.Max(_videoDuration, 0.001)) * _timelineBar.Maximum), 0, _timelineBar.Maximum);
            _timelineUpdateFromPlayer = false;
            _previewTimeLabel.Text = $"Playhead: {FormatTime(position)}";
            _trimTimelineView.SetPlayhead(position);
            _sequenceTimelineView.SetPlayhead(position);
            UpdateTransformInspectorUi();
        }
        catch
        {
        }
    }

    private void LoadExistingClipsFromFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _statusLabel.Text = $"Watch/output folder not found: {folder}";
            return;
        }

        var files = Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(file => File.GetLastWriteTimeUtc(file))
            .Take(120)
            .ToList();

        var added = AddMediaFiles(files);

        _statusLabel.Text = files.Count == 0
            ? "No videos were found in the watch folder yet."
            : $"Loaded {added} recent clip(s) from the watch folder.";
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*",
            Title = "Import clips into the media bin",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var added = AddMediaFiles(dialog.FileNames);
        _statusLabel.Text = added == 0
            ? "Those clips were already imported."
            : $"Imported {added} clip(s) into the media bin.";
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private void ImportFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder of clips to import into the media bin",
            SelectedPath = Directory.Exists(_outputFolderBox.Text)
                ? _outputFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var files = Directory
            .EnumerateFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(200)
            .ToList();

        var added = AddMediaFiles(files);
        _statusLabel.Text = files.Count == 0
            ? "That folder does not contain supported video files."
            : $"Imported {added} clip(s) from {Path.GetFileName(dialog.SelectedPath)}.";
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private void RemoveSelected()
    {
        var selected = _filesList.SelectedItems.Cast<object>().ToList();
        foreach (var item in selected)
            _filesList.Items.Remove(item);
        _statusLabel.Text = $"Loaded {_filesList.Items.Count} clip(s).";
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private void MoveSelected(int direction)
    {
        if (_filesList.SelectedItem is null)
            return;

        var index = _filesList.SelectedIndex;
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _filesList.Items.Count)
            return;

        var item = _filesList.Items[index];
        _filesList.Items.RemoveAt(index);
        _filesList.Items.Insert(newIndex, item);
        _filesList.SelectedIndex = newIndex;
        _ = RefreshMediaBinThumbnailsAsync();
    }

    private async Task HandleSelectionChangedAsync()
    {
        if (_filesList.SelectedItems.Count != 1)
        {
            _selectedFile = null;
            StopPlayback(resetToStart: true);
            _mediaElement.Source = null;
            _videoInfoLabel.Text = _filesList.SelectedItems.Count > 1
                ? "Multiple clips selected — you can merge them, but preview/edit works on one clip at a time."
                : "Select one clip to preview and edit.";
            _trimTimelineView.SetTimeline(0, 0, 0, 0, _filesList.SelectedItems.Count > 1 ? "Multi-select" : "No clip selected");
            _trimTimelineView.SetThumbnails(Array.Empty<Image>());
            _trimTimelineView.SetWaveform(null);
            UpdateMarkerUi();
            UpdateInspectorModeUi();
            _ = RefreshMediaBinThumbnailsAsync();
            _sourcePreview.ClearPreview();
            ReplacePicture(_outputPreview, null);
            return;
        }

        _selectedFile = _filesList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
        {
            _selectedFile = null;
            StopPlayback(resetToStart: true);
            _mediaElement.Source = null;
            _videoInfoLabel.Text = "The selected file could not be found.";
            _trimTimelineView.SetTimeline(0, 0, 0, 0, "Missing clip");
            _trimTimelineView.SetThumbnails(Array.Empty<Image>());
            _trimTimelineView.SetWaveform(null);
            UpdateMarkerUi();
            UpdateInspectorModeUi();
            _ = RefreshMediaBinThumbnailsAsync();
            _sourcePreview.ClearPreview();
            ReplacePicture(_outputPreview, null);
            return;
        }

        _statusLabel.Text = $"Loading preview for {Path.GetFileName(_selectedFile)}...";

        try
        {
            var details = await GetVideoDetailsAsync(_selectedFile, CancellationToken.None);
            _videoDuration = details.Duration;
            _videoSize = new Size(details.Width, details.Height);
            _videoInfoLabel.Text = $"{Path.GetFileName(_selectedFile)} • {details.Width}×{details.Height} • {FormatTime(details.Duration)}";

            var durationDecimal = (decimal)Math.Max(details.Duration, 0.25);
            _startBox.Maximum = durationDecimal;
            _endBox.Maximum = durationDecimal;
            _startBox.Value = Math.Min(_startBox.Value, durationDecimal);
            _endBox.Value = Math.Min(durationDecimal, Math.Max((decimal)1, _endBox.Value <= _startBox.Value ? _startBox.Value + 1 : _endBox.Value));

            _cropXBox.Maximum = Math.Max(1, details.Width);
            _cropYBox.Maximum = Math.Max(1, details.Height);
            _cropWBox.Maximum = Math.Max(1, details.Width);
            _cropHBox.Maximum = Math.Max(1, details.Height);

            StopPlayback(resetToStart: true);
            _mediaElement.Source = new Uri(_selectedFile);
            _playerStatusLabel.Text = "Loading video…";

            _timelineBar.Value = 0;
            UpdateTrimTimelineUi();
            UpdateMarkerUi();
            UpdateInspectorModeUi();
            _ = RefreshMediaBinThumbnailsAsync();
            await RefreshTrimTimelineFramesAsync();
            await RefreshWaveformAsync();
            ResetCropToFullFrame();
            await RefreshPreviewAsync(0);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load video preview info", ex);
            _videoInfoLabel.Text = $"Preview unavailable: {ex.Message}";
            _statusLabel.Text = $"Could not load the selected clip: {ex.Message}";
        }
    }

    private async Task<VideoDetails> GetVideoDetailsAsync(string input, CancellationToken ct)
    {
        var ffprobePath = FFmpegHelper.GetFFprobePath() ?? "ffprobe";
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height:format=duration -of default=noprint_wrappers=1:nokey=1 {Quote(input)}";
        var psi = new ProcessStartInfo(ffprobePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffprobe could not be started.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "ffprobe failed." : stderr[^Math.Min(stderr.Length, 400)..]);

        var lines = stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 3)
            throw new InvalidOperationException("ffprobe did not return video size information.");

        var width = int.TryParse(lines[0], out var parsedWidth) ? parsedWidth : 1920;
        var height = int.TryParse(lines[1], out var parsedHeight) ? parsedHeight : 1080;
        var duration = double.TryParse(lines[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDuration) ? parsedDuration : 0;
        return new VideoDetails(duration, width, height);
    }

    private void QueuePreviewRefreshFromSlider()
    {
        _requestedPreviewTime = GetCurrentPreviewTime();
        _previewTimeLabel.Text = $"Playhead: {FormatTime(_requestedPreviewTime)}";

        if (!_timelineUpdateFromPlayer)
        {
            SeekToTime(_requestedPreviewTime, refreshPreview: false);
        }

        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private double GetCurrentPreviewTime()
    {
        if (_videoDuration <= 0)
            return 0;

        return (_timelineBar.Value / 1000d) * _videoDuration;
    }

    private async Task RefreshPreviewAsync(double time)
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
            return;

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var image = await ExtractPreviewImageAsync(_selectedFile, time, ct);
            if (ct.IsCancellationRequested)
            {
                image.Dispose();
                return;
            }

            _sourcePreview.VideoSize = _videoSize;
            _sourcePreview.SetPreviewImage(image);
            _sourcePreview.ShowCropOverlay = _enableCropBox.Checked;
            _previewTimeLabel.Text = $"Playhead: {FormatTime(time)}";
            _trimTimelineView.SetPlayhead(time);
            _sequenceTimelineView.SetPlayhead(time);
            _statusLabel.Text = $"Preview updated for {Path.GetFileName(_selectedFile)} at {FormatTime(time)}.";

            await RefreshOutputPreviewAsync(time, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to refresh preview", ex);
            _statusLabel.Text = $"Preview failed: {ex.Message}";
        }
    }

    private async Task<Image> ExtractPreviewImageAsync(string filePath, double time, CancellationToken ct, Rectangle? crop = null)
    {
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        var tempFile = Path.Combine(Path.GetTempPath(), $"velo-preview-{Guid.NewGuid():N}.jpg");
        var clampedTime = Math.Max(0, time);
        var filter = crop is { Width: > 0, Height: > 0 }
            ? $"-vf \"crop={crop.Value.Width}:{crop.Value.Height}:{crop.Value.X}:{crop.Value.Y},scale=960:-1\""
            : "-vf \"scale=960:-1\"";

        var args = $"-ss {clampedTime.ToString(CultureInfo.InvariantCulture)} -i {Quote(filePath)} -frames:v 1 {filter} -q:v 2 -y {Quote(tempFile)}";
        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg could not be started.");
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0 || !File.Exists(tempFile))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "FFmpeg could not render the preview frame." : stderr[^Math.Min(stderr.Length, 400)..]);

            using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(fs);
            return new Bitmap(temp);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private async Task RefreshOutputPreviewAsync(double time, CancellationToken ct)
    {
        if (_sequenceSegments.Count > 0)
        {
            var sequenceTime = GetCurrentSequencePlayhead();
            var activeSegments = GetActiveSegmentsAtTime(sequenceTime);
            if (activeSegments.Count > 0)
            {
                var composite = await RenderCompositePreviewAsync(activeSegments, sequenceTime, ct);
                if (!ct.IsCancellationRequested)
                    ReplacePicture(_outputPreview, composite);
                else
                    composite.Dispose();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
        {
            ReplacePicture(_outputPreview, null);
            return;
        }

        Image baseFrame;
        if (_enableCropBox.Checked)
        {
            var cropRect = GetCropRectangle();
            baseFrame = cropRect.Width >= 2 && cropRect.Height >= 2
                ? await ExtractPreviewImageAsync(_selectedFile, time, ct, cropRect)
                : (_sourcePreview.ClonePreviewImage() ?? await ExtractPreviewImageAsync(_selectedFile, time, ct));
        }
        else
        {
            baseFrame = _sourcePreview.ClonePreviewImage() ?? await ExtractPreviewImageAsync(_selectedFile, time, ct);
        }

        var selectedIndex = _sequenceList.SelectedIndex;
        var transform = selectedIndex >= 0 && selectedIndex < _sequenceSegments.Count
            ? EvaluateTransformAtTime(_sequenceSegments[selectedIndex], GetCurrentSequencePlayhead())
            : new ClipTransform();
        var transformed = ApplyTransformToCanvas(baseFrame, transform);
        baseFrame.Dispose();

        if (!ct.IsCancellationRequested)
            ReplacePicture(_outputPreview, transformed);
        else
            transformed.Dispose();
    }

    private async Task<Image> RenderCompositePreviewAsync(IReadOnlyList<TimelineSegment> activeSegments, double sequenceTime, CancellationToken ct)
    {
        var canvas = new Bitmap(960, 540);
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.FromArgb(10, 10, 12));
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        foreach (var segment in activeSegments.OrderBy(segment => segment.SafeTrack))
        {
            var clipTime = Math.Clamp(segment.StartSec + Math.Max(0, sequenceTime - segment.SequenceStartSec), segment.StartSec, segment.EndSec);
            Rectangle? crop = null;
            if (_enableCropBox.Checked && string.Equals(segment.SourceFile, _selectedFile, StringComparison.OrdinalIgnoreCase))
            {
                var cropRect = GetCropRectangle();
                if (cropRect.Width >= 2 && cropRect.Height >= 2)
                    crop = cropRect;
            }

            using var frame = await ExtractPreviewImageAsync(segment.SourceFile, clipTime, ct, crop);
            using var transformed = ApplyTransformToCanvas(frame, EvaluateTransformAtTime(segment, sequenceTime), canvas.Size);
            graphics.DrawImage(transformed, 0, 0, canvas.Width, canvas.Height);
        }

        return canvas;
    }

    private Bitmap ApplyTransformToCanvas(Image source, ClipTransform transform, Size? canvasSize = null)
    {
        var targetSize = canvasSize ?? source.Size;
        var canvas = new Bitmap(Math.Max(1, targetSize.Width), Math.Max(1, targetSize.Height));
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        var drawWidth = Math.Max(1, (int)Math.Round(source.Width * Math.Max(0.1f, transform.Scale)));
        var drawHeight = Math.Max(1, (int)Math.Round(source.Height * Math.Max(0.1f, transform.Scale)));
        var drawX = ((canvas.Width - drawWidth) / 2f) + transform.PositionX;
        var drawY = ((canvas.Height - drawHeight) / 2f) + transform.PositionY;
        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(transform.Opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        graphics.DrawImage(source, new Rectangle((int)Math.Round(drawX), (int)Math.Round(drawY), drawWidth, drawHeight), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return canvas;
    }

    private void SetTrimBoundary(bool isStart)
    {
        var current = (decimal)SnapToFrame(GetCurrentPreviewTime());
        if (isStart)
        {
            _startBox.Value = Math.Min(_endBox.Value, current);
        }
        else
        {
            _endBox.Value = Math.Max(_startBox.Value, current);
        }

        UpdateTrimTimelineUi();
    }

    private void OnCropSelectionChanged(Rectangle cropRect)
    {
        if (_updatingCropFields)
            return;

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = cropRect.X;
            _cropYBox.Value = cropRect.Y;
            _cropWBox.Value = Math.Max(1, cropRect.Width);
            _cropHBox.Value = Math.Max(1, cropRect.Height);
        }
        finally
        {
            _updatingCropFields = false;
        }

        UpdateCropInfoLabel(cropRect);
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void SyncCropPreviewFromFields()
    {
        if (_updatingCropFields)
            return;

        var cropRect = GetCropRectangle();
        _sourcePreview.SetCropRect(cropRect);
        UpdateCropInfoLabel(cropRect);
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private Rectangle GetCropRectangle()
    {
        var maxWidth = Math.Max(1, _videoSize.Width);
        var maxHeight = Math.Max(1, _videoSize.Height);
        var x = Math.Clamp((int)Math.Round(_cropXBox.Value), 0, maxWidth - 1);
        var y = Math.Clamp((int)Math.Round(_cropYBox.Value), 0, maxHeight - 1);
        var width = Math.Clamp((int)Math.Round(_cropWBox.Value), 1, maxWidth - x);
        var height = Math.Clamp((int)Math.Round(_cropHBox.Value), 1, maxHeight - y);
        return new Rectangle(x, y, width, height);
    }

    private void ApplyAspectCrop(int aspectWidth, int aspectHeight)
    {
        if (_videoSize.Width <= 0 || _videoSize.Height <= 0)
            return;

        var targetRatio = aspectWidth / (double)Math.Max(1, aspectHeight);
        var width = _videoSize.Width;
        var height = (int)Math.Round(width / targetRatio);

        if (height > _videoSize.Height)
        {
            height = _videoSize.Height;
            width = (int)Math.Round(height * targetRatio);
        }

        var x = Math.Max(0, (_videoSize.Width - width) / 2);
        var y = Math.Max(0, (_videoSize.Height - height) / 2);

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = x;
            _cropYBox.Value = y;
            _cropWBox.Value = Math.Max(1, width);
            _cropHBox.Value = Math.Max(1, height);
        }
        finally
        {
            _updatingCropFields = false;
        }

        var rect = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
        _sourcePreview.SetCropRect(rect);
        UpdateCropInfoLabel(rect);
        _statusLabel.Text = $"Applied {aspectWidth}:{aspectHeight} crop guide.";
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void CenterCropSelection()
    {
        if (_videoSize.Width <= 0 || _videoSize.Height <= 0)
            return;

        var crop = GetCropRectangle();
        var centered = new Rectangle(
            Math.Max(0, (_videoSize.Width - crop.Width) / 2),
            Math.Max(0, (_videoSize.Height - crop.Height) / 2),
            crop.Width,
            crop.Height);

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = centered.X;
            _cropYBox.Value = centered.Y;
            _cropWBox.Value = centered.Width;
            _cropHBox.Value = centered.Height;
        }
        finally
        {
            _updatingCropFields = false;
        }

        _sourcePreview.SetCropRect(centered);
        UpdateCropInfoLabel(centered);
        _statusLabel.Text = "Centered the crop selection.";
        _ = RefreshPreviewAsync(GetCurrentPreviewTime());
    }

    private void UpdateCropInfoLabel(Rectangle cropRect)
    {
        if (_videoSize.Width <= 0 || _videoSize.Height <= 0 || cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            _cropInfoLabel.Text = "Crop: full frame";
            return;
        }

        var coverage = (cropRect.Width * cropRect.Height * 100d) / Math.Max(1d, _videoSize.Width * _videoSize.Height);
        _cropInfoLabel.Text = $"Crop: {cropRect.Width}×{cropRect.Height} • {DescribeAspect(cropRect.Width, cropRect.Height)} • {coverage:F0}% frame";
    }

    private static string DescribeAspect(int width, int height)
    {
        if (height <= 0)
            return "freeform";

        var ratio = width / (double)height;
        if (Math.Abs(ratio - (16d / 9d)) < 0.03) return "16:9";
        if (Math.Abs(ratio - (9d / 16d)) < 0.03) return "9:16";
        if (Math.Abs(ratio - 1d) < 0.03) return "1:1";
        if (Math.Abs(ratio - (4d / 5d)) < 0.03) return "4:5";
        return $"{ratio:F2}:1";
    }

    private void ResetCropToFullFrame()
    {
        if (_videoSize.Width <= 0 || _videoSize.Height <= 0)
            return;

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = 0;
            _cropYBox.Value = 0;
            _cropWBox.Value = _videoSize.Width;
            _cropHBox.Value = _videoSize.Height;
        }
        finally
        {
            _updatingCropFields = false;
        }

        var fullRect = new Rectangle(0, 0, _videoSize.Width, _videoSize.Height);
        _sourcePreview.SetCropRect(fullRect);
        UpdateCropInfoLabel(fullRect);
    }

    private void SyncTrimRangeFromFields()
    {
        if (_updatingTrimRange)
            return;

        if (_endBox.Value < _startBox.Value)
        {
            _updatingTrimRange = true;
            try
            {
                _endBox.Value = _startBox.Value;
            }
            finally
            {
                _updatingTrimRange = false;
            }
        }

        UpdateTrimTimelineUi();
    }

    private void ApplyTrimRangeFromTimeline(double start, double end)
    {
        if (_updatingTrimRange)
            return;

        var snappedStart = SnapToFrame(start);
        var snappedEnd = Math.Max(snappedStart + (1d / 30d), SnapToFrame(end));
        var max = (decimal)Math.Max(_videoDuration, 0.25);
        _updatingTrimRange = true;
        try
        {
            _startBox.Value = Math.Clamp((decimal)snappedStart, _startBox.Minimum, max);
            _endBox.Value = Math.Clamp((decimal)snappedEnd, _endBox.Minimum, max);
        }
        finally
        {
            _updatingTrimRange = false;
        }

        UpdateTrimTimelineUi();
    }

    private void UpdateTrimTimelineUi()
    {
        var label = string.IsNullOrWhiteSpace(_selectedFile) ? "No clip selected" : Path.GetFileName(_selectedFile);
        _trimTimelineView.SetTimeline(_videoDuration, (double)_startBox.Value, (double)_endBox.Value, GetCurrentPreviewTime(), label);
        _trimTimelineView.SetMarkers(GetCurrentClipMarkers());
    }

    private List<double> GetCurrentClipMarkers()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile))
            return [];

        return _clipMarkers.TryGetValue(_selectedFile, out var markers)
            ? markers
            : [];
    }

    private void UpdateMarkerUi()
    {
        _markerList.BeginUpdate();
        _markerList.Items.Clear();
        var markers = GetCurrentClipMarkers();
        for (var index = 0; index < markers.Count; index++)
            _markerList.Items.Add($"Marker {index + 1} • {FormatTime(markers[index])}");
        _markerList.EndUpdate();

        var hasClip = !string.IsNullOrWhiteSpace(_selectedFile);
        _addMarkerButton.Enabled = hasClip;
        _addCutButton.Enabled = hasClip;
        _overwriteCutButton.Enabled = hasClip;
        _splitPlayheadButton.Enabled = _sequenceList.SelectedIndex >= 0;
        _trimTimelineView.SetMarkers(markers);
    }

    private void UpdateTimelineZoom()
    {
        _timelineZoom = Math.Clamp(_timelineZoom, 1, 6);
        _trimTimelineView.SetZoom(_timelineZoom);
        _sequenceTimelineView.SetZoom(_timelineZoom);
        _trimTimelineView.SetSnappingEnabled(_snappingEnabled);
        _sequenceTimelineView.SetSnappingEnabled(_snappingEnabled);
    }

    private void AdjustTimelineZoom(double delta)
    {
        _timelineZoom = Math.Clamp(_timelineZoom + delta, 1, 6);
        UpdateTimelineZoom();
    }

    private void SetTimelineEditMode(TimelineEditMode mode)
    {
        _timelineEditMode = mode;
        var razorActive = mode == TimelineEditMode.Razor;
        var rippleActive = mode == TimelineEditMode.Ripple;
        var rollingActive = mode == TimelineEditMode.Rolling;
        var slipActive = mode == TimelineEditMode.Slip;
        _sequenceTimelineView.SetRazorMode(razorActive);
        _sequenceTimelineView.SetRippleMode(rippleActive);
        _sequenceTimelineView.SetRollingMode(rollingActive);
        _sequenceTimelineView.SetSlipMode(slipActive);
        StyleToolButton(_selectToolButton, mode == TimelineEditMode.Select);
        StyleToolButton(_rippleToolButton, rippleActive);
        StyleToolButton(_rollingToolButton, rollingActive);
        StyleToolButton(_slipToolButton, slipActive);
        StyleToolButton(_razorToolButton, razorActive);
        _timelineModeLabel.Text = mode switch
        {
            TimelineEditMode.Razor => "RAZOR TOOL ACTIVE — CLICK ANY TIMELINE CUT TO SLICE IT INSTANTLY.",
            TimelineEditMode.Ripple => "RIPPLE TOOL ACTIVE — TRIM OR MOVE AND EVERYTHING DOWNSTREAM CLOSES THE GAP.",
            TimelineEditMode.Rolling => "ROLLING TOOL ACTIVE — DRAG A CUT EDGE TO SHORTEN ONE CLIP AND LENGTHEN ITS NEIGHBOR.",
            TimelineEditMode.Slip => "SLIP TOOL ACTIVE — SLIDE THE CONTENT INSIDE A CLIP WITHOUT MOVING ITS TIMELINE POSITION.",
            _ => "SELECTION TOOL ACTIVE — DRAG CLIPS TO OVERWRITE OR SWAP IN PLACE.",
        };
        _statusLabel.Text = _timelineModeLabel.Text;
    }

    private void NudgeNearestTrimHandle(double deltaSeconds)
    {
        if (_sequenceList.SelectedIndex >= 0 && _sequenceList.SelectedIndex < _sequenceSegments.Count)
        {
            var index = _sequenceList.SelectedIndex;
            var segment = _sequenceSegments[index];
            var playhead = GetCurrentPreviewTime();
            var adjustStart = Math.Abs(playhead - segment.StartSec) <= Math.Abs(playhead - segment.EndSec);
            var newStart = segment.StartSec;
            var newEnd = segment.EndSec;

            if (adjustStart)
                newStart = Math.Clamp(segment.StartSec + deltaSeconds, 0, segment.EndSec - 0.05);
            else
                newEnd = Math.Max(segment.StartSec + 0.05, segment.EndSec + deltaSeconds);

            PushSequenceUndoState();
            _sequenceSegments[index] = segment with { StartSec = newStart, EndSec = newEnd };
            UpdateSequenceUi(index);

            if (string.Equals(_selectedFile, segment.SourceFile, StringComparison.OrdinalIgnoreCase))
            {
                _updatingTrimRange = true;
                try
                {
                    _startBox.Value = Math.Clamp((decimal)newStart, _startBox.Minimum, _startBox.Maximum);
                    _endBox.Value = Math.Clamp((decimal)newEnd, _endBox.Minimum, _endBox.Maximum);
                }
                finally
                {
                    _updatingTrimRange = false;
                }
                UpdateTrimTimelineUi();
            }

            _statusLabel.Text = adjustStart
                ? $"Nudged the IN point to {FormatTime(newStart)}."
                : $"Nudged the OUT point to {FormatTime(newEnd)}.";
            return;
        }

        var current = GetCurrentPreviewTime();
        var adjustSourceStart = Math.Abs(current - (double)_startBox.Value) <= Math.Abs(current - (double)_endBox.Value);
        _updatingTrimRange = true;
        try
        {
            if (adjustSourceStart)
                _startBox.Value = Math.Clamp(_startBox.Value + (decimal)deltaSeconds, _startBox.Minimum, _endBox.Value - 0.05M);
            else
                _endBox.Value = Math.Clamp(_endBox.Value + (decimal)deltaSeconds, _startBox.Value + 0.05M, _endBox.Maximum);
        }
        finally
        {
            _updatingTrimRange = false;
        }

        UpdateTrimTimelineUi();
        _statusLabel.Text = adjustSourceStart ? "Nudged the source IN point." : "Nudged the source OUT point.";
    }

    private async Task RefreshTrimTimelineFramesAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile) || _videoDuration <= 0)
        {
            _trimTimelineView.SetThumbnails(Array.Empty<Image>());
            return;
        }

        if (_trimThumbCache.TryGetValue(_selectedFile, out var cachedThumbs) && cachedThumbs.Count > 0)
        {
            _trimTimelineView.SetThumbnails(cachedThumbs);
            return;
        }

        var thumbs = new List<Image>();
        var samples = 6;
        for (var i = 0; i < samples; i++)
        {
            try
            {
                var time = _videoDuration <= 0.25 ? 0 : (_videoDuration * i) / Math.Max(1, samples - 1);
                using var frame = await ExtractPreviewImageAsync(_selectedFile, time, CancellationToken.None);
                thumbs.Add(new Bitmap(frame, new Size(112, 34)));
            }
            catch
            {
                break;
            }
        }

        if (thumbs.Count == 0)
        {
            var fallback = new Bitmap(112, 34);
            using var g = Graphics.FromImage(fallback);
            g.Clear(Color.FromArgb(24, 24, 30));
            TextRenderer.DrawText(g, "CLIP", new Font("Segoe UI", 8f, FontStyle.Bold), new Rectangle(0, 8, 112, 18), Color.FromArgb(210, 210, 220), TextFormatFlags.HorizontalCenter);
            thumbs.Add(fallback);
        }

        _trimThumbCache[_selectedFile] = thumbs;
        _trimTimelineView.SetThumbnails(thumbs);
    }

    private async Task RefreshWaveformAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
        {
            _trimTimelineView.SetWaveform(null);
            _sequenceTimelineView.Invalidate();
            return;
        }

        if (!_waveformCache.TryGetValue(_selectedFile, out var waveform))
        {
            waveform = await ExtractWaveformImageAsync(_selectedFile, CancellationToken.None);
            _waveformCache[_selectedFile] = waveform;
        }

        _trimTimelineView.SetWaveform(waveform);
        _sequenceTimelineView.Invalidate();
    }

    private async Task<Image> ExtractWaveformImageAsync(string filePath, CancellationToken ct)
    {
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        var tempFile = Path.Combine(Path.GetTempPath(), $"velo-wave-{Guid.NewGuid():N}.png");
        var args = $"-i {Quote(filePath)} -filter_complex \"aformat=channel_layouts=mono,showwavespic=s=2400x180:colors=0x7DD3FC\" -frames:v 1 -y {Quote(tempFile)}";
        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("FFmpeg could not be started.");

            var stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode == 0 && File.Exists(tempFile))
            {
                using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var temp = Image.FromStream(fs);
                return new Bitmap(temp);
            }
        }
        catch
        {
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }

        var fallback = new Bitmap(2400, 180);
        using (var g = Graphics.FromImage(fallback))
        {
            g.Clear(Color.Black);
            using var pen = new Pen(Color.FromArgb(125, 211, 252), 1.4f);
            var mid = fallback.Height / 2;
            for (var x = 0; x < fallback.Width; x += 3)
            {
                var height = 10 + (int)(Math.Abs(Math.Sin(x / 32d)) * 62);
                g.DrawLine(pen, x, mid - height, x, mid + height);
            }
        }
        return fallback;
    }

    private async Task ScrubSequenceToTimeAsync(double sequenceSeconds)
    {
        if (_sequenceSegments.Count == 0)
        {
            SeekToTime(sequenceSeconds, refreshPreview: true);
            return;
        }

        var totalDuration = GetSequenceTotalDuration();
        var clampedSequenceTime = Math.Clamp(sequenceSeconds, 0, Math.Max(0.001, totalDuration));
        var segment = GetTopVisibleSegmentAtTime(clampedSequenceTime)
            ?? _sequenceSegments.OrderBy(segment => segment.SequenceStartSec).LastOrDefault();
        if (segment is null)
            return;

        var index = _sequenceSegments.FindIndex(item => item == segment);
        var localOffset = Math.Clamp(clampedSequenceTime - segment.SequenceStartSec, 0, Math.Max(0.001, segment.Duration));
        var clipTime = Math.Clamp(segment.StartSec + localOffset, segment.StartSec, segment.EndSec);

        if (_sequenceList.SelectedIndex != index)
            _sequenceList.SelectedIndex = index;
        _sequenceTimelineView.SetSelectedIndex(index);

        var clipNeedsLoad = !string.Equals(_selectedFile, segment.SourceFile, StringComparison.OrdinalIgnoreCase) || _mediaElement.Source == null;
        if (clipNeedsLoad)
        {
            var existingIndex = _filesList.Items.IndexOf(segment.SourceFile);
            if (existingIndex < 0)
            {
                _filesList.Items.Insert(0, segment.SourceFile);
                existingIndex = 0;
            }

            if (_filesList.SelectedIndex != existingIndex || !string.Equals(_filesList.SelectedItem?.ToString(), segment.SourceFile, StringComparison.OrdinalIgnoreCase))
            {
                _filesList.ClearSelected();
                _filesList.SelectedIndex = existingIndex;
                await HandleSelectionChangedAsync();
            }
        }

        _updatingTrimRange = true;
        try
        {
            _startBox.Value = Math.Clamp((decimal)segment.StartSec, _startBox.Minimum, _startBox.Maximum);
            _endBox.Value = Math.Clamp((decimal)segment.EndSec, _endBox.Minimum, _endBox.Maximum);
        }
        finally
        {
            _updatingTrimRange = false;
        }

        UpdateTrimTimelineUi();
        SeekToTime(clipTime, refreshPreview: true);
    }

    private void AddMarkerAtPlayhead()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile))
        {
            MessageBox.Show(this, "Select one clip before creating a marker.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_clipMarkers.TryGetValue(_selectedFile, out var markers))
        {
            markers = [];
            _clipMarkers[_selectedFile] = markers;
        }

        var current = Math.Round(GetCurrentPreviewTime(), 3);
        if (!markers.Any(value => Math.Abs(value - current) < 0.05))
            markers.Add(current);
        markers.Sort();
        UpdateMarkerUi();
        _statusLabel.Text = $"Added marker at {FormatTime(current)} for {Path.GetFileName(_selectedFile)}.";
    }

    private void RemoveSelectedMarker()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !_clipMarkers.TryGetValue(_selectedFile, out var markers))
            return;

        var index = _markerList.SelectedIndex;
        if (index < 0 || index >= markers.Count)
            return;

        markers.RemoveAt(index);
        UpdateMarkerUi();
        _statusLabel.Text = "Marker removed.";
    }

    private void JumpToSelectedMarker()
    {
        var index = _markerList.SelectedIndex;
        var markers = GetCurrentClipMarkers();
        if (index < 0 || index >= markers.Count)
            return;

        SeekToTime(markers[index], refreshPreview: true);
    }

    private void PushSequenceUndoState()
    {
        _sequenceUndoStack.Push([.. _sequenceSegments]);
    }

    private void UndoLastSequenceEdit()
    {
        if (_sequenceUndoStack.Count == 0)
        {
            _statusLabel.Text = "Nothing to undo in the timeline.";
            return;
        }

        var snapshot = _sequenceUndoStack.Pop();
        _sequenceSegments.Clear();
        _sequenceSegments.AddRange(snapshot);
        UpdateSequenceUi(selectedIndex: Math.Min(_sequenceList.SelectedIndex, _sequenceSegments.Count - 1));
        _statusLabel.Text = "Undid the last timeline edit.";
    }

    private double GetCurrentSequencePlayhead()
    {
        if (_sequenceSegments.Count == 0)
            return 0;

        var selectedIndex = _sequenceList.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < _sequenceSegments.Count)
        {
            var segment = _sequenceSegments[selectedIndex];
            var localOffset = Math.Clamp(GetCurrentPreviewTime() - segment.StartSec, 0, segment.Duration);
            return Math.Clamp(segment.SequenceStartSec + localOffset, 0, Math.Max(0.001, GetSequenceTotalDuration()));
        }

        return GetSequenceTotalDuration();
    }

    private int InsertCutAtSequenceTime(TimelineSegment newSegment, double sequenceTime)
    {
        var clampedTime = Math.Max(0, sequenceTime);
        newSegment = newSegment with { SequenceStartSec = clampedTime };
        var updated = new List<TimelineSegment>();

        foreach (var segment in _sequenceSegments)
        {
            if (segment.SafeTrack != newSegment.SafeTrack)
            {
                updated.Add(segment);
                continue;
            }

            if (segment.SequenceStartSec >= clampedTime - 0.0001)
            {
                updated.Add(segment with { SequenceStartSec = segment.SequenceStartSec + newSegment.Duration });
                continue;
            }

            if (segment.SequenceStartSec < clampedTime && segment.SequenceEndSec > clampedTime + 0.0001)
            {
                var splitOffset = clampedTime - segment.SequenceStartSec;
                var splitPoint = segment.StartSec + splitOffset;
                var left = segment with { EndSec = splitPoint };
                var right = segment with { StartSec = splitPoint, SequenceStartSec = clampedTime + newSegment.Duration };
                if (left.Duration > 0.05)
                    updated.Add(left);
                if (right.Duration > 0.05)
                    updated.Add(right);
                continue;
            }

            updated.Add(segment);
        }

        updated.Add(newSegment);
        updated = updated.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
        _sequenceSegments.Clear();
        _sequenceSegments.AddRange(updated);
        return Math.Max(0, updated.FindIndex(segment => segment == newSegment));
    }

    private int OverwriteCutAtSequenceTime(TimelineSegment newSegment, double sequenceTime)
    {
        var clampedTime = Math.Max(0, sequenceTime);
        var overwriteEnd = clampedTime + newSegment.Duration;
        newSegment = newSegment with { SequenceStartSec = clampedTime };
        var updated = new List<TimelineSegment>();

        foreach (var segment in _sequenceSegments)
        {
            if (segment.SafeTrack != newSegment.SafeTrack)
            {
                updated.Add(segment);
                continue;
            }

            if (segment.SequenceEndSec <= clampedTime + 0.0001 || segment.SequenceStartSec >= overwriteEnd - 0.0001)
            {
                updated.Add(segment);
                continue;
            }

            if (segment.SequenceStartSec < clampedTime - 0.0001)
            {
                var leftKeep = clampedTime - segment.SequenceStartSec;
                var left = segment with { EndSec = segment.StartSec + leftKeep };
                if (left.Duration > 0.05)
                    updated.Add(left);
            }

            if (segment.SequenceEndSec > overwriteEnd + 0.0001)
            {
                var rightKeep = segment.SequenceEndSec - overwriteEnd;
                var right = segment with
                {
                    StartSec = segment.EndSec - rightKeep,
                    SequenceStartSec = overwriteEnd,
                };
                if (right.Duration > 0.05)
                    updated.Add(right);
            }
        }

        updated.Add(newSegment);
        updated = updated.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
        _sequenceSegments.Clear();
        _sequenceSegments.AddRange(updated);
        return Math.Max(0, updated.FindIndex(segment => segment == newSegment));
    }

    private void SplitSelectedSegmentAtPlayhead(int? requestedIndex = null, double? requestedTime = null)
    {
        var selectedIndex = requestedIndex ?? _sequenceList.SelectedIndex;
        var playhead = SnapToFrame(requestedTime ?? GetCurrentPreviewTime());
        if (selectedIndex < 0 || selectedIndex >= _sequenceSegments.Count)
        {
            MessageBox.Show(this, "Select a timeline cut first, then use Cut at playhead or the Razor tool.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var segment = _sequenceSegments[selectedIndex];
        if (requestedTime is null && !string.Equals(segment.SourceFile, _selectedFile, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Load that timeline cut into the monitor first, or switch to Razor and click the clip directly.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (playhead <= segment.StartSec + 0.05 || playhead >= segment.EndSec - 0.05)
        {
            MessageBox.Show(this, "Place the cut inside the clip, not on its very edge.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PushSequenceUndoState();
        var splitOffset = playhead - segment.StartSec;
        var left = segment with { EndSec = playhead };
        var right = segment with { StartSec = playhead, SequenceStartSec = segment.SequenceStartSec + splitOffset };
        _sequenceSegments[selectedIndex] = left;
        _sequenceSegments.Insert(selectedIndex + 1, right);
        UpdateSequenceUi(selectedIndex + 1);
        _statusLabel.Text = $"Cut timeline segment {selectedIndex + 1} at {FormatTime(playhead)}.";
    }

    private void ApplySequenceTrimFromTimeline(int index, double start, double end)
    {
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var segment = _sequenceSegments[index];
        var safeStart = SnapToFrame(Math.Max(0, Math.Min(start, end - (1d / 30d))));
        var safeEnd = Math.Max(safeStart + (1d / 30d), SnapToFrame(end));
        var changingLeftEdge = Math.Abs(safeStart - segment.StartSec) > Math.Abs(safeEnd - segment.EndSec);
        var updatedSegment = segment;

        if (changingLeftEdge)
        {
            var leftDelta = safeStart - segment.StartSec;
            updatedSegment = segment with
            {
                StartSec = safeStart,
                SequenceStartSec = Math.Max(0, segment.SequenceStartSec + leftDelta),
            };

            if (_timelineEditMode == TimelineEditMode.Rolling)
            {
                var previousIndex = _sequenceSegments
                    .Select((item, itemIndex) => (item, itemIndex))
                    .Where(entry => entry.itemIndex != index && entry.item.SafeTrack == segment.SafeTrack && Math.Abs(entry.item.SequenceEndSec - segment.SequenceStartSec) <= 0.12)
                    .OrderByDescending(entry => entry.item.SequenceEndSec)
                    .Select(entry => entry.itemIndex)
                    .FirstOrDefault(-1);
                if (previousIndex >= 0)
                {
                    var previous = _sequenceSegments[previousIndex];
                    _sequenceSegments[previousIndex] = previous with { EndSec = Math.Max(previous.StartSec + 0.05, previous.EndSec + leftDelta) };
                }
            }
        }
        else
        {
            var rightDelta = safeEnd - segment.EndSec;
            updatedSegment = segment with { EndSec = safeEnd };

            if (_timelineEditMode == TimelineEditMode.Rolling)
            {
                var nextIndex = _sequenceSegments
                    .Select((item, itemIndex) => (item, itemIndex))
                    .Where(entry => entry.itemIndex != index && entry.item.SafeTrack == segment.SafeTrack && Math.Abs(entry.item.SequenceStartSec - segment.SequenceEndSec) <= 0.12)
                    .OrderBy(entry => entry.item.SequenceStartSec)
                    .Select(entry => entry.itemIndex)
                    .FirstOrDefault(-1);
                if (nextIndex >= 0)
                {
                    var next = _sequenceSegments[nextIndex];
                    _sequenceSegments[nextIndex] = next with
                    {
                        StartSec = Math.Clamp(next.StartSec + rightDelta, 0, next.EndSec - 0.05),
                        SequenceStartSec = Math.Max(segment.SequenceStartSec + updatedSegment.Duration, next.SequenceStartSec + rightDelta),
                    };
                }
            }
        }

        var durationDelta = updatedSegment.Duration - segment.Duration;
        _sequenceSegments[index] = updatedSegment;

        if (_timelineEditMode == TimelineEditMode.Ripple && Math.Abs(durationDelta) > 0.0001)
        {
            for (var segmentIndex = 0; segmentIndex < _sequenceSegments.Count; segmentIndex++)
            {
                if (segmentIndex == index)
                    continue;

                var downstream = _sequenceSegments[segmentIndex];
                if (downstream.SafeTrack == updatedSegment.SafeTrack && downstream.SequenceStartSec > segment.SequenceStartSec + 0.0001)
                    _sequenceSegments[segmentIndex] = downstream with { SequenceStartSec = Math.Max(0, downstream.SequenceStartSec + durationDelta) };
            }
        }

        var selectedSegment = _sequenceSegments[index];
        var ordered = _sequenceSegments.OrderBy(item => item.SequenceStartSec).ThenBy(item => item.SafeTrack).ToList();
        _sequenceSegments.Clear();
        _sequenceSegments.AddRange(ordered);
        var newIndex = Math.Max(0, _sequenceSegments.FindIndex(item => item == selectedSegment));
        UpdateSequenceUi(newIndex);

        if (newIndex == _sequenceList.SelectedIndex && string.Equals(_selectedFile, selectedSegment.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            _updatingTrimRange = true;
            try
            {
                _startBox.Value = Math.Clamp((decimal)selectedSegment.StartSec, _startBox.Minimum, _startBox.Maximum);
                _endBox.Value = Math.Clamp((decimal)selectedSegment.EndSec, _endBox.Minimum, _endBox.Maximum);
            }
            finally
            {
                _updatingTrimRange = false;
            }
            UpdateTrimTimelineUi();
        }
    }

    private void ApplySequenceSlipFromTimeline(int index, double deltaSeconds)
    {
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var segment = _sequenceSegments[index];
        var duration = segment.Duration;
        var maxSourceEnd = string.Equals(segment.SourceFile, _selectedFile, StringComparison.OrdinalIgnoreCase) && _videoDuration > 0
            ? _videoDuration
            : segment.EndSec + 30;
        var newStart = Math.Clamp(segment.StartSec + deltaSeconds, 0, Math.Max(0, maxSourceEnd - duration));
        var newEnd = Math.Max(newStart + 0.05, newStart + duration);
        _sequenceSegments[index] = segment with { StartSec = newStart, EndSec = newEnd };
        UpdateSequenceUi(index);

        if (index == _sequenceList.SelectedIndex)
        {
            _updatingTrimRange = true;
            try
            {
                _startBox.Value = Math.Clamp((decimal)newStart, _startBox.Minimum, _startBox.Maximum);
                _endBox.Value = Math.Clamp((decimal)newEnd, _endBox.Minimum, _endBox.Maximum);
            }
            finally
            {
                _updatingTrimRange = false;
            }
            UpdateTrimTimelineUi();
        }

        _statusLabel.Text = $"Slipped the selected clip contents by {(deltaSeconds >= 0 ? "+" : string.Empty)}{deltaSeconds:F2}s without moving its timeline position.";
    }

    private void AddCurrentCutToSequence(bool overwrite = false)
    {
        if (!TryGetSingleSelectedFile(out var input, out var error))
        {
            MessageBox.Show(this, error, "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var start = (double)_startBox.Value;
        var end = (double)_endBox.Value;
        if (end <= start)
        {
            MessageBox.Show(this, "End time must be greater than start time before inserting into the timeline.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var sequencePlayhead = GetCurrentSequencePlayhead();
        var selectedIndex = _sequenceList.SelectedIndex;
        var targetTrack = Math.Clamp(_targetVideoTrack, 1, 2);

        var newSegment = new TimelineSegment(input, start, end, targetTrack, sequencePlayhead, new ClipTransform());
        PushSequenceUndoState();
        var insertedIndex = overwrite
            ? OverwriteCutAtSequenceTime(newSegment, sequencePlayhead)
            : InsertCutAtSequenceTime(newSegment, sequencePlayhead);

        UpdateSequenceUi(selectedIndex: insertedIndex);
        _statusLabel.Text = overwrite
            ? $"Overwrite edit placed on V{targetTrack} at {FormatTime(sequencePlayhead)}."
            : $"Insert edit placed on V{targetTrack} at {FormatTime(sequencePlayhead)}.";
    }

    private void RemoveSelectedSequenceSegment()
    {
        if (_sequenceList.SelectedIndex < 0 || _sequenceList.SelectedIndex >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var index = _sequenceList.SelectedIndex;
        var removed = _sequenceSegments[index];
        _sequenceSegments.RemoveAt(index);

        for (var segmentIndex = 0; segmentIndex < _sequenceSegments.Count; segmentIndex++)
        {
            var segment = _sequenceSegments[segmentIndex];
            if (segment.SafeTrack == removed.SafeTrack && segment.SequenceStartSec >= removed.SequenceEndSec - 0.0001)
                _sequenceSegments[segmentIndex] = segment with { SequenceStartSec = Math.Max(0, segment.SequenceStartSec - removed.Duration) };
        }

        UpdateSequenceUi(selectedIndex: Math.Min(index, _sequenceSegments.Count - 1));
        _statusLabel.Text = "Ripple deleted the selected timeline cut.";
    }

    private void MoveSelectedSequenceSegment(int direction)
    {
        var index = _sequenceList.SelectedIndex;
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var segment = _sequenceSegments[index];
        _sequenceSegments.RemoveAt(index);
        _sequenceSegments.Insert(newIndex, segment);
        UpdateSequenceUi(selectedIndex: newIndex);
    }

    private void ClearSequence()
    {
        if (_sequenceSegments.Count == 0)
            return;

        PushSequenceUndoState();
        _sequenceSegments.Clear();
        UpdateSequenceUi();
        _statusLabel.Text = "Cleared the timeline.";
    }

    private void MoveSequenceSegmentTo(int fromIndex, int targetIndex, int targetTrack)
    {
        if (fromIndex < 0 || fromIndex >= _sequenceSegments.Count)
            return;

        PushSequenceUndoState();
        var clampedTrack = Math.Clamp(targetTrack, 1, 2);
        var moving = _sequenceSegments[fromIndex];
        _sequenceSegments.RemoveAt(fromIndex);

        var ordered = _sequenceSegments.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
        var targetTime = targetIndex <= 0
            ? 0
            : targetIndex >= ordered.Count
                ? GetSequenceTotalDuration()
                : ordered[targetIndex].SequenceStartSec;

        moving = moving with { Track = clampedTrack, SequenceStartSec = Math.Max(0, targetTime) };
        var movedIndex = _timelineEditMode == TimelineEditMode.Ripple
            ? InsertCutAtSequenceTime(moving, moving.SequenceStartSec)
            : OverwriteCutAtSequenceTime(moving, moving.SequenceStartSec);

        UpdateSequenceUi(movedIndex);
        _statusLabel.Text = _timelineEditMode == TimelineEditMode.Ripple
            ? $"Ripple-inserted timeline cut on V{clampedTrack} and pushed downstream edits."
            : $"Overwrite-moved timeline cut to V{clampedTrack}.";
    }

    private int RippleMoveSequenceSegment(int fromIndex, int targetIndex, int targetTrack)
    {
        var segment = _sequenceSegments[fromIndex] with { Track = targetTrack };
        _sequenceSegments.RemoveAt(fromIndex);

        var insertIndex = Math.Clamp(targetIndex, 0, _sequenceSegments.Count);
        if (insertIndex > fromIndex)
            insertIndex--;

        _sequenceSegments.Insert(insertIndex, segment);
        return insertIndex;
    }

    private int OverwriteMoveSequenceSegment(int fromIndex, int targetIndex, int targetTrack)
    {
        if (_sequenceSegments.Count == 0)
            return -1;

        var moving = _sequenceSegments[fromIndex] with { Track = targetTrack };
        if (targetIndex == fromIndex)
        {
            _sequenceSegments[fromIndex] = moving;
            return fromIndex;
        }

        _sequenceSegments.RemoveAt(fromIndex);
        var insertIndex = Math.Clamp(targetIndex, 0, _sequenceSegments.Count);
        if (insertIndex > fromIndex)
            insertIndex--;

        if (insertIndex < _sequenceSegments.Count)
            _sequenceSegments.RemoveAt(insertIndex);

        insertIndex = Math.Clamp(insertIndex, 0, _sequenceSegments.Count);
        _sequenceSegments.Insert(insertIndex, moving);
        return insertIndex;
    }

    private void RippleDeleteGapAt(int insertIndex, int targetTrack)
    {
        if (_sequenceSegments.Count < 2)
            return;

        var clampedTrack = Math.Clamp(targetTrack, 1, 2);
        var orderedTrackSegments = _sequenceSegments
            .Select((segment, index) => (segment, index))
            .Where(entry => entry.segment.SafeTrack == clampedTrack)
            .OrderBy(entry => entry.segment.SequenceStartSec)
            .ToList();

        if (orderedTrackSegments.Count < 2)
        {
            _statusLabel.Text = $"No removable gap found on V{clampedTrack}.";
            return;
        }

        var chosenGapIndex = -1;
        var gapAmount = 0d;
        for (var index = 1; index < orderedTrackSegments.Count; index++)
        {
            var previous = orderedTrackSegments[index - 1].segment;
            var current = orderedTrackSegments[index].segment;
            var gap = current.SequenceStartSec - previous.SequenceEndSec;
            if (gap > 0.05)
            {
                chosenGapIndex = index;
                gapAmount = gap;
                if (index >= Math.Max(1, insertIndex))
                    break;
            }
        }

        if (chosenGapIndex < 0 || gapAmount <= 0.05)
        {
            _statusLabel.Text = $"No removable gap found on V{clampedTrack}.";
            return;
        }

        PushSequenceUndoState();
        var shiftFrom = orderedTrackSegments[chosenGapIndex].segment.SequenceStartSec;
        for (var index = 0; index < _sequenceSegments.Count; index++)
        {
            var segment = _sequenceSegments[index];
            if (segment.SafeTrack == clampedTrack && segment.SequenceStartSec >= shiftFrom - 0.0001)
                _sequenceSegments[index] = segment with { SequenceStartSec = Math.Max(0, segment.SequenceStartSec - gapAmount) };
        }

        UpdateSequenceUi();
        _statusLabel.Text = $"Ripple deleted the gap on V{clampedTrack} and pulled later clips left.";
    }

    private async Task LoadSequenceSegmentAsync(int index)
    {
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        var segment = _sequenceSegments[index];
        var existingIndex = _filesList.Items.IndexOf(segment.SourceFile);
        if (existingIndex < 0)
        {
            _filesList.Items.Insert(0, segment.SourceFile);
            existingIndex = 0;
        }

        var currentSelected = _filesList.SelectedItem?.ToString();
        if (!string.Equals(currentSelected, segment.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            _filesList.ClearSelected();
            _filesList.SelectedIndex = existingIndex;
            await HandleSelectionChangedAsync();
        }

        _startBox.Value = Math.Clamp((decimal)segment.StartSec, _startBox.Minimum, _startBox.Maximum);
        _endBox.Value = Math.Clamp((decimal)segment.EndSec, _endBox.Minimum, _endBox.Maximum);
        _sequenceList.SelectedIndex = index;
        _sequenceTimelineView.SetSelectedIndex(index);
        SeekToTime(segment.StartSec, refreshPreview: true);
        _statusLabel.Text = $"Loaded timeline cut {index + 1} back into the source/program monitors.";
    }

    private void UpdateSequenceUi(int selectedIndex = -1)
    {
        _sequenceList.BeginUpdate();
        _sequenceList.Items.Clear();
        foreach (var segment in _sequenceSegments)
            _sequenceList.Items.Add(segment);
        _sequenceList.EndUpdate();

        if (_sequenceSegments.Count > 0)
        {
            var safeIndex = selectedIndex >= 0 && selectedIndex < _sequenceSegments.Count
                ? selectedIndex
                : Math.Min(_sequenceList.SelectedIndex, _sequenceSegments.Count - 1);
            var totalDuration = GetSequenceTotalDuration();
            var v1Count = _sequenceSegments.Count(segment => segment.SafeTrack == 1);
            var v2Count = _sequenceSegments.Count(segment => segment.SafeTrack == 2);
            _sequenceSummaryLabel.Text = $"{_sequenceSegments.Count} cut(s) • V1 {v1Count} / V2 {v2Count} • {FormatTime(totalDuration)} seq";
            _sequenceTimelineView.SetTargetTracks(_targetVideoTrack, _targetAudioTrack);
            _sequenceTimelineView.SetSegments(_sequenceSegments, safeIndex);
            if (safeIndex >= 0 && safeIndex < _sequenceList.Items.Count)
                _sequenceList.SelectedIndex = safeIndex;
        }
        else
        {
            _sequenceSummaryLabel.Text = "Timeline empty — mark IN/OUT, then Insert or Overwrite at the playhead.";
            _sequenceTimelineView.SetSegments(Array.Empty<TimelineSegment>(), -1);
        }

        _splitPlayheadButton.Enabled = _sequenceSegments.Count > 0 && _sequenceList.SelectedIndex >= 0;
        _undoEditButton.Enabled = _sequenceUndoStack.Count > 0;
        _exportSequenceButton.Enabled = _sequenceSegments.Count > 0;
        UpdateInspectorModeUi();
    }

    private async Task ExportSequenceAsync()
    {
        if (_sequenceSegments.Count == 0)
        {
            MessageBox.Show(this, "Add at least one cut to the timeline before exporting.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var orderedSegments = _sequenceSegments.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
        var outputPath = BuildOutputPath(orderedSegments[0].SourceFile, "timeline", forceMp4: true);
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        var tempDir = Path.Combine(Path.GetTempPath(), $"velo-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFiles = new List<string>();

        SetEditorBusy(true, "Exporting timeline sequence…");

        try
        {
            var boundaries = orderedSegments
                .SelectMany(segment => new[] { segment.SequenceStartSec, segment.SequenceEndSec })
                .Append(0d)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
            var firstDetails = await GetVideoDetailsAsync(orderedSegments[0].SourceFile, CancellationToken.None);

            for (var index = 0; index < boundaries.Count - 1; index++)
            {
                var intervalStart = boundaries[index];
                var intervalEnd = boundaries[index + 1];
                var intervalDuration = intervalEnd - intervalStart;
                if (intervalDuration <= 0.03)
                    continue;

                var tempFile = Path.Combine(tempDir, $"segment-{index:D2}.mp4");
                tempFiles.Add(tempFile);
                var activeSegment = GetTopVisibleSegmentAtTime(intervalStart + (intervalDuration / 2d));

                if (activeSegment is null)
                {
                    var fillerArgs = $"-f lavfi -i color=c=black:s={firstDetails.Width}x{firstDetails.Height}:d={intervalDuration.ToString(CultureInfo.InvariantCulture)} -f lavfi -i anullsrc=r=48000:cl=stereo -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -y {Quote(tempFile)}";
                    await RunFfmpegProcessAsync(ffmpegPath, fillerArgs);
                    continue;
                }

                var clipOffset = Math.Max(0, intervalStart - activeSegment.SequenceStartSec);
                var extractStart = activeSegment.StartSec + clipOffset;
                var extractArgs = $"-ss {extractStart.ToString(CultureInfo.InvariantCulture)} -i {Quote(activeSegment.SourceFile)} -t {intervalDuration.ToString(CultureInfo.InvariantCulture)} -c copy -avoid_negative_ts make_zero -y {Quote(tempFile)}";
                await RunFfmpegProcessAsync(ffmpegPath, extractArgs);
            }

            var listFile = Path.Combine(tempDir, "timeline.txt");
            var listText = string.Join(Environment.NewLine, tempFiles.Select(file => $"file '{file.Replace("'", "'\\''")}'"));
            await File.WriteAllTextAsync(listFile, listText + Environment.NewLine);

            var concatArgs = $"-f concat -safe 0 -i {Quote(listFile)} -c copy -movflags +faststart -y {Quote(outputPath)}";
            await RunFfmpegProcessAsync(ffmpegPath, concatArgs);

            Logger.Info($"Video editor timeline export created: {outputPath}");
            if (!_filesList.Items.Contains(outputPath))
                _filesList.Items.Insert(0, outputPath);
            _statusLabel.Text = $"Timeline exported: {Path.GetFileName(outputPath)}";
            MessageBox.Show(this, $"Timeline export complete.\n\nSaved to:\n{outputPath}", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("Video editor timeline export failed", ex);
            _statusLabel.Text = $"Timeline export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            SetEditorBusy(false, _statusLabel.Text);
        }
    }

    private void PickOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where the edited video should be written",
            SelectedPath = Directory.Exists(_outputFolderBox.Text)
                ? _outputFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputFolderBox.Text = dialog.SelectedPath;
    }

    private async Task RunTrimAsync()
    {
        if (!TryGetSingleSelectedFile(out var input, out var error))
        {
            MessageBox.Show(this, error, "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var start = (double)_startBox.Value;
        var end = (double)_endBox.Value;
        if (end <= start)
        {
            MessageBox.Show(this, "End time must be greater than start time.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outputPath = BuildOutputPath(input, "trimmed");
        var duration = end - start;
        var args = $"-ss {start.ToString(CultureInfo.InvariantCulture)} -i {Quote(input)} -t {duration.ToString(CultureInfo.InvariantCulture)} -c copy -movflags +faststart -y {Quote(outputPath)}";
        await RunFfmpegAsync(args, $"Trim created: {Path.GetFileName(outputPath)}", outputPath);
    }

    private async Task RunCropAsync()
    {
        if (!TryGetSingleSelectedFile(out var input, out var error))
        {
            MessageBox.Show(this, error, "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var crop = GetCropRectangle();
        if (crop.Width < 2 || crop.Height < 2)
        {
            MessageBox.Show(this, "Choose a valid crop area first.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outputPath = BuildOutputPath(input, "cropped", forceMp4: true);
        var args = $"-i {Quote(input)} -vf \"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}\" -c:v libx264 -preset fast -crf 18 -c:a copy -movflags +faststart -y {Quote(outputPath)}";
        await RunFfmpegAsync(args, $"Crop created: {Path.GetFileName(outputPath)}", outputPath);
    }

    private async Task RunMergeAsync()
    {
        var files = _filesList.SelectedItems.Cast<string>().Where(File.Exists).ToList();
        if (files.Count < 2)
        {
            MessageBox.Show(this, "Select at least two clips to merge.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var listFile = Path.Combine(Path.GetTempPath(), $"velo-merge-{Guid.NewGuid():N}.txt");
        try
        {
            var builder = new StringBuilder();
            foreach (var file in files)
                builder.AppendLine($"file '{file.Replace("'", "'\\''")}'");
            await File.WriteAllTextAsync(listFile, builder.ToString());

            var outputPath = BuildOutputPath(files[0], "merged", forceMp4: true);
            var args = $"-f concat -safe 0 -i {Quote(listFile)} -c copy -movflags +faststart -y {Quote(outputPath)}";
            await RunFfmpegAsync(args, $"Merge created: {Path.GetFileName(outputPath)}", outputPath);
        }
        finally
        {
            try { File.Delete(listFile); } catch { }
        }
    }

    private bool TryGetSingleSelectedFile(out string input, out string error)
    {
        input = string.Empty;
        error = string.Empty;

        if (_filesList.SelectedItems.Count != 1)
        {
            error = "Select exactly one clip for trim or crop editing.";
            return false;
        }

        input = _filesList.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            error = "The selected input file is missing.";
            return false;
        }

        return true;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.I)
        {
            AddFiles();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            UndoLastSequenceEdit();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.B || e.KeyCode == Keys.K))
        {
            SplitSelectedSegmentAtPlayhead();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
        {
            var direction = e.KeyCode == Keys.Right ? 1 : -1;
            var frameStep = e.Shift ? (5d / 30d) : (1d / 30d);
            NudgeNearestTrimHandle(direction * frameStep);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
        {
            AdjustTimelineZoom(0.5);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
        {
            AdjustTimelineZoom(-0.5);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.C && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Razor);
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.KeyCode == Keys.R || e.KeyCode == Keys.B) && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Ripple);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.N && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Rolling);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Y && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Slip);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.V && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Select);
            e.SuppressKeyPress = true;
            return;
        }

        if ((e.KeyCode == Keys.J || e.KeyCode == Keys.K || e.KeyCode == Keys.L) && !e.Control && !e.Alt)
        {
            HandleTransportKey(e.KeyCode);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.S && !e.Control && !e.Alt)
        {
            _snappingEnabled = !_snappingEnabled;
            UpdateTimelineZoom();
            _statusLabel.Text = _snappingEnabled
                ? "SNAPPING ENABLED — PLAYHEAD AND EDITS SNAP TO TIME TICKS AND CUT BOUNDARIES."
                : "SNAPPING DISABLED — TIMELINES NOW MOVE FREELY.";
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Space)
        {
            TogglePlayback();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Delete)
        {
            if (_markerList.Focused)
                RemoveSelectedMarker();
            else
                RemoveSelectedSequenceSegment();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.I && !e.Control && !e.Alt)
        {
            SetTrimBoundary(true);
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.O && !e.Control && !e.Alt)
        {
            SetTrimBoundary(false);
            e.SuppressKeyPress = true;
        }
    }

    private async Task RunFfmpegAsync(string args, string successMessage, string outputPath)
    {
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        SetEditorBusy(true, "Running FFmpeg…");

        try
        {
            await RunFfmpegProcessAsync(ffmpegPath, args);

            Logger.Info($"Video editor output created: {outputPath}");
            if (!_filesList.Items.Contains(outputPath))
                _filesList.Items.Insert(0, outputPath);
            _statusLabel.Text = successMessage;
            MessageBox.Show(this, $"{successMessage}\n\nSaved to:\n{outputPath}", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("Video editor operation failed", ex);
            _statusLabel.Text = $"Editor task failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetEditorBusy(false, _statusLabel.Text);
        }
    }

    private static async Task RunFfmpegProcessAsync(string ffmpegPath, string args)
    {
        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("FFmpeg could not be started.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "FFmpeg failed." : stderr[^Math.Min(stderr.Length, 800)..]);
    }

    private void SetEditorBusy(bool busy, string status)
    {
        var canControlPlayback = !busy && _mediaElement.Source != null;
        _trimButton.Enabled = !busy;
        _cropButton.Enabled = !busy;
        _mergeButton.Enabled = !busy;
        _addCutButton.Enabled = !busy;
        _overwriteCutButton.Enabled = !busy;
        _undoEditButton.Enabled = !busy && _sequenceUndoStack.Count > 0;
        _exportSequenceButton.Enabled = !busy && _sequenceSegments.Count > 0;
        _splitPlayheadButton.Enabled = !busy && _sequenceList.SelectedIndex >= 0;
        _refreshPreviewButton.Enabled = !busy;
        _playPauseButton.Enabled = canControlPlayback;
        _jumpBackButton.Enabled = canControlPlayback;
        _jumpForwardButton.Enabled = canControlPlayback;
        _selectToolButton.Enabled = !busy;
        _rippleToolButton.Enabled = !busy;
        _razorToolButton.Enabled = !busy;
        _statusLabel.Text = status;
    }

    private string BuildOutputPath(string inputFile, string suffix, bool forceMp4 = false)
    {
        var folder = Directory.Exists(_outputFolderBox.Text)
            ? _outputFolderBox.Text
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Directory.CreateDirectory(folder);

        var baseName = string.IsNullOrWhiteSpace(_outputNameBox.Text)
            ? $"{Path.GetFileNameWithoutExtension(inputFile)}-{suffix}"
            : _outputNameBox.Text.Trim();
        var ext = forceMp4 ? ".mp4" : (Path.GetExtension(inputFile) is { Length: > 0 } currentExt ? currentExt : ".mp4");

        var candidate = Path.Combine(folder, $"{SanitizeFileName(baseName)}{ext}");
        var counter = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{SanitizeFileName(baseName)}-{counter}{ext}");
            counter++;
        }

        return candidate;
    }

    private static void ReplacePicture(PictureBox box, Image? image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string FormatTime(double totalSeconds)
    {
        var safe = Math.Max(0, totalSeconds);
        var ts = TimeSpan.FromSeconds(safe);
        return safe >= 3600
            ? ts.ToString(@"hh\:mm\:ss\.fff")
            : ts.ToString(@"mm\:ss\.fff");
    }

    private static double SnapToFrame(double seconds, double fps = 30d)
    {
        var safeFps = Math.Max(1d, fps);
        return Math.Round(Math.Max(0, seconds) * safeFps) / safeFps;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? $"clip-{DateTime.Now:yyyyMMdd-HHmmss}" : cleaned;
    }

    private sealed class TrimTimelineView : Control
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
        private string _clipLabel = "No clip selected";
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
            BackColor = Color.Black;
            ForeColor = Color.FromArgb(240, 240, 245);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += (_, _) => Focus();
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

            using var borderPen = new Pen(Color.FromArgb(52, 52, 66));
            using var textBrush = new SolidBrush(Color.FromArgb(145, 145, 160));
            using var railBrush = new SolidBrush(Color.FromArgb(34, 34, 34));
            using var clipBrush = new SolidBrush(Color.FromArgb(28, 59, 130, 246));
            using var wasteBrush = new SolidBrush(Color.FromArgb(153, 0, 0, 0));
            using var playheadPen = new Pen(Color.FromArgb(248, 113, 113), 1);
            using var handleBrush = new SolidBrush(Color.FromArgb(245, 245, 250));

            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            var rail = GetRailRect();

            TextRenderer.DrawText(e.Graphics, _clipLabel, Font, new Rectangle(14, 8, Width - 28, 18), Color.FromArgb(220, 220, 235), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            if (_duration <= 0)
            {
                TextRenderer.DrawText(e.Graphics, "Select a clip to see the editable trim timeline.", Font, new Rectangle(14, 30, Width - 28, 20), textBrush.Color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }

            e.Graphics.FillRectangle(railBrush, rail);
            e.Graphics.DrawRectangle(borderPen, rail);

            var badgeRect = new Rectangle(Math.Max(Width - 86, 14), 6, 72, 16);
            using var badgeBrush = new SolidBrush(Color.FromArgb(32, 45, 55));
            e.Graphics.FillRectangle(badgeBrush, badgeRect);
            TextRenderer.DrawText(e.Graphics, "SOURCE", new Font("Segoe UI", 7f, FontStyle.Bold), badgeRect, Color.FromArgb(191, 219, 254), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var (visibleStart, visibleDuration) = GetVisibleRange();
            using var tickPen = new Pen(Color.FromArgb(48, 71, 85));
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
            else
            {
                for (var i = 0; i < 12; i++)
                {
                    var blockWidth = Math.Max(10, rail.Width / 12 - 3);
                    var blockX = rail.Left + i * rail.Width / 12;
                    var blockRect = new Rectangle(blockX + 1, rail.Top + 4, blockWidth, rail.Height - 8);
                    using var filmBrush = new SolidBrush(i % 2 == 0 ? Color.FromArgb(32, 32, 40) : Color.FromArgb(25, 25, 32));
                    e.Graphics.FillRectangle(filmBrush, blockRect);
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
            using var selectedBorder = new Pen(Color.FromArgb(59, 130, 246), 1);
            e.Graphics.DrawRectangle(selectedBorder, selectedRect);

            if (!double.IsNaN(_snapIndicatorSeconds))
            {
                var snapX = SecondsToX(_snapIndicatorSeconds, rail);
                using var snapPen = new Pen(Color.FromArgb(250, 251, 191, 36), 2);
                using var snapBrush = new SolidBrush(Color.FromArgb(220, 251, 191, 36));
                e.Graphics.DrawLine(snapPen, snapX, rail.Top - 10, snapX, rail.Bottom + 10);
                var snapRect = new Rectangle(Math.Max(rail.Left, snapX - 26), 8, 52, 14);
                e.Graphics.FillRectangle(snapBrush, snapRect);
                TextRenderer.DrawText(e.Graphics, "SNAP", new Font("Segoe UI", 6.5f, FontStyle.Bold), snapRect, Color.FromArgb(17, 24, 39), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            using var markerPen = new Pen(Color.FromArgb(251, 191, 36), 2);
            using var markerBrush = new SolidBrush(Color.FromArgb(251, 191, 36));
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
            DrawTrimHandle(e.Graphics, startHandle, "I", Color.FromArgb(59, 130, 246));
            DrawTrimHandle(e.Graphics, endHandle, "O", Color.FromArgb(168, 85, 247));

            var playheadX = SecondsToX(_playhead, rail);
            e.Graphics.DrawLine(playheadPen, playheadX, 0, playheadX, Height - 1);

            TextRenderer.DrawText(e.Graphics, $"IN {FormatTime(_rangeStart)}", new Font("Segoe UI", 7f), new Point(14, rail.Bottom + 6), Color.FromArgb(200, 200, 215));
            TextRenderer.DrawText(e.Graphics, $"OUT {FormatTime(_rangeEnd)}", new Font("Segoe UI", 7f), new Point(120, rail.Bottom + 6), Color.FromArgb(200, 200, 215));
            TextRenderer.DrawText(e.Graphics, $"LEN {FormatTime(Math.Max(0, _rangeEnd - _rangeStart))}", new Font("Segoe UI", 7f), new Point(240, rail.Bottom + 6), Color.FromArgb(200, 200, 215));
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
            var railHeight = Math.Clamp(Height - 58, 36, 54);
            return new Rectangle(14, 28, Math.Max(120, Width - 28), railHeight);
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
                return SnapToFrame(seconds);
            }

            var (visibleStart, visibleDuration) = GetVisibleRange();
            var snapCandidates = new List<double> { 0, _duration, _playhead, _rangeStart, _rangeEnd };
            snapCandidates.AddRange(_markers);
            for (var tick = Math.Ceiling(visibleStart); tick <= visibleStart + visibleDuration; tick += 1d)
                snapCandidates.Add(tick);

            var threshold = Math.Max(0.05, visibleDuration * 0.02);
            var nearest = snapCandidates.OrderBy(value => Math.Abs(value - seconds)).FirstOrDefault();
            var snapped = Math.Abs(nearest - seconds) <= threshold;
            _snapIndicatorSeconds = snapped ? nearest : double.NaN;
            return SnapToFrame(snapped ? nearest : seconds);
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
            TextRenderer.DrawText(graphics, label, new Font("Segoe UI", 6.5f, FontStyle.Bold), labelRect, accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private sealed class SequenceTimelineView : Control
    {
        private enum SegmentDragMode
        {
            None,
            Seek,
            Move,
            ResizeLeft,
            ResizeRight,
        }

        private static readonly Cursor RazorCursor = CreateRazorCursor();

        private readonly List<TimelineSegment> _segments = [];
        private readonly List<(Rectangle Rect, int Index)> _hitTargets = [];
        private int _selectedIndex = -1;
        private double _playheadSeconds;
        private double _zoom = 1;
        private SegmentDragMode _dragMode;
        private int _dragIndex = -1;
        private TimelineSegment? _dragOriginSegment;
        private int _previewInsertIndex = -1;
        private int _previewTrack = 1;
        private bool _razorMode;
        private bool _rippleMoveMode;
        private bool _rollingMode;
        private bool _slipMode;
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
        private int _gapContextInsertIndex = -1;
        private int _gapContextTrack = 1;

        private Func<string, Image?>? _waveformProvider;
        private Func<string, IReadOnlyList<Image>>? _thumbnailProvider;

        public event Action<int>? SegmentClicked;
        public event Action<int, double, double>? SegmentTrimChanged;
        public event Action<int, int, int>? SegmentMoved;
        public event Action<int, double>? SegmentSplitRequested;
        public event Action<int, int>? RippleDeleteRequested;
        public event Action<int, double>? SegmentSlipRequested;
        public event Action<int, int>? TrackTargetChanged;
        public event Action<double>? SeekRequested;
        public event Action<double>? ZoomDeltaRequested;

        public SequenceTimelineView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.Black;
            ForeColor = Color.FromArgb(240, 240, 245);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += (_, _) => Focus();

            var rippleDeleteItem = new ToolStripMenuItem("Ripple Delete Gap");
            rippleDeleteItem.Click += (_, _) =>
            {
                if (_gapContextInsertIndex >= 0)
                    RippleDeleteRequested?.Invoke(_gapContextInsertIndex, _gapContextTrack);
            };
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

        public void SetPlayhead(double seconds)
        {
            _playheadSeconds = Math.Max(0, seconds);
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

        public void SetRazorMode(bool enabled)
        {
            _razorMode = enabled;
            Cursor = enabled ? RazorCursor : Cursors.Hand;
            Invalidate();
        }

        public void SetRippleMode(bool enabled)
        {
            _rippleMoveMode = enabled;
            if (!_razorMode)
                Cursor = enabled ? Cursors.Hand : Cursors.SizeAll;
            Invalidate();
        }

        public void SetRollingMode(bool enabled)
        {
            _rollingMode = enabled;
            if (!_razorMode && !_slipMode)
                Cursor = enabled ? Cursors.SizeWE : Cursors.SizeAll;
            Invalidate();
        }

        public void SetSlipMode(bool enabled)
        {
            _slipMode = enabled;
            if (!_razorMode)
                Cursor = enabled ? Cursors.SizeAll : (_rippleMoveMode ? Cursors.Hand : Cursors.SizeAll);
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

            if (_v1BadgeRect.Contains(e.Location))
            {
                _targetVideoTrack = 1;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }

            if (_v2BadgeRect.Contains(e.Location))
            {
                _targetVideoTrack = 2;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }

            if (_a1BadgeRect.Contains(e.Location))
            {
                _targetAudioTrack = 1;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }

            if (_a2BadgeRect.Contains(e.Location))
            {
                _targetAudioTrack = 2;
                TrackTargetChanged?.Invoke(_targetVideoTrack, _targetAudioTrack);
                Invalidate();
                return;
            }

            var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(e.Location));
            if (hit.Rect == Rectangle.Empty)
            {
                _dragMode = SegmentDragMode.Seek;
                UpdatePlayheadFromPoint(e.Location);
                return;
            }

            _selectedIndex = hit.Index;
            Invalidate();
            SegmentClicked?.Invoke(hit.Index);

            if (_razorMode)
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
                _previewInsertIndex = hit.Index;
                _previewTrack = _segments[hit.Index].SafeTrack;
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

            var totalDuration = Math.Max(0.1, _segments.Max(segment => segment.SequenceEndSec));
            var timelineLeft = 72;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var absoluteSeconds = visibleStart + (((e.X - timelineLeft) / (double)Math.Max(1, timelineWidth)) * visibleDuration);
            absoluteSeconds = SnapToBoundary(absoluteSeconds, totalDuration, out var snapped);
            var localPosition = absoluteSeconds - _dragOriginSegment.SequenceStartSec;
            _snapIndicatorSeconds = snapped ? absoluteSeconds : double.NaN;
            _snapIndicatorLabel = snapped
                ? (_dragMode == SegmentDragMode.Move ? (_rippleMoveMode ? "RIPPLE" : (_slipMode ? "SLIP" : "OVR")) : (_rollingMode ? "ROLL" : "TRIM"))
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

            if (_dragMode == SegmentDragMode.Move && _slipMode)
            {
                var slipDelta = SnapToFrame(absoluteSeconds - _dragAnchorSequenceSeconds);
                SegmentSlipRequested?.Invoke(_dragIndex, slipDelta);
                return;
            }

            var snappedX = timelineLeft + (int)Math.Round(((absoluteSeconds - visibleStart) / Math.Max(0.001, visibleDuration)) * timelineWidth);
            _previewInsertIndex = GetInsertIndex(snappedX, timelineLeft, timelineWidth, totalDuration);
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

            if (_dragMode == SegmentDragMode.Move && _dragIndex >= 0 && !_slipMode)
                SegmentMoved?.Invoke(_dragIndex, _previewInsertIndex < 0 ? _dragIndex : _previewInsertIndex, _previewTrack);
            else if (_dragMode == SegmentDragMode.Seek)
                UpdatePlayheadFromPoint(e.Location);

            _dragMode = SegmentDragMode.None;
            _dragIndex = -1;
            _dragOriginSegment = null;
            _previewInsertIndex = -1;
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

            using var borderPen = new Pen(Color.FromArgb(52, 52, 66));
            using var railBrush = new SolidBrush(Color.FromArgb(34, 34, 34));
            e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            if (_segments.Count == 0)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "Timeline is empty — mark an IN / OUT range and insert it here.",
                    Font,
                    new Rectangle(16, 16, Math.Max(40, Width - 32), Math.Max(20, Height - 32)),
                    Color.FromArgb(145, 145, 160),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                return;
            }

            var totalDuration = Math.Max(0.1, _segments.Max(segment => segment.SequenceEndSec));
            var timelineLeft = 72;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var rulerTop = 12;
            var laneTop = 34;
            var laneGap = Math.Max(6, Height / 24);
            var laneAreaHeight = Math.Max(96, Height - laneTop - 16);
            var videoLaneHeight = Math.Clamp((int)Math.Round(laneAreaHeight * 0.28), 30, 46);
            var audioLaneHeight = Math.Clamp((laneAreaHeight - (videoLaneHeight * 2) - (laneGap * 3)) / 2, 14, 24);
            var v1 = new Rectangle(timelineLeft, laneTop, timelineWidth, videoLaneHeight);
            var v2 = new Rectangle(timelineLeft, v1.Bottom + laneGap, timelineWidth, videoLaneHeight);
            var a1 = new Rectangle(timelineLeft, v2.Bottom + laneGap, timelineWidth, audioLaneHeight);
            var a2 = new Rectangle(timelineLeft, a1.Bottom + laneGap, timelineWidth, audioLaneHeight);

            var rulerRect = new Rectangle(timelineLeft, 10, timelineWidth, 18);
            using var rulerBrush = new SolidBrush(Color.FromArgb(17, 17, 17));
            using var labelBadgeBrush = new SolidBrush(Color.FromArgb(17, 17, 17));
            using var labelBorderPen = new Pen(Color.FromArgb(51, 51, 51));
            e.Graphics.FillRectangle(rulerBrush, rulerRect);
            e.Graphics.DrawRectangle(borderPen, rulerRect);

            _v1BadgeRect = new Rectangle(10, v1.Top + 6, 42, 18);
            _v2BadgeRect = new Rectangle(10, v2.Top + 6, 42, 18);
            _a1BadgeRect = new Rectangle(10, a1.Top, 42, 16);
            _a2BadgeRect = new Rectangle(10, a2.Top, 42, 16);
            using var activeBadgeBrush = new SolidBrush(Color.FromArgb(124, 58, 237));
            e.Graphics.FillRectangle(_targetVideoTrack == 1 ? activeBadgeBrush : labelBadgeBrush, _v1BadgeRect);
            e.Graphics.FillRectangle(_targetVideoTrack == 2 ? activeBadgeBrush : labelBadgeBrush, _v2BadgeRect);
            e.Graphics.FillRectangle(_targetAudioTrack == 1 ? activeBadgeBrush : labelBadgeBrush, _a1BadgeRect);
            e.Graphics.FillRectangle(_targetAudioTrack == 2 ? activeBadgeBrush : labelBadgeBrush, _a2BadgeRect);
            e.Graphics.DrawRectangle(labelBorderPen, _v1BadgeRect);
            e.Graphics.DrawRectangle(labelBorderPen, _v2BadgeRect);
            e.Graphics.DrawRectangle(labelBorderPen, _a1BadgeRect);
            e.Graphics.DrawRectangle(labelBorderPen, _a2BadgeRect);
            TextRenderer.DrawText(e.Graphics, $"V1{(_targetVideoTrack == 1 ? "*" : string.Empty)}", Font, _v1BadgeRect, Color.FromArgb(191, 219, 254), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, $"V2{(_targetVideoTrack == 2 ? "*" : string.Empty)}", Font, _v2BadgeRect, Color.FromArgb(191, 219, 254), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, $"A1{(_targetAudioTrack == 1 ? "*" : string.Empty)}", Font, _a1BadgeRect, Color.FromArgb(191, 219, 254), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, $"A2{(_targetAudioTrack == 2 ? "*" : string.Empty)}", Font, _a2BadgeRect, Color.FromArgb(191, 219, 254), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            e.Graphics.FillRectangle(railBrush, v1);
            e.Graphics.FillRectangle(railBrush, v2);
            e.Graphics.FillRectangle(railBrush, a1);
            e.Graphics.FillRectangle(railBrush, a2);
            e.Graphics.DrawRectangle(borderPen, v1);
            e.Graphics.DrawRectangle(borderPen, v2);
            e.Graphics.DrawRectangle(borderPen, a1);
            e.Graphics.DrawRectangle(borderPen, a2);

            using var secondPen = new Pen(Color.FromArgb(38, 38, 42));
            using var majorPen = new Pen(Color.FromArgb(64, 64, 72));
            var tickStart = Math.Floor(visibleStart);
            var tickEnd = Math.Ceiling(visibleStart + visibleDuration);
            for (var second = tickStart; second <= tickEnd; second += 1d)
            {
                var ratio = (second - visibleStart) / Math.Max(0.001, visibleDuration);
                if (ratio < 0 || ratio > 1)
                    continue;

                var x = timelineLeft + (int)Math.Round(ratio * timelineWidth);
                var isMajor = Math.Abs(second % 5d) < 0.001d;
                e.Graphics.DrawLine(isMajor ? majorPen : secondPen, x, rulerTop + 10, x, a2.Bottom);

                if (isMajor || visibleDuration <= 15)
                {
                    var stamp = FormatTime(second);
                    TextRenderer.DrawText(e.Graphics, stamp, new Font("Segoe UI", 7f), new Point(Math.Max(0, x - 16), rulerTop), Color.FromArgb(120, 120, 135));
                }
            }

            if (_razorMode)
            {
                var badgeRect = new Rectangle(Math.Max(timelineLeft, Width - 196), 8, 182, 18);
                using var badgeBrush = new SolidBrush(Color.FromArgb(170, 124, 58, 237));
                e.Graphics.FillRectangle(badgeBrush, badgeRect);
                TextRenderer.DrawText(e.Graphics, "RAZOR MODE • click to cut", new Font("Segoe UI", 7f, FontStyle.Bold), badgeRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            for (var index = 0; index < _segments.Count; index++)
            {
                var segment = _segments[index];
                var segmentStartInSequence = segment.SequenceStartSec;
                var segmentEndInSequence = segment.SequenceEndSec;
                if (segmentEndInSequence < visibleStart || segmentStartInSequence > visibleStart + visibleDuration)
                    continue;

                var startRatio = (segmentStartInSequence - visibleStart) / visibleDuration;
                var endRatio = (segmentEndInSequence - visibleStart) / visibleDuration;
                var startX = timelineLeft + (int)Math.Round(startRatio * timelineWidth);
                var endX = timelineLeft + (int)Math.Round(endRatio * timelineWidth);
                var blockWidth = Math.Max(38, endX - startX);
                var videoLane = segment.SafeTrack == 2 ? v2 : v1;
                var audioLane = segment.SafeTrack == 2 ? a2 : a1;
                var rect = new Rectangle(Math.Max(videoLane.Left, startX), videoLane.Top + 4, Math.Min(blockWidth, videoLane.Right - Math.Max(videoLane.Left, startX) - 1), videoLane.Height - 8);
                var audioBlock = new Rectangle(rect.Left, audioLane.Top + 2, rect.Width, audioLane.Height - 4);

                var fill = index == _selectedIndex ? Color.FromArgb(145, 88, 255) : (segment.SafeTrack == 2 ? Color.FromArgb(20, 184, 220) : Color.FromArgb(59, 130, 246));
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
                {
                    DrawImageWithOpacity(e.Graphics, waveform, rect, 0.22f);
                    DrawImageWithOpacity(e.Graphics, waveform, audioBlock, 0.55f);
                }
                e.Graphics.DrawRectangle(activePen, rect);
                e.Graphics.DrawRectangle(activePen, audioBlock);

                if (index == _selectedIndex)
                {
                    var leftGrip = new Rectangle(rect.Left - 4, rect.Top + 3, 8, rect.Height - 6);
                    var rightGrip = new Rectangle(rect.Right - 4, rect.Top + 3, 8, rect.Height - 6);
                    e.Graphics.FillRectangle(handleBrush, leftGrip);
                    e.Graphics.FillRectangle(handleBrush, rightGrip);
                    e.Graphics.DrawRectangle(Pens.Black, leftGrip);
                    e.Graphics.DrawRectangle(Pens.Black, rightGrip);
                    for (var grip = 0; grip < 3; grip++)
                    {
                        var leftX = leftGrip.Left + 2 + (grip * 2);
                        var rightX = rightGrip.Left + 2 + (grip * 2);
                        e.Graphics.DrawLine(Pens.DimGray, leftX, leftGrip.Top + 4, leftX, leftGrip.Bottom - 4);
                        e.Graphics.DrawLine(Pens.DimGray, rightX, rightGrip.Top + 4, rightX, rightGrip.Bottom - 4);
                    }
                }

                var tagRect = new Rectangle(rect.Left + 6, rect.Top + Math.Max(16, rect.Height - 18), Math.Min(Math.Max(52, rect.Width - 12), 78), 12);
                using var tagBrush = new SolidBrush(Color.FromArgb(96, 15, 23, 42));
                using var labelBackBrush = new SolidBrush(Color.FromArgb(165, 0, 0, 0));
                var label = Path.GetFileName(segment.SourceFile);
                var labelRect = new Rectangle(rect.Left + 4, rect.Top + 4, Math.Max(24, rect.Width - 8), 14);
                e.Graphics.FillRectangle(labelBackBrush, labelRect);
                TextRenderer.DrawText(e.Graphics, label, new Font("Segoe UI", 7f, FontStyle.Bold), labelRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                e.Graphics.FillRectangle(tagBrush, tagRect);
                TextRenderer.DrawText(e.Graphics, $"V{segment.SafeTrack} @ {FormatTime(segment.SequenceStartSec)}", new Font("Segoe UI", 6.25f, FontStyle.Bold), tagRect, Color.FromArgb(226, 232, 240), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                _hitTargets.Add((rect, index));
            }

            if (_dragMode == SegmentDragMode.Move && _previewInsertIndex >= 0)
            {
                var insertRatio = GetInsertRatio(_previewInsertIndex, totalDuration, visibleStart, visibleDuration);
                var insertX = timelineLeft + (int)Math.Round(insertRatio * timelineWidth);
                var targetLane = _previewTrack == 2 ? v2 : v1;
                var targetAudioLane = _previewTrack == 2 ? a2 : a1;

                if (_dragOriginSegment != null)
                {
                    var ghostWidth = Math.Max(40, (int)Math.Round((_dragOriginSegment.Duration / Math.Max(0.001, visibleDuration)) * timelineWidth));
                    ghostWidth = Math.Min(ghostWidth, Math.Max(40, targetLane.Width - 4));
                    var ghostLeft = Math.Clamp(insertX + 2, targetLane.Left + 1, targetLane.Right - ghostWidth - 1);
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
                }

                var indicatorColor = _rippleMoveMode ? Color.FromArgb(251, 191, 36) : Color.FromArgb(248, 113, 113);
                using var insertPen = new Pen(indicatorColor, 2);
                using var insertGlowPen = new Pen(Color.FromArgb(120, indicatorColor), 6);
                e.Graphics.DrawLine(insertGlowPen, insertX, targetLane.Top - 4, insertX, a2.Bottom + 4);
                e.Graphics.DrawLine(insertPen, insertX, targetLane.Top - 4, insertX, a2.Bottom + 4);
            }

            if (!double.IsNaN(_snapIndicatorSeconds))
            {
                var snapX = timelineLeft + (int)Math.Round(((Math.Clamp(_snapIndicatorSeconds, visibleStart, visibleStart + visibleDuration) - visibleStart) / visibleDuration) * timelineWidth);
                using var snapPen = new Pen(Color.FromArgb(34, 211, 238), 2);
                using var snapGlowPen = new Pen(Color.FromArgb(120, 34, 211, 238), 6);
                using var snapBrush = new SolidBrush(Color.FromArgb(34, 211, 238));
                e.Graphics.DrawLine(snapGlowPen, snapX, rulerTop + 10, snapX, a2.Bottom + 6);
                e.Graphics.DrawLine(snapPen, snapX, rulerTop + 10, snapX, a2.Bottom + 6);
                var snapRect = new Rectangle(Math.Max(timelineLeft, snapX - 24), rulerTop - 10, 48, 14);
                e.Graphics.FillRectangle(snapBrush, snapRect);
                TextRenderer.DrawText(e.Graphics, _snapIndicatorLabel, new Font("Segoe UI", 6.5f, FontStyle.Bold), snapRect, Color.FromArgb(15, 23, 42), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var selectedOffset = Math.Clamp(_playheadSeconds, 0, totalDuration);
            if (_selectedIndex >= 0 && _selectedIndex < _segments.Count)
            {
                var selectedSegment = _segments[_selectedIndex];
                var localOffset = Math.Clamp(_playheadSeconds - selectedSegment.StartSec, 0, selectedSegment.Duration);
                selectedOffset = Math.Clamp(selectedSegment.SequenceStartSec + localOffset, 0, totalDuration);
            }

            var playheadX = timelineLeft + (int)Math.Round(((selectedOffset - visibleStart) / visibleDuration) * timelineWidth);
            using var playheadPen = new Pen(Color.FromArgb(248, 113, 113), 2);
            using var playheadBrush = new SolidBrush(Color.FromArgb(248, 113, 113));
            e.Graphics.DrawLine(playheadPen, playheadX, rulerTop + 10, playheadX, a2.Bottom + 8);
            e.Graphics.FillPolygon(playheadBrush,
            [
                new Point(playheadX, rulerTop + 6),
                new Point(playheadX - 8, rulerTop - 4),
                new Point(playheadX + 8, rulerTop - 4),
            ]);
            e.Graphics.FillEllipse(playheadBrush, playheadX - 4, a2.Bottom + 6, 8, 8);
        }

        private void ShowGapContextMenu(Point location)
        {
            if (_segments.Count < 2)
                return;

            var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(location));
            if (hit.Rect != Rectangle.Empty || location.X < 72 || location.Y < 30)
                return;

            var totalDuration = Math.Max(0.1, _segments.Max(segment => segment.SequenceEndSec));
            var timelineLeft = 72;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            _gapContextInsertIndex = GetInsertIndex(location.X, timelineLeft, timelineWidth, totalDuration);
            _gapContextTrack = GetTrackForY(location.Y);
            if (_gapContextMenu.Items[0] is ToolStripMenuItem item)
                item.Text = $"Ripple Delete Gap on V{_gapContextTrack}";
            _gapContextMenu.Show(this, location);
        }

        private void UpdateCursor(Point location)
        {
            if (_razorMode)
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
                : Cursors.SizeAll;
        }

        private void UpdatePlayheadFromPoint(Point location)
        {
            var totalDuration = Math.Max(0.1, _segments.Max(segment => segment.SequenceEndSec));
            var timelineLeft = 72;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var ratio = Math.Clamp((location.X - timelineLeft) / (double)Math.Max(1, timelineWidth), 0, 1);
            _playheadSeconds = Math.Clamp(visibleStart + (ratio * visibleDuration), 0, totalDuration);
            SeekRequested?.Invoke(_playheadSeconds);
            Invalidate();
        }

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
                return SnapToFrame(absoluteSeconds);
            }

            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var threshold = Math.Max(0.05, visibleDuration * 0.02);
            var snapPoints = new List<double> { 0, totalDuration };
            foreach (var segment in _segments)
            {
                snapPoints.Add(segment.SequenceStartSec);
                snapPoints.Add(segment.SequenceEndSec);
            }

            for (var tick = Math.Ceiling(visibleStart); tick <= visibleStart + visibleDuration; tick += 1d)
                snapPoints.Add(tick);

            var nearest = snapPoints.OrderBy(value => Math.Abs(value - absoluteSeconds)).FirstOrDefault();
            snapped = Math.Abs(nearest - absoluteSeconds) <= threshold;
            return SnapToFrame(snapped ? nearest : absoluteSeconds);
        }

        private int GetInsertIndex(int x, int timelineLeft, int timelineWidth, double totalDuration)
        {
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var ordered = _segments.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                var segment = ordered[index];
                var midpointRatio = ((segment.SequenceStartSec + (segment.Duration / 2d)) - visibleStart) / visibleDuration;
                var midpointX = timelineLeft + (int)Math.Round(midpointRatio * timelineWidth);
                if (x < midpointX)
                    return index;
            }
            return ordered.Count;
        }

        private double GetInsertRatio(int insertIndex, double totalDuration, double visibleStart, double visibleDuration)
        {
            var ordered = _segments.OrderBy(segment => segment.SequenceStartSec).ThenBy(segment => segment.SafeTrack).ToList();
            if (ordered.Count == 0 || insertIndex <= 0)
                return Math.Clamp((0 - visibleStart) / visibleDuration, 0, 1);
            if (insertIndex >= ordered.Count)
            {
                var total = ordered.Max(segment => segment.SequenceEndSec);
                return Math.Clamp((total - visibleStart) / visibleDuration, 0, 1);
            }

            var insertTime = ordered[insertIndex].SequenceStartSec;
            return Math.Clamp((insertTime - visibleStart) / visibleDuration, 0, 1);
        }

        private int GetTrackForY(int y)
        {
            var midpoint = Math.Max(60, Height / 2);
            return y <= midpoint ? 1 : 2;
        }
    }

    private sealed class EditorPreviewBox : PictureBox
    {
        private enum CropDragMode
        {
            None,
            Draw,
            Move,
            ResizeTopLeft,
            ResizeTopRight,
            ResizeBottomLeft,
            ResizeBottomRight,
        }

        private bool _dragging;
        private Point _dragStart;
        private Rectangle _dragOriginCropRect;
        private CropDragMode _dragMode;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Size VideoSize { get; set; } = Size.Empty;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Rectangle CropRect { get; private set; } = Rectangle.Empty;

        [System.ComponentModel.DefaultValue(true)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowCropOverlay { get; set; } = true;
        public event Action<Rectangle>? CropChanged;

        public EditorPreviewBox()
        {
            BackColor = Color.FromArgb(10, 10, 12);
            BorderStyle = BorderStyle.FixedSingle;
            SizeMode = PictureBoxSizeMode.Zoom;
            Cursor = Cursors.Cross;
        }

        public void SetPreviewImage(Image image)
        {
            var old = Image;
            Image = image;
            old?.Dispose();
            Invalidate();
        }

        public Image? ClonePreviewImage() => Image is null ? null : new Bitmap(Image);

        public void ClearPreview()
        {
            var old = Image;
            Image = null;
            old?.Dispose();
            CropRect = Rectangle.Empty;
            Invalidate();
        }

        public void DisposePreviewImage()
        {
            var old = Image;
            Image = null;
            old?.Dispose();
        }

        public void SetCropRect(Rectangle rect)
        {
            CropRect = NormalizeToVideo(rect);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!ShowCropOverlay || e.Button != MouseButtons.Left || VideoSize.Width <= 0 || VideoSize.Height <= 0)
                return;

            var imgRect = GetImageBounds();
            if (!imgRect.Contains(e.Location))
                return;

            _dragging = true;
            _dragStart = e.Location;
            _dragOriginCropRect = CropRect;
            _dragMode = GetDragMode(e.Location, imgRect);

            if (_dragMode == CropDragMode.None)
                _dragMode = CropDragMode.Draw;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_dragging)
            {
                UpdateCursor(e.Location);
                return;
            }

            var rect = BuildDragRect(e.Location);
            if (rect.Width > 1 && rect.Height > 1)
            {
                CropRect = rect;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;
            var rect = BuildDragRect(e.Location);
            if (rect.Width > 1 && rect.Height > 1)
            {
                CropRect = rect;
                CropChanged?.Invoke(CropRect);
            }

            _dragMode = CropDragMode.None;
            UpdateCursor(e.Location);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            if (!ShowCropOverlay || Image == null || VideoSize.Width <= 0 || VideoSize.Height <= 0 || CropRect.Width <= 0 || CropRect.Height <= 0)
                return;

            var imgRect = GetImageBounds();
            var overlayRect = ToDisplayRect(CropRect, imgRect);
            using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            using var pen = new Pen(Color.FromArgb(124, 58, 237), 2);
            using var gridPen = new Pen(Color.FromArgb(210, 221, 214, 254), 1)
            {
                DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            };
            using var handleBrush = new SolidBrush(Color.FromArgb(245, 245, 250));
            using var handleBorder = new Pen(Color.FromArgb(60, 60, 70), 1);
            using var labelBrush = new SolidBrush(Color.FromArgb(180, 12, 12, 15));

            pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, imgRect.Top, imgRect.Width, Math.Max(0, overlayRect.Top - imgRect.Top)));
            pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, overlayRect.Bottom, imgRect.Width, Math.Max(0, imgRect.Bottom - overlayRect.Bottom)));
            pe.Graphics.FillRectangle(shade, new Rectangle(imgRect.Left, overlayRect.Top, Math.Max(0, overlayRect.Left - imgRect.Left), overlayRect.Height));
            pe.Graphics.FillRectangle(shade, new Rectangle(overlayRect.Right, overlayRect.Top, Math.Max(0, imgRect.Right - overlayRect.Right), overlayRect.Height));
            pe.Graphics.DrawRectangle(pen, overlayRect);

            var thirdWidth = overlayRect.Width / 3f;
            var thirdHeight = overlayRect.Height / 3f;
            pe.Graphics.DrawLine(gridPen, overlayRect.Left + thirdWidth, overlayRect.Top, overlayRect.Left + thirdWidth, overlayRect.Bottom);
            pe.Graphics.DrawLine(gridPen, overlayRect.Left + (thirdWidth * 2), overlayRect.Top, overlayRect.Left + (thirdWidth * 2), overlayRect.Bottom);
            pe.Graphics.DrawLine(gridPen, overlayRect.Left, overlayRect.Top + thirdHeight, overlayRect.Right, overlayRect.Top + thirdHeight);
            pe.Graphics.DrawLine(gridPen, overlayRect.Left, overlayRect.Top + (thirdHeight * 2), overlayRect.Right, overlayRect.Top + (thirdHeight * 2));

            var labelRect = new Rectangle(overlayRect.Left + 8, Math.Max(imgRect.Top + 6, overlayRect.Top + 8), 120, 20);
            pe.Graphics.FillRectangle(labelBrush, labelRect);
            TextRenderer.DrawText(pe.Graphics, $"{CropRect.Width}×{CropRect.Height}", Font, labelRect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            foreach (var handle in GetHandleRects(overlayRect))
            {
                pe.Graphics.FillRectangle(handleBrush, handle);
                pe.Graphics.DrawRectangle(handleBorder, handle);
            }
        }

        private Rectangle BuildDragRect(Point currentPoint)
        {
            return _dragMode switch
            {
                CropDragMode.Move => MoveCrop(currentPoint),
                CropDragMode.ResizeTopLeft => ResizeCrop(currentPoint, resizeLeft: true, resizeTop: true, resizeRight: false, resizeBottom: false),
                CropDragMode.ResizeTopRight => ResizeCrop(currentPoint, resizeLeft: false, resizeTop: true, resizeRight: true, resizeBottom: false),
                CropDragMode.ResizeBottomLeft => ResizeCrop(currentPoint, resizeLeft: true, resizeTop: false, resizeRight: false, resizeBottom: true),
                CropDragMode.ResizeBottomRight => ResizeCrop(currentPoint, resizeLeft: false, resizeTop: false, resizeRight: true, resizeBottom: true),
                _ => BuildCropRectFromPoints(_dragStart, currentPoint),
            };
        }

        private Rectangle MoveCrop(Point currentPoint)
        {
            var start = ClientToVideoPoint(_dragStart, clampToImage: true) ?? new Point(_dragOriginCropRect.Left, _dragOriginCropRect.Top);
            var current = ClientToVideoPoint(currentPoint, clampToImage: true) ?? start;
            var dx = current.X - start.X;
            var dy = current.Y - start.Y;

            var left = Math.Clamp(_dragOriginCropRect.Left + dx, 0, Math.Max(0, VideoSize.Width - _dragOriginCropRect.Width));
            var top = Math.Clamp(_dragOriginCropRect.Top + dy, 0, Math.Max(0, VideoSize.Height - _dragOriginCropRect.Height));
            return new Rectangle(left, top, _dragOriginCropRect.Width, _dragOriginCropRect.Height);
        }

        private Rectangle ResizeCrop(Point currentPoint, bool resizeLeft, bool resizeTop, bool resizeRight, bool resizeBottom)
        {
            var anchor = ClientToVideoPoint(currentPoint, clampToImage: true) ?? new Point(_dragOriginCropRect.Right, _dragOriginCropRect.Bottom);
            var left = resizeLeft ? anchor.X : _dragOriginCropRect.Left;
            var top = resizeTop ? anchor.Y : _dragOriginCropRect.Top;
            var right = resizeRight ? anchor.X : _dragOriginCropRect.Right;
            var bottom = resizeBottom ? anchor.Y : _dragOriginCropRect.Bottom;
            return NormalizeToVideo(Rectangle.FromLTRB(Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom)));
        }

        private void UpdateCursor(Point location)
        {
            if (!ShowCropOverlay || Image == null)
            {
                Cursor = Cursors.Default;
                return;
            }

            var imgRect = GetImageBounds();
            if (!imgRect.Contains(location))
            {
                Cursor = Cursors.Default;
                return;
            }

            Cursor = GetDragMode(location, imgRect) switch
            {
                CropDragMode.Move => Cursors.SizeAll,
                CropDragMode.ResizeTopLeft or CropDragMode.ResizeBottomRight => Cursors.SizeNWSE,
                CropDragMode.ResizeTopRight or CropDragMode.ResizeBottomLeft => Cursors.SizeNESW,
                _ => Cursors.Cross,
            };
        }

        private CropDragMode GetDragMode(Point location, Rectangle imageRect)
        {
            if (CropRect.Width <= 0 || CropRect.Height <= 0)
                return CropDragMode.Draw;

            var overlayRect = ToDisplayRect(CropRect, imageRect);
            var handles = GetHandleRects(overlayRect).ToArray();
            if (handles[0].Contains(location)) return CropDragMode.ResizeTopLeft;
            if (handles[1].Contains(location)) return CropDragMode.ResizeTopRight;
            if (handles[2].Contains(location)) return CropDragMode.ResizeBottomLeft;
            if (handles[3].Contains(location)) return CropDragMode.ResizeBottomRight;
            if (overlayRect.Contains(location)) return CropDragMode.Move;
            return CropDragMode.Draw;
        }

        private IEnumerable<Rectangle> GetHandleRects(Rectangle overlayRect)
        {
            const int size = 8;
            yield return new Rectangle(overlayRect.Left - (size / 2), overlayRect.Top - (size / 2), size, size);
            yield return new Rectangle(overlayRect.Right - (size / 2), overlayRect.Top - (size / 2), size, size);
            yield return new Rectangle(overlayRect.Left - (size / 2), overlayRect.Bottom - (size / 2), size, size);
            yield return new Rectangle(overlayRect.Right - (size / 2), overlayRect.Bottom - (size / 2), size, size);
        }

        private Rectangle BuildCropRectFromPoints(Point start, Point end)
        {
            var first = ClientToVideoPoint(start, clampToImage: true);
            var second = ClientToVideoPoint(end, clampToImage: true);
            if (first == null || second == null)
                return CropRect;

            var left = Math.Min(first.Value.X, second.Value.X);
            var top = Math.Min(first.Value.Y, second.Value.Y);
            var right = Math.Max(first.Value.X, second.Value.X);
            var bottom = Math.Max(first.Value.Y, second.Value.Y);
            return NormalizeToVideo(Rectangle.FromLTRB(left, top, right, bottom));
        }

        private Rectangle NormalizeToVideo(Rectangle rect)
        {
            if (VideoSize.Width <= 0 || VideoSize.Height <= 0)
                return Rectangle.Empty;

            var left = Math.Clamp(rect.Left, 0, VideoSize.Width - 1);
            var top = Math.Clamp(rect.Top, 0, VideoSize.Height - 1);
            var right = Math.Clamp(rect.Right, left + 1, VideoSize.Width);
            var bottom = Math.Clamp(rect.Bottom, top + 1, VideoSize.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Point? ClientToVideoPoint(Point point, bool clampToImage)
        {
            var rect = GetImageBounds();
            if (VideoSize.Width <= 0 || VideoSize.Height <= 0)
                return null;

            var px = clampToImage ? Math.Clamp(point.X, rect.Left, rect.Right) : point.X;
            var py = clampToImage ? Math.Clamp(point.Y, rect.Top, rect.Bottom) : point.Y;
            if (!clampToImage && !rect.Contains(point))
                return null;

            var x = (px - rect.Left) * VideoSize.Width / Math.Max(1, rect.Width);
            var y = (py - rect.Top) * VideoSize.Height / Math.Max(1, rect.Height);
            return new Point(Math.Clamp(x, 0, VideoSize.Width - 1), Math.Clamp(y, 0, VideoSize.Height - 1));
        }

        private Rectangle ToDisplayRect(Rectangle cropRect, Rectangle imageRect)
        {
            var x = imageRect.Left + (int)Math.Round(cropRect.X * imageRect.Width / (double)Math.Max(1, VideoSize.Width));
            var y = imageRect.Top + (int)Math.Round(cropRect.Y * imageRect.Height / (double)Math.Max(1, VideoSize.Height));
            var width = (int)Math.Round(cropRect.Width * imageRect.Width / (double)Math.Max(1, VideoSize.Width));
            var height = (int)Math.Round(cropRect.Height * imageRect.Height / (double)Math.Max(1, VideoSize.Height));
            return new Rectangle(x, y, Math.Max(2, width), Math.Max(2, height));
        }

        private Rectangle GetImageBounds()
        {
            if (Image == null)
                return ClientRectangle;

            var imageRatio = Image.Width / (double)Math.Max(1, Image.Height);
            var boxRatio = Width / (double)Math.Max(1, Height);
            if (imageRatio > boxRatio)
            {
                var drawHeight = (int)Math.Round(Width / imageRatio);
                var y = (Height - drawHeight) / 2;
                return new Rectangle(0, y, Width, drawHeight);
            }

            var drawWidth = (int)Math.Round(Height * imageRatio);
            var x = (Width - drawWidth) / 2;
            return new Rectangle(x, 0, drawWidth, Height);
        }
    }
}
