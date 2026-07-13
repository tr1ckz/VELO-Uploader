namespace VeloUploader.Editor;

/// <summary>
/// Owns the sequence state (clips on the timeline) and every edit operation,
/// with undo/redo. UI code never mutates segments directly — it calls into
/// this model and re-renders from <see cref="Segments"/> when
/// <see cref="Changed"/> fires.
/// </summary>
internal sealed class SequenceModel
{
    private const double MinClipDuration = 0.05;
    private const double Epsilon = 0.0001;
    private const int MaxHistoryDepth = 100;

    private readonly List<TimelineSegment> _segments = [];
    private readonly Stack<List<TimelineSegment>> _undoStack = new();
    private readonly Stack<List<TimelineSegment>> _redoStack = new();

    /// <summary>Raised after any mutation; the argument is the index to select (-1 for none).</summary>
    public event Action<int>? Changed;

    public IReadOnlyList<TimelineSegment> Segments => _segments;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public double TotalDuration => _segments.Count == 0 ? 0 : _segments.Max(s => s.SequenceEndSec);

    public int IndexOf(TimelineSegment segment) => _segments.FindIndex(s => ReferenceEquals(s, segment) || s == segment);

    public List<TimelineSegment> ActiveAt(double sequenceSeconds) =>
        _segments
            .Where(s => sequenceSeconds >= s.SequenceStartSec - Epsilon && sequenceSeconds <= s.SequenceEndSec + Epsilon)
            .OrderBy(s => s.SafeTrack)
            .ThenBy(s => s.SequenceStartSec)
            .ToList();

    public TimelineSegment? TopVisibleAt(double sequenceSeconds) =>
        ActiveAt(sequenceSeconds)
            .OrderByDescending(s => s.SafeTrack)
            .ThenByDescending(s => s.SequenceStartSec)
            .FirstOrDefault();

    public ClipTransform EvaluateTransform(TimelineSegment segment, double sequenceTime)
    {
        var transform = segment.SafeTransform;
        var keyframes = transform.SafeKeyframes.OrderBy(f => f.LocalSec).ToList();
        if (keyframes.Count == 0 || !transform.AnyKeyframed)
            return transform;

        var localTime = Math.Clamp(sequenceTime - segment.SequenceStartSec, 0, Math.Max(0.001, segment.Duration));
        var previous = keyframes.LastOrDefault(f => f.LocalSec <= localTime) ?? keyframes[0];
        var next = keyframes.FirstOrDefault(f => f.LocalSec >= localTime) ?? keyframes[^1];
        var span = next.LocalSec - previous.LocalSec;
        var ratio = Math.Abs(span) < Epsilon ? 0 : (localTime - previous.LocalSec) / span;

        return transform with
        {
            PositionX = transform.PositionKeyframed ? Lerp(previous.PositionX, next.PositionX, ratio) : transform.PositionX,
            PositionY = transform.PositionKeyframed ? Lerp(previous.PositionY, next.PositionY, ratio) : transform.PositionY,
            Scale = transform.ScaleKeyframed ? Lerp(previous.Scale, next.Scale, ratio) : transform.Scale,
            Opacity = transform.OpacityKeyframed ? Lerp(previous.Opacity, next.Opacity, ratio) : transform.Opacity,
        };
    }

    /// <summary>Insert edit: existing clips on the same track at/after the insert point shift right; a clip straddling it is split.</summary>
    public int Insert(TimelineSegment newSegment, double sequenceTime)
    {
        BeginEdit();
        var index = InsertCore(newSegment, sequenceTime);
        Commit(index);
        return index;
    }

    /// <summary>Overwrite edit: the new clip replaces whatever occupied its range on the same track.</summary>
    public int Overwrite(TimelineSegment newSegment, double sequenceTime)
    {
        BeginEdit();
        var index = OverwriteCore(newSegment, sequenceTime);
        Commit(index);
        return index;
    }

