namespace ReadestStats.Core;

/// <summary>
/// Converts canonical reading events into presentation-ready, truthful chart series.
/// It never infers completion, genres, page counts, or reading speed.
/// </summary>
public sealed class VisualizationEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly TimeZoneInfo _zone;

    public VisualizationEngine(TimeZoneInfo? zone = null)
    {
        _zone = zone ?? TimeZoneInfo.Local;
        _statistics = new(_zone);
    }

    public ReadingStory Build(
        IReadOnlyList<ReadingEvent> allEvents,
        ResolvedDateRange range,
        ComparisonResult comparison,
        int sessionGapMinutes,
        DateTimeOffset? current = null,
        IReadOnlyList<DailyStat>? precomputedDaily = null)
    {
        var now = TimeZoneInfo.ConvertTime(current ?? DateTimeOffset.Now, _zone);
        var today = DateOnly.FromDateTime(now.DateTime);
        var allDaily = precomputedDaily ?? _statistics.Daily(allEvents, TimeSpan.FromMinutes(sessionGapMinutes));
        var periodEvents = allEvents.Where(e => e.DurationSeconds > 0 && e.Start >= range.Start && e.Start < range.End).ToArray();
        var periodDaily = _statistics.Daily(periodEvents, TimeSpan.FromMinutes(sessionGapMinutes));

        var streak = BuildStreakTimeline(allDaily, today);
        var activeLast30 = streak.Count(point => point.Value > 0);
        var streaks = _statistics.Streaks(allDaily.Select(day => day.Date), today);
        DateOnly? streakStart = streaks.Current <= 0 ? null : allDaily.Select(day => day.Date).Where(date => date <= today).OrderDescending().FirstOrDefault().AddDays(-(streaks.Current - 1));
        var streakContext = streaks.Current <= 0
            ? $"{activeLast30} of the last 30 days were active"
            : $"Current run started {streakStart:MMM d} · {activeLast30} of 30 days active";

        var hourlySeconds = Enumerable.Range(0, 24)
            .Select(hour => periodEvents.Where(item => _statistics.ToLocal(item.Start).Hour == hour).Sum(item => item.DurationSeconds))
            .ToArray();
        var bestWindow = Enumerable.Range(0, 24)
            .Select(hour => new { Hour = hour, Seconds = hourlySeconds[hour] + hourlySeconds[(hour + 1) % 24] })
            .MaxBy(item => item.Seconds);
        var peakWindow = bestWindow is null || bestWindow.Seconds <= 0
            ? "Not enough activity for a peak window"
            : $"Peak window {bestWindow.Hour:00}:00–{(bestWindow.Hour + 2) % 24:00}:00 · {Formatters.Duration(bestWindow.Seconds)}";

        var weekdayOrder = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var weekdayTotals = weekdayOrder.Select(day => new
        {
            Day = day,
            Seconds = periodEvents.Where(item => _statistics.ToLocal(item.Start).DayOfWeek == day).Sum(item => item.DurationSeconds)
        }).ToArray();
        var bestWeekday = weekdayTotals.MaxBy(item => item.Seconds);
        var peakWeekday = bestWeekday is null || bestWeekday.Seconds <= 0
            ? "Not enough activity for a weekday pattern"
            : $"Most active on {bestWeekday.Day} · {Formatters.Duration(bestWeekday.Seconds)}";

        var monthly = BuildMonthlyJourney(allEvents, now.Year, sessionGapMinutes);
        var bestMonth = monthly.Where(point => point.Value > 0).MaxBy(point => point.Value);

        return new(
            streak,
            BuildCumulativeJourney(periodDaily, range),
            monthly,
            [
                new("This period", comparison.CurrentValue / 60d, Formatters.Duration(comparison.CurrentValue)),
                new("Previous", comparison.PreviousValue / 60d, Formatters.Duration(comparison.PreviousValue))
            ],
            peakWindow,
            peakWeekday,
            bestMonth is null ? "No monthly activity yet" : $"Best month {bestMonth.Label} · {bestMonth.Detail}",
            streakContext,
            activeLast30);
    }

    private static IReadOnlyList<ChartPoint> BuildStreakTimeline(IReadOnlyList<DailyStat> daily, DateOnly today)
    {
        var map = daily.ToDictionary(item => item.Date);
        return Enumerable.Range(0, 30).Select(offset =>
        {
            var date = today.AddDays(offset - 29);
            var value = map.GetValueOrDefault(date);
            return new ChartPoint(date.ToString("yyyy-MM-dd"), value?.Seconds ?? 0,
                value is null
                    ? $"{date:MMM d, yyyy}\nNo activity"
                    : $"{date:MMM d, yyyy}\n{Formatters.Duration(value.Seconds)} · {Formatters.Count(value.Sessions, "session")}");
        }).ToArray();
    }

    private static IReadOnlyList<ChartPoint> BuildCumulativeJourney(IReadOnlyList<DailyStat> daily, ResolvedDateRange range)
    {
        if (daily.Count == 0) return [];
        var span = range.EndDate.DayNumber - range.StartDate.DayNumber + 1;
        IEnumerable<IGrouping<string, DailyStat>> groups = span switch
        {
            <= 90 => daily.GroupBy(item => item.Date.ToString("yyyy-MM-dd")),
            <= 550 => daily.GroupBy(item => item.Date.AddDays(-(((int)item.Date.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd")),
            _ => daily.GroupBy(item => new DateOnly(item.Date.Year, item.Date.Month, 1).ToString("yyyy-MM-dd"))
        };

        var total = 0d;
        return groups.OrderBy(group => group.Key).Select(group =>
        {
            total += group.Sum(item => item.Seconds);
            var date = DateOnly.Parse(group.Key);
            var label = span <= 90 ? date.ToString("MMM d") : span <= 550 ? date.ToString("MMM d") : date.ToString("MMM yy");
            return new ChartPoint(label, total / 3600d, $"{Formatters.Duration(total)} accumulated");
        }).ToArray();
    }

    private IReadOnlyList<ChartPoint> BuildMonthlyJourney(IReadOnlyList<ReadingEvent> events, int year, int sessionGapMinutes)
    {
        var daily = _statistics.Daily(events.Where(item => _statistics.ToLocal(item.Start).Year == year), TimeSpan.FromMinutes(sessionGapMinutes));
        return Enumerable.Range(1, 12).Select(month =>
        {
            var seconds = daily.Where(item => item.Date.Month == month).Sum(item => item.Seconds);
            return new ChartPoint(new DateTime(year, month, 1).ToString("MMM"), seconds / 3600d, Formatters.Duration(seconds));
        }).ToArray();
    }
}
