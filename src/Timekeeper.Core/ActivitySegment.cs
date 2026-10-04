namespace Timekeeper.Core;

/// <summary>A continuous stretch of time spent on the same activity.</summary>
public sealed class ActivitySegment
{
    public long Id { get; set; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; set; }
    public required ActivityState State { get; init; }
    public string? ProcessName { get; init; }
    public string? WindowTitle { get; init; }
    public string? Url { get; init; }
    public string? DocumentPath { get; init; }

    public TimeSpan Duration => EndUtc - StartUtc;
}
