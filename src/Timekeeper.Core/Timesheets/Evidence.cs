using System.Diagnostics;

namespace Timekeeper.Core.Timesheets;

/// <summary>A recorded segment together with what was read from its first screenshot.</summary>
public sealed record SegmentEvidence(ActivitySegment Segment, string? OcrText, string? ScreenshotPath);

public static class Evidence
{
    private static readonly string OwnProcessName = Process.GetCurrentProcess().ProcessName;

    /// <summary>Segments in the interval with their first screenshot, leaving out time spent in this app.</summary>
    public static IReadOnlyList<SegmentEvidence> Gather(IActivityStore store, DateTime fromUtc, DateTime toUtc)
    {
        var firstScreenshots = store.GetScreenshots(fromUtc, toUtc)
            .GroupBy(s => s.SegmentId)
            .ToDictionary(g => g.Key, g => g.First());

        return store.GetSegments(fromUtc, toUtc)
            .Where(s => !string.Equals(s.ProcessName, OwnProcessName, StringComparison.OrdinalIgnoreCase))
            .Select(s =>
            {
                firstScreenshots.TryGetValue(s.Id, out var screenshot);
                return new SegmentEvidence(s, screenshot?.OcrText, screenshot?.FilePath);
            })
            .ToList();
    }
}
