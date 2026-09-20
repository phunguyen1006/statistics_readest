using System.Text.Json;

namespace ReadestStats.Core;

public sealed class ManualReadingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly AppDatabase _database;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ManualReadingStore(string path)
    {
        _path = path;
        _database = new AppDatabase(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!, "readest-stats.db"));
    }
    public string Path => _path;
    public string DatabasePath => _database.Path;
    public AppDatabaseHealth InspectDatabase() => _database.Inspect();
    public void CheckpointDatabase() => _database.Checkpoint();
    public void CaptureUndo(string action, ManualReadingData data) => _database.CaptureUndo(action, data);
    public IReadOnlyList<OperationJournalEntry> ListOperations() => _database.ListOperations();
    public ManualReadingData? RestoreLatestUndo() => _database.RestoreLatestUndo();

    public static async Task<ManualReadingData> LoadPortableFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<ManualReadingData>(stream, JsonOptions, cancellationToken) ?? new();
        Normalize(data);
        return data;
    }

    public async Task<ManualReadingData> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_database.HasManualData())
            {
                var stored = _database.LoadManualData();
                Normalize(stored);
                return stored;
            }
            if (!File.Exists(_path)) return new();
            await using var stream = File.OpenRead(_path);
            var data = await JsonSerializer.DeserializeAsync<ManualReadingData>(stream, JsonOptions, cancellationToken) ?? new();
            data.Books ??= [];
            data.Sessions ??= [];
            Normalize(data);
            _database.SaveManualData(data);
            _database.Checkpoint();
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            TryRestoreBackup();
            try
            {
                if (File.Exists(_path + ".bak"))
                {
                    await using var backup = File.OpenRead(_path + ".bak");
                    var data = await JsonSerializer.DeserializeAsync<ManualReadingData>(backup, JsonOptions, cancellationToken) ?? new();
                    Normalize(data);
                    return data;
                }
            }
            catch { }
            return new();
        }
    }

    public async Task SaveAsync(ManualReadingData data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Normalize(data);
            _database.SaveManualData(data);
            _database.Checkpoint();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(temp, _path, true);
        }
        finally { _writeLock.Release(); }
    }

    private static void Normalize(ManualReadingData data)
    {
        var sourceVersion = data.SchemaVersion;
        data.SchemaVersion = 3;
        data.Books ??= [];
        data.Sessions ??= [];
        var used = new HashSet<long>();
        foreach (var book in data.Books)
        {
            if (book.Id >= 0 || !used.Add(book.Id)) book.Id = NextBookId(used);
            used.Add(book.Id);
            book.Title = string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title.Trim();
            book.Authors = book.Authors?.Trim() ?? "";
            if (book.TotalPages <= 0) book.TotalPages = null;
            if (book.CurrentPage < 0) book.CurrentPage = null;
        }
        foreach (var session in data.Sessions)
        {
            session.Note ??= "";
            session.Segments ??= [];
            if (session.Segments.Count == 0 && session.DurationSeconds > 0)
            {
                var end = session.StartedAtUtc.AddSeconds(session.DurationSeconds);
                session.Segments.Add(new ManualReadingSegment { StartedAtUtc = session.StartedAtUtc, EndedAtUtc = end });
            }
            session.Segments.RemoveAll(segment => segment.EndedAtUtc <= segment.StartedAtUtc);
            if (session.Segments.Count > 0)
            {
                session.StartedAtUtc = session.Segments.Min(segment => segment.StartedAtUtc);
                session.EndedAtUtc = session.Segments.Max(segment => segment.EndedAtUtc);
                session.DurationSeconds = session.Segments.Sum(segment => segment.DurationSeconds);
            }
        }
        if (data.ActiveSession is { } active)
        {
            active.Segments ??= [];
            if (sourceVersion < 2 && active.AccumulatedSeconds > 0)
            {
                var end = active.LastResumedAtUtc ?? active.LastUpdatedUtc;
                active.Segments.Add(new ManualReadingSegment { StartedAtUtc = end.AddSeconds(-active.AccumulatedSeconds), EndedAtUtc = end });
            }
            active.AccumulatedSeconds = 0;
        }
    }

    public static long NextBookId(IEnumerable<long> existing)
    {
        var used = existing.ToHashSet();
        var candidate = -1L;
        while (used.Contains(candidate)) candidate--;
        return candidate;
    }

    private void TryRestoreBackup()
    {
        try
        {
            if (!File.Exists(_path + ".bak")) return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.Copy(_path + ".bak", _path, true);
        }
        catch { }
    }
}
