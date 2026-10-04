using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Timekeeper.Data;

internal static class Sql
{
    public static SqliteConnection Open(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public static string ConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    public static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // Round-trip format in UTC sorts correctly as text, so range queries can compare strings.
    public static string ToDb(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static string ToDb(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
