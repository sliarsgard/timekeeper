using Microsoft.Data.Sqlite;
using Timekeeper.Core;
using static Timekeeper.Data.Sql;

namespace Timekeeper.Data;

/// <summary>Stores activity in a local SQLite file. Safe to use from several threads.</summary>
public sealed class SqliteActivityStore : IActivityStore
{
    private const string Schema = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS segments (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            start_utc TEXT NOT NULL,
            end_utc TEXT NOT NULL,
            state TEXT NOT NULL,
            process TEXT,
            title TEXT,
            url TEXT,
            document TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_segments_start ON segments (start_utc);

        CREATE TABLE IF NOT EXISTS screenshots (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            segment_id INTEGER NOT NULL REFERENCES segments (id),
            taken_utc TEXT NOT NULL,
            path TEXT NOT NULL,
            ocr_text TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_screenshots_segment ON screenshots (segment_id);
        """;

    private readonly string _connectionString;

    public SqliteActivityStore(string databasePath)
    {
        _connectionString = ConnectionString(databasePath);
        using var connection = Open();
        Command(connection, Schema).ExecuteNonQuery();
    }

    public void InsertSegment(ActivitySegment segment)
    {
        using var connection = Open();
        var command = Command(
            connection,
            """
            INSERT INTO segments (start_utc, end_utc, state, process, title, url, document)
            VALUES ($start, $end, $state, $process, $title, $url, $document)
            RETURNING id
            """,
            ("$start", ToDb(segment.StartUtc)),
            ("$end", ToDb(segment.EndUtc)),
            ("$state", segment.State.ToString()),
            ("$process", segment.ProcessName),
            ("$title", segment.WindowTitle),
            ("$url", segment.Url),
            ("$document", segment.DocumentPath));
        segment.Id = (long)command.ExecuteScalar()!;
    }

    public void UpdateSegmentEnd(ActivitySegment segment)
    {
        using var connection = Open();
        Command(
                connection,
                "UPDATE segments SET end_utc = $end WHERE id = $id",
                ("$end", ToDb(segment.EndUtc)),
                ("$id", segment.Id))
            .ExecuteNonQuery();
    }

    public void UpdateSegment(ActivitySegment segment)
    {
        using var connection = Open();
        Command(
                connection,
                """
                UPDATE segments
                SET state = $state, process = $process, title = $title, url = $url, document = $document
                WHERE id = $id
                """,
                ("$state", segment.State.ToString()),
                ("$process", segment.ProcessName),
                ("$title", segment.WindowTitle),
                ("$url", segment.Url),
                ("$document", segment.DocumentPath),
                ("$id", segment.Id))
            .ExecuteNonQuery();
    }

    public IReadOnlyList<ActivitySegment> GetSegments(DateTime fromUtc, DateTime toUtc)
    {
        using var connection = Open();
        using var reader = Command(
                connection,
                """
                SELECT id, start_utc, end_utc, state, process, title, url, document
                FROM segments
                WHERE end_utc > $from AND start_utc < $to
                ORDER BY start_utc
                """,
                ("$from", ToDb(fromUtc)),
                ("$to", ToDb(toUtc)))
            .ExecuteReader();

        var segments = new List<ActivitySegment>();
        while (reader.Read())
        {
            segments.Add(new ActivitySegment
            {
                Id = reader.GetInt64(0),
                StartUtc = FromDb(reader.GetString(1)),
                EndUtc = FromDb(reader.GetString(2)),
                State = Enum.Parse<ActivityState>(reader.GetString(3)),
                ProcessName = GetNullableString(reader, 4),
                WindowTitle = GetNullableString(reader, 5),
                Url = GetNullableString(reader, 6),
                DocumentPath = GetNullableString(reader, 7),
            });
        }

        return segments;
    }

    public void InsertScreenshot(Screenshot screenshot)
    {
        using var connection = Open();
        var command = Command(
            connection,
            """
            INSERT INTO screenshots (segment_id, taken_utc, path, ocr_text)
            VALUES ($segment, $taken, $path, $ocr)
            RETURNING id
            """,
            ("$segment", screenshot.SegmentId),
            ("$taken", ToDb(screenshot.TakenUtc)),
            ("$path", screenshot.FilePath),
            ("$ocr", screenshot.OcrText));
        screenshot.Id = (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<Screenshot> GetScreenshots(long segmentId)
    {
        using var connection = Open();
        return ReadScreenshots(Command(
            connection,
            "SELECT id, segment_id, taken_utc, path, ocr_text FROM screenshots WHERE segment_id = $segment ORDER BY taken_utc",
            ("$segment", segmentId)));
    }

    public IReadOnlyList<Screenshot> GetScreenshots(DateTime fromUtc, DateTime toUtc)
    {
        using var connection = Open();
        return ReadScreenshots(Command(
            connection,
            "SELECT id, segment_id, taken_utc, path, ocr_text FROM screenshots WHERE taken_utc >= $from AND taken_utc < $to ORDER BY taken_utc",
            ("$from", ToDb(fromUtc)),
            ("$to", ToDb(toUtc))));
    }

    public IReadOnlyList<Screenshot> DeleteScreenshotsTakenBefore(DateTime cutoffUtc)
    {
        using var connection = Open();
        return ReadScreenshots(Command(
            connection,
            "DELETE FROM screenshots WHERE taken_utc < $cutoff RETURNING id, segment_id, taken_utc, path, ocr_text",
            ("$cutoff", ToDb(cutoffUtc))));
    }

    /// <summary>Points stored screenshot paths at a folder the files have been moved to.</summary>
    public void RelocateScreenshots(string oldDirectory, string newDirectory)
    {
        using var connection = Open();
        Command(
                connection,
                """
                UPDATE screenshots SET path = $new || substr(path, length($old) + 1)
                WHERE substr(path, 1, length($old)) = $old
                """,
                ("$old", oldDirectory),
                ("$new", newDirectory))
            .ExecuteNonQuery();
    }

    private static List<Screenshot> ReadScreenshots(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var screenshots = new List<Screenshot>();
        while (reader.Read())
        {
            screenshots.Add(new Screenshot
            {
                Id = reader.GetInt64(0),
                SegmentId = reader.GetInt64(1),
                TakenUtc = FromDb(reader.GetString(2)),
                FilePath = reader.GetString(3),
                OcrText = GetNullableString(reader, 4),
            });
        }

        return screenshots;
    }

    private SqliteConnection Open() => Sql.Open(_connectionString);
}
