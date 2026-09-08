namespace ReadestStats.Core;

public sealed class GoalEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly DateRangeService _ranges;
    private readonly TimeZoneInfo _zone;
    public GoalEngine(TimeZoneInfo? zone = null) { _zone = zone ?? TimeZoneInfo.Local; _statistics = new(_zone); _ranges = new(_zone); }

    public static List<GoalDefinition> Migrate(AppSettings settings)
    {
        if (settings.GoalSchemaVersion >= 2 && HasExpectedGoals(settings.Goals)) return settings.Goals;
        var oldDaily = settings.Goals.FirstOrDefault(g => g.Period == GoalPeriod.Daily && g.Metric == GoalMetric.ReadingTime);
        settings.Goals =
        [
            new() { Id = oldDaily?.Id ?? Guid.NewGuid().ToString("N"), Period = GoalPeriod.Daily, Metric = GoalMetric.ReadingTime, TargetValue = oldDaily?.TargetValue ?? Math.Max(0, settings.DailyGoalMinutes) * 60, Enabled = oldDaily?.Enabled ?? true },
            new() { Period = GoalPeriod.Weekly, Metric = GoalMetric.Books, TargetValue = 1 },
            new() { Period = GoalPeriod.Monthly, Metric = GoalMetric.Books, TargetValue = 4 },
            new() { Period = GoalPeriod.Yearly, Metric = GoalMetric.Books, TargetValue = 24 }
        ];
        settings.GoalSchemaVersion = 2;
        return settings.Goals;
    }

    private static bool HasExpectedGoals(IReadOnlyCollection<GoalDefinition> goals) =>
        goals.Count == 4 &&
        goals.Any(g => g.Period == GoalPeriod.Daily && g.Metric == GoalMetric.ReadingTime) &&
        new[] { GoalPeriod.Weekly, GoalPeriod.Monthly, GoalPeriod.Yearly }.All(period => goals.Any(g => g.Period == period && g.Metric == GoalMetric.Books));

    public bool SyncYearArchives(AppSettings settings, IReadOnlyList<ReadingEvent> events, DateTimeOffset? current = null)
    {
        Migrate(settings);
        settings.GoalArchives ??= [];
        var currentYear = TimeZoneInfo.ConvertTime(current ?? DateTimeOffset.Now, _zone).Year;
        var target = settings.Goals.First(g => g.Period == GoalPeriod.Yearly && g.Metric == GoalMetric.Books).TargetValue;
        var years = events.Select(e => _statistics.ToLocal(e.Start).Year).Where(year => year < currentYear).Distinct().OrderBy(year => year);
        var changed = false;
        foreach (var year in years)
        {
            if (settings.GoalArchives.Any(a => a.Year == year)) continue;
            settings.GoalArchives.Add(new GoalArchive
            {
                Year = year,
                TargetBooks = target,
                BooksRead = events.Where(e => _statistics.ToLocal(e.Start).Year == year).Select(e => e.BookId).Distinct().Count(),
                ArchivedAtUtc = current ?? DateTimeOffset.UtcNow
            });
            changed = true;
        }
        if (changed) settings.GoalArchives = settings.GoalArchives.OrderByDescending(a => a.Year).ToList();
        return changed;
    }

    public GoalProgress Progress(GoalDefinition goal, IReadOnlyList<ReadingEvent> events, int gapMinutes, DateTimeOffset? current = null, IReadOnlyList<DateTimeOffset>? completedBooks = null)
    {
        var now = current ?? DateTimeOffset.Now; var local = TimeZoneInfo.ConvertTime(now, _zone); var today = DateOnly.FromDateTime(local.DateTime); var (start, end, fullDays, elapsedDays) = Bounds(goal.Period, today, now); var periodEvents = events.Where(e => e.Start >= start && e.Start < end).ToArray(); var value = goal.Metric == GoalMetric.Books && completedBooks is not null ? completedBooks.Count(date => date >= start && date < end) : Value(goal.Metric, periodEvents, gapMinutes); var target = Math.Max(0, goal.TargetValue); var progress = target <= 0 ? 0 : value / target; var elapsed = fullDays <= 0 ? 0 : (double)elapsedDays / fullDays; var pace = elapsed <= 0 ? 0 : progress / elapsed; var remaining = Math.Max(0, target - value); var remainingDays = Math.Max(1, fullDays - elapsedDays + 1); var required = remaining / remainingDays; double? projected = elapsedDays < 2 ? null : value / elapsedDays * fullDays; var status = !goal.Enabled || target <= 0 ? "Not set" : value >= target ? "Complete ✓" : elapsedDays < 2 ? "Not enough data" : pace >= 1.08 ? "Ahead ↑" : pace >= .92 ? "On track —" : "Behind ↓";
        return new(goal, value, target, Math.Min(100, progress * 100), Math.Min(100, elapsed * 100), remaining, required, projected, status, Label(goal.Metric, value), Label(goal.Metric, target), Label(goal.Metric, remaining), goal.Metric switch { GoalMetric.ReadingTime => $"{Formatters.Duration(required)}/day", GoalMetric.Books => $"{required:0.#} books/day", _ => $"{required:0.#}/day" }, projected is null ? "Not enough data" : Label(goal.Metric, projected.Value));
    }

    public GoalHistory History(GoalDefinition goal, IReadOnlyList<ReadingEvent> events, int gapMinutes, DateTimeOffset? current = null, IReadOnlyList<DateTimeOffset>? completedBooks = null)
    {
        var now = current ?? DateTimeOffset.Now; var local = TimeZoneInfo.ConvertTime(now, _zone); var today = DateOnly.FromDateTime(local.DateTime); var count = goal.Period switch { GoalPeriod.Daily => 30, GoalPeriod.Weekly => 12, GoalPeriod.Monthly => 12, _ => 5 }; var points = new List<ChartPoint>(); var hits = new List<bool>();
        for (var i = count; i >= 1; i--)
        {
            var anchor = goal.Period switch { GoalPeriod.Daily => today.AddDays(-i), GoalPeriod.Weekly => today.AddDays(-7 * i), GoalPeriod.Monthly => today.AddMonths(-i), _ => today.AddYears(-i) }; var (start, end, _, _) = CompletedBounds(goal.Period, anchor); var value = goal.Metric == GoalMetric.Books && completedBooks is not null ? completedBooks.Count(date => date >= start && date < end) : Value(goal.Metric, events.Where(e => e.Start >= start && e.Start < end).ToArray(), gapMinutes); var hit = goal.Enabled && goal.TargetValue > 0 && value >= goal.TargetValue; hits.Add(hit); points.Add(new(anchor.ToString(goal.Period == GoalPeriod.Daily ? "MMM d" : goal.Period == GoalPeriod.Monthly ? "MMM yy" : goal.Period == GoalPeriod.Yearly ? "yyyy" : "MMM d"), hit ? 1 : 0, $"{Label(goal.Metric, value)} · {(hit ? "Goal hit" : "Not hit")}"));
        }
        var best = 0; var run = 0; foreach (var hit in hits) { run = hit ? run + 1 : 0; best = Math.Max(best, run); } var totalHit = hits.Count(x => x); return new(goal.Period.ToString(), totalHit, hits.Count, hits.Count == 0 ? 0 : totalHit * 100d / hits.Count, best, points);
    }

    private double Value(GoalMetric metric, IReadOnlyList<ReadingEvent> events, int gapMinutes) => metric switch { GoalMetric.ActiveDays => events.Select(e => DateOnly.FromDateTime(_statistics.ToLocal(e.Start).DateTime)).Distinct().Count(), GoalMetric.Sessions => _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes)).Count, GoalMetric.Books => events.Select(e => e.BookId).Distinct().Count(), _ => events.Sum(e => e.DurationSeconds) };
    private static string Label(GoalMetric metric, double value) => metric switch { GoalMetric.ReadingTime => Formatters.Duration(value), GoalMetric.Books => Formatters.Count((int)Math.Round(value), "book"), _ => $"{value:0.#}" };

    private (DateTimeOffset Start, DateTimeOffset End, int FullDays, int ElapsedDays) Bounds(GoalPeriod period, DateOnly today, DateTimeOffset now)
    {
        DateOnly start; DateOnly finish;
        switch (period) { case GoalPeriod.Daily: start = finish = today; break; case GoalPeriod.Weekly: start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); finish = start.AddDays(6); break; case GoalPeriod.Monthly: start = new(today.Year, today.Month, 1); finish = start.AddMonths(1).AddDays(-1); break; default: start = new(today.Year, 1, 1); finish = new(today.Year, 12, 31); break; }
        return (_ranges.ToUtc(start), now.AddMilliseconds(1), finish.DayNumber - start.DayNumber + 1, today.DayNumber - start.DayNumber + 1);
    }

    private (DateTimeOffset Start, DateTimeOffset End, int FullDays, int ElapsedDays) CompletedBounds(GoalPeriod period, DateOnly anchor)
    {
        DateOnly start; DateOnly finish;
        switch (period) { case GoalPeriod.Daily: start = finish = anchor; break; case GoalPeriod.Weekly: start = anchor.AddDays(-(((int)anchor.DayOfWeek + 6) % 7)); finish = start.AddDays(6); break; case GoalPeriod.Monthly: start = new(anchor.Year, anchor.Month, 1); finish = start.AddMonths(1).AddDays(-1); break; default: start = new(anchor.Year, 1, 1); finish = new(anchor.Year, 12, 31); break; }
        var days = finish.DayNumber - start.DayNumber + 1; return (_ranges.ToUtc(start), _ranges.ToUtc(finish.AddDays(1)), days, days);
    }
}
