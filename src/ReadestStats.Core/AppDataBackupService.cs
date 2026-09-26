using System.IO.Compression;
using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace ReadestStats.Core;

public sealed record BackupManifest(int SchemaVersion, string AppVersion, DateTimeOffset CreatedAtUtc, string[] Files, int Books = 0, int Sessions = 0, int Notes = 0, int Covers = 0, Dictionary<string, string>? Checksums = null);
public sealed record BackupHistoryItem(string Path, string FileName, DateTimeOffset CreatedAt, long SizeBytes, string Version, int Books, int Sessions, string Status)
{
    public string SizeLabel => SizeBytes >= 1024 * 1024 ? $"{SizeBytes / 1024d / 1024d:0.0} MB" : $"{Math.Max(1, SizeBytes / 1024d):0} KB";
    public string Detail => $"{CreatedAt.LocalDateTime:g} · {SizeLabel} · v{Version} · {Books} books · {Sessions} sessions";
}

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
        Recovery().Recover();
    }

    public void Create(string destination, string appVersion)
    {
        var fullDestination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temp = fullDestination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var snapshot = temp + ".db";
        try
        {
        if (File.Exists(_databasePath))
        {
            using var source = Connect(_databasePath, SqliteOpenMode.ReadOnly);
            using var target = Connect(snapshot, SqliteOpenMode.ReadWriteCreate);
            source.BackupDatabase(target);
            ValidateDatabase(target);
        }
        if (File.Exists(temp)) File.Delete(temp);
        var files = new List<string>();
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            AddIfPresent(archive, _settingsPath, "settings.json", files);
            if (File.Exists(snapshot))
            {
                var data = new AppDatabase(snapshot).LoadManualData();
                using (var json = archive.CreateEntry("manual-reading.json").Open()) JsonSerializer.Serialize(json, data);
                files.Add("manual-reading.json");
            }
            else AddIfPresent(archive, _manualReadingPath, "manual-reading.json", files);
            AddIfPresent(archive, snapshot, "readest-stats.db", files);
            if (Directory.Exists(_coverDirectory))
            {
                foreach (var cover in Directory.EnumerateFiles(_coverDirectory))
                {
                    var entryName = "covers/" + Path.GetFileName(cover);
                    archive.CreateEntryFromFile(cover, entryName, CompressionLevel.Optimal);
                    files.Add(entryName);
                }
            }
            var manual = ReadManualSummary(snapshot);
            var manifest = new BackupManifest(1, appVersion, DateTimeOffset.UtcNow, files.ToArray(), manual.Books, manual.Sessions, manual.Notes, files.Count(file => file.StartsWith("covers/", StringComparison.Ordinal)));
            var entry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Update))
        {
            var checksums = new Dictionary<string, string>();
            foreach (var file in files) { using var input = archive.GetEntry(file)!.Open(); checksums[file] = Convert.ToHexString(SHA256.HashData(input)); }
            var manifest = ReadManifest(archive) with { SchemaVersion = 2, Checksums = checksums };
            archive.GetEntry("manifest.json")!.Delete();
            using var output = archive.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(output, manifest);
        }
        Inspect(temp);
        File.Move(temp, fullDestination, true);
        }
        finally { foreach (var file in new[] { temp, snapshot, snapshot + "-wal", snapshot + "-shm" }) if (File.Exists(file)) File.Delete(file); }
    }

    public string CreateAutomatic(string appVersion, int keep = 7)
    {
        var directory = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backups");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"readest-stats-{DateTime.Today:yyyy-MM-dd}.zip");
        Create(destination, appVersion);
        var cutoff = DateTime.Today.AddDays(1 - Math.Max(1, keep));
        foreach (var stale in Directory.EnumerateFiles(directory, "readest-stats-*.zip"))
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(stale)[14..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < cutoff) File.Delete(stale);
        return destination;
    }

    public BackupManifest Inspect(string source)
    {
        using var archive = ZipFile.OpenRead(source);
        return Verify(archive);
    }

    public IReadOnlyList<BackupHistoryItem> ListAutomatic()
    {
        var directory = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backups");
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.zip").Where(path => Path.GetFileName(path).StartsWith("readest-stats-", StringComparison.Ordinal) || Path.GetFileName(path).StartsWith("before-restore-", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path =>
            {
                try
                {
                    var manifest = Inspect(path);
                    return new BackupHistoryItem(path, Path.GetFileName(path), manifest.CreatedAtUtc, new FileInfo(path).Length, manifest.AppVersion, manifest.Books, manifest.Sessions, manifest.SchemaVersion == 2 ? "Verified" : "Legacy backup");
                }
                catch
                {
                    return new BackupHistoryItem(path, Path.GetFileName(path), File.GetLastWriteTimeUtc(path), new FileInfo(path).Length, "?", 0, 0, "Unreadable");
                }
            }).ToArray();
    }

    public void DeleteAutomatic(string path)
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backups"));
        var target = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(target), directory, StringComparison.OrdinalIgnoreCase) || !(Path.GetFileName(target).StartsWith("readest-stats-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(target).StartsWith("before-restore-", StringComparison.OrdinalIgnoreCase)) || !target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only automatic Readest Stats backups can be deleted here.");
        if (File.Exists(target)) File.Delete(target);
    }

    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "backups");

    public void Restore(string source)
    {
        Recovery().Recover();
        using var archive = ZipFile.OpenRead(source);
        var manifest = Verify(archive);
        var recovery = Path.Combine(BackupDirectory, $"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        Recovery().Apply(archive, manifest.Files, () =>
        {
            try { Create(recovery, "2.3.0"); }
            catch (Exception ex) when (ex is JsonException or SqliteException or InvalidDataException)
            {
                // Preserve damaged live bytes without blocking a verified restore.
                using var raw = ZipFile.Open(recovery, ZipArchiveMode.Create);
                var files = new List<string>();
                AddIfPresent(raw, _settingsPath, "settings.json", files);
                AddIfPresent(raw, _manualReadingPath, "manual-reading.json", files);
                AddIfPresent(raw, _databasePath, "readest-stats.db", files);
                AddIfPresent(raw, _databasePath + "-wal", "readest-stats.db-wal", files);
                using var output = raw.CreateEntry("manifest.json").Open();
                JsonSerializer.Serialize(output, new BackupManifest(1, "2.3.0", DateTimeOffset.UtcNow, files.ToArray()));
            }
            if (File.Exists(_databasePath))
                try { new AppDatabase(_databasePath).Checkpoint(); } catch (SqliteException) { }
        });
    }

    private static BackupManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("This is not a Readest Stats backup.");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ?? throw new InvalidDataException("The backup manifest is invalid.");
        if (manifest.SchemaVersion is not (1 or 2) || manifest.Files is null) throw new InvalidDataException("This backup version is not supported.");
        return manifest;
    }

    private static void AddIfPresent(ZipArchive archive, string path, string entryName, ICollection<string> files)
    {
        if (!File.Exists(path)) return;
        archive.CreateEntryFromFile(path, entryName, CompressionLevel.Optimal);
        files.Add(entryName);
    }

    private (int Books, int Sessions, int Notes) ReadManualSummary(string snapshot)
    {
        try
        {
            if (!File.Exists(snapshot) && !File.Exists(_manualReadingPath)) return (0, 0, 0);
            var data = File.Exists(snapshot) ? new AppDatabase(snapshot).LoadManualData() : JsonSerializer.Deserialize<ManualReadingData>(File.ReadAllText(_manualReadingPath));
            return (data?.Books?.Count ?? 0, data?.Sessions?.Count ?? 0, data?.Sessions?.Count(session => !string.IsNullOrWhiteSpace(session.Note)) ?? 0);
        }
        catch { return (0, 0, 0); }
    }

    private RestoreTransaction Recovery() => new(_settingsPath, _manualReadingPath);
    internal static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    internal static void ValidateDatabase(SqliteConnection connection)
    {
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check";
        if (command.ExecuteScalar()?.ToString() != "ok") throw new InvalidDataException("The backup database failed its integrity check.");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('manual_books','manual_sessions','active_manual_session')";
        if (Convert.ToInt32(command.ExecuteScalar()) != 3) throw new InvalidDataException("The backup database is missing required tables.");
    }
    private static BackupManifest Verify(ZipArchive archive)
    {
        var manifest = ReadManifest(archive);
        if (manifest.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Length || archive.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("The backup contains duplicate entries.");
        foreach (var name in manifest.Files)
        {
            if (!RestoreTransaction.IsAllowed(name)) throw new InvalidDataException("The backup contains an unsupported path.");
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"The backup is missing {name}.");
            using var input = entry.Open(); var hash = Convert.ToHexString(SHA256.HashData(input));
            if (manifest.SchemaVersion == 2 && (manifest.Checksums is null || !manifest.Checksums.TryGetValue(name, out var expected) || !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException($"The backup failed verification: {name}.");
            if (name.EndsWith(".json", StringComparison.Ordinal)) { using var data = entry.Open(); using var json = JsonDocument.Parse(data); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid backup data."); }
        }
        if (archive.Entries.Any(e => e.FullName != "manifest.json" && !manifest.Files.Contains(e.FullName, StringComparer.Ordinal))) throw new InvalidDataException("The backup contains unlisted files.");
        return manifest;
    }

}
