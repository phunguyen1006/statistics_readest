using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class HardeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestHardening-" + Guid.NewGuid().ToString("N"));

    public HardeningTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("https://openlibrary.org/books/1", true)]
    [InlineData("http://example.org/book", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:pass@example.org/book", false)]
    public void External_metadata_links_allow_only_web_pages(string value, bool expected) =>
        Assert.Equal(expected, ExternalLinkPolicy.TryGetWebUri(value, out _));

    [Fact]
    public void Readest_book_hash_cannot_escape_books_directory()
    {
        var outside = Path.Combine(_root, "outside.epub");
        File.WriteAllText(outside, "book");
        Assert.Null(ReadestLibraryLocator.FindBookFile(Path.Combine(_root, "statistics.db"), "../.."));
        Assert.Null(ReadestLibraryLocator.FindBookFile(Path.Combine(_root, "statistics.db"), "../outside"));
        Assert.False(ReadestLibraryLocator.IsSupportedBookFile(Path.Combine(_root, "app.exe")));
        Assert.True(ReadestLibraryLocator.IsSupportedBookFile(outside));
        Assert.False(ReadestLibraryLocator.IsLocalBookFile(@"\\server\share\book.epub"));
    }

    [Fact]
    public async Task Corrupt_settings_are_not_silently_replaced()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => new SettingsStore(path).LoadAsync());
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public async Task Legacy_api_key_is_migrated_to_current_user_protection()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{\"GoogleBooksApiKey\":\"test-secret-api-key\",\"Language\":\"Tiếng Việt\"}");
        var loaded = await new SettingsStore(path).LoadAsync();
        Assert.Equal("test-secret-api-key", loaded.GoogleBooksApiKey);
        Assert.Equal("Tiếng Việt", loaded.Language);
        var persisted = File.ReadAllText(path);
        Assert.DoesNotContain("test-secret-api-key", persisted);
        using var json = JsonDocument.Parse(persisted);
        Assert.False(json.RootElement.TryGetProperty("GoogleBooksApiKey", out _));
        Assert.Equal("test-secret-api-key", (await new SettingsStore(path).LoadAsync()).GoogleBooksApiKey);
    }

    [Fact]
    public async Task Unreadable_protected_key_does_not_discard_reading_preferences()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{\"GoogleBooksApiKeyProtected\":\"not-base64\",\"DefaultRangePreset\":\"7 days\"}");
        var store = new SettingsStore(path);
        var loaded = await store.LoadAsync();
        Assert.Null(loaded.GoogleBooksApiKey);
        Assert.Equal("7 days", loaded.DefaultRangePreset);
        Assert.NotNull(store.ApiKeyWarning);
    }

    [Fact]
    public void New_settings_and_zip_backups_omit_legacy_and_protected_api_keys()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{\"GoogleBooksApiKey\":\"plain-secret\",\"GoogleBooksApiKeyProtected\":\"protected-secret\",\"Language\":\"English\"}");
        var portable = Path.Combine(_root, "portable.json");
        new SettingsStore(path).Backup(portable);
        Assert.DoesNotContain("secret", File.ReadAllText(portable));
        var zipPath = Path.Combine(_root, "portable.zip");
        new AppDataBackupService(path, Path.Combine(_root, "manual-reading.json")).Create(zipPath, "2.4.0");
        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry("settings.json")!.Open());
        var content = reader.ReadToEnd();
        Assert.DoesNotContain("secret", content);
        Assert.Contains("English", content);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task Corrupt_manual_data_without_valid_backup_is_not_replaced(string content)
    {
        var path = Path.Combine(_root, "manual-reading.json");
        File.WriteAllText(path, content);
        File.WriteAllText(path + ".bak", "also broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ManualReadingStore(path).LoadAsync());
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public async Task Recovery_keeps_damaged_manual_bytes_and_loads_only_a_valid_backup()
    {
        var path = Path.Combine(_root, "manual-reading.json");
        File.WriteAllText(path, "{damaged");
        File.WriteAllText(path + ".bak", JsonSerializer.Serialize(new ManualReadingData { Books = [new() { Id = -1, Title = "Recovered" }] }));
        Assert.Equal("Recovered", Assert.Single((await new ManualReadingStore(path).LoadAsync()).Books).Title);
        Assert.Equal("{damaged", File.ReadAllText(path + ".corrupt"));
    }

    [Fact]
    public async Task Custom_Readest_root_outranks_stale_saved_and_default_database()
    {
        var roaming = Path.Combine(_root, "roaming");
        var local = Path.Combine(_root, "local");
        var settings = Path.Combine(roaming, "com.bilingify.readest", "settings.json");
        var oldDatabase = Path.Combine(roaming, "com.bilingify.readest", "Readest", "statistics.db");
        var custom = Path.Combine(_root, "new-root");
        var newDatabase = Path.Combine(custom, "Readest", "statistics.db");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        Directory.CreateDirectory(Path.GetDirectoryName(oldDatabase)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newDatabase)!);
        File.WriteAllText(settings, JsonSerializer.Serialize(new { customRootDir = custom }));
        File.WriteAllText(oldDatabase, "old");
        File.WriteAllText(newDatabase, "new");
        var locator = new ReadestDatabaseLocator(roaming, local);
        Assert.Equal(newDatabase, await locator.LocateAsync(oldDatabase, preferSavedPath: false));
        Assert.Equal(oldDatabase, await locator.LocateAsync(oldDatabase, preferSavedPath: true));
        File.Delete(newDatabase);
        Assert.Null(await locator.LocateAsync(oldDatabase, preferSavedPath: false));
    }

    [Fact]
    public async Task Legacy_external_database_selection_is_preserved_without_a_custom_root()
    {
        var roaming = Path.Combine(_root, "legacy-roaming");
        var defaultPath = Path.Combine(roaming, "com.bilingify.readest", "Readest", "statistics.db");
        var selected = Path.Combine(_root, "selected", "statistics.db");
        Directory.CreateDirectory(Path.GetDirectoryName(defaultPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(selected)!);
        File.WriteAllText(defaultPath, "default");
        File.WriteAllText(selected, "manual");
        var locator = new ReadestDatabaseLocator(roaming, Path.Combine(_root, "legacy-local"));
        Assert.Equal(selected, await locator.LocateAsync(selected, preferSavedPath: false));
    }

    [Fact]
    public async Task Sqlite_save_remains_successful_when_json_mirror_fails()
    {
        var mirror = Path.Combine(_root, "manual-reading.json");
        Directory.CreateDirectory(mirror);
        var store = new ManualReadingStore(mirror);
        await store.SaveAsync(new ManualReadingData { Books = [new ManualBook { Id = -1, Title = "Saved" }] });
        Assert.NotNull(store.LastMirrorError);
        Assert.Equal("Saved", Assert.Single((await store.LoadAsync()).Books).Title);
    }

    [Fact]
    public async Task Csv_preview_rejects_oversized_file_and_field()
    {
        var file = Path.Combine(_root, "library.csv");
        await File.WriteAllTextAsync(file, "Title\n" + new string('A', 64_001));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryImportService().PreviewCsvAsync(file));
        using (var output = File.Create(file)) output.SetLength(32_000_001);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryImportService().PreviewCsvAsync(file));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Csv_column_limit_applies_at_newline_and_end_of_file(bool newline)
    {
        var file = Path.Combine(_root, "columns.csv");
        await File.WriteAllTextAsync(file, "Title\n" + string.Join(',', Enumerable.Repeat("value", 257)) + (newline ? "\n" : ""));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LibraryImportService().PreviewCsvAsync(file));
    }

    [Fact]
    public void Oversized_backup_manifest_is_rejected_without_touching_live_settings()
    {
        var settings = Path.Combine(_root, "settings.json");
        File.WriteAllText(settings, "{\"Language\":\"English\"}");
        var zipPath = Path.Combine(_root, "oversized.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(new string(' ', 1_000_001));
        var service = new AppDataBackupService(settings, Path.Combine(_root, "manual-reading.json"));
        Assert.Throws<InvalidDataException>(() => service.Restore(zipPath));
        Assert.Equal("{\"Language\":\"English\"}", File.ReadAllText(settings));
    }

    [Fact]
    public void Backup_with_too_many_entries_is_rejected_before_entry_materialization()
    {
        var zipPath = Path.Combine(_root, "too-many.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write("{\"SchemaVersion\":1,\"AppVersion\":\"test\",\"Files\":[]}");
            for (var index = 0; index < 20_001; index++) zip.CreateEntry($"covers/{index}.jpg");
        }
        var service = new AppDataBackupService(Path.Combine(_root, "settings.json"), Path.Combine(_root, "manual-reading.json"));
        Assert.Throws<InvalidDataException>(() => service.Inspect(zipPath));
    }

    [Fact]
    public void Forged_zip_file_count_does_not_bypass_entry_preflight()
    {
        var zipPath = Path.Combine(_root, "forged-count.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("manifest.json");
            zip.CreateEntry("settings.json");
        }
        using (var file = new FileStream(zipPath, FileMode.Open, FileAccess.Write))
        {
            file.Seek(-14, SeekOrigin.End);
            file.Write(new byte[] { 1, 0, 1, 0 });
        }
        var service = new AppDataBackupService(Path.Combine(_root, "settings.json"), Path.Combine(_root, "manual-reading.json"));
        var error = Assert.Throws<InvalidDataException>(() => service.Inspect(zipPath));
        Assert.Contains("count", error.Message);
    }

    [Fact]
    public async Task Concurrent_cover_requests_do_not_share_temporary_files()
    {
        var image = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0 };
        var http = new HttpClient(new StaticHandler(() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }));
        var directory = Path.Combine(_root, "parallel-covers");
        var cache = new CoverCacheService(directory, http);
        var paths = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => cache.CacheAsync("https://covers.example.invalid/same.png")));
        Assert.All(paths, path => Assert.NotNull(path));
        Assert.Single(Directory.EnumerateFiles(directory));
        Assert.Equal(image, await File.ReadAllBytesAsync(paths[0]!));
    }

    [Fact]
    public async Task Remote_cover_failure_never_leaves_a_live_url_in_the_library()
    {
        var http = new HttpClient(new StaticHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not an image"))
        }));
        var cache = new CoverCacheService(Path.Combine(_root, "covers"), http);
        Assert.Null(await cache.CacheAsync("https://covers.example.invalid/book.jpg"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "covers")));
    }

    [Fact]
    public async Task Unknown_length_remote_cover_is_stopped_at_streaming_limit()
    {
        var http = new HttpClient(new StaticHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new RepeatingStream(CoverCacheService.MaxCoverBytes + 1))
        }));
        var directory = Path.Combine(_root, "oversized-covers");
        var cache = new CoverCacheService(directory, http);
        Assert.Null(await cache.CacheAsync("https://covers.example.invalid/book.jpg"));
        Assert.Empty(Directory.EnumerateFiles(directory));
    }

    [Fact]
    public void Reading_time_is_clipped_to_both_sides_of_midnight()
    {
        var start = new DateTimeOffset(2026, 9, 28, 23, 50, 0, TimeSpan.Zero);
        var events = new[] { new ReadingEvent(1, 1, start.ToUnixTimeSeconds(), 1200, null) };
        var first = ReadingEventWindow.Clip(events, start.AddMinutes(-10), start.AddMinutes(10));
        var second = ReadingEventWindow.Clip(events, start.AddMinutes(10), start.AddMinutes(30));
        Assert.Equal(600, Assert.Single(first).DurationSeconds);
        Assert.Equal(600, Assert.Single(second).DurationSeconds);
        var range = new ResolvedDateRange("Custom", start.AddMinutes(10), start.AddMinutes(30), start.AddMinutes(-10), start.AddMinutes(10), new(2026, 9, 29), new(2026, 9, 29), 1);
        var comparison = new AnalyticsEngine(TimeZoneInfo.Utc).Compare(events, range);
        Assert.Equal(600, comparison.CurrentValue);
        Assert.Equal(600, comparison.PreviousValue);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private sealed class StaticHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(WithRequest(response(), request));

        private static HttpResponseMessage WithRequest(HttpResponseMessage result, HttpRequestMessage request)
        {
            result.RequestMessage = request;
            return result;
        }
    }

    private sealed class RepeatingStream(long length) : Stream
    {
        private long _remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => Fill(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Fill(buffer.Span));
        private int Fill(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, _remaining);
            buffer[..count].Fill(0x41);
            _remaining -= count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
