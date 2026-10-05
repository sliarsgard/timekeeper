namespace Timekeeper.Core;

public interface IActivityStore
{
    /// <summary>Saves a new segment and assigns its <see cref="ActivitySegment.Id"/>.</summary>
    void InsertSegment(ActivitySegment segment);

    void UpdateSegmentEnd(ActivitySegment segment);

    /// <summary>Overwrites everything about a stored segment except its id and times, e.g. to correct its state.</summary>
    void UpdateSegment(ActivitySegment segment);

    /// <summary>Returns the segments overlapping the interval, ordered by start.</summary>
    IReadOnlyList<ActivitySegment> GetSegments(DateTime fromUtc, DateTime toUtc);

    void InsertScreenshot(Screenshot screenshot);

    IReadOnlyList<Screenshot> GetScreenshots(long segmentId);

    /// <summary>Returns the screenshots taken within the interval, ordered by time.</summary>
    IReadOnlyList<Screenshot> GetScreenshots(DateTime fromUtc, DateTime toUtc);

    /// <summary>Removes screenshots taken before the cutoff and returns them so their files can be deleted.</summary>
    IReadOnlyList<Screenshot> DeleteScreenshotsTakenBefore(DateTime cutoffUtc);
}
