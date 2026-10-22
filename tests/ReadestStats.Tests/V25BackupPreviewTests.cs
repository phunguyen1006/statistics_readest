using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25BackupPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestStats-v25-preview-" + Guid.NewGuid().ToString("N"));
    private string Settings => Path.Combine(_root, "settings.json");
    private string Manual => Path.Combine(_root, "manual-reading.json");
    private string Database => Path.Combine(_root, "readest-stats.db");
    private AppDataBackupService Service => new(Settings, Manual);

    public V25BackupPreviewTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Preview_UsesActualLegacyDataNotManifestCounts_AndKeepsLiveFilesUnchanged()
    {
        File.WriteAllText(Settings, """{"Theme":"Dark","Goals":[{},{}],"NoteStates":{"local":{}},"GoogleBooksApiKey":"private-current"}""");
        File.WriteAllText(Manual, JsonSerializer.Serialize(new ManualReadingData { Books = [new() { Id = -1, Title = "Current" }] }));
        var archive = WriteLegacyBackup("legacy.zip", new ManualReadingData
        {
            Books = [new() { Id = -2, Title = "Backup 1" }, new() { Id = -3, Title = "Backup 2" }],
            Sessions = [new() { Id = "session", BookId = -2, Note = "private-session" }]
        }, """{"Theme":"Light","Goals":[{}],"NoteStates":{"remote":{},"other":{}},"GoogleBooksApiKey":"private-backup"}""");
        var settingsBefore = File.ReadAllBytes(Settings);
        var manualBefore = File.ReadAllBytes(Manual);

        var preview = Service.Preview(archive);

        Assert.Equal(2, preview.Manifest.Books);
        Assert.Equal(1, preview.Manifest.Sessions);
        Assert.Equal(1, preview.Manifest.Notes);
        var books = Assert.Single(preview.Domains, row => row.Name == "Physical books");
        Assert.Equal(1, books.CurrentCount);
        Assert.Equal(2, books.BackupCount);
        Assert.Contains("not merge", books.Action);
        Assert.Contains("Theme", preview.ChangedSettings);
        Assert.DoesNotContain("GoogleBooksApiKey", preview.ChangedSettings);
        var serialized = JsonSerializer.Serialize(preview);
        Assert.DoesNotContain("private-current", serialized);
        Assert.DoesNotContain("private-backup", serialized);
        Assert.DoesNotContain("private-session", serialized);
        Assert.Equal(settingsBefore, File.ReadAllBytes(Settings));
        Assert.Equal(manualBefore, File.ReadAllBytes(Manual));
        Assert.False(File.Exists(Database));
        Assert.False(Directory.Exists(Path.Combine(_root, ".restore-pending")));
        Assert.Contains(preview.Warnings, warning => warning.Contains("Readest separately"));
        Assert.Contains(preview.Warnings, warning => warning.Contains("no stored checksums"));
    }

    [Fact]
    public void Preview_VerifiesDatabase_AndComparesWithoutInitializingOrChangingLiveDatabase()
    {
        File.WriteAllText(Settings, "{}");
        var db = new AppDatabase(Database);
        db.SaveManualData(new ManualReadingData
        {
            Books = [new() { Id = -1, Title = "Saved" }],
            Sessions = [new() { Id = "saved-session", BookId = -1, Note = "Remember" }]
        });
        db.Checkpoint();
        var archive = Path.Combine(_root, "database.zip");
        Service.Create(archive, "2.5.0");
        db.SaveManualData(new ManualReadingData { Books = [new() { Id = -2, Title = "Current" }, new() { Id = -3, Title = "Other" }] });
        db.Checkpoint();
        var before = File.ReadAllBytes(Database);

        var preview = Service.Preview(archive);

        Assert.True(preview.BackupDatabaseValidated);
        Assert.Equal("Checksums verified", preview.VerificationStatus);
        var books = Assert.Single(preview.Domains, row => row.Name == "Physical books");
        Assert.Equal(2, books.CurrentCount);
        Assert.Equal(1, books.BackupCount);
        Assert.Equal(before, File.ReadAllBytes(Database));
        Assert.Equal(2, db.LoadManualData().Books.Count);
    }

    [Fact]
    public void Preview_MissingDomainsAreClearlyKept_NotReplacedWithEmptyData()
    {
        File.WriteAllText(Settings, "{}");
        File.WriteAllText(Manual, "{}");
        var archive = WriteLegacyBackup("settings-only.zip", null, "{}");

        var preview = Service.Preview(archive);

        var books = Assert.Single(preview.Domains, row => row.Name == "Physical books");
        Assert.Null(books.BackupCount);
        Assert.Equal("Keep current", books.Action);
        Assert.Contains(preview.Warnings, warning => warning.Contains("Physical books, sessions"));
        Assert.Contains("Missing domains stay unchanged", preview.RestoreSemantics);
    }

    [Fact]
    public void Preview_CorruptCurrentDataProducesUnknownCounts_WithoutOverwritingIt()
    {
        File.WriteAllText(Settings, "broken settings");
        File.WriteAllText(Manual, "broken library");
        var archive = WriteLegacyBackup("good.zip", new(), "{}");

        var preview = Service.Preview(archive);

        Assert.Null(Assert.Single(preview.Domains, row => row.Name == "Physical books").CurrentCount);
        Assert.Null(Assert.Single(preview.Domains, row => row.Name == "Settings").CurrentCount);
        Assert.Equal("broken settings", File.ReadAllText(Settings));
        Assert.Equal("broken library", File.ReadAllText(Manual));
        Assert.Contains(preview.Warnings, warning => warning.Contains("counts are unknown"));
    }

    [Fact]
    public void Preview_RejectsInvalidArchivedDatabase_BeforeTouchingLiveFiles()
    {
        File.WriteAllText(Settings, "{}");
        var path = Path.Combine(_root, "invalid-db.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var output = zip.CreateEntry("readest-stats.db").Open()) output.Write("not sqlite"u8);
            using var manifest = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(manifest, new BackupManifest(1, "old", DateTimeOffset.UtcNow, ["readest-stats.db"]));
        }
        Assert.Throws<SqliteException>(() => Service.Preview(path));
        Assert.Equal("{}", File.ReadAllText(Settings));
        Assert.False(File.Exists(Database));
    }

    private string WriteLegacyBackup(string name, ManualReadingData? data, string? settings)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var files = new List<string>();
        if (data is not null)
        {
            using var output = zip.CreateEntry("manual-reading.json").Open();
            JsonSerializer.Serialize(output, data);
            files.Add("manual-reading.json");
        }
        if (settings is not null)
        {
            using var output = new StreamWriter(zip.CreateEntry("settings.json").Open());
            output.Write(settings);
            files.Add("settings.json");
        }
        using var manifest = zip.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(manifest, new BackupManifest(1, "1.8.0", DateTimeOffset.UtcNow, files.ToArray(), 999, 999, 999));
        return path;
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