    /// <summary>Splits the segment at a source-file time, producing two adjacent clips. Returns the right-half index, or -1 if the cut is invalid.</summary>
    public int Split(int index, double sourceTime)
    {
        if (index < 0 || index >= _segments.Count)
            return -1;

        var segment = _segments[index];
        if (sourceTime <= segment.StartSec + MinClipDuration || sourceTime >= segment.EndSec - MinClipDuration)
            return -1;

        BeginEdit();
        var splitOffset = sourceTime - segment.StartSec;
        var left = segment with { EndSec = sourceTime };
        var right = segment with { StartSec = sourceTime, SequenceStartSec = segment.SequenceStartSec + splitOffset };
        _segments[index] = left;
        _segments.Insert(index + 1, right);
        Commit(index + 1);
        return index + 1;
    }

    /// <summary>
    /// Trims one edge of a segment to new source in/out times. In Ripple mode a
    /// duration change shifts everything downstream on the track; in Rolling
    /// mode the adjacent clip's edge moves in tandem.
    /// </summary>
    public int TrimEdge(int index, double newStart, double newEnd, TimelineTool tool)
    {
        if (index < 0 || index >= _segments.Count)
            return -1;

        BeginEdit();
        var segment = _segments[index];
        var safeStart = EditorTime.SnapToFrame(Math.Max(0, Math.Min(newStart, newEnd - (1d / EditorTime.DefaultFps))));
        var safeEnd = Math.Max(safeStart + (1d / EditorTime.DefaultFps), EditorTime.SnapToFrame(newEnd));
        var changingLeftEdge = Math.Abs(safeStart - segment.StartSec) > Math.Abs(safeEnd - segment.EndSec);
        TimelineSegment updated;

        if (changingLeftEdge)
        {
            var leftDelta = safeStart - segment.StartSec;
            updated = segment with
            {
                StartSec = safeStart,
                SequenceStartSec = Math.Max(0, segment.SequenceStartSec + leftDelta),
            };

            if (tool == TimelineTool.Rolling)
            {
                var previousIndex = FindAdjacentIndex(index, segment, lookLeft: true);
                if (previousIndex >= 0)
                {
                    var previous = _segments[previousIndex];
                    _segments[previousIndex] = previous with { EndSec = Math.Max(previous.StartSec + MinClipDuration, previous.EndSec + leftDelta) };
                }
            }
        }
        else
        {
            var rightDelta = safeEnd - segment.EndSec;
            updated = segment with { EndSec = safeEnd };

            if (tool == TimelineTool.Rolling)
            {
                var nextIndex = FindAdjacentIndex(index, segment, lookLeft: false);
                if (nextIndex >= 0)
                {
                    var next = _segments[nextIndex];
                    _segments[nextIndex] = next with
                    {
                        StartSec = Math.Clamp(next.StartSec + rightDelta, 0, next.EndSec - MinClipDuration),
                        SequenceStartSec = Math.Max(segment.SequenceStartSec + updated.Duration, next.SequenceStartSec + rightDelta),
                    };
                }
            }
        }

        var durationDelta = updated.Duration - segment.Duration;
        _segments[index] = updated;

        if (tool == TimelineTool.Ripple && Math.Abs(durationDelta) > Epsilon)
        {
            for (var i = 0; i < _segments.Count; i++)
            {
                if (i == index)
                    continue;

                var downstream = _segments[i];
                if (downstream.SafeTrack == updated.SafeTrack && downstream.SequenceStartSec > segment.SequenceStartSec + Epsilon)
                    _segments[i] = downstream with { SequenceStartSec = Math.Max(0, downstream.SequenceStartSec + durationDelta) };
            }
        }

        var selected = _segments[index];
        Normalize();
        var newIndex = Math.Max(0, _segments.FindIndex(s => s == selected));
        Commit(newIndex);
        return newIndex;
    }

    /// <summary>Slips the source content inside a clip without moving its timeline placement.</summary>
    public int Slip(int index, double deltaSeconds, double sourceDurationLimit)
    {
        if (index < 0 || index >= _segments.Count)
            return -1;

        BeginEdit();
        var segment = _segments[index];
        var duration = segment.Duration;
        var maxStart = Math.Max(0, sourceDurationLimit - duration);
        var newStart = Math.Clamp(segment.StartSec + deltaSeconds, 0, maxStart);
        _segments[index] = segment with { StartSec = newStart, EndSec = newStart + duration };
        Commit(index);
        return index;
    }

