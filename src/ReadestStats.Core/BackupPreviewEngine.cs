using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ReadestStats.Core;

public sealed record BackupPreviewDomain(string Name, int? CurrentCount, int? BackupCount, string Action, string Detail)
{
    public string CountComparison => $"{(CurrentCount is { } current ? current.ToString() : "Unknown")} → {(BackupCount is { } backup ? backup.ToString() : "Unchanged")}";
}

public sealed record BackupRestorePreview(BackupManifest Manifest, IReadOnlyList<BackupPreviewDomain> Domains,
    IReadOnlyList<string> ChangedSettings, IReadOnlyList<string> Warnings, bool BackupDatabaseValidated,
    string RestoreSemantics)
{
    public string VerificationStatus => Manifest.SchemaVersion == 2 ? "Checksums verified" : "Legacy backup · no checksums";
}

/// <summary>Read-only domain comparison after ZIP verification. No live files are replaced or migrated.</summary>
public static class BackupPreviewEngine
{
    private sealed record LibraryCounts(int Books, int Sessions, int Notes, int ActiveTimers);
    private static readonly HashSet<string> SecretFields = new(StringComparer.OrdinalIgnoreCase)
        { "GoogleBooksApiKey", "GoogleBooksApiKeyProtected" };

    internal static BackupRestorePreview FromVerifiedArchive(ZipArchive archive, BackupManifest manifest,
        string settingsPath, string manualPath, string databasePath, string coverDirectory)
    {
        var warnings = new List<string>();
        LibraryCounts? currentLibrary = null;
        try
        {
            currentLibrary = ReadCurrentLibrary(databasePath, manualPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SqliteException or InvalidDataException)
        {
            warnings.Add("The current physical library cannot be inspected. Its counts are unknown; restore will still preserve recovery copies.");
        }
        using var currentSettings = ReadCurrentSettings(settingsPath, warnings);
        using var backupSettings = archive.GetEntry("settings.json") is { } settingsEntry
            ? JsonDocument.Parse(BackupReadLimits.ReadSmallEntry(settingsEntry)) : null;
        var manualEntry = archive.GetEntry("manual-reading.json");
        var databaseEntry = archive.GetEntry("readest-stats.db");
        LibraryCounts? jsonLibrary = null;
        if (manualEntry is not null)
        {
            try { jsonLibrary = CountLibrary(BackupReadLimits.ReadSmallEntry(manualEntry)); }
            catch (Exception ex) when (databaseEntry is not null && (ex is JsonException or InvalidDataException))
            {
                warnings.Add("The JSON recovery mirror cannot be interpreted. The verified database will be authoritative after restore.");
            }
        }
        LibraryCounts? backupLibrary = jsonLibrary;
        var databaseValidated = false;
        if (databaseEntry is not null)
        {
            var staging = Path.Combine(Path.GetTempPath(), "ReadestStats-backup-preview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var database = Path.Combine(staging, "readest-stats.db");
                using (var input = databaseEntry.Open())
                using (var output = new FileStream(database, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    BackupReadLimits.CopyBounded(input, output, BackupReadLimits.EntryLimit(databaseEntry.FullName), BackupReadLimits.MaxTotalBytes);
                backupLibrary = CountDatabase(database);
                databaseValidated = true;
                if (jsonLibrary is not null && jsonLibrary != backupLibrary)
                    warnings.Add("The JSON recovery mirror has different counts from the verified database. The database is authoritative after restore.");
            }
            finally { Directory.Delete(staging, true); }
        }
        var currentCoverCount = CountCurrentCovers(coverDirectory, warnings);
        var backupCoverCount = manifest.Files.Count(name => name.StartsWith("covers/", StringComparison.Ordinal));
        var currentFields = SettingsFields(currentSettings);
        var backupFields = SettingsFields(backupSettings);
        string[] changedSettings = backupSettings is null || currentSettings is null ? [] : currentFields.Keys.Union(backupFields.Keys)
            .Where(name => !currentFields.TryGetValue(name, out var current) || !backupFields.TryGetValue(name, out var backup) || current != backup)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var libraryAction = backupLibrary is null ? "Keep current" : "Replace (not merge)";
        const string libraryDetail = "The entire app-owned physical library is replaced; these counts are read from the backup data, not its manifest summary.";
        var domains = new List<BackupPreviewDomain>
        {
            new("Physical books", currentLibrary?.Books, backupLibrary?.Books, libraryAction, libraryDetail),
            new("Manual sessions", currentLibrary?.Sessions, backupLibrary?.Sessions, libraryAction, libraryDetail),
            new("Session notes", currentLibrary?.Notes, backupLibrary?.Notes, libraryAction, "Private note text is never shown in this preview."),
            new("Active timer", currentLibrary?.ActiveTimers, backupLibrary?.ActiveTimers, libraryAction, "A restored timer is recovered using the normal paused-session safety rules."),
            new("Settings", currentSettings is null ? null : currentFields.Count, backupSettings is null ? null : backupFields.Count,
                backupSettings is null ? "Keep current" : "Replace (not merge)",
                backupSettings is null ? "This backup has no settings file; existing settings stay in place." : $"{changedSettings.Length} setting field(s) differ. Goals, source/filter preferences and note curation are restored together; API keys are not portable."),
            new("Goals", ArrayCount(currentSettings, "Goals"), ArrayCount(backupSettings, "Goals"),
                backupSettings is null ? "Keep current" : "Replace (not merge)", "Saved goal definitions in the app-owned settings file."),
            new("Note curation", ObjectCount(currentSettings, "NoteStates"), ObjectCount(backupSettings, "NoteStates"),
                backupSettings is null ? "Keep current" : "Replace (not merge)", "Favorites and personal note curation; original Readest highlights are not backed up here."),
            new("Cached covers", currentCoverCount, backupCoverCount == 0 ? null : backupCoverCount,
                backupCoverCount == 0 ? "Keep current" : "Overlay (other covers kept)", "Matching cover files are replaced. Other cached covers remain; cover counts are not a projected final library size.")
        };
        if (manifest.SchemaVersion == 1) warnings.Add("This legacy backup has no stored checksums. Its file paths, size limits and data syntax were checked.");
        if (backupSettings is null) warnings.Add("Settings, goals and note curation are absent and will stay unchanged.");
        if (backupLibrary is null) warnings.Add("Physical books, sessions and session notes are absent and will stay unchanged.");
        if (backupLibrary?.ActiveTimers > 0) warnings.Add("The backup contains an unfinished physical-reading timer. It will be recovered safely; time while the app was closed is not counted.");
        warnings.Add("This backup does not contain original Readest statistics, book files or config.json highlights/annotations. Back up Readest separately.");
        warnings.Add("The backup is not encrypted. Keep it private; normal backups omit API credentials, but older backups may contain credential bytes.");
        var actualManifest = manifest with
        {
            Books = backupLibrary?.Books ?? 0, Sessions = backupLibrary?.Sessions ?? 0,
            Notes = backupLibrary?.Notes ?? 0, Covers = backupCoverCount
        };
        return new(actualManifest, domains, changedSettings, warnings, databaseValidated,
            "Restore replaces included app-owned data; it does not merge libraries. Missing domains stay unchanged. Current files are preserved as recovery copies, and Readest remains read-only.");
    }

    private static LibraryCounts CountDatabase(string path)
    {
        using var connection = AppDataBackupService.Connect(path, SqliteOpenMode.ReadOnly);
        AppDataBackupService.ValidateDatabase(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM manual_books), (SELECT COUNT(*) FROM manual_sessions), (SELECT COUNT(*) FROM manual_sessions WHERE length(trim(note)) > 0), (SELECT COUNT(*) FROM active_manual_session)";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("The backup database summary is unavailable.");
        return new(checked((int)reader.GetInt64(0)), checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)), checked((int)reader.GetInt64(3)));
    }

    private static LibraryCounts CountLibrary(byte[] json)
    {
        if (json.LongLength > BackupReadLimits.EntryLimit("manual-reading.json")) throw new InvalidDataException("The physical-library file is too large to preview.");
        var data = JsonSerializer.Deserialize<ManualReadingData>(json) ?? throw new InvalidDataException("The backup physical library is empty.");
        if (data.Books?.Any(book => book is null) == true || data.Sessions?.Any(session => session is null) == true)
            throw new InvalidDataException("The backup physical library contains empty records.");
        return new(data.Books?.Count ?? 0, data.Sessions?.Count ?? 0,
            data.Sessions?.Count(session => !string.IsNullOrWhiteSpace(session.Note)) ?? 0, data.ActiveSession is null ? 0 : 1);
    }

    private static LibraryCounts ReadCurrentLibrary(string databasePath, string manualPath)
    {
        try
        {
            // Unlike File.Exists, this does not hide access-denied errors as an empty library.
            _ = File.GetAttributes(databasePath);
            return CountDatabase(databasePath);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        try { return CountLibrary(ReadBoundedJsonFile(manualPath, "manual-reading.json")); }
        catch (FileNotFoundException) { return new(0, 0, 0, 0); }
        catch (DirectoryNotFoundException) { return new(0, 0, 0, 0); }
    }

    private static JsonDocument? ReadCurrentSettings(string path, ICollection<string> warnings)
    {
        try
        {
            using var input = File.OpenRead(path);
            if (input.Length > BackupReadLimits.EntryLimit("settings.json")) throw new InvalidDataException("Settings are too large to preview.");
            var document = JsonDocument.Parse(input);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
            throw new InvalidDataException("Settings are not a JSON object.");
        }
        catch (FileNotFoundException) { return JsonDocument.Parse("{}"); }
        catch (DirectoryNotFoundException) { return JsonDocument.Parse("{}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warnings.Add("The current settings file cannot be inspected. Its counts are unknown; restore will preserve its original bytes in recovery copies.");
            return null;
        }
    }

    private static Dictionary<string, string> SettingsFields(JsonDocument? document)
    {
        if (document is null) return [];
        // Only expose names from the app's schema; untrusted JSON property names and secret values remain hidden.
        var knownNames = typeof(AppSettings).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject().Where(property => knownNames.Contains(property.Name) && !SecretFields.Contains(property.Name)))
        {
            // JSON deserialization uses the last duplicate value; mirror that behavior without exposing values.
            fields[property.Name] = JsonSerializer.Serialize(property.Value);
        }
        return fields;
    }

    private static int? ArrayCount(JsonDocument? document, string name) => document is null ? null :
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.GetArrayLength() : 0;

    private static int? ObjectCount(JsonDocument? document, string name) => document is null ? null :
        document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value.EnumerateObject().Count() : 0;

    private static int? CountCurrentCovers(string directory, ICollection<string> warnings)
    {
        try { return Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Count() : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add("The current cached-cover directory cannot be counted."); return null; }
    }

    private static byte[] ReadBoundedJsonFile(string path, string name)
    {
        using var input = File.OpenRead(path);
        var limit = BackupReadLimits.EntryLimit(name);
        if (input.Length > limit) throw new InvalidDataException("The physical-library file is too large to preview.");
        using var output = new MemoryStream();
        BackupReadLimits.CopyBounded(input, output, limit, limit);
        return output.ToArray();
    }
}
