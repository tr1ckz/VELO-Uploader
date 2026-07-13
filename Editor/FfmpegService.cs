namespace VeloUploader.Editor;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// All FFmpeg/FFprobe process work for the editor: probing, frame extraction,
/// waveform rendering, and export runs. Centralizes argument quoting so paths
/// with quotes or spaces can't break a command line.
/// </summary>
internal static class FfmpegService
{
    public static string FfmpegPath => FFmpegHelper.GetFFmpegPath() ?? "ffmpeg";
    public static string FfprobePath => FFmpegHelper.GetFFprobePath() ?? "ffprobe";

    /// <summary>Quotes a process argument for Windows command-line rules (escapes embedded quotes and trailing backslashes).</summary>
    public static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', backslashes).Append(ch);
            }

            backslashes = 0;
        }

        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static string Invariant(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static async Task<VideoDetails> ProbeAsync(string input, CancellationToken ct = default)
    {
        var args = $"-v error -show_entries stream=codec_type,width,height,avg_frame_rate:format=duration -of json {Quote(input)}";
        var (exitCode, stdout, stderr) = await RunProcessAsync(FfprobePath, args, ct);
        if (exitCode != 0)
            throw new InvalidOperationException(TailOf(stderr, "ffprobe failed.", 400));

        using var doc = JsonDocument.Parse(stdout);
        var duration = 0d;
        if (doc.RootElement.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationProp) &&
            double.TryParse(durationProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDuration))
        {
            duration = parsedDuration;
        }

        int width = 0, height = 0;
        var fps = EditorTime.DefaultFps;
        var hasAudio = false;
        if (doc.RootElement.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = stream.TryGetProperty("codec_type", out var typeProp) ? typeProp.GetString() : null;
                if (codecType == "audio")
                {
                    hasAudio = true;
                }
                else if (codecType == "video" && width == 0)
                {
                    width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    if (stream.TryGetProperty("avg_frame_rate", out var rateProp) && rateProp.GetString() is { } rate)
                        fps = ParseFrameRate(rate);
                }
            }
        }

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("ffprobe did not return video dimensions.");

        return new VideoDetails(duration, width, height, fps, hasAudio);
    }

    /// <summary>Extracts a single frame as a Bitmap, optionally cropped, scaled down to a preview-friendly width.</summary>
    public static async Task<Image> ExtractFrameAsync(string filePath, double time, CancellationToken ct, Rectangle? crop = null, int maxWidth = 960)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"velo-preview-{Guid.NewGuid():N}.jpg");
        var filter = crop is { Width: > 0, Height: > 0 }
            ? $"-vf \"crop={crop.Value.Width}:{crop.Value.Height}:{crop.Value.X}:{crop.Value.Y},scale={maxWidth}:-1\""
            : $"-vf \"scale={maxWidth}:-1\"";
        var args = $"-ss {Invariant(Math.Max(0, time))} -i {Quote(filePath)} -frames:v 1 {filter} -q:v 2 -y {Quote(tempFile)}";

        try
        {
            var (exitCode, _, stderr) = await RunProcessAsync(FfmpegPath, args, ct);
            if (exitCode != 0 || !File.Exists(tempFile))
                throw new InvalidOperationException(TailOf(stderr, "FFmpeg could not render the preview frame.", 400));

            await using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var temp = Image.FromStream(fs);
            return new Bitmap(temp);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    /// <summary>Renders the clip's mono waveform to an image; falls back to a synthetic pattern when audio is missing.</summary>
    public static async Task<Image> ExtractWaveformAsync(string filePath, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"velo-wave-{Guid.NewGuid():N}.png");
        var color = $"0x{EditorTheme.Waveform.R:X2}{EditorTheme.Waveform.G:X2}{EditorTheme.Waveform.B:X2}";
        var args = $"-i {Quote(filePath)} -filter_complex \"aformat=channel_layouts=mono,showwavespic=s=2400x180:colors={color}\" -frames:v 1 -y {Quote(tempFile)}";

        try
        {
            var (exitCode, _, _) = await RunProcessAsync(FfmpegPath, args, ct);
            if (exitCode == 0 && File.Exists(tempFile))
            {
                await using var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var temp = Image.FromStream(fs);
                return new Bitmap(temp);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
        finally
        {
            TryDelete(tempFile);
        }

        return BuildFallbackWaveform();
    }

    /// <summary>Runs an ffmpeg command to completion; throws with the stderr tail on failure.</summary>
    public static async Task RunAsync(string args, CancellationToken ct = default)
    {
        var (exitCode, _, stderr) = await RunProcessAsync(FfmpegPath, args, ct);
        if (exitCode != 0)
            throw new InvalidOperationException(TailOf(stderr, "FFmpeg failed.", 800));
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{Path.GetFileName(fileName)} could not be started.");
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static double ParseFrameRate(string rate)
    {
        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0 && numerator > 0)
        {
            var fps = numerator / denominator;
            if (fps is > 1 and <= 480)
                return fps;
        }

        return EditorTime.DefaultFps;
    }

    private static Image BuildFallbackWaveform()
    {
        var fallback = new Bitmap(2400, 180);
        using var g = Graphics.FromImage(fallback);
        g.Clear(Color.Black);
        using var pen = new Pen(EditorTheme.Waveform, 1.4f);
        var mid = fallback.Height / 2;
        for (var x = 0; x < fallback.Width; x += 3)
        {
            var height = 10 + (int)(Math.Abs(Math.Sin(x / 32d)) * 62);
            g.DrawLine(pen, x, mid - height, x, mid + height);
        }

        return fallback;
    }

    private static string TailOf(string text, string fallback, int maxLength) =>
        string.IsNullOrWhiteSpace(text) ? fallback : text[^Math.Min(text.Length, maxLength)..];

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
