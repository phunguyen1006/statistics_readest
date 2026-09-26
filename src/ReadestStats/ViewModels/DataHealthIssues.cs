using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed record DataHealthIssue(string Title, string Detail, string Action, long? BookId = null, string? SessionId = null, DateTimeOffset? EventStart = null, string Route = "Settings");

public static class DataHealthIssues
{
    public static IReadOnlyList<DataHealthIssue> For(string check, IReadOnlyList<Book> books, IReadOnlyList<ReadingEvent> events, ManualLogViewModel manual, IReadOnlyList<BookEditionLink> links, DatabaseDiagnostics? diagnostics)
    {
        var result = new List<DataHealthIssue>();
        void BookIssue(Book book, string detail) => result.Add(new(book.Title, detail, "Open book", book.Id, Route: "Books"));
        void SessionIssue(ManualReadingSession session, string detail) => result.Add(new(manual.FindBook(session.BookId)?.Title ?? "Unknown book", $"{session.StartedAtUtc.ToLocalTime():g} · {detail}", "Edit session", session.BookId, session.Id, Route: "Manual log"));
        switch (check)
        {
            case "Physical page ranges":
                foreach (var s in manual.Sessions.Where(s => s.EndPage is { } page && manual.FindBook(s.BookId)?.TotalPages is { } total && page > total)) SessionIssue(s, $"Page {s.EndPage} exceeds {manual.FindBook(s.BookId)?.TotalPages} pages.");
                break;
            case "Manual session timeline":
                foreach (var group in manual.Sessions.GroupBy(s => s.BookId))
                {
                    var ordered = group.OrderBy(s => s.StartedAtUtc).ToArray();
                    for (var i = 0; i < ordered.Length; i++)
                        if (ordered.Take(i).Any(previous => previous.EndedAtUtc > ordered[i].StartedAtUtc)) SessionIssue(ordered[i], "Overlaps another session for this book.");
                }
                break;
            case "Duplicate physical editions":
                foreach (var group in manual.DuplicateGroups) result.Add(new(group.Label, group.Detail, "Review duplicates", group.Books.First().Id, Route: "Manual log"));
                break;
            case "Book metadata":
                foreach (var b in books.Where(b => string.IsNullOrWhiteSpace(b.Title) || string.IsNullOrWhiteSpace(b.Authors))) BookIssue(b, "Title or author is missing.");
                break;
            case "Local book files":
                foreach (var b in books.Where(b => !string.IsNullOrWhiteSpace(b.Hash) && b.Source != "Manual" && ReadestLibraryLocator.FindBookFile(diagnostics?.Path, b.Hash) is null)) BookIssue(b, "The local Readest book file is unavailable.");
                break;
            case "Edition links":
                foreach (var link in links.Where(l => !books.Any(b => b.Hash == l.ReadestKey || "id:" + b.Id == l.ReadestKey) || !long.TryParse(l.ManualKey.Replace("manual:", ""), out var id) || manual.FindBook(id) is null))
                    result.Add(new("Unresolved edition link", $"{link.ReadestKey} ↔ {link.ManualKey}", "Open library", Route: "Books"));
                break;
            case "Event timestamps": case "Event duration": case "Duplicate activity": case "Book mapping":
                var repeated = events.GroupBy(e => (e.BookId, e.Page, e.StartTime)).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
                foreach (var e in events.Where(e => check switch { "Event timestamps" => e.Start > DateTimeOffset.Now.AddMinutes(10), "Event duration" => e.DurationSeconds > 28800, "Duplicate activity" => repeated.Contains(e), _ => !books.Any(b => b.Id == e.BookId) }))
                    result.Add(new(books.FirstOrDefault(b => b.Id == e.BookId)?.Title ?? $"Book {e.BookId}", $"{e.Start.ToLocalTime():g} · {Formatters.Duration(e.DurationSeconds)} · {e.Source}", "Open activity", e.BookId, EventStart: e.Start, Route: "Activity"));
                break;
            case "Complete backup": result.Add(new("Backup history", "Create or inspect a complete backup below.", "Create backup", Route: "Backup")); break;
            case "Readest note files": result.Add(new("Readest notes", "Check the source diagnostics and refresh after Readest finishes saving.", "Open notes", Route: "Notes")); break;
            case "Source connection": result.Add(new("Readest connection", "Select the Readest statistics database below.", "Reconnect", Route: "Reconnect")); break;
        }
        return result;
    }
}
