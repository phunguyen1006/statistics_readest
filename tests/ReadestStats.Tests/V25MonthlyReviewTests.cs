using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25MonthlyReviewTests
{
    [Fact]
    public void PastMonth_ClipsCrossingMonthSession_AndUsesFullPreviousMonth()
    {
        var start = new DateTimeOffset(2024, 2, 29, 23, 50, 0, TimeSpan.Zero);
        var review = MonthlyReviewEngine.Build([new(1, 1, start.ToUnixTimeSeconds(), 1200, 100)], [new(1, "Book", "Author")],
            2024, 3, now: new DateTimeOffset(2024, 4, 10, 12, 0, 0, TimeSpan.Zero), zone: TimeZoneInfo.Utc);

        Assert.True(review.IsComplete);
        Assert.Equal(600, review.TotalSeconds);
        Assert.Equal(600, review.PreviousSeconds);
        Assert.Equal(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), review.PreviousStartUtc);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), review.PreviousEndUtc);
        Assert.Equal(31, review.Daily.Count);
        Assert.Equal(1, review.ActiveDays);
        Assert.Equal(review.TotalSeconds, review.Daily.Sum(day => day.Seconds));
        Assert.Equal(review.TotalSeconds, review.TopBooks.Sum(book => book.Seconds));
        Assert.Equal("Previous full month", review.ComparisonLabel);
    }

    [Fact]
    public void CurrentMonth_ComparesEqualElapsedTime_NotAllOfPreviousMonth()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event(new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero), 300),
            Event(new(2026, 10, 4, 13, 0, 0, TimeSpan.Zero), 700),
            Event(new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero), 120),
            Event(new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero), 900)
        };
        var review = MonthlyReviewEngine.Build(events, [new(1, "Book", "Author")], 2026, 10, now: now, zone: TimeZoneInfo.Utc);

        Assert.False(review.IsComplete);
        Assert.Equal(now, review.EndUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero), review.PreviousEndUtc);
        Assert.Equal(300, review.TotalSeconds);
        Assert.Equal(120, review.PreviousSeconds);
        Assert.Equal(4, review.CoveredDays);
        Assert.Equal("Same elapsed time in previous month", review.ComparisonLabel);
    }

    [Fact]
    public void SourceScopeFiltersBothPeriods_AndUnknownBookStillRanks()
    {
        var events = new[]
        {
            Event(new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero), 60),
            Event(new(2026, 9, 3, 10, 5, 0, TimeSpan.Zero), 120, "Manual", -1),
            Event(new(2026, 8, 3, 10, 0, 0, TimeSpan.Zero), 700),
            Event(new(2026, 8, 3, 10, 5, 0, TimeSpan.Zero), 180, "Manual", -1)
        };
        var review = MonthlyReviewEngine.Build(events, [], 2026, 9, sourceScope: "Manual",
            now: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), zone: TimeZoneInfo.Utc);

        Assert.Equal(120, review.TotalSeconds);
        Assert.Equal(180, review.PreviousSeconds);
        Assert.Equal("Manual", Assert.Single(review.Sources).Source);
        Assert.Equal("Unknown book", Assert.Single(review.TopBooks).Title);
        Assert.Contains("Source: Manual", review.Provenance);
        Assert.Contains("local exclusions", review.Provenance);
    }

    [Fact]
    public void LocalTimezoneControlsBoundary_NotUtcMonth()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
        var start = new DateTimeOffset(2026, 8, 31, 16, 50, 0, TimeSpan.Zero);
        var review = MonthlyReviewEngine.Build([Event(start, 1200)], [new(1, "Book", "Author")], 2026, 9,
            now: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), zone: zone);
        Assert.Equal(600, review.TotalSeconds);
        Assert.Equal(600, review.PreviousSeconds);
        Assert.Equal(600, review.Daily[0].Seconds);
    }

    [Fact]
    public void ShortPreviousMonth_IsCappedAndDisclosed()
    {
        var review = MonthlyReviewEngine.Build([], [], 2026, 3,
            now: new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero), zone: TimeZoneInfo.Utc);
        Assert.Equal(review.StartUtc, review.PreviousEndUtc);
        Assert.Contains("preceding month is shorter", review.Provenance);
        Assert.False(review.HasBaseline);
        Assert.Null(review.PercentChange);
    }

    [Fact]
    public void FutureMonth_HasNoElapsedDays_OrFutureEventTotals()
    {
        var review = MonthlyReviewEngine.Build([Event(new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero), 300)], [], 2026, 10,
            now: new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), zone: TimeZoneInfo.Utc);
        Assert.Equal(0, review.CoveredDays);
        Assert.Equal(0, review.TotalSeconds);
        Assert.Equal(0, review.PreviousSeconds);
    }

    [Fact]
    public async Task ExistingMonthlyExporter_UsesSameClippedTotals()
    {
        var path = Path.Combine(Path.GetTempPath(), "ReadestStats-v25-report-" + Guid.NewGuid().ToString("N") + ".md");
        try
        {
            var boundary = new DateRangeService().ToUtc(new DateOnly(2020, 2, 1));
            await new ExportService().ExportMonthlyMarkdownAsync(path, 2020, 2,
                [Event(boundary.AddMinutes(-10), 1200)], [new(1, "Book", "Author")], 5);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("Reading time: 10m", content);
            Assert.Contains("Time zone:", content);
            Assert.Contains("clipped to local month boundaries", content);
            Assert.DoesNotContain("Reading time: 20m", content);
        }
        finally { File.Delete(path); }
    }

    private static ReadingEvent Event(DateTimeOffset start, double seconds, string source = "Readest", long bookId = 1) =>
        new(bookId, 1, start.ToUnixTimeSeconds(), seconds, 100, source);
}
