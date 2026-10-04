namespace Timekeeper.Core.Timesheets;

/// <summary>A model's pick among a fixed set of options.</summary>
/// <param name="Confidence">Probability of the pick, 0–1.</param>
public sealed record Decision(string Choice, double Confidence);

/// <summary>A model that only makes typed decisions (Jev). Cheap and fast, but cannot write text or see images.</summary>
public interface IDecisionModel
{
    Task<Decision> ChooseAsync(string question, string context, IReadOnlyList<string> options, CancellationToken cancellationToken);
}

/// <summary>A general model that can read screenshots and write text (Luna).</summary>
public interface ILanguageModel
{
    Task<Decision> ChooseAsync(
        string question,
        string context,
        IReadOnlyList<string> options,
        string? imagePath,
        CancellationToken cancellationToken);

    Task<string> WriteCommentAsync(string context, CancellationToken cancellationToken);
}
