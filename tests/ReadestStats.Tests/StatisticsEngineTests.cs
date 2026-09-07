using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class StatisticsEngineTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test +07", TimeSpan.FromHours(7), "Test +07", "Test +07");
    private readonly StatisticsEngine _engine = new(Zone);
    private static long Ts(int year, int month, int day, int hour = 0, int minute = 0) => new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeSeconds();
    private static ReadingEvent E(long time, double duration = 60, long book = 1, int page = 1) => new(book, page, time, duration, 100);

    [Fact] public void TotalTime_SumsEvents() { var overview = _engine.Overview([E(Ts(2026, 9, 7), 30), E(Ts(2026, 9, 7), 90)], [new(1, "Book", "Author")], 30, TimeSpan.FromMinutes(5), new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero)); Assert.Equal(120, overview.TotalSeconds); }

    [Fact] public void DayGrouping_UsesLocalCalendarDate()
    {
        var daily = _engine.Daily([E(Ts(2026, 9, 6, 16, 59)), E(Ts(2026, 9, 6, 17, 1))], TimeSpan.FromMinutes(5));
        Assert.Equal(new DateOnly(2026, 9, 6), daily[0].Date); Assert.Equal(new DateOnly(2026, 9, 7), daily[1].Date);
    }

    [Theory]
    [InlineData(new[] { 0 }, 1)]
    [InlineData(new[] { 0, -1 }, 2)]
    [InlineData(new[] { -1, -2 }, 2)]
    [InlineData(new[] { 0, -2 }, 1)]
    [InlineData(new[] { -2, -3 }, 0)]
    public void CurrentStreak_FollowsTodayYesterdayRule(int[] offsets, int expected)
    {
        var today = new DateOnly(2026, 1, 1); var result = _engine.Streaks(offsets.Select(today.AddDays), today); Assert.Equal(expected, result.Current);
    }

    [Fact] public void LongestStreak_HandlesMonthAndYearBoundaries()
    {
        DateOnly[] dates = [new(2025, 12, 30), new(2025, 12, 31), new(2026, 1, 1), new(2026, 1, 4), new(2026, 1, 5)]; Assert.Equal(3, _engine.Streaks(dates, new(2026, 1, 5)).Longest);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(6, 2)]
    public void Sessions_RespectGapThreshold(int gapMinutes, int expected)
    {
        var first = E(Ts(2026, 9, 7, 10), 60); var second = E(first.StartTime + 60 + gapMinutes * 60, 60); Assert.Equal(expected, _engine.BuildSessions([first, second], TimeSpan.FromMinutes(5)).Count);
    }

    [Fact] public void Sessions_MergeOverlapsAndBookSwitches()
    {
        var a = E(Ts(2026, 9, 7, 10), 600, 1); var b = E(a.StartTime + 300, 600, 2); var session = Assert.Single(_engine.BuildSessions([a, b], TimeSpan.FromMinutes(5))); Assert.Equal(900, session.DurationSeconds); Assert.Equal(2, session.BookIds.Count);
    }

    [Fact] public void Comparison_HandlesNormalAndZeroPrevious()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero); var events = new[] { E(start.AddDays(-1).ToUnixTimeSeconds(), 100), E(start.AddDays(1).ToUnixTimeSeconds(), 150) };
        Assert.Equal(50, _engine.Compare(events, start, start.AddDays(2)).PercentChange); Assert.Null(_engine.Compare([events[1]], start, start.AddDays(2)).PercentChange);
    }

    [Fact] public void BookRanking_OrdersByCanonicalDuration()
    {
        var books = new[] { new Book(1, "One", "A"), new Book(2, "Two", "B") }; var ranked = _engine.RankBooks([E(Ts(2026, 1, 1), 20, 1), E(Ts(2026, 1, 1), 90, 2)], books); Assert.Equal(2, ranked[0].Id);
    }

    [Fact] public void HourlyAndWeekday_UseLocalTime()
    {
        var overview = _engine.Overview([E(Ts(2026, 9, 6, 17), 600)], [new(1, "Book", "A")], 30, TimeSpan.FromMinutes(5), new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero)); Assert.Equal(10, overview.Hourly[0].Value); Assert.Equal(10, overview.Weekdays.Single(x => x.Label == "Mon").Value);
    }

    [Fact] public void Median_IsTrueMedian() { Assert.Equal(2.5, StatisticsEngine.Median([1, 2, 3, 8])); Assert.Equal(3, StatisticsEngine.Median([1, 3, 9])); }
    [Fact] public void EmptyData_DoesNotCrash() { var result = _engine.Overview([], [], 30, TimeSpan.FromMinutes(5)); Assert.Equal(0, result.TotalSeconds); Assert.Empty(result.Records); }
}
