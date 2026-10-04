using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Timekeeper.App.Controls;
using Timekeeper.Core;

namespace Timekeeper.App.ViewModels;

public sealed record LegendItem(string Label, IBrush Brush, string Duration);

/// <summary>The raw record of a day: what was on screen, when, and for how long.</summary>
public sealed partial class ActivityPageViewModel : PageViewModel
{
    private static readonly TimeSpan WorkdayStart = TimeSpan.FromHours(8);
    private static readonly TimeSpan WorkdayEnd = TimeSpan.FromHours(17);

    private readonly IActivityStore _store;
    private readonly Dictionary<long, SegmentRowViewModel> _rowsById = [];
    private ProgramPalette _palette = new();

    [ObservableProperty]
    private SegmentRowViewModel? _selectedRow;

    [ObservableProperty]
    private SegmentDetailViewModel? _detail;

    [ObservableProperty]
    private IReadOnlyList<TimelineItem> _timeline = [];

    [ObservableProperty]
    private IReadOnlyList<LegendItem> _legend = [];

    [ObservableProperty]
    private DateTime _rangeStart;

    [ObservableProperty]
    private DateTime _rangeEnd;

    [ObservableProperty]
    private string _activeTime = "";

    [ObservableProperty]
    private string _awayTime = "";

    [ObservableProperty]
    private string _firstActivity = "–";

    [ObservableProperty]
    private string _switches = "0";

    public ActivityPageViewModel(IActivityStore store, DaySelection day)
        : base("Aktivitet", "")
    {
        _store = store;
        Day = day;
        Day.PropertyChanged += OnDayChanged;
        Refresh();
    }

    public DaySelection Day { get; }

    /// <summary>Newest first, so the live end of the day is at the top.</summary>
    public ObservableCollection<SegmentRowViewModel> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public void OnSampled()
    {
        if (Day.IsToday)
        {
            Refresh();
        }
    }

    [RelayCommand]
    private void CloseDetail() => SelectedRow = null;

    partial void OnSelectedRowChanged(SegmentRowViewModel? value)
    {
        Detail?.Dispose();
        Detail = value is null ? null : new SegmentDetailViewModel(value, _store);
    }

    private void OnDayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DaySelection.Date))
        {
            return;
        }

        SelectedRow = null;
        Rows.Clear();
        _rowsById.Clear();
        _palette = new ProgramPalette();
        Refresh();
    }

    private void Refresh()
    {
        var segments = _store.GetSegments(Day.StartUtc, Day.EndUtc);

        foreach (var segment in segments)
        {
            if (_rowsById.TryGetValue(segment.Id, out var row))
            {
                row.Update(segment);
                continue;
            }

            // Away rows are drawn in their own colour and must not use up a palette slot.
            var brush = segment.State == ActivityState.Active
                ? _palette.BrushFor(ProgramNames.Friendly(segment.ProcessName))
                : Brushes.Transparent;
            row = new SegmentRowViewModel(segment, brush);
            _rowsById.Add(segment.Id, row);
            Rows.Insert(0, row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        UpdateSummary(segments);
        UpdateTimeline(segments);
    }

    private void UpdateSummary(IReadOnlyList<ActivitySegment> segments)
    {
        var active = segments.Where(s => s.State == ActivityState.Active).ToList();
        ActiveTime = Format.Duration(Total(active));
        AwayTime = Format.Duration(Total(segments.Where(s => s.State != ActivityState.Active)));
        FirstActivity = active.Count > 0 ? Format.Time(active[0].StartUtc) : "–";
        Switches = active.Count.ToString(Format.Swedish);
    }

    private void UpdateTimeline(IReadOnlyList<ActivitySegment> segments)
    {
        // Back-to-back segments in the same program read as one block.
        var blocks = new List<(string Program, DateTime Start, DateTime End)>();
        foreach (var segment in segments.Where(s => s.State == ActivityState.Active))
        {
            var program = ProgramNames.Friendly(segment.ProcessName);
            var blockStart = segment.StartUtc.ToLocalTime();
            var blockEnd = segment.EndUtc.ToLocalTime();
            if (blocks.Count > 0 && blocks[^1].Program == program && blockStart - blocks[^1].End < TimeSpan.FromSeconds(30))
            {
                blocks[^1] = blocks[^1] with { End = blockEnd };
            }
            else
            {
                blocks.Add((program, blockStart, blockEnd));
            }
        }

        Timeline = blocks
            .Select(b => new TimelineItem(
                b.Start,
                b.End,
                _palette.BrushFor(b.Program),
                $"{b.Program} · {b.Start:HH:mm}–{b.End:HH:mm} · {Format.Duration(b.End - b.Start)}"))
            .ToList();

        Legend = blocks
            .GroupBy(b => _palette.LegendLabel(b.Program))
            .Select(g => new LegendItem(
                g.Key,
                _palette.BrushFor(g.First().Program),
                Format.Duration(g.Aggregate(TimeSpan.Zero, (sum, b) => sum + (b.End - b.Start)))))
            .ToList();

        // Show at least office hours, widened to whatever was actually recorded.
        var start = Day.Date + WorkdayStart;
        var end = Day.Date + WorkdayEnd;
        if (segments.Count > 0)
        {
            var first = segments[0].StartUtc.ToLocalTime();
            var last = segments[^1].EndUtc.ToLocalTime();
            start = first < start ? first.Date.AddHours(first.Hour) : start;
            end = last > end ? last.Date.AddHours(last.Hour + 1) : end;
        }

        RangeStart = start;
        RangeEnd = end;
    }

    private static TimeSpan Total(IEnumerable<ActivitySegment> segments) =>
        segments.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Duration);
}
