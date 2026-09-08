using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class AnalyticsEngineTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.Zero, "UTC", "UTC");
    private readonly DateRangeService _ranges = new(Zone);
    private readonly AnalyticsEngine _analytics = new(Zone);
    private readonly InsightEngine _insights = new(Zone);
    private static DateTimeOffset At(int y, int m, int d, int h = 12) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
    private static ReadingEvent E(DateTimeOffset at, double seconds = 60, long book = 1) => new(book, 1, at.ToUnixTimeSeconds(), seconds, null);

    [Theory]
    [InlineData("7 days", 7)]
    [InlineData("30 days", 30)]
    [InlineData("90 days", 90)]
    public void RollingRangesHaveExpectedAndComparableLengths(string preset, int days)
    {
        var range = _ranges.Resolve(preset, At(2026, 9, 7));
        Assert.Equal(days, range.EndDate.DayNumber - range.StartDate.DayNumber + 1);
        Assert.Equal(range.End - range.Start, range.PreviousEnd - range.PreviousStart);
    }

    [Fact] public void CurrentMonthComparesTheSameElapsedDays()
    {
        var range = _ranges.Resolve("This month", At(2026, 9, 7));
        Assert.Equal(At(2026, 8, 1, 0), range.PreviousStart);
        Assert.Equal(At(2026, 8, 8, 0), range.PreviousEnd);
    }

    [Fact] public void CustomRangeNormalizesOrderAndClampsFuture()
    {
        var range = _ranges.Resolve("Custom", At(2026, 9, 7), customStart: new(2026, 9, 20), customEnd: new(2026, 9, 1));
        Assert.Equal(new DateOnly(2026, 9, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 7), range.EndDate);
    }

    [Fact] public void ConsistencyStartsAtFirstRecordedEvent()
    {
        var events = new[] { E(At(2026, 9, 5)), E(At(2026, 9, 7)) };
        var range = _ranges.Resolve("7 days", At(2026, 9, 7), events[0].Start);
        var metrics = _analytics.Metrics(events, range, 5);
        Assert.Equal(3, metrics.AvailableDays);
        Assert.Equal(2d / 3 * 100, metrics.ConsistencyPercent, 6);
    }

    [Fact] public void ZeroBaselineComparisonIsExplicitAndFinite()
    {
        var result = _analytics.Compare(new[] { E(At(2026, 9, 7), 120) }, _ranges.Resolve("7 days", At(2026, 9, 7)));
        Assert.False(result.HasBaseline);
        Assert.Null(result.PercentDelta);
        Assert.False(double.IsNaN(result.AbsoluteDelta));
    }

    [Fact] public void SessionProfileComputesMedianPercentilesAndBuckets()
    {
        var sessions = new[] { 60d, 300, 600, 1200, 3600 }.Select((s, i) => new ReadingSession(At(2026, 9, i + 1), At(2026, 9, i + 1).AddSeconds(s), s, new long[] { 1 }, 1));
        var profile = _analytics.Sessions(sessions);
        Assert.Equal(600, profile.Median);
        Assert.Equal(300, profile.P25);
        Assert.Equal(1200, profile.P75);
        Assert.Equal(6, profile.Histogram.Count);
        Assert.Equal(1, profile.Histogram[0].Value);
        Assert.Equal(1, profile.Histogram[^1].Value);
    }

    [Fact] public void WeekdayWeekendAveragesIncludeInactiveEligibleDays()
    {
        var events = new[] { E(At(2026, 9, 1), 600), E(At(2026, 9, 5), 600) };
        var result = _analytics.WeekdayWeekend(events, _ranges.Resolve("7 days", At(2026, 9, 7), events[0].Start));
        Assert.Equal(120, result.AverageWeekdaySeconds);
        Assert.Equal(300, result.AverageWeekendSeconds);
    }

    [Fact] public void CommonTwoHourWindowCanWrapMidnight()
    {
        var events = new[] { E(At(2026, 9, 6, 23), 300), E(At(2026, 9, 7, 0), 600) };
        var result = _analytics.CommonWindow(events, _ranges.Resolve("7 days", At(2026, 9, 7), events[0].Start));
        Assert.Equal(23, result.StartHour);
        Assert.Equal(900, result.Seconds);
    }

    [Fact] public void YearHeatmapHonorsLeapYearsAndEmptyData()
    {
        Assert.Equal(366, _analytics.Year([], [], 2024, 5).Heatmap.Count);
        var common = _analytics.Year([], [], 2025, 5);
        Assert.Equal(365, common.Heatmap.Count);
        Assert.Equal(0, common.TotalSeconds);
    }

    [Fact] public void InsightsExposeRealSampleCountsAndCalculationEvidence()
    {
        var events = Enumerable.Range(0, 5).Select(i => E(At(2026, 9, 7, 12).AddMinutes(i * 10), 60, 1)).ToArray();
        var range = _ranges.Resolve("7 days", At(2026, 9, 7, 18), events[0].Start);
        var items = _insights.Generate(events, [new(1, "Book", "Author")], range, 5);

        var rhythm = items.Single(item => item.Id == "reading-window");
        Assert.Equal("Based on 5 events", rhythm.SampleStatus);
        Assert.Contains("rolling two-hour window", rhythm.Evidence);
        Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Evidence)));
    }

    [Fact] public void LimitedInsightReportsTheActualEventCount()
    {
        var events = new[] { E(At(2026, 9, 7), 60) };
        var range = _ranges.Resolve("7 days", At(2026, 9, 7, 18), events[0].Start);

        var item = _insights.Generate(events, [new(1, "Book", "Author")], range, 5).Single(x => x.Id == "reading-window-insufficient");
        Assert.Equal("Only 1 event", item.SampleStatus);
    }

    [Fact] public void InsightClockLabelsRespectTwelveHourPreference()
    {
        var events = Enumerable.Range(0, 5).Select(i => E(At(2026, 9, 7, 14).AddMinutes(i), 60)).ToArray();
        var range = _ranges.Resolve("7 days", At(2026, 9, 7, 18), events[0].Start);

        var item = _insights.Generate(events, [new(1, "Book", "Author")], range, 5, use24HourTime: false).Single(x => x.Id == "reading-window");
        Assert.Contains("PM", item.Description);
        Assert.DoesNotContain("14:00", item.Description);
    }
}
