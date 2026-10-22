using System.Globalization;
using System.Text;

namespace ReadestStats.Core;

public sealed record MonthlySourceSummary(string Source, double Seconds, int Events);

public sealed record MonthlyReview(
    int Year, int Month, DateOnly StartDate, DateOnly EndDate,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    DateTimeOffset PreviousStartUtc, DateTimeOffset PreviousEndUtc,
    bool IsComplete, int CoveredDays, string SourceScope, string TimeZoneId,
    double TotalSeconds, double PreviousSeconds, int ActiveDays, int Sessions, int ActiveBooks,
    IReadOnlyList<DailyStat> Daily, IReadOnlyList<ChartPoint> Days,
    IReadOnlyList<BookSummary> TopBooks, IReadOnlyList<MonthlySourceSummary> Sources,
    string Provenance)
{
    public MonthlySourceCoverage? SourceCoverage { get; init; }
    public double DeltaSeconds => TotalSeconds - PreviousSeconds;
    public double? PercentChange => PreviousSeconds <= 0 ? null : DeltaSeconds / PreviousSeconds * 100d;
    public bool HasBaseline => PreviousSeconds > 0;
    public string ComparisonLabel => IsComplete ? "Previous full month" : "Same elapsed time in previous month";
    public string Heading => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
}

/// <summary>One timezone-aware monthly calculation for on-screen reviews and exported reports.</summary>
public static class MonthlyReviewEngine
{
    public static MonthlyReview Build(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books,
        int year, int month, int sessionGapMinutes = 5, string sourceScope = "All sources",
        DateTimeOffset? now = null, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var currentTime = now ?? DateTimeOffset.Now;
        // DateOnly validates the requested month. Avoid overflowing the preceding/following months.
        var first = new DateOnly(year, month, 1);
        if (first == DateOnly.MinValue || year == 9999 && month == 12)
            throw new ArgumentOutOfRangeException(nameof(year), "This month has no supported comparison boundary.");
        var next = first.AddMonths(1);
        var previous = first.AddMonths(-1);
        var dates = new DateRangeService(zone);
        var statistics = new StatisticsEngine(zone);
        var startUtc = dates.ToUtc(first);
        var fullEndUtc = dates.ToUtc(next);
        var previousStartUtc = dates.ToUtc(previous);
        var isComplete = currentTime >= fullEndUtc;
        var endUtc = isComplete ? fullEndUtc : currentTime <= startUtc ? startUtc : currentTime;
        // Compare an unfinished month to equal elapsed time, never to a completed prior month.
        // A shorter preceding month is capped at its boundary and disclosed in the provenance.
        var previousEndUtc = isComplete ? startUtc : previousStartUtc + (endUtc - startUtc);
        var previousCapped = previousEndUtc > startUtc;
        if (previousCapped) previousEndUtc = startUtc;
        var scope = sourceScope is "Readest" or "Manual" ? sourceScope : "All sources";
        var scoped = events.Where(item => scope switch
        {
            "Manual" => item.Source.Equals("Manual", StringComparison.OrdinalIgnoreCase),
            "Readest" => !item.Source.Equals("Manual", StringComparison.OrdinalIgnoreCase),
            _ => true
        }).ToArray();
        var current = ReadingEventWindow.Clip(scoped, startUtc, endUtc);
        var prior = ReadingEventWindow.Clip(scoped, previousStartUtc, previousEndUtc);
        var gap = TimeSpan.FromMinutes(Math.Clamp(sessionGapMinutes, 0, 1440));
        var observedDays = statistics.Daily(current, gap);
        var dailyMap = observedDays.ToDictionary(day => day.Date);
        var monthDays = next.DayNumber - first.DayNumber;
        var daily = Enumerable.Range(0, monthDays).Select(index =>
        {
            var date = first.AddDays(index);
            return dailyMap.GetValueOrDefault(date) ?? new DailyStat(date, 0, 0, 0);
        }).ToArray();
        var availableBooks = books.GroupBy(book => book.Id).Select(group => group.First()).ToList();
        var knownIds = availableBooks.Select(book => book.Id).ToHashSet();
        availableBooks.AddRange(current.Select(item => item.BookId).Distinct().Where(id => !knownIds.Contains(id))
            .Select(id => new Book(id, "Unknown book", "")));
        var coveredDays = isComplete ? monthDays : endUtc <= startUtc ? 0 :
            Math.Clamp(DateOnly.FromDateTime(statistics.ToLocal(endUtc.AddTicks(-1)).DateTime).DayNumber - first.DayNumber + 1, 0, monthDays);
        var provenance = $"Source: {scope}. Time zone: {zone.Id}. Durations are clipped to local month boundaries; no private note text is included. " +
            "Statistics-selected events are used; source-overlap handling and local exclusions are applied by the caller. " +
            (isComplete ? "Comparison uses the complete preceding calendar month." : "Comparison uses equal elapsed time in the preceding month.") +
            (previousCapped ? " The preceding month is shorter, so its comparison window is capped at month end." : "");
        return new(year, month, first, next.AddDays(-1), startUtc, endUtc,
            previousStartUtc, previousEndUtc, isComplete, coveredDays, scope, zone.Id,
            current.Sum(item => item.DurationSeconds), prior.Sum(item => item.DurationSeconds),
            observedDays.Count(day => day.Seconds > 0), statistics.BuildSessions(current, gap).Count,
            current.Select(item => item.BookId).Distinct().Count(), daily,
            daily.Select(day => new ChartPoint(day.Date.Day.ToString(CultureInfo.InvariantCulture), day.Seconds / 60d,
                $"{day.Date:yyyy-MM-dd} · {Formatters.Duration(day.Seconds)}", "min")).ToArray(),
            statistics.RankBooks(current, availableBooks),
            current.GroupBy(item => item.Source.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? "Manual" : "Readest")
                .OrderBy(group => group.Key).Select(group => new MonthlySourceSummary(group.Key, group.Sum(item => item.DurationSeconds), group.Count())).ToArray(),
            provenance);
    }

