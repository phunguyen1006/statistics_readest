using System.Text.Json;

namespace ReadestStats.Core;

/// <summary>
/// A note as it exists in Readest. The source object is immutable in Statistics;
/// personal curation is stored separately in <see cref="NoteUserState"/>.
/// </summary>
public sealed record ReadestNote(
    string Id,
    string BookHash,
    string BookTitle,
    string Authors,
    string? BookPath,
    string? CoverPath,
    string Text,
    string Note,
    string Type,
    string Color,
    int? Page,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Cfi,
    string? XpointerStart,
    string? XpointerEnd,
    bool IsDeleted = false)
{
    public bool HasComment => !string.IsNullOrWhiteSpace(Note);
    public bool HasHighlight => !string.IsNullOrWhiteSpace(Text);
    public string TypeLabel => string.IsNullOrWhiteSpace(Type) ? (HasComment ? "Note" : "Highlight") : Type;
    public string ColorLabel => string.IsNullOrWhiteSpace(Color) ? "Default" : Color;
    public string PageLabel => Page is null or <= 0 ? "Page not recorded" : $"Page {Page}";
    public string DisplayDate => (CreatedAt ?? UpdatedAt)?.ToLocalTime().ToString("MMM d, yyyy") ?? "Date not recorded";
}

public sealed class NoteUserState
{
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Collections { get; set; } = [];
    public string PersonalNote { get; set; } = "";
    public string ReviewStatus { get; set; } = "New";
    public DateTimeOffset? LastReviewedUtc { get; set; }
    public DateTimeOffset? NextReviewUtc { get; set; }
    public int ReviewIntervalDays { get; set; } = 1;
    public int TimesSeen { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
    public bool ExcludeFromRandom { get; set; }
}

public sealed record ReadestBookMetadata(
    string Hash,
    string Title,
    string Authors,
    string? BookPath,
    string? CoverPath);

public sealed record NotesLoadDiagnostics(int FilesScanned, int FilesFailed, int NotesLoaded, int NotesSkipped, int UnmappedBooks)
{
    public string Summary => $"{FilesScanned} files scanned · {NotesLoaded} notes loaded · {FilesFailed} unreadable · {NotesSkipped} skipped · {UnmappedBooks} unmapped books";
}

public sealed class ReadestNotesRepository
{
    private static readonly int[] RetryDelays = [0, 150, 400, 900];
    private readonly string _databasePath;
    public NotesLoadDiagnostics LastDiagnostics { get; private set; } = new(0, 0, 0, 0, 0);

    public ReadestNotesRepository(string databasePath) => _databasePath = Path.GetFullPath(databasePath);

    public async Task<IReadOnlyList<ReadestNote>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(_databasePath);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(Path.Combine(root, "Books"))) { LastDiagnostics = new(0, 0, 0, 0, 0); return []; }

        var books = await ReadLibraryAsync(root, cancellationToken);
        var result = new List<ReadestNote>();
        var scanned = 0; var failed = 0; var skipped = 0; var unmapped = 0;
        foreach (var configPath in Directory.EnumerateFiles(Path.Combine(root, "Books"), "config.json", SearchOption.AllDirectories))
        {
            scanned++;
            cancellationToken.ThrowIfCancellationRequested();
            var hash = Path.GetFileName(Path.GetDirectoryName(configPath) ?? "");
            if (string.IsNullOrWhiteSpace(hash)) continue;
            if (!books.ContainsKey(hash)) unmapped++;
            var book = books.GetValueOrDefault(hash) ?? new ReadestBookMetadata(
                hash,
                "Untitled book",
                "Unknown author",
                ReadestLibraryLocator.FindBookFile(_databasePath, hash),
                ReadestLibraryLocator.FindCoverFile(_databasePath, hash));
            var json = await ReadJsonWithRetryAsync(configPath, cancellationToken);
            if (json is null) { failed++; continue; }
            if (!json.RootElement.TryGetProperty("booknotes", out var notes) || notes.ValueKind != JsonValueKind.Array) { skipped++; continue; }
            var index = 0;
            foreach (var item in notes.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.ValueKind != JsonValueKind.Object) { skipped++; index++; continue; }
                var deletedAt = ReadDateTime(item, "deletedAt");
                if (deletedAt is not null) { skipped++; index++; continue; }
                var note = ReadString(item, "note");
                var text = ReadString(item, "text");
                var type = ReadString(item, "type");
                var color = ReadString(item, "color");
                var created = ReadDateTime(item, "createdAt");
                var updated = ReadDateTime(item, "updatedAt");
                var id = ReadString(item, "id");
                if (string.IsNullOrWhiteSpace(id)) id = $"{hash}:{ReadString(item, "cfi")}:{ReadString(item, "xpointer0")}:{created?.UtcTicks ?? index}";
                if (string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(type)) { skipped++; index++; continue; }
                result.Add(new ReadestNote(
                    id,
                    hash,
                    book.Title,
                    book.Authors,
                    book.BookPath,
                    book.CoverPath,
                    text,
                    note,
                    type,
                    color,
                    ReadInt(item, "page"),
                    created,
                    updated,
                    ReadString(item, "cfi"),
                    ReadString(item, "xpointer0"),
                    ReadString(item, "xpointer1")));
                index++;
            }
        }
        var loaded = result
            .GroupBy(note => note.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(note => note.UpdatedAt ?? note.CreatedAt).First())
            .OrderByDescending(note => note.CreatedAt ?? note.UpdatedAt ?? DateTimeOffset.MinValue)
            .ToArray();
        LastDiagnostics = new(scanned, failed, loaded.Length, skipped, unmapped);
        return loaded;
    }

    private static async Task<Dictionary<string, ReadestBookMetadata>> ReadLibraryAsync(string root, CancellationToken token)
    {
        var result = new Dictionary<string, ReadestBookMetadata>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(root, "Books", "library.json");
        var json = await ReadJsonWithRetryAsync(path, token);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var hash = ReadString(item, "hash");
            if (string.IsNullOrWhiteSpace(hash)) continue;
            var title = ReadString(item, "title");
            var author = ReadString(item, "author");
            var bookPath = ReadString(item, "filePath");
            if (!string.IsNullOrWhiteSpace(bookPath) && !Path.IsPathRooted(bookPath)) bookPath = Path.GetFullPath(Path.Combine(root, bookPath));
            if (string.IsNullOrWhiteSpace(bookPath) || !File.Exists(bookPath)) bookPath = ReadestLibraryLocator.FindBookFile(Path.Combine(root, "statistics.db"), hash);
            result[hash] = new ReadestBookMetadata(hash, string.IsNullOrWhiteSpace(title) ? "Untitled book" : title, string.IsNullOrWhiteSpace(author) ? "Unknown author" : author, bookPath, ReadestLibraryLocator.FindCoverFile(Path.Combine(root, "statistics.db"), hash));
        }
        return result;
    }

    private static async Task<JsonDocument?> ReadJsonWithRetryAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        Exception? last = null;
        foreach (var delay in RetryDelays)
        {
            if (delay > 0) await Task.Delay(delay, token);
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return await JsonDocument.ParseAsync(stream, cancellationToken: token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                last = ex;
            }
        }
        return null;
    }

    private static string ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }

    private static int? ReadInt(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer)) return integer;
        return int.TryParse(value.ToString(), out integer) ? integer : null;
    }

    private static DateTimeOffset? ReadDateTime(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number);
        var text = value.ToString();
        if (long.TryParse(text, out number)) return number > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number);
        return DateTimeOffset.TryParse(text, out var parsed) ? parsed : null;
    }
}
