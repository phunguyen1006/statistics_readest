using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class DataQualityEngineTests
{
    [Fact]
    public void Analyze_HealthyData_PassesCoreChecks()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var books = new[] { new Book(1, "Book", "Author") };
        var events = new[] { new ReadingEvent(1, 4, now.AddDays(-1).ToUnixTimeSeconds(), 120, 100) };
        var diagnostics = new DatabaseDiagnostics("missing.db", 100, 1, 1, events[0].Start, events[0].Start, "Connected", 1, ["book", "page_stat_data"]);

        var report = new DataQualityEngine().Analyze(books, events, diagnostics, now);

        Assert.Equal("Healthy", report.Status);
        Assert.Equal(100, report.Score);
        Assert.All(report.Checks, check => Assert.False(check.NeedsAttention));
    }

    [Fact]
    public void Analyze_FlagsOrphansFutureEventsDuplicatesAndMetadata()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var future = now.AddHours(1).ToUnixTimeSeconds();
        var books = new[] { new Book(1, "", "") };
        var events = new[]
        {
            new ReadingEvent(2, 1, future, 9 * 3600, 100),
            new ReadingEvent(2, 1, future, 9 * 3600, 100)
        };

        var report = new DataQualityEngine().Analyze(books, events, null, now);

        Assert.Equal("Needs attention", report.Status);
        Assert.Contains(report.Checks, check => check.Name == "Book mapping" && check.NeedsAttention);
        Assert.Contains(report.Checks, check => check.Name == "Duplicate activity" && check.NeedsAttention);
        Assert.Contains(report.Checks, check => check.Name == "Book metadata" && check.NeedsAttention);
    }
}
