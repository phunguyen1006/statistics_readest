using System.Text;
using System.Text.Json;

namespace ReadestStats.Core;

public sealed class ExportService
{
    public async Task ExportCsvAsync(string path, IEnumerable<DailyStat> daily, CancellationToken token = default)
    {
        var text = new StringBuilder("Date,ReadingSeconds,Books,Sessions\r\n");
        foreach (var item in daily) text.Append(item.Date.ToString("yyyy-MM-dd")).Append(',').Append(item.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(item.Books).Append(',').Append(item.Sessions).Append("\r\n");
        await File.WriteAllTextAsync(path, text.ToString(), new UTF8Encoding(true), token);
    }

    public Task ExportJsonAsync(string path, OverviewStats overview, IEnumerable<BookSummary> books, CancellationToken token = default) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { ExportedAt = DateTimeOffset.Now, Overview = overview, Books = books }, new JsonSerializerOptions { WriteIndented = true }), token);

    public Task ExportMonthlyMarkdownAsync(string path, int year, int month, IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books, int sessionGapMinutes, CancellationToken token = default)
    {
        var statistics = new StatisticsEngine();
        var monthlyEvents = events.Where(e => { var local = statistics.ToLocal(e.Start); return local.Year == year && local.Month == month; }).ToArray();
        var daily = statistics.Daily(monthlyEvents, TimeSpan.FromMinutes(sessionGapMinutes));
        var sessions = statistics.BuildSessions(monthlyEvents, TimeSpan.FromMinutes(sessionGapMinutes));
        var ranked = statistics.RankBooks(monthlyEvents, books);
        var title = new DateTime(year, month, 1).ToString("MMMM yyyy");
        var lines = new List<string>
        {
            $"# Readest Stats — {title}", "", $"Generated {DateTimeOffset.Now:MMM d, yyyy}", "",
            "## Summary", "", $"- Reading time: {Formatters.Duration(monthlyEvents.Sum(e => e.DurationSeconds))}",
            $"- Active days: {daily.Count}", $"- Sessions: {sessions.Count}", $"- Active books: {ranked.Count}", "", "## Books", ""
        };
        if (ranked.Count == 0) lines.Add("No reading activity was recorded this month.");
        else lines.AddRange(ranked.Select((book, index) => $"{index + 1}. **{book.Title}** — {book.Authors} — {Formatters.Duration(book.Seconds)}"));
        lines.AddRange(["", "## Daily activity", ""]);
        if (daily.Count == 0) lines.Add("No active days.");
        else lines.AddRange(daily.Select(day => $"- {day.Date:MMM d}: {Formatters.Duration(day.Seconds)} · {Formatters.Count(day.Sessions, "session")} · {Formatters.Count(day.Books, "book")}"));
        lines.AddRange(["", "---", "Created locally by Readest Stats. Source reading data was not modified."]);
        return File.WriteAllLinesAsync(path, lines, new UTF8Encoding(true), token);
    }
}
