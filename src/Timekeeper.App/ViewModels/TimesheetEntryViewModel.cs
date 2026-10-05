using CommunityToolkit.Mvvm.ComponentModel;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.ViewModels;

/// <summary>An editable timesheet row. Every edit is saved straight away.</summary>
public sealed partial class TimesheetEntryViewModel : ObservableObject
{
    private readonly Action<TimesheetEntryViewModel> _save;

    [ObservableProperty]
    private string _client;

    [ObservableProperty]
    private string _activity;

    [ObservableProperty]
    private decimal? _hours;

    [ObservableProperty]
    private string _comment;

    public TimesheetEntryViewModel(TimesheetEntry entry, double confidenceThreshold, Action<TimesheetEntryViewModel> save)
    {
        Entry = entry;
        _save = save;
        _client = entry.Client;
        _activity = entry.Activity;
        _hours = entry.Minutes / 60m;
        _comment = entry.Comment;
        IsUncertain = entry.Confidence < confidenceThreshold;
    }

    public TimesheetEntry Entry { get; }

    public bool IsUncertain { get; }

    /// <summary>The time actually recorded, e.g. "1 h 12 min"; empty for rows added by hand.</summary>
    public string Recorded => Entry.RecordedMinutes is { } minutes ? Format.Duration(TimeSpan.FromMinutes(minutes)) : "";

    public string ConfidenceTooltip => Entry.Confidence is { } confidence
        ? $"AI:n var {confidence:P0} säker. Kontrollera kund och tid."
        : "";

    partial void OnClientChanged(string value) => Save(() => Entry.Client = value.Trim());

    partial void OnActivityChanged(string value) => Save(() => Entry.Activity = value);

    partial void OnHoursChanged(decimal? value) => Save(() => Entry.Minutes = (int)Math.Round((value ?? 0) * 60));

    partial void OnCommentChanged(string value) => Save(() => Entry.Comment = value);

    private void Save(Action apply)
    {
        apply();
        _save(this);
    }
}
