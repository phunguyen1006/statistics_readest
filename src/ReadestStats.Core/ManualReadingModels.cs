namespace ReadestStats.Core;

public sealed class ManualBook
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Authors { get; set; } = "";
    public int? TotalPages { get; set; }
    public int? CurrentPage { get; set; }
    public string? Isbn10 { get; set; }
    public string? Isbn13 { get; set; }
    public string? Publisher { get; set; }
    public string? PublishedDate { get; set; }
    public string? Language { get; set; }
    public string? CoverUrl { get; set; }
    public string? InfoUrl { get; set; }
    public string? GoodreadsUrl { get; set; }
    public string? ExternalSource { get; set; }
    public string? ExternalId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public bool Archived { get; set; }

    public double ProgressPercent => TotalPages is > 0 && CurrentPage is > 0
        ? Math.Clamp(CurrentPage.Value * 100d / TotalPages.Value, 0, 100)
        : 0;
}

public sealed class ManualReadingSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long BookId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public double DurationSeconds { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public int? PagesRead { get; set; }
    public string Note { get; set; } = "";
    public List<ManualReadingSegment> Segments { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManualReadingSegment
{
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public double DurationSeconds => Math.Max(0, (EndedAtUtc - StartedAtUtc).TotalSeconds);
}

public sealed class ActiveManualSession
{
    public long BookId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? LastResumedAtUtc { get; set; }
    public double AccumulatedSeconds { get; set; }
    public bool IsPaused { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ManualReadingSegment> Segments { get; set; } = [];
    public int? TargetMinutes { get; set; }
    public bool IsFinishing { get; set; }
    public string FinishStartPage { get; set; } = "";
    public string FinishEndPage { get; set; } = "";
    public string FinishNote { get; set; } = "";

    public double ElapsedSeconds(DateTimeOffset nowUtc) => Math.Max(0,
        AccumulatedSeconds + Segments.Sum(segment => segment.DurationSeconds) +
        (!IsPaused && LastResumedAtUtc is { } resumed ? (nowUtc - resumed).TotalSeconds : 0));

    public double? TargetRemainingSeconds(DateTimeOffset nowUtc) => TargetMinutes is > 0 and <= 720
        ? Math.Max(0, TargetMinutes.Value * 60d - ElapsedSeconds(nowUtc)) : null;

    public void Pause(DateTimeOffset nowUtc)
    {
        if (!IsPaused && LastResumedAtUtc is { } started && nowUtc > started)
            Segments.Add(new ManualReadingSegment { StartedAtUtc = started, EndedAtUtc = nowUtc });
        IsPaused = true;
        LastResumedAtUtc = null;
        LastUpdatedUtc = nowUtc;
    }

    public void Resume(DateTimeOffset nowUtc)
    {
        if (!IsPaused) return;
        IsPaused = false;
        IsFinishing = false;
        LastResumedAtUtc = nowUtc;
        LastUpdatedUtc = nowUtc;
    }

    public void BeginFinish(DateTimeOffset nowUtc, int defaultStartPage)
    {
        Pause(nowUtc);
        IsFinishing = true;
        // A cancelled finish is still the same session. Keep its typed draft.
        if (string.IsNullOrWhiteSpace(FinishStartPage)) FinishStartPage = Math.Max(1, defaultStartPage).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed class ManualReadingData
{
    public int SchemaVersion { get; set; } = 3;
    public List<ManualBook> Books { get; set; } = [];
    public List<ManualReadingSession> Sessions { get; set; } = [];
    public ActiveManualSession? ActiveSession { get; set; }
    public LibraryReferenceSnapshot? RecoveryReferences { get; set; }
}

public sealed record LibraryReferenceSnapshot(Dictionary<string, BookTrackingState> Tracking, List<BookEditionLink> Links, List<string> Pins);

public sealed record BookMetadataResult(
    string Source,
    string ExternalId,
    string Title,
    string Authors,
    string? Publisher,
    string? PublishedDate,
    int? PageCount,
    string? Isbn10,
    string? Isbn13,
    string? Language,
    string? CoverUrl,
    string? InfoUrl,
    string? Description = null)
{
    public string? CachedCoverPath { get; init; }
    public string SecondaryLabel => string.Join(" · ", new[] { Authors, PublishedDate, PageCount is > 0 ? $"{PageCount} pages" : null }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record ManualSessionRow(
    string Id,
    long BookId,
    string BookTitle,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    int? StartPage,
    int? EndPage,
    int? PagesRead,
    string Note = "")
{
    public string DateLabel => StartedAt.ToString("MMM d, yyyy · HH:mm");
    public string DurationLabel => Formatters.Duration(DurationSeconds);
    public string PageRangeLabel => StartPage is not null && EndPage is not null ? $"pp. {StartPage}–{EndPage}" : "Time only";
    public string PagesReadLabel => PagesRead is > 0 ? $"{PagesRead} pages" : "Progress unchanged";
}

public sealed record ManualJournalBookOption(long? BookId, string Label);

public static class ManualSessionJournal
{
    public static IReadOnlyList<ManualSessionRow> Query(IReadOnlyList<ManualReadingSession> sessions, IReadOnlyList<ManualBook> books,
        string? search = null, long? bookId = null, DateOnly? from = null, DateOnly? through = null, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        if (from is { } start && through is { } end && start > end) return [];
        var bookMap = books.GroupBy(book => book.Id).ToDictionary(group => group.Key, group => group.First());
        var terms = (search ?? "").Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return sessions.Where(session => bookId is null || session.BookId == bookId)
            .Select(session => (Session: session, Book: bookMap.GetValueOrDefault(session.BookId),
                LocalStart: TimeZoneInfo.ConvertTime(session.StartedAtUtc, timeZone)))
            .Where(item => (from is null || DateOnly.FromDateTime(item.LocalStart.DateTime) >= from) &&
                (through is null || DateOnly.FromDateTime(item.LocalStart.DateTime) <= through))
            .Where(item => terms.All(term => Contains(string.Join(" ", item.Book?.Title, item.Book?.Authors, item.Session.Note,
                item.LocalStart.ToString("yyyy-MM-dd MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)), term)))
            .OrderByDescending(item => item.Session.StartedAtUtc).ThenBy(item => item.Session.Id, StringComparer.Ordinal)
            .Select(item => new ManualSessionRow(item.Session.Id, item.Session.BookId, item.Book?.Title ?? "Unknown book", item.LocalStart,
                item.Session.DurationSeconds, item.Session.StartPage, item.Session.EndPage, item.Session.PagesRead, item.Session.Note))
            .ToArray();
    }

    private static bool Contains(string text, string term) => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
        text, term, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;
}

public static class ManualReadingRules
{
    public static int CalculatePagesRead(int startPage, int endPage)
    {
        if (startPage <= 0) throw new ArgumentOutOfRangeException(nameof(startPage));
        if (endPage < startPage) throw new ArgumentOutOfRangeException(nameof(endPage));
        return endPage - startPage + 1;
    }
}
