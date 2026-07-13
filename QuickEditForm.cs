namespace VeloUploader;

using System.Drawing.Imaging;
using System.Windows.Forms.Integration;
using VeloUploader.Editor;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;

/// <summary>
/// VELO Studio: the clip editor window. UI shell only — timeline state lives
/// in <see cref="SequenceModel"/>, FFmpeg work in <see cref="FfmpegService"/>
/// and <see cref="SequenceExporter"/>, and the heavy custom-drawn surfaces in
/// the Editor controls.
/// </summary>
public sealed class QuickEditForm : Form
{
    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    // ----- model -----
    private readonly SequenceModel _sequence = new();
    private readonly List<string> _mediaFiles = [];
    private readonly Dictionary<string, Image> _mediaThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Image>> _stripThumbCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image> _waveformCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<double>> _clipMarkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _watchFolder;

    private string? _selectedFile;
    private double _videoDuration;
    private Size _videoSize = Size.Empty;
    private double _sequencePlayheadSec;
    private int _selectedSegmentIndex = -1;
    private TimelineTool _tool = TimelineTool.Select;
    private int _targetVideoTrack = 1;
    private int _targetAudioTrack = 1;
    private double _timelineZoom = 1;
    private bool _snappingEnabled = true;
    private bool _busy;

    // ----- playback -----
    private bool _isPlaying;
    private int _transportDirection;
    private int _transportSpeedLevel;
    private bool _timelineUpdateFromPlayer;
    private CancellationTokenSource? _previewCts;
    private double _requestedPreviewTime;

    // ----- guards against feedback loops between fields and views -----
    private bool _updatingCropFields;
    private bool _updatingTrimRange;
    private bool _updatingTransformFields;
    private bool _suppressBinEvents;

    // ----- controls -----
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _toolStrip = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _sequenceStatsLabel = new();

    private readonly ListBox _binList;
    private readonly TextBox _binSearchBox;

    private readonly EditorPreviewBox _sourcePreview;
    private readonly ElementHost _playerHost;
    private readonly WpfControls.MediaElement _mediaElement;
    private readonly PictureBox _sequencePreview;
    private readonly Label _sourceTimeLabel;
    private readonly Label _programStatusLabel;
    private readonly Label _clipInfoLabel;

    private readonly TrimTimelineView _trimView;
    private readonly SequenceTimelineView _sequenceView;
    private readonly TrackBar _scrubBar;
    private readonly Button _playPauseButton;

    private readonly TextBox _outputNameBox;
    private readonly TextBox _outputFolderBox;
    private readonly NumericUpDown _startBox;
    private readonly NumericUpDown _endBox;
    private readonly NumericUpDown _cropXBox;
    private readonly NumericUpDown _cropYBox;
    private readonly NumericUpDown _cropWBox;
    private readonly NumericUpDown _cropHBox;
    private readonly CheckBox _enableCropBox;
    private readonly Label _cropInfoLabel;
    private readonly NumericUpDown _positionXBox;
    private readonly NumericUpDown _positionYBox;
    private readonly NumericUpDown _scaleBox;
    private readonly NumericUpDown _opacityBox;
    private readonly Button _positionKeyframeButton;
    private readonly Button _scaleKeyframeButton;
    private readonly Button _opacityKeyframeButton;
    private readonly ListBox _markerList;

    private readonly Dictionary<TimelineTool, ToolStripButton> _toolButtons = [];
    private ToolStripButton _snapButton = null!;
    private ToolStripMenuItem _undoMenuItem = null!;
    private ToolStripMenuItem _redoMenuItem = null!;
    private readonly List<ToolStripItem> _busySensitiveItems = [];

    private readonly System.Windows.Forms.Timer _previewDebounceTimer;
    private readonly System.Windows.Forms.Timer _playerTimer;

    public QuickEditForm(string defaultOutputFolder)
    {
        _watchFolder = Directory.Exists(defaultOutputFolder)
            ? defaultOutputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Text = "VELO Studio";
        ClientSize = new Size(1440, 880);
        MinimumSize = new Size(1180, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = EditorTheme.WindowBack;
        ForeColor = EditorTheme.TextPrimary;
        Font = EditorTheme.BaseFont;
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

        // --- media element (WPF) ---
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
            SetStatus($"Playback error: {e.ErrorException?.Message ?? "unknown"}");
        };

        _playerHost = new ElementHost
        {
            Dock = DockStyle.Fill,
            Child = _mediaElement,
            BackColor = Color.FromArgb(8, 8, 10),
        };

        _sequencePreview = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(8, 8, 10),
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = false,
        };

        _sourcePreview = new EditorPreviewBox { Dock = DockStyle.Fill };
        _sourcePreview.CropChanged += OnCropSelectionChanged;

        _sourceTimeLabel = new Label
        {
            Text = "00:00.000",
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = EditorTheme.MonoFont,
            ForeColor = EditorTheme.TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };

        _programStatusLabel = new Label
        {
            Text = "Load a clip to start playback.",
            Dock = DockStyle.Bottom,
            Height = 20,
            Font = EditorTheme.SmallFont,
            ForeColor = EditorTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
        };

        _clipInfoLabel = new Label
        {
            Text = "Import clips into the media bin to start editing.",
            Dock = DockStyle.Top,
            Height = 20,
            Font = EditorTheme.SmallFont,
            ForeColor = EditorTheme.TextSecondary,
            Padding = new Padding(4, 2, 0, 0),
        };

        // --- timeline views ---
        _trimView = new TrimTimelineView { Dock = DockStyle.Fill };
        _trimView.SeekRequested += seconds => SeekSource(seconds, refreshPreview: true);
        _trimView.RangeChanged += ApplyTrimRangeFromTimeline;
        _trimView.ZoomDeltaRequested += AdjustTimelineZoom;

        _sequenceView = new SequenceTimelineView { Dock = DockStyle.Fill };
        _sequenceView.SegmentClicked += index => _ = LoadSequenceSegmentAsync(index);
        _sequenceView.SegmentTrimChanged += (index, start, end) => SelectSegment(_sequence.TrimEdge(index, start, end, _tool));
        _sequenceView.SegmentMoved += (index, dropTime, track) => SelectSegment(_sequence.Move(index, dropTime, track, ripple: _tool == TimelineTool.Ripple));
        _sequenceView.SegmentSplitRequested += (index, splitTime) => SelectSegment(_sequence.Split(index, EditorTime.SnapToFrame(splitTime)));
        _sequenceView.GapDeleteRequested += (track, time) =>
        {
            SetStatus(_sequence.DeleteGapAt(track, time)
                ? $"Closed the gap on V{track}."
                : $"No gap found on V{track} there.");
        };
        _sequenceView.SegmentSlipRequested += (index, delta) =>
        {
            var limit = index >= 0 && index < _sequence.Segments.Count &&
                        string.Equals(_sequence.Segments[index].SourceFile, _selectedFile, StringComparison.OrdinalIgnoreCase) && _videoDuration > 0
                ? _videoDuration
                : double.MaxValue;
            SelectSegment(_sequence.Slip(index, delta, limit));
        };
        _sequenceView.TrackTargetChanged += (videoTrack, audioTrack) =>
        {
            _targetVideoTrack = videoTrack;
            _targetAudioTrack = audioTrack;
            SetStatus($"Source patched to V{videoTrack} / A{audioTrack}.");
        };
        _sequenceView.SeekRequested += seconds => _ = ScrubSequenceToTimeAsync(seconds);
        _sequenceView.ZoomDeltaRequested += AdjustTimelineZoom;
        _sequenceView.SetWaveformProvider(file => _waveformCache.TryGetValue(file, out var waveform) ? waveform : null);
        _sequenceView.SetThumbnailProvider(file =>
        {
            if (_stripThumbCache.TryGetValue(file, out var thumbs) && thumbs.Count > 0)
                return thumbs;
            return _mediaThumbCache.TryGetValue(file, out var thumb) ? [thumb] : [];
        });

