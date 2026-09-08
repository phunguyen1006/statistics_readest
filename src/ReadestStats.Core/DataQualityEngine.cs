namespace ReadestStats.Core;

public sealed class DataQualityEngine
{
    public DataQualityReport Analyze(IReadOnlyList<Book> books, IReadOnlyList<ReadingEvent> events, DatabaseDiagnostics? diagnostics, DateTimeOffset now)
    {
        var checks = new List<DataQualityCheck>();
        Add(checks, "Source connection", diagnostics is not null, diagnostics is null ? "Statistics database is unavailable." : "Readest statistics database is readable.", diagnostics is null ? "Reconnect from Settings." : $"Schema {diagnostics.SchemaVersion} · {diagnostics.Tables.Count} tables");

        var bookIds = books.Select(book => book.Id).ToHashSet();
        var orphanEvents = events.Count(e => !bookIds.Contains(e.BookId));
        Add(checks, "Book mapping", orphanEvents == 0, orphanEvents == 0 ? "Every activity event maps to a known book." : $"{orphanEvents} activity events do not map to a book.", $"{events.Count - orphanEvents} of {events.Count} events mapped");

        var futureEvents = events.Count(e => e.Start > now.AddMinutes(10));
        Add(checks, "Event timestamps", futureEvents == 0, futureEvents == 0 ? "No activity is dated unexpectedly in the future." : $"{futureEvents} events are more than 10 minutes in the future.", events.Count == 0 ? "No events available" : $"Latest event {events.Max(e => e.Start):MMM d, yyyy HH:mm}");

        var unusuallyLong = events.Count(e => e.DurationSeconds > TimeSpan.FromHours(8).TotalSeconds);
        Add(checks, "Event duration", unusuallyLong == 0, unusuallyLong == 0 ? "No single reading event exceeds eight hours." : $"{unusuallyLong} events exceed eight hours and may distort totals.", $"{events.Count} positive-duration events analyzed");

        var duplicates = events.GroupBy(e => (e.BookId, e.Page, e.StartTime)).Sum(group => Math.Max(0, group.Count() - 1));
        Add(checks, "Duplicate activity", duplicates == 0, duplicates == 0 ? "No duplicate activity keys were found." : $"{duplicates} repeated activity records were found.", "Compared book, page and start time");

        var incompleteBooks = books.Count(book => string.IsNullOrWhiteSpace(book.Title) || string.IsNullOrWhiteSpace(book.Authors));
        Add(checks, "Book metadata", incompleteBooks == 0, incompleteBooks == 0 ? "All books have a title and author." : $"{incompleteBooks} books have a missing title or author.", $"{books.Count - incompleteBooks} of {books.Count} books complete");

        var missingFiles = books.Count(book => !string.IsNullOrWhiteSpace(book.Hash) && ReadestLibraryLocator.FindBookFile(diagnostics?.Path, book.Hash) is null);
        Add(checks, "Local book files", missingFiles == 0, missingFiles == 0 ? "All mapped library files are available locally." : $"{missingFiles} books have no readable local file.", $"{Math.Max(0, books.Count - missingFiles)} of {books.Count} files located");

        var latest = events.Count == 0 ? (DateTimeOffset?)null : events.Max(e => e.Start);
        var isFresh = latest is not null && latest >= now.AddDays(-30);
        Add(checks, "Activity freshness", isFresh, latest is null ? "No reading activity is available yet." : isFresh ? "Reading activity has been recorded in the last 30 days." : "The latest reading activity is more than 30 days old.", latest is null ? "Waiting for the first event" : $"Latest activity {latest:MMM d, yyyy}");

        var attention = checks.Count(check => check.NeedsAttention);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(100d * (checks.Count - attention) / checks.Count);
        var status = attention == 0 ? "Healthy" : score >= 75 ? "Mostly healthy" : "Needs attention";
        var summary = attention == 0 ? "All checks passed. Statistics are ready to use." : $"{attention} of {checks.Count} checks need attention. Open a check to review its evidence.";
        return new(score, status, checks.Count - attention, attention, summary, checks);
    }

    private static void Add(ICollection<DataQualityCheck> checks, string name, bool passed, string detail, string evidence) => checks.Add(new(name, passed ? "Passed" : "Attention", detail, evidence, !passed));
}
