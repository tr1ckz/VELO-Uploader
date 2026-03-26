namespace VeloUploader;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _serverUrlBox;
    private readonly TextBox _apiTokenBox;
    private readonly TextBox _watchFolderBox;
    private readonly CheckBox _watchSubfoldersBox;
    private readonly CheckBox _notificationsBox;
    private readonly Button _saveButton;
    private readonly Button _browseButton;
    private readonly Button _testButton;
    private readonly Label _statusLabel;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "VELO Uploader — Settings";
        Size = new Size(520, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 24, 27); // zinc-900
        ForeColor = Color.FromArgb(228, 228, 231); // zinc-200

        var y = 20;

        AddLabel("VELO Server URL", 20, y);
        y += 22;
        _serverUrlBox = AddTextBox(settings.ServerUrl, "https://clips.example.com", 20, y, 460);
        y += 40;

        AddLabel("API Token", 20, y);
        y += 22;
        _apiTokenBox = AddTextBox(settings.ApiToken, "velo_...", 20, y, 360);
        _apiTokenBox.UseSystemPasswordChar = true;

        _testButton = new Button
        {
            Text = "Test",
            Location = new Point(390, y - 1),
            Size = new Size(90, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(109, 40, 217), // violet-700
            ForeColor = Color.White,
        };
        _testButton.Click += async (_, _) => await TestConnection();
        Controls.Add(_testButton);
        y += 40;

        AddLabel("Watch Folder (NVIDIA Replay save location)", 20, y);
        y += 22;
        _watchFolderBox = AddTextBox(settings.WatchFolder, @"C:\Users\you\Videos", 20, y, 370);

        _browseButton = new Button
        {
            Text = "Browse...",
            Location = new Point(400, y - 1),
            Size = new Size(80, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(63, 63, 70), // zinc-700
            ForeColor = Color.White,
        };
        _browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = _watchFolderBox.Text,
                Description = "Select NVIDIA Instant Replay save folder"
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                _watchFolderBox.Text = dialog.SelectedPath;
        };
        Controls.Add(_browseButton);
        y += 40;

        _watchSubfoldersBox = new CheckBox
        {
            Text = "Watch subfolders (recommended — ShadowPlay creates game-named subfolders)",
            Checked = settings.WatchSubfolders,
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(161, 161, 170), // zinc-400
        };
        Controls.Add(_watchSubfoldersBox);
        y += 30;

        _notificationsBox = new CheckBox
        {
            Text = "Show desktop notifications",
            Checked = settings.ShowNotifications,
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(161, 161, 170),
        };
        Controls.Add(_notificationsBox);
        y += 40;

        _statusLabel = new Label
        {
            Text = "",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(74, 222, 128), // green-400
        };
        Controls.Add(_statusLabel);

        _saveButton = new Button
        {
            Text = "Save && Start Watching",
            Location = new Point(320, y - 5),
            Size = new Size(160, 35),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(124, 58, 237), // violet-600
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
        };
        _saveButton.Click += (_, _) => SaveSettings();
        Controls.Add(_saveButton);
    }

    private Label AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(161, 161, 170),
            Font = new Font(Font.FontFamily, 8.5f),
        };
        Controls.Add(label);
        return label;
    }

    private TextBox AddTextBox(string value, string placeholder, int x, int y, int width)
    {
        var box = new TextBox
        {
            Text = value,
            PlaceholderText = placeholder,
            Location = new Point(x, y),
            Size = new Size(width, 26),
            BackColor = Color.FromArgb(9, 9, 11), // zinc-950
            ForeColor = Color.FromArgb(228, 228, 231),
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(box);
        return box;
    }

    private void SaveSettings()
    {
        var url = _serverUrlBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            ShowStatus("Server URL is required", true);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            ShowStatus("Invalid server URL", true);
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiTokenBox.Text))
        {
            ShowStatus("API token is required", true);
            return;
        }

        _settings.ServerUrl = url;
        _settings.ApiToken = _apiTokenBox.Text.Trim();
        _settings.WatchFolder = _watchFolderBox.Text.Trim();
        _settings.WatchSubfolders = _watchSubfoldersBox.Checked;
        _settings.ShowNotifications = _notificationsBox.Checked;
        _settings.Save();

        ShowStatus("Settings saved!", false);
        Task.Delay(1000).ContinueWith(_ => { if (!IsDisposed) Invoke(Close); });
    }

    private async Task TestConnection()
    {
        var url = _serverUrlBox.Text.Trim().TrimEnd('/') + "/api/videos";
        var token = _apiTokenBox.Text.Trim();

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
        {
            ShowStatus("Fill in URL and token first", true);
            return;
        }

        _testButton.Enabled = false;
        ShowStatus("Testing...", false);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp = await http.GetAsync(url);

            ShowStatus(resp.IsSuccessStatusCode
                ? "Connected successfully!"
                : $"Server returned {(int)resp.StatusCode}", !resp.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            ShowStatus($"Connection failed: {ex.Message}", true);
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        _statusLabel.ForeColor = isError
            ? Color.FromArgb(248, 113, 113) // red-400
            : Color.FromArgb(74, 222, 128); // green-400
        _statusLabel.Text = message;
    }
}
