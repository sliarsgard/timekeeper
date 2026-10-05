using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Timekeeper.Core;

namespace Timekeeper.App.ViewModels;

public sealed partial class SegmentRowViewModel(ActivitySegment segment, IBrush brush) : ObservableObject
{
    /// <summary>The client the window was classified as, once it has been.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClient))]
    private string? _client;

    private static readonly IBrush AwayBrush = new ImmutableSolidColorBrush(Color.Parse("#3A4152"));

    public ActivitySegment Segment { get; private set; } = segment;

    public long Id => Segment.Id;

    public bool IsAway => Segment.State != ActivityState.Active;

    public IBrush Brush => IsAway ? AwayBrush : brush;

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
