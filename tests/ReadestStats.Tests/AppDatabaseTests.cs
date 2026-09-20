using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class AppDatabaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "readest-stats-db-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Manual_data_round_trips_transactionally()
    {
        Directory.CreateDirectory(_directory);
        var database = new AppDatabase(Path.Combine(_directory, "readest-stats.db"));
        var started = DateTimeOffset.UtcNow.AddMinutes(-15);
        database.SaveManualData(new ManualReadingData
        {
            Books = [new ManualBook { Id = -1, Title = "Physical", Authors = "Author", TotalPages = 300 }],
            Sessions = [new ManualReadingSession { Id = "s1", BookId = -1, StartedAtUtc = started, EndedAtUtc = started.AddMinutes(15), DurationSeconds = 900, StartPage = 1, EndPage = 12, Note = "Remember" }]
        });

        var loaded = database.LoadManualData();
        var health = database.Inspect();

        Assert.Equal("Physical", Assert.Single(loaded.Books).Title);
        Assert.Equal("Remember", Assert.Single(loaded.Sessions).Note);
        Assert.Equal("ok", health.IntegrityStatus);
        Assert.Equal(1, health.Books);
        Assert.Equal(1, health.Sessions);
    }

    [Fact]
    public async Task Legacy_json_is_migrated_and_remains_as_recovery_copy()
    {
        Directory.CreateDirectory(_directory);
        var jsonPath = Path.Combine(_directory, "manual-reading.json");
        await File.WriteAllTextAsync(jsonPath, "{\"SchemaVersion\":2,\"Books\":[{\"Id\":-1,\"Title\":\"Legacy\",\"Authors\":\"\"}],\"Sessions\":[]}");

        var store = new ManualReadingStore(jsonPath);
        var loaded = await store.LoadAsync();

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal("Legacy", Assert.Single(loaded.Books).Title);
        Assert.True(File.Exists(store.DatabasePath));
        Assert.True(File.Exists(jsonPath));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, true); } catch { }
    }
}
