namespace ReadestStats.Core;

public sealed class InsightEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly AnalyticsEngine _analytics;

    public InsightEngine(TimeZoneInfo? zone = null)
    {
        _statistics = new(zone);
        _analytics = new(zone);
    }

    public IReadOnlyList<InsightItem> Generate(IReadOnlyList<ReadingEvent> allEvents, IReadOnlyList<Book> books, ResolvedDateRange range, int gapMinutes, IReadOnlyList<GoalProgress>? goals = null, bool use24HourTime = true)
    {
        var events = _analytics.Filter(allEvents, range);
        var daily = _statistics.Daily(events, TimeSpan.FromMinutes(gapMinutes));
        var sessions = _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes));
        var metrics = _analytics.Metrics(allEvents, range, gapMinutes);
        var comparison = _analytics.Compare(allEvents, range);
        var result = new List<InsightItem>();

        if (events.Count >= 5)
        {
            var window = _analytics.CommonWindow(allEvents, range);
            result.Add(new("reading-window", 100, "Reading rhythm", "Your strongest reading window",
                $"{HourRange(window.StartHour, window.Hours, use24HourTime)} contains {window.Share * 100:0}% of reading time.",
                $"Based on {Formatters.Count(events.Count, "event")}",
                "Recorded active seconds were grouped into every rolling two-hour window. The window with the highest total is shown; all times use your local timezone."));

            var parts = new[] { (Name: "morning", Start: 5, End: 12), (Name: "afternoon", Start: 12, End: 17), (Name: "evening", Start: 17, End: 22), (Name: "night", Start: 22, End: 29) };
            var byPart = parts.Select(part => (part.Name, Seconds: events.Where(e =>
            {
                var hour = _statistics.ToLocal(e.Start).Hour;
                if (part.End > 24 && hour < 5) hour += 24;
                return hour >= part.Start && hour < part.End;
            }).Sum(e => e.DurationSeconds))).MaxBy(x => x.Seconds);
            result.Add(new("day-part", 90, "Reading rhythm", $"You read most in the {byPart.Name}",
                $"{(metrics.TotalSeconds <= 0 ? 0 : byPart.Seconds / metrics.TotalSeconds * 100):0}% of selected-period reading happens then.",
                $"Based on {Formatters.Count(events.Count, "event")}",
                $"Each activity event was assigned to morning ({HourRange(5, 7, use24HourTime)}), afternoon ({HourRange(12, 5, use24HourTime)}), evening ({HourRange(17, 5, use24HourTime)}), or night ({HourRange(22, 7, use24HourTime)}) in your local time. The period with the most active seconds is shown."));
        }
        else
        {
            result.Add(new("reading-window-insufficient", 10, "Reading rhythm", "More reading data is needed",
                "At least five activity events are needed to identify a dependable reading window.",
                $"Only {Formatters.Count(events.Count, "event")}",
                "A reading window is intentionally not inferred until the selected period contains at least five recorded activity events."));
        }

        result.Add(new("consistency", 85, "Consistency", $"{metrics.ConsistencyPercent:0}% consistency",
            $"{Formatters.Count(metrics.ActiveDays, "active day")} across {Formatters.Count(metrics.AvailableDays, "eligible day")} in this period.",
            $"Based on {Formatters.Count(metrics.AvailableDays, "eligible day")}",
            "Consistency is active days divided by eligible calendar days. Days before your first recorded event are excluded, while inactive eligible days are included."));

        if (daily.Count >= 4)
        {
            var split = _analytics.WeekdayWeekend(allEvents, range);
            result.Add(new("weekday-weekend", 75, "Consistency", "Weekday vs weekend", split.Summary,
                $"Based on {Formatters.Count(daily.Count, "active day")}",
                "Recorded active seconds are divided by all eligible weekdays and weekend days in the selected period, including eligible days with no activity."));
        }

        if (sessions.Count >= 3)
        {
            var profile = _analytics.Sessions(sessions);
            result.Add(new("session-profile", 80, "Session behavior", "Your typical session",
                $"Median {Formatters.Duration(profile.Median)}; average {Formatters.Duration(profile.Average)} across {Formatters.Count(sessions.Count, "session")}.",
                $"Based on {Formatters.Count(sessions.Count, "session")}",
                $"Sessions are reconstructed from nearby Readest activity using the configured {gapMinutes}-minute gap. The median is used as the typical value because it is less affected by unusually long sessions."));
        }

        var top = _statistics.RankBooks(events, books).FirstOrDefault();
        if (top is not null && events.Count >= 3)
        {
            result.Add(new("top-book", 70, "Book behavior", "Most-active book",
                $"{top.Title} accounts for {top.Share * 100:0}% of selected-period reading.",
                $"Based on {Formatters.Count(events.Count, "event")}",
                "Active seconds were summed by book for the selected period, then divided by total recorded reading time. This describes activity, not whether a book was completed."));
        }

        result.Add(comparison.HasBaseline
            ? new("comparison", 95, "Period comparison", comparison.AbsoluteDelta >= 0 ? "Reading increased" : "Reading decreased",
                $"{Math.Abs(comparison.PercentDelta ?? 0):0.#}% versus the previous comparable period ({Formatters.Duration(Math.Abs(comparison.AbsoluteDelta))} difference).",
                "Two equal periods",
                "The selected period is compared with the immediately preceding period of the same elapsed length. The percentage and absolute active-time difference come from recorded events in those two ranges.")
            : new("comparison-none", 30, "Period comparison", "No previous-period baseline",
                "There is not enough earlier activity for a meaningful comparison.",
                "No earlier activity",
                "A comparison is shown only when the immediately preceding equal-length period contains recorded reading time; this avoids misleading percentages from a zero baseline."));

        if (goals is not null)
        {
            foreach (var goal in goals.Where(g => g.Definition.Enabled).Take(2))
            {
                result.Add(new("goal-" + goal.Definition.Id, 60, "Goal performance", $"{goal.Definition.Period} goal: {goal.Status}",
                    $"{goal.CurrentLabel} of {goal.TargetLabel}; {goal.RemainingLabel} remaining.",
                    "Current goal data",
                    "Progress uses the goal target saved in this app and the matching recorded activity period. Reading-time goals use active seconds; book goals count unique titles with activity."));
            }
        }

        return result.OrderByDescending(i => i.Priority).ToArray();
    }

    private static string HourRange(int startHour, int hours, bool use24HourTime)
    {
        string Format(int hour) => DateTime.Today.AddHours(hour % 24).ToString(use24HourTime ? "HH:mm" : "h:mm tt");
        return $"{Format(startHour)}–{Format(startHour + hours)}";
    }
}
