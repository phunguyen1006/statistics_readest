using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReadestStats.Core;

/// <summary>Durable rollback journal for app-owned files. An interrupted restore is rolled back on startup.</summary>
public sealed class RestoreTransaction(string settingsPath, string manualPath)
{
    private string Root => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manualPath))!, ".restore-pending");
    private string Journal => Path.Combine(Root, "journal.json");
    public sealed record Entry(string Name, bool Existed);
    public static bool IsAllowed(string name) => name is "settings.json" or "manual-reading.json" or "readest-stats.db" ||
        name.StartsWith("covers/", StringComparison.Ordinal) && name.Length > 7 && !name[7..].Contains('/') && !name.Contains('\\') && !name.Contains(':') && Path.GetFileName(name) is not ("." or "..");
    private string Destination(string name) => name switch
    {
        "settings.json" => settingsPath, "manual-reading.json" => manualPath,
        _ when IsAllowed(name) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manualPath))!, name),
        _ => throw new InvalidDataException("Invalid restore path.")
    };
    public void Apply(ZipArchive archive, string[] files, Action beforeReplace)
    {
        Recover();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
        Directory.CreateDirectory(Root);
        try
        {
            foreach (var name in files)
            {
                if (!IsAllowed(name)) throw new InvalidDataException("Invalid restore path.");
                var staged = Path.Combine(Root, "new", name); Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                archive.GetEntry(name)!.ExtractToFile(staged, true);
            }
            var dbPath = Path.Combine(Root, "new", "readest-stats.db");
            if (File.Exists(dbPath)) { using var db = AppDataBackupService.Connect(dbPath, SqliteOpenMode.ReadOnly); AppDataBackupService.ValidateDatabase(db); }
            beforeReplace();
            var names = files.ToList();
            if (names.Contains("manual-reading.json") && !names.Contains("readest-stats.db")) names.Add("readest-stats.db");
            var entries = names.Select(name => new Entry(name, File.Exists(Destination(name)))).ToArray();
            foreach (var item in entries.Where(e => e.Existed))
            {
                var old = Path.Combine(Root, "old", item.Name); Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Copy(Destination(item.Name), old, true);
                File.Copy(Destination(item.Name), Destination(item.Name) + ".before-restore.bak", true);
            }
            using (var journal = new FileStream(Journal + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(journal, entries); journal.Flush(true); }
            File.Move(Journal + ".tmp", Journal);
            foreach (var item in entries)
            {
                var target = Destination(item.Name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); ClearSidecars(item.Name, target);
                var staged = Path.Combine(Root, "new", item.Name);
                if (File.Exists(staged)) File.Copy(staged, target, true); else if (File.Exists(target)) File.Delete(target);
            }
            File.Delete(Journal);
        }
        catch { Recover(); throw; }
        finally { if (Directory.Exists(Root) && !File.Exists(Journal)) Directory.Delete(Root, true); }
    }
    public void Recover()
    {
        if (!File.Exists(Journal)) return;
        var entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(Journal)) ?? throw new InvalidDataException("Invalid restore recovery journal.");
        foreach (var item in entries)
        {
            var target = Destination(item.Name); ClearSidecars(item.Name, target);
            if (item.Existed) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(Root, "old", item.Name), target, true); }
            else if (File.Exists(target)) File.Delete(target);
        }
        File.Delete(Journal); Directory.Delete(Root, true);
    }
    private static void ClearSidecars(string name, string path)
    {
        if (name != "readest-stats.db") return;
        foreach (var file in new[] { path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
    }
}
