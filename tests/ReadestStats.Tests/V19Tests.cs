using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V19Tests
{
    [Fact]
    public void Reading_plan_uses_only_weekdays_when_requested()
    {
        var engine = new ReadingPlanEngine();
        var plan = new ReadingPlan
        {
            Enabled = true,
            TargetDate = new DateOnly(2026, 9, 21),
            DailyPages = 20,
            IncludeWeekends = false
        };

        var progress = engine.Evaluate(plan, 40, 100, 0, new DateOnly(2026, 9, 18));

        Assert.Equal(2, ReadingPlanEngine.CountReadingDays(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 21), false));
        Assert.Equal(30, progress.RequiredPerReadingDay);
        Assert.Equal("Behind", progress.Status);
    }

    [Fact]
    public void Reading_plan_reports_complete_at_final_page()
    {
        var progress = new ReadingPlanEngine().Evaluate(
            new ReadingPlan { Enabled = true, TargetDate = new DateOnly(2026, 10, 1), DailyPages = 10 },
            320, 320, 0, new DateOnly(2026, 9, 19));

        Assert.Equal("Complete", progress.Status);
        Assert.Equal(0, progress.Remaining);
        Assert.Equal("Target reached.", progress.Summary);
    }

    [Fact]
    public async Task Settings_round_trip_book_links_and_reading_plan()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        var settings = new AppSettings
        {
            BookLinks = [new BookEditionLink { ReadestKey = "hash:readest", ManualKey = "manual:-1" }],
            BookTracking = new Dictionary<string, BookTrackingState>
            {
                ["hash:readest"] = new() { Status = "Reading", Plan = new ReadingPlan { Enabled = true, DailyMinutes = 25, Priority = 3 } }
            }
        };

        var store = new SettingsStore(path);
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded.BookLinks);
        Assert.Equal("manual:-1", loaded.BookLinks[0].ManualKey);
        Assert.Equal(25, loaded.BookTracking["hash:readest"].Plan!.DailyMinutes);
        Assert.Equal(3, loaded.BookTracking["hash:readest"].Plan!.Priority);
    }

    [Fact]
    public void Backup_preview_includes_physical_library_counts()
    {
        var directory = NewDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        var manualPath = Path.Combine(directory, "manual-reading.json");
        File.WriteAllText(settingsPath, "{}");
        File.WriteAllText(manualPath, JsonSerializer.Serialize(new ManualReadingData
        {
            Books = [new ManualBook { Id = -1, Title = "Physical" }],
            Sessions = [new ManualReadingSession { Id = "session", BookId = -1, Note = "Remember this" }]
        }));
        var archive = Path.Combine(directory, "backup.zip");
        var service = new AppDataBackupService(settingsPath, manualPath);

        service.Create(archive, "1.9.0");
        var manifest = service.Inspect(archive);

        Assert.Equal(1, manifest.Books);
        Assert.Equal(1, manifest.Sessions);
        Assert.Equal(1, manifest.Notes);
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "readest-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