    /// <summary>Moves a segment to a new time/track. Ripple mode inserts (pushing clips aside); otherwise it overwrites.</summary>
    public int Move(int fromIndex, double targetTime, int targetTrack, bool ripple)
    {
        if (fromIndex < 0 || fromIndex >= _segments.Count)
            return -1;

        BeginEdit();
        var moving = _segments[fromIndex];
        _segments.RemoveAt(fromIndex);

        var clampedTime = Math.Max(0, targetTime);
        moving = moving with { Track = Math.Clamp(targetTrack, 1, 2), SequenceStartSec = clampedTime };
        var index = ripple ? InsertCore(moving, clampedTime) : OverwriteCore(moving, clampedTime);
        Commit(index);
        return index;
    }

    /// <summary>Removes a segment and pulls later clips on its track left to close the hole.</summary>
    public void RippleDelete(int index)
    {
        if (index < 0 || index >= _segments.Count)
            return;

        BeginEdit();
        var removed = _segments[index];
        _segments.RemoveAt(index);

        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.SafeTrack == removed.SafeTrack && segment.SequenceStartSec >= removed.SequenceEndSec - Epsilon)
                _segments[i] = segment with { SequenceStartSec = Math.Max(0, segment.SequenceStartSec - removed.Duration) };
        }

        Commit(Math.Min(index, _segments.Count - 1));
    }

    /// <summary>Closes the gap on a track at (or after) the given sequence time. Returns false when no gap exists there.</summary>
    public bool DeleteGapAt(int track, double sequenceTime)
    {
        var clampedTrack = Math.Clamp(track, 1, 2);
        var ordered = _segments
            .Where(s => s.SafeTrack == clampedTrack)
            .OrderBy(s => s.SequenceStartSec)
            .ToList();

        // Include the implicit gap between sequence start and the first clip.
        var gapEnd = double.NaN;
        var gapAmount = 0d;
        var previousEnd = 0d;
        foreach (var segment in ordered)
        {
            var gap = segment.SequenceStartSec - previousEnd;
            if (gap > MinClipDuration && sequenceTime <= segment.SequenceStartSec + Epsilon)
            {
                gapEnd = segment.SequenceStartSec;
                gapAmount = gap;
                break;
            }

            previousEnd = Math.Max(previousEnd, segment.SequenceEndSec);
        }

        if (double.IsNaN(gapEnd))
            return false;

        BeginEdit();
        for (var i = 0; i < _segments.Count; i++)
        {
            var segment = _segments[i];
            if (segment.SafeTrack == clampedTrack && segment.SequenceStartSec >= gapEnd - Epsilon)
                _segments[i] = segment with { SequenceStartSec = Math.Max(0, segment.SequenceStartSec - gapAmount) };
        }

        Commit(-1);
        return true;
    }

    public void SetTransform(int index, ClipTransform transform)
    {
        if (index < 0 || index >= _segments.Count)
            return;

        BeginEdit();
        _segments[index] = _segments[index] with { Transform = transform };
        Commit(index);
    }

    public void Clear()
    {
        if (_segments.Count == 0)
            return;

        BeginEdit();
        _segments.Clear();
        Commit(-1);
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0)
            return false;

        _redoStack.Push([.. _segments]);
        Restore(_undoStack.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0)
            return false;

        _undoStack.Push([.. _segments]);
        Restore(_redoStack.Pop());
        return true;
    }

    private void Restore(List<TimelineSegment> snapshot)
    {
        _segments.Clear();
        _segments.AddRange(snapshot);
        Changed?.Invoke(_segments.Count > 0 ? 0 : -1);
    }

    private DateTime _lastEditTimeUtc = DateTime.MinValue;

    private void BeginEdit()
    {
        // Coalesce rapid-fire edits (e.g. a continuous trim drag) into one undo step.
        var now = DateTime.UtcNow;
        var coalesce = (now - _lastEditTimeUtc).TotalMilliseconds < 500 && _undoStack.Count > 0;
        _lastEditTimeUtc = now;
        if (coalesce)
        {
            _redoStack.Clear();
            return;
        }

        _undoStack.Push([.. _segments]);
        if (_undoStack.Count > MaxHistoryDepth)
        {
            var trimmed = _undoStack.Take(MaxHistoryDepth).Reverse().ToList();
            _undoStack.Clear();
            trimmed.ForEach(_undoStack.Push);
        }

        _redoStack.Clear();
    }

    private void Commit(int selectIndex)
    {
        Changed?.Invoke(selectIndex);
    }

    private int InsertCore(TimelineSegment newSegment, double sequenceTime)
    {
        var clampedTime = Math.Max(0, sequenceTime);
        newSegment = newSegment with { SequenceStartSec = clampedTime };
        var updated = new List<TimelineSegment>();

        foreach (var segment in _segments)
        {
            if (segment.SafeTrack != newSegment.SafeTrack)
            {
                updated.Add(segment);
                continue;
            }

            if (segment.SequenceStartSec >= clampedTime - Epsilon)
            {
                updated.Add(segment with { SequenceStartSec = segment.SequenceStartSec + newSegment.Duration });
                continue;
            }

            if (segment.SequenceStartSec < clampedTime && segment.SequenceEndSec > clampedTime + Epsilon)
            {
                var splitOffset = clampedTime - segment.SequenceStartSec;
                var splitPoint = segment.StartSec + splitOffset;
                var left = segment with { EndSec = splitPoint };
                var right = segment with { StartSec = splitPoint, SequenceStartSec = clampedTime + newSegment.Duration };
                if (left.Duration > MinClipDuration)
                    updated.Add(left);
                if (right.Duration > MinClipDuration)
                    updated.Add(right);
                continue;
            }

            updated.Add(segment);
        }

        updated.Add(newSegment);
        _segments.Clear();
        _segments.AddRange(updated);
        Normalize();
        return Math.Max(0, _segments.FindIndex(s => s == newSegment));
    }

    private int OverwriteCore(TimelineSegment newSegment, double sequenceTime)
    {
        var clampedTime = Math.Max(0, sequenceTime);
        var overwriteEnd = clampedTime + newSegment.Duration;
        newSegment = newSegment with { SequenceStartSec = clampedTime };
        var updated = new List<TimelineSegment>();

        foreach (var segment in _segments)
        {
            if (segment.SafeTrack != newSegment.SafeTrack ||
                segment.SequenceEndSec <= clampedTime + Epsilon ||
                segment.SequenceStartSec >= overwriteEnd - Epsilon)
            {
                updated.Add(segment);
                continue;
            }

            if (segment.SequenceStartSec < clampedTime - Epsilon)
            {
                var leftKeep = clampedTime - segment.SequenceStartSec;
                var left = segment with { EndSec = segment.StartSec + leftKeep };
                if (left.Duration > MinClipDuration)
                    updated.Add(left);
            }

            if (segment.SequenceEndSec > overwriteEnd + Epsilon)
            {
                var rightKeep = segment.SequenceEndSec - overwriteEnd;
                var right = segment with
                {
                    StartSec = segment.EndSec - rightKeep,
                    SequenceStartSec = overwriteEnd,
                };
                if (right.Duration > MinClipDuration)
                    updated.Add(right);
            }
        }

        updated.Add(newSegment);
        _segments.Clear();
        _segments.AddRange(updated);
        Normalize();
        return Math.Max(0, _segments.FindIndex(s => s == newSegment));
    }

    private int FindAdjacentIndex(int index, TimelineSegment segment, bool lookLeft)
    {
        const double touchTolerance = 0.12;
        var candidates = _segments
            .Select((item, itemIndex) => (item, itemIndex))
            .Where(entry => entry.itemIndex != index && entry.item.SafeTrack == segment.SafeTrack);

        return lookLeft
            ? candidates
                .Where(entry => Math.Abs(entry.item.SequenceEndSec - segment.SequenceStartSec) <= touchTolerance)
                .OrderByDescending(entry => entry.item.SequenceEndSec)
                .Select(entry => entry.itemIndex)
                .FirstOrDefault(-1)
            : candidates
                .Where(entry => Math.Abs(entry.item.SequenceStartSec - segment.SequenceEndSec) <= touchTolerance)
                .OrderBy(entry => entry.item.SequenceStartSec)
                .Select(entry => entry.itemIndex)
                .FirstOrDefault(-1);
    }

    private void Normalize()
    {
        var ordered = _segments.OrderBy(s => s.SequenceStartSec).ThenBy(s => s.SafeTrack).ToList();
        _segments.Clear();
        _segments.AddRange(ordered);
    }

    private static float Lerp(float start, float end, double ratio) =>
        (float)(start + ((end - start) * Math.Clamp(ratio, 0, 1)));
}
