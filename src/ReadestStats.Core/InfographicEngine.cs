namespace ReadestStats.Core;

/// <summary>
/// Builds visualization-ready aggregates from observed Readest events. Selection rules are
/// deterministic: sessions use individual marks through 15 observations, matrices aggregate
/// long ranges by week, and rolling momentum is withheld until four calendar observations exist.
/// </summary>
public sealed class InfographicEngine
{
    private readonly StatisticsEngine _statistics;
    private readonly TimeZoneInfo _zone;

    public InfographicEngine(TimeZoneInfo? zone = null)
    {
        _zone = zone ?? TimeZoneInfo.Local;
        _statistics = new(_zone);
    }

    public IReadOnlyList<MatrixCell> WeekHourMatrix(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, int gapMinutes, bool sessionsMetric)
    {
        var filtered = InRange(events, range);
        var sessions = _statistics.BuildSessions(filtered, TimeSpan.FromMinutes(gapMinutes));
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        return days.SelectMany((day, row) => Enumerable.Range(0, 24).Select(hour =>
        {
            var seconds = filtered.Where(item => Local(item.Start).DayOfWeek == day && Local(item.Start).Hour == hour).Sum(item => item.DurationSeconds);
            var count = sessions.Count(item => Local(item.Start).DayOfWeek == day && Local(item.Start).Hour == hour);
            var value = sessionsMetric ? count : seconds / 60d;
            return new MatrixCell(day.ToString()[..3], $"{hour:00}", row, hour, value, count,
                $"{day} · {hour:00}:00–{(hour + 1) % 24:00}:00\n{Formatters.Duration(seconds)} · {Formatters.Count(count, "session")}");
        })).ToArray();
    }

    public IReadOnlyList<TimelineSpan> DayTimeline(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, DateOnly date, int gapMinutes)
    {
        var titles = books.ToDictionary(book => book.Id, book => string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title);
        var sessions = _statistics.BuildSessions(events, TimeSpan.FromMinutes(gapMinutes))
            .Where(item => DateOnly.FromDateTime(Local(item.Start).DateTime) == date)
            .OrderBy(item => item.Start)
            .ToArray();
        return sessions.Select((session, lane) =>
        {
            var start = Local(session.Start);
            var end = Local(session.End);
            var startHour = start.TimeOfDay.TotalHours;
            var endHour = Math.Clamp(end.Date == start.Date ? end.TimeOfDay.TotalHours : 24, startHour + 1d / 60, 24);
            var label = session.BookIds.Count == 1 ? titles.GetValueOrDefault(session.BookIds[0], "Unknown book") : $"{session.BookIds.Count} books";
            return new TimelineSpan(startHour, endHour, lane, label,
                $"{label}\n{start:HH:mm}–{end:HH:mm} · {Formatters.Duration(session.DurationSeconds)} active\n{Formatters.Count(session.EventCount, "activity event")}");
        }).ToArray();
    }

    public IReadOnlyList<DotDatum> SessionDots(IReadOnlyList<ReadingSession> sessions, IReadOnlyList<Book> books)
    {
        var titles = books.ToDictionary(book => book.Id, book => string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title);
        return sessions.OrderBy(item => item.DurationSeconds).Select(session =>
        {
            var local = Local(session.Start);
            var title = session.BookIds.Count == 1 ? titles.GetValueOrDefault(session.BookIds[0], "Unknown book") : $"{session.BookIds.Count} books";
            return new DotDatum(local.ToString("MMM d"), session.DurationSeconds / 60d, session.EventCount,
                $"{local:MMM d, yyyy · HH:mm}\n{title}\n{Formatters.Duration(session.DurationSeconds)} · {Formatters.Count(session.EventCount, "activity event")}");
        }).ToArray();
    }

    public IReadOnlyList<DotDatum> ReadingStyleDays(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, int gapMinutes)
    {
        var filtered = InRange(events, range);
        var sessions = _statistics.BuildSessions(filtered, TimeSpan.FromMinutes(gapMinutes));
        return sessions.GroupBy(item => DateOnly.FromDateTime(Local(item.Start).DateTime)).OrderBy(group => group.Key).Select(group =>
        {
            var total = group.Sum(item => item.DurationSeconds);
            return new DotDatum(group.Key.ToString("MMM d"), group.Count(), total / group.Count() / 60d,
                $"{group.Key:MMM d, yyyy}\n{Formatters.Count(group.Count(), "session")} · {Formatters.Duration(total / group.Count())} average\n{Formatters.Duration(total)} total");
        }).ToArray();
    }

