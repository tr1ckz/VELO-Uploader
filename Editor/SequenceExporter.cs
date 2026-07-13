namespace VeloUploader.Editor;

/// <summary>
/// Renders the sequence to a single video. Each interval between clip
/// boundaries is re-encoded (frame-accurate, uniform codec/canvas/fps), V2
/// clips are composited over V1 with their position/scale/opacity applied
/// (keyframed values are sampled at the interval midpoint), gaps become black
/// with silence, and the intervals are concatenated losslessly.
/// </summary>
internal sealed class SequenceExporter
{
    /// <summary>Preview canvas width the inspector's position values are expressed in; export scales them to the output canvas.</summary>
    private const double InspectorCanvasWidth = 960d;

    private readonly SequenceModel _sequence;
    private readonly Dictionary<string, VideoDetails> _probeCache = new(StringComparer.OrdinalIgnoreCase);

    public SequenceExporter(SequenceModel sequence)
    {
        _sequence = sequence;
    }

    public async Task ExportAsync(string outputPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var segments = _sequence.Segments;
        if (segments.Count == 0)
            throw new InvalidOperationException("The timeline is empty.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"velo-sequence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var first = segments.OrderBy(s => s.SequenceStartSec).First();
            var canvas = await ProbeAsync(first.SourceFile, ct);
            var width = Math.Max(2, canvas.Width / 2 * 2);
            var height = Math.Max(2, canvas.Height / 2 * 2);
            var fps = Math.Clamp(canvas.Fps, 10, 120);

            var boundaries = segments
                .SelectMany(s => new[] { s.SequenceStartSec, s.SequenceEndSec })
                .Append(0d)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            var partFiles = new List<string>();
            for (var index = 0; index < boundaries.Count - 1; index++)
            {
                ct.ThrowIfCancellationRequested();
                var intervalStart = boundaries[index];
                var intervalEnd = boundaries[index + 1];
                var duration = intervalEnd - intervalStart;
                if (duration <= 0.03)
                    continue;

                progress?.Report($"Rendering part {partFiles.Count + 1}…");
                var partFile = Path.Combine(tempDir, $"part-{index:D3}.mp4");
                var args = await BuildIntervalArgsAsync(intervalStart, duration, width, height, fps, partFile, ct);
                await FfmpegService.RunAsync(args, ct);
                partFiles.Add(partFile);
            }

            if (partFiles.Count == 0)
                throw new InvalidOperationException("Nothing to export — all timeline intervals were empty.");

            progress?.Report("Joining parts…");
            var listFile = Path.Combine(tempDir, "concat.txt");
            var listText = string.Join(Environment.NewLine, partFiles.Select(file => $"file '{file.Replace("'", "'\\''")}'"));
            await File.WriteAllTextAsync(listFile, listText + Environment.NewLine, ct);

            var concatArgs = $"-f concat -safe 0 -i {FfmpegService.Quote(listFile)} -c copy -movflags +faststart -y {FfmpegService.Quote(outputPath)}";
            await FfmpegService.RunAsync(concatArgs, ct);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private async Task<string> BuildIntervalArgsAsync(double intervalStart, double duration, int width, int height, double fps, string outputFile, CancellationToken ct)
    {
        var midpoint = intervalStart + (duration / 2d);
        var layers = _sequence.ActiveAt(midpoint)
            .OrderBy(s => s.SafeTrack)
            .ThenBy(s => s.SequenceStartSec)
            .GroupBy(s => s.SafeTrack)
            .Select(g => g.Last())
            .ToList();

        var args = new StringBuilder();
        var filter = new StringBuilder();
        var audioLabels = new List<string>();
        var inputIndex = 0;

        filter.Append($"color=c=black:s={width}x{height}:r={FfmpegService.Invariant(fps)}:d={FfmpegService.Invariant(duration)}[bg];");
        var currentVideo = "bg";

        foreach (var segment in layers)
        {
            var details = await ProbeAsync(segment.SourceFile, ct);
            var clipTime = segment.StartSec + Math.Max(0, intervalStart - segment.SequenceStartSec);
            args.Append($"-ss {FfmpegService.Invariant(clipTime)} -t {FfmpegService.Invariant(duration)} -i {FfmpegService.Quote(segment.SourceFile)} ");

            var transform = _sequence.EvaluateTransform(segment, midpoint);
            var chain = new StringBuilder($"[{inputIndex}:v]fps={FfmpegService.Invariant(fps)},scale={width}:{height}:force_original_aspect_ratio=decrease,setsar=1");
            if (Math.Abs(transform.Scale - 1f) > 0.001f)
            {
                var scale = FfmpegService.Invariant(Math.Clamp(transform.Scale, 0.1f, 4f));
                chain.Append($",scale=trunc(iw*{scale}/2)*2:trunc(ih*{scale}/2)*2");
            }

            if (transform.Opacity < 0.999f)
            {
                var opacity = FfmpegService.Invariant(Math.Clamp(transform.Opacity, 0f, 1f));
                chain.Append($",format=rgba,colorchannelmixer=aa={opacity}");
            }

            var layerLabel = $"l{inputIndex}";
            chain.Append($"[{layerLabel}];");
            filter.Append(chain);

            // Inspector positions are in preview-canvas pixels; rescale to the export canvas.
            var offsetX = (int)Math.Round(transform.PositionX * width / InspectorCanvasWidth);
            var offsetY = (int)Math.Round(transform.PositionY * width / InspectorCanvasWidth);
            var outLabel = $"v{inputIndex}";
            filter.Append($"[{currentVideo}][{layerLabel}]overlay=x=(W-w)/2+{offsetX}:y=(H-h)/2+{offsetY}[{outLabel}];");
            currentVideo = outLabel;

            if (details.HasAudio)
                audioLabels.Add($"{inputIndex}:a");
            inputIndex++;
        }

        string audioMap;
        if (audioLabels.Count == 0)
        {
            args.Append($"-f lavfi -t {FfmpegService.Invariant(duration)} -i anullsrc=r=48000:cl=stereo ");
            audioMap = $"{inputIndex}:a";
            inputIndex++;
        }
        else if (audioLabels.Count == 1)
        {
            audioMap = audioLabels[0];
        }
        else
        {
            filter.Append(string.Concat(audioLabels.Select(label => $"[{label}]")));
            filter.Append($"amix=inputs={audioLabels.Count}:duration=longest[aout];");
            audioMap = "[aout]";
        }

        var filterText = filter.ToString().TrimEnd(';');
        args.Append($"-filter_complex \"{filterText}\" ");
        args.Append($"-map \"[{currentVideo}]\" -map {(audioMap.StartsWith('[') ? $"\"{audioMap}\"" : audioMap)} ");
        args.Append($"-t {FfmpegService.Invariant(duration)} ");
        args.Append($"-c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -r {FfmpegService.Invariant(fps)} ");
        args.Append("-c:a aac -ar 48000 -ac 2 -movflags +faststart ");
        args.Append($"-y {FfmpegService.Quote(outputFile)}");
        return args.ToString();
    }

    private async Task<VideoDetails> ProbeAsync(string file, CancellationToken ct)
    {
        if (_probeCache.TryGetValue(file, out var cached))
            return cached;

        var details = await FfmpegService.ProbeAsync(file, ct);
        _probeCache[file] = details;
        return details;
    }
}
