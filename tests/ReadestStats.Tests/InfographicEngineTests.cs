using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class InfographicEngineTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Infographic UTC", TimeSpan.Zero, "Infographic UTC", "Infographic UTC");
    private readonly InfographicEngine _engine = new(Zone);
    private readonly DateRangeService _ranges = new(Zone);
    private static DateTimeOffset At(int day, int hour = 12, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);
    private static ReadingEvent Event(int day, int hour, double seconds = 60, long book = 1) => new(book, 1, At(day, hour).ToUnixTimeSeconds(), seconds, null);
    private static readonly Book[] Books = [new(1, "Book A", "Author"), new(2, "Book B", "Author")];

    [Fact]
    public void WeekHourMatrixAlwaysUsesSevenByTwentyFourTruthfulCells()
    {
        var events = new[] { Event(8, 10, 120), Event(8, 10, 180) };
        var range = _ranges.Resolve("30 days", At(13, 23), events[0].Start);
        var matrix = _engine.WeekHourMatrix(events, range, 5, false);

        Assert.Equal(168, matrix.Count);
        var active = Assert.Single(matrix.Where(cell => cell.Value > 0));
        Assert.Equal("Tue", active.RowLabel);
        Assert.Equal("10", active.ColumnLabel);
        Assert.Equal(5, active.Value);
    }

    [Fact]
    public void EmptyAndSingleSessionInputsStayFinite()
    {
        Assert.Empty(_engine.SessionDots([], Books));
        var point = Assert.Single(_engine.SessionDots([new(At(8, 10), At(8, 10, 2), 120, [1], 2)], Books));
        Assert.Equal(2, point.Value);
        Assert.True(double.IsFinite(point.Secondary));
    }

    [Fact]
    public void SparseDayTimelineUsesObservedClockPositions()
    {
        var events = new[] { new ReadingEvent(1, 1, At(13, 8, 21).ToUnixTimeSeconds(), 120, null) };
        var span = Assert.Single(_engine.DayTimeline(events, Books, new DateOnly(2026, 9, 13), 5));

        Assert.InRange(span.StartHour, 8.34, 8.36);
        Assert.True(span.EndHour > span.StartHour);
        Assert.Contains("Book A", span.Detail);
    }

    [Fact]
    public void AttentionUsesObservedSharesAndAggregatesOnlyOverflow()
    {
        var events = new[] { Event(8, 10, 300, 1), Event(9, 10, 100, 2) };
        var parts = _engine.BookAttention(events, Books, 6);

        Assert.Equal(2, parts.Count);
        Assert.Equal(400, parts.Sum(part => part.Value));
        Assert.DoesNotContain(parts, part => part.Label == "Other");
    }

    [Fact]
    public void MomentumNeedsFourCalendarObservationsAndNeverFabricatesActivity()
    {
        var shortRange = _ranges.Resolve("Today", At(13, 23), At(13));
        Assert.Empty(_engine.RollingMomentum([Event(13, 10)], shortRange, 5));

        var range = _ranges.Resolve("7 days", At(13, 23), At(7));
        var points = _engine.RollingMomentum([Event(13, 10, 180)], range, 5);
        Assert.Equal(7, points.Count);
        Assert.All(points, point => Assert.True(double.IsFinite(point.Value) && point.Value >= 0));
    }

    [Fact]
    public void DenseSessionInputRemainsBoundedAndStaircaseMonotonic()
    {
        var sessions = Enumerable.Range(0, 300).Select(index => new ReadingSession(At(1).AddMinutes(index * 6), At(1).AddMinutes(index * 6 + 2), 120, [1], 1)).ToArray();
        var dots = _engine.SessionDots(sessions, Books);
        var staircase = _engine.SessionStaircase(sessions);

        Assert.Equal(300, dots.Count);
        Assert.Equal(300, staircase.Count);
        Assert.True(staircase.Zip(staircase.Skip(1)).All(pair => pair.First.Value <= pair.Second.Value));
    }

    [Fact]
    public void GoalPaceDoesNotProjectMissingCompletions()
    {
        var pace = _engine.YearGoalPace([], 2026, 24, new DateOnly(2026, 9, 13));

        Assert.Equal(9, pace.Count);
        Assert.All(pace, point => Assert.Equal(0, point.Actual));
        Assert.True(pace[^1].Required > pace[0].Required);
    }
}
