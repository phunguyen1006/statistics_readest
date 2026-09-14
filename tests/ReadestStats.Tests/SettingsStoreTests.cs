using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ReadestSettingsTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AutomaticBackup_IsCreatedOnlyOncePerDay()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        await store.SaveAsync(new AppSettings());
        var now = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

        Assert.True(store.CreateAutomaticBackup(now));
        Assert.False(store.CreateAutomaticBackup(now.AddHours(2)));
        Assert.True(File.Exists(Path.Combine(_directory, "Backups", "settings-2026-09-08.json")));
    }

    [Fact]
    public async Task FilterAndChartPreferencesRoundTripWithoutLosingCalendarPresets()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);
        await store.SaveAsync(new AppSettings { DefaultRangePreset = "Last year", TrendMetric = "Sessions", TrendGranularity = "Month", CustomRangeStart = new(2025, 1, 1), CustomRangeEnd = new(2025, 3, 31) });
        var loaded = await store.LoadAsync();
        Assert.Equal("Last year", loaded.DefaultRangePreset);
        Assert.Equal("Sessions", loaded.TrendMetric);
        Assert.Equal("Month", loaded.TrendGranularity);
        Assert.Equal(new DateOnly(2025, 1, 1), loaded.CustomRangeStart);
    }

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
}
