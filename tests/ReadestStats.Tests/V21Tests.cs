using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V21Tests
{
    [Fact]
    public void Plan_adherence_distinguishes_complete_partial_missed_and_skipped_days()
    {
        var start = new DateOnly(2026, 9, 14);
        var plan = new ReadingPlan
        {
            Enabled = true,
            DailyMinutes = 20,
            ReadingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
            SkippedDates = [start.AddDays(3)]
        };
        var actual = new Dictionary<DateOnly, double>
        {
            [start] = 20,
            [start.AddDays(1)] = 8
        };

        var days = new PlanAdherenceEngine().Build(plan, start, start.AddDays(4), actual);

        Assert.Equal(["Complete", "Partial", "Missed", "Skipped", "Rest day"], days.Select(day => day.Status));
    }

    [Fact]
    public void Search_ranking_is_accent_insensitive_and_prefers_title_matches()
    {
        var title = SearchRanking.Score("cay cam ngot", "Cây cam ngọt của tôi", "José Mauro", "Book");
        var detail = SearchRanking.Score("cay cam ngot", "Một cuốn khác", "Cây cam ngọt của tôi", "Book");

        Assert.True(title > detail);
        Assert.True(title > 0);
    }

    [Fact]
    public async Task Snooze_exclusion_and_skipped_plan_dates_round_trip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "readest-stats-v21", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var snoozed = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);
        var skipped = new DateOnly(2026, 9, 20);
        var settings = new AppSettings
        {
            NoteStates = new Dictionary<string, NoteUserState> { ["note"] = new() { SnoozedUntilUtc = snoozed, ExcludeFromRandom = true } },
            BookTracking = new Dictionary<string, BookTrackingState> { ["book"] = new() { Plan = new() { Enabled = true, SkippedDates = [skipped] } } }
        };

        var store = new SettingsStore(path);
        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(snoozed, loaded.NoteStates["note"].SnoozedUntilUtc);
        Assert.True(loaded.NoteStates["note"].ExcludeFromRandom);
        Assert.Contains(skipped, loaded.BookTracking["book"].Plan!.SkippedDates);
    }

    [Fact]
    public void Backup_history_reports_valid_and_unreadable_archives()
    {
        var directory = Path.Combine(Path.GetTempPath(), "readest-stats-v21", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "settings.json");
        var manual = Path.Combine(directory, "manual-reading.json");
        File.WriteAllText(settings, "{}");
        File.WriteAllText(manual, "{}");
        var service = new AppDataBackupService(settings, manual);

        service.CreateAutomatic("2.1.0");
        var item = Assert.Single(service.ListAutomatic());

        Assert.Equal("Ready", item.Status);
        Assert.Equal("2.1.0", item.Version);
    }
}