    public IReadOnlyList<CompositionPart> BookAttention(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, int maxParts = 6)
    {
        var ranked = _statistics.RankBooks(events, books);
        if (ranked.Count == 0) return [];
        var parts = ranked.Take(maxParts).Select((book, index) => new CompositionPart(book.Title, book.Seconds, $"{book.Title}\n{Formatters.Duration(book.Seconds)} · {book.Share:P0} of reading time", index)).ToList();
        if (ranked.Count > maxParts)
        {
            var other = ranked.Skip(maxParts).Sum(book => book.Seconds);
            parts.Add(new("Other", other, $"Other {ranked.Count - maxParts} books\n{Formatters.Duration(other)}", maxParts));
        }
        return parts;
    }

    public IReadOnlyList<MatrixCell> BookPeriodMatrix(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, ResolvedDateRange range, int maxBooks = 6)
    {
        var filtered = InRange(events, range);
        var ranked = _statistics.RankBooks(filtered, books).Take(maxBooks).ToArray();
        if (ranked.Length == 0) return [];
        var span = range.EndDate.DayNumber - range.StartDate.DayNumber + 1;
        string Key(DateOnly date) => span <= 45 ? date.ToString("yyyy-MM-dd") : date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
        string Label(string key) => DateOnly.Parse(key).ToString(span <= 45 ? "d" : "MMM d");
        var keys = Enumerable.Range(0, Math.Max(1, span)).Select(range.StartDate.AddDays).Select(Key).Distinct().ToArray();
        return ranked.SelectMany((book, row) => keys.Select((key, column) =>
        {
            var bucket = filtered.Where(item => item.BookId == book.Id && Key(DateOnly.FromDateTime(Local(item.Start).DateTime)) == key).ToArray();
            var seconds = bucket.Sum(item => item.DurationSeconds);
            var unit = span <= 45 ? DateOnly.Parse(key).ToString("MMM d") : $"Week of {DateOnly.Parse(key):MMM d}";
            return new MatrixCell(book.Title, Label(key), row, column, seconds / 60d, bucket.Length,
                $"{book.Title}\n{unit} · {Formatters.Duration(seconds)} · {Formatters.Count(bucket.Length, "activity event")}");
        })).ToArray();
    }

    public IReadOnlyList<ChartPoint> Fingerprint(IReadOnlyList<DailyStat> daily, DateOnly end, int days = 90)
    {
        var map = daily.ToDictionary(item => item.Date);
        return Enumerable.Range(0, days).Select(index =>
        {
            var date = end.AddDays(index - days + 1);
            var item = map.GetValueOrDefault(date);
            return new ChartPoint(date.ToString("yyyy-MM-dd"), item?.Seconds ?? 0,
                item is null ? $"{date:MMM d, yyyy}\nNo activity" : $"{date:MMM d, yyyy}\n{Formatters.Duration(item.Seconds)} · {Formatters.Count(item.Sessions, "session")}");
        }).ToArray();
    }

