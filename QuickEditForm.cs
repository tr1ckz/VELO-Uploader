namespace VeloUploader;

using System.Diagnostics;
using System.Globalization;
using System.Text;

public sealed class QuickEditForm : Form
{
    private readonly ListBox _filesList;
    private readonly NumericUpDown _startBox;
    private readonly NumericUpDown _endBox;
    private readonly TextBox _outputNameBox;
    private readonly TextBox _outputFolderBox;
    private readonly Label _statusLabel;
    private readonly Button _trimButton;
    private readonly Button _mergeButton;

    public QuickEditForm(string defaultOutputFolder)
    {
        var outputFolder = Directory.Exists(defaultOutputFolder)
            ? defaultOutputFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Text = "VELO Quick Editor";
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(760, 520);
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
            Text = "Quick local trim / merge",
            AutoSize = true,
            Location = new Point(20, 16),
            Font = new Font("Segoe UI Semibold", 12f),
            ForeColor = Color.White,
        };
        Controls.Add(title);

        var hint = new Label
        {
            Text = "Create a new video locally and drop it straight into your watch folder so the uploader can queue it.",
            AutoSize = false,
            Size = new Size(700, 36),
            Location = new Point(20, 42),
            ForeColor = Color.FromArgb(155, 155, 165),
        };
        Controls.Add(hint);

        _filesList = new ListBox
        {
            Location = new Point(20, 92),
            Size = new Size(420, 280),
            HorizontalScrollbar = true,
            SelectionMode = SelectionMode.MultiExtended,
            BackColor = Color.FromArgb(18, 18, 22),
            ForeColor = Color.FromArgb(240, 240, 245),
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_filesList);

        var addButton = BuildButton("Add clips...", 20, 384, 110, (_, _) => AddFiles());
        Controls.Add(addButton);
        Controls.Add(BuildButton("Remove", 140, 384, 90, (_, _) => RemoveSelected()));
        Controls.Add(BuildButton("Move up", 240, 384, 90, (_, _) => MoveSelected(-1)));
        Controls.Add(BuildButton("Move down", 340, 384, 100, (_, _) => MoveSelected(1)));

        var rightPanel = new Panel
        {
            Location = new Point(460, 92),
            Size = new Size(280, 360),
            BackColor = Color.FromArgb(18, 18, 22),
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(rightPanel);

        var outputNameLabel = BuildLabel("Output name", 14, 14);
        rightPanel.Controls.Add(outputNameLabel);
        _outputNameBox = BuildTextBox("Leave blank to auto-name", 14, 34, 248);
        rightPanel.Controls.Add(_outputNameBox);

        var outputFolderLabel = BuildLabel("Output folder", 14, 72);
        rightPanel.Controls.Add(outputFolderLabel);
        _outputFolderBox = BuildTextBox(outputFolder, 14, 92, 180);
        rightPanel.Controls.Add(_outputFolderBox);
        rightPanel.Controls.Add(BuildButton("Browse", 200, 90, 62, (_, _) => PickOutputFolder()));

        var trimLabel = BuildLabel("Trim selected clip", 14, 138);
        trimLabel.Font = new Font(trimLabel.Font, FontStyle.Bold);
        rightPanel.Controls.Add(trimLabel);

        rightPanel.Controls.Add(BuildLabel("Start (sec)", 14, 170));
        _startBox = BuildNumeric(0, 0, 86400, 14, 190, 116);
        rightPanel.Controls.Add(_startBox);

        rightPanel.Controls.Add(BuildLabel("End (sec)", 146, 170));
        _endBox = BuildNumeric(30, 0, 86400, 146, 190, 116);
        rightPanel.Controls.Add(_endBox);

        _trimButton = BuildButton("Create trimmed clip", 14, 230, 248, async (_, _) => await RunTrimAsync());
        rightPanel.Controls.Add(_trimButton);

        var mergeLabel = BuildLabel("Merge selected clips", 14, 282);
        mergeLabel.Font = new Font(mergeLabel.Font, FontStyle.Bold);
        rightPanel.Controls.Add(mergeLabel);

        _mergeButton = BuildButton("Merge selection", 14, 308, 248, async (_, _) => await RunMergeAsync());
        rightPanel.Controls.Add(_mergeButton);

        _statusLabel = new Label
        {
            Text = "Ready.",
            AutoSize = false,
            Size = new Size(720, 42),
            Location = new Point(20, 462),
            ForeColor = Color.FromArgb(155, 155, 165),
        };
        Controls.Add(_statusLabel);
    }

    private static Label BuildLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        ForeColor = Color.FromArgb(200, 200, 210),
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

    private static NumericUpDown BuildNumeric(decimal value, decimal min, decimal max, int x, int y, int width) => new()
    {
        Value = value,
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

    private void AddFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*",
            Title = "Add clips to quick editor",
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
        if (_filesList.SelectedItems.Count != 1)
        {
            MessageBox.Show(this, "Select exactly one clip to trim.", "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var input = _filesList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
        {
            MessageBox.Show(this, "The selected input file is missing.", "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var start = (double)_startBox.Value;
        var end = (double)_endBox.Value;
        if (end <= start)
        {
            MessageBox.Show(this, "End time must be greater than start time.", "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outputPath = BuildOutputPath(input, "trimmed");
        var duration = end - start;
        var args = $"-ss {start.ToString(CultureInfo.InvariantCulture)} -i {Quote(input)} -t {duration.ToString(CultureInfo.InvariantCulture)} -c copy -movflags +faststart -y {Quote(outputPath)}";
        await RunFfmpegAsync(args, $"Trim created: {Path.GetFileName(outputPath)}", outputPath);
    }

    private async Task RunMergeAsync()
    {
        var files = _filesList.SelectedItems.Cast<string>().ToList();
        if (files.Count < 2)
        {
            MessageBox.Show(this, "Select at least two clips to merge.", "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private async Task RunFfmpegAsync(string args, string successMessage, string outputPath)
    {
        var ffmpegPath = FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
        _trimButton.Enabled = false;
        _mergeButton.Enabled = false;
        _statusLabel.Text = "Running FFmpeg…";

        try
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

            Logger.Info($"Quick editor output created: {outputPath}");
            _statusLabel.Text = successMessage;
            MessageBox.Show(this, $"{successMessage}\n\nSaved to:\n{outputPath}", "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error("Quick editor operation failed", ex);
            _statusLabel.Text = $"Editor task failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "VELO Quick Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _trimButton.Enabled = true;
            _mergeButton.Enabled = true;
        }
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

    private static string Quote(string value) => $"\"{value}\"";

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? $"clip-{DateTime.Now:yyyyMMdd-HHmmss}" : cleaned;
    }
}
