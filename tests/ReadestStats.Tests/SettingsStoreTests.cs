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

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
}
