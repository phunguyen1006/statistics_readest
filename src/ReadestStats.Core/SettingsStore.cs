using System.Text.Json;

namespace ReadestStats.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public SettingsStore(string path) => _path = path;
    public string? ApiKeyWarning { get; private set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = File.OpenRead(_path);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var settings = json.RootElement.Deserialize<AppSettings>(JsonOptions)
                ?? throw new InvalidDataException("Readest Stats settings are empty. Restore a settings backup before continuing.");
            var migrateKey = false;
            ApiKeyWarning = null;
            if (!string.IsNullOrWhiteSpace(settings.GoogleBooksApiKeyProtected))
            {
                if (LocalSecretProtection.TryUnprotect(settings.GoogleBooksApiKeyProtected, out var key)) settings.GoogleBooksApiKey = key;
                else ApiKeyWarning = "The saved Google Books API key is unavailable for this Windows account. Enter it again in Settings.";
            }
            else if (json.RootElement.TryGetProperty("GoogleBooksApiKey", out var legacy) && legacy.ValueKind == JsonValueKind.String)
            {
                settings.GoogleBooksApiKey = legacy.GetString();
                migrateKey = !string.IsNullOrWhiteSpace(settings.GoogleBooksApiKey);
            }
            Normalize(settings);
            GoalEngine.Migrate(settings);
            if (migrateKey)
            {
                // Close the read handle before atomically replacing the legacy file.
                await stream.DisposeAsync();
                await SaveAsync(settings, cancellationToken);
            }
            return settings;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidDataException("Readest Stats settings could not be read. The original file was left unchanged; restore a settings backup before continuing.", ex);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Normalize(settings);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            settings.GoogleBooksApiKeyProtected = string.IsNullOrWhiteSpace(settings.GoogleBooksApiKey)
                ? null : LocalSecretProtection.Protect(settings.GoogleBooksApiKey);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temp, _path, true);
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Drain already queued writes before restore. Callers must suspend new mutations first.</summary>
    public async Task WaitForWritesAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _writeLock.Release();
    }

    public void Backup(string destination)
    {
        if (File.Exists(_path)) WritePortableBackup(destination);
    }

    public bool CreateAutomaticBackup(DateTimeOffset? current = null, int retainedCopies = 7)
    {
        if (!File.Exists(_path)) return false;
        var now = current ?? DateTimeOffset.Now;
        var directory = Path.Combine(Path.GetDirectoryName(_path)!, "Backups");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"settings-{now:yyyy-MM-dd}.json");
        if (File.Exists(destination)) return false;
        WritePortableBackup(destination);
        foreach (var old in Directory.EnumerateFiles(directory, "settings-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(Math.Max(1, retainedCopies))) File.Delete(old);
        return true;
    }

    private void WritePortableBackup(string destination)
    {
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(_path))
            using (var output = File.Create(temp)) WriteBackupJson(input, output);
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static void WriteBackupJson(Stream input, Stream output)
    {
        using var json = JsonDocument.Parse(input);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The settings backup is invalid.");
        using var writer = new Utf8JsonWriter(output, new() { Indented = true });
        writer.WriteStartObject();
        foreach (var property in json.RootElement.EnumerateObject())
            if (!property.Name.Equals("GoogleBooksApiKey", StringComparison.OrdinalIgnoreCase)
                && !property.Name.Equals("GoogleBooksApiKeyProtected", StringComparison.OrdinalIgnoreCase)) property.WriteTo(writer);
        writer.WriteEndObject();
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
            if (tracking.Plan is { } plan) { plan.ReadingDays ??= []; plan.SkippedDates ??= []; }
        }
        settings.LibrarySchemaVersion = Math.Max(2, settings.LibrarySchemaVersion);
    }
}
