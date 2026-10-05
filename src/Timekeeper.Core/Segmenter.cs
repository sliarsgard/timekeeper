namespace Timekeeper.Core;

/// <param name="Current">The segment the latest sample belongs to.</param>
/// <param name="Closed">The previous segment, when the latest sample started a new one.</param>
public sealed record SegmentChange(ActivitySegment Current, ActivitySegment? Closed, bool Started);

/// <summary>Turns a stream of samples into contiguous segments of the same activity.</summary>
public sealed class Segmenter(TimeSpan maxSampleGap)
{
    private ActivitySegment? _current;
    private DateTime _notBeforeUtc = DateTime.MinValue;

    public SegmentChange Add(ActivitySample sample)
    {
        var previous = _current;
        var continuous = previous is not null && sample.TimestampUtc - previous.EndUtc <= maxSampleGap;

        if (continuous && IsSameActivity(previous!, sample))
        {
            previous!.EndUtc = sample.TimestampUtc;
            return new SegmentChange(previous, Closed: null, Started: false);
        }

        // Idle time began at the last input, not when the threshold was crossed.
        var start = sample.State == ActivityState.Idle ? sample.TimestampUtc - sample.IdleFor : sample.TimestampUtc;
        var earliest = previous is null ? _notBeforeUtc : continuous ? previous.StartUtc : previous.EndUtc;
        start = start < earliest ? earliest : start;

        // Back-to-back segments share a boundary so no time falls between samples.
        if (continuous)
        {
            previous!.EndUtc = start;
        }

        _current = new ActivitySegment
        {
            StartUtc = start,
            EndUtc = sample.TimestampUtc,
            State = sample.State,
            ProcessName = sample.ProcessName,
            WindowTitle = sample.WindowTitle,
            Url = sample.Url,
            DocumentPath = sample.DocumentPath,
        };
        return new SegmentChange(_current, previous, Started: true);
    }

    /// <summary>
    /// Keeps the next segment from starting before time that is already recorded, e.g. by an earlier
    /// run of the app. Otherwise idle time reaching back to the last input could be counted twice.
    /// </summary>
    public void StartAfter(DateTime utc) => _notBeforeUtc = utc;

    /// <summary>Ends the current segment, e.g. when tracking is paused.</summary>
    public ActivitySegment? Close()
    {
        var closed = _current;
        _current = null;
        return closed;
    }

    private static bool IsSameActivity(ActivitySegment segment, ActivitySample sample)
    {
        if (segment.State != sample.State)
        {
            return false;
        }

        // While away, whatever window happens to be in front is irrelevant.
        return sample.State != ActivityState.Active
            || (segment.ProcessName == sample.ProcessName
                && segment.WindowTitle == sample.WindowTitle
                && segment.Url == sample.Url
                && segment.DocumentPath == sample.DocumentPath);
    }
}
