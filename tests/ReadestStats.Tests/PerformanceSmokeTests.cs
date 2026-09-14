using System.Diagnostics;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class PerformanceSmokeTests
{
    [Fact]
    public void LargeLocalLibraryAnalysisCompletesWithinSmokeBudget()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 100_000).Select(index => new ReadingEvent(index % 1_000 + 1, index % 500, now.AddMinutes(-index * 3L).ToUnixTimeSeconds(), 45 + index % 180, 500)).ToArray();
        var ranges = new DateRangeService(TimeZoneInfo.Utc); var analytics = new AnalyticsEngine(TimeZoneInfo.Utc);
        var range = ranges.Resolve("1 year", now, events[^1].Start);
        var timer = Stopwatch.StartNew();
        var metrics = analytics.Metrics(events, range, 5);
        var trend = analytics.AggregateTrend(events, range, "Reading time", "Auto", 5);
        timer.Stop();
        Assert.True(metrics.TotalSeconds > 0);
        Assert.InRange(trend.Count, 12, 13);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Large-library analysis took {timer.Elapsed}.");
    }

    [Fact]
    public void TenThousandNoteDiscoveryIndexRemainsInteractive()
    {
        var engine = new NoteDiscoveryEngine(new Random(42)); var timer = Stopwatch.StartNew();
        engine.Update(Enumerable.Range(0, 10_000).Select(index => KeyValuePair.Create($"note-{index}", $"book-{index % 1_000}")));
        for (var index = 0; index < 100; index++) _ = engine.Next(index == 0 ? null : $"note-{index - 1}");
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Note discovery took {timer.Elapsed}.");
    }
}
