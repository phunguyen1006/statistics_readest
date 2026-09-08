using Microsoft.Data.Sqlite;

namespace ReadestStats.Core;

public sealed class SqliteReadestRepository : IReadestRepository
{
    private static readonly int[] RetryDelays = [0, 200, 500, 1000];
    public string DatabasePath { get; }

    public SqliteReadestRepository(string databasePath) => DatabasePath = Path.GetFullPath(databasePath);

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared, Pooling = false, DefaultTimeout = 2 };
        return new SqliteConnection(builder.ToString());
    }

    private async Task<T> WithRetryAsync<T>(Func<SqliteConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var delay in RetryDelays)
        {
            if (delay > 0) await Task.Delay(delay, cancellationToken);
            try
            {
                await using var connection = CreateConnection();
                await connection.OpenAsync(cancellationToken);
                await using var guard = connection.CreateCommand();
                guard.CommandText = "PRAGMA query_only = ON";
                await guard.ExecuteNonQueryAsync(cancellationToken);
                return await action(connection);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { last = ex; }
        }
        throw new IOException("Readest is busy. Please wait a moment and try again.", last);
    }

    public Task<SchemaValidation> ValidateAsync(CancellationToken cancellationToken = default) => WithRetryAsync<SchemaValidation>(async connection =>
    {
        if (!File.Exists(DatabasePath)) return new(false, "The selected database no longer exists.");
        var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view') ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
        }
        if (!names.Contains("book") || !names.Contains("page_stat_data")) return new(false, "This is not a compatible Readest statistics database.");
        var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["book"] = ["id", "title", "authors"],
            ["page_stat_data"] = ["id_book", "page", "start_time", "duration", "total_pages"]
        };
        foreach (var table in required)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table.Key})";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
            if (table.Value.Any(c => !columns.Contains(c))) return new(false, $"Table {table.Key} does not contain the required columns.");
        }
        var (books, events, first, latest, version) = await ReadDiagnosticsAsync(connection, cancellationToken);
        return new(true, "Connected to Readest", new(DatabasePath, new FileInfo(DatabasePath).Length, books, events, first, latest, "Connected to Readest", version, names));
    }, cancellationToken);

    private static async Task<(int Books, int Events, DateTimeOffset? First, DateTimeOffset? Latest, int Version)> ReadDiagnosticsAsync(SqliteConnection connection, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM book), COUNT(*), MIN(start_time), MAX(start_time), (SELECT user_version FROM pragma_user_version) FROM page_stat_data";
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        DateTimeOffset? Convert(int ordinal) => reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(ordinal));
        return (reader.GetInt32(0), reader.GetInt32(1), Convert(2), Convert(3), reader.GetInt32(4));
    }

    public Task<IReadOnlyList<Book>> GetBooksAsync(CancellationToken cancellationToken = default) => WithRetryAsync<IReadOnlyList<Book>>(async connection =>
    {
        var result = new List<Book>();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(book)";
            await using var schemaReader = await schema.ExecuteReaderAsync(cancellationToken);
            while (await schemaReader.ReadAsync(cancellationToken)) columns.Add(schemaReader.GetString(1));
        }
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, COALESCE(title,''), COALESCE(authors,''), last_open, pages, series, language, {(columns.Contains("md5") ? "md5" : "NULL")} FROM book ORDER BY title COLLATE NOCASE";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<ReadingEvent>> GetEventsAsync(DateTimeOffset? start = null, DateTimeOffset? end = null, long? bookId = null, CancellationToken cancellationToken = default) => WithRetryAsync<IReadOnlyList<ReadingEvent>>(async connection =>
    {
        var conditions = new List<string> { "duration > 0" };
        await using var command = connection.CreateCommand();
        if (start is not null) { conditions.Add("start_time >= @start"); command.Parameters.AddWithValue("@start", start.Value.ToUnixTimeSeconds()); }
        if (end is not null) { conditions.Add("start_time < @end"); command.Parameters.AddWithValue("@end", end.Value.ToUnixTimeSeconds()); }
        if (bookId is not null) { conditions.Add("id_book = @book"); command.Parameters.AddWithValue("@book", bookId.Value); }
        command.CommandText = $"SELECT id_book, page, start_time, duration, total_pages FROM page_stat_data WHERE {string.Join(" AND ", conditions)} ORDER BY start_time";
        var result = new List<ReadingEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetDouble(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        return result;
    }, cancellationToken);
}
