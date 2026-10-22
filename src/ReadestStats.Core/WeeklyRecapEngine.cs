namespace ReadestStats.Core;

public sealed record WeeklyRecap(
    DateOnly Start, DateOnly End, IReadOnlyList<ChartPoint> Days,
    double TotalSeconds, double PreviousSeconds, int ActiveDays,
    string TopBook, double ReadestSeconds, double ManualSeconds,
    double GoalSeconds)
{
    public double DeltaSeconds => TotalSeconds - PreviousSeconds;
    public double GoalPercent => GoalSeconds <= 0 ? 0 : Math.Round(100 * TotalSeconds / GoalSeconds);
}

public static class WeeklyRecapEngine
{
    public static WeeklyRecap Build(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books,
        DateOnly today, bool mondayStart, double goalMinutes, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var offset = ((int)today.DayOfWeek - (mondayStart ? 1 : 0) + 7) % 7;
        var start = today.AddDays(-offset);
        var end = start.AddDays(6);
        var ranges = new DateRangeService(zone);
        var statistics = new StatisticsEngine(zone);
        var current = ReadingEventWindow.Clip(events, ranges.ToUtc(start), ranges.ToUtc(today.AddDays(1)));
        var previous = ReadingEventWindow.Clip(events, ranges.ToUtc(start.AddDays(-7)), ranges.ToUtc(today.AddDays(-6)));
        var byDay = statistics.Daily(current, TimeSpan.FromMinutes(5)).ToDictionary(day => day.Date, day => day.Seconds);
        var titles = books.ToDictionary(book => book.Id, book => book.Title);
        var top = current.GroupBy(item => item.BookId).OrderByDescending(group => group.Sum(item => item.DurationSeconds)).FirstOrDefault();
        return new WeeklyRecap(start, end,
            Enumerable.Range(0, 7).Select(index => {
                var day = start.AddDays(index);
                var seconds = byDay.GetValueOrDefault(day);
                return new ChartPoint(day.ToString("ddd d"), seconds / 60d, $"{day:yyyy-MM-dd} · {Formatters.Duration(seconds)}", "min");
            }).ToArray(),
            current.Sum(item => item.DurationSeconds), previous.Sum(item => item.DurationSeconds),
            byDay.Count(pair => pair.Value > 0), top is null ? "—" : titles.GetValueOrDefault(top.Key, "Unknown book"),
            current.Where(item => item.Source != "Manual").Sum(item => item.DurationSeconds),
            current.Where(item => item.Source == "Manual").Sum(item => item.DurationSeconds),
            Math.Max(0, goalMinutes) * 60);
    }

    public static string ToMarkdown(WeeklyRecap recap)
    {
        var lines = new List<string> {
            $"# Reading week · {recap.Start:yyyy-MM-dd} – {recap.End:yyyy-MM-dd}", "",
            $"- Reading: {Formatters.Duration(recap.TotalSeconds)} ({(recap.DeltaSeconds >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(recap.DeltaSeconds))} vs same days last week)",
            $"- Active days: {recap.ActiveDays}/7", $"- Top book: {recap.TopBook}",
            $"- Weekly goal: {recap.GoalPercent:0}% of {Formatters.Duration(recap.GoalSeconds)}",
            $"- Sources: Readest {Formatters.Duration(recap.ReadestSeconds)} · manual {Formatters.Duration(recap.ManualSeconds)}", "", "| Day | Minutes |", "|---|---:|"
        };
        lines.AddRange(recap.Days.Select(day => $"| {day.Label} | {day.Value:0.#} |"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
