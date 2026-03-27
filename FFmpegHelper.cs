namespace VeloUploader;

/// <summary>
/// Helper to locate and manage FFmpeg (portable bundled version or system PATH).
/// Checks for portable FFmpeg first, then falls back to system PATH.
/// </summary>
public static class FFmpegHelper
{
    // Portable FFmpeg directory relative to app root
    private static readonly string PortableFFmpegDir = 
        Path.Combine(AppContext.BaseDirectory, "ffmpeg-portable");
    
    private static readonly string PortableFFmpegExe = 
        Path.Combine(PortableFFmpegDir, "bin", "ffmpeg.exe");

    /// <summary>
    /// Get the full path to the FFmpeg executable.
    /// Returns path if available (portable or system), null if not found.
    /// </summary>
    public static string? GetFFmpegPath()
    {
        // Check portable version first
        if (File.Exists(PortableFFmpegExe))
        {
            Logger.Debug($"Using portable FFmpeg: {PortableFFmpegExe}");
            return PortableFFmpegExe;
        }

        // Fall back to system PATH
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
                    Logger.Debug($"Using system FFmpeg: {output}");
                    return output;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Check if FFmpeg is available (portable or system PATH).
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
    /// Get the portable FFmpeg directory path (where it should be extracted).
    /// </summary>
    public static string GetPortableFFmpegDir() => PortableFFmpegDir;

    /// <summary>
    /// Check if portable FFmpeg is installed.
    /// </summary>
    public static bool IsPortableFFmpegInstalled() => File.Exists(PortableFFmpegExe);

    /// <summary>
    /// Get download URL for portable FFmpeg (gyan.dev full build).
    /// </summary>
    public static string GetDownloadUrl() => 
        "https://github.com/GyanD/codexffmpeg/releases/download/8.1/ffmpeg-8.1-full_build.zip";

    /// <summary>
    /// Get helpful message for users about portable FFmpeg.
    /// </summary>
    public static string GetFFmpegNotFoundMessage() =>
        @"FFmpeg not found on this system.

Compression features require FFmpeg, which can be installed as a portable bundled package or via winget.

To use compression:
• Download the portable FFmpeg package from your VELO release
• Extract it to the app folder as 'ffmpeg-portable'
OR
• Run: winget install -e --id Gyan.FFmpeg

Compression will be disabled until FFmpeg is installed.";
}
