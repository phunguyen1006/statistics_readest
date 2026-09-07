namespace ReadestStats.Core;

public sealed class StatisticsEngine
{
    private readonly TimeZoneInfo _zone;
    public StatisticsEngine(TimeZoneInfo? zone = null) => _zone = zone ?? TimeZoneInfo.Local;

    public DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);
    public DateOnly LocalDate(ReadingEvent value) => DateOnly.FromDateTime(ToLocal(value.Start).DateTime);

    public IReadOnlyList<ReadingSession> BuildSessions(IEnumerable<ReadingEvent> source, TimeSpan gap, long? perBook = null)
    {
        var events = source.Where(e => e.DurationSeconds > 0 && (perBook is null || e.BookId == perBook)).OrderBy(e => e.StartTime).ToList();
        if (events.Count == 0) return [];
        var result = new List<ReadingSession>();
        var group = new List<ReadingEvent> { events[0] };
        var end = events[0].End;
        foreach (var item in events.Skip(1))
        {
            if (item.Start <= end + gap)
            {
                group.Add(item);
                if (item.End > end) end = item.End;
            }
            else
            {
                result.Add(ToSession(group));
                group = [item];
                end = item.End;
            }
        }
        result.Add(ToSession(group));
        return result;
    }

    private static ReadingSession ToSession(IReadOnlyList<ReadingEvent> events)
    {
        var start = events.Min(e => e.Start);
        var end = events.Max(e => e.End);
        var duration = MergeDuration(events);
        return new(start, end, duration, events.Select(e => e.BookId).Distinct().ToArray(), events.Count);
    }

    private static double MergeDuration(IEnumerable<ReadingEvent> events)
    {
        var ranges = events.Select(e => (Start: e.Start, End: e.End)).OrderBy(x => x.Start).ToList();
        if (ranges.Count == 0) return 0;
        var total = TimeSpan.Zero;
        var start = ranges[0].Start;
        var end = ranges[0].End;
        foreach (var range in ranges.Skip(1))
        {
            if (range.Start <= end) { if (range.End > end) end = range.End; }
            else { total += end - start; start = range.Start; end = range.End; }
        }
        return (total + (end - start)).TotalSeconds;
    }

    public IReadOnlyList<DailyStat> Daily(IEnumerable<ReadingEvent> source, TimeSpan sessionGap)
    {
        var events = source.Where(e => e.DurationSeconds > 0).ToList();
        var sessionDates = BuildSessions(events, sessionGap).GroupBy(s => DateOnly.FromDateTime(ToLocal(s.Start).DateTime)).ToDictionary(g => g.Key, g => g.Count());
        return events.GroupBy(LocalDate).OrderBy(g => g.Key).Select(g => new DailyStat(g.Key, g.Sum(e => e.DurationSeconds), g.Select(e => e.BookId).Distinct().Count(), sessionDates.GetValueOrDefault(g.Key))).ToArray();
    }

    public (int Current, int Longest) Streaks(IEnumerable<DateOnly> dates, DateOnly today)
    {
        var ordered = dates.Distinct().Order().ToArray();
        if (ordered.Length == 0) return (0, 0);
        var longest = 1;
        var run = 1;
        for (var i = 1; i < ordered.Length; i++)
        {
            run = ordered[i].DayNumber == ordered[i - 1].DayNumber + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }
        var last = ordered[^1];
        if (last != today && last != today.AddDays(-1)) return (0, longest);
        var current = 1;
        for (var i = ordered.Length - 1; i > 0 && ordered[i - 1].DayNumber == ordered[i].DayNumber - 1; i--) current++;
        return (current, longest);
    }

    public PeriodComparison Compare(IEnumerable<ReadingEvent> source, DateTimeOffset periodStart, DateTimeOffset periodEnd)
    {
        var seconds = (periodEnd - periodStart).TotalSeconds;
        var previousStart = periodStart.AddSeconds(-seconds);
        var list = source.ToList();
        double Sum(DateTimeOffset a, DateTimeOffset b) => list.Where(e => e.Start >= a && e.Start < b).Sum(e => e.DurationSeconds);
        var current = Sum(periodStart, periodEnd);
        var previous = Sum(previousStart, periodStart);
        return new(current, previous, previous <= 0 ? null : (current - previous) / previous * 100);
    }

    public OverviewStats Overview(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, int rangeDays, TimeSpan sessionGap, DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.Now;
        var localNow = ToLocal(current);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var daily = Daily(events, sessionGap);
        var streaks = Streaks(daily.Select(d => d.Date), today);
        var currentStartLocal = localNow.Date.AddDays(-(rangeDays - 1));
        var start = ToUtc(currentStartLocal);
        var end = current;
        var comparison = Compare(events, start, end);
        var sessions = BuildSessions(events, sessionGap);
        var periodEvents = events.Where(e => e.Start >= start && e.Start < end).ToArray();
        var totalSeconds = periodEvents.Sum(e => e.DurationSeconds);
        var hourly = Enumerable.Range(0, 24).Select(h => { var value = periodEvents.Where(e => ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds); return new ChartPoint($"{h:00}:00", value / 60d, $"{Formatters.Duration(value)} · {(totalSeconds <= 0 ? 0 : value / totalSeconds * 100):0.#}% of period"); }).ToArray();
        var weekdays = Enumerable.Range(1, 7).Select(i => (Day: (DayOfWeek)(i % 7), Name: ((DayOfWeek)(i % 7)).ToString())).Select(x => new ChartPoint(x.Name[..3], periodEvents.Where(e => ToLocal(e.Start).DayOfWeek == x.Day).Sum(e => e.DurationSeconds) / 60d)).ToArray();
        var records = Records(events, books, daily, sessions, hourly, weekdays);
        var insights = Insights(events, daily, sessions, hourly, weekdays, streaks.Current, current);
        return new(events.Sum(e => e.DurationSeconds), daily.FirstOrDefault(d => d.Date == today)?.Seconds ?? 0, streaks.Current, streaks.Longest, daily.Count, events.Select(e => e.BookId).Distinct().Count(), comparison, daily.Where(d => d.Date >= today.AddDays(-(rangeDays - 1))).ToArray(), hourly, weekdays, records, insights);
    }

    private DateTimeOffset ToUtc(DateTime localUnspecified)
    {
        var unspecified = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, _zone), TimeSpan.Zero);
    }

    public IReadOnlyList<BookSummary> RankBooks(IEnumerable<ReadingEvent> source, IEnumerable<Book> books)
    {
        var events = source.ToList();
        var total = events.Sum(e => e.DurationSeconds);
        return books.Join(events.GroupBy(e => e.BookId), b => b.Id, g => g.Key, (b, g) =>
        {
            var list = g.ToList();
            var latest = list.MaxBy(e => e.StartTime)!;
            return new BookSummary(b.Id, string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title, string.IsNullOrWhiteSpace(b.Authors) ? "Unknown author" : b.Authors, list.Sum(e => e.DurationSeconds), list.Select(LocalDate).Distinct().Count(), list.Min(e => e.Start), list.Max(e => e.Start), list.Count, list.Select(e => e.Page).Distinct().Count(), latest.Page, latest.TotalPages, total <= 0 ? 0 : list.Sum(e => e.DurationSeconds) / total);
        }).OrderByDescending(x => x.Seconds).ToArray();
    }

    public BookDetail BookDetails(BookSummary summary, IEnumerable<ReadingEvent> source, int gapMinutes, DateTimeOffset? now = null)
    {
        var list = source.Where(e => e.BookId == summary.Id).ToList();
        var daily = Daily(list, TimeSpan.FromMinutes(gapMinutes));
        var streaks = Streaks(daily.Select(x => x.Date), DateOnly.FromDateTime(ToLocal(now ?? DateTimeOffset.Now).DateTime));
        var sessions = BuildSessions(list, TimeSpan.FromMinutes(gapMinutes), summary.Id);
        return new(summary, streaks.Current, streaks.Longest, sessions.Count, sessions.Count == 0 ? 0 : sessions.Average(x => x.DurationSeconds), sessions.Count == 0 ? 0 : sessions.Max(x => x.DurationSeconds), Median(list.Select(e => e.DurationSeconds)), daily, sessions.Reverse().ToArray());
    }

    public SessionSummary SessionStatistics(IEnumerable<ReadingEvent> source, int gapMinutes, bool weekStartsMonday = true, DateTimeOffset? now = null)
    {
        var sessions = BuildSessions(source, TimeSpan.FromMinutes(gapMinutes));
        var localNow = ToLocal(now ?? DateTimeOffset.Now);
        var offset = weekStartsMonday ? ((int)localNow.DayOfWeek + 6) % 7 : (int)localNow.DayOfWeek;
        var startOfWeek = localNow.Date.AddDays(-offset);
        var days = sessions.Select(s => DateOnly.FromDateTime(ToLocal(s.Start).DateTime)).Distinct().Count();
        return new(sessions.Count, sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds), Median(sessions.Select(s => s.DurationSeconds)), sessions.Count == 0 ? 0 : sessions.Max(s => s.DurationSeconds), sessions.Count(s => ToLocal(s.Start).Date >= startOfWeek), days == 0 ? 0 : (double)sessions.Count / days, sessions.OrderByDescending(s => s.Start).Take(250).ToArray());
    }

    public IReadOnlyList<ChartPoint> Monthly(IEnumerable<DailyStat> daily, int year, int month) => Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(day => new ChartPoint(day.ToString(), (daily.FirstOrDefault(x => x.Date == new DateOnly(year, month, day))?.Seconds ?? 0) / 60d)).ToArray();
    public IReadOnlyList<ChartPoint> Yearly(IEnumerable<DailyStat> daily, int year) => Enumerable.Range(1, 12).Select(month => new ChartPoint(new DateTime(year, month, 1).ToString("MMM"), daily.Where(x => x.Date.Year == year && x.Date.Month == month).Sum(x => x.Seconds) / 3600d)).ToArray();

    public static double Median(IEnumerable<double> values)
    {
        var a = values.Order().ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    private IReadOnlyList<PersonalRecord> Records(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, IReadOnlyList<DailyStat> daily, IReadOnlyList<ReadingSession> sessions, IReadOnlyList<ChartPoint> hourly, IReadOnlyList<ChartPoint> weekdays)
    {
        var list = new List<PersonalRecord>();
        if (daily.Count == 0) return list;
        var mostDay = daily.MaxBy(d => d.Seconds)!;
        var bestBook = RankBooks(events, books).FirstOrDefault();
        var mostHour = hourly.MaxBy(x => x.Value)!;
        var mostWeekday = weekdays.MaxBy(x => x.Value)!;
        var longestStreak = Streaks(daily.Select(d => d.Date), daily.Max(d => d.Date)).Longest;
        var bestWeek = daily.GroupBy(d => d.Date.AddDays(-(((int)d.Date.DayOfWeek + 6) % 7))).Select(g => new { Start = g.Key, Seconds = g.Sum(x => x.Seconds) }).MaxBy(x => x.Seconds)!;
        var bestMonth = daily.GroupBy(d => new DateOnly(d.Date.Year, d.Date.Month, 1)).Select(g => new { Month = g.Key, Seconds = g.Sum(x => x.Seconds) }).MaxBy(x => x.Seconds)!;
        list.Add(new("Longest streak", $"{longestStreak} days", "Consecutive active reading days"));
        list.Add(new("Most reading in one day", Formatters.Duration(mostDay.Seconds), mostDay.Date.ToString("MMM d, yyyy")));
        list.Add(new("Most reading in one week", Formatters.Duration(bestWeek.Seconds), $"Week of {bestWeek.Start:MMM d, yyyy}"));
        list.Add(new("Most reading in one month", Formatters.Duration(bestMonth.Seconds), bestMonth.Month.ToString("MMMM yyyy")));
        if (sessions.Count > 0) list.Add(new("Longest session", Formatters.Duration(sessions.Max(x => x.DurationSeconds)), ToLocal(sessions.MaxBy(x => x.DurationSeconds)!.Start).ToString("MMM d, yyyy")));
        if (bestBook is not null) list.Add(new("Most-read book", Formatters.Duration(bestBook.Seconds), bestBook.Title));
        list.Add(new("Most active hour", $"{mostHour.Label}–{(int.Parse(mostHour.Label[..2]) + 1) % 24:00}:00", Formatters.Duration(mostHour.Value * 60)));
        list.Add(new("Most active weekday", mostWeekday.Label, Formatters.Duration(mostWeekday.Value * 60)));
        return list;
    }

    private IReadOnlyList<string> Insights(IReadOnlyList<ReadingEvent> events, IReadOnlyList<DailyStat> daily, IReadOnlyList<ReadingSession> sessions, IReadOnlyList<ChartPoint> hourly, IReadOnlyList<ChartPoint> weekdays, int streak, DateTimeOffset now)
    {
        var result = new List<string>();
        if (events.Count < 3) return result;
        var local = ToLocal(now);
        var thisWeek = local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        var thisStart = ToUtc(thisWeek);
        var lastStart = thisStart.AddDays(-7);
        var current = events.Where(e => e.Start >= thisStart && e.Start <= now).Sum(e => e.DurationSeconds);
        var previous = events.Where(e => e.Start >= lastStart && e.Start < thisStart).Sum(e => e.DurationSeconds);
        if (previous > 0) result.Add($"You read {Math.Abs((current - previous) / previous * 100):0}% {(current >= previous ? "more" : "less")} this week than last week.");
        if (hourly.Max(x => x.Value) > 0) { var h = hourly.MaxBy(x => x.Value)!; result.Add($"Your most active reading time is {h.Label}–{(int.Parse(h.Label[..2]) + 1) % 24:00}:00."); }
        if (hourly.Max(x => x.Value) > 0)
        {
            double Block(params int[] hours) => hours.Sum(h => hourly[h].Value);
            var parts = new[] { (Name: "Morning", Value: Block(5, 6, 7, 8, 9, 10, 11)), (Name: "Afternoon", Value: Block(12, 13, 14, 15, 16)), (Name: "Evening", Value: Block(17, 18, 19, 20, 21)), (Name: "Night", Value: Block(22, 23, 0, 1, 2, 3, 4)) };
            result.Add($"You read most in the {parts.MaxBy(x => x.Value).Name}.");
        }
        if (weekdays.Max(x => x.Value) > 0) result.Add($"{weekdays.MaxBy(x => x.Value)!.Label} is your most active reading day.");
        if (sessions.Count > 0) result.Add($"Your longest reading session was {Formatters.Duration(sessions.Max(s => s.DurationSeconds))}.");
        result.Add($"You have read on {daily.Count(d => d.Date >= DateOnly.FromDateTime(local.Date.AddDays(-29)))} of the last 30 days.");
        if (streak > 1) result.Add($"Your current streak is {streak} days.");
        return result.Take(4).ToArray();
    }
}
