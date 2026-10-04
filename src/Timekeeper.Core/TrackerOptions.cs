namespace Timekeeper.Core;

public sealed record TrackerOptions
{
    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Time without input after which the user counts as away.</summary>
    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often to take a new screenshot while staying in the same window.</summary>
    public TimeSpan ScreenshotInterval { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ScreenshotRetention { get; init; } = TimeSpan.FromDays(14);

    /// <summary>A longer pause between samples (sleep, crash) starts a new segment.</summary>
    public TimeSpan MaxSampleGap => SampleInterval * 4;
}
