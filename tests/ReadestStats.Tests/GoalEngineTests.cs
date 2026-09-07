using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class GoalEngineTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC", TimeSpan.Zero, "UTC", "UTC");
    private readonly GoalEngine _goals = new(Zone);
    private static DateTimeOffset At(int y, int m, int d, int h = 12) => new(y, m, d, h, 0, 0, TimeSpan.Zero);
    private static ReadingEvent E(DateTimeOffset at, double seconds = 60, long bookId = 1) => new(bookId, 1, at.ToUnixTimeSeconds(), seconds, null);

    [Fact] public void MigrationPreservesDailyTimeAndCreatesBookGoals()
    {
        var migrated = GoalEngine.Migrate(new() { DailyGoalMinutes = 20, WeeklyGoalMinutes = 100, MonthlyGoalHours = 9 });
        Assert.Equal(1200, migrated.Single(x => x.Period == GoalPeriod.Daily).TargetValue);
        Assert.Equal(GoalMetric.ReadingTime, migrated.Single(x => x.Period == GoalPeriod.Daily).Metric);
        Assert.Equal((GoalMetric.Books, 1d), (migrated.Single(x => x.Period == GoalPeriod.Weekly).Metric, migrated.Single(x => x.Period == GoalPeriod.Weekly).TargetValue));
        Assert.Equal((GoalMetric.Books, 4d), (migrated.Single(x => x.Period == GoalPeriod.Monthly).Metric, migrated.Single(x => x.Period == GoalPeriod.Monthly).TargetValue));
        Assert.Equal((GoalMetric.Books, 24d), (migrated.Single(x => x.Period == GoalPeriod.Yearly).Metric, migrated.Single(x => x.Period == GoalPeriod.Yearly).TargetValue));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(60, false)]
    public void ZeroOrDisabledGoalIsNotSet(double target, bool enabled)
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Daily, TargetValue = target, Enabled = enabled };
        Assert.Equal("Not set", _goals.Progress(goal, [], 5, At(2026, 9, 7)).Status);
    }

    [Fact] public void CompletedGoalIsCappedAndMarkedComplete()
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Daily, TargetValue = 60 };
        var progress = _goals.Progress(goal, new[] { E(At(2026, 9, 7), 180) }, 5, At(2026, 9, 7, 18));
        Assert.Equal("Complete ✓", progress.Status);
        Assert.Equal(100, progress.ProgressPercent);
        Assert.Equal(0, progress.Remaining);
    }

    [Fact] public void PeriodJustStartedDoesNotInventProjection()
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Monthly, TargetValue = 3600 };
        var progress = _goals.Progress(goal, new[] { E(At(2026, 9, 1), 60) }, 5, At(2026, 9, 1));
        Assert.Null(progress.Projected);
        Assert.Equal("Not enough data", progress.Status);
    }

    [Fact] public void LastDayProjectionUsesLeapMonthLength()
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Monthly, TargetValue = 2900 };
        var events = Enumerable.Range(1, 29).Select(d => E(At(2024, 2, d), 100)).ToArray();
        var progress = _goals.Progress(goal, events, 5, At(2024, 2, 29, 23));
        Assert.Equal(2900, progress.Projected);
        Assert.Equal("Complete ✓", progress.Status);
    }

    [Fact] public void YearlyProjectionHonorsLeapYear()
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Yearly, TargetValue = 36600 };
        Assert.Equal(36600, _goals.Progress(goal, new[] { E(At(2024, 1, 1), 200) }, 5, At(2024, 1, 2)).Projected);
    }

    [Fact] public void HistoryCrossesDecemberJanuaryAndTracksHits()
    {
        var goal = new GoalDefinition { Period = GoalPeriod.Monthly, TargetValue = 60 };
        var history = _goals.History(goal, new[] { E(At(2025, 12, 20), 60), E(At(2026, 1, 20), 30) }, 5, At(2026, 2, 2));
        Assert.Equal(12, history.Total);
        Assert.Equal(1, history.Hit);
        Assert.Equal(1, history.BestStreak);
    }

    [Fact] public void CountGoalsSupportActiveDaysAndSessions()
    {
        var events = new[] { E(At(2026, 9, 7, 9)), E(At(2026, 9, 7, 10)) };
        Assert.Equal(1, _goals.Progress(new() { Period = GoalPeriod.Daily, Metric = GoalMetric.ActiveDays, TargetValue = 1 }, events, 5, At(2026, 9, 7, 18)).Current);
        Assert.Equal(2, _goals.Progress(new() { Period = GoalPeriod.Daily, Metric = GoalMetric.Sessions, TargetValue = 2 }, events, 5, At(2026, 9, 7, 18)).Current);
    }

    [Fact] public void BookGoalCountsEachTitleOnceWithinPeriod()
    {
        var events = new[] { E(At(2026, 9, 2), bookId: 1), E(At(2026, 9, 3), bookId: 1), E(At(2026, 9, 4), bookId: 2) };
        var progress = _goals.Progress(new() { Period = GoalPeriod.Monthly, Metric = GoalMetric.Books, TargetValue = 4 }, events, 5, At(2026, 9, 7));
        Assert.Equal(2, progress.Current);
        Assert.Equal("2 books", progress.CurrentLabel);
    }

    [Fact] public void PastYearsAreArchivedOnceAndCurrentYearIsNot()
    {
        var settings = new AppSettings();
        GoalEngine.Migrate(settings).Single(g => g.Period == GoalPeriod.Yearly).TargetValue = 12;
        var events = new[] { E(At(2024, 2, 1), bookId: 1), E(At(2024, 5, 1), bookId: 2), E(At(2026, 1, 1), bookId: 3) };
        Assert.True(_goals.SyncYearArchives(settings, events, At(2026, 9, 7)));
        Assert.False(_goals.SyncYearArchives(settings, events, At(2026, 9, 7)));
        var archive = Assert.Single(settings.GoalArchives);
        Assert.Equal(2024, archive.Year);
        Assert.Equal(2, archive.BooksRead);
        Assert.Equal(12, archive.TargetBooks);
    }

    [Theory]
    [InlineData(9, "9s")]
    [InlineData(75, "1m 15s")]
    [InlineData(660, "11m")]
    [InlineData(28800, "8h")]
    [InlineData(456180, "126h 43m")]
    public void DurationFormattingIsHumanFriendly(double seconds, string expected) => Assert.Equal(expected, Formatters.Duration(seconds));

    [Fact] public void PluralizationHandlesOne() { Assert.Equal("1 day", Formatters.Count(1, "day")); Assert.Equal("2 days", Formatters.Count(2, "day")); }
}
