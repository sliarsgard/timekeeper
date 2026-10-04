using Timekeeper.Core;

namespace Timekeeper.Tests;

public class SegmenterTests
{
    private static readonly DateTime T0 = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(20);

    private static ActivitySample Active(int seconds, string title, string process = "EXCEL") =>
        new(T0.AddSeconds(seconds), ActivityState.Active, ProcessName: process, WindowTitle: title);

    [Fact]
    public void Same_window_extends_the_current_segment()
    {
        var segmenter = new Segmenter(MaxGap);

        var first = segmenter.Add(Active(0, "Bokslut.xlsx"));
        var second = segmenter.Add(Active(5, "Bokslut.xlsx"));

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Same(first.Current, second.Current);
        Assert.Equal(TimeSpan.FromSeconds(5), second.Current.Duration);
    }

    [Fact]
    public void Switching_window_closes_the_previous_segment_where_the_next_begins()
    {
        var segmenter = new Segmenter(MaxGap);
        segmenter.Add(Active(0, "Bokslut.xlsx"));
        segmenter.Add(Active(5, "Bokslut.xlsx"));

        var change = segmenter.Add(Active(10, "Inkorg - Outlook", "olk"));

        Assert.True(change.Started);
        Assert.Equal(T0.AddSeconds(10), change.Closed!.EndUtc);
        Assert.Equal(T0.AddSeconds(10), change.Current.StartUtc);
        Assert.Equal("olk", change.Current.ProcessName);
    }

    [Fact]
    public void Different_url_in_the_same_browser_window_starts_a_new_segment()
    {
        var segmenter = new Segmenter(MaxGap);
        segmenter.Add(Active(0, "Fortnox") with { Url = "apps.fortnox.se/bokforing" });

        var change = segmenter.Add(Active(5, "Fortnox") with { Url = "apps.fortnox.se/lon" });

        Assert.True(change.Started);
    }

    [Fact]
    public void Idle_segment_starts_at_the_last_input_and_cuts_the_active_segment_short()
    {
        var segmenter = new Segmenter(MaxGap);
        for (var seconds = 0; seconds <= 400; seconds += 5)
        {
            segmenter.Add(Active(seconds, "Bokslut.xlsx"));
        }

        var change = segmenter.Add(new ActivitySample(T0.AddSeconds(405), ActivityState.Idle, TimeSpan.FromSeconds(300)));

        Assert.Equal(T0.AddSeconds(105), change.Closed!.EndUtc);
        Assert.Equal(T0.AddSeconds(105), change.Current.StartUtc);
        Assert.Equal(ActivityState.Idle, change.Current.State);
    }

    [Fact]
    public void Idle_start_is_never_before_the_segment_it_replaces()
    {
        var segmenter = new Segmenter(MaxGap);
        segmenter.Add(Active(0, "Bokslut.xlsx"));
        segmenter.Add(Active(5, "Ny flik"));

        var change = segmenter.Add(new ActivitySample(T0.AddSeconds(10), ActivityState.Idle, TimeSpan.FromMinutes(5)));

        Assert.Equal(T0.AddSeconds(5), change.Current.StartUtc);
        Assert.Equal(TimeSpan.Zero, change.Closed!.Duration);
    }

    [Fact]
    public void Idle_samples_with_different_windows_belong_to_the_same_segment()
    {
        var segmenter = new Segmenter(MaxGap);
        segmenter.Add(new ActivitySample(T0, ActivityState.Idle, TimeSpan.FromMinutes(5), "EXCEL", "A"));

        var change = segmenter.Add(new ActivitySample(T0.AddSeconds(5), ActivityState.Idle, TimeSpan.FromMinutes(5), "olk", "B"));

        Assert.False(change.Started);
    }

    [Fact]
    public void Gap_longer_than_the_limit_starts_a_new_segment_and_keeps_the_old_end()
    {
        var segmenter = new Segmenter(MaxGap);
        segmenter.Add(Active(0, "Bokslut.xlsx"));
        segmenter.Add(Active(5, "Bokslut.xlsx"));

        var change = segmenter.Add(Active(3600, "Bokslut.xlsx"));

        Assert.True(change.Started);
        Assert.Equal(T0.AddSeconds(5), change.Closed!.EndUtc);
        Assert.Equal(T0.AddSeconds(3600), change.Current.StartUtc);
    }

    [Fact]
    public void Close_ends_tracking_so_the_next_sample_starts_fresh()
    {
        var segmenter = new Segmenter(MaxGap);
        var first = segmenter.Add(Active(0, "Bokslut.xlsx"));

        var closed = segmenter.Close();
        var next = segmenter.Add(Active(5, "Bokslut.xlsx"));

        Assert.Same(first.Current, closed);
        Assert.True(next.Started);
        Assert.Null(next.Closed);
    }
}
