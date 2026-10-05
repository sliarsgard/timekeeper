namespace Timekeeper.Core.Timesheets;

/// <summary>Identifies "the same window": same program, title, address and document.</summary>
public static class WindowSignature
{
    public static string Of(ActivitySegment segment) =>
        string.Join('\u001f', segment.ProcessName, segment.WindowTitle, segment.Url, segment.DocumentPath);
}

public enum LabelSource
{
    /// <summary>No model was available; a cheap guess that should be replaced once one is.</summary>
    Guess,

    /// <summary>A client's name or keyword appears in the window title, address or document.</summary>
    Rule,

    /// <summary>Decided by Jev or Luna.</summary>
    Model,

    /// <summary>The user said so. Always wins.</summary>
    User,
}

/// <summary>Which client the work in a window is for.</summary>
public sealed record WindowLabel(string Signature, string Client, double Confidence, LabelSource Source)
{
    public Decision ToDecision() => new(Client, Confidence);
}

/// <summary>Remembers window classifications so each window is only sent to a model once.</summary>
public interface ILabelStore
{
    IReadOnlyDictionary<string, WindowLabel> GetLabels();

    /// <summary>Inserts or replaces the label for the label's signature.</summary>
    void SaveLabel(WindowLabel label);
}
