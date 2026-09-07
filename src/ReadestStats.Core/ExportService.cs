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
}
