using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace VeloUploader;

/// <summary>
/// Runs FFmpeg locally to compress a video before uploading.
/// Uses the same settings as the server: libx264, crf 23, slow preset, max 1080p, aac audio.
/// </summary>
public static class LocalCompressor
{
    private sealed class PresetOptions
    {
        public required string VideoCodecArgs { get; init; }
        public required string ScaleArgs { get; init; }
        public required string AudioArgs { get; init; }
    }

    // Tracks all active FFmpeg processes so they can be killed on app crash/exit
    private static readonly ConcurrentDictionary<int, Process> _activeProcesses = new();

    static LocalCompressor()
    {
        // Kill any tracked FFmpeg processes when the process exits (normal or crash)
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    /// <summary>
    /// Kill all active FFmpeg processes. Called on app exit or crash.
    /// </summary>
    public static void KillAll()
    {
        foreach (var kvp in _activeProcesses)
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(entireProcessTree: true);
                    Logger.Info($"Killed orphaned FFmpeg process {kvp.Key}");
                }
            }
            catch { }
        }
        _activeProcesses.Clear();
    }

    /// <summary>
    /// Check if ffmpeg is available on this system (portable or system PATH).
    /// </summary>
    public static bool IsAvailable() => FFmpegHelper.IsFFmpegAvailable();

    /// <summary>
    /// Check if GPU acceleration is available (NVIDIA NVENC).
    /// </summary>
    public static bool IsGPUAvailable()
    {
        try
        {
            var ffmpegPath = FFmpegHelper.GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
                return false;

            // Check for NVIDIA NVENC codec support
            var psi = new ProcessStartInfo(ffmpegPath, "-codecs")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            // Look for NVIDIA NVENC codecs (h264_nvenc for H.264, hevc_nvenc for H.265)
            var hasNVENC = output.Contains("h264_nvenc") || output.Contains("hevc_nvenc");
            if (hasNVENC)
            {
                Logger.Info("GPU acceleration available: NVIDIA NVENC detected");
                return true;
            }

            // Could also check for other GPUs here (AMD VCE, Intel QSV, etc.)
            // For now, just NVIDIA NVENC
            return false;
        }
        catch (Exception ex)
        {
            Logger.Debug($"GPU check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Compress a video file using FFmpeg. Returns the path to the compressed file, or null on failure.
    /// The compressed file is placed next to the original with a .velo-compressed.mp4 suffix.
    /// </summary>
    public static async Task<string?> CompressAsync(
        string inputPath,
        string preset,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? Path.GetTempPath();
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outputPath = Path.Combine(dir, $"{baseName}.velo-compressed.mp4");
        var options = GetPresetOptions(preset);

        // Delete leftover from previous attempt
        if (File.Exists(outputPath))
        {
            try { File.Delete(outputPath); } catch { }
        }

        var args = string.Join(" ",
            $"-i \"{inputPath}\"",
            options.VideoCodecArgs,
            options.ScaleArgs,
            "-pix_fmt yuv420p",
            "-movflags +faststart",
            options.AudioArgs,
            "-y",
            $"\"{outputPath}\""
        );

        Logger.Info($"Compressing locally: {Path.GetFileName(inputPath)} ({preset}){(IsGPU(preset) ? " [GPU ACCELERATED]" : "")}");
        Logger.Debug($"ffmpeg {args}");

        var ffmpegPath = FFmpegHelper.GetFFmpegPath();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            Logger.Error("FFmpeg not found - compression unavailable");
            return null;
        }

        var psi = new ProcessStartInfo(ffmpegPath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        double totalDuration = 0;
        var tcs = new TaskCompletionSource<int>();

        proc.Exited += (_, _) =>
        {
            try { tcs.TrySetResult(proc.ExitCode); }
            catch { tcs.TrySetResult(-1); }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            // Parse total duration
            if (totalDuration == 0)
            {
                var durMatch = Regex.Match(e.Data, @"Duration:\s*(\d+):(\d+):(\d+)\.(\d+)");
                if (durMatch.Success)
                {
                    totalDuration = int.Parse(durMatch.Groups[1].Value) * 3600
                                  + int.Parse(durMatch.Groups[2].Value) * 60
                                  + int.Parse(durMatch.Groups[3].Value)
                                  + int.Parse(durMatch.Groups[4].Value) / 100.0;
                }
            }

            // Parse progress
            var timeMatch = Regex.Match(e.Data, @"time=\s*(\d+):(\d+):(\d+)\.(\d+)");
            if (timeMatch.Success && totalDuration > 0)
            {
                var current = int.Parse(timeMatch.Groups[1].Value) * 3600
                            + int.Parse(timeMatch.Groups[2].Value) * 60
                            + int.Parse(timeMatch.Groups[3].Value)
                            + int.Parse(timeMatch.Groups[4].Value) / 100.0;
                var pct = Math.Min(99, current / totalDuration * 100);
                progress?.Report(pct);
            }
        };

        try
        {
            proc.Start();
            proc.BeginErrorReadLine();

            // Register in the global process table so it gets killed if the app crashes
            _activeProcesses[proc.Id] = proc;

            // Wait for exit or cancellation
            using var reg = ct.Register(() =>
            {
                try { proc.Kill(true); } catch { }
                tcs.TrySetCanceled();
            });

            var exitCode = await tcs.Task;

            // Deregister from active processes
            _activeProcesses.TryRemove(proc.Id, out _);

            if (exitCode != 0)
            {
                Logger.Error($"FFmpeg exited with code {exitCode}");
                TryDelete(outputPath);
                return null;
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                Logger.Error("FFmpeg produced no output file");
                TryDelete(outputPath);
                return null;
            }

            var originalSize = new FileInfo(inputPath).Length;
            var compressedSize = new FileInfo(outputPath).Length;
            var ratio = (1.0 - (double)compressedSize / originalSize) * 100;
            var encoder = IsGPU(preset) ? "GPU" : "CPU";

            Logger.Info($"Compression complete ({encoder}): {originalSize / 1024 / 1024}MB → {compressedSize / 1024 / 1024}MB ({ratio:F1}% smaller)");
            progress?.Report(100);

            return outputPath;
        }
        catch (OperationCanceledException)
        {
            _activeProcesses.TryRemove(proc.Id, out _);
            Logger.Warn("Compression cancelled");
            TryDelete(outputPath);
            return null;
        }
        catch (Exception ex)
        {
            _activeProcesses.TryRemove(proc.Id, out _);
            Logger.Error("FFmpeg compression failed", ex);
            TryDelete(outputPath);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static PresetOptions GetPresetOptions(string preset)
    {
        return preset switch
        {
            CompressionPreset.Quality => new PresetOptions
            {
                VideoCodecArgs = "-c:v libx264 -crf 20 -preset slow",
                ScaleArgs = "-vf \"scale='min(2560,iw)':'min(1440,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 224k",
            },
            CompressionPreset.Aggressive => new PresetOptions
            {
                VideoCodecArgs = "-c:v libx264 -crf 28 -preset medium",
                ScaleArgs = "-vf \"scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 160k",
            },
            CompressionPreset.Discord => new PresetOptions
            {
                VideoCodecArgs = "-c:v libx264 -crf 28 -preset fast",
                ScaleArgs = "-vf \"scale='min(1280,iw)':'min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 128k",
            },
            // GPU-accelerated presets (NVIDIA NVENC).
            // NVENC does not support -crf; use -rc:v vbr -cq for constant-quality VBR mode.
            // -b:v 0 lets the encoder use CQ without a bitrate ceiling constraint.
            CompressionPreset.BalancedGPU => new PresetOptions
            {
                VideoCodecArgs = "-c:v h264_nvenc -preset fast -tune hq -rc:v vbr -cq 26 -b:v 0 -maxrate 8M -bufsize 16M",
                ScaleArgs = "-vf \"scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 192k",
            },
            CompressionPreset.QualityGPU => new PresetOptions
            {
                VideoCodecArgs = "-c:v h264_nvenc -preset slow -tune hq -rc:v vbr -cq 21 -b:v 0 -maxrate 20M -bufsize 40M",
                ScaleArgs = "-vf \"scale='min(2560,iw)':'min(1440,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 224k",
            },
            CompressionPreset.Discord_GPU => new PresetOptions
            {
                VideoCodecArgs = "-c:v h264_nvenc -preset fast -rc:v vbr -cq 28 -b:v 0 -maxrate 4M -bufsize 8M",
                ScaleArgs = "-vf \"scale='min(1280,iw)':'min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 128k",
            },
            _ => new PresetOptions
            {
                VideoCodecArgs = "-c:v libx264 -crf 23 -preset slow",
                ScaleArgs = "-vf \"scale='min(1920,iw)':'min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2\"",
                AudioArgs = "-c:a aac -b:a 192k",
            },
        };
    }

    private static bool IsGPU(string preset)
    {
        return preset.Contains("GPU") || preset.Equals(CompressionPreset.BalancedGPU) || 
               preset.Equals(CompressionPreset.QualityGPU) || preset.Equals(CompressionPreset.Discord_GPU);
    }
}
