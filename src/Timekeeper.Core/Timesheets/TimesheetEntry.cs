namespace Timekeeper.Core.Timesheets;

/// <summary>One row of the timesheet: time spent on a client during a day.</summary>
public sealed class TimesheetEntry
{
    public long Id { get; set; }
    public required DateOnly Date { get; init; }
    public required string Client { get; set; }
    public required string Activity { get; set; }
    public required int Minutes { get; set; }
    public string Comment { get; set; } = "";

    /// <summary>How sure the classification was, 0–1. Null for rows the user added.</summary>
    public double? Confidence { get; set; }
}
