using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class RepositorySafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ReadestStatsTests-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_directory, "statistics.db");

    public RepositorySafetyTests()
    {
        Directory.CreateDirectory(_directory);
        using var connection = new SqliteConnection($"Data Source={PathName}"); connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE book(id INTEGER PRIMARY KEY,title TEXT,authors TEXT,last_open INTEGER,pages INTEGER,series TEXT,language TEXT); CREATE TABLE page_stat_data(id_book INTEGER,page INTEGER,start_time INTEGER,duration REAL,total_pages INTEGER); INSERT INTO book VALUES(1,'Fixture','Tester',NULL,100,NULL,'en'); INSERT INTO page_stat_data VALUES(1,2,1700000000,120,100);"; command.ExecuteNonQuery();
        connection.Close(); SqliteConnection.ClearAllPools();
    }

    [Fact] public async Task Repository_ValidatesAndReadsWithoutChangingDatabase()
    {
        var before = SHA256.HashData(await File.ReadAllBytesAsync(PathName)); var repository = new SqliteReadestRepository(PathName); var validation = await repository.ValidateAsync(); var events = await repository.GetEventsAsync(); var books = await repository.GetBooksAsync(); var after = SHA256.HashData(await File.ReadAllBytesAsync(PathName));
        Assert.True(validation.IsValid, validation.Message); Assert.Single(events); Assert.Single(books); Assert.Equal(before, after); Assert.Empty(Directory.GetFiles(_directory).Where(x => x.EndsWith("-wal") || x.EndsWith("-shm")));
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_directory, true); } catch { } }
}