    public IReadOnlyList<ChartPoint> RollingMomentum(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, int gapMinutes)
    {
        var span = Math.Max(1, range.EndDate.DayNumber - range.StartDate.DayNumber + 1);
        if (span < 4) return [];
        var start = span > 120 ? range.EndDate.AddDays(-119) : range.StartDate;
        var daily = _statistics.Daily(InRange(events, range), TimeSpan.FromMinutes(gapMinutes)).ToDictionary(item => item.Date);
        var dates = Enumerable.Range(0, range.EndDate.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        var window = dates.Length < 14 ? 3 : 7;
        return dates.Select((date, index) =>
        {
            var from = Math.Max(0, index - window + 1);
            var samples = dates[from..(index + 1)];
            var raw = daily.GetValueOrDefault(date)?.Seconds ?? 0;
            var average = samples.Average(day => daily.GetValueOrDefault(day)?.Seconds ?? 0);
            return new ChartPoint(date.ToString("MMM d"), average / 60d, $"{date:MMM d}\n{Formatters.Duration(raw)} raw · {window}-day average {Formatters.Duration(average)}");
        }).ToArray();
    }

    public IReadOnlyList<DumbbellDatum> PeriodDumbbells(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range, int gapMinutes)
    {
        var current = events.Where(item => item.DurationSeconds > 0 && item.Start >= range.Start && item.Start < range.End).ToArray();
        var previous = events.Where(item => item.DurationSeconds > 0 && item.Start >= range.PreviousStart && item.Start < range.PreviousEnd).ToArray();
        var gap = TimeSpan.FromMinutes(gapMinutes);
        return new[]
        {
            new DumbbellDatum("Reading time", current.Sum(item => item.DurationSeconds), previous.Sum(item => item.DurationSeconds), Formatters.Duration(current.Sum(item => item.DurationSeconds)), Formatters.Duration(previous.Sum(item => item.DurationSeconds))),
            new DumbbellDatum("Active days", current.Select(_statistics.LocalDate).Distinct().Count(), previous.Select(_statistics.LocalDate).Distinct().Count(), current.Select(_statistics.LocalDate).Distinct().Count().ToString(), previous.Select(_statistics.LocalDate).Distinct().Count().ToString()),
            new DumbbellDatum("Sessions", _statistics.BuildSessions(current, gap).Count, _statistics.BuildSessions(previous, gap).Count, _statistics.BuildSessions(current, gap).Count.ToString(), _statistics.BuildSessions(previous, gap).Count.ToString()),
            new DumbbellDatum("Books active", current.Select(item => item.BookId).Distinct().Count(), previous.Select(item => item.BookId).Distinct().Count(), current.Select(item => item.BookId).Distinct().Count().ToString(), previous.Select(item => item.BookId).Distinct().Count().ToString())
        };
    }

    public IReadOnlyList<ChartPoint> SessionStaircase(IReadOnlyList<ReadingSession> sessions)
    {
        var cumulative = 0d;
        return sessions.OrderBy(item => item.Start).Select(item =>
        {
            cumulative += item.DurationSeconds;
            return new ChartPoint(Local(item.Start).ToString("MMM d"), cumulative / 3600d, $"{Local(item.Start):MMM d · HH:mm}\n{Formatters.Duration(item.DurationSeconds)} step · {Formatters.Duration(cumulative)} accumulated");
        }).ToArray();
    }

    public IReadOnlyList<PaceDatum> YearGoalPace(IReadOnlyList<DateTimeOffset> completed, int year, double target, DateOnly today)
    {
        if (target <= 0) return [];
        var endMonth = year == today.Year ? today.Month : 12;
        var local = completed.Select(Local).Where(date => date.Year == year).ToArray();
        return Enumerable.Range(1, endMonth).Select(month =>
        {
            var actual = local.Count(date => date.Month <= month);
            var required = target * new DateOnly(year, month, DateTime.DaysInMonth(year, month)).DayOfYear / (DateTime.IsLeapYear(year) ? 366d : 365d);
            return new PaceDatum(new DateTime(year, month, 1).ToString("MMM"), actual, required, $"{new DateTime(year, month, 1):MMMM}\n{actual} finished · {required:0.#} required pace");
        }).ToArray();
    }

    public IReadOnlyList<ChartPoint> BestDays(IReadOnlyList<ReadingEvent> events, int year, int gapMinutes, int count = 5) =>
        _statistics.Daily(events.Where(item => Local(item.Start).Year == year), TimeSpan.FromMinutes(gapMinutes))
            .OrderByDescending(item => item.Seconds).Take(count)
            .Select(item => new ChartPoint(item.Date.ToString("MMM d"), item.Seconds / 60d, $"{item.Date:MMM d, yyyy} · {Formatters.Duration(item.Seconds)} · {Formatters.Count(item.Sessions, "session")}"))
            .ToArray();

    private IReadOnlyList<ReadingEvent> InRange(IReadOnlyList<ReadingEvent> events, ResolvedDateRange range) =>
        events.Where(item => item.DurationSeconds > 0 && item.Start >= range.Start && item.Start < range.End).ToArray();

    private DateTimeOffset Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);
}
