using System.Text.Json;

namespace ReadestStats.Core;

public sealed class ReadestDatabaseLocator
{
    private readonly string _roaming;
    private readonly string _local;

    public ReadestDatabaseLocator(string? roamingAppData = null, string? localAppData = null)
    {
        _roaming = roamingAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public string AppDataDirectory => Path.Combine(_local, "ReadestStats");
    public string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");

    public async Task<string?> LocateAsync(string? savedPath = null, CancellationToken cancellationToken = default, bool preferSavedPath = true)
    {
        if (preferSavedPath && IsDatabase(savedPath)) return Path.GetFullPath(savedPath!);

        var defaultPath = Path.Combine(_roaming, "com.bilingify.readest", "Readest", "statistics.db");
        var settingsPath = Path.Combine(_roaming, "com.bilingify.readest", "settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                await using var stream = File.OpenRead(settingsPath);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (json.RootElement.TryGetProperty("customRootDir", out var root) && root.ValueKind == JsonValueKind.String)
                {
                    var custom = root.GetString();
                    if (!string.IsNullOrWhiteSpace(custom))
                    {
                        var candidate = Path.Combine(custom, "Readest", "statistics.db");
                        // A configured custom root is authoritative. Do not silently use
                        // an older default database while the custom drive is unavailable.
                        return IsDatabase(candidate) ? Path.GetFullPath(candidate) : null;
                    }
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // A legacy saved path outside the standard location may have been
        // chosen manually before DatabasePathIsManual was introduced.
        if (IsDatabase(savedPath) && !string.Equals(Path.GetFullPath(savedPath!), Path.GetFullPath(defaultPath), StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(savedPath!);
        if (IsDatabase(defaultPath)) return defaultPath;
        if (IsDatabase(savedPath)) return Path.GetFullPath(savedPath!);

        foreach (var root in PortableCandidates())
        {
            var candidate = Path.Combine(root, "Readest", "statistics.db");
            if (IsDatabase(candidate)) return candidate;
        }
        return null;
    }

    private static bool IsDatabase(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && string.Equals(Path.GetFileName(path), "statistics.db", StringComparison.OrdinalIgnoreCase);

    private IEnumerable<string> PortableCandidates()
    {
        yield return AppContext.BaseDirectory;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Readest");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Readest");
    }
}
