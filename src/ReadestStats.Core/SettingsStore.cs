using System.Text.Json;

namespace ReadestStats.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public SettingsStore(string path) => _path = path;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path)) return new();
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new();
            Normalize(settings);
            GoalEngine.Migrate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Normalize(settings);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            File.Move(temp, _path, true);
        }
        finally { _writeLock.Release(); }
    }

    public void Backup(string destination)
    {
        if (File.Exists(_path)) File.Copy(_path, destination, true);
    }

    public bool CreateAutomaticBackup(DateTimeOffset? current = null, int retainedCopies = 7)
    {
        if (!File.Exists(_path)) return false;
        var now = current ?? DateTimeOffset.Now;
        var directory = Path.Combine(Path.GetDirectoryName(_path)!, "Backups");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"settings-{now:yyyy-MM-dd}.json");
        if (File.Exists(destination)) return false;
        File.Copy(_path, destination);
        foreach (var old in Directory.EnumerateFiles(directory, "settings-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(Math.Max(1, retainedCopies))) File.Delete(old);
        return true;
    }

    private static void Normalize(AppSettings settings)
    {
        settings.Goals ??= [];
        settings.GoalArchives ??= [];
        settings.BookTracking ??= [];
        settings.BookLinks ??= [];
        settings.PinnedBookKeys ??= [];
        settings.NoteStates ??= [];
        settings.BookLinks.RemoveAll(link => link is null || string.IsNullOrWhiteSpace(link.ManualKey) || string.IsNullOrWhiteSpace(link.ReadestKey));
        foreach (var tracking in settings.BookTracking.Values)
        {
            tracking.Cycles ??= [];
            if (tracking.Plan is { } plan) plan.ReadingDays ??= [];
        }
        settings.LibrarySchemaVersion = Math.Max(2, settings.LibrarySchemaVersion);
    }
}
