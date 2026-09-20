using System.Text;

namespace ReadestStats.Core;

public sealed record CatalogImportBook(string Title, string Authors, string? Isbn13, int? Pages, DateTimeOffset? FinishedAtUtc);
public sealed record CatalogImportPreview(string Provider, int Rows, int ValidBooks, int SkippedRows, IReadOnlyList<CatalogImportBook> Books);

public sealed class LibraryImportService
{
    public async Task<CatalogImportPreview> PreviewCsvAsync(string path, CancellationToken token = default)
    {
        var rows = ParseCsv(await File.ReadAllTextAsync(path, token));
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

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"') { if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; } else quoted = !quoted; }
            else if (character == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear(); if (row.Any(value => value.Length > 0)) rows.Add(row); row = [];
            }
            else field.Append(character);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
