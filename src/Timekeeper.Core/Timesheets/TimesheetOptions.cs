namespace Timekeeper.Core.Timesheets;

public sealed record TimesheetOptions
{
    public IReadOnlyList<string> Activities { get; init; } = ["Löpande bokföring", "Bokslut", "Konsult"];

    /// <summary>Used when no model is available to pick an activity.</summary>
    public string DefaultActivity { get; init; } = "Löpande bokföring";

    public int RoundingMinutes { get; init; } = 15;

    /// <summary>Round up to the next step, as for a started quarter, rather than to the nearest.</summary>
    public bool RoundUp { get; init; } = true;

    /// <summary>
    /// Less time than this for a client during the day is left out, so a glance at a client's
    /// file does not become a whole rounded-up step.
    /// </summary>
    public int MinimumMinutes { get; init; } = 3;

    /// <summary>Classifications below this confidence are escalated, and flagged for review.</summary>
    public double ConfidenceThreshold { get; init; } = 0.75;

    /// <summary>Shorter detours between two blocks for the same client count towards that client.</summary>
    public TimeSpan InterruptionThreshold { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Whether uncertain classifications may send a screenshot to the language model.</summary>
    public bool SendScreenshots { get; init; } = true;
}
