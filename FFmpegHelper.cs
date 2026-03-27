namespace VeloUploader;

/// <summary>
/// Helper to locate FFmpeg in system PATH and offer installation via winget.
/// </summary>
public static class FFmpegHelper
{
    /// <summary>
    /// Get the full path to the FFmpeg executable from system PATH.
    /// Returns null if not found.
    /// </summary>
    public static string? GetFFmpegPath()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where", "ffmpeg")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(3000);
                if (!string.IsNullOrEmpty(output) && File.Exists(output))
                {
                    Logger.Debug($"Found FFmpeg: {output}");
                    return output;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Check if FFmpeg is available in system PATH.
    /// </summary>
    public static bool IsFFmpegAvailable()
    {
        var path = GetFFmpegPath();
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(path, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Install FFmpeg via winget in the background.
    /// Returns true if installation started successfully.
    /// </summary>
    public static bool TryInstallFFmpeg()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("winget", "install -e --id Gyan.FFmpeg")
            {
                UseShellExecute = false,
                CreateNoWindow = false, // Show the command window for visibility
                RedirectStandardOutput = false,
                RedirectStandardError = false,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start FFmpeg installation", ex);
            return false;
        }
    }

    /// <summary>
    /// Get helpful message for users about FFmpeg installation.
    /// </summary>
    public static string GetFFmpegNotFoundMessage() =>
        @"FFmpeg not found on this system.

Compression features require FFmpeg, which can be installed instantly via Windows Package Manager (winget).

Would you like to install FFmpeg now? The installation takes about 1-2 minutes and will download the full build with all codecs and hardware acceleration support.

You can also install manually later:
winget install -e --id Gyan.FFmpeg

Compression will be disabled until FFmpeg is installed.";
}
