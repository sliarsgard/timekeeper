namespace Timekeeper.Core.Timesheets;

/// <summary>The models and preferences to use for one run; read fresh each time so settings apply at once.</summary>
public sealed record LiveClassifierSetup(
    TimesheetOptions Options,
    IDecisionModel? DecisionModel,
    ILanguageModel? LanguageModel,
    bool AskWhenUnsure);

/// <summary>A window the classification is unsure about, to ask the user about.</summary>
/// <param name="Suggestion">The most likely client, or null when there is no good guess.</param>
public sealed record WindowQuestion(
    string Signature,
    string? ProcessName,
    string Title,
    string? Suggestion,
    IReadOnlyList<string> Clients);

/// <summary>
/// Classifies windows during the day, as soon as they have been used for a while, so the timesheet
/// is ready at the end of the day and unsure windows can be asked about while the user remembers.
/// </summary>
public sealed class LiveClassifier(IActivityStore activityStore, ITimesheetStore timesheetStore, ILabelStore labelStore)
{
    /// <summary>Windows that only flash by are not worth a model call.</summary>
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
    /// Classifies the day's windows that have been used long enough and are not yet classified,
    /// then returns a question about an unsure window if it is time to ask one.
    /// </summary>
    /// <remarks>Model failures propagate; already stored labels are kept and the rest is retried next run.</remarks>
    public async Task<WindowQuestion?> RunOnceAsync(
        LiveClassifierSetup setup,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var windows = Evidence.Gather(activityStore, dayStartUtc, dayEndUtc)
            .Where(e => e.Segment.State == ActivityState.Active)
            .GroupBy(e => WindowSignature.Of(e.Segment))
            .Select(g => new Window(
                g.Key,
                g.FirstOrDefault(e => e.OcrText is not null) ?? g.First(),
                g.Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Segment.Duration),
                g.Max(e => e.Segment.EndUtc)))
            .Where(w => w.TimeSpent >= MinimumWindowTime)
            .OrderByDescending(w => w.LastSeenUtc)
            .ToList();

        var clients = timesheetStore.GetClients();
        var classifier = new WindowClassifier(setup.Options, setup.DecisionModel, setup.LanguageModel, clients);
        var labels = new Dictionary<string, WindowLabel>(labelStore.GetLabels());

        var classified = 0;
        try
        {
            foreach (var window in windows)
            {
                if (classified == MaxClassificationsPerRun)
                {
                    break;
                }

                labels.TryGetValue(window.Signature, out var label);

                // Guesses made without models are revisited once a model is available.
                if (label is not null && !(label.Source == LabelSource.Guess && classifier.HasModels))
                {
                    continue;
                }

                label = await classifier.ClassifyAsync(window.Evidence, label, cancellationToken);
                labelStore.SaveLabel(label);
                labels[window.Signature] = label;
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

        return NextQuestion(setup, windows, labels, clients, nowUtc);
    }

    /// <summary>Records that the question was shown, so the same window is not asked about again.</summary>
    public void MarkAsked(string signature, DateTime nowUtc)
    {
        _asked.Add(signature);
        _lastAskedUtc = nowUtc;
    }

    public void Answer(string signature, string client)
    {
        labelStore.SaveLabel(new WindowLabel(signature, client, 1, LabelSource.User));
        LabelsChanged?.Invoke(this, EventArgs.Empty);
    }

    private WindowQuestion? NextQuestion(
        LiveClassifierSetup setup,
        IReadOnlyList<Window> windows,
        IReadOnlyDictionary<string, WindowLabel> labels,
        IReadOnlyList<Client> clients,
        DateTime nowUtc)
    {
        if (!setup.AskWhenUnsure || nowUtc - _lastAskedUtc < TimeBetweenQuestions)
        {
            return null;
        }

        // The most recent window first: that is the one the user remembers best.
        foreach (var window in windows)
        {
            if (window.TimeSpent < MinimumTimeBeforeAsking
                || _asked.Contains(window.Signature)
                || !labels.TryGetValue(window.Signature, out var label)
                || label.Source is LabelSource.User or LabelSource.Rule
                || label.Confidence >= setup.Options.ConfidenceThreshold)
            {
                continue;
            }

            var segment = window.Evidence.Segment;
            return new WindowQuestion(
                window.Signature,
                segment.ProcessName,
                segment.WindowTitle ?? "",
                label.Client is WindowClassifier.InternalLabel or WindowClassifier.NotWorkLabel ? null : label.Client,
                clients.Select(c => c.Name).ToList());
        }

        return null;
    }

    private sealed record Window(string Signature, SegmentEvidence Evidence, TimeSpan TimeSpent, DateTime LastSeenUtc);
}
