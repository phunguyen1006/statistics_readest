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

    public double ElapsedSeconds(DateTimeOffset nowUtc) => Math.Max(0,
        AccumulatedSeconds + Segments.Sum(segment => segment.DurationSeconds) +
        (!IsPaused && LastResumedAtUtc is { } resumed ? (nowUtc - resumed).TotalSeconds : 0));
}

public sealed class ManualReadingData
{
    public int SchemaVersion { get; set; } = 3;
    public List<ManualBook> Books { get; set; } = [];
    public List<ManualReadingSession> Sessions { get; set; } = [];
    public ActiveManualSession? ActiveSession { get; set; }
}

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
    int? PagesRead)
{
    public string DateLabel => StartedAt.ToString("MMM d, yyyy · HH:mm");
    public string DurationLabel => Formatters.Duration(DurationSeconds);
    public string PageRangeLabel => StartPage is not null && EndPage is not null ? $"pp. {StartPage}–{EndPage}" : "Time only";
    public string PagesReadLabel => PagesRead is > 0 ? $"{PagesRead} pages" : "Progress unchanged";
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
