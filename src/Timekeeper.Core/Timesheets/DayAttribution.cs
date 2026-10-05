using System.Text;

namespace Timekeeper.Core.Timesheets;

/// <summary>Why a segment was attributed to its client, so the user can see what to improve.</summary>
public enum AttributionReason
{
    /// <summary>Nothing is known yet.</summary>
    None,
    UserAnswer,
    Leisure,
    NameInWindow,
    NameOnScreen,
    Ai,
    WorkBeforeAndAfter,
    WorkBefore,
    WorkAfter,
}

/// <summary>A segment with the client its time belongs to.</summary>
public sealed record AttributedSegment(SegmentEvidence Evidence, string Client, double Confidence, AttributionReason Reason)
{
    public bool IsKnown => Reason != AttributionReason.None;

    /// <summary>Attributed to an actual client, not internal work or leisure.</summary>
    public bool IsClient => IsKnown && Client is not (WindowClassifier.InternalLabel or WindowClassifier.NotWorkLabel);
}

/// <summary>
/// Attributes a day's work to clients without asking any model, from what is already known about
/// each segment and, where nothing is, from the work around it: a Fortnox window right after a
/// client's spreadsheet is most likely for that client.
/// </summary>
public static class DayAttribution
{
    /// <summary>How far before or after a segment the surrounding work still says something about it.</summary>
    public static readonly TimeSpan ContextWindow = TimeSpan.FromMinutes(10);

    private const int MaxSurroundingWindows = 4;

    /// <param name="active">Active segments, in time order.</param>
    /// <param name="windowLabels">Stored labels per window signature; not used for shared windows.</param>
    /// <param name="occasionLabels">Stored labels for single occasions of shared windows, by segment id.</param>
    public static IReadOnlyList<AttributedSegment> Attribute(
        IReadOnlyList<SegmentEvidence> active,
        WindowClassifier classifier,
        IReadOnlyDictionary<string, WindowLabel> windowLabels,
        IReadOnlyDictionary<long, WindowLabel> occasionLabels)
    {
        var own = active.Select(e => Own(e, classifier, windowLabels, occasionLabels)).ToList();

        var attributed = new List<AttributedSegment>(own.Count);
        for (var i = 0; i < own.Count; i++)
        {
            attributed.Add(own[i].IsKnown ? own[i] : FromSurroundings(own, i) ?? own[i]);
        }

        return attributed;
    }

    /// <summary>The label stored for a segment: per occasion for shared windows, per window otherwise.</summary>
    public static WindowLabel? StoredLabel(
        ActivitySegment segment,
        IReadOnlyDictionary<string, WindowLabel> windowLabels,
        IReadOnlyDictionary<long, WindowLabel> occasionLabels) =>
        IsShared(segment)
            ? occasionLabels.GetValueOrDefault(segment.Id)
            : windowLabels.GetValueOrDefault(WindowSignature.Of(segment));

    public static bool IsShared(ActivitySegment segment) =>
        WindowKinds.IsShared(segment.ProcessName, segment.WindowTitle, segment.Url);

    /// <summary>What the user worked on just before and after a segment, as text for a model.</summary>
    public static string Surroundings(IReadOnlyList<AttributedSegment> day, int index)
    {
        var segment = day[index].Evidence.Segment;
        var before = Neighbours(day, index, -1).Take(MaxSurroundingWindows).Reverse().ToList();
        var after = Neighbours(day, index, 1).Take(MaxSurroundingWindows).ToList();
        if (before.Count == 0 && after.Count == 0)
        {
            return "";
        }

        var text = new StringBuilder("Arbete strax före och efter:\n");
        foreach (var neighbour in before)
        {
            text.AppendLine(Line(neighbour, segment.StartUtc - neighbour.Evidence.Segment.EndUtc, "före"));
        }

        text.AppendLine("- (det här fönstret)");
        foreach (var neighbour in after)
        {
            text.AppendLine(Line(neighbour, neighbour.Evidence.Segment.StartUtc - segment.EndUtc, "efter"));
        }

        return text.ToString();
    }

