using System.Text;

namespace Timekeeper.Core.Timesheets;

/// <summary>A recorded segment together with what was read from its first screenshot.</summary>
public sealed record SegmentEvidence(ActivitySegment Segment, string? OcrText, string? ScreenshotPath);

public sealed record TimesheetProgress(int Done, int Total, string Message);

/// <summary>
/// Turns a day of recorded activity into a draft timesheet: one entry per client with time,
/// activity and a short comment.
/// </summary>
/// <remarks>
/// Each distinct window is classified once, cheapest first: keyword rules, then the decision model,
/// and only when that is unsure the language model with a screenshot. Either model may be missing.
/// </remarks>
public sealed class TimesheetBuilder(TimesheetOptions options, IDecisionModel? decisionModel, ILanguageModel? languageModel)
{
    public const string InternalLabel = "Internt";

    // Jev is most accurate in English; option names stay as the user wrote them.
    private const string ClientQuestion =
        "Which client company of a Swedish accounting firm is the work in this window for? "
        + $"Choose \"{InternalLabel}\" if it is not for any specific client.";

    private const string ActivityQuestion = "Which kind of accounting work does this activity describe?";
    private const int MaxOcrCharacters = 2000;
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

        var matcher = new ClientMatcher(clients);
        var clientOptions = clients.Select(c => c.Name).Append(InternalLabel).ToList();

        // People switch back and forth between the same few windows; classify each window once.
        var windows = active.GroupBy(e => Signature(e.Segment)).ToList();
        var labels = new Dictionary<string, Decision>();
        for (var i = 0; i < windows.Count; i++)
        {
            progress?.Report(new TimesheetProgress(i, windows.Count, "Klassificerar fönster"));
            var representative = windows[i].FirstOrDefault(e => e.OcrText is not null) ?? windows[i].First();
            labels[windows[i].Key] = await ClassifyAsync(representative, matcher, clientOptions, cancellationToken);
        }

        var labelled = active.Select(e => new LabelledSegment(e, labels[Signature(e.Segment)])).ToList();
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

    private async Task<Decision> ClassifyAsync(
        SegmentEvidence evidence,
        ClientMatcher matcher,
        IReadOnlyList<string> clientOptions,
        CancellationToken cancellationToken)
    {
        var segment = evidence.Segment;
        var strongMatches = matcher.FindIn(string.Join('\n', segment.WindowTitle, segment.Url, segment.DocumentPath));
        if (strongMatches.Count == 1)
        {
            return new Decision(strongMatches[0].Name, 0.95);
        }

        var ocrMatches = matcher.FindIn(evidence.OcrText);
        var context = Describe(evidence, ocrMatches);

        Decision? decision = null;
        if (decisionModel is not null)
        {
            decision = Normalize(await decisionModel.ChooseAsync(ClientQuestion, context, clientOptions, cancellationToken), clientOptions);
            if (decision.Confidence >= options.ConfidenceThreshold)
            {
                return decision;
            }
        }

        if (languageModel is not null)
        {
            var image = options.SendScreenshots ? evidence.ScreenshotPath : null;
            return Normalize(
                await languageModel.ChooseAsync(ClientQuestion, context, clientOptions, image, cancellationToken),
                clientOptions);
        }

        // Without models, a client named on screen is a reasonable but unsure guess.
        return decision
            ?? (ocrMatches.Count == 1 ? new Decision(ocrMatches[0].Name, 0.5) : new Decision(InternalLabel, 0));
    }

    private async Task<string> PickActivityAsync(string summary, CancellationToken cancellationToken)
    {
        Decision? decision = null;
        if (decisionModel is not null)
        {
            decision = Normalize(await decisionModel.ChooseAsync(ActivityQuestion, summary, options.Activities, cancellationToken), options.Activities);
            if (decision.Confidence >= options.ConfidenceThreshold)
            {
                return decision.Choice;
            }
        }

        if (languageModel is not null)
        {
            decision = Normalize(
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

    /// <summary>Maps a free-form answer onto the option list; anything unrecognised is not a decision.</summary>
    private static Decision Normalize(Decision decision, IReadOnlyList<string> optionList)
    {
        var match = optionList.FirstOrDefault(o => string.Equals(o, decision.Choice.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is null
            ? new Decision(optionList.Contains(InternalLabel) ? InternalLabel : optionList[0], 0)
            : decision with { Choice = match };
    }

    private static string Signature(ActivitySegment segment) =>
        string.Join('\u001f', segment.ProcessName, segment.WindowTitle, segment.Url, segment.DocumentPath);

    private static string Describe(SegmentEvidence evidence, IReadOnlyList<Client> mentionedClients)
    {
        var segment = evidence.Segment;
        var text = new StringBuilder();
        text.AppendLine($"Program: {segment.ProcessName}");
        text.AppendLine($"Fönstertitel: {segment.WindowTitle}");
        AppendIfPresent(text, "Webbadress", segment.Url);
        AppendIfPresent(text, "Dokument", segment.DocumentPath);
        if (mentionedClients.Count > 0)
        {
            text.AppendLine($"Kunder som nämns på skärmen: {string.Join(", ", mentionedClients.Select(c => c.Name))}");
        }

        if (!string.IsNullOrWhiteSpace(evidence.OcrText))
        {
            text.AppendLine("Text på skärmen (OCR):");
            text.AppendLine(Truncate(evidence.OcrText, MaxOcrCharacters));
        }

        return text.ToString();
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
                text.AppendLine($"---\n{Truncate(excerpt.Evidence.OcrText!, 600)}");
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

    private static void AppendIfPresent(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.AppendLine($"{label}: {value}");
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

    private sealed class LabelledSegment(SegmentEvidence evidence, Decision label)
    {
        public SegmentEvidence Evidence { get; } = evidence;
        public Decision Label { get; set; } = label;
    }
}