        _scrubBar = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            BackColor = EditorTheme.PanelBack,
        };
        _scrubBar.Scroll += (_, _) => QueuePreviewRefreshFromSlider();

        _playPauseButton = EditorTheme.PrimaryButton("▶ Play", (_, _) => TogglePlayback());
        _playPauseButton.Width = 86;

        // --- media bin ---
        _binSearchBox = EditorTheme.TextField();
        _binSearchBox.PlaceholderText = "Filter clips…";
        _binSearchBox.Dock = DockStyle.Fill;
        _binSearchBox.TextChanged += (_, _) => RefreshBinList();

        _binList = new ListBox
        {
            Dock = DockStyle.Fill,
            HorizontalScrollbar = false,
            SelectionMode = SelectionMode.MultiExtended,
            AllowDrop = true,
            BackColor = EditorTheme.InsetBack,
            ForeColor = EditorTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 96,
            IntegralHeight = false,
        };
        _binList.DrawItem += DrawMediaBinItem;
        _binList.SelectedIndexChanged += async (_, _) => await HandleSelectionChangedAsync();
        _binList.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _binList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                ImportFiles(files);
        };

        var binMenu = new ContextMenuStrip
        {
            Renderer = new EditorStripRenderer(),
            BackColor = EditorTheme.PanelBack,
            ForeColor = EditorTheme.TextPrimary,
        };
        binMenu.Items.Add(new ToolStripMenuItem("Move up", null, (_, _) => MoveBinSelection(-1)) { ForeColor = EditorTheme.TextPrimary });
        binMenu.Items.Add(new ToolStripMenuItem("Move down", null, (_, _) => MoveBinSelection(1)) { ForeColor = EditorTheme.TextPrimary });
        binMenu.Items.Add(new ToolStripSeparator());
        binMenu.Items.Add(new ToolStripMenuItem("Remove from bin", null, (_, _) => RemoveSelectedFromBin()) { ForeColor = EditorTheme.TextPrimary });
        _binList.ContextMenuStrip = binMenu;

        // --- inspector fields ---
        _outputNameBox = EditorTheme.TextField();
        _outputNameBox.PlaceholderText = "Leave blank to auto-name";
        _outputFolderBox = EditorTheme.TextField(_watchFolder);

        _startBox = EditorTheme.NumericField(0, 0, 86400);
        _endBox = EditorTheme.NumericField(30, 0, 86400);
        _startBox.ValueChanged += (_, _) => SyncTrimRangeFromFields();
        _endBox.ValueChanged += (_, _) => SyncTrimRangeFromFields();

        _cropXBox = EditorTheme.NumericField(0, 0, 10000, 0);
        _cropYBox = EditorTheme.NumericField(0, 0, 10000, 0);
        _cropWBox = EditorTheme.NumericField(1920, 1, 10000, 0);
        _cropHBox = EditorTheme.NumericField(1080, 1, 10000, 0);
        _cropXBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropYBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropWBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();
        _cropHBox.ValueChanged += (_, _) => SyncCropPreviewFromFields();

        _enableCropBox = new CheckBox
        {
            Text = "Enable crop overlay",
            Checked = false,
            AutoSize = true,
            ForeColor = EditorTheme.TextPrimary,
            BackColor = Color.Transparent,
        };
        _enableCropBox.CheckedChanged += (_, _) =>
        {
            _sourcePreview.ShowCropOverlay = _enableCropBox.Checked;
            _sourcePreview.Invalidate();
            _ = RefreshPreviewAsync(GetSourcePlayhead());
        };
        _cropInfoLabel = EditorTheme.HintLabel("Crop: full frame", 250);

        _positionXBox = EditorTheme.NumericField(0, -4000, 4000);
        _positionYBox = EditorTheme.NumericField(0, -4000, 4000);
        _scaleBox = EditorTheme.NumericField(100, 10, 400);
        _opacityBox = EditorTheme.NumericField(100, 0, 100);
        _positionXBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _positionYBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _scaleBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _opacityBox.ValueChanged += (_, _) => SyncTransformFromFields();
        _positionKeyframeButton = EditorTheme.ToolButton("⏱", (_, _) => ToggleKeyframing("position"));
        _scaleKeyframeButton = EditorTheme.ToolButton("⏱", (_, _) => ToggleKeyframing("scale"));
        _opacityKeyframeButton = EditorTheme.ToolButton("⏱", (_, _) => ToggleKeyframing("opacity"));

        _markerList = new ListBox
        {
            MinimumSize = new Size(0, 76),
            IntegralHeight = false,
            BackColor = EditorTheme.FieldBack,
            ForeColor = EditorTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _markerList.DoubleClick += (_, _) => JumpToSelectedMarker();

        BuildLayout();
        BuildMenu();
        BuildToolStrip();
        BuildStatusStrip();

        _sequence.Changed += OnSequenceChanged;
        KeyDown += OnEditorKeyDown;

        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 260 };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer.Stop();
            await RefreshPreviewAsync(_requestedPreviewTime);
        };

        _playerTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _playerTimer.Tick += (_, _) => StepTransportPlayback();

        SetTool(TimelineTool.Select);
        UpdateSequenceDependentUi();
        UpdateMarkerUi();
        UpdateTransformInspectorUi();
        SetStatus("Ready — import clips (Ctrl+I) or drag files into the media bin.");
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
            _previewCts?.Dispose();
            StopPlayback(resetToStart: false);
            ReplaceImage(_sequencePreview, null);
            _sourcePreview.DisposePreviewImage();
            foreach (var image in _mediaThumbCache.Values)
                image.Dispose();
            _mediaThumbCache.Clear();
            foreach (var images in _stripThumbCache.Values)
                foreach (var image in images)
                    image.Dispose();
            _stripThumbCache.Clear();
            foreach (var image in _waveformCache.Values)
                image.Dispose();
            _waveformCache.Clear();
        }

        base.Dispose(disposing);
    }

    // ================================================================
    // Layout
    // ================================================================

    private void BuildLayout()
    {
        var outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = EditorTheme.Border,
            SplitterWidth = 4,
        };

        var topSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = EditorTheme.Border,
            SplitterWidth = 4,
            FixedPanel = FixedPanel.Panel1,
        };

        var monitorSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = EditorTheme.Border,
            SplitterWidth = 4,
            FixedPanel = FixedPanel.Panel2,
        };

        // ----- media bin (left) -----
        var binPanel = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.PanelBack, Padding = new Padding(8) };
        var binLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent,
        };
        binLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        binLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        binLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        binLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var binTitle = EditorTheme.SectionLabel("Media bin");
        binLayout.Controls.Add(binTitle, 0, 0);
        binLayout.Controls.Add(_binSearchBox, 0, 1);

        var binListHost = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.InsetBack, Padding = new Padding(1), Margin = new Padding(0, 6, 0, 6) };
        binListHost.Controls.Add(_binList);
        binLayout.Controls.Add(binListHost, 0, 2);

        var binButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent,
        };
        binButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        binButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        binButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        var addButton = EditorTheme.ToolButton("Add files", (_, _) => ImportFilesDialog());
        addButton.Dock = DockStyle.Fill;
        var watchButton = EditorTheme.ToolButton("Watch folder", (_, _) => LoadWatchFolder());
        watchButton.Dock = DockStyle.Fill;
        var removeButton = EditorTheme.ToolButton("Remove", (_, _) => RemoveSelectedFromBin());
        removeButton.Dock = DockStyle.Fill;
        binButtons.Controls.Add(addButton, 0, 0);
        binButtons.Controls.Add(watchButton, 1, 0);
        binButtons.Controls.Add(removeButton, 2, 0);
        binLayout.Controls.Add(binButtons, 0, 3);
        binPanel.Controls.Add(binLayout);

        // ----- monitors (center) -----
        var monitorsPanel = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.PanelBack, Padding = new Padding(8) };
        var monitorsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        monitorsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        monitorsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        monitorsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        monitorsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitorsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var sourceTitle = EditorTheme.SectionLabel("Source monitor");
        var programTitle = EditorTheme.SectionLabel("Program monitor");
        monitorsLayout.Controls.Add(sourceTitle, 0, 0);
        monitorsLayout.Controls.Add(programTitle, 1, 0);

        var sourceHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8, 8, 10), Margin = new Padding(0, 2, 4, 2) };
        sourceHost.Controls.Add(_sourcePreview);
        monitorsLayout.Controls.Add(sourceHost, 0, 1);

        var programHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8, 8, 10), Margin = new Padding(4, 2, 0, 2) };
        programHost.Controls.Add(_sequencePreview);
        programHost.Controls.Add(_playerHost);
        _sequencePreview.BringToFront();
        monitorsLayout.Controls.Add(programHost, 1, 1);

        monitorsLayout.Controls.Add(_sourceTimeLabel, 0, 2);
        monitorsLayout.Controls.Add(_programStatusLabel, 1, 2);
        monitorsPanel.Controls.Add(monitorsLayout);
        monitorsPanel.Controls.Add(_clipInfoLabel);
        _clipInfoLabel.SendToBack();

        // ----- inspector (right) -----
        var inspectorPanel = BuildInspector();

        // ----- timeline dock (bottom) -----
        var timelinePanel = new Panel { Dock = DockStyle.Fill, BackColor = EditorTheme.PanelBack, Padding = new Padding(8, 4, 8, 4) };
        var timelineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        timelineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var trimHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 2) };
        trimHost.Controls.Add(_trimView);
        timelineLayout.Controls.Add(trimHost, 0, 0);

        var transportRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        transportRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transportRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transportRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transportRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var backButton = EditorTheme.ToolButton("⏪ 5s", (_, _) => SkipSeconds(-5));
        backButton.Width = 56;
        var fwdButton = EditorTheme.ToolButton("5s ⏩", (_, _) => SkipSeconds(5));
        fwdButton.Width = 56;
        transportRow.Controls.Add(_playPauseButton, 0, 0);
        transportRow.Controls.Add(backButton, 1, 0);
        transportRow.Controls.Add(fwdButton, 2, 0);
        transportRow.Controls.Add(_scrubBar, 3, 0);
        timelineLayout.Controls.Add(transportRow, 0, 1);

        var sequenceHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
        sequenceHost.Controls.Add(_sequenceView);
        timelineLayout.Controls.Add(sequenceHost, 0, 2);
        timelinePanel.Controls.Add(timelineLayout);

        // ----- assemble -----
        monitorSplit.Panel1.Controls.Add(monitorsPanel);
        monitorSplit.Panel2.Controls.Add(inspectorPanel);
        topSplit.Panel1.Controls.Add(binPanel);
        topSplit.Panel2.Controls.Add(monitorSplit);
        outerSplit.Panel1.Controls.Add(topSplit);
        outerSplit.Panel2.Controls.Add(timelinePanel);

        Controls.Add(outerSplit);
        Controls.Add(_toolStrip);
        Controls.Add(_menu);
        Controls.Add(_statusStrip);
        MainMenuStrip = _menu;

        Load += (_, _) =>
        {
            try
            {
                outerSplit.Panel1MinSize = 260;
                outerSplit.Panel2MinSize = 220;
                topSplit.Panel1MinSize = 180;
                monitorSplit.Panel2MinSize = 280;
                outerSplit.SplitterDistance = Math.Max(outerSplit.Panel1MinSize, (int)(ClientSize.Height * 0.55));
                topSplit.SplitterDistance = 250;
                monitorSplit.SplitterDistance = Math.Max(300, monitorSplit.Width - 312);
            }
            catch (InvalidOperationException)
            {
                // A very small window can make the preferred splits unsatisfiable; defaults are fine.
            }
        };

        EditorTheme.ApplyDarkScrollbars(_binList);
        EditorTheme.ApplyDarkScrollbars(_markerList);
    }

    private Control BuildInspector()
    {
        var inspector = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = EditorTheme.PanelBack,
            AutoScroll = true,
            Padding = new Padding(10, 4, 10, 10),
        };
        EditorTheme.ApplyDarkScrollbars(inspector);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
        };
        inspector.Controls.Add(stack);

        void AddCard(string title, Control content)
        {
            var titleLabel = EditorTheme.SectionLabel(title);
            stack.Controls.Add(titleLabel);
            content.Dock = DockStyle.Top;
            stack.Controls.Add(content);
        }

        TableLayoutPanel Grid(int columns, params (Control Control, int Column, int Row, int Span)[] cells)
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = columns,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4),
            };
            for (var i = 0; i < columns; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            foreach (var (control, column, row, span) in cells)
            {
                control.Dock = DockStyle.Fill;
                control.Margin = new Padding(1, 1, 1, 1);
                grid.Controls.Add(control, column, row);
                if (span > 1)
                    grid.SetColumnSpan(control, span);
            }

            return grid;
        }

        // Export card
        var browseButton = EditorTheme.ToolButton("Browse…", (_, _) => PickOutputFolder());
        var exportButton = EditorTheme.PrimaryButton("Export timeline (Ctrl+E)", async (_, _) => await ExportSequenceAsync());
        AddCard("Export", Grid(2,
            (EditorTheme.FieldLabel("Name"), 0, 0, 2),
            (_outputNameBox, 0, 1, 2),
            (EditorTheme.FieldLabel("Folder"), 0, 2, 2),
            (_outputFolderBox, 0, 3, 1),
            (browseButton, 1, 3, 1),
            (exportButton, 0, 4, 2)));

        // Source trim card
        var markInButton = EditorTheme.ToolButton("Mark IN (I)", (_, _) => SetTrimBoundary(isStart: true));
        var markOutButton = EditorTheme.ToolButton("Mark OUT (O)", (_, _) => SetTrimBoundary(isStart: false));
        var insertButton = EditorTheme.ToolButton("Insert  (,)", (_, _) => AddCurrentCutToSequence(overwrite: false));
        var overwriteButton = EditorTheme.ToolButton("Overwrite  (.)", (_, _) => AddCurrentCutToSequence(overwrite: true));
        var saveTrimButton = EditorTheme.ToolButton("Save IN/OUT as clip", async (_, _) => await RunTrimAsync());
        var mergeButton = EditorTheme.ToolButton("Merge selected clips", async (_, _) => await RunMergeAsync());
        AddCard("Source clip", Grid(2,
            (EditorTheme.FieldLabel("In (sec)"), 0, 0, 1),
            (EditorTheme.FieldLabel("Out (sec)"), 1, 0, 1),
            (_startBox, 0, 1, 1),
            (_endBox, 1, 1, 1),
            (markInButton, 0, 2, 1),
            (markOutButton, 1, 2, 1),
            (insertButton, 0, 3, 1),
            (overwriteButton, 1, 3, 1),
            (saveTrimButton, 0, 4, 2),
            (mergeButton, 0, 5, 2)));

        // Markers card
        var addMarkerButton = EditorTheme.ToolButton("Add at playhead (M)", (_, _) => AddMarkerAtPlayhead());
        var removeMarkerButton = EditorTheme.ToolButton("Remove", (_, _) => RemoveSelectedMarker());
        AddCard("Markers", Grid(2,
            (addMarkerButton, 0, 0, 1),
            (removeMarkerButton, 1, 0, 1),
            (_markerList, 0, 1, 2)));

        // Crop card
        var aspect169 = EditorTheme.ToolButton("16:9", (_, _) => ApplyAspectCrop(16, 9));
        var aspect916 = EditorTheme.ToolButton("9:16", (_, _) => ApplyAspectCrop(9, 16));
        var aspect11 = EditorTheme.ToolButton("1:1", (_, _) => ApplyAspectCrop(1, 1));
        var aspectReset = EditorTheme.ToolButton("Full", (_, _) => ResetCropToFullFrame());
        var saveCropButton = EditorTheme.ToolButton("Save cropped clip", async (_, _) => await RunCropAsync());
        AddCard("Crop", Grid(4,
            (_enableCropBox, 0, 0, 4),
            (aspect169, 0, 1, 1),
            (aspect916, 1, 1, 1),
            (aspect11, 2, 1, 1),
            (aspectReset, 3, 1, 1),
            (EditorTheme.FieldLabel("X"), 0, 2, 1),
            (EditorTheme.FieldLabel("Y"), 1, 2, 1),
            (EditorTheme.FieldLabel("W"), 2, 2, 1),
            (EditorTheme.FieldLabel("H"), 3, 2, 1),
            (_cropXBox, 0, 3, 1),
            (_cropYBox, 1, 3, 1),
            (_cropWBox, 2, 3, 1),
            (_cropHBox, 3, 3, 1),
            (_cropInfoLabel, 0, 4, 4),
            (saveCropButton, 0, 5, 4)));

        // Motion card (selected timeline clip)
        var setKeyframeButton = EditorTheme.ToolButton("Set keyframe", (_, _) => CaptureTransformKeyframeAtPlayhead());
        var resetMotionButton = EditorTheme.ToolButton("Reset motion", (_, _) => ResetSelectedMotion());
        AddCard("Motion — selected timeline clip", Grid(3,
            (EditorTheme.FieldLabel("Position X / Y"), 0, 0, 2),
            (_positionKeyframeButton, 2, 0, 1),
            (_positionXBox, 0, 1, 1),
            (_positionYBox, 1, 1, 1),
            (EditorTheme.FieldLabel("Scale %"), 0, 2, 2),
            (_scaleKeyframeButton, 2, 2, 1),
            (_scaleBox, 0, 3, 2),
            (EditorTheme.FieldLabel("Opacity %"), 0, 4, 2),
            (_opacityKeyframeButton, 2, 4, 1),
            (_opacityBox, 0, 5, 2),
            (setKeyframeButton, 0, 6, 2),
            (resetMotionButton, 2, 6, 1)));

        return inspector;
    }

    private void BuildMenu()
    {
        _menu.Renderer = new EditorStripRenderer();
        _menu.BackColor = EditorTheme.PanelBack;
        _menu.ForeColor = EditorTheme.TextPrimary;

        ToolStripMenuItem Item(string text, Keys shortcut, EventHandler onClick, bool busySensitive = true)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = EditorTheme.TextPrimary };
            if (shortcut != Keys.None)
            {
                item.ShortcutKeys = shortcut;
                item.ShowShortcutKeys = true;
            }

            item.Click += onClick;
            if (busySensitive)
                _busySensitiveItems.Add(item);
            return item;
        }

        var fileMenu = new ToolStripMenuItem("File") { ForeColor = EditorTheme.TextPrimary };
        fileMenu.DropDownItems.Add(Item("Import clips…", Keys.Control | Keys.I, (_, _) => ImportFilesDialog()));
        fileMenu.DropDownItems.Add(Item("Load watch folder", Keys.None, (_, _) => LoadWatchFolder()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(Item("Export timeline…", Keys.Control | Keys.E, async (_, _) => await ExportSequenceAsync()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(Item("Close editor", Keys.None, (_, _) => Close(), busySensitive: false));

        var editMenu = new ToolStripMenuItem("Edit") { ForeColor = EditorTheme.TextPrimary };
        _undoMenuItem = Item("Undo", Keys.Control | Keys.Z, (_, _) => UndoSequenceEdit());
        _redoMenuItem = Item("Redo", Keys.Control | Keys.Y, (_, _) => RedoSequenceEdit());
        editMenu.DropDownItems.Add(_undoMenuItem);
        editMenu.DropDownItems.Add(_redoMenuItem);
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(Item("Split at playhead", Keys.Control | Keys.K, (_, _) => SplitSelectedSegmentAtPlayhead()));
        editMenu.DropDownItems.Add(Item("Ripple delete selected", Keys.None, (_, _) => RippleDeleteSelected()));
        editMenu.DropDownItems.Add(Item("Clear timeline", Keys.None, (_, _) => ClearSequence()));

        var timelineMenu = new ToolStripMenuItem("Timeline") { ForeColor = EditorTheme.TextPrimary };
        timelineMenu.DropDownItems.Add(Item("Insert IN/OUT at playhead", Keys.None, (_, _) => AddCurrentCutToSequence(overwrite: false)));
        timelineMenu.DropDownItems.Add(Item("Overwrite IN/OUT at playhead", Keys.None, (_, _) => AddCurrentCutToSequence(overwrite: true)));
        timelineMenu.DropDownItems.Add(new ToolStripSeparator());
        timelineMenu.DropDownItems.Add(Item("Zoom in", Keys.Control | Keys.Oemplus, (_, _) => AdjustTimelineZoom(0.5), busySensitive: false));
        timelineMenu.DropDownItems.Add(Item("Zoom out", Keys.Control | Keys.OemMinus, (_, _) => AdjustTimelineZoom(-0.5), busySensitive: false));
        timelineMenu.DropDownItems.Add(Item("Toggle snapping", Keys.None, (_, _) => ToggleSnapping(), busySensitive: false));

        var helpMenu = new ToolStripMenuItem("Help") { ForeColor = EditorTheme.TextPrimary };
        helpMenu.DropDownItems.Add(Item("Keyboard shortcuts", Keys.None, (_, _) => ShowShortcutsDialog(), busySensitive: false));

        _menu.Items.Add(fileMenu);
        _menu.Items.Add(editMenu);
        _menu.Items.Add(timelineMenu);
        _menu.Items.Add(helpMenu);
    }

    private void BuildToolStrip()
    {
        _toolStrip.Renderer = new EditorStripRenderer();
        _toolStrip.BackColor = EditorTheme.PanelBack;
        _toolStrip.ForeColor = EditorTheme.TextPrimary;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.Padding = new Padding(6, 2, 6, 2);

        ToolStripButton ToolToggle(string text, string tooltip, TimelineTool tool)
        {
            var button = new ToolStripButton(text)
            {
                ForeColor = EditorTheme.TextPrimary,
                ToolTipText = tooltip,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
            };
            button.Click += (_, _) => SetTool(tool);
            _toolButtons[tool] = button;
            return button;
        }

        _toolStrip.Items.Add(ToolToggle("⌖ Select", "Selection tool (V) — drag clips to move, edges to trim", TimelineTool.Select));
        _toolStrip.Items.Add(ToolToggle("⇄ Ripple", "Ripple tool (B) — trims and moves close gaps downstream", TimelineTool.Ripple));
        _toolStrip.Items.Add(ToolToggle("⥮ Roll", "Rolling tool (N) — move a cut point between two clips", TimelineTool.Rolling));
        _toolStrip.Items.Add(ToolToggle("⇆ Slip", "Slip tool (Y) — slide clip contents without moving it", TimelineTool.Slip));
        _toolStrip.Items.Add(ToolToggle("✂ Razor", "Razor tool (C) — click a clip to cut it", TimelineTool.Razor));
        _toolStrip.Items.Add(new ToolStripSeparator());

        _snapButton = new ToolStripButton("Snap")
        {
            ForeColor = EditorTheme.TextPrimary,
            ToolTipText = "Toggle snapping (S)",
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Checked = _snappingEnabled,
            CheckOnClick = false,
        };
        _snapButton.Click += (_, _) => ToggleSnapping();
        _toolStrip.Items.Add(_snapButton);

        var zoomOut = new ToolStripButton("−") { ForeColor = EditorTheme.TextPrimary, ToolTipText = "Zoom out (Ctrl+-)" };
        zoomOut.Click += (_, _) => AdjustTimelineZoom(-0.5);
        var zoomIn = new ToolStripButton("+") { ForeColor = EditorTheme.TextPrimary, ToolTipText = "Zoom in (Ctrl+=)" };
        zoomIn.Click += (_, _) => AdjustTimelineZoom(0.5);
        _toolStrip.Items.Add(zoomOut);
        _toolStrip.Items.Add(zoomIn);
        _toolStrip.Items.Add(new ToolStripSeparator());

        var splitButton = new ToolStripButton("✂ Split") { ForeColor = EditorTheme.TextPrimary, ToolTipText = "Split selected clip at playhead (Ctrl+K)" };
        splitButton.Click += (_, _) => SplitSelectedSegmentAtPlayhead();
        _busySensitiveItems.Add(splitButton);
        _toolStrip.Items.Add(splitButton);

        var deleteButton = new ToolStripButton("Ripple delete") { ForeColor = EditorTheme.TextPrimary, ToolTipText = "Remove selected clip and close the gap (Del)" };
        deleteButton.Click += (_, _) => RippleDeleteSelected();
        _busySensitiveItems.Add(deleteButton);
        _toolStrip.Items.Add(deleteButton);
    }

    private void BuildStatusStrip()
    {
        _statusStrip.Renderer = new EditorStripRenderer();
        _statusStrip.BackColor = EditorTheme.PanelBack;
        _statusStrip.SizingGrip = false;
        _statusLabel.ForeColor = EditorTheme.TextSecondary;
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _sequenceStatsLabel.ForeColor = EditorTheme.TextMuted;
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_sequenceStatsLabel);
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    // ================================================================
    // Media bin
    // ================================================================

    private void ImportFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*",
            Title = "Import clips into the media bin",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            ImportFiles(dialog.FileNames);
    }

    private void ImportFiles(IEnumerable<string> files)
    {
        var added = 0;
        foreach (var file in files.Where(File.Exists))
        {
            if (!_mediaFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                _mediaFiles.Add(file);
                added++;
            }
        }

        SetStatus(added == 0 ? "Those clips are already in the media bin." : $"Imported {added} clip(s).");
        RefreshBinList();
        _ = RefreshMediaThumbnailsAsync();
    }

    private void LoadWatchFolder()
    {
        if (!Directory.Exists(_watchFolder))
        {
            SetStatus($"Watch folder not found: {_watchFolder}");
            return;
        }

        var files = Directory
            .EnumerateFiles(_watchFolder, "*.*", SearchOption.AllDirectories)
            .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(120)
            .ToList();

        if (files.Count == 0)
            SetStatus("No videos found in the watch folder yet.");
        else
            ImportFiles(files);
    }

    private void RemoveSelectedFromBin()
    {
        var selected = _binList.SelectedItems.Cast<object>().Select(item => item.ToString()!).ToList();
        if (selected.Count == 0)
            return;

        _mediaFiles.RemoveAll(file => selected.Contains(file, StringComparer.OrdinalIgnoreCase));
        RefreshBinList();
        SetStatus($"Removed {selected.Count} clip(s) from the bin. Timeline cuts that use them are unaffected.");
    }

    private void RefreshBinList()
    {
        var filter = _binSearchBox.Text.Trim();
        var visible = string.IsNullOrEmpty(filter)
            ? _mediaFiles
            : _mediaFiles.Where(file => Path.GetFileName(file).Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Rebuilding the list fires selection events; the loaded clip should survive a filter change.
        _suppressBinEvents = true;
        try
        {
            _binList.BeginUpdate();
            _binList.Items.Clear();
            foreach (var file in visible)
                _binList.Items.Add(file);
            if (_selectedFile != null)
            {
                var index = _binList.Items.IndexOf(_selectedFile);
                if (index >= 0)
                    _binList.SelectedIndex = index;
            }

            _binList.EndUpdate();
        }
        finally
        {
            _suppressBinEvents = false;
        }
    }

    private void MoveBinSelection(int direction)
    {
        if (_binList.SelectedItems.Count != 1 || _binList.SelectedItem?.ToString() is not { } file)
            return;

        var index = _mediaFiles.FindIndex(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _mediaFiles.Count)
            return;

        (_mediaFiles[index], _mediaFiles[newIndex]) = (_mediaFiles[newIndex], _mediaFiles[index]);
        RefreshBinList();
    }

    private void DrawMediaBinItem(object? sender, DrawItemEventArgs e)
    {
        using var surfaceBrush = new SolidBrush(EditorTheme.InsetBack);
        e.Graphics.FillRectangle(surfaceBrush, e.Bounds);

        if (e.Index < 0 || e.Index >= _binList.Items.Count)
            return;

        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

        var filePath = _binList.Items[e.Index]?.ToString() ?? string.Empty;
        var isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var tileRect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, e.Bounds.Width - 8, e.Bounds.Height - 6);
        var thumbRect = new Rectangle(tileRect.Left + 4, tileRect.Top + 4, 118, tileRect.Height - 8);
        var textRect = new Rectangle(thumbRect.Right + 8, tileRect.Top + 6, Math.Max(20, tileRect.Right - thumbRect.Right - 12), tileRect.Height - 12);

        using var tileBrush = new SolidBrush(isSelected ? Color.FromArgb(40, EditorTheme.Accent) : EditorTheme.PanelBack);
        using var borderPen = new Pen(isSelected ? EditorTheme.Accent : EditorTheme.BorderSoft, 1);
        using var thumbBack = new SolidBrush(Color.Black);
        e.Graphics.FillRectangle(tileBrush, tileRect);
        e.Graphics.FillRectangle(thumbBack, thumbRect);
        e.Graphics.DrawRectangle(borderPen, tileRect);

        if (!string.IsNullOrWhiteSpace(filePath) && _mediaThumbCache.TryGetValue(filePath, out var cachedThumb))
            e.Graphics.DrawImage(cachedThumb, thumbRect);
        else
            TextRenderer.DrawText(e.Graphics, "…", EditorTheme.SmallBoldFont, thumbRect, EditorTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(
            e.Graphics,
            Path.GetFileName(filePath),
            EditorTheme.SmallBoldFont,
            textRect,
            isSelected ? Color.White : EditorTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);

        var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        var subRect = new Rectangle(textRect.Left, textRect.Bottom - 16, textRect.Width, 14);
        TextRenderer.DrawText(e.Graphics, folder, EditorTheme.TinyFont, subRect, EditorTheme.TextMuted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private async Task RefreshMediaThumbnailsAsync()
    {
        if (IsDisposed)
            return;

        var files = _mediaFiles.Where(File.Exists).ToList();
        if (!string.IsNullOrWhiteSpace(_selectedFile) && files.Remove(_selectedFile))
            files.Insert(0, _selectedFile);

        foreach (var file in files.Take(32))
        {
            if (IsDisposed)
                return;

            if (!_mediaThumbCache.ContainsKey(file))
            {
                try
                {
                    using var thumb = await FfmpegService.ExtractFrameAsync(file, 0, CancellationToken.None, maxWidth: 320);
                    _mediaThumbCache[file] = new Bitmap(thumb, new Size(192, 108));
                }
                catch
                {
                    var fallback = new Bitmap(192, 108);
                    using var g = Graphics.FromImage(fallback);
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, "VIDEO", EditorTheme.SmallBoldFont, new Rectangle(0, 44, 192, 20), EditorTheme.TextMuted, TextFormatFlags.HorizontalCenter);
                    _mediaThumbCache[file] = fallback;
                }

                _binList.Invalidate();
            }

            if (!_stripThumbCache.ContainsKey(file))
            {
                var strip = new List<Image>();
                foreach (var sampleTime in new[] { 0d, 5d, 10d })
                {
                    try
                    {
                        using var frame = await FfmpegService.ExtractFrameAsync(file, sampleTime, CancellationToken.None, maxWidth: 200);
                        strip.Add(new Bitmap(frame, new Size(96, 54)));
                    }
                    catch
                    {
                    }
                }

                if (strip.Count > 0)
                {
                    _stripThumbCache[file] = strip;
                    _sequenceView.Invalidate();
                }
            }
        }

        _binList.Invalidate();
    }

    // ================================================================
    // Selection / preview
    // ================================================================

    private async Task HandleSelectionChangedAsync()
    {
        if (_suppressBinEvents)
            return;

        if (_binList.SelectedItems.Count != 1)
        {
            _selectedFile = null;
            StopPlayback(resetToStart: true);
            _mediaElement.Source = null;
            _clipInfoLabel.Text = _binList.SelectedItems.Count > 1
                ? "Multiple clips selected — Merge works on the selection; preview needs a single clip."
                : "Select a clip to preview and edit.";
            _trimView.SetTimeline(0, 0, 0, 0, _binList.SelectedItems.Count > 1 ? "Multiple clips selected" : "No clip loaded");
            _trimView.SetThumbnails([]);
            _trimView.SetWaveform(null);
            UpdateMarkerUi();
            _sourcePreview.ClearPreview();
            return;
        }

        var file = _binList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            _selectedFile = null;
            _clipInfoLabel.Text = "The selected file could not be found on disk.";
            return;
        }

        await LoadClipIntoEditorAsync(file);
    }

    private async Task LoadClipIntoEditorAsync(string file)
    {
        if (string.Equals(file, _selectedFile, StringComparison.OrdinalIgnoreCase))
            return;

        _selectedFile = file;
        SetStatus($"Loading {Path.GetFileName(file)}…");

        try
        {
            var details = await FfmpegService.ProbeAsync(file, CancellationToken.None);
            _videoDuration = details.Duration;
            _videoSize = new Size(details.Width, details.Height);
            _clipInfoLabel.Text = $"{Path.GetFileName(file)}  •  {details.Width}×{details.Height}  •  {details.Fps:0.##} fps  •  {EditorTime.Format(details.Duration)}{(details.HasAudio ? string.Empty : "  •  no audio")}";

            var durationDecimal = (decimal)Math.Max(details.Duration, 0.25);
            _updatingTrimRange = true;
            try
            {
                _startBox.Maximum = durationDecimal;
                _endBox.Maximum = durationDecimal;
                _startBox.Value = 0;
                _endBox.Value = durationDecimal;
            }
            finally
            {
                _updatingTrimRange = false;
            }

            _cropXBox.Maximum = Math.Max(1, details.Width);
            _cropYBox.Maximum = Math.Max(1, details.Height);
            _cropWBox.Maximum = Math.Max(1, details.Width);
            _cropHBox.Maximum = Math.Max(1, details.Height);

            StopPlayback(resetToStart: true);
            _mediaElement.Source = new Uri(file);
            _programStatusLabel.Text = "Loading video…";
            ShowProgramVideo();

            _scrubBar.Value = 0;
            UpdateTrimTimelineUi();
            UpdateMarkerUi();
            ResetCropToFullFrame();
            _ = RefreshMediaThumbnailsAsync();
            await RefreshTrimStripAsync();
            await RefreshWaveformAsync();
            await RefreshPreviewAsync(0);
            SetStatus($"Loaded {Path.GetFileName(file)}.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load video preview info", ex);
            _clipInfoLabel.Text = $"Preview unavailable: {ex.Message}";
            SetStatus($"Could not load the selected clip: {ex.Message}");
        }
    }

    private double GetSourcePlayhead()
    {
        if (_videoDuration <= 0)
            return 0;
        return (_scrubBar.Value / 1000d) * _videoDuration;
    }

    private void QueuePreviewRefreshFromSlider()
    {
        _requestedPreviewTime = GetSourcePlayhead();
        _sourceTimeLabel.Text = EditorTime.Format(_requestedPreviewTime);

        if (!_timelineUpdateFromPlayer)
            SeekSource(_requestedPreviewTime, refreshPreview: false);

        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private async Task RefreshPreviewAsync(double time)
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
            return;

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var image = await FfmpegService.ExtractFrameAsync(_selectedFile, time, ct);
            if (ct.IsCancellationRequested)
            {
                image.Dispose();
                return;
            }

            _sourcePreview.VideoSize = _videoSize;
            _sourcePreview.SetPreviewImage(image);
            _sourcePreview.ShowCropOverlay = _enableCropBox.Checked;
            _sourceTimeLabel.Text = EditorTime.Format(time);
            _trimView.SetPlayhead(time);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to refresh preview", ex);
            SetStatus($"Preview failed: {ex.Message}");
        }
    }

    private async Task RefreshTrimStripAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile) || _videoDuration <= 0)
        {
            _trimView.SetThumbnails([]);
            return;
        }

        if (_stripThumbCache.TryGetValue(_selectedFile, out var cached) && cached.Count > 0)
        {
            _trimView.SetThumbnails(cached);
            return;
        }

        var thumbs = new List<Image>();
        const int samples = 6;
        for (var i = 0; i < samples; i++)
        {
            try
            {
                var time = _videoDuration <= 0.25 ? 0 : (_videoDuration * i) / Math.Max(1, samples - 1);
                using var frame = await FfmpegService.ExtractFrameAsync(_selectedFile, time, CancellationToken.None, maxWidth: 220);
                thumbs.Add(new Bitmap(frame, new Size(112, 34)));
            }
            catch
            {
                break;
            }
        }

        if (thumbs.Count > 0)
        {
            _stripThumbCache[_selectedFile] = thumbs;
            _trimView.SetThumbnails(thumbs);
        }
    }

    private async Task RefreshWaveformAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile) || !File.Exists(_selectedFile))
        {
            _trimView.SetWaveform(null);
            return;
        }

        if (!_waveformCache.TryGetValue(_selectedFile, out var waveform))
        {
            waveform = await FfmpegService.ExtractWaveformAsync(_selectedFile);
            _waveformCache[_selectedFile] = waveform;
        }

        _trimView.SetWaveform(waveform);
        _sequenceView.Invalidate();
    }

    // ================================================================
    // Playback / transport
    // ================================================================

    private void OnMediaOpened()
    {
        if (_mediaElement.NaturalDuration.HasTimeSpan)
            _videoDuration = Math.Max(_videoDuration, _mediaElement.NaturalDuration.TimeSpan.TotalSeconds);

        _programStatusLabel.Text = "Paused — Space to play, J/K/L to shuttle.";
        _playPauseButton.Text = "▶ Play";
        _isPlaying = false;
        UpdatePlayerPositionFromPlayback();
    }

    private void TogglePlayback()
    {
        if (_mediaElement.Source == null)
        {
            SetStatus("Load a clip first.");
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
            SetStatus("Load a clip before using J/K/L transport.");
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
            ShowProgramVideo();
            if (_transportDirection >= 0)
            {
                _mediaElement.SpeedRatio = speed;
                _mediaElement.Play();
                _isPlaying = true;
                _playPauseButton.Text = speed > 1 ? $"▶ {speed:0}×" : "❚❚ Pause";
                _programStatusLabel.Text = speed > 1 ? $"Playing {speed:0}× (L to ramp, K to stop)." : "Playing…";
            }
            else
            {
                _mediaElement.Pause();
                _isPlaying = false;
                _playPauseButton.Text = $"◀ {speed:0}×";
                _programStatusLabel.Text = $"Reverse {speed:0}× (J to ramp, K to stop).";
            }

            _playerTimer.Start();
        }
        catch (Exception ex)
        {
            _programStatusLabel.Text = $"Playback failed: {ex.Message}";
            SetStatus(_programStatusLabel.Text);
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
            try { _mediaElement.Position = TimeSpan.Zero; } catch { }
        }

        _transportDirection = 0;
        _transportSpeedLevel = 0;
        _isPlaying = false;
        _playerTimer.Stop();
        _playPauseButton.Text = "▶ Play";
        _programStatusLabel.Text = _mediaElement.Source == null
            ? "Load a clip to start playback."
            : "Paused — Space to play, J/K/L to shuttle.";

        if (resetToStart)
        {
            _timelineUpdateFromPlayer = true;
            _scrubBar.Value = 0;
            _timelineUpdateFromPlayer = false;
            _sourceTimeLabel.Text = "00:00.000";
            _trimView.SetPlayhead(0);
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
            var target = GetSourcePlayhead() - (0.12 * speed);
            if (target <= 0.01)
            {
                SeekSource(0, refreshPreview: true);
                StopPlayback(resetToStart: false);
                return;
            }

            SeekSource(target, refreshPreview: true);
            return;
        }

        if (_transportDirection > 0)
            UpdatePlayerPositionFromPlayback();
    }

    private void SkipSeconds(double deltaSeconds) => SeekSource(GetSourcePlayhead() + deltaSeconds, refreshPreview: true);

    /// <summary>Seeks the source monitor. Also advances the sequence playhead when the loaded clip is the selected timeline segment.</summary>
    private void SeekSource(double seconds, bool refreshPreview)
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
        _scrubBar.Value = _videoDuration <= 0
            ? 0
            : Math.Clamp((int)Math.Round((clamped / _videoDuration) * _scrubBar.Maximum), 0, _scrubBar.Maximum);
        _timelineUpdateFromPlayer = false;
        _sourceTimeLabel.Text = EditorTime.Format(clamped);
        _trimView.SetPlayhead(clamped);
        SyncSequencePlayheadFromSource(clamped);

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
            _scrubBar.Value = _videoDuration <= 0
                ? 0
                : Math.Clamp((int)Math.Round((position / Math.Max(_videoDuration, 0.001)) * _scrubBar.Maximum), 0, _scrubBar.Maximum);
            _timelineUpdateFromPlayer = false;
            _sourceTimeLabel.Text = EditorTime.Format(position);
            _trimView.SetPlayhead(position);
            SyncSequencePlayheadFromSource(position);
        }
        catch
        {
        }
    }

    /// <summary>Maps the source playhead to sequence time when the loaded clip is the selected timeline segment.</summary>
    private void SyncSequencePlayheadFromSource(double sourceSeconds)
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
            return;

        var segment = _sequence.Segments[_selectedSegmentIndex];
        if (!string.Equals(segment.SourceFile, _selectedFile, StringComparison.OrdinalIgnoreCase))
            return;

        var localOffset = Math.Clamp(sourceSeconds - segment.StartSec, 0, segment.Duration);
        _sequencePlayheadSec = segment.SequenceStartSec + localOffset;
        _sequenceView.SetPlayhead(_sequencePlayheadSec);
        UpdateTransformInspectorUi();
    }

    // ================================================================
    // Sequence editing
    // ================================================================

    private void OnSequenceChanged(int selectIndex)
    {
        _selectedSegmentIndex = selectIndex >= 0 && selectIndex < _sequence.Segments.Count ? selectIndex : Math.Min(_selectedSegmentIndex, _sequence.Segments.Count - 1);
        UpdateSequenceDependentUi();
    }

    private void SelectSegment(int index)
    {
        if (index >= 0)
            _selectedSegmentIndex = index;
        UpdateSequenceDependentUi();
    }

    private void UpdateSequenceDependentUi()
    {
        _sequenceView.SetSegments(_sequence.Segments, _selectedSegmentIndex);
        _sequenceView.SetTargetTracks(_targetVideoTrack, _targetAudioTrack);

        var total = _sequence.TotalDuration;
        var v1Count = _sequence.Segments.Count(s => s.SafeTrack == 1);
        var v2Count = _sequence.Segments.Count(s => s.SafeTrack == 2);
        _sequenceStatsLabel.Text = _sequence.Segments.Count == 0
            ? "Timeline empty"
            : $"{_sequence.Segments.Count} cut(s)  •  V1 {v1Count} / V2 {v2Count}  •  {EditorTime.Format(total)}";

        _undoMenuItem.Enabled = _sequence.CanUndo && !_busy;
        _redoMenuItem.Enabled = _sequence.CanRedo && !_busy;
        UpdateTransformInspectorUi();
    }

    private void AddCurrentCutToSequence(bool overwrite)
    {
        if (!TryGetSingleSelectedFile(out var input, out var error))
        {
            SetStatus(error);
            return;
        }

        var start = (double)_startBox.Value;
        var end = (double)_endBox.Value;
        if (end <= start)
        {
            SetStatus("OUT must be after IN before inserting into the timeline.");
            return;
        }

        var segment = new TimelineSegment(input, start, end, _targetVideoTrack, _sequencePlayheadSec, new ClipTransform());
        var index = overwrite
            ? _sequence.Overwrite(segment, _sequencePlayheadSec)
            : _sequence.Insert(segment, _sequencePlayheadSec);
        SelectSegment(index);

        // Park the playhead at the end of the new cut so consecutive inserts chain naturally.
        _sequencePlayheadSec = _sequence.Segments[index].SequenceEndSec;
        _sequenceView.SetPlayhead(_sequencePlayheadSec);
        SetStatus($"{(overwrite ? "Overwrite" : "Insert")} on V{_targetVideoTrack} at {EditorTime.Format(segment.SequenceStartSec)}.");
    }

    private void SplitSelectedSegmentAtPlayhead()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
        {
            SetStatus("Select a timeline clip first (click it), then split.");
            return;
        }

        var segment = _sequence.Segments[_selectedSegmentIndex];
        var localOffset = _sequencePlayheadSec - segment.SequenceStartSec;
        var sourceTime = EditorTime.SnapToFrame(segment.StartSec + localOffset);
        var result = _sequence.Split(_selectedSegmentIndex, sourceTime);
        SetStatus(result >= 0
            ? $"Split at {EditorTime.Format(_sequencePlayheadSec)}."
            : "Place the playhead inside the selected clip, not on its edge.");
        if (result >= 0)
            SelectSegment(result);
    }

    private void RippleDeleteSelected()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
            return;

        _sequence.RippleDelete(_selectedSegmentIndex);
        SetStatus("Ripple deleted the selected clip.");
    }

    private void ClearSequence()
    {
        _sequence.Clear();
        _sequencePlayheadSec = 0;
        _sequenceView.SetPlayhead(0);
        SetStatus("Cleared the timeline.");
    }

    private void UndoSequenceEdit()
    {
        SetStatus(_sequence.Undo() ? "Undid the last timeline edit." : "Nothing to undo.");
    }

    private void RedoSequenceEdit()
    {
        SetStatus(_sequence.Redo() ? "Redid the timeline edit." : "Nothing to redo.");
    }

    private async Task LoadSequenceSegmentAsync(int index)
    {
        if (index < 0 || index >= _sequence.Segments.Count)
            return;

        _selectedSegmentIndex = index;
        var segment = _sequence.Segments[index];

        if (!_mediaFiles.Contains(segment.SourceFile, StringComparer.OrdinalIgnoreCase))
        {
            _mediaFiles.Insert(0, segment.SourceFile);
            RefreshBinList();
        }

        if (!string.Equals(_selectedFile, segment.SourceFile, StringComparison.OrdinalIgnoreCase))
        {
            var binIndex = _binList.Items.IndexOf(segment.SourceFile);
            if (binIndex >= 0)
            {
                _suppressBinEvents = true;
                try
                {
                    _binList.ClearSelected();
                    _binList.SelectedIndex = binIndex;
                }
                finally
                {
                    _suppressBinEvents = false;
                }
            }

            await LoadClipIntoEditorAsync(segment.SourceFile);
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
        _sequenceView.SetSelectedIndex(index);
        _sequencePlayheadSec = segment.SequenceStartSec;
        _sequenceView.SetPlayhead(_sequencePlayheadSec);
        SeekSource(segment.StartSec, refreshPreview: true);
        UpdateTransformInspectorUi();
    }

    private async Task ScrubSequenceToTimeAsync(double sequenceSeconds)
    {
        _sequencePlayheadSec = Math.Clamp(sequenceSeconds, 0, Math.Max(0.001, _sequence.TotalDuration));
        _sequenceView.SetPlayhead(_sequencePlayheadSec);

        if (_sequence.Segments.Count == 0)
            return;

        var segment = _sequence.TopVisibleAt(_sequencePlayheadSec);
        if (segment is null)
        {
            // Playhead is in a gap — show black in the program monitor.
            ShowSequenceFrame(null);
            return;
        }

        var index = _sequence.IndexOf(segment);
        if (index >= 0 && index != _selectedSegmentIndex)
        {
            _selectedSegmentIndex = index;
            _sequenceView.SetSelectedIndex(index);
            UpdateTransformInspectorUi();
        }

        await RenderSequencePreviewAsync(_sequencePlayheadSec);
    }

    private async Task RenderSequencePreviewAsync(double sequenceTime)
    {
        var active = _sequence.ActiveAt(sequenceTime);
        if (active.Count == 0)
        {
            ShowSequenceFrame(null);
            return;
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var canvas = new Bitmap(960, 540);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(Color.FromArgb(8, 8, 10));
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                foreach (var segment in active.OrderBy(s => s.SafeTrack))
                {
                    var clipTime = Math.Clamp(segment.StartSec + Math.Max(0, sequenceTime - segment.SequenceStartSec), segment.StartSec, segment.EndSec);
                    using var frame = await FfmpegService.ExtractFrameAsync(segment.SourceFile, clipTime, ct);
                    using var transformed = ApplyTransformToCanvas(frame, _sequence.EvaluateTransform(segment, sequenceTime), canvas.Size);
                    graphics.DrawImage(transformed, 0, 0, canvas.Width, canvas.Height);
                }
            }

            if (!ct.IsCancellationRequested)
                ShowSequenceFrame(canvas);
            else
                canvas.Dispose();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Error("Sequence preview failed", ex);
        }
    }

    private void ShowSequenceFrame(Image? image)
    {
        ReplaceImage(_sequencePreview, image);
        _sequencePreview.Visible = true;
        _playerHost.Visible = false;
        _programStatusLabel.Text = image == null
            ? $"Sequence {EditorTime.Format(_sequencePlayheadSec)} — gap (black)."
            : $"Sequence preview at {EditorTime.Format(_sequencePlayheadSec)}. Space plays the loaded clip.";
    }

    private void ShowProgramVideo()
    {
        if (!_playerHost.Visible)
        {
            _playerHost.Visible = true;
            _sequencePreview.Visible = false;
        }
    }

    private static Bitmap ApplyTransformToCanvas(Image source, ClipTransform transform, Size targetSize)
    {
        var canvas = new Bitmap(Math.Max(1, targetSize.Width), Math.Max(1, targetSize.Height));
        using var graphics = Graphics.FromImage(canvas);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

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

    // ================================================================
    // Trim / markers
    // ================================================================

    private void SetTrimBoundary(bool isStart)
    {
        var current = (decimal)EditorTime.SnapToFrame(GetSourcePlayhead());
        if (isStart)
            _startBox.Value = Math.Min(_endBox.Value, current);
        else
            _endBox.Value = Math.Max(_startBox.Value, current);

        UpdateTrimTimelineUi();
    }

    private void SyncTrimRangeFromFields()
    {
        if (_updatingTrimRange)
            return;

        if (_endBox.Value < _startBox.Value)
        {
            _updatingTrimRange = true;
            try { _endBox.Value = _startBox.Value; }
            finally { _updatingTrimRange = false; }
        }

        UpdateTrimTimelineUi();
    }

    private void ApplyTrimRangeFromTimeline(double start, double end)
    {
        if (_updatingTrimRange)
            return;

        var snappedStart = EditorTime.SnapToFrame(start);
        var snappedEnd = Math.Max(snappedStart + (1d / EditorTime.DefaultFps), EditorTime.SnapToFrame(end));
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
        var label = string.IsNullOrWhiteSpace(_selectedFile) ? "No clip loaded" : Path.GetFileName(_selectedFile);
        _trimView.SetTimeline(_videoDuration, (double)_startBox.Value, (double)_endBox.Value, GetSourcePlayhead(), label);
        _trimView.SetMarkers(GetCurrentClipMarkers());
    }

    private List<double> GetCurrentClipMarkers()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile))
            return [];

        return _clipMarkers.TryGetValue(_selectedFile, out var markers) ? markers : [];
    }

    private void AddMarkerAtPlayhead()
    {
        if (string.IsNullOrWhiteSpace(_selectedFile))
        {
            SetStatus("Load a clip before adding markers.");
            return;
        }

        if (!_clipMarkers.TryGetValue(_selectedFile, out var markers))
        {
            markers = [];
            _clipMarkers[_selectedFile] = markers;
        }

        var current = Math.Round(GetSourcePlayhead(), 3);
        if (!markers.Any(value => Math.Abs(value - current) < 0.05))
            markers.Add(current);
        markers.Sort();
        UpdateMarkerUi();
        SetStatus($"Marker at {EditorTime.Format(current)}.");
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
        SetStatus("Marker removed.");
    }

    private void JumpToSelectedMarker()
    {
        var index = _markerList.SelectedIndex;
        var markers = GetCurrentClipMarkers();
        if (index < 0 || index >= markers.Count)
            return;

        SeekSource(markers[index], refreshPreview: true);
    }

    private void UpdateMarkerUi()
    {
        _markerList.BeginUpdate();
        _markerList.Items.Clear();
        var markers = GetCurrentClipMarkers();
        for (var index = 0; index < markers.Count; index++)
            _markerList.Items.Add($"Marker {index + 1}  •  {EditorTime.Format(markers[index])}");
        _markerList.EndUpdate();
        _trimView.SetMarkers(markers);
    }

    // ================================================================
    // Crop
    // ================================================================

    private void OnCropSelectionChanged(Rectangle cropRect)
    {
        if (_updatingCropFields)
            return;

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = Math.Clamp(cropRect.X, _cropXBox.Minimum, _cropXBox.Maximum);
            _cropYBox.Value = Math.Clamp(cropRect.Y, _cropYBox.Minimum, _cropYBox.Maximum);
            _cropWBox.Value = Math.Clamp(Math.Max(1, cropRect.Width), _cropWBox.Minimum, _cropWBox.Maximum);
            _cropHBox.Value = Math.Clamp(Math.Max(1, cropRect.Height), _cropHBox.Minimum, _cropHBox.Maximum);
        }
        finally
        {
            _updatingCropFields = false;
        }

        UpdateCropInfoLabel(cropRect);
    }

    private void SyncCropPreviewFromFields()
    {
        if (_updatingCropFields)
            return;

        var cropRect = GetCropRectangle();
        _sourcePreview.SetCropRect(cropRect);
        UpdateCropInfoLabel(cropRect);
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
        var rect = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));

        _updatingCropFields = true;
        try
        {
            _cropXBox.Value = rect.X;
            _cropYBox.Value = rect.Y;
            _cropWBox.Value = rect.Width;
            _cropHBox.Value = rect.Height;
        }
        finally
        {
            _updatingCropFields = false;
        }

        if (!_enableCropBox.Checked)
            _enableCropBox.Checked = true;
        _sourcePreview.SetCropRect(rect);
        UpdateCropInfoLabel(rect);
        SetStatus($"Applied {aspectWidth}:{aspectHeight} crop guide.");
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

    private void UpdateCropInfoLabel(Rectangle cropRect)
    {
        if (_videoSize.Width <= 0 || _videoSize.Height <= 0 || cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            _cropInfoLabel.Text = "Crop: full frame";
            return;
        }

        var coverage = (cropRect.Width * cropRect.Height * 100d) / Math.Max(1d, _videoSize.Width * (double)_videoSize.Height);
        _cropInfoLabel.Text = $"Crop: {cropRect.Width}×{cropRect.Height} • {DescribeAspect(cropRect.Width, cropRect.Height)} • {coverage:F0}% of frame";
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

    // ================================================================
    // Motion / keyframes
    // ================================================================

    private void UpdateTransformInspectorUi()
    {
        var hasSelection = _selectedSegmentIndex >= 0 && _selectedSegmentIndex < _sequence.Segments.Count;
        _positionXBox.Enabled = hasSelection;
        _positionYBox.Enabled = hasSelection;
        _scaleBox.Enabled = hasSelection;
        _opacityBox.Enabled = hasSelection;
        _positionKeyframeButton.Enabled = hasSelection;
        _scaleKeyframeButton.Enabled = hasSelection;
        _opacityKeyframeButton.Enabled = hasSelection;

        if (!hasSelection)
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

            EditorTheme.SetToggleState(_positionKeyframeButton, false);
            EditorTheme.SetToggleState(_scaleKeyframeButton, false);
            EditorTheme.SetToggleState(_opacityKeyframeButton, false);
            return;
        }

        var segment = _sequence.Segments[_selectedSegmentIndex];
        var transform = _sequence.EvaluateTransform(segment, _sequencePlayheadSec);
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

        EditorTheme.SetToggleState(_positionKeyframeButton, segment.SafeTransform.PositionKeyframed);
        EditorTheme.SetToggleState(_scaleKeyframeButton, segment.SafeTransform.ScaleKeyframed);
        EditorTheme.SetToggleState(_opacityKeyframeButton, segment.SafeTransform.OpacityKeyframed);
    }

    private void SyncTransformFromFields()
    {
        if (_updatingTransformFields || _selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
            return;

        var segment = _sequence.Segments[_selectedSegmentIndex];
        var transform = segment.SafeTransform with
        {
            PositionX = (float)_positionXBox.Value,
            PositionY = (float)_positionYBox.Value,
            Scale = Math.Max(0.1f, (float)_scaleBox.Value / 100f),
            Opacity = Math.Clamp((float)_opacityBox.Value / 100f, 0f, 1f),
        };

        if (transform.AnyKeyframed)
            transform = UpsertKeyframe(segment, transform);

        _sequence.SetTransform(_selectedSegmentIndex, transform);
        _ = RenderSequencePreviewAsync(_sequencePlayheadSec);
    }

    private ClipTransform UpsertKeyframe(TimelineSegment segment, ClipTransform transform)
    {
        var localTime = Math.Clamp(_sequencePlayheadSec - segment.SequenceStartSec, 0, Math.Max(0.001, segment.Duration));
        var keyframes = transform.SafeKeyframes.ToList();
        var keyframe = new TransformKeyframe(localTime, transform.PositionX, transform.PositionY, transform.Scale, transform.Opacity);
        var existingIndex = keyframes.FindIndex(frame => Math.Abs(frame.LocalSec - localTime) <= (1d / EditorTime.DefaultFps));
        if (existingIndex >= 0)
            keyframes[existingIndex] = keyframe;
        else
            keyframes.Add(keyframe);

        return transform with { Keyframes = keyframes.OrderBy(frame => frame.LocalSec).ToList() };
    }

    private void ToggleKeyframing(string propertyName)
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
        {
            SetStatus("Select a timeline clip first.");
            return;
        }

        var segment = _sequence.Segments[_selectedSegmentIndex];
        var transform = segment.SafeTransform;
        transform = propertyName switch
        {
            "position" => transform with { PositionKeyframed = !transform.PositionKeyframed },
            "scale" => transform with { ScaleKeyframed = !transform.ScaleKeyframed },
            "opacity" => transform with { OpacityKeyframed = !transform.OpacityKeyframed },
            _ => transform,
        };

        if (transform.AnyKeyframed)
            transform = UpsertKeyframe(segment, transform);

        _sequence.SetTransform(_selectedSegmentIndex, transform);
        SetStatus($"{propertyName} keyframing {(transform.AnyKeyframed ? "armed" : "off")} for the selected clip.");
    }

    private void CaptureTransformKeyframeAtPlayhead()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
        {
            SetStatus("Select a timeline clip first.");
            return;
        }

        var segment = _sequence.Segments[_selectedSegmentIndex];
        var transform = segment.SafeTransform;
        if (!transform.AnyKeyframed)
        {
            SetStatus("Enable at least one stopwatch (⏱) before adding a keyframe.");
            return;
        }

        _sequence.SetTransform(_selectedSegmentIndex, UpsertKeyframe(segment, transform));
        SetStatus($"Keyframe captured at {EditorTime.Format(_sequencePlayheadSec)}.");
    }

    private void ResetSelectedMotion()
    {
        if (_selectedSegmentIndex < 0 || _selectedSegmentIndex >= _sequence.Segments.Count)
            return;

        _sequence.SetTransform(_selectedSegmentIndex, new ClipTransform());
        SetStatus("Reset position, scale, and opacity for the selected clip.");
        _ = RenderSequencePreviewAsync(_sequencePlayheadSec);
    }

    // ================================================================
    // Tools / zoom / snapping
    // ================================================================

    private void SetTool(TimelineTool tool)
    {
        _tool = tool;
        _sequenceView.SetTool(tool);
        foreach (var (buttonTool, button) in _toolButtons)
            button.Checked = buttonTool == tool;

        SetStatus(tool switch
        {
            TimelineTool.Razor => "Razor (C): click a timeline clip to cut it.",
            TimelineTool.Ripple => "Ripple (B): trims and moves close the gap downstream.",
            TimelineTool.Rolling => "Rolling (N): drag a cut edge to move the edit point between clips.",
            TimelineTool.Slip => "Slip (Y): drag a clip to slide its contents without moving it.",
            _ => "Select (V): drag clips to move them, edges to trim, empty space to scrub.",
        });
    }

    private void AdjustTimelineZoom(double delta)
    {
        _timelineZoom = Math.Clamp(_timelineZoom + delta, 1, 6);
        _trimView.SetZoom(_timelineZoom);
        _sequenceView.SetZoom(_timelineZoom);
    }

    private void ToggleSnapping()
    {
        _snappingEnabled = !_snappingEnabled;
        _snapButton.Checked = _snappingEnabled;
        _trimView.SetSnappingEnabled(_snappingEnabled);
        _sequenceView.SetSnappingEnabled(_snappingEnabled);
        SetStatus(_snappingEnabled ? "Snapping on." : "Snapping off.");
    }

    // ================================================================
    // Keyboard
    // ================================================================

    private static bool IsTextInputFocused(Control? control)
    {
        while (control is ContainerControl container && container.ActiveControl != null)
            control = container.ActiveControl;
        return control is TextBoxBase or NumericUpDown or ComboBox;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Never hijack plain keys while the user is typing in a field.
        if (IsTextInputFocused(this) && !e.Control)
            return;

        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Z when e.Shift:
                    RedoSequenceEdit();
                    break;
                case Keys.K:
                    SplitSelectedSegmentAtPlayhead();
                    break;
                case Keys.Left or Keys.Right:
                    var step = (e.KeyCode == Keys.Right ? 1 : -1) * (e.Shift ? 5d : 1d) / EditorTime.DefaultFps;
                    NudgeNearestTrimHandle(step);
                    break;
                default:
                    return; // menu shortcuts (Ctrl+Z/Y/I/E/…) are handled by the MenuStrip
            }

            e.SuppressKeyPress = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Space:
                TogglePlayback();
                break;
            case Keys.J or Keys.K or Keys.L:
                HandleTransportKey(e.KeyCode);
                break;
            case Keys.I:
                SetTrimBoundary(isStart: true);
                break;
            case Keys.O:
                SetTrimBoundary(isStart: false);
                break;
            case Keys.Oemcomma:
                AddCurrentCutToSequence(overwrite: false);
                break;
            case Keys.OemPeriod:
                AddCurrentCutToSequence(overwrite: true);
                break;
            case Keys.V:
                SetTool(TimelineTool.Select);
                break;
            case Keys.B:
                SetTool(TimelineTool.Ripple);
                break;
            case Keys.N:
                SetTool(TimelineTool.Rolling);
                break;
            case Keys.Y:
                SetTool(TimelineTool.Slip);
                break;
            case Keys.C:
                SetTool(TimelineTool.Razor);
                break;
            case Keys.S:
                ToggleSnapping();
                break;
            case Keys.M:
                AddMarkerAtPlayhead();
                break;
            case Keys.Left or Keys.Right:
                var frames = e.Shift ? 5d : 1d;
                SkipSeconds((e.KeyCode == Keys.Right ? 1 : -1) * frames / EditorTime.DefaultFps);
                break;
            case Keys.Home:
                SeekSource(0, refreshPreview: true);
                break;
            case Keys.End:
                SeekSource(_videoDuration, refreshPreview: true);
                break;
            case Keys.Delete:
                if (_markerList.Focused)
                    RemoveSelectedMarker();
                else
                    RippleDeleteSelected();
                break;
            default:
                return;
        }

        e.SuppressKeyPress = true;
    }

    private void NudgeNearestTrimHandle(double deltaSeconds)
    {
        if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _sequence.Segments.Count)
        {
            var segment = _sequence.Segments[_selectedSegmentIndex];
            var playhead = GetSourcePlayhead();
            var adjustStart = Math.Abs(playhead - segment.StartSec) <= Math.Abs(playhead - segment.EndSec);
            var newStart = adjustStart ? Math.Clamp(segment.StartSec + deltaSeconds, 0, segment.EndSec - 0.05) : segment.StartSec;
            var newEnd = adjustStart ? segment.EndSec : Math.Max(segment.StartSec + 0.05, segment.EndSec + deltaSeconds);
            SelectSegment(_sequence.TrimEdge(_selectedSegmentIndex, newStart, newEnd, _tool));
            SetStatus(adjustStart ? $"Nudged IN to {EditorTime.Format(newStart)}." : $"Nudged OUT to {EditorTime.Format(newEnd)}.");
            return;
        }

        var current = GetSourcePlayhead();
        var adjustSourceStart = Math.Abs(current - (double)_startBox.Value) <= Math.Abs(current - (double)_endBox.Value);
        _updatingTrimRange = true;
        try
        {
            if (adjustSourceStart)
                _startBox.Value = Math.Clamp(_startBox.Value + (decimal)deltaSeconds, _startBox.Minimum, Math.Max(_startBox.Minimum, _endBox.Value - 0.05M));
            else
                _endBox.Value = Math.Clamp(_endBox.Value + (decimal)deltaSeconds, Math.Min(_endBox.Maximum, _startBox.Value + 0.05M), _endBox.Maximum);
        }
        finally
        {
            _updatingTrimRange = false;
        }

        UpdateTrimTimelineUi();
        SetStatus(adjustSourceStart ? "Nudged the source IN point." : "Nudged the source OUT point.");
    }

    private void ShowShortcutsDialog()
    {
        MessageBox.Show(this,
            """
            Playback:      Space play/pause   •   J/K/L shuttle   •   ←/→ frame step (Shift = 5)
            Marking:       I mark IN   •   O mark OUT   •   M add marker
            Editing:       , insert   •   . overwrite   •   Ctrl+K split   •   Del ripple delete
            Tools:         V select   •   B ripple   •   N roll   •   Y slip   •   C razor   •   S snapping
            Trim nudge:    Ctrl+←/→ (Shift = 5 frames)
            History:       Ctrl+Z undo   •   Ctrl+Y / Ctrl+Shift+Z redo
            View:          Ctrl+= / Ctrl+- zoom   •   Alt+wheel zoom over a timeline
            Project:       Ctrl+I import clips   •   Ctrl+E export timeline
            """,
            "VELO Studio — keyboard shortcuts",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ================================================================
    // Render / export operations
    // ================================================================

    private bool TryGetSingleSelectedFile(out string input, out string error)
    {
        input = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(_selectedFile))
        {
            error = "Select exactly one clip in the media bin first.";
            return false;
        }

        if (!File.Exists(_selectedFile))
        {
            error = "The selected input file is missing.";
            return false;
        }

        input = _selectedFile;
        return true;
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

    private void PickOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where edited videos are written",
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
            SetStatus(error);
            return;
        }

        var start = (double)_startBox.Value;
        var end = (double)_endBox.Value;
        if (end <= start)
        {
            SetStatus("OUT must be after IN.");
            return;
        }

        var outputPath = BuildOutputPath(input, "trimmed");
        var args = $"-ss {FfmpegService.Invariant(start)} -i {FfmpegService.Quote(input)} -t {FfmpegService.Invariant(end - start)} -c copy -movflags +faststart -y {FfmpegService.Quote(outputPath)}";
        await RunFfmpegOperationAsync(args, $"Trim saved: {Path.GetFileName(outputPath)}", outputPath);
    }

    private async Task RunCropAsync()
    {
        if (!TryGetSingleSelectedFile(out var input, out var error))
        {
            SetStatus(error);
            return;
        }

        var crop = GetCropRectangle();
        if (crop.Width < 2 || crop.Height < 2)
        {
            SetStatus("Choose a valid crop area first.");
            return;
        }

        var outputPath = BuildOutputPath(input, "cropped", forceMp4: true);
        var args = $"-i {FfmpegService.Quote(input)} -vf \"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}\" -c:v libx264 -preset fast -crf 18 -c:a copy -movflags +faststart -y {FfmpegService.Quote(outputPath)}";
        await RunFfmpegOperationAsync(args, $"Crop saved: {Path.GetFileName(outputPath)}", outputPath);
    }

    private async Task RunMergeAsync()
    {
        var files = _binList.SelectedItems.Cast<object>().Select(item => item.ToString()!).Where(File.Exists).ToList();
        if (files.Count < 2)
        {
            SetStatus("Select at least two clips in the media bin to merge.");
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
            var args = $"-f concat -safe 0 -i {FfmpegService.Quote(listFile)} -c copy -movflags +faststart -y {FfmpegService.Quote(outputPath)}";
            await RunFfmpegOperationAsync(args, $"Merge saved: {Path.GetFileName(outputPath)}", outputPath);
        }
        finally
        {
            try { File.Delete(listFile); } catch { }
        }
    }

    private async Task RunFfmpegOperationAsync(string args, string successMessage, string outputPath)
    {
        SetBusy(true, "Running FFmpeg…");
        try
        {
            await FfmpegService.RunAsync(args);
            Logger.Info($"Editor output created: {outputPath}");
            ImportFiles([outputPath]);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            Logger.Error("Editor FFmpeg operation failed", ex);
            SetStatus($"Operation failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "VELO Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? string.Empty);
        }
    }

    private async Task ExportSequenceAsync()
    {
        if (_sequence.Segments.Count == 0)
        {
            SetStatus("Add at least one cut to the timeline before exporting.");
            return;
        }

        var firstSegment = _sequence.Segments.OrderBy(s => s.SequenceStartSec).First();
        var outputPath = BuildOutputPath(firstSegment.SourceFile, "timeline", forceMp4: true);

        SetBusy(true, "Exporting timeline…");
        try
        {
            var exporter = new SequenceExporter(_sequence);
            var progress = new Progress<string>(message => SetStatus($"Exporting timeline — {message}"));
            await exporter.ExportAsync(outputPath, progress);

            Logger.Info($"Editor timeline export created: {outputPath}");
            ImportFiles([outputPath]);
            SetStatus($"Timeline exported: {Path.GetFileName(outputPath)}");
            MessageBox.Show(this, $"Timeline export complete.\n\nSaved to:\n{outputPath}", "VELO Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("Editor timeline export failed", ex);
            SetStatus($"Timeline export failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "VELO Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text ?? string.Empty);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _busy = busy;
        foreach (var item in _busySensitiveItems)
            item.Enabled = !busy;
        _undoMenuItem.Enabled = !busy && _sequence.CanUndo;
        _redoMenuItem.Enabled = !busy && _sequence.CanRedo;
        UseWaitCursor = busy;
        SetStatus(status);
    }

    private static void ReplaceImage(PictureBox box, Image? image)
    {
        var old = box.Image;
        box.Image = image;
        old?.Dispose();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? $"clip-{DateTime.Now:yyyyMMdd-HHmmss}" : cleaned;
    }
}
