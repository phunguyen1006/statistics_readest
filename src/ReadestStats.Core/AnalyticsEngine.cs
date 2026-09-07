namespace ReadestStats.Core;

public sealed class AnalyticsEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly DateRangeService _ranges;
    public AnalyticsEngine(TimeZoneInfo? zone = null) { _statistics = new(zone); _ranges = new(zone); }

    public IReadOnlyList<ReadingEvent> Filter(IEnumerable<ReadingEvent> source, ResolvedDateRange range) => source.Where(e => e.Start >= range.Start && e.Start < range.End && e.DurationSeconds > 0).ToArray();
    public PeriodMetrics Metrics(IReadOnlyList<ReadingEvent> source, ResolvedDateRange range, int gapMinutes)
    {
        var events = Filter(source, range); var daily = _statistics.Daily(events, TimeSpan.FromMinutes(gapMinutes)); var sessions = _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes)); var total = events.Sum(e => e.DurationSeconds);
        return new(total, daily.Count, range.AvailableDays, sessions.Count, events.Select(e => e.BookId).Distinct().Count(), daily.Count == 0 ? 0 : total / daily.Count, range.AvailableDays == 0 ? 0 : total / range.AvailableDays, sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds), range.AvailableDays == 0 ? 0 : daily.Count * 100d / range.AvailableDays);
    }

    public ComparisonResult Compare(IReadOnlyList<ReadingEvent> source, ResolvedDateRange range)
    {
        var current = source.Where(e => e.Start >= range.Start && e.Start < range.End).Sum(e => e.DurationSeconds); var previous = source.Where(e => e.Start >= range.PreviousStart && e.Start < range.PreviousEnd).Sum(e => e.DurationSeconds); var baseline = previous > 0;
        return new(current, previous, current - previous, baseline ? (current - previous) / previous * 100 : null, baseline);
    }

    public SessionProfile Sessions(IEnumerable<ReadingSession> source)
    {
        var values = source.Select(s => s.DurationSeconds).Where(x => x > 0).Order().ToArray();
        var buckets = new[] { ("<5m", 0d, 300d), ("5–10m", 300d, 600d), ("10–20m", 600d, 1200d), ("20–30m", 1200d, 1800d), ("30–60m", 1800d, 3600d), ("60m+", 3600d, double.MaxValue) };
        var chart = buckets.Select(b => new ChartPoint(b.Item1, values.Count(x => x >= b.Item2 && x < b.Item3), $"{values.Count(x => x >= b.Item2 && x < b.Item3)} sessions")).ToArray();
        return new(values.Length == 0 ? 0 : values.Average(), StatisticsEngine.Median(values), values.Length == 0 ? 0 : values[^1], values.Length == 0 ? 0 : values[0], Percentile(values, .25), Percentile(values, .75), chart);
    }

    public WeekdayWeekendStats WeekdayWeekend(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range)
    {
        var daily = _statistics.Daily(Filter(events, range), TimeSpan.FromMinutes(5)); var weekdays = daily.Where(d => d.Date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).ToArray(); var weekends = daily.Where(d => d.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToArray();
        var firstEligible = events.Count == 0 ? range.EndDate.AddDays(1) : DateOnly.FromDateTime(_statistics.ToLocal(events.Min(e => e.Start)).DateTime); if (firstEligible < range.StartDate) firstEligible = range.StartDate; var eligibleDates = firstEligible > range.EndDate ? [] : Enumerable.Range(0, range.EndDate.DayNumber - firstEligible.DayNumber + 1).Select(firstEligible.AddDays).ToArray(); var availableWeekdays = eligibleDates.Count(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday); var availableWeekends = eligibleDates.Length - availableWeekdays;
        var wd = weekdays.Sum(x => x.Seconds); var we = weekends.Sum(x => x.Seconds); var wdAvg = availableWeekdays == 0 ? 0 : wd / availableWeekdays; var weAvg = availableWeekends == 0 ? 0 : we / availableWeekends;
        var summary = weekdays.Length + weekends.Length < 4 ? "More reading data is needed for this comparison." : weAvg > wdAvg ? $"You read {(wdAvg <= 0 ? 0 : (weAvg - wdAvg) / wdAvg * 100):0}% more per active day on weekends." : $"You read {(weAvg <= 0 ? 0 : (wdAvg - weAvg) / weAvg * 100):0}% more per active day on weekdays.";
        return new(wd, we, wdAvg, weAvg, summary);
    }

    public ReadingWindow CommonWindow(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, int windowHours = 2)
    {
        var filtered = Filter(events, range); var hourly = Enumerable.Range(0, 24).Select(h => filtered.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds)).ToArray(); var best = Enumerable.Range(0, 24).Select(h => new { Hour = h, Seconds = Enumerable.Range(0, windowHours).Sum(i => hourly[(h + i) % 24]) }).MaxBy(x => x.Seconds)!; var total = hourly.Sum(); return new(best.Hour, windowHours, best.Seconds, total <= 0 ? 0 : best.Seconds / total);
    }

    public YearInReading Year(IReadOnlyList<ReadingEvent> all, IReadOnlyList<Book> books, int year, int gapMinutes)
    {
        var events = all.Where(e => _statistics.ToLocal(e.Start).Year == year).ToArray(); var daily = _statistics.Daily(events, TimeSpan.FromMinutes(gapMinutes)); var sessions = _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes)); var months = Enumerable.Range(1, 12).Select(m => new ChartPoint(new DateTime(year, m, 1).ToString("MMM"), daily.Where(d => d.Date.Month == m).Sum(d => d.Seconds) / 3600, Formatters.Duration(daily.Where(d => d.Date.Month == m).Sum(d => d.Seconds)))).ToArray(); var rankings = _statistics.RankBooks(events, books); var dates = daily.Select(d => d.Date).ToArray(); var heatStart = new DateOnly(year, 1, 1); var map = daily.ToDictionary(d => d.Date); var heat = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Select(i => { var date = heatStart.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("MMM d, yyyy"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : Formatters.Duration(d.Seconds)); }).ToArray(); var hourly = Enumerable.Range(0, 24).Select(h => new ChartPoint($"{h:00}:00–{(h + 1) % 24:00}:00", events.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds))).ToArray(); var weekdays = Enum.GetValues<DayOfWeek>().Select(day => new ChartPoint(day.ToString(), events.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds))).ToArray();
        return new(year, events.Sum(e => e.DurationSeconds), daily.Count, sessions.Count, events.Select(e => e.BookId).Distinct().Count(), dates.Length == 0 ? 0 : _statistics.Streaks(dates, dates.Max()).Longest, daily.MaxBy(d => d.Seconds), months.MaxBy(m => m.Value), rankings.FirstOrDefault(), hourly.MaxBy(h => h.Value), weekdays.MaxBy(d => d.Value), months, heat, rankings.Take(5).ToArray());
    }

    public IReadOnlyList<ChartPoint> AggregateTrend(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, string metric, string granularity, int gapMinutes)
    {
        var filtered = Filter(events, range); var days = range.EndDate.DayNumber - range.StartDate.DayNumber + 1; var unit = granularity == "Auto" ? (days > 370 ? "Month" : days > 100 ? "Week" : "Day") : granularity; var sessions = _statistics.BuildSessions(filtered, TimeSpan.FromMinutes(gapMinutes));
        string Key(DateOnly date) => unit switch { "Month" => $"{date:yyyy-MM}", "Week" => $"{date.AddDays(-(((int)date.DayOfWeek + 6) % 7)):yyyy-MM-dd}", _ => date.ToString("yyyy-MM-dd") };
        var dates = Enumerable.Range(0, Math.Max(1, days)).Select(i => range.StartDate.AddDays(i)).GroupBy(Key).Select(g => (Key: g.Key, Label: unit == "Month" ? DateOnly.ParseExact(g.Key + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("MMM yyyy") : DateOnly.ParseExact(g.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("MMM d"))).ToArray();
        return dates.Select(bucket => { var bucketEvents = filtered.Where(e => Key(DateOnly.FromDateTime(_statistics.ToLocal(e.Start).DateTime)) == bucket.Key).ToArray(); var value = metric switch { "Sessions" => sessions.Count(s => Key(DateOnly.FromDateTime(_statistics.ToLocal(s.Start).DateTime)) == bucket.Key), "Active books" => bucketEvents.Select(e => e.BookId).Distinct().Count(), _ => bucketEvents.Sum(e => e.DurationSeconds) / 60d }; return new ChartPoint(bucket.Label, value, metric == "Reading time" ? Formatters.Duration(value * 60) : $"{value:0} {metric.ToLowerInvariant()}"); }).ToArray();
    }

    private static double Percentile(double[] ordered, double p) { if (ordered.Length == 0) return 0; var position = (ordered.Length - 1) * p; var lower = (int)Math.Floor(position); var upper = (int)Math.Ceiling(position); return lower == upper ? ordered[lower] : ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower); }
}
