using Microsoft.Data.Sqlite;
using Timekeeper.Core;
using Timekeeper.Data;

namespace Timekeeper.Tests;

public sealed class SqliteActivityStoreTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"timekeeper-{Guid.NewGuid()}.db");
    private readonly SqliteActivityStore _store;

    public SqliteActivityStoreTests() => _store = new SqliteActivityStore(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private ActivitySegment Insert(DateTime start, DateTime end, string title = "Bokslut.xlsx")
    {
        var segment = new ActivitySegment
        {
            StartUtc = start,
            EndUtc = end,
            State = ActivityState.Active,
            ProcessName = "EXCEL",
            WindowTitle = title,
            DocumentPath = "https://byran.sharepoint.com/Kunder/Bolag AB/Bokslut.xlsx",
        };
        _store.InsertSegment(segment);
        return segment;
    }

    [Fact]
    public void Inserted_segment_round_trips_with_updated_end()
    {
        var segment = Insert(T0, T0.AddMinutes(1));
        segment.EndUtc = T0.AddMinutes(7);
        _store.UpdateSegmentEnd(segment);

        var loaded = Assert.Single(_store.GetSegments(T0, T0.AddHours(1)));

        Assert.Equal(segment.Id, loaded.Id);
        Assert.Equal(T0, loaded.StartUtc);
        Assert.Equal(T0.AddMinutes(7), loaded.EndUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.StartUtc.Kind);
        Assert.Equal(segment.DocumentPath, loaded.DocumentPath);
        Assert.Null(loaded.Url);
    }

    [Fact]
    public void GetSegments_returns_segments_overlapping_the_interval_in_order()
    {
        Insert(T0.AddHours(-2), T0.AddHours(-1), "före");
        Insert(T0.AddMinutes(30), T0.AddMinutes(40), "senare");
        Insert(T0.AddMinutes(-5), T0.AddMinutes(5), "överlappar");
        Insert(T0.AddHours(2), T0.AddHours(3), "efter");

        var titles = _store.GetSegments(T0, T0.AddHours(1)).Select(s => s.WindowTitle);

        Assert.Equal(["överlappar", "senare"], titles);
    }

    [Fact]
    public void DeleteScreenshotsTakenBefore_removes_and_returns_only_old_screenshots()
    {
        var segment = Insert(T0, T0.AddMinutes(10));
        _store.InsertScreenshot(new Screenshot { SegmentId = segment.Id, TakenUtc = T0, FilePath = "old.jpg", OcrText = "Bolag AB" });
        _store.InsertScreenshot(new Screenshot { SegmentId = segment.Id, TakenUtc = T0.AddMinutes(5), FilePath = "new.jpg" });

        var deleted = _store.DeleteScreenshotsTakenBefore(T0.AddMinutes(1));

        Assert.Equal("old.jpg", Assert.Single(deleted).FilePath);
        Assert.Equal("new.jpg", Assert.Single(_store.GetScreenshots(segment.Id)).FilePath);
    }
}
