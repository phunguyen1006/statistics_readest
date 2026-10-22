using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ReadestStats.ViewModels;
using ReadestStats.Views;
using ReadestStats.Core;
using ReadestStats.Localization;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace ReadestStats.UiTests;

public sealed class WpfSmokeTests
{
    private static string _stage = "starting";
    [Fact]
    public void All_pages_construct_measure_and_arrange_on_sta()
    {
        RunOnSta(async () =>
        {
            var app = Application.Current as App ?? new App(startWorkspace: false);
            app.InitializeComponent();
            var coverConverter = new CoverImageConverter();
            Assert.Null(coverConverter.Convert("https://external.example.invalid/cover.jpg", typeof(BitmapImage), "80", System.Globalization.CultureInfo.InvariantCulture));
            Assert.Null(coverConverter.Convert(@"\\external.example.invalid\share\cover.jpg", typeof(BitmapImage), "80", System.Globalization.CultureInfo.InvariantCulture));
            var root = Path.Combine(Path.GetTempPath(), "readest-v24-ui-" + Guid.NewGuid().ToString("N"));
            var dataRoot = Path.Combine(root, "ReadestStats"); Directory.CreateDirectory(dataRoot);
            var now = DateTimeOffset.Now.AddHours(-2);
            var data = new ManualReadingData
            {
                Books = [new() { Id = -1, Title = "Bitter Harvest", Authors = "Ann Rule", Isbn13 = "123", TotalPages = 574 }, new() { Id = -2, Title = "Bitter Harvest", Authors = "Ann Rule", Isbn13 = "123", TotalPages = 574, CompletedAtUtc = now }],
                Sessions = [new() { Id = "s1", BookId = -1, StartedAtUtc = now, EndedAtUtc = now.AddMinutes(30), DurationSeconds = 1800, StartPage = 1, EndPage = 30, Note = "Keep this user note in English." }, new() { Id = "s2", BookId = -2, StartedAtUtc = now.AddDays(-40), EndedAtUtc = now.AddDays(-40).AddMinutes(10), DurationSeconds = 600, StartPage = 1, EndPage = 10 }]
            };
            await new ManualReadingStore(Path.Combine(dataRoot, "manual-reading.json")).SaveAsync(data);
            await new SettingsStore(Path.Combine(dataRoot, "settings.json")).SaveAsync(new() { AutomaticBackups = false, Language = "English", PinnedBookKeys = ["manual:-2"], BookTracking = new() { ["manual:-2"] = new() { Status = "Reading", Plan = new() { Enabled = true, DailyMinutes = 20 } } } });
            using var viewModel = new MainViewModel(() => null, (_, _) => null, dataDirectory: root, confirmMerge: _ => true);
            _stage = "initialize";
            await viewModel.InitializeAsync();
            Assert.False(viewModel.IsConnected);
            Assert.Equal(1800, viewModel.Period!.TotalSeconds);
            Assert.Contains("Physical", viewModel.ManualSourceStatus);
            Assert.NotNull(viewModel.WeeklyRecap);
            _stage = "edition draft";
            var edition = new BookMetadataResult("Open Library", "edition-1", "Provider edition", "Author", "Publisher", "2026", 220, null, "9781234567890", "en", null, null);
            viewModel.Manual.AddBookCommand.Execute(null);
            viewModel.Manual.SelectedSearchResult = edition;
            viewModel.Manual.SelectedSearchResult = edition with { InfoUrl = "ms-settings:privacy" };
            Assert.False(viewModel.Manual.OpenMetadataCommand.CanExecute(null));
            viewModel.Manual.SelectedSearchResult = edition;
            viewModel.Manual.DraftTitle = "My edited title";
            viewModel.Manual.SelectedSearchResult = edition with { ExternalId = "edition-2", Title = "Another edition", Publisher = "New publisher" };
            Assert.Equal("My edited title", viewModel.Manual.DraftTitle);
            Assert.Equal("New publisher", viewModel.Manual.DraftPublisher);
            viewModel.Manual.CancelAddBookCommand.Execute(null);
            viewModel.Manual.AddBookCommand.Execute(null);
            viewModel.Manual.SelectedSearchResult = edition;
            Assert.Equal("Provider edition", viewModel.Manual.DraftTitle);
            viewModel.Manual.CancelAddBookCommand.Execute(null);
            _stage = "construct pages";
            FrameworkElement[] views =
            [
                new OverviewView(), new ActivityView(), new SessionsView(), new BooksView(),
                new NotesView(), new GoalsView(), new StatisticsView(), new YearView(), new SettingsView()
            ];
            foreach (var view in views)
            {
                view.DataContext = viewModel;
                Layout(view);
            }
            var manual = new ManualLogView { DataContext = viewModel.Manual };
            Layout(manual);
            var addBook = new AddBookWindow { DataContext = viewModel.Manual };
            Layout(addBook);
            var sessionEditor = new SessionEditorWindow(viewModel.Manual);
            Layout(sessionEditor);
            var importPreview = new ImportPreviewWindow { DataContext = new ReadestStats.Core.CatalogImportPreview("Goodreads", 1, 1, 0, [new("Book", "Author", null, 100, null)]) };
            Layout(importPreview);
            var window = new MainWindow { DataContext = viewModel };
            _stage = "navigate pages";
            Assert.Equal(1, window.LoadedPageCount);
            foreach (var page in viewModel.Pages)
            {
                viewModel.SelectedPage = page;
                Layout(window);
            }
            Assert.Equal(viewModel.Pages.Length, window.LoadedPageCount);
            viewModel.SelectedPage = "Today"; Assert.Equal(viewModel.Pages.Length, window.LoadedPageCount);

            _stage = "archive";
            viewModel.SelectedSource = "Manual"; viewModel.SelectedRange = "All time";
            var total = viewModel.Period!.TotalSeconds; var finished = viewModel.FinishedAllTime;
            viewModel.Manual.SelectedBook = viewModel.Manual.Books.Single(b => b.Id == -2);
            await InvokeAsync(viewModel.Manual, "ArchiveSelectedBookAsync");
            Assert.Equal(total, viewModel.Period!.TotalSeconds); Assert.Equal(finished, viewModel.FinishedAllTime);
            Assert.DoesNotContain(viewModel.Manual.Books, b => b.Id == -2); Assert.Single(viewModel.Manual.ArchivedBooks);
            await InvokeAsync(viewModel.Manual, "RestoreArchivedBookAsync");
            Assert.Equal(2, viewModel.Manual.Books.Count);
            _stage = "merge";
            viewModel.Manual.MergePrimary = viewModel.Manual.Books.Single(b => b.Id == -1);
            await InvokeAsync(viewModel.Manual, "MergeDuplicateBooksAsync");
            Assert.Single(viewModel.Manual.Books); Assert.Equal(2, viewModel.Manual.Sessions.Count); Assert.All(viewModel.Manual.Sessions, s => Assert.Equal(-1, s.BookId));
            var mergedSettings = await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync();
            Assert.Contains("manual:-1", mergedSettings.PinnedBookKeys); Assert.True(mergedSettings.BookTracking["manual:-1"].Plan!.Enabled);
            await InvokeAsync(viewModel.Manual, "UndoRecentActionAsync");
            Assert.Equal(2, viewModel.Manual.Books.Count); Assert.Equal(total, viewModel.Period!.TotalSeconds);
            var undoneSettings = await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync();
            Assert.Contains("manual:-2", undoneSettings.PinnedBookKeys); Assert.True(undoneSettings.BookTracking.ContainsKey("manual:-2"));

            _stage = "comparison";
            viewModel.SelectedRange = "30 days"; viewModel.CompareMode = "Custom";
            viewModel.CompareCustomStart = now.AddDays(-40).Date; viewModel.CompareCustomEnd = now.AddDays(-40).Date;
            Assert.Equal(600, viewModel.Comparison!.PreviousValue); Assert.Equal(1800, viewModel.Comparison.CurrentValue);
            Assert.Empty(viewModel.ComparisonTrend); Assert.Single(viewModel.ComparisonData); Assert.NotEmpty(viewModel.PeriodDumbbells);
            viewModel.CompareMode = "Off"; Assert.Null(viewModel.Comparison); Assert.Empty(viewModel.PeriodDumbbells);
            viewModel.CompareMode = "Previous period"; viewModel.TrendGranularity = "Month";
            Assert.All(viewModel.Trend.Concat(viewModel.ComparisonData), p => Assert.Equal("h", p.Unit));

            _stage = "restore workspace";
            await InvokeAsync(viewModel, "SaveSettingsAsync");
            var backupPath = Path.Combine(root, "workspace.zip");
            new AppDataBackupService(Path.Combine(dataRoot, "settings.json"), Path.Combine(dataRoot, "manual-reading.json")).Create(backupPath, "2.3.0");
            viewModel.SelectedRange = "Today";
            var settingsStore = (SettingsStore)typeof(MainViewModel).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
            var writer = (SemaphoreSlim)typeof(SettingsStore).GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsStore)!;
            await writer.WaitAsync();
            var queuedSave = InvokeAsync(viewModel, "SaveSettingsDataAsync", CancellationToken.None);
            Task restoring;
            try
            {
                restoring = InvokeAsync(viewModel, "RestoreAppBackupAsync", backupPath);
                Assert.False(restoring.IsCompleted); Assert.True(viewModel.IsRestoring); Assert.False(viewModel.CanInteract);
                await InvokeAsync(viewModel, "RefreshAsync", false);
            }
            finally { writer.Release(); }
            await restoring; await queuedSave;
            Assert.False(viewModel.IsRestoring); Assert.True(viewModel.CanInteract);
            Assert.Equal("30 days", (await settingsStore.LoadAsync()).DefaultRangePreset);
            Assert.Equal("30 days", viewModel.SelectedRange);
            Assert.Equal(2, viewModel.Manual.Books.Count);
            Assert.Equal(1800, viewModel.Period!.TotalSeconds);

            _stage = "localization";
            viewModel.Language = "Tiếng Việt";
            Assert.Equal("Lưu sách", Localizer.Instance.Translate("Save book"));
            Assert.Equal("Trang kết thúc không được vượt quá 574.", Localizer.Instance.Translate("End page cannot exceed 574."));
            Assert.Equal("Ghi chú giữ nguyên", Localizer.Instance.Translate("Ghi chú giữ nguyên"));
            Assert.Equal("Bitter Harvest", viewModel.Manual.Books[0].Title);
            foreach (var page in viewModel.Pages) { viewModel.SelectedPage = page; Layout(window); }
            Assert.Equal("Cài đặt", viewModel.PageTitle);
            viewModel.SelectedPage = "Manual log"; Layout(window);
            if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output); ThemeManager.Apply("Light"); Layout(window);
                Render(window, Path.Combine(output, "manual-vi-light.png"));
                viewModel.SelectedPage = "Statistics"; Layout(window); Render(window, Path.Combine(output, "statistics-vi-light.png"));
                viewModel.SelectedPage = "Today"; Layout(window); Render(window, Path.Combine(output, "today-vi-light.png"));
                Layout(window, 700, 450); Render(window, Path.Combine(output, "today-vi-scaled.png"));
                viewModel.SelectedPage = "Settings"; Layout(window, 700, 450); Render(window, Path.Combine(output, "settings-vi-scaled.png"));
                viewModel.SelectedPage = "Statistics"; Layout(window);
                ThemeManager.Apply("Dark"); Layout(window); Render(window, Path.Combine(output, "statistics-vi-dark.png"));
            }
            _stage = "virtualization";
            viewModel.Language = "English"; Assert.Equal("Save book", Localizer.Instance.Translate("Save book"));
            viewModel.SelectedPage = "Books"; viewModel.BookFilter = "All";
            var sample = viewModel.Books.First();
            for (var i = 0; i < 2000; i++) viewModel.Books.Add(sample);
            Layout(window);
            var list = Descendants(window).OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, viewModel.Books));
            Assert.InRange(Descendants(list).OfType<ListBoxItem>().Count(), 1, 99);
            _stage = "Readest audit integration";
            var databasePath = Path.Combine(dataRoot, "statistics.db");
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"CREATE TABLE book(id INTEGER PRIMARY KEY,title TEXT,authors TEXT,last_open INTEGER,pages INTEGER,series TEXT,language TEXT); CREATE TABLE page_stat_data(id_book INTEGER,page INTEGER,start_time INTEGER,duration REAL,total_pages INTEGER); INSERT INTO book VALUES(1,'Digital fixture','Test author',NULL,100,NULL,'en'); INSERT INTO page_stat_data VALUES(1,5,{DateTimeOffset.Now.AddMinutes(-20).ToUnixTimeSeconds()},600,100);";
                command.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            var databaseHash = SHA256.HashData(await File.ReadAllBytesAsync(databasePath));
            await InvokeAsync(viewModel, "ConnectAsync", databasePath);
            Assert.True(viewModel.IsConnected);
            viewModel.SelectedSource = "All sources"; viewModel.SelectedRange = "30 days";
            Assert.Equal(2400, viewModel.Period!.TotalSeconds);
            viewModel.SelectedAuditEvent = Assert.Single(viewModel.AuditEvents);
            await InvokeAsync(viewModel, "ToggleAuditEventAsync");
            Assert.Equal(1800, viewModel.Period!.TotalSeconds);
            Assert.Single((await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync()).ReadingEventExclusions);
            await InvokeAsync(viewModel, "ToggleAuditEventAsync");
            Assert.Equal(2400, viewModel.Period!.TotalSeconds);
            Assert.Empty((await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync()).ReadingEventExclusions);
            SqliteConnection.ClearAllPools();
            Assert.Equal(databaseHash, SHA256.HashData(await File.ReadAllBytesAsync(databasePath)));
            _stage = "full refresh reconciles old Readest changes";
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE page_stat_data SET duration=300";
                command.ExecuteNonQuery();
            }
            await InvokeAsync(viewModel, "RefreshAsync", false);
            Assert.Equal(2100, viewModel.Period!.TotalSeconds);
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM page_stat_data";
                command.ExecuteNonQuery();
            }
            await InvokeAsync(viewModel, "RefreshAsync", false);
            Assert.Equal(1800, viewModel.Period!.TotalSeconds);
            Assert.Empty(viewModel.AuditEvents);
            await VerifyV25WorkflowsAsync(root);
            viewModel.Dispose(); window.DataContext = null;
            try { Directory.Delete(root, true); } catch { }
        });
    }

    private static async Task VerifyV25WorkflowsAsync(string parentRoot)
    {
        _stage = "v2.5 workflow fixtures";
        var root = Path.Combine(parentRoot, "v25");
        var dataRoot = Path.Combine(root, "ReadestStats"); Directory.CreateDirectory(dataRoot);
        var now = DateTimeOffset.UtcNow.AddHours(-3);
        var data = new ManualReadingData
        {
            Books = [new() { Id = -1, Title = "Physical fixture", Authors = "Test author", TotalPages = 100 }],
            Sessions = Enumerable.Range(0, 5).Select(i => new ManualReadingSession { Id = $"n{i}", BookId = -1, StartedAtUtc = now.AddDays(-i), EndedAtUtc = now.AddDays(-i).AddMinutes(5), DurationSeconds = 300, Note = $"Private note {i}" }).ToList()
        };
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        await new ManualReadingStore(Path.Combine(dataRoot, "manual-reading.json")).SaveAsync(data);
        await new SettingsStore(settingsPath).SaveAsync(new()
        {
            AutomaticBackups = false, Language = "English", NoteStates = new()
            {
                ["manual:n3"] = new() { ExcludeFromRandom = true },
                ["manual:n4"] = new() { SnoozedUntilUtc = DateTimeOffset.UtcNow.AddDays(1) }
            }
        });
        var vm = new MainViewModel(() => null, (_, _) => null, dataDirectory: root);
        await vm.InitializeAsync();
        Assert.NotNull(vm.DailyNote);
        Assert.Equal(3, vm.RandomEligibleCount);
        _stage = "v2.5 random workflow";
        var ids = new HashSet<string> { vm.RandomNote!.Id };
        for (var i = 0; i < 2; i++) { vm.RandomNoteCommand.Execute(null); Assert.True(ids.Add(vm.RandomNote!.Id)); }
        var cycleLast = vm.RandomNote!.Id;
        vm.RandomNoteCommand.Execute(null); Assert.NotEqual(cycleLast, vm.RandomNote!.Id);
        Assert.All(ids, id => Assert.DoesNotContain(id, new[] { "manual:n3", "manual:n4" }));
        var seen = vm.Notes.ToDictionary(note => note.Id, note => note.TimesSeen);
        vm.PreviousRandomNoteCommand.Execute(null);
        Assert.All(vm.Notes, note => Assert.Equal(seen[note.Id], note.TimesSeen));
        vm.OpenRediscoveryHistoryCommand.Execute(vm.RandomNoteHistory.Last());
        Assert.All(vm.Notes, note => Assert.Equal(seen[note.Id], note.TimesSeen));
        vm.RandomNotePool = "Physical books";
        vm.RandomNoteCommand.Execute(null);
        var randomId = vm.RandomNote!.Id;
        await vm.FlushPreferencesAsync();
        vm.Dispose();
        vm = new MainViewModel(() => null, (_, _) => null, dataDirectory: root);
        await vm.InitializeAsync();
        Assert.Equal("Physical books", vm.RandomNotePool);
        Assert.Equal(randomId, vm.RandomNote!.Id);
        Assert.NotEmpty(vm.RandomNoteHistory);
        vm.RandomNotePool = "Favorites";
        Assert.Null(vm.RandomNote); Assert.False(vm.RandomNoteCommand.CanExecute(null));
        Assert.Contains("No eligible", vm.RandomPoolStatus);
        await VerifyV25EnhancementsAsync(vm, root);

        _stage = "v2.5 private exports and backup preview";
        var options = new ExportOptions(true, 2, 5);
        Assert.False(options.IncludePrivate); Assert.False(options.ArchiveMode);
        var exportWindow = new ExportOptionsWindow(options); Layout(exportWindow, 584, 431);
        Assert.NotNull(exportWindow.Style); Assert.NotNull(((Border)exportWindow.Content).Background);
        options.ArchiveMode = true; Assert.False(options.IsSharing); Layout(exportWindow, 584, 431);
        var backupPath = Path.Combine(root, "preview.zip");
        var backup = new AppDataBackupService(settingsPath, Path.Combine(dataRoot, "manual-reading.json"));
        backup.Create(backupPath, "2.5.0");
        var previewWindow = new BackupPreviewWindow(backup.Preview(backupPath)); Layout(previewWindow, 824, 611);
        Assert.NotNull(previewWindow.Style); Assert.NotNull(((Border)previewWindow.Content).Background);
        vm.ReportMonth = now.LocalDateTime;
        var fullMonthSeconds = vm.MonthlyReview!.TotalSeconds;
        vm.SelectedRange = "Today";
        Assert.Equal(fullMonthSeconds, vm.MonthlyReview.TotalSeconds);
        vm.SelectedSource = "Readest"; Assert.Equal(0, vm.MonthlyReview.TotalSeconds);
        vm.SelectedSource = "Manual"; Assert.Equal(fullMonthSeconds, vm.MonthlyReview.TotalSeconds);
        var monthlyWindow = new MainWindow { DataContext = vm };
        vm.SelectedPage = "Statistics"; Layout(monthlyWindow);
        var monthlyDetails = Descendants(monthlyWindow).OfType<Expander>().Single(expander =>
            System.Windows.Data.BindingOperations.GetBindingExpression(expander, HeaderedContentControl.HeaderProperty)?.ParentBinding.Path.Path == nameof(MainViewModel.MonthlyReviewExactLabel));
        monthlyDetails.IsExpanded = true; Layout(monthlyWindow);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle); Layout(monthlyWindow);
        var monthlyGrid = Descendants(monthlyWindow).OfType<DataGrid>().Single(grid => ReferenceEquals(grid.ItemsSource, vm.MonthlyReviewDaily));
        Assert.All(monthlyGrid.Columns, column => Assert.True(column.ActualWidth >= 60));
        Assert.All(monthlyGrid.Columns, column => Assert.False(string.IsNullOrWhiteSpace(column.Header?.ToString())));
        Assert.All(vm.MonthlyReviewDaily, day => Assert.True(day.Date <= DateOnly.FromDateTime(DateTime.Today)));
        if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } exactOutput)
        {
            ThemeManager.Apply("Light"); Layout(monthlyWindow);
            Render(monthlyWindow, Path.Combine(exactOutput, "v25-monthly-exact-light.png"));
        }

        _stage = "v2.5 finish cancel resume and durable draft";
        vm.Manual.SelectedBook = vm.Manual.Books.Single(); vm.Manual.TargetMinutes = "1";
        _stage = "v2.5 start timer";
        await InvokeAsync(vm.Manual, "StartSessionAsync");
        Assert.True(vm.Manual.HasFocusTarget);
        await Task.Delay(1150);
        vm.Manual.BeginFinishCommand.Execute(null);
        Assert.True(vm.Manual.IsFinishing); Assert.True(vm.Manual.IsSessionPaused);
        vm.Manual.FinishStartPage = "1"; vm.Manual.FinishEndPage = "12"; vm.Manual.FinishNote = "Unsaved keyboard draft";
        vm.Manual.CancelFinishCommand.Execute(null);
        _stage = "v2.5 resume timer";
        var ticks = 0;
        vm.Manual.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ManualLogViewModel.ElapsedLabel)) ticks++; };
        await InvokeAsync(vm.Manual, "PauseResumeAsync");
        ticks = 0; await Task.Delay(1250); Assert.True(ticks > 0, "Resumed timer must publish dispatcher ticks");
        vm.Manual.BeginFinishCommand.Execute(null);
        Assert.Equal("12", vm.Manual.FinishEndPage); Assert.Equal("Unsaved keyboard draft", vm.Manual.FinishNote);
        _stage = "v2.5 flush active finish draft";
        var settingsWrites = (HashSet<Task>)typeof(MainViewModel).GetField("_settingsWrites", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        settingsWrites.Add(Task.FromException(new IOException("Synthetic settings write failure")));
        await Assert.ThrowsAsync<IOException>(vm.FlushPreferencesAsync);
        var manualWrites = (HashSet<Task>)typeof(ManualLogViewModel).GetField("_pendingStorageTasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm.Manual)!;
        manualWrites.Add(Task.FromException(new IOException("Synthetic manual write failure")));
        await Assert.ThrowsAsync<IOException>(vm.FlushPreferencesAsync);
        // Both observed errors must allow a later close to retry the preserved
        // current draft, rather than rethrowing a cached failed task forever.
        await vm.FlushPreferencesAsync();
        _stage = "v2.5 dispose active finish draft";
        vm.Dispose();
        _stage = "v2.5 reload active finish draft";
        vm = new MainViewModel(() => null, (_, _) => null, dataDirectory: root);
        await vm.InitializeAsync();
        Assert.True(vm.Manual.IsFinishing); Assert.True(vm.Manual.IsSessionPaused);
        Assert.Equal("12", vm.Manual.FinishEndPage); Assert.Equal("Unsaved keyboard draft", vm.Manual.FinishNote);
        Assert.True(vm.Manual.HasFocusTarget);
        Assert.True(vm.Manual.SaveFinishedSessionCommand.CanExecute(null));
        _stage = "v2.5 save recovered draft";
        await InvokeAsync(vm.Manual, "SaveFinishedSessionAsync");
        Assert.False(vm.Manual.HasActiveSession);
        var saved = vm.Manual.Sessions.Single(session => session.Note == "Unsaved keyboard draft");
        Assert.Equal(12, saved.EndPage);
        vm.Manual.JournalSearch = "keyboard"; Assert.Single(vm.Manual.RecentSessions);
        vm.GlobalSearch = "keyboard"; Assert.Contains(vm.GlobalSearchResults, result => result.Kind == "SESSION" && result.Key == saved.Id);
        if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } output)
        {
            vm.Manual.JournalSearch = "";
            vm.SelectedRange = "30 days";
            var window = new MainWindow { DataContext = vm };
            vm.SelectedPage = "Statistics"; ThemeManager.Apply("Light"); Layout(window); Render(window, Path.Combine(output, "v25-monthly-light.png"));
            ThemeManager.Apply("Dark"); Layout(window); Render(window, Path.Combine(output, "v25-monthly-dark.png"));
            vm.SelectedPage = "Manual log"; Layout(window);
            vm.Manual.SelectedJournalSession = vm.Manual.RecentSessions.Single(row => row.Id == saved.Id);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle); Layout(window);
            var journalGrid = Descendants(window).OfType<DataGrid>().Single(grid => ReferenceEquals(grid.ItemsSource, vm.Manual.RecentSessions));
            Assert.All(journalGrid.Columns.Take(4), column => Assert.True(column.ActualWidth >= 60, $"Journal column is clipped: {column.Header}, {column.ActualWidth}px"));
            Assert.All(journalGrid.Columns.Take(4), column => Assert.False(string.IsNullOrWhiteSpace(column.Header?.ToString()), "Journal headers must resolve before display"));
            Render(window, Path.Combine(output, "v25-journal-dark.png"));
            ThemeManager.Apply("Light"); Layout(window); Render(window, Path.Combine(output, "v25-journal-light.png"));
            var manualScroll = Descendants(window).OfType<ManualLogView>().SelectMany(Descendants).OfType<ScrollViewer>().First();
            manualScroll.ScrollToVerticalOffset(1050); Layout(window); Render(window, Path.Combine(output, "v25-journal-detail-light.png"));
            ThemeManager.Apply("Dark"); Layout(window); Render(window, Path.Combine(output, "v25-journal-detail-dark.png"));
            ThemeManager.Apply("Light");
            Layout(previewWindow, 824, 611); Render(previewWindow, Path.Combine(output, "v25-backup-preview.png"));
            options.ArchiveMode = false; Layout(exportWindow, 584, 431); Render(exportWindow, Path.Combine(output, "v25-export-options.png"));
            ThemeManager.Apply("Dark");
            Layout(previewWindow, 824, 611); Render(previewWindow, Path.Combine(output, "v25-backup-preview-dark.png"));
            Layout(exportWindow, 584, 431); Render(exportWindow, Path.Combine(output, "v25-export-options-dark.png"));
        }
        await vm.FlushPreferencesAsync(); vm.Dispose();
    }

    private static async Task VerifyV25EnhancementsAsync(MainViewModel vm, string root)
    {
        _stage = "v2.5 focused note reader";
        vm.RandomNotePool = "All eligible notes";
        var note = vm.Notes.Single(row => row.Id == "manual:n0");
        vm.SelectedNote = note;
        Assert.Equal("Private note 0", note.PreviewContent);
        Assert.False(note.HasSeparateComment);
        Assert.DoesNotContain("Private note", NoteReaderContent.QuoteWithCitation(note.Source));
        Assert.Contains("Private note 0", NoteReaderContent.PrivateWriting(note.Note, note.PersonalNote));
        var reader = new NoteReaderWindow(vm);
        Assert.DoesNotContain("No eligible", vm.NotesStatus);
        Assert.NotNull(reader.Style); Assert.NotNull(((Border)reader.Content).Background);
        var originalNote = note.Note;
        note.Note = string.Join("\n\n", Enumerable.Repeat("Sách giấy cũng có một nơi để lưu lại những điều đáng nhớ. Đọc chậm, ghi lại một ý hay, rồi quay lại khi cần. This is synthetic reading-journal text for checking the reader's line length, wrapping and Unicode support—not private reading history.", 5));
        foreach (var theme in new[] { "Light", "Dark" })
        {
            ThemeManager.Apply(theme); Layout(reader, 820, 760);
            var content = (TextBox)reader.FindName("SourceContent");
            Assert.Equal(note.PreviewContent, content.Text); Assert.True(content.IsReadOnly);
            Assert.Equal(System.Windows.Data.BindingMode.OneWay,
                System.Windows.Data.BindingOperations.GetBinding(content, TextBox.TextProperty)!.Mode);
            Assert.InRange(content.ActualWidth, 100, 620);
            if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } output)
                Render(reader, Path.Combine(output, $"v25-note-reader-{theme.ToLowerInvariant()}.png"));
        }
        Layout(reader, 540, 440);
        Assert.True(((ScrollViewer)reader.FindName("ReaderScroll")).ViewportWidth > 0);
        note.Note = originalNote;
        reader.Close(); ThemeManager.Apply("Light");

        _stage = "v2.5 curation pending-save selection and exact-ID undo";
        var store = (SettingsStore)typeof(MainViewModel).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var writer = (SemaphoreSlim)typeof(SettingsStore).GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        await writer.WaitAsync();
        Task snoozing;
        string changedSelection;
        try
        {
            snoozing = InvokeAsync(vm, "SnoozeSelectedNoteAsync", 7);
            Assert.True(vm.IsNoteCurationBusy); Assert.False(vm.UndoNoteCurationCommand.CanExecute(null));
            vm.RandomNoteCommand.Execute(null);
            changedSelection = vm.RandomNote!.Id;
            Assert.NotEqual(note.Id, changedSelection);
        }
        finally { writer.Release(); }
        await snoozing;
        Assert.False(vm.IsNoteCurationBusy); Assert.Equal(changedSelection, vm.RandomNote!.Id);
        Assert.True(note.IsSnoozed); Assert.True(vm.HasNoteCurationUndo);
        var seen = vm.Notes.ToDictionary(row => row.Id, row => row.TimesSeen);
        await InvokeAsync(vm, "UndoNoteCurationAsync");
        Assert.False(note.IsSnoozed); Assert.False(vm.HasNoteCurationUndo);
        Assert.All(vm.Notes, row => Assert.Equal(seen[row.Id], row.TimesSeen));
        Assert.Null(vm.Notes.Single(row => row.Id == changedSelection).State.SnoozedUntilUtc);
        var persisted = await store.LoadAsync(); Assert.Null(persisted.NoteStates[note.Id].SnoozedUntilUtc);
        vm.NoteScope = "Snoozed"; Assert.Equal("manual:n4", Assert.Single(vm.Notes).Id);
        vm.NoteScope = "Excluded from random"; Assert.Equal("manual:n3", Assert.Single(vm.Notes).Id);
        vm.NoteScope = "All notes";

        _stage = "v2.5 elapsed clock boundary with already-rebuilt pool";
        var snoozed = vm.Notes.Single(row => row.Id == "manual:n4");
        var originalSnooze = snoozed.State.SnoozedUntilUtc;
        var clockNow = DateTimeOffset.UtcNow;
        snoozed.State.SnoozedUntilUtc = clockNow.AddSeconds(-1);
        typeof(MainViewModel).GetField("_lastObservedNoteClockUtc", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, clockNow.AddSeconds(-2));
        ((System.Collections.IDictionary)typeof(MainViewModel).GetField("_rediscoveryPoolCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!).Clear();
        Assert.Equal(4, vm.RandomEligibleCount); // Query replaces the old cache deadline before the timer.
        var notifications = 0;
        snoozed.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(NoteRow.SnoozeLabel)) notifications++; };
        var history = vm.RandomNoteHistory.Select(row => row.Id).ToArray();
        var randomId = vm.RandomNote!.Id;
        seen = vm.Notes.ToDictionary(row => row.Id, row => row.TimesSeen);
        var lastSync = vm.LastSync;
        typeof(MainViewModel).GetMethod("RefreshLocalTimeStateAt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [clockNow]);
        Assert.True(notifications > 0); Assert.Equal(randomId, vm.RandomNote!.Id);
        Assert.Equal(history, vm.RandomNoteHistory.Select(row => row.Id)); Assert.Equal(lastSync, vm.LastSync);
        Assert.All(vm.Notes, row => Assert.Equal(seen[row.Id], row.TimesSeen));
        snoozed.RestoreRandomCuration(originalSnooze, snoozed.ExcludeFromRandom);

        _stage = "v2.5 narrowed browse scope must not erase random selection";
        var notesView = new NotesView { DataContext = vm };
        vm.NoteScope = "Snoozed"; Layout(notesView);
        vm.RandomNoteCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle); Layout(notesView);
        Assert.NotNull(vm.SelectedNote); Assert.Same(vm.RandomNote, vm.SelectedNote);
        Assert.NotEqual("manual:n4", vm.SelectedNote!.Id);
        var list = Descendants(notesView).OfType<ListBox>().Single(box => ReferenceEquals(box.ItemsSource, vm.Notes));
        var visible = vm.Notes.Single(); var previousSeen = visible.TimesSeen;
        list.SelectedItem = visible;
        Assert.Same(visible, vm.SelectedNote); Assert.Equal(previousSeen + 1, visible.TimesSeen);
        vm.SelectedNote = visible; Assert.Equal(previousSeen + 1, visible.TimesSeen);
        vm.NoteScope = "All notes";

        _stage = "v2.5 effective export counts";
        vm.NoteSearch = "no-such-content"; Assert.Empty(vm.Notes);
        Assert.True(vm.ExportNotesMarkdownCommand.CanExecute(null));
        var options = new ExportOptions(false, 0, 5, selection => ExportScopeCounter.Notes(
            (selection.AllItems ? vm.Manual.AsNotes().Select(source => new NoteExportScopeItem(true, source.HasHighlight))
                : vm.Notes.Select(row => new NoteExportScopeItem(row.IsManual, row.HasHighlight))), selection.IncludePrivate));
        Assert.False(options.CanExport);
        options.AllItems = true; Assert.False(options.CanExport); Assert.Equal(5, options.Counts.Omitted);
        options.IncludePrivate = true; Assert.True(options.CanExport); Assert.Equal(5, options.Counts.Notes);
        options.AllItems = false; Assert.False(options.CanExport);
        vm.NoteSearch = ""; options.RefreshCounts(); Assert.True(options.CanExport);
        Assert.Equal(5, options.Counts.Notes);
        var unlogged = new ManualBook { Id = -40, Title = "Unlogged fixture" };
        var physical = new ExportOptions(true, 0, 0, selection => ExportScopeCounter.Physical(
            new[] { unlogged }, Array.Empty<ManualReadingSession>(), selection.AllItems || selection.ArchiveMode));
        Assert.False(physical.CanExport); physical.AllItems = true;
        Assert.True(physical.CanExport); Assert.Equal(1, physical.Counts.Books); Assert.Equal(0, physical.Counts.Sessions);

        _stage = "v2.5 month navigation and day drilldown";
        vm.SelectedSource = "Manual"; var range = vm.SelectedRange;
        vm.ReportMonth = new DateTime(2025, 1, 1);
        vm.PreviousReviewMonthCommand.Execute(null); Assert.Equal(new DateTime(2024, 12, 1), vm.ReportMonth);
        vm.NextReviewMonthCommand.Execute(null); Assert.Equal(new DateTime(2025, 1, 1), vm.ReportMonth);
        vm.CurrentReviewMonthCommand.Execute(null); Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), vm.ReportMonth);
        Assert.False(vm.NextReviewMonthCommand.CanExecute(null));
        vm.ReportMonth = note.SortDate.LocalDateTime;
        var day = vm.MonthlyReviewDaily.Single(row => row.Date == DateOnly.FromDateTime(note.SortDate.LocalDateTime));
        vm.OpenMonthlyDayCommand.Execute(day);
        Assert.Equal("Activity", vm.SelectedPage); Assert.Equal(day.Date, vm.SelectedDate);
        Assert.Equal("Manual", vm.SelectedSource); Assert.Equal(range, vm.SelectedRange);
        Assert.Equal(day.Seconds, vm.SelectedDay!.Seconds);
        Assert.DoesNotContain("Readest:", vm.MonthlyReviewCoverage, StringComparison.OrdinalIgnoreCase);
        vm.SelectedSource = "Readest";
        Assert.Contains("unavailable", vm.MonthlyReviewCoverage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable", MonthlyReviewEngine.ToMarkdown(vm.MonthlyReview!), StringComparison.OrdinalIgnoreCase);
        vm.SelectedSource = "Manual";
        VerifyV25BarLabels();

        _stage = "v2.5 journal selection, date presets and navigation";
        vm.Manual.JournalSearch = "Private note";
        vm.Manual.SelectedJournalBook = vm.Manual.JournalBooks.Single(option => option.BookId == -1);
        foreach (var preset in vm.Manual.JournalDatePresets.Where(item => item.Key != "custom"))
        {
            vm.Manual.SelectedJournalPreset = preset;
            Assert.Equal("Private note", vm.Manual.JournalSearch); Assert.Equal(-1, vm.Manual.SelectedJournalBook!.BookId);
            Assert.Equal(range, vm.SelectedRange);
            if (preset.Key == "today")
            {
                Assert.Equal(DateTime.Today, vm.Manual.JournalFrom); Assert.Equal(DateTime.Today, vm.Manual.JournalThrough);
            }
            if (preset.Key == "seven") Assert.Equal(DateTime.Today.AddDays(-6), vm.Manual.JournalFrom);
            if (preset.Key == "month") Assert.Equal(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), vm.Manual.JournalFrom);
        }
        vm.Manual.SelectedJournalPreset = vm.Manual.JournalDatePresets.Single(item => item.Key == "all");
        vm.Manual.SelectedJournalPreset = vm.Manual.JournalDatePresets.Single(item => item.Key == "today");
        vm.Manual.RefreshJournalDatePreset(DateTime.Today.AddDays(1));
        Assert.Equal(DateTime.Today.AddDays(1), vm.Manual.JournalFrom);
        Assert.Equal("Private note", vm.Manual.JournalSearch); Assert.Equal(-1, vm.Manual.SelectedJournalBook!.BookId);
        vm.Manual.RefreshJournalDatePreset(DateTime.Today);
        vm.Manual.JournalFrom = DateTime.Today.AddDays(-9);
        Assert.Equal("custom", vm.Manual.SelectedJournalPreset!.Key);
        vm.Manual.RefreshJournalDatePreset(DateTime.Today.AddDays(1));
        Assert.Equal(DateTime.Today.AddDays(-9), vm.Manual.JournalFrom);
        vm.Manual.SelectedJournalPreset = vm.Manual.JournalDatePresets.Single(item => item.Key == "all");
        vm.Manual.SelectedJournalSession = vm.Manual.RecentSessions.Single(row => row.Id == "n0");
        Assert.Equal("Private note 0", vm.Manual.SelectedSessionNote); Assert.True(vm.Manual.HasSelectedJournalSession);
        vm.Manual.OpenJournalBookCommand.Execute(null);
        Assert.Equal("Books", vm.SelectedPage); Assert.Equal(-1, vm.SelectedBook!.Id);
        vm.Manual.OpenJournalDayCommand.Execute(null);
        Assert.Equal("Activity", vm.SelectedPage);
        Assert.Equal(DateOnly.FromDateTime(vm.Manual.FindSession("n0")!.StartedAtUtc.LocalDateTime), vm.SelectedDate);
        Assert.Equal(range, vm.SelectedRange);
        vm.Manual.JournalSearch = ""; vm.Manual.SelectedJournalBook = vm.Manual.JournalBooks.First();
        await VerifyV25SessionCorrectionAsync(root);
        await VerifyV25ExportSnapshotAsync(vm.Manual.CapturePortableLibrary(), root);
    }

    private static async Task VerifyV25ExportSnapshotAsync(ManualReadingData data, string parentRoot)
    {
        _stage = "v2.5 live export preview and immutable confirmed scope";
        var root = Path.Combine(parentRoot, "export-snapshot");
        var dataRoot = Path.Combine(root, "ReadestStats"); Directory.CreateDirectory(dataRoot);
        data.Sessions.Single(session => session.Id == "n0").Note = "Private writing for unique-export-fixture";
        await new ManualReadingStore(Path.Combine(dataRoot, "manual-reading.json")).SaveAsync(data);
        await new SettingsStore(Path.Combine(dataRoot, "settings.json")).SaveAsync(new() { AutomaticBackups = false, Language = "English" });
        var path = Path.Combine(root, "shared.json");
        MainViewModel? exportVm = null;
        using var vm = exportVm = new MainViewModel(() => null, (_, _) =>
        {
            // A native file picker can dispatch updates. The confirmed content must already be detached.
            exportVm!.Manual.FindBook(-1)!.Title = "Changed after scope capture";
            exportVm.Manual.FindSession("n0")!.Note = "Changed after scope capture";
            return path;
        }, dataDirectory: root);
        await vm.InitializeAsync();
        Exception? dialogError = null;
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ExportOptionsWindow>().Single(window => window.IsVisible);
            try
            {
                var options = (ExportOptions)dialog.DataContext;
                Assert.False(options.IncludePrivate); Assert.Equal(5, options.Counts.Sessions);
                vm.Manual.SelectedJournalPreset = vm.Manual.JournalDatePresets.Single(item => item.Key == "today");
                vm.Manual.RefreshJournalDatePreset(DateTime.Today.AddDays(1));
                Assert.Equal(0, options.Counts.Sessions); Assert.False(options.CanExport);
                vm.Manual.SelectedJournalPreset = vm.Manual.JournalDatePresets.Single(item => item.Key == "all");
                vm.Manual.JournalSearch = "unique-export-fixture";
                Assert.Equal(1, options.Counts.Sessions); Assert.Equal(1, options.Counts.Books);
                options.IncludePrivate = true;
                dialog.DialogResult = true;
            }
            catch (Exception ex) { dialogError = ex; dialog.DialogResult = false; }
        }));
        await InvokeAsync(vm, "ExportPhysicalLibraryV25Async");
        if (dialogError is not null) ExceptionDispatchInfo.Capture(dialogError).Throw();
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var books = json.RootElement.GetProperty("Books"); var sessions = json.RootElement.GetProperty("Sessions");
        Assert.Equal(1, books.GetArrayLength()); Assert.Equal(1, sessions.GetArrayLength());
        Assert.Equal("Physical fixture", books[0].GetProperty("Title").GetString());
        Assert.Equal("n0", sessions[0].GetProperty("Id").GetString());
        Assert.Equal("Private writing for unique-export-fixture", sessions[0].GetProperty("Note").GetString());
        Assert.True(json.RootElement.GetProperty("Export").GetProperty("IncludesPrivateWriting").GetBoolean());
        await vm.FlushPreferencesAsync();
    }

    private static void VerifyV25BarLabels()
    {
        _stage = "v2.5 categorical bar labels align with band centers";
        var labels = new[] { "1", "2", "3", "4" };
        var chart = new ReadestStats.Controls.BarChart
        {
            ItemsSource = labels.Select(label => new ChartPoint(label, 5, "5 min", "min")).ToArray()
        };
        Layout(chart, 480, 180);
        var glyphs = DrawingDescendants(VisualTreeHelper.GetDrawing(chart)).OfType<GlyphRunDrawing>()
            .Where(drawing => drawing.GlyphRun.BaselineOrigin.Y > 145 && drawing.GlyphRun.BaselineOrigin.X > 43).ToArray();
        Assert.Equal(4, glyphs.Length);
        for (var i = 0; i < glyphs.Length; i++)
        {
            var center = glyphs[i].GlyphRun.BaselineOrigin.X + glyphs[i].GlyphRun.AdvanceWidths.Sum() / 2;
            var expected = 43 + (480 - 51) / 4d * (i + .5);
            Assert.InRange(center, expected - .5, expected + .5);
        }
    }

    private static IEnumerable<Drawing> DrawingDescendants(Drawing drawing)
    {
        yield return drawing;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var descendant in DrawingDescendants(child)) yield return descendant;
    }

    private static async Task VerifyV25SessionCorrectionAsync(string parentRoot)
    {
        _stage = "v2.5 exact segmented session correction";
        var root = Path.Combine(parentRoot, "editor"); Directory.CreateDirectory(root);
        var start = new DateTimeOffset(DateTime.Today.AddDays(-5).AddHours(23).AddMinutes(59).AddSeconds(30), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today)).ToUniversalTime().AddTicks(1234567);
        var original = new ManualReadingSession
        {
            Id = "precise", BookId = -1, StartedAtUtc = start, EndedAtUtc = start.AddSeconds(140.75),
            DurationSeconds = 120.75, StartPage = 1, EndPage = 5, PagesRead = 5,
            Note = "First paragraph.\nFull untruncated private second paragraph.",
            Segments = [new() { StartedAtUtc = start, EndedAtUtc = start.AddSeconds(60.25) },
                new() { StartedAtUtc = start.AddSeconds(80.25), EndedAtUtc = start.AddSeconds(140.75) }]
        };
        var store = new ManualReadingStore(Path.Combine(root, "manual-reading.json"));
        await store.SaveAsync(new() { Books = [new() { Id = -1, Title = "Archived physical fixture", Archived = true, TotalPages = 100, CurrentPage = 5 },
            new() { Id = -2, Title = "Other edition", TotalPages = 100 }], Sessions = [original] });
        using var manual = new ManualLogViewModel(store, new BookMetadataService());
        await manual.InitializeAsync();
        manual.SelectedJournalSession = Assert.Single(manual.RecentSessions);
        Assert.Equal(original.Note, manual.SelectedSessionNote); Assert.Equal(2, manual.SelectedSessionSegments.Count);
        Assert.Contains("120.75", manual.SelectedSessionActive); Assert.Contains("140.75", manual.SelectedSessionElapsed);
        Assert.Contains("20", manual.SelectedSessionPauses);
        var editor = new SessionEditorWindow(manual, manual.SelectedJournalSession); Layout(editor, 520, 640);
        var books = (ComboBox)editor.FindName("BookInput");
        Assert.Equal(-1, ((ManualBook)books.SelectedItem).Id);
        Assert.Contains(books.Items.Cast<ManualBook>(), book => book.Id == -1 && book.Archived);
        Assert.True(((TextBox)editor.FindName("DurationInput")).IsReadOnly);
        Assert.Equal("2.0125", ((TextBox)editor.FindName("DurationInput")).Text);
        Assert.True(((TextBox)editor.FindName("TimeInput")).IsReadOnly);
        Assert.False(((DatePicker)editor.FindName("DateInput")).IsEnabled);
        Assert.Equal(Visibility.Visible, ((TextBox)editor.FindName("RecordedDateInput")).Visibility);
        Assert.True(((TextBox)editor.FindName("RecordedDateInput")).IsReadOnly);
        var timing = (CheckBox)editor.FindName("EditTimingInput");
        timing.IsChecked = true;
        Assert.False(((TextBox)editor.FindName("DurationInput")).IsReadOnly);
        Assert.True(((DatePicker)editor.FindName("DateInput")).IsEnabled);
        Assert.Equal(Visibility.Collapsed, ((TextBox)editor.FindName("RecordedDateInput")).Visibility);
        timing.IsChecked = false;
        ((TextBox)editor.FindName("NoteInput")).Text = "Edited private writing, exact time preserved.";
        ((TextBox)editor.FindName("EndPageInput")).Text = "8";
        await InvokeAsync(editor, "SaveAsync");
        var edited = manual.FindSession("precise")!;
        Assert.Equal(start, edited.StartedAtUtc); Assert.Equal(original.EndedAtUtc, edited.EndedAtUtc);
        Assert.Equal(120.75, edited.DurationSeconds); Assert.Equal(2, edited.Segments.Count);
        Assert.Equal(original.Segments.Select(segment => (segment.StartedAtUtc, segment.EndedAtUtc)), edited.Segments.Select(segment => (segment.StartedAtUtc, segment.EndedAtUtc)));
        Assert.Equal(8, edited.EndPage); Assert.Equal(8, manual.FindBook(-1)!.CurrentPage);
        timing.IsChecked = true;
        await InvokeAsync(editor, "SaveAsync");
        edited = manual.FindSession("precise")!;
        Assert.Equal(original.StartedAtUtc, edited.StartedAtUtc); Assert.Equal(original.EndedAtUtc, edited.EndedAtUtc);
        Assert.Equal(original.DurationSeconds, edited.DurationSeconds);
        Assert.Equal(original.Segments.Select(segment => (segment.StartedAtUtc, segment.EndedAtUtc)), edited.Segments.Select(segment => (segment.StartedAtUtc, segment.EndedAtUtc)));
        manual.SelectedJournalSession = Assert.Single(manual.RecentSessions);
        editor.Close();

        _stage = "v2.5 historical correction drains writes and defers checkpoints";
        var writeLock = (SemaphoreSlim)typeof(ManualReadingStore).GetField("_writeLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        await writeLock.WaitAsync();
        Task<string?> correction;
        try
        {
            correction = manual.SaveHistoricalSessionAsync("precise", -1, start, 2, 1, 8, edited.Note);
            Assert.True(manual.IsCorrectingSession); Assert.False(correction.IsCompleted);
            Assert.Same(edited, manual.FindSession("precise")); // Candidate isn't visible until older writes drain.
            var pending = (HashSet<Task>)typeof(ManualLogViewModel).GetField("_pendingStorageTasks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manual)!;
            var count = pending.Count;
            await InvokeAsync(manual, "SaveCheckpointAsync"); Assert.Equal(count, pending.Count);
            Assert.False(manual.StartSessionCommand.CanExecute(null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(manual, "StartSessionAsync"));
            await Assert.ThrowsAsync<InvalidOperationException>(manual.FlushDraftAsync);
        }
        finally { writeLock.Release(); }
        Assert.Null(await correction); Assert.False(manual.IsCorrectingSession);
        edited = manual.FindSession("precise")!;
        manual.SelectedJournalSession = Assert.Single(manual.RecentSessions);

        _stage = "v2.5 correction rollback on synthetic SQLite failure";
        var retryEditor = new SessionEditorWindow(manual, manual.SelectedJournalSession);
        ((ComboBox)retryEditor.FindName("BookInput")).SelectedItem = manual.FindBook(-2);
        ((TextBox)retryEditor.FindName("EndPageInput")).Text = "99";
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DatabasePath }.ToString());
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER reject_fixture_edit BEFORE INSERT ON manual_sessions BEGIN SELECT RAISE(ABORT, 'Synthetic save failure'); END";
            command.ExecuteNonQuery();
        }
        try
        {
            await InvokeAsync(retryEditor, "SaveAsync");
            Assert.Same(edited, manual.FindSession("precise"));
            Assert.Equal(-1, manual.FindSession("precise")!.BookId);
            Assert.Equal(8, manual.FindBook(-1)!.CurrentPage); Assert.Null(manual.FindBook(-2)!.CurrentPage);
            Assert.Contains("unchanged", ((TextBlock)retryEditor.FindName("ErrorText")).Text);
            Assert.True(((Button)retryEditor.FindName("SaveButton")).IsEnabled);
        }
        finally
        {
            using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER reject_fixture_edit"; command.ExecuteNonQuery();
        }
        // An observed failed task is acknowledged once, so retry/close remains possible.
        await Assert.ThrowsAsync<SqliteException>(manual.FlushDraftAsync);
        await manual.FlushDraftAsync();
        await InvokeAsync(retryEditor, "SaveAsync");
        Assert.Equal(-2, manual.FindSession("precise")!.BookId);
        Assert.Equal(99, manual.FindBook(-2)!.CurrentPage);
        Assert.Equal(120.75, manual.FindSession("precise")!.DurationSeconds);
        if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } output)
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ThemeManager.Apply(theme); Layout(retryEditor, 520, 640);
                Render(retryEditor, Path.Combine(output, $"v25-session-editor-{theme.ToLowerInvariant()}.png"));
            }
        }
        retryEditor.Close(); ThemeManager.Apply("Light");
    }


    private static void Layout(FrameworkElement element, double width = 1280, double height = 800)
    {
        if (element is Window window) element = (FrameworkElement)window.Content;
        Localizer.RefreshBindings(element);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static Task InvokeAsync(object target, string method, params object[] args) => (Task)target.GetType()
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .Single(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
        .Invoke(target, args)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Window window) root = (DependencyObject)window.Content;
        if (root is not Visual) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        if (element is Window window) element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }

    private static void RunOnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () => { try { await action(); } catch (Exception ex) { failure = ex; } finally { dispatcher.InvokeShutdown(); } });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "WPF verification timed out at: " + _stage);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
