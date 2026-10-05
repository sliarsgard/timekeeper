namespace Timekeeper.Core.Timesheets;

/// <summary>The models and preferences to use for one run; read fresh each time so settings apply at once.</summary>
public sealed record LiveClassifierSetup(
    TimesheetOptions Options,
    IDecisionModel? DecisionModel,
    ILanguageModel? LanguageModel,
    bool AskWhenUnsure);

/// <summary>A window the classification is unsure about, to ask the user about.</summary>
/// <param name="SegmentId">Set for shared windows such as Fortnox: the answer only applies to that occasion.</param>
/// <param name="Suggestion">The most likely client, or null when there is no good guess.</param>
public sealed record WindowQuestion(
    string Signature,
    long? SegmentId,
    string? ProcessName,
    string Title,
    string? Suggestion,
    IReadOnlyList<string> Clients)
{
    /// <summary>Identifies what is being asked about, so it is only asked once.</summary>
    public string Key => SegmentId is { } id ? $"#{id}" : Signature;
}

/// <summary>
/// Classifies windows during the day, as soon as they have been used for a while, so the timesheet
/// is ready at the end of the day and unsure windows can be asked about while the user remembers.
/// </summary>
public sealed class LiveClassifier(IActivityStore activityStore, ITimesheetStore timesheetStore, ILabelStore labelStore)
{
    /// <summary>Windows that only flash by are not worth a model call; the surrounding work decides them.</summary>
    public static readonly TimeSpan MinimumWindowTime = TimeSpan.FromMinutes(1);

    /// <summary>Only windows that matter for the timesheet are worth interrupting the user for.</summary>
    public static readonly TimeSpan MinimumTimeBeforeAsking = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan TimeBetweenQuestions = TimeSpan.FromMinutes(5);

    // Spreads a backlog (e.g. after adding API keys mid-day) over several runs.
    private const int MaxClassificationsPerRun = 5;

    private readonly HashSet<string> _asked = [];
    private DateTime _lastAskedUtc = DateTime.MinValue;

    /// <summary>Raised, possibly on a background thread, when stored labels have changed.</summary>
    public event EventHandler? LabelsChanged;

    /// <summary>
    /// Classifies the day's windows that have been used long enough and are not yet known, with the
    /// surrounding work as context, then returns a question about an unsure one if it is time to ask.
    /// </summary>
    /// <remarks>Model failures propagate; already stored labels are kept and the rest is retried next run.</remarks>
    public async Task<WindowQuestion?> RunOnceAsync(
        LiveClassifierSetup setup,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var active = TimesheetBuilder.Active(Evidence.Gather(activityStore, dayStartUtc, dayEndUtc));
        var clients = timesheetStore.GetClients();
        var classifier = new WindowClassifier(setup.Options, setup.DecisionModel, setup.LanguageModel, clients);
        var labels = new StoredLabels(labelStore, active);
        var day = labels.Attribute(active, classifier);

        if (classifier.HasModels)
        {
            var classified = 0;
            try
            {
                foreach (var index in TimesheetBuilder.Pending(active, classifier, labels, MinimumWindowTime))
                {
                    if (classified == MaxClassificationsPerRun)
                    {
                        break;
                    }

                    // An ordinary window is judged on all its time today; the occasion still in front may grow.
                    var segment = active[index].Segment;
                    var shared = DayAttribution.IsShared(segment);
                    if ((shared && index == active.Count - 1)
                        || (!shared && TimeSpent(active, WindowSignature.Of(segment)) < MinimumWindowTime))
                    {
                        continue;
                    }

                    var label = await classifier.ClassifyAsync(
                        active[index],
                        labels.For(segment),
                        DayAttribution.Surroundings(day, index),
                        cancellationToken);
                    labels.Save(segment, label);
                    classified++;
                }
            }
            finally
            {
                if (classified > 0)
                {
                    LabelsChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            day = labels.Attribute(active, classifier);
        }

        return NextQuestion(setup, day, clients, nowUtc);
    }

    /// <summary>Records that the question was shown, so the same thing is not asked about again.</summary>
    public void MarkAsked(string key, DateTime nowUtc)
    {
        _asked.Add(key);
        _lastAskedUtc = nowUtc;
    }

    public void Answer(WindowQuestion question, string client)
    {
        var label = new WindowLabel(question.Signature, client, 1, LabelSource.User);
        if (question.SegmentId is { } segmentId)
        {
            labelStore.SaveOccasionLabel(segmentId, label);
        }
        else
        {
            labelStore.SaveLabel(label);
        }

        LabelsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Answers for an ordinary window, which then applies wherever that window appears.</summary>
    public void Answer(string signature, string client)
    {
        labelStore.SaveLabel(new WindowLabel(signature, client, 1, LabelSource.User));
        LabelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private WindowQuestion? NextQuestion(
        LiveClassifierSetup setup,
        IReadOnlyList<AttributedSegment> day,
        IReadOnlyList<Client> clients,
        DateTime nowUtc)
    {
        if (!setup.AskWhenUnsure || nowUtc - _lastAskedUtc < TimeBetweenQuestions)
        {
            return null;
        }

        // Ordinary windows are asked about once for all their time, shared ones per occasion;
        // the most recent first, as that is what the user remembers best.
        var candidates = day
            .GroupBy(a => DayAttribution.IsShared(a.Evidence.Segment) ? $"#{a.Evidence.Segment.Id}" : WindowSignature.Of(a.Evidence.Segment))
            .Select(g => (Key: g.Key, Latest: g.MaxBy(a => a.Evidence.Segment.EndUtc)!, TimeSpent: g.Aggregate(TimeSpan.Zero, (sum, a) => sum + a.Evidence.Segment.Duration)))
            .OrderByDescending(c => c.Latest.Evidence.Segment.EndUtc);

        foreach (var (key, latest, timeSpent) in candidates)
        {
            if (timeSpent < MinimumTimeBeforeAsking
                || _asked.Contains(key)
                || latest.Reason is AttributionReason.UserAnswer or AttributionReason.NameInWindow or AttributionReason.Leisure
                || (latest.IsKnown && latest.Confidence >= setup.Options.ConfidenceThreshold))
            {
                continue;
            }

            var segment = latest.Evidence.Segment;
            return new WindowQuestion(
                WindowSignature.Of(segment),
                DayAttribution.IsShared(segment) ? segment.Id : null,
                segment.ProcessName,
                segment.WindowTitle ?? "",
                latest.IsClient ? latest.Client : null,
                clients.Select(c => c.Name).ToList());
        }

        return null;
    }

    private static TimeSpan TimeSpent(IEnumerable<SegmentEvidence> active, string signature) =>
        active
            .Where(e => WindowSignature.Of(e.Segment) == signature)
            .Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Segment.Duration);
}
