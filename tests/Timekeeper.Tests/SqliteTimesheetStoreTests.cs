using Microsoft.Data.Sqlite;
using Timekeeper.Core.Timesheets;
using Timekeeper.Data;

namespace Timekeeper.Tests;

public sealed class SqliteTimesheetStoreTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 5);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"timekeeper-{Guid.NewGuid()}.db");
    private readonly SqliteTimesheetStore _store;

    public SqliteTimesheetStoreTests() => _store = new SqliteTimesheetStore(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    private static TimesheetEntry Entry(string client, int minutes) =>
        new() { Date = Day, Client = client, Activity = "Löpande bokföring", Minutes = minutes, Comment = "Bokföring", Confidence = 0.9 };

    [Fact]
    public void Clients_round_trip_with_keywords_and_can_be_renamed()
    {
        var saved = _store.SaveClient(new Client(0, "Bageriet i Lund AB", ["Bageriet", "556677-8899"]));
        _store.SaveClient(saved with { Name = "Bageriet Lund AB" });

        var client = Assert.Single(_store.GetClients());

        Assert.Equal(saved.Id, client.Id);
        Assert.Equal("Bageriet Lund AB", client.Name);
        Assert.Equal(["Bageriet", "556677-8899"], client.Keywords);
    }

    [Fact]
    public void ReplaceEntries_swaps_the_whole_day_and_leaves_other_days()
    {
        var otherDay = new TimesheetEntry { Date = Day.AddDays(1), Client = "Annan", Activity = "Bokslut", Minutes = 30 };
        _store.SaveEntry(otherDay);
        _store.ReplaceEntries(Day, [Entry("Gammal", 15)]);

        _store.ReplaceEntries(Day, [Entry("A", 60), Entry("B", 30)]);

        Assert.Equal(["A", "B"], _store.GetEntries(Day).Select(e => e.Client));
        Assert.Single(_store.GetEntries(Day.AddDays(1)));
    }

    [Fact]
    public void SaveEntry_updates_an_existing_entry()
    {
        var entry = Entry("A", 60);
        _store.SaveEntry(entry);

        entry.Minutes = 45;
        entry.Comment = "Avstämning";
        entry.Confidence = null;
        _store.SaveEntry(entry);

        var loaded = Assert.Single(_store.GetEntries(Day));
        Assert.Equal(45, loaded.Minutes);
        Assert.Equal("Avstämning", loaded.Comment);
        Assert.Null(loaded.Confidence);
    }
}
