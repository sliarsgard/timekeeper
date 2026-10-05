using System.Text;

namespace Timekeeper.Core.Timesheets;

/// <summary>
/// Decides which client the work in a window is for, cheapest first: what the user said, keyword
/// rules, a remembered model answer, a client named on screen, the decision model, and only when
/// that is unsure the language model with a screenshot. Either model may be missing.
/// </summary>
public sealed class WindowClassifier
{
    public const string InternalLabel = "Internt";

    /// <summary>Not work at all, e.g. music or videos. Left out of the timesheet.</summary>
    public const string NotWorkLabel = "Ej arbete";

    // Jev is most accurate in English; option names stay as the user wrote them.
    private const string ClientQuestion =
        "Which client company of a Swedish accounting firm is the work in this window for? "
        + "Use what the user worked on just before and after as strong evidence when the window itself does not say. "
        + $"Choose \"{InternalLabel}\" if it is work but not for any specific client, "
        + $"and \"{NotWorkLabel}\" if it is not work at all, such as music, videos or private browsing.";

    private const int MaxOcrCharacters = 2000;

    // Programs like Fortnox show the company being worked on in their header, at the top of the screen.
    private const int HeaderLines = 12;

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
            .Append(NotWorkLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasModels => _decisionModel is not null || _languageModel is not null;

    /// <summary>Whether the name is one of the answers: a client, Internt or Ej arbete.</summary>
    public bool IsOption(string name) => _clientOptions.Contains(name, StringComparer.OrdinalIgnoreCase);

    public Task<WindowLabel> ClassifyAsync(SegmentEvidence evidence, WindowLabel? known, CancellationToken cancellationToken) =>
        ClassifyAsync(evidence, known, surroundings: null, cancellationToken);

    /// <param name="known">A previously stored label for the window or occasion, reused when still valid.</param>
    /// <param name="surroundings">What the user worked on just before and after, for the models.</param>
    public async Task<WindowLabel> ClassifyAsync(
        SegmentEvidence evidence,
        WindowLabel? known,
        string? surroundings,
        CancellationToken cancellationToken)
    {
        if (ClassifyWithoutModels(evidence, known) is { } label)
        {
            return label;
        }

        var segment = evidence.Segment;
        var signature = WindowSignature.Of(segment);
        var context = Describe(evidence, _matcher.FindIn(evidence.OcrText), surroundings);

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

        return new WindowLabel(signature, InternalLabel, 0, LabelSource.Guess);
    }

    /// <summary>
    /// The answer if it can be had without a model: what the user said, music or video, a client
    /// named in the window, a remembered model answer, or exactly one client named on screen.
    /// Null when a model would have to be asked.
    /// </summary>
    public WindowLabel? ClassifyWithoutModels(SegmentEvidence evidence, WindowLabel? known)
    {
        var segment = evidence.Segment;
        var signature = WindowSignature.Of(segment);

        // Labels for clients that have since been removed or renamed no longer apply.
        var stillValid = known is not null && IsOption(known.Client);
        if (stillValid && known!.Source == LabelSource.User)
        {
            return known;
        }

        if (WindowKinds.IsLeisure(segment.ProcessName, segment.Url))
        {
            return new WindowLabel(signature, NotWorkLabel, 0.9, LabelSource.Rule);
        }

        var inWindow = _matcher.FindIn(string.Join('\n', segment.WindowTitle, segment.Url, segment.DocumentPath));
        if (inWindow.Count == 1)
        {
            return new WindowLabel(signature, inWindow[0].Name, 0.95, LabelSource.Rule);
        }

        // The model saw the screen text too, and what was around it.
        if (stillValid && known!.Source == LabelSource.Model)
        {
            return known;
        }

        return OnScreen(signature, evidence.OcrText);
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

    /// <summary>
    /// One client named on screen is good evidence; in the header it usually names the company being
    /// worked on, further down it may just be mentioned (a supplier, an e-mail in a list).
    /// </summary>
    private WindowLabel? OnScreen(string signature, string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return null;
        }

        var header = _matcher.FindIn(string.Join('\n', ocrText.Split('\n').Take(HeaderLines)));
        if (header.Count == 1)
        {
            return new WindowLabel(signature, header[0].Name, 0.85, LabelSource.Screen);
        }

        var anywhere = _matcher.FindIn(ocrText);
        return anywhere.Count == 1 ? new WindowLabel(signature, anywhere[0].Name, 0.7, LabelSource.Screen) : null;
    }

    private static string Describe(SegmentEvidence evidence, IReadOnlyList<Client> mentionedClients, string? surroundings)
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

        if (!string.IsNullOrWhiteSpace(surroundings))
        {
            text.AppendLine();
            text.AppendLine(surroundings);
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