    private static AttributedSegment Own(
        SegmentEvidence evidence,
        WindowClassifier classifier,
        IReadOnlyDictionary<string, WindowLabel> windowLabels,
        IReadOnlyDictionary<long, WindowLabel> occasionLabels)
    {
        var known = StoredLabel(evidence.Segment, windowLabels, occasionLabels);
        var label = classifier.ClassifyWithoutModels(evidence, known);
        if (label is null)
        {
            return new AttributedSegment(evidence, WindowClassifier.InternalLabel, 0, AttributionReason.None);
        }

        var reason = label.Source switch
        {
            LabelSource.User => AttributionReason.UserAnswer,
            LabelSource.Rule when label.Client == WindowClassifier.NotWorkLabel => AttributionReason.Leisure,
            LabelSource.Rule => AttributionReason.NameInWindow,
            LabelSource.Screen => AttributionReason.NameOnScreen,
            LabelSource.Model => AttributionReason.Ai,
            _ => AttributionReason.None,
        };
        return new AttributedSegment(evidence, label.Client, label.Confidence, reason);
    }

    /// <summary>
    /// The client of the nearest known work before and after, within <see cref="ContextWindow"/>.
    /// Only the adjacent known work counts: if that was internal or leisure, it says nothing about a client.
    /// </summary>
    /// <remarks>
    /// Only short stretches inherit: a quick look at Fortnox or the inbox is part of the work around
    /// it, but half an hour in the inbox needs evidence of its own.
    /// </remarks>
    private static AttributedSegment? FromSurroundings(IReadOnlyList<AttributedSegment> own, int index)
    {
        var segment = own[index].Evidence.Segment;
        if (segment.Duration > ContextWindow)
        {
            return null;
        }

        var before = Neighbours(own, index, -1).FirstOrDefault(n => n.IsKnown);
        var after = Neighbours(own, index, 1).FirstOrDefault(n => n.IsKnown);
        before = before is { IsClient: true } ? before : null;
        after = after is { IsClient: true } ? after : null;

        if (before is not null && after is not null && before.Client == after.Client)
        {
            return own[index] with { Client = before.Client, Confidence = 0.8, Reason = AttributionReason.WorkBeforeAndAfter };
        }

        // Different clients on each side: the nearer one is the better guess.
        if (before is not null
            && (after is null || segment.StartUtc - before.Evidence.Segment.EndUtc <= after.Evidence.Segment.StartUtc - segment.EndUtc))
        {
            return own[index] with { Client = before.Client, Confidence = 0.6, Reason = AttributionReason.WorkBefore };
        }

        return after is null
            ? null
            : own[index] with { Client = after.Client, Confidence = 0.55, Reason = AttributionReason.WorkAfter };
    }

    /// <summary>Segments before (direction -1) or after (+1), nearest first, within the context window.</summary>
    private static IEnumerable<AttributedSegment> Neighbours(IReadOnlyList<AttributedSegment> day, int index, int direction)
    {
        var segment = day[index].Evidence.Segment;
        for (var i = index + direction; i >= 0 && i < day.Count; i += direction)
        {
            var other = day[i].Evidence.Segment;
            var gap = direction < 0 ? segment.StartUtc - other.EndUtc : other.StartUtc - segment.EndUtc;
            if (gap > ContextWindow)
            {
                yield break;
            }

            yield return day[i];
        }
    }

    private static string Line(AttributedSegment neighbour, TimeSpan gap, string side)
    {
        var segment = neighbour.Evidence.Segment;
        var client = neighbour.IsKnown ? $" → {neighbour.Client}" : "";
        var location = segment.DocumentPath ?? segment.Url;
        return $"- {(int)Math.Ceiling(segment.Duration.TotalMinutes)} min, {(int)gap.TotalMinutes} min {side}: "
            + $"{segment.ProcessName} · {segment.WindowTitle}{(location is null ? "" : $" · {location}")}{client}";
    }
}
