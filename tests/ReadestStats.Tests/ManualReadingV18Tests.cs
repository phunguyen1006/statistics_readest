using System.IO.Compression;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class ManualReadingV18Tests
{
    [Fact]
    public async Task V1_session_is_migrated_to_one_timed_segment()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "manual-reading.json");
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Books = new[] { new { Id = -1, Title = "Physical", Authors = "Author" } },
            Sessions = new[] { new { Id = "old", BookId = -1, StartedAtUtc = start, EndedAtUtc = start.AddMinutes(25), DurationSeconds = 1500, StartPage = 1, EndPage = 20 } }
        }));

        var data = await new ManualReadingStore(path).LoadAsync();

        Assert.Equal(2, data.SchemaVersion);
        var segment = Assert.Single(Assert.Single(data.Sessions).Segments);
        Assert.Equal(1500, segment.DurationSeconds, 3);
    }

    [Fact]
    public void Complete_backup_round_trips_settings_manual_data_and_cover()
    {
        var directory = NewDirectory();
        var settings = Path.Combine(directory, "settings.json");
        var manual = Path.Combine(directory, "manual-reading.json");
        var covers = Path.Combine(directory, "covers");
        Directory.CreateDirectory(covers);
        File.WriteAllText(settings, "{\"Theme\":\"Dark\"}");
        File.WriteAllText(manual, "{\"SchemaVersion\":2}");
        File.WriteAllBytes(Path.Combine(covers, "cover.jpg"), [1, 2, 3]);
        var archive = Path.Combine(directory, "backup.zip");
        var service = new AppDataBackupService(settings, manual);

        service.Create(archive, "1.8.0");

        using (var zip = ZipFile.OpenRead(archive))
        {
            Assert.NotNull(zip.GetEntry("manifest.json"));
            Assert.NotNull(zip.GetEntry("settings.json"));
            Assert.NotNull(zip.GetEntry("manual-reading.json"));
            Assert.NotNull(zip.GetEntry("covers/cover.jpg"));
        }
        File.WriteAllText(settings, "changed");
        service.Restore(archive);
        Assert.Contains("Dark", File.ReadAllText(settings));
        Assert.True(File.Exists(settings + ".before-restore.bak"));
    }

    [Fact]
    public void Metadata_relevance_handles_vietnamese_diacritics()
    {
        var exact = new BookMetadataResult("x", "1", "Cây cam ngọt của tôi", "José Mauro", null, null, 200, null, null, "vi", null, null);
        var unrelated = exact with { ExternalId = "2", Title = "Một cuốn sách khác" };
        Assert.True(BookMetadataService.Relevance(exact, "cay cam ngot cua toi") > BookMetadataService.Relevance(unrelated, "cay cam ngot cua toi"));
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "readest-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
