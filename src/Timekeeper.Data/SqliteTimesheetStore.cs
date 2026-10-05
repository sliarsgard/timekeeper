using Microsoft.Data.Sqlite;
using Timekeeper.Core.Timesheets;
using static Timekeeper.Data.Sql;

namespace Timekeeper.Data;

/// <summary>Stores clients and timesheets in the same SQLite file as the activity log.</summary>
public sealed class SqliteTimesheetStore : ITimesheetStore, ILabelStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS clients (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            keywords TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS timesheet_entries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT NOT NULL,
            client TEXT NOT NULL,
            activity TEXT NOT NULL,
            minutes INTEGER NOT NULL,
            comment TEXT NOT NULL,
            confidence REAL
        );
        CREATE INDEX IF NOT EXISTS ix_timesheet_entries_date ON timesheet_entries (date);

        CREATE TABLE IF NOT EXISTS window_labels (
            signature TEXT PRIMARY KEY,
            client TEXT NOT NULL,
            confidence REAL NOT NULL,
            source TEXT NOT NULL
        );
        """;

    // Keywords are kept as one line of text, the way the user types them.
    private const char KeywordSeparator = ',';

    private readonly string _connectionString;

    public SqliteTimesheetStore(string databasePath)
    {
        _connectionString = ConnectionString(databasePath);
        using var connection = Open();
        Command(connection, Schema).ExecuteNonQuery();
    }

    public IReadOnlyList<Client> GetClients()
    {
        using var connection = Open();
        using var reader = Command(connection, "SELECT id, name, keywords FROM clients ORDER BY name COLLATE NOCASE").ExecuteReader();
        var clients = new List<Client>();
        while (reader.Read())
        {
            var keywords = reader.GetString(2)
                .Split(KeywordSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            clients.Add(new Client(reader.GetInt64(0), reader.GetString(1), keywords));
        }

        return clients;
    }

    public Client SaveClient(Client client)
    {
        using var connection = Open();
        var keywords = string.Join($"{KeywordSeparator} ", client.Keywords);
        if (client.Id != 0)
        {
            Command(
                    connection,
                    "UPDATE clients SET name = $name, keywords = $keywords WHERE id = $id",
                    ("$name", client.Name),
                    ("$keywords", keywords),
                    ("$id", client.Id))
                .ExecuteNonQuery();
            return client;
        }

        var id = (long)Command(
                connection,
                "INSERT INTO clients (name, keywords) VALUES ($name, $keywords) RETURNING id",
                ("$name", client.Name),
                ("$keywords", keywords))
            .ExecuteScalar()!;
        return client with { Id = id };
    }

    public void DeleteClient(long id)
    {
        using var connection = Open();
        Command(connection, "DELETE FROM clients WHERE id = $id", ("$id", id)).ExecuteNonQuery();
    }

    public IReadOnlyList<TimesheetEntry> GetEntries(DateOnly date)
    {
        using var connection = Open();
        using var reader = Command(
                connection,
                "SELECT id, client, activity, minutes, comment, confidence FROM timesheet_entries WHERE date = $date ORDER BY id",
                ("$date", ToDb(date)))
            .ExecuteReader();

        var entries = new List<TimesheetEntry>();
        while (reader.Read())
        {
            entries.Add(new TimesheetEntry
            {
                Id = reader.GetInt64(0),
                Date = date,
                Client = reader.GetString(1),
                Activity = reader.GetString(2),
                Minutes = reader.GetInt32(3),
                Comment = reader.GetString(4),
                Confidence = reader.IsDBNull(5) ? null : reader.GetDouble(5),
            });
        }

        return entries;
    }

    public void ReplaceEntries(DateOnly date, IReadOnlyList<TimesheetEntry> entries)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Command(connection, "DELETE FROM timesheet_entries WHERE date = $date", ("$date", ToDb(date))).ExecuteNonQuery();
        foreach (var entry in entries)
        {
            Insert(connection, entry);
        }

        transaction.Commit();
    }

    public void SaveEntry(TimesheetEntry entry)
    {
        using var connection = Open();
        if (entry.Id == 0)
        {
            Insert(connection, entry);
            return;
        }

        Command(
                connection,
                """
                UPDATE timesheet_entries
                SET client = $client, activity = $activity, minutes = $minutes, comment = $comment, confidence = $confidence
                WHERE id = $id
                """,
                ("$client", entry.Client),
                ("$activity", entry.Activity),
                ("$minutes", entry.Minutes),
                ("$comment", entry.Comment),
                ("$confidence", entry.Confidence),
                ("$id", entry.Id))
            .ExecuteNonQuery();
    }

    public void DeleteEntry(long id)
    {
        using var connection = Open();
        Command(connection, "DELETE FROM timesheet_entries WHERE id = $id", ("$id", id)).ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, WindowLabel> GetLabels()
    {
        using var connection = Open();
        using var reader = Command(connection, "SELECT signature, client, confidence, source FROM window_labels").ExecuteReader();
        var labels = new Dictionary<string, WindowLabel>();
        while (reader.Read())
        {
            var label = new WindowLabel(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                Enum.Parse<LabelSource>(reader.GetString(3)));
            labels[label.Signature] = label;
        }

        return labels;
    }

    public void SaveLabel(WindowLabel label)
    {
        using var connection = Open();
        Command(
                connection,
                """
                INSERT INTO window_labels (signature, client, confidence, source)
                VALUES ($signature, $client, $confidence, $source)
                ON CONFLICT (signature) DO UPDATE
                SET client = excluded.client, confidence = excluded.confidence, source = excluded.source
                """,
                ("$signature", label.Signature),
                ("$client", label.Client),
                ("$confidence", label.Confidence),
                ("$source", label.Source.ToString()))
            .ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection connection, TimesheetEntry entry)
    {
        entry.Id = (long)Command(
                connection,
                """
                INSERT INTO timesheet_entries (date, client, activity, minutes, comment, confidence)
                VALUES ($date, $client, $activity, $minutes, $comment, $confidence)
                RETURNING id
                """,
                ("$date", ToDb(entry.Date)),
                ("$client", entry.Client),
                ("$activity", entry.Activity),
                ("$minutes", entry.Minutes),
                ("$comment", entry.Comment),
                ("$confidence", entry.Confidence))
            .ExecuteScalar()!;
    }

    private SqliteConnection Open() => Sql.Open(_connectionString);
}
