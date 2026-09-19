using System.Net;
using System.Text;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class ManualReadingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ReadestManualTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Store_RoundTripsBooksSessionsAndActiveTimer()
    {
        var path = Path.Combine(_directory, "manual-reading.json");
        var store = new ManualReadingStore(path);
        var started = new DateTimeOffset(2026, 9, 15, 1, 2, 3, TimeSpan.Zero);
        var data = new ManualReadingData
        {
            Books = [new ManualBook { Id = -1, Title = "Physical Book", Authors = "Author", TotalPages = 320, CurrentPage = 42 }],
            Sessions = [new ManualReadingSession { BookId = -1, StartedAtUtc = started, EndedAtUtc = started.AddMinutes(20), DurationSeconds = 1200, StartPage = 21, EndPage = 42, PagesRead = 22 }],
            ActiveSession = new ActiveManualSession { BookId = -1, StartedAtUtc = started, AccumulatedSeconds = 60, IsPaused = true }
        };

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded.Books);
        Assert.Single(loaded.Sessions);
        Assert.Equal(-1, loaded.Books[0].Id);
        Assert.Equal(42, loaded.Books[0].CurrentPage);
        Assert.True(loaded.ActiveSession!.IsPaused);
    }

    [Fact]
    public async Task Store_RecoversLastBackupWhenPrimaryIsCorrupt()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "manual-reading.json");
        var store = new ManualReadingStore(path);
        await store.SaveAsync(new ManualReadingData { Books = [new ManualBook { Id = -1, Title = "First" }] });
        await store.SaveAsync(new ManualReadingData { Books = [new ManualBook { Id = -1, Title = "Second" }] });
        await File.WriteAllTextAsync(path, "{broken");

        var loaded = await store.LoadAsync();

        Assert.Single(loaded.Books);
        Assert.Equal("First", loaded.Books[0].Title);
    }

    [Fact]
    public void Rules_CountInclusivePhysicalPages()
    {
        Assert.Equal(20, ManualReadingRules.CalculatePagesRead(1, 20));
        Assert.Equal(10, ManualReadingRules.CalculatePagesRead(21, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => ManualReadingRules.CalculatePagesRead(30, 29));
    }

    [Fact]
    public void ActiveTimer_ExcludesPausedTime()
    {
        var resumed = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var active = new ActiveManualSession { AccumulatedSeconds = 120, LastResumedAtUtc = resumed, IsPaused = false };
        Assert.Equal(420, active.ElapsedSeconds(resumed.AddMinutes(5)));
        active.IsPaused = true;
        Assert.Equal(120, active.ElapsedSeconds(resumed.AddHours(1)));
    }

    [Fact]
    public async Task MetadataSearch_MapsOpenLibraryEditionFields()
    {
        const string json = """
        {"docs":[{"key":"/works/OL1W","title":"Test Book","author_name":["Ada Writer"],"first_publish_year":2020,"publisher":["Example Press"],"isbn":["1234567890","9781234567897"],"language":["eng"],"cover_i":123,"edition_key":["OL2M"],"number_of_pages_median":240}]}
        """;
        var client = new HttpClient(new StubHandler(json));
        var service = new BookMetadataService(client);

        var result = await service.SearchAsync("Test Book");

        var book = Assert.Single(result);
        Assert.Equal("Open Library", book.Source);
        Assert.Equal("Test Book", book.Title);
        Assert.Equal("Ada Writer", book.Authors);
        Assert.Equal(240, book.PageCount);
        Assert.Equal("9781234567897", book.Isbn13);
        Assert.Contains("covers.openlibrary.org", book.CoverUrl);
    }

    [Fact]
    public async Task MetadataSearch_UsesGoogleBooksWithoutAnApiKeyAndCombinesProviders()
    {
        var requested = new List<Uri>();
        var client = new HttpClient(new RouteHandler(requested));
        var service = new BookMetadataService(client);

        var results = await service.SearchAsync("Cây cam ngọt của tôi");

        Assert.Contains(requested, uri => uri.Host == "www.googleapis.com" && !uri.Query.Contains("key="));
        Assert.Contains(requested, uri => uri.Host == "openlibrary.org");
        Assert.Contains(results, result => result.Source == "Google Books" && result.Title == "Cây cam ngọt của tôi");
        Assert.Contains(results, result => result.Source == "Open Library");
    }

    [Fact]
    public void Daily_SplitsReadingAcrossLocalMidnight()
    {
        var zone = TimeZoneInfo.Utc;
        var engine = new StatisticsEngine(zone);
        var start = new DateTimeOffset(2026, 9, 14, 23, 50, 0, TimeSpan.Zero);
        var daily = engine.Daily([new ReadingEvent(-1, 10, start.ToUnixTimeSeconds(), 1200, 100, "Manual")], TimeSpan.FromMinutes(5));

        Assert.Equal(2, daily.Count);
        Assert.Equal(600, daily[0].Seconds);
        Assert.Equal(600, daily[1].Seconds);
        Assert.Equal(1, daily[0].Sessions);
        Assert.Equal(1, daily[1].Sessions);
    }

    [Fact]
    public void Sessions_PreserveReadingSources()
    {
        var engine = new StatisticsEngine(TimeZoneInfo.Utc);
        var start = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var sessions = engine.BuildSessions([
            new ReadingEvent(1, 1, start.ToUnixTimeSeconds(), 60, 100),
            new ReadingEvent(-1, 2, start.AddMinutes(1).ToUnixTimeSeconds(), 60, 200, "Manual")
        ], TimeSpan.FromMinutes(5));

        var session = Assert.Single(sessions);
        Assert.Equal(["Readest", "Manual"], session.Sources);
    }

    [Fact]
    public void Merge_ExcludesReadestTimeFromOverlappingManualSession()
    {
        var start = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var manual = new ReadingEvent(-1, 20, start.ToUnixTimeSeconds(), 1200, 200, "Manual");
        var readest = new ReadingEvent(1, 2, start.AddMinutes(5).ToUnixTimeSeconds(), 300, 100);

        var remaining = ReadingEventMerger.ExcludeOverlaps([manual], [readest]);

        Assert.Equal(2, remaining.Count);
        Assert.Equal(900, remaining.Sum(item => item.DurationSeconds));
        Assert.All(remaining, item => Assert.Equal("Manual", item.Source));
    }

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    private sealed class RouteHandler(List<Uri> requested) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requested.Add(request.RequestUri!);
            var json = request.RequestUri!.Host == "www.googleapis.com"
                ? """{"items":[{"id":"google-1","volumeInfo":{"title":"Cây cam ngọt của tôi","authors":["José Mauro de Vasconcelos"],"publishedDate":"2020","pageCount":244}}]}"""
                : """{"docs":[{"key":"/works/OL1W","title":"My Sweet Orange Tree","author_name":["José Mauro de Vasconcelos"],"first_publish_year":1968}]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
