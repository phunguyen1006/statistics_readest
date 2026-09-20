using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V20Tests
{
    [Fact]
    public void Reading_plan_honors_custom_reading_days()
    {
        var plan = new ReadingPlan
        {
            Enabled = true,
            TargetDate = new DateOnly(2026, 9, 27),
            DailyPages = 10,
            ReadingDays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]
        };

        Assert.Equal(4, ReadingPlanEngine.CountReadingDays(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 27), plan));
    }

    [Fact]
    public void Paused_plan_does_not_report_behind()
    {
        var progress = new ReadingPlanEngine().Evaluate(
            new ReadingPlan { Enabled = true, IsPaused = true, TargetDate = new DateOnly(2026, 9, 21), DailyPages = 20 },
            10, 300, 0, new DateOnly(2026, 9, 20));

        Assert.Equal("Paused", progress.Status);
    }

    [Fact]
    public async Task Reading_cycles_and_note_rediscovery_state_round_trip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "readest-stats-v20", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var completed = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        var settings = new AppSettings
        {
            BookTracking = new Dictionary<string, BookTrackingState>
            {
                ["book"] = new() { Status = "Finished", Cycles = [new ReadingCycle { Number = 1, CompletedAtUtc = completed }] }
            },
            NoteStates = new Dictionary<string, NoteUserState>
            {
                ["note"] = new() { TimesSeen = 2, LastSeenUtc = completed }
            }
        };

        var store = new SettingsStore(path);
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(completed, loaded.BookTracking["book"].Cycles.Single().CompletedAtUtc);
        Assert.Equal(completed, loaded.NoteStates["note"].LastSeenUtc);
    }

    [Fact]
    public async Task Portable_import_reads_the_selected_json_not_neighboring_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "readest-stats-v20", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var exportPath = Path.Combine(directory, "library.json");
        await File.WriteAllTextAsync(exportPath, JsonSerializer.Serialize(new ManualReadingData
        {
            Books = [new ManualBook { Id = -1, Title = "Portable title" }]
        }));
        new AppDatabase(Path.Combine(directory, "readest-stats.db")).SaveManualData(new ManualReadingData
        {
            Books = [new ManualBook { Id = -2, Title = "Unrelated local database" }]
        });

        var imported = await ManualReadingStore.LoadPortableFileAsync(exportPath);

        Assert.Equal("Portable title", imported.Books.Single().Title);
    }

    [Fact]
    public async Task Import_center_previews_goodreads_csv_with_quoted_titles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "readest-stats-v20", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "goodreads.csv");
        await File.WriteAllTextAsync(path, "Title,Author,ISBN13,Number of Pages,Exclusive Shelf,Date Read\r\n\"A Title, With Comma\",An Author,9781234567890,320,read,2026-09-20");

        var preview = await new LibraryImportService().PreviewCsvAsync(path);

        Assert.Equal("Goodreads", preview.Provider);
        Assert.Equal("A Title, With Comma", preview.Books.Single().Title);
        Assert.Equal(320, preview.Books.Single().Pages);
        Assert.NotNull(preview.Books.Single().FinishedAtUtc);
    }
}
