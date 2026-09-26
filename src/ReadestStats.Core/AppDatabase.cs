using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReadestStats.Core;

public sealed record AppDatabaseHealth(int SchemaVersion, int Books, int Sessions, bool HasActiveSession, string IntegrityStatus, string Path);
public sealed record OperationJournalEntry(long Id, string Action, DateTimeOffset CreatedAtUtc)
{
    public string TimeLabel => CreatedAtUtc.ToLocalTime().ToString("MMM d · HH:mm");
}

/// <summary>
/// Transactional, app-owned storage. This database never attaches to or writes to Readest.
/// </summary>
public sealed class AppDatabase
{
    public const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _path;

    public AppDatabase(string path) => _path = System.IO.Path.GetFullPath(path);
    public string Path => _path;

    public void Initialize()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS app_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS manual_books (id INTEGER PRIMARY KEY, title TEXT NOT NULL, authors TEXT NOT NULL, isbn13 TEXT, archived INTEGER NOT NULL DEFAULT 0, payload TEXT NOT NULL)");
        Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_manual_books_title ON manual_books(title)");
        Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_manual_books_isbn13 ON manual_books(isbn13)");
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS manual_sessions (id TEXT PRIMARY KEY, book_id INTEGER NOT NULL, started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, duration_seconds REAL NOT NULL, note TEXT NOT NULL, payload TEXT NOT NULL)");
        Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_manual_sessions_book_start ON manual_sessions(book_id, started_utc)");
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS active_manual_session (singleton INTEGER PRIMARY KEY CHECK(singleton = 1), payload TEXT NOT NULL)");
        Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS operation_journal (id INTEGER PRIMARY KEY AUTOINCREMENT, action TEXT NOT NULL, created_utc TEXT NOT NULL, snapshot TEXT NOT NULL)");
        Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS ix_operation_journal_created ON operation_journal(created_utc DESC)");
        SetMeta(connection, transaction, "schema_version", CurrentSchemaVersion.ToString());
        transaction.Commit();
    }

    public bool HasManualData()
    {
        Initialize();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM manual_books) OR EXISTS(SELECT 1 FROM manual_sessions) OR EXISTS(SELECT 1 FROM active_manual_session) OR EXISTS(SELECT 1 FROM app_meta WHERE key='last_write_utc')";
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    public ManualReadingData LoadManualData()
    {
        Initialize();
        using var connection = Open();
        var data = new ManualReadingData { SchemaVersion = 3 };
        var references = ScalarString(connection, "SELECT value FROM app_meta WHERE key='recovery_references'");
        if (references is not null) data.RecoveryReferences = JsonSerializer.Deserialize<LibraryReferenceSnapshot>(references);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT payload FROM manual_books ORDER BY id";
            using var reader = command.ExecuteReader();
            while (reader.Read()) if (Deserialize<ManualBook>(reader.GetString(0)) is { } book) data.Books.Add(book);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT payload FROM manual_sessions ORDER BY started_utc";
            using var reader = command.ExecuteReader();
            while (reader.Read()) if (Deserialize<ManualReadingSession>(reader.GetString(0)) is { } session) data.Sessions.Add(session);
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT payload FROM active_manual_session WHERE singleton = 1";
            if (command.ExecuteScalar() is string json) data.ActiveSession = Deserialize<ActiveManualSession>(json);
        }
        return data;
    }

    public void SaveManualData(ManualReadingData data)
    {
        Initialize();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        WriteManualData(connection, transaction, data);
        transaction.Commit();
    }

    private static void WriteManualData(SqliteConnection connection, SqliteTransaction transaction, ManualReadingData data)
    {
        Execute(connection, transaction, "DELETE FROM manual_books");
        Execute(connection, transaction, "DELETE FROM manual_sessions");
        Execute(connection, transaction, "DELETE FROM active_manual_session");
        foreach (var book in data.Books)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO manual_books(id,title,authors,isbn13,archived,payload) VALUES($id,$title,$authors,$isbn13,$archived,$payload)";
            command.Parameters.AddWithValue("$id", book.Id);
            command.Parameters.AddWithValue("$title", book.Title);
            command.Parameters.AddWithValue("$authors", book.Authors);
            command.Parameters.AddWithValue("$isbn13", (object?)book.Isbn13 ?? DBNull.Value);
            command.Parameters.AddWithValue("$archived", book.Archived ? 1 : 0);
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(book, JsonOptions));
            command.ExecuteNonQuery();
        }
        foreach (var session in data.Sessions)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO manual_sessions(id,book_id,started_utc,ended_utc,duration_seconds,note,payload) VALUES($id,$book,$start,$end,$duration,$note,$payload)";
            command.Parameters.AddWithValue("$id", session.Id);
            command.Parameters.AddWithValue("$book", session.BookId);
            command.Parameters.AddWithValue("$start", session.StartedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$end", session.EndedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$duration", session.DurationSeconds);
            command.Parameters.AddWithValue("$note", session.Note ?? "");
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(session, JsonOptions));
            command.ExecuteNonQuery();
        }
        if (data.ActiveSession is not null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO active_manual_session(singleton,payload) VALUES(1,$payload)";
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(data.ActiveSession, JsonOptions));
            command.ExecuteNonQuery();
        }
        SetMeta(connection, transaction, "schema_version", CurrentSchemaVersion.ToString());
        SetMeta(connection, transaction, "last_write_utc", DateTimeOffset.UtcNow.ToString("O"));
        if (data.RecoveryReferences is not null) SetMeta(connection, transaction, "recovery_references", JsonSerializer.Serialize(data.RecoveryReferences));
        else Execute(connection, transaction, "DELETE FROM app_meta WHERE key='recovery_references'");
    }

    public AppDatabaseHealth Inspect()
    {
        Initialize();
        using var connection = Open();
        var integrity = ScalarString(connection, "PRAGMA quick_check") ?? "unknown";
        return new(
            int.TryParse(ScalarString(connection, "SELECT value FROM app_meta WHERE key='schema_version'"), out var version) ? version : 0,
            ScalarInt(connection, "SELECT COUNT(*) FROM manual_books"),
            ScalarInt(connection, "SELECT COUNT(*) FROM manual_sessions"),
            ScalarInt(connection, "SELECT COUNT(*) FROM active_manual_session") > 0,
            integrity,
            _path);
    }

    public void Checkpoint()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    public void CaptureUndo(string action, ManualReadingData data, int keep = 50)
    {
        Initialize(); using var connection = Open(); using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "INSERT INTO operation_journal(action,created_utc,snapshot) VALUES($action,$created,$snapshot)";
            command.Parameters.AddWithValue("$action", action); command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(data, JsonOptions)); command.ExecuteNonQuery();
        }
        using (var trim = connection.CreateCommand()) { trim.Transaction = transaction; trim.CommandText = "DELETE FROM operation_journal WHERE id NOT IN (SELECT id FROM operation_journal ORDER BY id DESC LIMIT $keep)"; trim.Parameters.AddWithValue("$keep", Math.Max(1, keep)); trim.ExecuteNonQuery(); }
        transaction.Commit();
    }

    public IReadOnlyList<OperationJournalEntry> ListOperations(int limit = 20)
    {
        Initialize(); using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT id,action,created_utc FROM operation_journal ORDER BY id DESC LIMIT $limit"; command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        var result = new List<OperationJournalEntry>(); using var reader = command.ExecuteReader(); while (reader.Read()) if (DateTimeOffset.TryParse(reader.GetString(2), out var created)) result.Add(new(reader.GetInt64(0), reader.GetString(1), created)); return result;
    }

    public ManualReadingData? RestoreLatestUndo()
    {
        Initialize(); using var connection = Open(); using var transaction = connection.BeginTransaction(); long id; string json;
        using (var command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "SELECT id,snapshot FROM operation_journal ORDER BY id DESC LIMIT 1"; using var reader = command.ExecuteReader(); if (!reader.Read()) return null; id = reader.GetInt64(0); json = reader.GetString(1); }
        var restored = JsonSerializer.Deserialize<ManualReadingData>(json, JsonOptions) ?? throw new InvalidDataException("The undo snapshot is invalid.");
        WriteManualData(connection, transaction, restored);
        using (var command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "DELETE FROM operation_journal WHERE id=$id"; command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery(); }
        transaction.Commit(); return Deserialize<ManualReadingData>(json);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void SetMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO app_meta(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar()?.ToString();
    }

    private static T? Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }
}
