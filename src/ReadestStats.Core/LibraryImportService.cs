using System.Text;

namespace ReadestStats.Core;

public sealed record CatalogImportBook(string Title, string Authors, string? Isbn13, int? Pages, DateTimeOffset? FinishedAtUtc);
public sealed record CatalogImportPreview(string Provider, int Rows, int ValidBooks, int SkippedRows, IReadOnlyList<CatalogImportBook> Books);

public sealed class LibraryImportService
{
    internal const long MaxCsvBytes = 32_000_000;
    internal const int MaxRows = 100_000;
    internal const int MaxFieldLength = 64_000;
    internal const int MaxColumns = 256;

    public async Task<CatalogImportPreview> PreviewCsvAsync(string path, CancellationToken token = default)
    {
        var rows = ParseCsv(await ReadBoundedTextAsync(path, token), token);
        if (rows.Count == 0) return new("Generic CSV", 0, 0, 0, []);
        var headers = rows[0].Select(Normalize).ToArray();
        var provider = headers.Contains("exclusive shelf") ? "Goodreads" : headers.Contains("read status") || headers.Contains("star rating") ? "StoryGraph" : "Generic CSV";
        var title = Find(headers, "title", "book title");
        if (title < 0) throw new InvalidDataException("The CSV needs a Title or Book Title column.");
        var author = Find(headers, "author", "authors", "author l f", "author lf");
        var isbn = Find(headers, "isbn13", "isbn 13", "isbn");
        var pages = Find(headers, "number of pages", "pages", "page count");
        var finished = Find(headers, "date read", "date finished", "last date read");
        var books = new List<CatalogImportBook>();
        var skipped = 0;
        foreach (var row in rows.Skip(1))
        {
            token.ThrowIfCancellationRequested();
            var bookTitle = Value(row, title).Trim();
            if (string.IsNullOrWhiteSpace(bookTitle)) { skipped++; continue; }
            var rawIsbn = Digits(Value(row, isbn));
            books.Add(new(bookTitle, Value(row, author).Trim(), rawIsbn.Length == 13 ? rawIsbn : null,
                int.TryParse(Value(row, pages), out var pageCount) && pageCount > 0 ? pageCount : null,
                DateTimeOffset.TryParse(Value(row, finished), out var date) ? date.ToUniversalTime() : null));
        }
        return new(provider, Math.Max(0, rows.Count - 1), books.Count, skipped, books);
    }

    private static int Find(IReadOnlyList<string> headers, params string[] options)
    {
        foreach (var option in options) for (var index = 0; index < headers.Count; index++) if (headers[index].Equals(option, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
    private static string Value(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] : "";
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string Normalize(string value) => string.Join(' ', value.Trim().ToLowerInvariant().Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));

    private static async Task<string> ReadBoundedTextAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (input.Length > MaxCsvBytes) throw new InvalidDataException("The CSV is too large to import (32 MB maximum).");
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if (bytes.Length + read > MaxCsvBytes) throw new InvalidDataException("The CSV is too large to import (32 MB maximum).");
            bytes.Write(buffer, 0, read);
        }
        bytes.Position = 0;
        using var reader = new StreamReader(bytes, Encoding.UTF8, true);
        return await reader.ReadToEndAsync(token);
    }

    private static List<List<string>> ParseCsv(string text, CancellationToken token)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            if ((index & 4095) == 0) token.ThrowIfCancellationRequested();
            var character = text[index];
            if (character == '"') { if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; } else quoted = !quoted; }
            else if (character == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); if (row.Count > MaxColumns) throw new InvalidDataException("The CSV has too many columns."); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > MaxColumns) throw new InvalidDataException("The CSV has too many columns.");
                if (row.Any(value => value.Length > 0)) rows.Add(row); row = [];
                if (rows.Count > MaxRows) throw new InvalidDataException("The CSV has too many rows.");
            }
            else field.Append(character);
            if (field.Length > MaxFieldLength) throw new InvalidDataException("A CSV field is too long.");
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        if (row.Count > MaxColumns || rows.Count > MaxRows) throw new InvalidDataException("The CSV exceeds the supported size.");
        return rows;
    }
}
