using System.Text;

namespace Timekeeper.Core.Timesheets;

public sealed record TimesheetProgress(int Done, int Total, string Message);

/// <summary>
/// Turns a day of recorded activity into a draft timesheet: one entry per client with time,
/// activity and a short comment.
/// </summary>
/// <remarks>
/// Each distinct window is classified once by <see cref="WindowClassifier"/>. With a label store,
/// windows already classified during the day are not sent to a model again.
/// </remarks>
public sealed class TimesheetBuilder(
    TimesheetOptions options,
    IDecisionModel? decisionModel,
    ILanguageModel? languageModel,
    ILabelStore? labelStore = null)
{
    public const string InternalLabel = WindowClassifier.InternalLabel;

    private const string ActivityQuestion = "Which kind of accounting work does this activity describe?";
    private const int MaxSummaryLines = 12;

    public async Task<IReadOnlyList<TimesheetEntry>> BuildAsync(
        DateOnly date,
        IReadOnlyList<SegmentEvidence> evidence,
        IReadOnlyList<Client> clients,
        IProgress<TimesheetProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var active = evidence
            .Where(e => e.Segment.State == ActivityState.Active && e.Segment.Duration > TimeSpan.Zero)
            .OrderBy(e => e.Segment.StartUtc)
            .ToList();

        var classifier = new WindowClassifier(options, decisionModel, languageModel, clients);
        var known = labelStore?.GetLabels() ?? new Dictionary<string, WindowLabel>();

        // People switch back and forth between the same few windows; classify each window once.
        var windows = active.GroupBy(e => WindowSignature.Of(e.Segment)).ToList();
        var labels = new Dictionary<string, Decision>();
        for (var i = 0; i < windows.Count; i++)
        {
            progress?.Report(new TimesheetProgress(i, windows.Count, "Klassificerar fönster"));
            var representative = windows[i].FirstOrDefault(e => e.OcrText is not null) ?? windows[i].First();
            known.TryGetValue(windows[i].Key, out var previous);
            var label = await classifier.ClassifyAsync(representative, previous, cancellationToken);
            if (label != previous)
            {
                labelStore?.SaveLabel(label);
            }

            labels[windows[i].Key] = label.ToDecision();
        }

        var labelled = active.Select(e => new LabelledSegment(e, labels[WindowSignature.Of(e.Segment)])).ToList();
        AbsorbInterruptions(labelled);

        var groups = labelled
            .GroupBy(l => l.Label.Choice)
            .Select(g => (Client: g.Key, Segments: g.ToList(), Minutes: Round(Sum(g))))
            .Where(g => g.Minutes > 0)
            .OrderByDescending(g => g.Minutes)
            .ToList();

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
                Comment = await WriteCommentAsync(client, activity, summary, segments, cancellationToken),
                Confidence = WeightedConfidence(segments),
            });
        }

        progress?.Report(new TimesheetProgress(groups.Count, groups.Count, "Klar"));
        return entries;
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
            if (label.Choice == InternalLabel)
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
                for (var k = i + 1; k < next; k++)
                {
                    segments[k].Label = label;
                }
            }
        }
    }

    private int Round(TimeSpan duration)
    {
        var increments = Math.Round(duration.TotalMinutes / options.RoundingMinutes, MidpointRounding.AwayFromZero);
        return (int)increments * options.RoundingMinutes;
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
