using System.Text;

namespace Timekeeper.Core.Timesheets;

/// <summary>
/// Decides which client the work in a window is for, cheapest first: what the user said, keyword
/// rules, a remembered model answer, the decision model, and only when that is unsure the language
/// model with a screenshot. Either model may be missing.
/// </summary>
public sealed class WindowClassifier
{
    public const string InternalLabel = "Internt";

    // Jev is most accurate in English; option names stay as the user wrote them.
    private const string ClientQuestion =
        "Which client company of a Swedish accounting firm is the work in this window for? "
        + $"Choose \"{InternalLabel}\" if it is not for any specific client.";

    private const int MaxOcrCharacters = 2000;

    private readonly TimesheetOptions _options;
    private readonly IDecisionModel? _decisionModel;
    private readonly ILanguageModel? _languageModel;
    private readonly ClientMatcher _matcher;
    private readonly List<string> _clientOptions;

    public WindowClassifier(
        TimesheetOptions options,
        IDecisionModel? decisionModel,
        ILanguageModel? languageModel,
        IReadOnlyList<Client> clients)
    {
        _options = options;
        _decisionModel = decisionModel;
        _languageModel = languageModel;
        _matcher = new ClientMatcher(clients);
        _clientOptions = clients
            .Select(c => c.Name)
            .Append(InternalLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasModels => _decisionModel is not null || _languageModel is not null;

    /// <param name="known">A previously stored label for the window, reused when still valid.</param>
    public async Task<WindowLabel> ClassifyAsync(SegmentEvidence evidence, WindowLabel? known, CancellationToken cancellationToken)
    {
        var segment = evidence.Segment;
        var signature = WindowSignature.Of(segment);

        // Labels for clients that have since been removed or renamed no longer apply.
        var stillValid = known is not null && _clientOptions.Contains(known.Client, StringComparer.OrdinalIgnoreCase);
        if (stillValid && known!.Source == LabelSource.User)
        {
            return known;
        }

        var strongMatches = _matcher.FindIn(string.Join('\n', segment.WindowTitle, segment.Url, segment.DocumentPath));
        if (strongMatches.Count == 1)
        {
            return new WindowLabel(signature, strongMatches[0].Name, 0.95, LabelSource.Rule);
        }

        if (stillValid && known!.Source == LabelSource.Model)
        {
            return known;
        }

        var ocrMatches = _matcher.FindIn(evidence.OcrText);
        var context = Describe(evidence, ocrMatches);

        Decision? decision = null;
        if (_decisionModel is not null)
        {
            decision = Normalize(await _decisionModel.ChooseAsync(ClientQuestion, context, _clientOptions, cancellationToken), _clientOptions);
            if (decision.Confidence >= _options.ConfidenceThreshold || _languageModel is null)
            {
                return new WindowLabel(signature, decision.Choice, decision.Confidence, LabelSource.Model);
            }
        }

        if (_languageModel is not null)
        {
            var image = _options.SendScreenshots ? evidence.ScreenshotPath : null;
            decision = Normalize(
                await _languageModel.ChooseAsync(ClientQuestion, context, _clientOptions, image, cancellationToken),
                _clientOptions);
            return new WindowLabel(signature, decision.Choice, decision.Confidence, LabelSource.Model);
        }

        // Without models, a client named on screen is a reasonable but unsure guess.
        return ocrMatches.Count == 1
            ? new WindowLabel(signature, ocrMatches[0].Name, 0.5, LabelSource.Guess)
            : new WindowLabel(signature, InternalLabel, 0, LabelSource.Guess);
    }

    /// <summary>Maps a free-form answer onto the option list; anything unrecognised is not a decision.</summary>
    internal static Decision Normalize(Decision decision, IReadOnlyList<string> optionList)
    {
        var match = optionList.FirstOrDefault(o => string.Equals(o, decision.Choice.Trim(), StringComparison.OrdinalIgnoreCase));
        return match is null
            ? new Decision(optionList.Contains(InternalLabel) ? InternalLabel : optionList[0], 0)
            : decision with { Choice = match };
    }

    internal static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";

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

    private static void AppendIfPresent(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.AppendLine($"{label}: {value}");
        }
    }
}
