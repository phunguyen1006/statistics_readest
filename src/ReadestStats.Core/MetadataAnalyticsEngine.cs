namespace ReadestStats.Core;

public sealed record MetadataBreakdown(string Name, double Seconds, int Books);
public sealed record MetadataAnalytics(
    IReadOnlyList<MetadataBreakdown> Authors,
    IReadOnlyList<MetadataBreakdown> Languages,
    int BooksWithAuthors, int BooksWithLanguages, int ActiveBooks,
    double SecondsWithAuthors, double SecondsWithLanguages, double TotalSeconds);

public static class MetadataAnalyticsEngine
{
    public static MetadataAnalytics Build(IReadOnlyList<ReadingEvent> events, IReadOnlyList<Book> books)
    {
        var byId = books.ToDictionary(book => book.Id);
        var groups = events.GroupBy(item => item.BookId).Where(group => byId.ContainsKey(group.Key))
            .Select(group => (Book: byId[group.Key], Seconds: group.Sum(item => item.DurationSeconds))).ToArray();
        static string PrimaryAuthor(string value) => value.Split([',', ';', '&'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var authors = groups.Where(item => !string.IsNullOrWhiteSpace(item.Book.Authors))
            .GroupBy(item => PrimaryAuthor(item.Book.Authors), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new MetadataBreakdown(group.Key, group.Sum(item => item.Seconds), group.Count()))
            .OrderByDescending(item => item.Seconds).ToArray();
        var languages = groups.Where(item => !string.IsNullOrWhiteSpace(item.Book.Language))
            .GroupBy(item => item.Book.Language!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new MetadataBreakdown(group.Key, group.Sum(item => item.Seconds), group.Count()))
            .OrderByDescending(item => item.Seconds).ToArray();
        return new MetadataAnalytics(authors, languages, groups.Count(item => !string.IsNullOrWhiteSpace(item.Book.Authors)),
            groups.Count(item => !string.IsNullOrWhiteSpace(item.Book.Language)), events.Select(item => item.BookId).Distinct().Count(),
            authors.Sum(item => item.Seconds), languages.Sum(item => item.Seconds), events.Sum(item => item.DurationSeconds));
    }
}