    public static string ToMarkdown(MonthlyReview review)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Readest Stats — {review.Heading}").AppendLine();
        builder.AppendLine($"- Period: {review.StartDate:yyyy-MM-dd} – {review.EndDate:yyyy-MM-dd} ({(review.IsComplete ? "complete month" : "month to date")})");
        builder.AppendLine($"- Source: {review.SourceScope}");
        builder.AppendLine($"- Time zone: {review.TimeZoneId}").AppendLine();
        builder.AppendLine("## Summary").AppendLine();
        builder.AppendLine($"- Reading time: {Formatters.Duration(review.TotalSeconds)}");
        builder.AppendLine($"- Active days: {review.ActiveDays}/{review.CoveredDays} elapsed days");
        builder.AppendLine($"- Sessions: {review.Sessions}");
        builder.AppendLine($"- Active books: {review.ActiveBooks}");
        builder.AppendLine($"- {review.ComparisonLabel}: {Formatters.Duration(review.PreviousSeconds)}");
        builder.AppendLine(review.HasBaseline
            ? $"- Change: {(review.DeltaSeconds >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(review.DeltaSeconds))} ({review.PercentChange:+0.0;-0.0;0.0}%)"
            : "- Change: no previous-period baseline");
        builder.AppendLine().AppendLine("## Books").AppendLine();
        if (review.TopBooks.Count == 0) builder.AppendLine("No reading activity was recorded this month.");
        else foreach (var pair in review.TopBooks.Select((book, index) => (book, index)))
            builder.AppendLine($"{pair.index + 1}. **{Inline(pair.book.Title)}** — {Inline(pair.book.Authors)} — {Formatters.Duration(pair.book.Seconds)}");
        builder.AppendLine().AppendLine("## Daily activity").AppendLine();
        var activeDays = review.Daily.Where(day => day.Seconds > 0).ToArray();
        if (activeDays.Length == 0) builder.AppendLine("No active days.");
        else foreach (var day in activeDays)
            builder.AppendLine($"- {day.Date:MMM d}: {Formatters.Duration(day.Seconds)} · {Formatters.Count(day.Sessions, "session")} · {Formatters.Count(day.Books, "book")}");
        builder.AppendLine().AppendLine("## Sources").AppendLine();
        foreach (var source in review.Sources) builder.AppendLine($"- {source.Source}: {Formatters.Duration(source.Seconds)}");
        if (review.SourceCoverage is { } coverage)
            builder.AppendLine().AppendLine("## Source coverage").AppendLine().AppendLine(coverage.ToText());
        builder.AppendLine().AppendLine("---").AppendLine(review.Provenance);
        builder.AppendLine("Created locally by Readest Stats. Source reading data was not modified.");
        return builder.ToString();
    }

    private static string Inline(string value) => value.Replace("\r", " ").Replace("\n", " ")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\\", "\\\\").Replace("*", "\\*").Replace("[", "\\[").Replace("]", "\\]");
}
