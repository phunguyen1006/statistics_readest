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
            GoalEngine.Migrate(settings);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
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
}
