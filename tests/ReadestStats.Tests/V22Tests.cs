using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V22Tests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "readest-stats-v22-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Durable_undo_restores_the_latest_library_snapshot()
    {
        Directory.CreateDirectory(_directory);
        var database = new AppDatabase(Path.Combine(_directory, "readest-stats.db"));
        var before = new ManualReadingData { Books = [new ManualBook { Id = -1, Title = "Before", Authors = "A" }] };
        database.CaptureUndo("Deleted Before", before);
        database.SaveManualData(new ManualReadingData());

        var operation = Assert.Single(database.ListOperations());
        var restored = database.RestoreLatestUndo();

        Assert.Equal("Deleted Before", operation.Action);
        Assert.Equal("Before", Assert.Single(restored!.Books).Title);
        Assert.Empty(database.ListOperations());
        Assert.Equal(2, database.Inspect().SchemaVersion);
    }

    [Fact]
    public void Backup_center_only_deletes_managed_automatic_backups()
    {
        Directory.CreateDirectory(_directory);
        var settings = Path.Combine(_directory, "settings.json");
        var manual = Path.Combine(_directory, "manual-reading.json");
        File.WriteAllText(settings, "{}");
        File.WriteAllText(manual, JsonSerializer.Serialize(new ManualReadingData()));
        var service = new AppDataBackupService(settings, manual);
        var managed = service.CreateAutomatic("2.2.0", 7);
        var unrelated = Path.Combine(_directory, "unrelated.zip");
        File.WriteAllText(unrelated, "keep");

        service.DeleteAutomatic(managed);

        Assert.False(File.Exists(managed));
        Assert.Throws<InvalidOperationException>(() => service.DeleteAutomatic(unrelated));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void Version_22_preferences_round_trip()
    {
        var settings = new AppSettings { CompareMode = "Same period last year", BackupRetentionDays = 30, Language = "Tiếng Việt" };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
        Assert.Equal("Same period last year", restored!.CompareMode);
        Assert.Equal(30, restored.BackupRetentionDays);
        Assert.Equal("Tiếng Việt", restored.Language);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, true); } catch { }
    }
}
