using System.IO.Compression;
using System.Text.Json;

namespace ReadestStats.Core;

public sealed record BackupManifest(int SchemaVersion, string AppVersion, DateTimeOffset CreatedAtUtc, string[] Files, int Books = 0, int Sessions = 0, int Notes = 0, int Covers = 0);

public sealed class AppDataBackupService
{
    private readonly string _settingsPath;
    private readonly string _manualReadingPath;
    private readonly string _databasePath;
    private readonly string _coverDirectory;

    public AppDataBackupService(string settingsPath, string manualReadingPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
        _manualReadingPath = Path.GetFullPath(manualReadingPath);
        _databasePath = Path.Combine(Path.GetDirectoryName(_manualReadingPath)!, "readest-stats.db");
        _coverDirectory = Path.Combine(Path.GetDirectoryName(_manualReadingPath)!, "covers");
    }

    public void Create(string destination, string appVersion)
    {
        var fullDestination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temp = fullDestination + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        var files = new List<string>();
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            AddIfPresent(archive, _settingsPath, "settings.json", files);
            AddIfPresent(archive, _manualReadingPath, "manual-reading.json", files);
            AddIfPresent(archive, _databasePath, "readest-stats.db", files);
            if (Directory.Exists(_coverDirectory))
            {
                foreach (var cover in Directory.EnumerateFiles(_coverDirectory))
                {
                    var entryName = "covers/" + Path.GetFileName(cover);
                    archive.CreateEntryFromFile(cover, entryName, CompressionLevel.Optimal);
                    files.Add(entryName);
                }
            }
            var manual = ReadManualSummary();
            var manifest = new BackupManifest(1, appVersion, DateTimeOffset.UtcNow, files.ToArray(), manual.Books, manual.Sessions, manual.Notes, files.Count(file => file.StartsWith("covers/", StringComparison.Ordinal)));
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        File.Move(temp, fullDestination, true);
    }

    public string CreateAutomatic(string appVersion, int keep = 7)
    {
        var directory = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backups");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"readest-stats-{DateTime.Today:yyyy-MM-dd}.zip");
        Create(destination, appVersion);
        foreach (var stale in Directory.EnumerateFiles(directory, "readest-stats-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(Math.Max(1, keep))) File.Delete(stale);
        return destination;
    }

    public BackupManifest Inspect(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("This is not a Readest Stats backup.");
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new InvalidDataException("The backup manifest is invalid.");
    }

    public void Restore(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        _ = ReadManifest(archive);
        RestoreEntry(archive, "settings.json", _settingsPath);
        RestoreEntry(archive, "manual-reading.json", _manualReadingPath);
        RestoreEntry(archive, "readest-stats.db", _databasePath);
        foreach (var entry in archive.Entries.Where(item => item.FullName.StartsWith("covers/", StringComparison.Ordinal) && !item.FullName.EndsWith('/')))
        {
            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            RestoreArchiveEntry(entry, Path.Combine(_coverDirectory, fileName));
        }
    }

    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("This is not a Readest Stats backup.");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new InvalidDataException("The backup manifest is invalid.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("This backup version is not supported.");
        return manifest;
    }

    private static void AddIfPresent(ZipArchive archive, string path, string entryName, ICollection<string> files)
    {
        if (!File.Exists(path)) return;
        archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        files.Add(entryName);
    }

    private (int Books, int Sessions, int Notes) ReadManualSummary()
    {
        try
        {
            if (!File.Exists(_manualReadingPath)) return (0, 0, 0);
            var data = JsonSerializer.Deserialize<ManualReadingData>(File.ReadAllText(_manualReadingPath));
            return (data?.Books?.Count ?? 0, data?.Sessions?.Count ?? 0, data?.Sessions?.Count(session => !string.IsNullOrWhiteSpace(session.Note)) ?? 0);
        }
        catch { return (0, 0, 0); }
    }

    private static void RestoreEntry(ZipArchive archive, string entryName, string destination)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return;
        RestoreArchiveEntry(entry, destination);
    }

    private static void RestoreArchiveEntry(ZipArchiveEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".restore.tmp";
        using (var source = entry.Open())
        using (var target = File.Create(temp)) source.CopyTo(target);
        if (File.Exists(destination)) File.Copy(destination, destination + ".before-restore.bak", true);
        File.Move(temp, destination, true);
    }
}
