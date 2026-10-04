namespace Timekeeper.Core;

/// <summary>A single observation of what the user is doing at <see cref="TimestampUtc"/>.</summary>
/// <param name="IdleFor">Time since the last keyboard or mouse input.</param>
/// <param name="Url">Address shown in the browser, when the foreground window is a browser.</param>
/// <param name="DocumentPath">Full path or SharePoint URL of the open Office document.</param>
public sealed record ActivitySample(
    DateTime TimestampUtc,
    ActivityState State,
    TimeSpan IdleFor = default,
    string? ProcessName = null,
    string? WindowTitle = null,
    string? Url = null,
    string? DocumentPath = null);
