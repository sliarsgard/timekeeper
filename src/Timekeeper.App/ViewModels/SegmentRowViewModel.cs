using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Timekeeper.Core;
using Timekeeper.Core.Timesheets;

namespace Timekeeper.App.ViewModels;

public sealed partial class SegmentRowViewModel(ActivitySegment segment, IBrush brush) : ObservableObject
{
    /// <summary>The client the time was attributed to, once it has been.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClient))]
    private string? _client;

    /// <summary>Why the time went to that client, e.g. "Kundnamn på skärmen · 85 %".</summary>
    [ObservableProperty]
    private string _reason = "";

    public ActivitySegment Segment { get; private set; } = segment;

    public long Id => Segment.Id;

    public bool IsAway => Segment.State != ActivityState.Active;

    public IBrush Brush => IsAway ? ProgramPalette.Away : brush;

    public string Program => Segment.State switch
    {
        ActivityState.Idle => "Inaktiv",
        ActivityState.Locked => "Låst",
        _ => ProgramNames.Friendly(Segment.ProcessName),
    };

    public string Title => Segment.State switch
    {
        ActivityState.Idle => "Ingen aktivitet vid datorn",
        ActivityState.Locked => "Datorn var låst",
        _ => string.IsNullOrWhiteSpace(Segment.WindowTitle) ? "(fönster utan titel)" : Segment.WindowTitle,
    };

    public string? Detail => IsAway ? null : Segment.DocumentPath ?? Segment.Url;

    public bool HasDetail => Detail is not null;

    public bool HasClient => Client is not null;

    public void SetAttribution(AttributedSegment? attribution)
    {
        Client = attribution is { IsKnown: true } ? attribution.Client : null;
        Reason = attribution switch
        {
            null => "",
            { IsKnown: false } => Reasons.Describe(attribution.Reason),
            _ => $"{Reasons.Describe(attribution.Reason)} · {attribution.Confidence:P0}",
        };
    }

    public string TimeRange => $"{Format.Time(Segment.StartUtc)}–{Format.Time(Segment.EndUtc)}";

    public string Duration => Format.Duration(Segment.Duration);

    /// <summary>Picks up a new end time while the segment is still growing.</summary>
    public void Update(ActivitySegment segment)
    {
        Segment = segment;
        OnPropertyChanged(nameof(TimeRange));
        OnPropertyChanged(nameof(Duration));
    }
}
