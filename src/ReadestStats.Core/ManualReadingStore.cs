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
    public string? LastMirrorError { get; private set; }
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
        if (_database.HasManualData())
        {
            var stored = _database.LoadManualData();
            Normalize(stored);
            return stored;
        }
        ManualReadingData data;
        try
        {
            await using var stream = File.OpenRead(_path);
            data = await JsonSerializer.DeserializeAsync<ManualReadingData>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The manual library file is empty.");
            Normalize(data);
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            try
            {
                await using var backup = File.OpenRead(_path + ".bak");
                data = await JsonSerializer.DeserializeAsync<ManualReadingData>(backup, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("The manual library backup is empty.");
                Normalize(data);
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                throw new InvalidDataException("The manual library could not be read and no valid backup is available. The original data was left unchanged.", ex);
            }
            // Validate the backup first and keep damaged bytes for recovery.
            try
            {
                File.Copy(_path, _path + ".corrupt", true);
                File.Copy(_path + ".bak", _path, true);
            }
            catch (Exception mirrorError) when (mirrorError is IOException or UnauthorizedAccessException) { LastMirrorError = mirrorError.Message; }
        }
        // Database failures must not be mistaken for a corrupt legacy JSON file.
        _database.SaveManualData(data);
        return data;
    }

    public async Task SaveAsync(ManualReadingData data, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Normalize(data);
            _database.SaveManualData(data);
            // The SQLite commit is authoritative. A failed JSON mirror must not
            // make callers retry a transaction that was already saved.
            var temp = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                using (var stream = File.Create(temp))
                    await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
                File.Move(temp, _path, true);
                LastMirrorError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException)
            {
                LastMirrorError = ex.Message;
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Drain already queued SQLite/mirror writes before restore. Suspend new mutations first.</summary>
    public async Task WaitForWritesAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _writeLock.Release();
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

}
