namespace VeloUploader;

using System.Diagnostics;
using System.Globalization;
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
    private readonly Label _cropInfoLabel;
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
    private readonly Label _statusLabel;
    private readonly Button _trimButton;
    private readonly Button _cropButton;
    private readonly Button _mergeButton;
    private readonly Button _addCutButton;
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
    private double _requestedPreviewTime;
    private TimelineEditMode _timelineEditMode = TimelineEditMode.Select;
    private CancellationTokenSource? _previewCts;
    private readonly List<TimelineSegment> _sequenceSegments = [];
    private readonly Dictionary<string, Image> _mediaThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Image>> _trimThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _waveformCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<double>> _clipMarkers = new(StringComparer.OrdinalIgnoreCase);
    private double _timelineZoom = 1;

    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    private sealed record VideoDetails(double Duration, int Width, int Height);
    private sealed record TimelineSegment(string SourceFile, double StartSec, double EndSec, int Track = 1)
    {
        public int SafeTrack => Math.Clamp(Track, 1, 2);
        public double Duration => Math.Max(0, EndSec - StartSec);
        public override string ToString() => $"[V{SafeTrack}] {Path.GetFileName(SourceFile)}  •  {FormatTime(StartSec)} → {FormatTime(EndSec)}  ({FormatTime(Duration)})";
    }

    private enum TimelineEditMode
    {
        Select,
        Razor,
    }

    public QuickEditForm(string defaultOutputFolder)
    {
        var outputFolder = Directory.Exists(defaultOutputFolder)
            ? defaultOutputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Text = "VELO Video Editor";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(12, 12, 15);
        ForeColor = Color.FromArgb(240, 240, 245);
        Font = new Font("Segoe UI", 9f);
        KeyPreview = true;

        try
        {
            using var stream = typeof(QuickEditForm).Assembly.GetManifestResourceStream("velo.ico");
            if (stream != null) Icon = new Icon(stream);
        }
        catch
        {
        }

        var title = new Label
        {
            Text = "Premiere-style timeline editor",
            AutoSize = true,
            Location = new Point(20, 14),
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Color.White,
        };
        Controls.Add(title);

        var hint = new Label
        {
            Text = "Import footage, mark an IN / OUT range, insert it into the timeline, and switch between Select and Razor tools like a real editor.",
            AutoSize = false,
            Size = new Size(1320, 40),
            Location = new Point(20, 42),
            ForeColor = Color.FromArgb(155, 155, 165),
        };
        Controls.Add(hint);

        var leftPanel = new Panel
        {
            Location = new Point(20, 92),
            Size = new Size(260, 680),
            BackColor = Color.FromArgb(18, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
        };
        Controls.Add(leftPanel);

        leftPanel.Controls.Add(BuildSectionLabel("Project / media bin", 12, 12));
        leftPanel.Controls.Add(BuildSmallLabel("Import clips, preview their thumbnails, then mark ranges to send into the timeline.", 12, 34, 248));

        _mediaThumbStrip = new FlowLayoutPanel
        {
            Location = new Point(12, 64),
            Size = new Size(236, 92),
            BackColor = Color.FromArgb(14, 14, 18),
            BorderStyle = BorderStyle.FixedSingle,
            WrapContents = true,
            AutoScroll = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(4),
        };
        leftPanel.Controls.Add(_mediaThumbStrip);

        _filesList = new ListBox
        {
            Location = new Point(12, 166),
            Size = new Size(236, 406),
            HorizontalScrollbar = true,
            SelectionMode = SelectionMode.MultiExtended,
            AllowDrop = true,
            BackColor = Color.FromArgb(14, 14, 18),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
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
            Location = new Point(296, 92),
            Size = new Size(760, 680),
            BackColor = Color.FromArgb(18, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(centerPanel);

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
        centerPanel.Controls.Add(_previewTimeLabel);

        _playerStatusLabel = BuildSmallLabel("Load a clip to start playback.", 390, 274, 354);
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

        _playPauseButton = BuildActionButton("Play", 14, 468, 76, (_, _) => TogglePlayback());
        centerPanel.Controls.Add(_playPauseButton);

        _jumpBackButton = BuildButton("« 5s", 98, 468, 58, (_, _) => SkipSeconds(-5));
        centerPanel.Controls.Add(_jumpBackButton);

        _jumpForwardButton = BuildButton("5s »", 164, 468, 58, (_, _) => SkipSeconds(5));
        centerPanel.Controls.Add(_jumpForwardButton);

        _selectToolButton = BuildButton("Select (V)", 238, 468, 88, (_, _) => SetTimelineEditMode(TimelineEditMode.Select));
        centerPanel.Controls.Add(_selectToolButton);

        _razorToolButton = BuildButton("Razor (C)", 334, 468, 88, (_, _) => SetTimelineEditMode(TimelineEditMode.Razor));
        centerPanel.Controls.Add(_razorToolButton);

        _timelineModeLabel = BuildSmallLabel("Selection tool active — press C for Razor or Ctrl+K to cut at the playhead.", 430, 466, 314);
        centerPanel.Controls.Add(_timelineModeLabel);

        var sequenceSectionLabel = BuildSectionLabel("Sequence timeline", 14, 512);
        centerPanel.Controls.Add(sequenceSectionLabel);
        var timelineHint = BuildSmallLabel("Use Select to move or trim clips, switch to Razor to click-cut them instantly, and scroll the mouse wheel to zoom the timeline.", 14, 534, 720);
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
        _sequenceTimelineView.ZoomDeltaRequested += AdjustTimelineZoom;
        _sequenceTimelineView.SetWaveformProvider(file => _waveformCache.TryGetValue(file, out var waveform) ? waveform : null);
        centerPanel.Controls.Add(_sequenceTimelineView);

        var rightPanel = new Panel
        {
            Location = new Point(1072, 92),
            Size = new Size(300, 680),
            BackColor = Color.FromArgb(18, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            AutoScroll = true,
        };
        Controls.Add(rightPanel);

        int y = 12;
        rightPanel.Controls.Add(BuildSectionLabel("Inspector / export", 14, y));
        y += 22;
        rightPanel.Controls.Add(BuildSmallLabel("This panel mirrors the program output and gives you the trim, crop, and export controls.", 14, y, 260));
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

        _markerHintLabel = BuildSmallLabel("Single clip: Save clip from IN/OUT range. Full edit: Save full timeline as video.", 14, y, 260);
        rightPanel.Controls.Add(_markerHintLabel);
        y += 34;

        _addCutButton = BuildButton("Insert range into timeline", 14, y, 170, (_, _) => AddCurrentCutToSequence());
        rightPanel.Controls.Add(_addCutButton);
        _splitPlayheadButton = BuildButton("Cut at playhead", 192, y, 82, (_, _) => SplitSelectedSegmentAtPlayhead());
        rightPanel.Controls.Add(_splitPlayheadButton);
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

        rightPanel.Controls.Add(BuildSectionLabel("Timeline / sequence", 14, y));
        y += 22;
        _sequenceHintLabel = BuildSmallLabel("Insert cuts here, then move them with Select or slice them with Razor. Double-click any cut to reload it in the monitors.", 14, y, 260);
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
        };
        _sequenceList.DoubleClick += async (_, _) => await LoadSequenceSegmentAsync(_sequenceList.SelectedIndex);
        rightPanel.Controls.Add(_sequenceList);
        y += 128;

        rightPanel.Controls.Add(BuildButton("Remove", 14, y, 70, (_, _) => RemoveSelectedSequenceSegment()));
        rightPanel.Controls.Add(BuildButton("Up", 92, y, 42, (_, _) => MoveSelectedSequenceSegment(-1)));
        rightPanel.Controls.Add(BuildButton("Down", 142, y, 52, (_, _) => MoveSelectedSequenceSegment(1)));
        rightPanel.Controls.Add(BuildButton("Clear", 202, y, 72, (_, _) => ClearSequence()));
        y += 38;

        _sequenceSummaryLabel = BuildSmallLabel("Timeline empty — insert a range to start cutting.", 14, y, 260);
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
            ForeColor = Color.FromArgb(155, 155, 165),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Controls.Add(_statusLabel);

        void LayoutWorkspace()
        {
            const int outerMargin = 20;
            const int top = 92;
            const int gap = 14;
            var panelHeight = Math.Max(600, ClientSize.Height - top - 88);
            var leftWidth = Math.Clamp((int)Math.Round(ClientSize.Width * 0.19), 246, 280);
            var rightWidth = Math.Clamp((int)Math.Round(ClientSize.Width * 0.21), 284, 320);

            leftPanel.Bounds = new Rectangle(outerMargin, top, leftWidth, panelHeight);
            rightPanel.Bounds = new Rectangle(ClientSize.Width - outerMargin - rightWidth, top, rightWidth, panelHeight);
            centerPanel.Bounds = new Rectangle(leftPanel.Right + gap, top, Math.Max(600, rightPanel.Left - gap - (leftPanel.Right + gap)), panelHeight);

            _mediaThumbStrip.Size = new Size(leftPanel.ClientSize.Width - 24, 92);
            _filesList.Size = new Size(leftPanel.ClientSize.Width - 24, Math.Max(220, leftPanel.ClientSize.Height - 274));

            const int margin = 16;
            const int monitorGap = 12;
            var availableWidth = Math.Max(320, centerPanel.ClientSize.Width - (margin * 2));
            var stackMonitors = availableWidth < 760;
            var sourceWidth = stackMonitors
                ? availableWidth
                : Math.Clamp((int)Math.Round(availableWidth * 0.33), 220, 320);
            var programX = stackMonitors ? margin : margin + sourceWidth + monitorGap;
            var programWidth = stackMonitors
                ? availableWidth
                : Math.Max(320, availableWidth - sourceWidth - monitorGap);
            var sourceHeight = stackMonitors
                ? Math.Clamp((int)Math.Round(Math.Min(centerPanel.ClientSize.Height * 0.19, availableWidth / 2.1)), 170, 220)
                : Math.Clamp((int)Math.Round(Math.Min(centerPanel.ClientSize.Height * 0.32, sourceWidth / 1.45)), 210, 280);
            var programHeight = stackMonitors
                ? Math.Clamp(sourceHeight + 22, 200, 260)
                : Math.Clamp((int)Math.Round(Math.Min(centerPanel.ClientSize.Height * 0.34, programWidth / 1.65)), 220, 300);

            sourceMonitorLabel.Location = new Point(margin, 14);
            _videoInfoLabel.Location = new Point(margin, 36);
            _videoInfoLabel.Size = new Size(centerPanel.ClientSize.Width - (margin * 2), 30);

            _sourcePreview.Bounds = new Rectangle(margin, 68, sourceWidth, sourceHeight);

            if (stackMonitors)
            {
                programMonitorLabel.Location = new Point(margin, _sourcePreview.Bottom + 12);
                _playerHost.Bounds = new Rectangle(margin, programMonitorLabel.Bottom + 10, programWidth, programHeight);
            }
            else
            {
                programMonitorLabel.Location = new Point(programX, 14);
                _playerHost.Bounds = new Rectangle(programX, 68, programWidth, programHeight);
            }

            _previewTimeLabel.Location = new Point(margin, _playerHost.Bottom + 10);
            _previewTimeLabel.Size = new Size(Math.Max(220, sourceWidth + 40), 24);
            _playerStatusLabel.Location = new Point(stackMonitors ? margin + 232 : programX, _playerHost.Bottom + 10);
            _playerStatusLabel.Size = new Size(stackMonitors ? Math.Max(180, availableWidth - 232) : programWidth, 24);

            var trimHeaderY = _playerHost.Bottom + 38;
            trimSectionLabel.Location = new Point(margin, trimHeaderY);
            trimHint.Location = new Point(margin, trimHeaderY + 20);
            trimHint.Size = new Size(centerPanel.ClientSize.Width - (margin * 2), 30);

            _trimTimelineView.Bounds = new Rectangle(margin, trimHint.Bottom + 6, centerPanel.ClientSize.Width - (margin * 2), Math.Clamp((int)Math.Round(centerPanel.ClientSize.Height * 0.14), 92, 116));
            _refreshPreviewButton.Location = new Point(centerPanel.ClientSize.Width - margin - _refreshPreviewButton.Width, _trimTimelineView.Bottom + 10);
            _timelineBar.Bounds = new Rectangle(margin, _trimTimelineView.Bottom + 12, Math.Max(240, _refreshPreviewButton.Left - margin - 8), 28);

            var transportY = _timelineBar.Bottom + 10;
            _playPauseButton.Location = new Point(margin, transportY);
            _jumpBackButton.Location = new Point(_playPauseButton.Right + 10, transportY);
            _jumpForwardButton.Location = new Point(_jumpBackButton.Right + 10, transportY);

            var compactToolRow = centerPanel.ClientSize.Width < 760;
            if (compactToolRow)
            {
                _selectToolButton.Location = new Point(margin, _playPauseButton.Bottom + 8);
                _razorToolButton.Location = new Point(_selectToolButton.Right + 8, _selectToolButton.Top);
                _timelineModeLabel.Location = new Point(_razorToolButton.Right + 10, _selectToolButton.Top - 2);
                _timelineModeLabel.Size = new Size(Math.Max(140, centerPanel.ClientSize.Width - _timelineModeLabel.Left - margin), 34);
            }
            else
            {
                _selectToolButton.Location = new Point(_jumpForwardButton.Right + 18, transportY);
                _razorToolButton.Location = new Point(_selectToolButton.Right + 8, transportY);
                _timelineModeLabel.Location = new Point(_razorToolButton.Right + 12, transportY - 2);
                _timelineModeLabel.Size = new Size(Math.Max(160, centerPanel.ClientSize.Width - _timelineModeLabel.Left - margin), 34);
            }

            var toolRowBottom = Math.Max(_playPauseButton.Bottom, Math.Max(_selectToolButton.Bottom, _timelineModeLabel.Bottom));
            var sequenceHeaderY = toolRowBottom + 22;
            sequenceSectionLabel.Location = new Point(margin, sequenceHeaderY);
            timelineHint.Location = new Point(margin, sequenceHeaderY + 20);
            timelineHint.Size = new Size(centerPanel.ClientSize.Width - (margin * 2), 30);
            _sequenceTimelineView.Bounds = new Rectangle(margin, timelineHint.Bottom + 6, centerPanel.ClientSize.Width - (margin * 2), Math.Max(178, centerPanel.ClientSize.Height - timelineHint.Bottom - 20));

            _statusLabel.Location = new Point(20, ClientSize.Height - 48);
            _statusLabel.Size = new Size(ClientSize.Width - 40, 24);
        }

        SizeChanged += (_, _) => LayoutWorkspace();
        centerPanel.SizeChanged += (_, _) => LayoutWorkspace();
        KeyDown += OnEditorKeyDown;
        LayoutWorkspace();
        UpdateTimelineZoom();
        SetTimelineEditMode(TimelineEditMode.Select);

        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 280 };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer.Stop();
            await RefreshPreviewAsync(_requestedPreviewTime);
        };

        _playerTimer = new System.Windows.Forms.Timer { Interval = 180 };
        _playerTimer.Tick += (_, _) => UpdatePlayerPositionFromPlayback();

        UpdateSequenceUi();
        UpdateMarkerUi();
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
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        ForeColor = Color.FromArgb(124, 58, 237),
    };

    private static Label BuildLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = Color.FromArgb(200, 200, 210),
    };

    private static Label BuildSmallLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 34),
        ForeColor = Color.FromArgb(150, 150, 160),
    };

    private static TextBox BuildTextBox(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 26),
        BackColor = Color.FromArgb(14, 14, 18),
        ForeColor = Color.FromArgb(240, 240, 245),
        BorderStyle = BorderStyle.FixedSingle,
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
            Size = new Size(width, 26),
            BackColor = Color.FromArgb(14, 14, 18),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
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
            Size = new Size(width, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 38, 46),
            ForeColor = Color.White,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 82);
        button.Click += onClick;
        return button;
    }

    private static Button BuildActionButton(string text, int x, int y, int width, EventHandler onClick)
    {
        var button = BuildButton(text, x, y, width, onClick);
        button.BackColor = Color.FromArgb(124, 58, 237);
        button.FlatAppearance.BorderColor = Color.FromArgb(154, 92, 255);
        return button;
    }

    private static void StyleToolButton(Button button, bool active)
    {
        button.BackColor = active ? Color.FromArgb(124, 58, 237) : Color.FromArgb(38, 38, 46);
        button.FlatAppearance.BorderColor = active ? Color.FromArgb(154, 92, 255) : Color.FromArgb(70, 70, 82);
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

        files = files.Take(4).ToList();

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
                    Size = new Size(_mediaThumbStrip.Width - 12, 72),
                    Text = "Import clips or load your watch folder to populate the media bin with preview thumbnails.",
                    ForeColor = Color.FromArgb(150, 150, 160),
                    BackColor = Color.Transparent,
                });
                return;
            }

            foreach (var file in files)
            {
                var card = new Panel
                {
                    Size = new Size(106, 74),
                    Margin = new Padding(4),
                    BackColor = Color.FromArgb(18, 18, 22),
                    BorderStyle = BorderStyle.FixedSingle,
                };

                var thumbBox = new PictureBox
                {
                    Location = new Point(4, 4),
                    Size = new Size(96, 44),
                    BackColor = Color.FromArgb(8, 8, 10),
                    SizeMode = PictureBoxSizeMode.Zoom,
                };
                card.Controls.Add(thumbBox);

                card.Controls.Add(new Label
                {
                    Location = new Point(4, 52),
                    Size = new Size(96, 18),
                    Text = Path.GetFileNameWithoutExtension(file),
                    ForeColor = Color.White,
                    AutoEllipsis = true,
                });

                _mediaThumbStrip.Controls.Add(card);

                if (!_mediaThumbCache.TryGetValue(file, out var cachedThumb))
                {
                    try
                    {
                        using var thumb = await ExtractPreviewImageAsync(file, 0, CancellationToken.None);
                        cachedThumb = new Bitmap(thumb, new Size(96, 44));
                        _mediaThumbCache[file] = cachedThumb;
                    }
                    catch
                    {
                        cachedThumb = new Bitmap(96, 44);
                        using var g = Graphics.FromImage(cachedThumb);
                        g.Clear(Color.FromArgb(20, 20, 26));
                        TextRenderer.DrawText(g, "VIDEO", new Font("Segoe UI", 8f, FontStyle.Bold), new Rectangle(0, 12, 96, 20), Color.FromArgb(190, 190, 205), TextFormatFlags.HorizontalCenter);
                        _mediaThumbCache[file] = cachedThumb;
                    }
                }

                thumbBox.Image = cachedThumb;
            }
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
        _playPauseButton.Text = "Play";
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

        if (_isPlaying)
        {
            StopPlayback(resetToStart: false);
            return;
        }

        try
        {
            _mediaElement.Play();
            _isPlaying = true;
            _playPauseButton.Text = "Pause";
            _playerStatusLabel.Text = "Playing…";
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

        _isPlaying = false;
        _playerTimer.Stop();
        _playPauseButton.Text = "Play";
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
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
        {
            ReplacePicture(_outputPreview, null);
            return;
        }

        if (!_enableCropBox.Checked)
        {
            ReplacePicture(_outputPreview, _sourcePreview.ClonePreviewImage());
            return;
        }

        var cropRect = GetCropRectangle();
        if (cropRect.Width < 2 || cropRect.Height < 2)
        {
            ReplacePicture(_outputPreview, _sourcePreview.ClonePreviewImage());
            return;
        }

        var cropPreview = await ExtractPreviewImageAsync(_selectedFile, time, ct, cropRect);
        if (!ct.IsCancellationRequested)
            ReplacePicture(_outputPreview, cropPreview);
        else
            cropPreview.Dispose();
    }

    private void SetTrimBoundary(bool isStart)
    {
        var current = (decimal)GetCurrentPreviewTime();
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

        var max = (decimal)Math.Max(_videoDuration, 0.25);
        _updatingTrimRange = true;
        try
        {
            _startBox.Value = Math.Clamp((decimal)start, _startBox.Minimum, max);
            _endBox.Value = Math.Clamp((decimal)end, _endBox.Minimum, max);
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
        _splitPlayheadButton.Enabled = _sequenceList.SelectedIndex >= 0;
        _trimTimelineView.SetMarkers(markers);
    }

    private void UpdateTimelineZoom()
    {
        _timelineZoom = Math.Clamp(_timelineZoom, 1, 6);
        _trimTimelineView.SetZoom(_timelineZoom);
        _sequenceTimelineView.SetZoom(_timelineZoom);
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
        _sequenceTimelineView.SetRazorMode(razorActive);
        StyleToolButton(_selectToolButton, !razorActive);
        StyleToolButton(_razorToolButton, razorActive);
        _timelineModeLabel.Text = razorActive
            ? "Razor tool active — click any timeline clip to cut it instantly."
            : "Selection tool active — drag clips, trim edges, or press C for Razor.";
        _statusLabel.Text = _timelineModeLabel.Text;
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
        var args = $"-i {Quote(filePath)} -filter_complex \"aformat=channel_layouts=mono,showwavespic=s=1600x120:colors=0x38BDF8\" -frames:v 1 -y {Quote(tempFile)}";
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

        var fallback = new Bitmap(1600, 120);
        using (var g = Graphics.FromImage(fallback))
        {
            g.Clear(Color.FromArgb(18, 18, 24));
            using var pen = new Pen(Color.FromArgb(56, 189, 248), 2);
            var mid = fallback.Height / 2;
            for (var x = 0; x < fallback.Width; x += 8)
            {
                var height = 6 + (int)(Math.Abs(Math.Sin(x / 40d)) * 42);
                g.DrawLine(pen, x, mid - height, x, mid + height);
            }
        }
        return fallback;
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

    private void SplitSelectedSegmentAtPlayhead(int? requestedIndex = null, double? requestedTime = null)
    {
        var selectedIndex = requestedIndex ?? _sequenceList.SelectedIndex;
        var playhead = requestedTime ?? GetCurrentPreviewTime();
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

        var left = segment with { EndSec = playhead };
        var right = segment with { StartSec = playhead };
        _sequenceSegments[selectedIndex] = left;
        _sequenceSegments.Insert(selectedIndex + 1, right);
        UpdateSequenceUi(selectedIndex + 1);
        _statusLabel.Text = $"Cut timeline segment {selectedIndex + 1} at {FormatTime(playhead)}.";
    }

    private void ApplySequenceTrimFromTimeline(int index, double start, double end)
    {
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        var segment = _sequenceSegments[index];
        var safeStart = Math.Max(0, Math.Min(start, end - 0.05));
        var safeEnd = Math.Max(safeStart + 0.05, end);
        _sequenceSegments[index] = segment with { StartSec = safeStart, EndSec = safeEnd };
        UpdateSequenceUi(index);

        if (index == _sequenceList.SelectedIndex && string.Equals(_selectedFile, segment.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            _updatingTrimRange = true;
            try
            {
                _startBox.Value = Math.Clamp((decimal)safeStart, _startBox.Minimum, _startBox.Maximum);
                _endBox.Value = Math.Clamp((decimal)safeEnd, _endBox.Minimum, _endBox.Maximum);
            }
            finally
            {
                _updatingTrimRange = false;
            }
            UpdateTrimTimelineUi();
        }
    }

    private void AddCurrentCutToSequence()
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
            MessageBox.Show(this, "End time must be greater than start time before adding a cut.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var insertIndex = _sequenceList.SelectedIndex >= 0 && _sequenceList.SelectedIndex < _sequenceSegments.Count
            ? _sequenceList.SelectedIndex + 1
            : _sequenceSegments.Count;
        var targetTrack = insertIndex > 0 && insertIndex - 1 < _sequenceSegments.Count
            ? _sequenceSegments[insertIndex - 1].SafeTrack
            : ((_sequenceSegments.Count % 2) + 1);

        _sequenceSegments.Insert(insertIndex, new TimelineSegment(input, start, end, targetTrack));
        UpdateSequenceUi(selectedIndex: insertIndex);
        _statusLabel.Text = $"Inserted cut {insertIndex + 1} on V{targetTrack} from {Path.GetFileName(input)}.";
    }

    private void RemoveSelectedSequenceSegment()
    {
        if (_sequenceList.SelectedIndex < 0 || _sequenceList.SelectedIndex >= _sequenceSegments.Count)
            return;

        var index = _sequenceList.SelectedIndex;
        _sequenceSegments.RemoveAt(index);
        UpdateSequenceUi(selectedIndex: Math.Min(index, _sequenceSegments.Count - 1));
    }

    private void MoveSelectedSequenceSegment(int direction)
    {
        var index = _sequenceList.SelectedIndex;
        if (index < 0 || index >= _sequenceSegments.Count)
            return;

        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _sequenceSegments.Count)
            return;

        var segment = _sequenceSegments[index];
        _sequenceSegments.RemoveAt(index);
        _sequenceSegments.Insert(newIndex, segment);
        UpdateSequenceUi(selectedIndex: newIndex);
    }

    private void ClearSequence()
    {
        _sequenceSegments.Clear();
        UpdateSequenceUi();
    }

    private void MoveSequenceSegmentTo(int fromIndex, int targetIndex, int targetTrack)
    {
        if (fromIndex < 0 || fromIndex >= _sequenceSegments.Count)
            return;

        var clampedTrack = Math.Clamp(targetTrack, 1, 2);
        var segment = _sequenceSegments[fromIndex] with { Track = clampedTrack };
        _sequenceSegments.RemoveAt(fromIndex);

        var insertIndex = Math.Clamp(targetIndex, 0, _sequenceSegments.Count);
        if (insertIndex > fromIndex)
            insertIndex--;

        _sequenceSegments.Insert(insertIndex, segment);
        UpdateSequenceUi(insertIndex);
        _statusLabel.Text = $"Moved timeline cut to V{clampedTrack}.";
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
            var totalDuration = _sequenceSegments.Sum(segment => segment.Duration);
            var v1Count = _sequenceSegments.Count(segment => segment.SafeTrack == 1);
            var v2Count = _sequenceSegments.Count(segment => segment.SafeTrack == 2);
            _sequenceSummaryLabel.Text = $"{_sequenceSegments.Count} cut(s) • V1 {v1Count} / V2 {v2Count} • {FormatTime(totalDuration)} total";
            _sequenceTimelineView.SetSegments(_sequenceSegments, safeIndex);
            if (safeIndex >= 0 && safeIndex < _sequenceList.Items.Count)
                _sequenceList.SelectedIndex = safeIndex;
        }
        else
        {
            _sequenceSummaryLabel.Text = "Timeline empty — insert a range to start cutting.";
            _sequenceTimelineView.SetSegments(Array.Empty<TimelineSegment>(), -1);
        }

        _splitPlayheadButton.Enabled = _sequenceSegments.Count > 0 && _sequenceList.SelectedIndex >= 0;
        _exportSequenceButton.Enabled = _sequenceSegments.Count > 0;
    }

    private async Task ExportSequenceAsync()
    {
        if (_sequenceSegments.Count == 0)
        {
            MessageBox.Show(this, "Add at least one cut to the timeline before exporting.", "VELO Video Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var outputPath = BuildOutputPath(_sequenceSegments[0].SourceFile, "timeline", forceMp4: true);
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        var tempDir = Path.Combine(Path.GetTempPath(), $"velo-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFiles = new List<string>();

        SetEditorBusy(true, "Exporting timeline sequence…");

        try
        {
            for (var index = 0; index < _sequenceSegments.Count; index++)
            {
                var segment = _sequenceSegments[index];
                var tempFile = Path.Combine(tempDir, $"segment-{index:D2}.mp4");
                tempFiles.Add(tempFile);

                var extractArgs = $"-ss {segment.StartSec.ToString(CultureInfo.InvariantCulture)} -i {Quote(segment.SourceFile)} -t {segment.Duration.ToString(CultureInfo.InvariantCulture)} -c copy -avoid_negative_ts make_zero -y {Quote(tempFile)}";
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

        if (e.Control && (e.KeyCode == Keys.B || e.KeyCode == Keys.K))
        {
            SplitSelectedSegmentAtPlayhead();
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

        if (e.KeyCode == Keys.V && !e.Control && !e.Alt)
        {
            SetTimelineEditMode(TimelineEditMode.Select);
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
        _exportSequenceButton.Enabled = !busy && _sequenceSegments.Count > 0;
        _splitPlayheadButton.Enabled = !busy && _sequenceList.SelectedIndex >= 0;
        _refreshPreviewButton.Enabled = !busy;
        _playPauseButton.Enabled = canControlPlayback;
        _jumpBackButton.Enabled = canControlPlayback;
        _jumpForwardButton.Enabled = canControlPlayback;
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
        private string _clipLabel = "No clip selected";
        private readonly List<double> _markers = [];
        private readonly List<Image> _thumbnails = [];
        private Image? _waveformImage;
        private DragMode _dragMode;
        private double _dragOffsetSeconds;

        public event Action<double>? SeekRequested;
        public event Action<double, double>? RangeChanged;
        public event Action<double>? ZoomDeltaRequested;

        public TrimTimelineView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(10, 10, 12);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_duration <= 0)
                return;

            var rail = GetRailRect();
            var startX = SecondsToX(_rangeStart, rail);
            var endX = SecondsToX(_rangeEnd, rail);
            var startHandle = new Rectangle(startX - 5, rail.Top - 4, 10, rail.Height + 8);
            var endHandle = new Rectangle(endX - 5, rail.Top - 4, 10, rail.Height + 8);
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
            UpdateCursor(e.Location);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomDeltaRequested?.Invoke(e.Delta > 0 ? 0.25 : -0.25);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var borderPen = new Pen(Color.FromArgb(52, 52, 66));
            using var textBrush = new SolidBrush(Color.FromArgb(145, 145, 160));
            using var railBrush = new SolidBrush(Color.FromArgb(26, 26, 34));
            using var clipBrush = new SolidBrush(Color.FromArgb(188, 37, 99, 235));
            using var rangeBrush = new SolidBrush(Color.FromArgb(228, 124, 58, 237));
            using var playheadPen = new Pen(Color.FromArgb(248, 113, 113), 2);
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

            var (visibleStart, visibleDuration) = GetVisibleRange();

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

            var selectedRect = Rectangle.FromLTRB(SecondsToX(_rangeStart, rail), rail.Top + 2, SecondsToX(_rangeEnd, rail), rail.Bottom - 2);
            e.Graphics.FillRectangle(clipBrush, rail.Left + 1, rail.Top + 2, rail.Width - 2, rail.Height - 4);
            e.Graphics.FillRectangle(rangeBrush, selectedRect);
            using var selectedBorder = new Pen(Color.FromArgb(221, 214, 254), 2);
            e.Graphics.DrawRectangle(selectedBorder, selectedRect);

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

            var startHandle = new Rectangle(selectedRect.Left - 4, rail.Top - 3, 8, rail.Height + 6);
            var endHandle = new Rectangle(selectedRect.Right - 4, rail.Top - 3, 8, rail.Height + 6);
            e.Graphics.FillRectangle(handleBrush, startHandle);
            e.Graphics.FillRectangle(handleBrush, endHandle);
            e.Graphics.DrawRectangle(borderPen, startHandle);
            e.Graphics.DrawRectangle(borderPen, endHandle);

            var playheadX = SecondsToX(_playhead, rail);
            e.Graphics.DrawLine(playheadPen, playheadX, rail.Top - 6, playheadX, rail.Bottom + 6);

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
            var startHandle = new Rectangle(startX - 5, rail.Top - 4, 10, rail.Height + 8);
            var endHandle = new Rectangle(endX - 5, rail.Top - 4, 10, rail.Height + 8);
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

            var (visibleStart, visibleDuration) = GetVisibleRange();
            var snapCandidates = new List<double> { 0, _duration, _playhead, _rangeStart, _rangeEnd };
            snapCandidates.AddRange(_markers);
            var threshold = Math.Max(0.05, visibleDuration * 0.02);
            var nearest = snapCandidates.OrderBy(value => Math.Abs(value - seconds)).FirstOrDefault();
            return Math.Abs(nearest - seconds) <= threshold ? nearest : seconds;
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
    }

    private sealed class SequenceTimelineView : Control
    {
        private enum SegmentDragMode
        {
            None,
            Move,
            ResizeLeft,
            ResizeRight,
        }

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

        private Func<string, Image?>? _waveformProvider;

        public event Action<int>? SegmentClicked;
        public event Action<int, double, double>? SegmentTrimChanged;
        public event Action<int, int, int>? SegmentMoved;
        public event Action<int, double>? SegmentSplitRequested;
        public event Action<double>? ZoomDeltaRequested;

        public SequenceTimelineView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(10, 10, 12);
            ForeColor = Color.FromArgb(240, 240, 245);
            Cursor = Cursors.Hand;
            TabStop = true;
            MouseEnter += (_, _) => Focus();
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

        public void SetZoom(double zoom)
        {
            _zoom = Math.Clamp(zoom, 1, 6);
            Invalidate();
        }

        public void SetRazorMode(bool enabled)
        {
            _razorMode = enabled;
            Cursor = enabled ? Cursors.Cross : Cursors.Hand;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(e.Location));
            if (hit.Rect == Rectangle.Empty)
                return;

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
                _previewInsertIndex = hit.Index;
                _previewTrack = _segments[hit.Index].SafeTrack;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragMode == SegmentDragMode.None || _dragOriginSegment == null || _dragIndex < 0 || _dragIndex >= _segments.Count)
                return;

            var totalDuration = Math.Max(0.1, _segments.Sum(segment => segment.Duration));
            var timelineLeft = 72;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var absoluteSeconds = visibleStart + (((e.X - timelineLeft) / (double)Math.Max(1, timelineWidth)) * visibleDuration);
            var priorDuration = _segments.Take(_dragIndex).Sum(segment => segment.Duration);
            var localPosition = absoluteSeconds - priorDuration;

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

            _previewInsertIndex = GetInsertIndex(e.X, timelineLeft, timelineWidth, totalDuration);
            _previewTrack = GetTrackForY(e.Y);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragMode == SegmentDragMode.Move && _dragIndex >= 0)
                SegmentMoved?.Invoke(_dragIndex, _previewInsertIndex < 0 ? _dragIndex : _previewInsertIndex, _previewTrack);

            _dragMode = SegmentDragMode.None;
            _dragIndex = -1;
            _dragOriginSegment = null;
            _previewInsertIndex = -1;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomDeltaRequested?.Invoke(e.Delta > 0 ? 0.25 : -0.25);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _hitTargets.Clear();

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var borderPen = new Pen(Color.FromArgb(52, 52, 66));
            using var railBrush = new SolidBrush(Color.FromArgb(20, 20, 28));
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

            var totalDuration = Math.Max(0.1, _segments.Sum(segment => segment.Duration));
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

            TextRenderer.DrawText(e.Graphics, "V1", Font, new Point(18, v1.Top + 8), Color.FromArgb(180, 180, 195));
            TextRenderer.DrawText(e.Graphics, "V2", Font, new Point(18, v2.Top + 8), Color.FromArgb(180, 180, 195));
            TextRenderer.DrawText(e.Graphics, "A1", Font, new Point(18, a1.Top + 1), Color.FromArgb(180, 180, 195));
            TextRenderer.DrawText(e.Graphics, "A2", Font, new Point(18, a2.Top + 1), Color.FromArgb(180, 180, 195));

            e.Graphics.FillRectangle(railBrush, v1);
            e.Graphics.FillRectangle(railBrush, v2);
            e.Graphics.FillRectangle(railBrush, a1);
            e.Graphics.FillRectangle(railBrush, a2);
            e.Graphics.DrawRectangle(borderPen, v1);
            e.Graphics.DrawRectangle(borderPen, v2);
            e.Graphics.DrawRectangle(borderPen, a1);
            e.Graphics.DrawRectangle(borderPen, a2);

            for (var mark = 0; mark <= 8; mark++)
            {
                var x = timelineLeft + (int)Math.Round(mark * (timelineWidth / 8d));
                using var gridPen = new Pen(Color.FromArgb(32, 32, 40));
                e.Graphics.DrawLine(gridPen, x, rulerTop + 10, x, a2.Bottom);
                var stamp = FormatTime(visibleStart + (visibleDuration * mark / 8d));
                TextRenderer.DrawText(e.Graphics, stamp, new Font("Segoe UI", 7f), new Point(Math.Max(0, x - 16), rulerTop), Color.FromArgb(120, 120, 135));
            }

            if (_razorMode)
            {
                var badgeRect = new Rectangle(Math.Max(timelineLeft, Width - 196), 8, 182, 18);
                using var badgeBrush = new SolidBrush(Color.FromArgb(170, 124, 58, 237));
                e.Graphics.FillRectangle(badgeBrush, badgeRect);
                TextRenderer.DrawText(e.Graphics, "RAZOR MODE • click to cut", new Font("Segoe UI", 7f, FontStyle.Bold), badgeRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            var cursorSeconds = 0d;
            for (var index = 0; index < _segments.Count; index++)
            {
                var segment = _segments[index];
                var segmentStartInSequence = cursorSeconds;
                var segmentEndInSequence = cursorSeconds + segment.Duration;
                if (segmentEndInSequence < visibleStart || segmentStartInSequence > visibleStart + visibleDuration)
                {
                    cursorSeconds += segment.Duration;
                    continue;
                }

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
                using var blockBrush = new SolidBrush(fill);
                using var audioBrush = new SolidBrush(Color.FromArgb(Math.Max(0, fill.R - 24), Math.Max(0, fill.G - 24), Math.Max(0, fill.B - 24)));
                using var activePen = new Pen(index == _selectedIndex ? Color.FromArgb(221, 214, 254) : Color.FromArgb(120, 120, 150), index == _selectedIndex ? 2 : 1);
                using var handleBrush = new SolidBrush(Color.FromArgb(245, 245, 250));

                e.Graphics.FillRectangle(blockBrush, rect);
                e.Graphics.FillRectangle(audioBrush, audioBlock);
                if (_waveformProvider?.Invoke(segment.SourceFile) is Image waveform)
                    e.Graphics.DrawImage(waveform, audioBlock);
                e.Graphics.DrawRectangle(activePen, rect);
                e.Graphics.DrawRectangle(activePen, audioBlock);

                if (index == _selectedIndex)
                {
                    e.Graphics.FillRectangle(handleBrush, new Rectangle(rect.Left - 3, rect.Top + 4, 6, rect.Height - 8));
                    e.Graphics.FillRectangle(handleBrush, new Rectangle(rect.Right - 3, rect.Top + 4, 6, rect.Height - 8));
                }

                var label = $"{index + 1}. {Path.GetFileNameWithoutExtension(segment.SourceFile)}";
                TextRenderer.DrawText(e.Graphics, label, Font, rect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                _hitTargets.Add((rect, index));
                cursorSeconds += segment.Duration;
            }

            if (_dragMode == SegmentDragMode.Move && _previewInsertIndex >= 0)
            {
                var insertX = timelineLeft + (int)Math.Round(GetInsertRatio(_previewInsertIndex, totalDuration, visibleStart, visibleDuration) * timelineWidth);
                var targetLane = _previewTrack == 2 ? v2 : v1;
                using var insertPen = new Pen(Color.FromArgb(251, 191, 36), 2);
                e.Graphics.DrawLine(insertPen, insertX, targetLane.Top - 4, insertX, a2.Bottom + 4);
            }

            var selectedOffset = Math.Clamp(_playheadSeconds, 0, totalDuration);
            if (_selectedIndex >= 0 && _selectedIndex < _segments.Count)
            {
                var priorDuration = _segments.Take(_selectedIndex).Sum(segment => segment.Duration);
                var selectedSegment = _segments[_selectedIndex];
                var localOffset = Math.Clamp(_playheadSeconds - selectedSegment.StartSec, 0, selectedSegment.Duration);
                selectedOffset = Math.Clamp(priorDuration + localOffset, 0, totalDuration);
            }

            var playheadX = timelineLeft + (int)Math.Round(((selectedOffset - visibleStart) / visibleDuration) * timelineWidth);
            using var playheadPen = new Pen(Color.FromArgb(248, 113, 113), 2);
            e.Graphics.DrawLine(playheadPen, playheadX, rulerTop + 10, playheadX, a2.Bottom + 8);
        }

        private (double Start, double Duration) GetVisibleRange(double totalDuration)
        {
            var visibleDuration = Math.Min(totalDuration, Math.Max(3, totalDuration / Math.Max(1, _zoom)));
            var start = Math.Clamp(_playheadSeconds - (visibleDuration / 2), 0, Math.Max(0, totalDuration - visibleDuration));
            return (start, Math.Max(0.001, visibleDuration));
        }

        private int GetInsertIndex(int x, int timelineLeft, int timelineWidth, double totalDuration)
        {
            var (visibleStart, visibleDuration) = GetVisibleRange(totalDuration);
            var cursorSeconds = 0d;
            for (var index = 0; index < _segments.Count; index++)
            {
                var segment = _segments[index];
                var midpointRatio = ((cursorSeconds + (segment.Duration / 2d)) - visibleStart) / visibleDuration;
                var midpointX = timelineLeft + (int)Math.Round(midpointRatio * timelineWidth);
                if (x < midpointX)
                    return index;
                cursorSeconds += segment.Duration;
            }
            return _segments.Count;
        }

        private double GetInsertRatio(int insertIndex, double totalDuration, double visibleStart, double visibleDuration)
        {
            if (_segments.Count == 0 || insertIndex <= 0)
                return Math.Clamp((0 - visibleStart) / visibleDuration, 0, 1);
            if (insertIndex >= _segments.Count)
            {
                var total = _segments.Sum(segment => segment.Duration);
                return Math.Clamp((total - visibleStart) / visibleDuration, 0, 1);
            }

            var durationBefore = _segments.Take(insertIndex).Sum(segment => segment.Duration);
            return Math.Clamp((durationBefore - visibleStart) / visibleDuration, 0, 1);
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
