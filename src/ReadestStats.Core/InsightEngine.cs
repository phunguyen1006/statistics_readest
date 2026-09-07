namespace ReadestStats.Core;

public sealed class InsightEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly AnalyticsEngine _analytics;
    public InsightEngine(TimeZoneInfo? zone = null) { _statistics = new(zone); _analytics = new(zone); }

    public IReadOnlyList<InsightItem> Generate(IReadOnlyList<ReadingEvent> allEvents, IReadOnlyList<Book> books, ResolvedDateRange range, int gapMinutes, IReadOnlyList<GoalProgress>? goals = null)
    {
        var events = _analytics.Filter(allEvents, range); var daily = _statistics.Daily(events, TimeSpan.FromMinutes(gapMinutes)); var sessions = _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes)); var metrics = _analytics.Metrics(allEvents, range, gapMinutes); var comparison = _analytics.Compare(allEvents, range); var result = new List<InsightItem>();
        if (events.Count >= 5)
        {
            var window = _analytics.CommonWindow(allEvents, range); result.Add(new("reading-window", 100, "Reading rhythm", "Your strongest reading window", $"{window.StartHour:00}:00–{(window.StartHour + window.Hours) % 24:00}:00 contains {window.Share * 100:0}% of reading time."));
            var parts = new[] { ("morning", 5, 12), ("afternoon", 12, 17), ("evening", 17, 22), ("night", 22, 29) }; var byPart = parts.Select(p => (p.Item1, Seconds: events.Where(e => { var h = _statistics.ToLocal(e.Start).Hour; if (p.Item3 > 24 && h < 5) h += 24; return h >= p.Item2 && h < p.Item3; }).Sum(e => e.DurationSeconds))).MaxBy(x => x.Seconds); result.Add(new("day-part", 90, "Reading rhythm", $"You read most in the {byPart.Item1}", $"{(metrics.TotalSeconds <= 0 ? 0 : byPart.Seconds / metrics.TotalSeconds * 100):0}% of selected-period reading happens then."));
        }
        else result.Add(new("reading-window-insufficient", 10, "Reading rhythm", "More reading data is needed", "At least five activity events are needed to identify a dependable reading window.", "Limited sample"));
        result.Add(new("consistency", 85, "Consistency", $"{metrics.ConsistencyPercent:0}% consistency", $"{Formatters.Count(metrics.ActiveDays, "active day")} across {Formatters.Count(metrics.AvailableDays, "eligible day")} in this period."));
        if (daily.Count >= 4) { var split = _analytics.WeekdayWeekend(allEvents, range); result.Add(new("weekday-weekend", 75, "Consistency", "Weekday vs weekend", split.Summary)); }
        if (sessions.Count >= 3) { var profile = _analytics.Sessions(sessions); result.Add(new("session-profile", 80, "Session behavior", "Your typical session", $"Median {Formatters.Duration(profile.Median)}; average {Formatters.Duration(profile.Average)} across {Formatters.Count(sessions.Count, "session")}.")); }
        var top = _statistics.RankBooks(events, books).FirstOrDefault(); if (top is not null && events.Count >= 3) result.Add(new("top-book", 70, "Book behavior", "Most-active book", $"{top.Title} accounts for {top.Share * 100:0}% of selected-period reading."));
        result.Add(comparison.HasBaseline ? new("comparison", 95, "Period comparison", comparison.AbsoluteDelta >= 0 ? "Reading increased" : "Reading decreased", $"{Math.Abs(comparison.PercentDelta ?? 0):0.#}% versus the previous comparable period ({Formatters.Duration(Math.Abs(comparison.AbsoluteDelta))} difference).") : new("comparison-none", 30, "Period comparison", "No previous-period baseline", "There is not enough earlier activity for a meaningful comparison.", "Limited sample"));
        if (goals is not null) foreach (var goal in goals.Where(g => g.Definition.Enabled).Take(2)) result.Add(new("goal-" + goal.Definition.Id, 60, "Goal performance", $"{goal.Definition.Period} goal: {goal.Status}", $"{goal.CurrentLabel} of {goal.TargetLabel}; {goal.RemainingLabel} remaining."));
        return result.OrderByDescending(i => i.Priority).ToArray();
    }
}
