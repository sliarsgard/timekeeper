using System.Text;

namespace Timekeeper.Core.Timesheets;

public sealed record TimesheetProgress(int Done, int Total, string Message);

/// <summary>A row of the preliminary timesheet shown during the day.</summary>
/// <param name="Minutes">Rounded the way the timesheet will be.</param>
/// <param name="TimeSpent">The time actually recorded.</param>
/// <param name="Summary">The windows that took the most time.</param>
public sealed record PreviewEntry(string Client, int Minutes, TimeSpan TimeSpent, string Summary, double Confidence);

/// <summary>
/// Turns a day of recorded activity into a draft timesheet: one entry per client with time,
/// activity and a short comment.
/// </summary>
/// <remarks>
/// Ordinary windows are classified once per window and shared windows (Fortnox, Outlook, Teams)
/// once per occasion, by <see cref="WindowClassifier"/> with the surrounding work as context. With
/// a label store, what was classified during the day is not sent to a model again. The day is then
/// attributed by <see cref="DayAttribution"/>, the same way as the preliminary timesheet.
/// </remarks>
public sealed class TimesheetBuilder(
    TimesheetOptions options,
    IDecisionModel? decisionModel,
    ILanguageModel? languageModel,
    ILabelStore? labelStore = null)
{
    public const string InternalLabel = WindowClassifier.InternalLabel;

    public const string NotWorkLabel = WindowClassifier.NotWorkLabel;

    /// <summary>Windows not yet classified during the day, shown separately in the preview.</summary>
    public const string UnclassifiedLabel = "Ej klassat ännu";

    /// <summary>Shorter occasions of shared windows are left to the surrounding work rather than a model.</summary>
    private static readonly TimeSpan MinimumOccasionTime = TimeSpan.FromSeconds(30);

    private const string ActivityQuestion = "Which kind of accounting work does this activity describe?";
    private const int MaxSummaryLines = 12;

    public async Task<IReadOnlyList<TimesheetEntry>> BuildAsync(
        DateOnly date,
        IReadOnlyList<SegmentEvidence> evidence,
        IReadOnlyList<Client> clients,
        IProgress<TimesheetProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var active = Active(evidence);
        var classifier = new WindowClassifier(options, decisionModel, languageModel, clients);
        var labels = new StoredLabels(labelStore, active);
        var day = labels.Attribute(active, classifier);

        if (classifier.HasModels)
        {
            var pending = Pending(active, classifier, labels, MinimumOccasionTime);
            for (var i = 0; i < pending.Count; i++)
            {
                progress?.Report(new TimesheetProgress(i, pending.Count, "Klassificerar fönster"));
                var index = pending[i];
                var segment = active[index].Segment;
                var label = await classifier.ClassifyAsync(
                    active[index],
                    labels.For(segment),
                    DayAttribution.Surroundings(day, index),
                    cancellationToken);
                labels.Save(segment, label);
            }

            day = labels.Attribute(active, classifier);
        }

        // Time that is not work stays out of the timesheet.
        var groups = Group(day, unknownLabel: InternalLabel).Where(g => g.Client != NotWorkLabel).ToList();

        var entries = new List<TimesheetEntry>();
        for (var i = 0; i < groups.Count; i++)
        {
            var (client, segments, minutes) = groups[i];
            progress?.Report(new TimesheetProgress(i, groups.Count, $"Skriver kommentar för {client}"));

            var summary = Summarize(segments);
            var activity = client == InternalLabel ? InternalLabel : await PickActivityAsync(summary, cancellationToken);
            entries.Add(new TimesheetEntry
            {
                Date = date,
                Client = client,
                Activity = activity,
                Minutes = minutes,
                RecordedMinutes = (int)Math.Round(Sum(segments).TotalMinutes),
                Comment = await WriteCommentAsync(client, activity, summary, segments, cancellationToken),
                Confidence = WeightedConfidence(segments),
            });
        }

        progress?.Report(new TimesheetProgress(groups.Count, groups.Count, "Klar"));
        return entries;
    }

    /// <summary>
    /// A preliminary timesheet from what is already known, without asking any model, so it is
    /// cheap enough to refresh continuously. Work is attributed exactly as in the final draft;
    /// segments nothing is known about yet are grouped as <see cref="UnclassifiedLabel"/>.
    /// </summary>
    public IReadOnlyList<PreviewEntry> Preview(IReadOnlyList<SegmentEvidence> evidence, IReadOnlyList<Client> clients)
    {
        var active = Active(evidence);
        var day = new StoredLabels(labelStore, active).Attribute(active, new WindowClassifier(options, null, null, clients));

        return Group(day, unknownLabel: UnclassifiedLabel)
            .Select(g => new PreviewEntry(
                g.Client,
                g.Minutes,
                Sum(g.Segments),
                string.Join(", ", TopWindows(g.Segments).Take(3).Select(w => w.Title)),
                WeightedConfidence(g.Segments)))
            .ToList();
    }

    /// <summary>Active segments in time order: what <see cref="DayAttribution"/> works on.</summary>
    public static List<SegmentEvidence> Active(IReadOnlyList<SegmentEvidence> evidence) =>
        evidence
            .Where(e => e.Segment.State == ActivityState.Active && e.Segment.Duration > TimeSpan.Zero)
            .OrderBy(e => e.Segment.StartUtc)
            .ToList();

    /// <summary>
    /// Indexes of segments a model should look at: one per ordinary window and one per occasion of a
    /// shared window that is long enough, where nothing is known without a model.
    /// </summary>
    public static List<int> Pending(
        IReadOnlyList<SegmentEvidence> active,
        WindowClassifier classifier,
        StoredLabels labels,
        TimeSpan minimumOccasionTime)
    {
        var pending = new List<int>();
        var seenWindows = new HashSet<string>();
        for (var i = active.Count - 1; i >= 0; i--)
        {
            var segment = active[i].Segment;
            if (classifier.ClassifyWithoutModels(active[i], labels.For(segment)) is not null)
            {
                continue;
            }

            if (DayAttribution.IsShared(segment)
                ? segment.Duration >= minimumOccasionTime
                : seenWindows.Add(WindowSignature.Of(segment)))
            {
                pending.Add(i);
            }
        }

        // Most recent first, so the end of the day is ready soonest.
        return pending;
    }

    /// <summary>Folds short detours into the surrounding client, then sums and rounds per client.</summary>
    private List<(string Client, List<LabelledSegment> Segments, int Minutes)> Group(
        IReadOnlyList<AttributedSegment> day,
        string unknownLabel)
    {
        var labelled = day
            .Select(a => new LabelledSegment(a.Evidence, new Decision(a.IsKnown ? a.Client : unknownLabel, a.Confidence)))
            .ToList();
        AbsorbInterruptions(labelled);

        return labelled
            .GroupBy(l => l.Label.Choice)
            .Select(g => (Client: g.Key, Segments: g.ToList(), Minutes: Round(Sum(g))))
            .Where(g => g.Minutes > 0)
            .OrderByDescending(g => g.Minutes)
            .ToList();
    }

    private async Task<string> PickActivityAsync(string summary, CancellationToken cancellationToken)
    {
        Decision? decision = null;
        if (decisionModel is not null)
        {
            decision = WindowClassifier.Normalize(await decisionModel.ChooseAsync(ActivityQuestion, summary, options.Activities, cancellationToken), options.Activities);
            if (decision.Confidence >= options.ConfidenceThreshold)
            {
                return decision.Choice;
            }
        }

        if (languageModel is not null)
        {
            decision = WindowClassifier.Normalize(
                await languageModel.ChooseAsync(ActivityQuestion, summary, options.Activities, imagePath: null, cancellationToken),
                options.Activities);
        }

        return decision is { Confidence: > 0 } ? decision.Choice : options.DefaultActivity;
    }

    private async Task<string> WriteCommentAsync(
        string client,
        string activity,
        string summary,
        IReadOnlyList<LabelledSegment> segments,
        CancellationToken cancellationToken)
    {
        if (languageModel is not null)
        {
            var context = $"Kund: {client}\nAktivitet: {activity}\n\n{summary}";
            return (await languageModel.WriteCommentAsync(context, cancellationToken)).Trim();
        }

        // Without a language model, name the windows that took the most time.
        return string.Join(", ", TopWindows(segments).Take(3).Select(w => w.Title));
    }

    /// <summary>
    /// A short detour between two stretches for the same client (checking mail, a quick lookup)
    /// is part of that client's work.
    /// </summary>
    private void AbsorbInterruptions(List<LabelledSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            var label = segments[i].Label;
            if (label.Choice is InternalLabel or UnclassifiedLabel or NotWorkLabel)
            {
                continue;
            }

            var next = i + 1;
            while (next < segments.Count && segments[next].Label.Choice != label.Choice)
            {
                next++;
            }

            if (next < segments.Count
                && next > i + 1
                && segments[next].Evidence.Segment.StartUtc - segments[i].Evidence.Segment.EndUtc <= options.InterruptionThreshold)
            {
                // A video in the middle of client work is still not work for that client.
                for (var k = i + 1; k < next; k++)
                {
                    if (segments[k].Label.Choice != NotWorkLabel)
                    {
                        segments[k].Label = label;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Rounds a client's time for the day to whole steps: up to the next started step by default,
    /// otherwise to the nearest. Time under the minimum is left out.
    /// </summary>
    private int Round(TimeSpan duration)
    {
        var minutes = Math.Round(duration.TotalMinutes);
        if (minutes < options.MinimumMinutes)
        {
            return 0;
        }

        var steps = options.RoundUp
            ? Math.Ceiling(minutes / options.RoundingMinutes)
            : Math.Round(minutes / options.RoundingMinutes, MidpointRounding.AwayFromZero);
        return (int)steps * options.RoundingMinutes;
    }

    private static TimeSpan Sum(IEnumerable<LabelledSegment> segments) =>
        segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Evidence.Segment.Duration);

    private static double WeightedConfidence(IReadOnlyList<LabelledSegment> segments)
    {
        var total = Sum(segments).TotalSeconds;
        return total <= 0
            ? 0
            : segments.Sum(s => s.Label.Confidence * s.Evidence.Segment.Duration.TotalSeconds) / total;
    }

    private static string Summarize(IReadOnlyList<LabelledSegment> segments)
    {
        var text = new StringBuilder("Fönster, med tid:\n");
        foreach (var window in TopWindows(segments).Take(MaxSummaryLines))
        {
            text.Append($"- {(int)window.Duration.TotalMinutes} min · {window.Process} · {window.Title}");
            text.AppendLine(window.Location is null ? "" : $" · {window.Location}");
        }

        var excerpts = segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Evidence.OcrText))
            .OrderByDescending(s => s.Evidence.Segment.Duration)
            .Take(3)
            .ToList();
        if (excerpts.Count > 0)
        {
            text.AppendLine("\nUtdrag ur skärmtext (OCR):");
            foreach (var excerpt in excerpts)
            {
                text.AppendLine($"---\n{WindowClassifier.Truncate(excerpt.Evidence.OcrText!, 600)}");
            }
        }

        return text.ToString();
    }

    private static IEnumerable<(string Process, string Title, string? Location, TimeSpan Duration)> TopWindows(
        IEnumerable<LabelledSegment> segments) =>
        segments
            .GroupBy(s => (s.Evidence.Segment.ProcessName, s.Evidence.Segment.WindowTitle, Location: s.Evidence.Segment.DocumentPath ?? s.Evidence.Segment.Url))
            .Select(g => (g.Key.ProcessName ?? "", g.Key.WindowTitle ?? "", g.Key.Location, Sum(g)))
            .OrderByDescending(w => w.Item4);

    private sealed class LabelledSegment(SegmentEvidence evidence, Decision label)
    {
        public SegmentEvidence Evidence { get; } = evidence;
        public Decision Label { get; set; } = label;
    }
}

/// <summary>
/// The stored labels relevant to a day, kept in step with what is saved while classifying: per
/// window for ordinary windows and per occasion for shared ones.
/// </summary>
public sealed class StoredLabels
{
    private readonly ILabelStore? _store;
    private readonly Dictionary<string, WindowLabel> _windows;
    private readonly Dictionary<long, WindowLabel> _occasions;

    public StoredLabels(ILabelStore? store, IReadOnlyList<SegmentEvidence> segments)
    {
        _store = store;
        _windows = new Dictionary<string, WindowLabel>(store?.GetLabels() ?? new Dictionary<string, WindowLabel>());
        _occasions = store is null || segments.Count == 0
            ? []
            : new Dictionary<long, WindowLabel>(store.GetOccasionLabels(segments.Min(s => s.Segment.Id), segments.Max(s => s.Segment.Id)));
    }

    public WindowLabel? For(ActivitySegment segment) => DayAttribution.StoredLabel(segment, _windows, _occasions);

    public void Save(ActivitySegment segment, WindowLabel label)
    {
        if (DayAttribution.IsShared(segment))
        {
            _occasions[segment.Id] = label;
            _store?.SaveOccasionLabel(segment.Id, label);
        }
        else
        {
            _windows[label.Signature] = label;
            _store?.SaveLabel(label);
        }
    }

    public IReadOnlyList<AttributedSegment> Attribute(IReadOnlyList<SegmentEvidence> active, WindowClassifier classifier) =>
        DayAttribution.Attribute(active, classifier, _windows, _occasions);
}
