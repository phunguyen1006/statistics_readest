using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class ExportServiceTests
{
    [Fact]
    public async Task MonthlyMarkdown_ContainsSummaryAndRankedBooks()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".md");
        try
        {
            var time = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            await new ExportService().ExportMonthlyMarkdownAsync(path, 2026, 9, [new ReadingEvent(1, 1, time, 120, 100)], [new Book(1, "Book", "Author")], 5);
            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("# Readest Stats", text);
            Assert.Contains("**Book**", text);
            Assert.Contains("Reading time", text);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
