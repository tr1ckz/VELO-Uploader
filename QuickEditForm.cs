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
    private readonly ElementHost _playerHost;
    private readonly WpfControls.MediaElement _mediaElement;
    private readonly EditorPreviewBox _sourcePreview;
    private readonly PictureBox _outputPreview;
    private readonly TrackBar _timelineBar;
    private readonly Button _playPauseButton;
    private readonly Button _jumpBackButton;
    private readonly Button _jumpForwardButton;
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
    private double _requestedPreviewTime;
    private CancellationTokenSource? _previewCts;
    private readonly List<TimelineSegment> _sequenceSegments = [];

    private static readonly string[] SupportedExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm"];

    private sealed record VideoDetails(double Duration, int Width, int Height);
    private sealed record TimelineSegment(string SourceFile, double StartSec, double EndSec)
    {
        public double Duration => Math.Max(0, EndSec - StartSec);
        public override string ToString() => $"{Path.GetFileName(SourceFile)}  •  {FormatTime(StartSec)} → {FormatTime(EndSec)}  ({FormatTime(Duration)})";
    }

    public QuickEditForm(string defaultOutputFolder)
    {
        var outputFolder = Directory.Exists(defaultOutputFolder)
            ? defaultOutputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Text = "VELO Video Editor";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(1260, 780);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(12, 12, 15);
        ForeColor = Color.FromArgb(240, 240, 245);
        Font = new Font("Segoe UI", 9f);

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
            Text = "Premiere-style video editor",
            AutoSize = true,
            Location = new Point(20, 14),
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = Color.White,
        };
        Controls.Add(title);

        var hint = new Label
        {
            Text = "Import clips into the project bin, scrub a source frame, preview the program monitor, and assemble cuts on a proper timeline workspace.",
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
        leftPanel.Controls.Add(BuildSmallLabel("Import clips, reorder them, then mark ranges to send into the timeline.", 12, 34, 232));

        _filesList = new ListBox
        {
            Location = new Point(12, 62),
            Size = new Size(224, 510),
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

        leftPanel.Controls.Add(BuildButton("Load folder", 12, 584, 102, (_, _) => LoadExistingClipsFromFolder(outputFolder)));
        leftPanel.Controls.Add(BuildButton("Import...", 122, 584, 62, (_, _) => AddFiles()));
        leftPanel.Controls.Add(BuildButton("Remove", 192, 584, 56, (_, _) => RemoveSelected()));
        leftPanel.Controls.Add(BuildButton("Up", 12, 620, 52, (_, _) => MoveSelected(-1)));
        leftPanel.Controls.Add(BuildButton("Down", 72, 620, 62, (_, _) => MoveSelected(1)));

        var centerPanel = new Panel
        {
            Location = new Point(296, 92),
            Size = new Size(760, 680),
            BackColor = Color.FromArgb(18, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(centerPanel);

        centerPanel.Controls.Add(BuildSectionLabel("Source monitor", 14, 12));
        centerPanel.Controls.Add(BuildSectionLabel("Program monitor", 390, 12));
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

        _timelineBar = new TrackBar
        {
            Location = new Point(14, 304),
            Size = new Size(632, 42),
            Minimum = 0,
            Maximum = 1000,
            TickStyle = TickStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _timelineBar.Scroll += (_, _) => QueuePreviewRefreshFromSlider();
        centerPanel.Controls.Add(_timelineBar);

        _refreshPreviewButton = BuildButton("Refresh frame", 652, 302, 92, async (_, _) => await RefreshPreviewAsync(GetCurrentPreviewTime()));
        _refreshPreviewButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        centerPanel.Controls.Add(_refreshPreviewButton);

        _playPauseButton = BuildActionButton("Play", 14, 344, 76, (_, _) => TogglePlayback());
        centerPanel.Controls.Add(_playPauseButton);

        _jumpBackButton = BuildButton("« 5s", 98, 344, 58, (_, _) => SkipSeconds(-5));
        centerPanel.Controls.Add(_jumpBackButton);

        _jumpForwardButton = BuildButton("5s »", 164, 344, 58, (_, _) => SkipSeconds(5));
        centerPanel.Controls.Add(_jumpForwardButton);

        centerPanel.Controls.Add(BuildSectionLabel("Timeline", 14, 392));
        var timelineHint = BuildSmallLabel("Marked cuts appear here like edit blocks. Double-click a cut on the right to load it back into the source monitor.", 14, 414, 720);
        centerPanel.Controls.Add(timelineHint);

        _sequenceTimelineView = new SequenceTimelineView
        {
            Location = new Point(14, 446),
            Size = new Size(730, 188),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        _sequenceTimelineView.SegmentClicked += async index => await LoadSequenceSegmentAsync(index);
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
        y += 36;
        rightPanel.Controls.Add(BuildButton("Mark IN at playhead", 14, y, 124, (_, _) => SetTrimBoundary(true)));
        rightPanel.Controls.Add(BuildButton("Mark OUT at playhead", 150, y, 124, (_, _) => SetTrimBoundary(false)));
        y += 38;
        _trimButton = BuildActionButton("Render selected range", 14, y, 260, async (_, _) => await RunTrimAsync());
        rightPanel.Controls.Add(_trimButton);
        y += 38;

        _addCutButton = BuildButton("Insert marked range to timeline", 14, y, 260, (_, _) => AddCurrentCutToSequence());
        rightPanel.Controls.Add(_addCutButton);
        y += 50;

        rightPanel.Controls.Add(BuildSectionLabel("Crop", 14, y));
        y += 22;
        _enableCropBox = new CheckBox
        {
            Text = "Enable visual crop",
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
        _cropButton = BuildActionButton("Render cropped clip", 14, y, 260, async (_, _) => await RunCropAsync());
        rightPanel.Controls.Add(_cropButton);
        y += 50;

        rightPanel.Controls.Add(BuildSectionLabel("Timeline / sequence", 14, y));
        y += 22;
        _sequenceHintLabel = BuildSmallLabel("Queue cuts here, reorder them, then export one merged result. Double-click any cut to load it back into the monitors.", 14, y, 260);
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
        _sequenceList.SelectedIndexChanged += (_, _) => _sequenceTimelineView.SetSelectedIndex(_sequenceList.SelectedIndex);
        _sequenceList.DoubleClick += async (_, _) => await LoadSequenceSegmentAsync(_sequenceList.SelectedIndex);
        rightPanel.Controls.Add(_sequenceList);
        y += 128;

        rightPanel.Controls.Add(BuildButton("Remove", 14, y, 70, (_, _) => RemoveSelectedSequenceSegment()));
        rightPanel.Controls.Add(BuildButton("Up", 92, y, 42, (_, _) => MoveSelectedSequenceSegment(-1)));
        rightPanel.Controls.Add(BuildButton("Down", 142, y, 52, (_, _) => MoveSelectedSequenceSegment(1)));
        rightPanel.Controls.Add(BuildButton("Clear", 202, y, 72, (_, _) => ClearSequence()));
        y += 38;

        _sequenceSummaryLabel = BuildSmallLabel("Timeline empty — add a cut to start building your export.", 14, y, 260);
        rightPanel.Controls.Add(_sequenceSummaryLabel);
        y += 36;

        _exportSequenceButton = BuildActionButton("Export timeline", 14, y, 260, async (_, _) => await ExportSequenceAsync());
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
            Text = "Ready.",
            AutoSize = false,
            Size = new Size(1240, 36),
            Location = new Point(20, 726),
            ForeColor = Color.FromArgb(155, 155, 165),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        Controls.Add(_statusLabel);

        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 280 };
        _previewDebounceTimer.Tick += async (_, _) =>
        {
            _previewDebounceTimer.Stop();
            await RefreshPreviewAsync(_requestedPreviewTime);
        };

        _playerTimer = new System.Windows.Forms.Timer { Interval = 180 };
        _playerTimer.Tick += (_, _) => UpdatePlayerPositionFromPlayback();

        UpdateSequenceUi();
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

        foreach (var file in files.Where(File.Exists))
        {
            if (!_filesList.Items.Contains(file))
                _filesList.Items.Add(file);
        }

        _statusLabel.Text = $"Added {files.Length} dragged clip(s) to the editor.";
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

        foreach (var file in files)
        {
            if (!_filesList.Items.Contains(file))
                _filesList.Items.Add(file);
        }

        _statusLabel.Text = files.Count == 0
            ? "No videos were found in the watch folder yet."
            : $"Loaded {files.Count} recent clip(s) from the watch folder.";
    }

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*",
            Title = "Add clips to the video editor",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        foreach (var file in dialog.FileNames)
        {
            if (!_filesList.Items.Contains(file))
                _filesList.Items.Add(file);
        }

        _statusLabel.Text = $"Loaded {_filesList.Items.Count} clip(s).";
    }

    private void RemoveSelected()
    {
        var selected = _filesList.SelectedItems.Cast<object>().ToList();
        foreach (var item in selected)
            _filesList.Items.Remove(item);
        _statusLabel.Text = $"Loaded {_filesList.Items.Count} clip(s).";
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

        _sequenceSegments.Add(new TimelineSegment(input, start, end));
        UpdateSequenceUi(selectedIndex: _sequenceSegments.Count - 1);
        _statusLabel.Text = $"Added cut from {Path.GetFileName(input)} to the export timeline.";
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
            _sequenceSummaryLabel.Text = $"{_sequenceSegments.Count} cut(s) queued • {FormatTime(totalDuration)} total";
            _sequenceTimelineView.SetSegments(_sequenceSegments, safeIndex);
            if (safeIndex >= 0 && safeIndex < _sequenceList.Items.Count)
                _sequenceList.SelectedIndex = safeIndex;
        }
        else
        {
            _sequenceSummaryLabel.Text = "Timeline empty — add a cut to start building your export.";
            _sequenceTimelineView.SetSegments(Array.Empty<TimelineSegment>(), -1);
        }

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

    private sealed class SequenceTimelineView : Control
    {
        private readonly List<TimelineSegment> _segments = [];
        private readonly List<(Rectangle Rect, int Index)> _hitTargets = [];
        private int _selectedIndex = -1;
        private double _playheadSeconds;

        public event Action<int>? SegmentClicked;

        public SequenceTimelineView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(10, 10, 12);
            ForeColor = Color.FromArgb(240, 240, 245);
            Cursor = Cursors.Hand;
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = _hitTargets.FirstOrDefault(target => target.Rect.Contains(e.Location));
            if (hit.Rect != Rectangle.Empty)
            {
                _selectedIndex = hit.Index;
                Invalidate();
                SegmentClicked?.Invoke(hit.Index);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _hitTargets.Clear();

            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var borderPen = new Pen(Color.FromArgb(40, 40, 50));
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
            var timelineLeft = 56;
            var timelineWidth = Math.Max(120, Width - timelineLeft - 14);
            var rulerTop = 16;
            var videoRect = new Rectangle(timelineLeft, 38, timelineWidth, 58);
            var audioRect = new Rectangle(timelineLeft, 108, timelineWidth, 26);

            TextRenderer.DrawText(e.Graphics, "V1", Font, new Point(16, videoRect.Top + 18), Color.FromArgb(180, 180, 195));
            TextRenderer.DrawText(e.Graphics, "A1", Font, new Point(16, audioRect.Top + 5), Color.FromArgb(180, 180, 195));

            using var railBrush = new SolidBrush(Color.FromArgb(18, 18, 24));
            e.Graphics.FillRectangle(railBrush, videoRect);
            e.Graphics.FillRectangle(railBrush, audioRect);
            e.Graphics.DrawRectangle(borderPen, videoRect);
            e.Graphics.DrawRectangle(borderPen, audioRect);

            for (var mark = 0; mark <= 6; mark++)
            {
                var x = timelineLeft + (int)Math.Round(mark * (timelineWidth / 6d));
                using var gridPen = new Pen(Color.FromArgb(32, 32, 40));
                e.Graphics.DrawLine(gridPen, x, rulerTop + 12, x, audioRect.Bottom);
                var stamp = FormatTime(totalDuration * mark / 6d);
                TextRenderer.DrawText(e.Graphics, stamp, new Font("Segoe UI", 7f), new Point(Math.Max(0, x - 16), rulerTop), Color.FromArgb(120, 120, 135));
            }

            var cursorSeconds = 0d;
            for (var index = 0; index < _segments.Count; index++)
            {
                var segment = _segments[index];
                var startRatio = cursorSeconds / totalDuration;
                var endRatio = (cursorSeconds + segment.Duration) / totalDuration;
                var startX = timelineLeft + (int)Math.Round(startRatio * timelineWidth);
                var endX = timelineLeft + (int)Math.Round(endRatio * timelineWidth);
                var blockWidth = Math.Max(34, endX - startX);
                var rect = new Rectangle(startX, videoRect.Top + 6, Math.Min(blockWidth, videoRect.Right - startX - 1), videoRect.Height - 12);
                var audioBlock = new Rectangle(startX, audioRect.Top + 4, rect.Width, audioRect.Height - 8);

                var fill = index == _selectedIndex ? Color.FromArgb(124, 58, 237) : (index % 2 == 0 ? Color.FromArgb(37, 99, 235) : Color.FromArgb(8, 145, 178));
                using var blockBrush = new SolidBrush(fill);
                using var audioBrush = new SolidBrush(Color.FromArgb(Math.Max(0, fill.R - 20), Math.Max(0, fill.G - 20), Math.Max(0, fill.B - 20)));
                using var activePen = new Pen(index == _selectedIndex ? Color.FromArgb(221, 214, 254) : Color.FromArgb(120, 120, 150), index == _selectedIndex ? 2 : 1);

                e.Graphics.FillRectangle(blockBrush, rect);
                e.Graphics.FillRectangle(audioBrush, audioBlock);
                e.Graphics.DrawRectangle(activePen, rect);
                e.Graphics.DrawRectangle(activePen, audioBlock);

                var label = $"{index + 1}. {Path.GetFileNameWithoutExtension(segment.SourceFile)}";
                TextRenderer.DrawText(e.Graphics, label, Font, rect, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                _hitTargets.Add((rect, index));
                cursorSeconds += segment.Duration;
            }

            var selectedOffset = Math.Clamp(_playheadSeconds, 0, totalDuration);
            if (_selectedIndex >= 0 && _selectedIndex < _segments.Count)
            {
                var priorDuration = _segments.Take(_selectedIndex).Sum(segment => segment.Duration);
                var selectedSegment = _segments[_selectedIndex];
                var localOffset = Math.Clamp(_playheadSeconds - selectedSegment.StartSec, 0, selectedSegment.Duration);
                selectedOffset = Math.Clamp(priorDuration + localOffset, 0, totalDuration);
            }

            var playheadX = timelineLeft + (int)Math.Round((selectedOffset / totalDuration) * timelineWidth);
            using var playheadPen = new Pen(Color.FromArgb(248, 113, 113), 2);
            e.Graphics.DrawLine(playheadPen, playheadX, rulerTop + 10, playheadX, audioRect.Bottom + 8);
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
