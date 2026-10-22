using ReadestStats.Core;
using System.Diagnostics;

namespace ReadestStats.Tests;

public sealed class V24Tests
{
    private static ReadingEvent At(long book, int day, double seconds, string source = "Readest") =>
        new(book, day, new DateTimeOffset(2026, 9, day, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), seconds, 300, source);

    [Fact]
    public void LocalExclusionIsReversibleAndDoesNotMutateSource()
    {
        var events = new[] { At(1, 20, 600), At(1, 21, 1200) };
        var key = ReadingEventAudit.Key(events[1]);
        var exclusions = new[] { new ReadingEventExclusion(key, "duplicate", DateTimeOffset.UtcNow) };
        Assert.Single(ReadingEventAudit.Apply(events, exclusions));
        Assert.Equal(2, events.Length);
        Assert.Equal(2, ReadingEventAudit.Apply(events, []).Count);
        var preview = ReadingEventAudit.Preview(events, [], events[1]);
        Assert.Equal(1800, preview.BeforeSeconds);
        Assert.Equal(600, preview.AfterSeconds);
    }

    [Fact]
    public void WeeklyRecapSplitsSourcesAndComparesPreviousWeek()
    {
        var books = new[] { new Book(1, "A", "Author"), new Book(2, "B", "Other") };
        var events = new[] { At(1, 14, 600), At(1, 21, 1200), At(2, 22, 1800, "Manual") };
        var recap = WeeklyRecapEngine.Build(events, books, new DateOnly(2026, 9, 23), true, 60, TimeZoneInfo.Utc);
        Assert.Equal(new DateOnly(2026, 9, 21), recap.Start);
        Assert.Equal(3000, recap.TotalSeconds);
        Assert.Equal(600, recap.PreviousSeconds);
        Assert.Equal(2, recap.ActiveDays);
        Assert.Equal(1200, recap.ReadestSeconds);
        Assert.Equal(1800, recap.ManualSeconds);
        Assert.Contains("|", WeeklyRecapEngine.ToMarkdown(recap));
    }

    [Fact]
    public void MetadataAnalyticsAttributesEachBookOnceToPrimaryAuthor()
    {
        var books = new[] { new Book(1, "A", "Alice, Bob", Language: "en"), new Book(2, "B", "", Language: null) };
        var data = MetadataAnalyticsEngine.Build([At(1, 20, 600), At(1, 21, 300), At(2, 22, 100)], books);
        Assert.Single(data.Authors);
        Assert.Equal("Alice", data.Authors[0].Name);
        Assert.Equal(900, data.Authors[0].Seconds);
        Assert.Equal(1, data.BooksWithAuthors);
        Assert.Equal(1000, data.TotalSeconds);
    }

    [Fact]
    public void HyphenatedIsbnRanksAsExactMatch()
    {
        var result = new BookMetadataResult("Open Library", "x", "Other title", "A", null, null, 100, null, "9781234567890", null, null, null);
        Assert.True(BookMetadataService.Relevance(result, "978-1-234-56789-0") >= 1200);
    }

    [Fact]
    public async Task EventExclusionsSurviveSettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "readest-v24-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings { ReadingEventExclusions = [new("Readest:1:2:3", "duplicate", DateTimeOffset.UtcNow)] };
            var store = new SettingsStore(path);
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();
            Assert.Equal("duplicate", Assert.Single(loaded.ReadingEventExclusions).Reason);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task LocalCoverIsCopiedIntoAppCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "readest-v24-cover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "my-cover.png");
        try
        {
            await File.WriteAllBytesAsync(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/XxkAAAAASUVORK5CYII="));
            var cached = await new CoverCacheService(Path.Combine(root, "cache")).ImportLocalAsync(source);
            Assert.StartsWith(Path.Combine(root, "cache"), cached);
            File.Delete(source);
            Assert.True(File.Exists(cached));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NewAnalyticsMeetLargeLibrarySmokeBudget()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var books = Enumerable.Range(1, 1_000).Select(index => new Book(index, $"Book {index}", $"Author {index % 50}", Language: index % 2 == 0 ? "en" : "vi")).ToArray();
        var events = Enumerable.Range(0, 100_000).Select(index => new ReadingEvent(index % 1_000 + 1, index % 400, start.AddMinutes(index * 2L).ToUnixTimeSeconds(), 40 + index % 180, 400)).ToArray();
        var timer = Stopwatch.StartNew();
        var included = ReadingEventAudit.Apply(events, [new ReadingEventExclusion(ReadingEventAudit.Key(events[0]), "test", DateTimeOffset.UtcNow)]);
        var recap = WeeklyRecapEngine.Build(included, books, new DateOnly(2026, 9, 27), true, 210, TimeZoneInfo.Utc);
        var metadata = MetadataAnalyticsEngine.Build(included, books);
        timer.Stop();
        Assert.Equal(events.Length - 1, included.Count);
        Assert.Equal(1_000, metadata.ActiveBooks);
        Assert.Equal(7, recap.Days.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"v2.4 analytics took {timer.Elapsed}.");
    }
}
