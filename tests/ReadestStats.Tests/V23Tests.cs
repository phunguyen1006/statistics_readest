using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V23Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "readest-v23-" + Guid.NewGuid().ToString("N"));
    public V23Tests() => Directory.CreateDirectory(_root);
    private AppDataBackupService Backups => new(Path.Combine(_root, "settings.json"), Path.Combine(_root, "manual-reading.json"));
    private AppDatabase Database => new(Path.Combine(_root, "readest-stats.db"));

    [Fact]
    public void Snapshot_includes_uncheckpointed_WAL_and_restores_current_database()
    {
        Database.SaveManualData(new() { Books = [new() { Id = -1, Title = "Before" }] });
        using var writer = new SqliteConnection($"Data Source={Database.Path};Pooling=False"); writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "PRAGMA wal_autocheckpoint=0; UPDATE manual_books SET title='From WAL', payload=$payload WHERE id=-1";
            command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(new ManualBook { Id = -1, Title = "From WAL" })); command.ExecuteNonQuery();
        }
        Assert.True(new FileInfo(Database.Path + "-wal").Length > 0);
        var path = Path.Combine(_root, "saved.zip"); Backups.Create(path, "2.3.0");
        writer.Close();
        Database.SaveManualData(new() { Books = [new() { Id = -2, Title = "After" }] });
        Backups.Restore(path);
        Assert.Equal("From WAL", Assert.Single(Database.LoadManualData().Books).Title);
        Assert.Equal(2, Backups.Inspect(path).SchemaVersion);
        Assert.Contains(Backups.ListAutomatic(), item => item.FileName.StartsWith("before-restore-"));
    }

    [Fact]
    public void Corrupt_backup_is_rejected_before_any_live_file_changes()
    {
        var settings = Path.Combine(_root, "settings.json"); File.WriteAllText(settings, "{\"Language\":\"English\"}");
        var path = Path.Combine(_root, "saved.zip"); Backups.Create(path, "2.3.0");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            zip.GetEntry("settings.json")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("settings.json").Open()); writer.Write("{}");
        }
        Assert.Throws<InvalidDataException>(() => Backups.Restore(path));
        Assert.Equal("{\"Language\":\"English\"}", File.ReadAllText(settings));
    }

    [Fact]
    public void Interrupted_restore_rolls_back_old_files_and_removes_new_files()
    {
        var pending = Path.Combine(_root, ".restore-pending"); Directory.CreateDirectory(Path.Combine(pending, "old"));
        File.WriteAllText(Path.Combine(pending, "old", "settings.json"), "{\"before\":true}");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"partial\":true}");
        File.WriteAllText(Path.Combine(_root, "manual-reading.json"), "{}");
        File.WriteAllText(Path.Combine(pending, "journal.json"), JsonSerializer.Serialize(new[] { new RestoreTransaction.Entry("settings.json", true), new RestoreTransaction.Entry("manual-reading.json", false) }));
        _ = Backups;
        Assert.Equal("{\"before\":true}", File.ReadAllText(Path.Combine(_root, "settings.json")));
        Assert.False(File.Exists(Path.Combine(_root, "manual-reading.json")));
        Assert.False(Directory.Exists(pending));
    }

    [Fact]
    public void Retention_uses_calendar_age_even_when_fewer_than_seven_backups_exist()
    {
        var directory = Backups.BackupDirectory; Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, $"readest-stats-{DateTime.Today.AddDays(-40):yyyy-MM-dd}.zip");
        var recent = Path.Combine(directory, $"readest-stats-{DateTime.Today.AddDays(-6):yyyy-MM-dd}.zip");
        File.WriteAllText(stale, "old"); File.WriteAllText(recent, "recent");
        var latest = Backups.CreateAutomatic("2.3.0", 7);
        Assert.False(File.Exists(stale)); Assert.True(File.Exists(recent)); Assert.True(File.Exists(latest));
    }

    [Fact]
    public void Undo_commits_restored_records_and_reference_recovery_together()
    {
        var before = new ManualReadingData { Books = [new() { Id = -1, Title = "Restored" }], RecoveryReferences = new(new() { ["manual:-1"] = new() { Status = "Reading" } }, [], ["manual:-1"]) };
        Database.SaveManualData(new()); Database.CaptureUndo("Removed", before);
        _ = Database.RestoreLatestUndo();
        var loaded = Database.LoadManualData();
        Assert.Equal("Restored", Assert.Single(loaded.Books).Title);
        Assert.Equal("Reading", loaded.RecoveryReferences!.Tracking["manual:-1"].Status);
        Assert.Empty(Database.ListOperations());
    }

    [Fact]
    public void Failed_undo_keeps_journal_and_current_database()
    {
        Database.SaveManualData(new() { Books = [new() { Id = -3, Title = "Keep" }] });
        Database.CaptureUndo("Broken", new() { Books = [new() { Id = -1 }, new() { Id = -1 }] });
        Assert.Throws<SqliteException>(() => Database.RestoreLatestUndo());
        Assert.Single(Database.ListOperations()); Assert.Equal("Keep", Assert.Single(Database.LoadManualData().Books).Title);
    }

    [Fact]
    public void Comparison_modes_use_exact_bounds_and_reject_reversed_custom_dates()
    {
        var now = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var range = new DateRangeService(TimeZoneInfo.Utc).Resolve("30 days", now);
        var prior = ComparisonRanges.Resolve(range, "Previous period", timeZone: TimeZoneInfo.Utc)!;
        Assert.Equal(range.PreviousStart, prior.Start); Assert.Equal(range.PreviousEnd, prior.End);
        var year = ComparisonRanges.Resolve(range, "Same period last year", timeZone: TimeZoneInfo.Utc)!;
        Assert.Equal(range.Start.AddYears(-1), year.Start); Assert.Equal(range.End.AddYears(-1), year.End);
        Assert.Null(ComparisonRanges.Resolve(range, "Custom", new(2026, 9, 9), new(2026, 9, 1)));
        Assert.Null(ComparisonRanges.Resolve(range, "Off"));
        var custom = ComparisonRanges.Resolve(range, "Custom", new(2025, 1, 1), new(2025, 1, 2), TimeZoneInfo.Utc)!;
        var events = new[] { new ReadingEvent(1, 1, now.AddDays(-1).ToUnixTimeSeconds(), 120, null), new ReadingEvent(1, 1, custom.Start.ToUnixTimeSeconds(), 30, null) };
        var result = new AnalyticsEngine().Compare(events, range with { PreviousStart = custom.Start, PreviousEnd = custom.End });
        Assert.Equal(120, result.CurrentValue); Assert.Equal(30, result.PreviousValue); Assert.Equal(300, result.PercentDelta);
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_root, true); } catch { } }
}
