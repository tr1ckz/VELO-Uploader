namespace VeloUploader.Editor;

/// <summary>Probed properties of a media file.</summary>
internal sealed record VideoDetails(double Duration, int Width, int Height, double Fps, bool HasAudio);

/// <summary>A transform sample anchored to a time local to its clip (seconds from clip start in the sequence).</summary>
internal sealed record TransformKeyframe(double LocalSec, float PositionX, float PositionY, float Scale, float Opacity);

internal sealed record ClipTransform(
    float PositionX = 0f,
    float PositionY = 0f,
    float Scale = 1f,
    float Opacity = 1f,
    bool PositionKeyframed = false,
    bool ScaleKeyframed = false,
    bool OpacityKeyframed = false,
    IReadOnlyList<TransformKeyframe>? Keyframes = null)
{
    public IReadOnlyList<TransformKeyframe> SafeKeyframes => Keyframes ?? [];

    public bool IsDefault =>
        Math.Abs(PositionX) < 0.01f && Math.Abs(PositionY) < 0.01f &&
        Math.Abs(Scale - 1f) < 0.001f && Math.Abs(Opacity - 1f) < 0.001f &&
        !PositionKeyframed && !ScaleKeyframed && !OpacityKeyframed;

    public bool AnyKeyframed => PositionKeyframed || ScaleKeyframed || OpacityKeyframed;
}

/// <summary>
/// One clip on the sequence timeline. StartSec/EndSec address the source file;
/// SequenceStartSec places the clip on the sequence. Track 1 is the base video
/// track, track 2 renders above it.
/// </summary>
internal sealed record TimelineSegment(
    string SourceFile,
    double StartSec,
    double EndSec,
    int Track = 1,
    double SequenceStartSec = 0,
    ClipTransform? Transform = null)
{
    public int SafeTrack => Math.Clamp(Track, 1, 2);
    public double Duration => Math.Max(0, EndSec - StartSec);
    public double SequenceEndSec => SequenceStartSec + Duration;
    public ClipTransform SafeTransform => Transform ?? new();

    public override string ToString() =>
        $"V{SafeTrack} {EditorTime.Format(SequenceStartSec)}  {Path.GetFileName(SourceFile)}  ({EditorTime.Format(Duration)})";
}

internal enum TimelineTool
{
    Select,
    Ripple,
    Rolling,
    Slip,
    Razor,
}

internal static class EditorTime
{
    public const double DefaultFps = 30d;

    /// <summary>Formats seconds as mm:ss.fff (or hh:mm:ss.fff past an hour).</summary>
    public static string Format(double totalSeconds)
    {
        var safe = Math.Max(0, totalSeconds);
        var ts = TimeSpan.FromSeconds(safe);
        return safe >= 3600
            ? ts.ToString(@"hh\:mm\:ss\.fff")
            : ts.ToString(@"mm\:ss\.fff");
    }

    public static double SnapToFrame(double seconds, double fps = DefaultFps)
    {
        var safeFps = Math.Max(1d, fps);
        return Math.Round(Math.Max(0, seconds) * safeFps) / safeFps;
    }
}
