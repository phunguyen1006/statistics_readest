using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class ManualLogViewModel : ObservableObject, IDisposable
{
    public sealed record JournalDatePreset(string Key, string English, string Vietnamese)
    {
        public string Label => Text(English, Vietnamese);
    }

    public sealed record SessionSegmentRow(int Number, DateTimeOffset Start, DateTimeOffset End, double Seconds)
    {
        public string TimeLabel => $"{Start:MMM d, HH:mm:ss.fff} – {End:MMM d, HH:mm:ss.fff}";
        public string DurationLabel => ExactDuration(Seconds);
    }

    public sealed record DuplicateBookGroup(string Key, string Label, IReadOnlyList<ManualBook> Books)
    {
        public string Detail => $"{Books.Count} editions · {string.Join(" · ", Books.Select(book => book.Title))}";
    }

    private readonly ManualReadingStore _store;
    private readonly BookMetadataService _metadata;
    private readonly Action<string> _log;
    private readonly Func<string, bool> _confirmMerge;
    private readonly CoverCacheService _coverCache;
    private ManualReadingData _data = new();
    private readonly HashSet<Task> _pendingStorageTasks = [];
    private bool _disposed;
    private string? _flushedActiveSnapshot;
    private bool _suspendedForRestore;
    private long _storageEpoch;
    private int _mutationOperations;
    private bool _isCorrectingSession;
    private string _status = "Your physical-reading data is stored locally.";

    public Func<LibraryReferenceSnapshot>? CaptureReferences { get; set; }
    public Func<LibraryReferenceSnapshot, Task>? RestoreReferences { get; set; }
    public Func<long, IReadOnlyList<long>, Task>? MergeReferences { get; set; }

    public event EventHandler? DataChanged;
    public event Action<long>? OpenBookRequested;
    public event Action<DateOnly>? OpenCalendarDayRequested;

    public bool IsCorrectingSession => _isCorrectingSession;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public IReadOnlyList<ManualBook> AllBooks => _data.Books;
    public IReadOnlyList<ManualReadingSession> Sessions => _data.Sessions;
    public string DatabasePath => _store.DatabasePath;
    public AppDatabaseHealth DatabaseHealth => _store.InspectDatabase();
    public bool HasMirrorWarning => _store.LastMirrorError is not null;
    public string StorageStatus => HasMirrorWarning
        ? Text("Local sessions are safe in SQLite. The recovery JSON mirror could not be updated; retry to repair it.", "Các phiên đã lưu an toàn trong SQLite. Bản JSON phục hồi chưa cập nhật được; hãy thử lại để sửa.")
        : Text("SQLite and the local recovery mirror are up to date.", "SQLite và bản phục hồi trên máy đã được cập nhật.");
    public string LibrarySummary => $"{Books.Count} physical {(Books.Count == 1 ? "book" : "books")} · {_data.Sessions.Count} manual {(_data.Sessions.Count == 1 ? "session" : "sessions")}";

    public RelayCommand AddBookCommand { get; }
    public RelayCommand CancelAddBookCommand { get; }
    public AsyncCommand SearchBooksCommand { get; }
    public RelayCommand UseManualEntryCommand { get; }
    public AsyncCommand SaveBookCommand { get; }
    public AsyncCommand StartSessionCommand { get; }
    public AsyncCommand PauseResumeCommand { get; }
    public RelayCommand BeginFinishCommand { get; }
    public RelayCommand CancelFinishCommand { get; }
    public AsyncCommand SaveFinishedSessionCommand { get; }
    public AsyncCommand DiscardSessionCommand { get; }
    public ParameterCommand<ManualSessionRow> DeleteSessionCommand { get; }
    public AsyncCommand UndoDeleteSessionCommand { get; }
    public AsyncCommand UndoDeleteBookCommand { get; }
    public AsyncCommand UndoLastImportCommand { get; }
    public AsyncCommand UndoRecentActionCommand { get; }
    public ParameterCommand<ManualBook> DeleteBookCommand { get; }
    public ParameterCommand<ManualBook> EditBookCommand { get; }
    public RelayCommand OpenMetadataCommand { get; }
    public AsyncCommand ArchiveSelectedBookCommand { get; }
    public AsyncCommand MergeDuplicateBooksCommand { get; }
    public AsyncCommand RestoreArchivedBookCommand { get; }
    public RelayCommand ClearJournalFiltersCommand { get; }
    public AsyncCommand RetryMirrorCommand { get; }
    public RelayCommand OpenJournalBookCommand { get; }
    public RelayCommand OpenJournalDayCommand { get; }

    public ManualLogViewModel(ManualReadingStore store, BookMetadataService metadata, Action<string>? log = null, CoverCacheService? coverCache = null, Func<string, bool>? confirmMerge = null)
    {
        _store = store;
        _metadata = metadata;
        _log = log ?? (_ => { });
        _confirmMerge = confirmMerge ?? (preview => Localization.UiDialog.Show(preview, "Merge physical editions", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.OK);
        _coverCache = coverCache ?? new(Path.Combine(Path.GetDirectoryName(store.Path)!, "covers"));
        _timer.Tick += TimerTick;
        _draftCheckpointTimer.Tick += async (_, _) => { _draftCheckpointTimer.Stop(); await SaveCheckpointAsync(); };

        AddBookCommand = new RelayCommand(BeginAddBook, () => !IsCorrectingSession);
        CancelAddBookCommand = new RelayCommand(CancelAddBook, () => !IsCorrectingSession);
        SearchBooksCommand = new AsyncCommand(SearchBooksAsync, () => !IsCorrectingSession && SearchQuery.Trim().Length >= 2);
        UseManualEntryCommand = new RelayCommand(UseManualEntry, () => !IsCorrectingSession);
        SaveBookCommand = new AsyncCommand(SaveBookAsync, () => !IsCorrectingSession && IsAddingBook);
        StartSessionCommand = new AsyncCommand(StartSessionAsync, () => !IsCorrectingSession && !_suspendedForRestore && SelectedBook is not null && !HasActiveSession);
        PauseResumeCommand = new AsyncCommand(PauseResumeAsync, () => !IsCorrectingSession && HasActiveSession && !IsFinishing);
        BeginFinishCommand = new RelayCommand(BeginFinish, () => !IsCorrectingSession && HasActiveSession && !IsFinishing);
        CancelFinishCommand = new RelayCommand(CancelFinish, () => !IsCorrectingSession);
        SaveFinishedSessionCommand = new AsyncCommand(SaveFinishedSessionAsync, () => !IsCorrectingSession && HasActiveSession && IsFinishing);
        DiscardSessionCommand = new AsyncCommand(DiscardSessionAsync, () => !IsCorrectingSession && HasActiveSession);
        DeleteSessionCommand = new ParameterCommand<ManualSessionRow>(row => { if (row is not null) _ = DeleteSessionAsync(row.Id); }, row => !IsCorrectingSession && row is not null);
        UndoDeleteSessionCommand = new AsyncCommand(UndoDeleteSessionAsync, () => !IsCorrectingSession && _lastDeletedSession is not null);
        UndoDeleteBookCommand = new AsyncCommand(UndoDeleteBookAsync, () => !IsCorrectingSession && _lastDeletedBook is not null);
        UndoLastImportCommand = new AsyncCommand(UndoLastImportAsync, () => !IsCorrectingSession && (_lastImportedBookIds.Count > 0 || _lastImportedSessionIds.Count > 0));
        UndoRecentActionCommand = new AsyncCommand(UndoRecentActionAsync, () => !IsCorrectingSession && ActivityLog.Count > 0 && !HasActiveSession);
        DeleteBookCommand = new ParameterCommand<ManualBook>(book => { if (book is not null) _ = DeleteBookAsync(book.Id); }, book => !IsCorrectingSession && book is not null);
        EditBookCommand = new ParameterCommand<ManualBook>(BeginEditBook, book => !IsCorrectingSession && book is not null && !HasActiveSession);
        OpenMetadataCommand = new RelayCommand(OpenMetadata, () => ExternalLinkPolicy.TryGetWebUri(SelectedSearchResult?.InfoUrl, out _));
        ArchiveSelectedBookCommand = new AsyncCommand(ArchiveSelectedBookAsync, () => !IsCorrectingSession && SelectedBook is not null && !HasActiveSession);
        MergeDuplicateBooksCommand = new AsyncCommand(MergeDuplicateBooksAsync, () => !IsCorrectingSession && SelectedDuplicateGroup is not null && !HasActiveSession);
        RestoreArchivedBookCommand = new AsyncCommand(RestoreArchivedBookAsync, () => !IsCorrectingSession && SelectedArchivedBook is not null && !HasActiveSession);
        ClearJournalFiltersCommand = new RelayCommand(ClearJournalFilters);
        RetryMirrorCommand = new AsyncCommand(RetryMirrorAsync, () => !IsCorrectingSession && HasMirrorWarning);
        _selectedJournalPreset = JournalDatePresets[0];
        OpenJournalBookCommand = new RelayCommand(() => { if (SelectedJournalSession is { } row) OpenBookRequested?.Invoke(row.BookId); }, () => SelectedJournalSession is not null);
        OpenJournalDayCommand = new RelayCommand(() => { if (SelectedJournalSession is { } row) OpenCalendarDayRequested?.Invoke(DateOnly.FromDateTime(row.StartedAt.LocalDateTime)); }, () => SelectedJournalSession is not null);
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCorrectingSession) throw new InvalidOperationException("Wait for the session correction to finish before reloading the manual library.");
        _timer.Stop();
        _draftCheckpointTimer.Stop();
        IsFinishing = false;
        _draftCheckpointError = "";
        Raise(nameof(FinishDraftHint));
        _data = await _store.LoadAsync();
        if (_disposed) return;
        if (_data.RecoveryReferences is not null) { await RecoverReferencesAsync(); await SaveStorageAsync(allowWhileSuspended: true); }
        RebuildCollections();
        if (_data.ActiveSession is not null)
        {
            var now = DateTimeOffset.UtcNow;
            if (_data.ActiveSession.IsPaused)
            {
                Status = "Recovered a paused manual session.";
            }
            else if (_data.ActiveSession.LastUpdatedUtc != default && now - _data.ActiveSession.LastUpdatedUtc > TimeSpan.FromMinutes(5))
            {
                CloseRunningSegment(_data.ActiveSession, _data.ActiveSession.LastUpdatedUtc.AddSeconds(2));
                _data.ActiveSession.IsPaused = true;
                _data.ActiveSession.LastResumedAtUtc = null;
                _data.ActiveSession.LastUpdatedUtc = now;
                Status = Text("Recovered and paused your session after the app was closed.", "Đã phục hồi và tạm dừng phiên sau khi đóng ứng dụng.");
                await SaveStorageAsync(allowWhileSuspended: true);
            }
            SelectedBook = Books.FirstOrDefault(book => book.Id == _data.ActiveSession.BookId);
            RestoreFinishDraft();
            IsFinishing = _data.ActiveSession.IsFinishing;
            TargetMinutes = _data.ActiveSession.TargetMinutes?.ToString() ?? "";
            if (IsFinishing) Status = Text("Recovered your finish draft · confirm the pages and save the session.", "Đã phục hồi bản nháp kết thúc · xác nhận số trang rồi lưu phiên.");
            else if (!Status.StartsWith("Recovered and paused", StringComparison.Ordinal)) Status = _data.ActiveSession.IsPaused ? "Recovered a paused manual session." : "Recovered a running manual session.";
            _lastTimerHeartbeatUtc = now;
            if (!IsFinishing && !_suspendedForRestore) _timer.Start();
        }
        RaiseTimerState();
        RaiseStorageState();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<Book> AsBooks() => _data.Books
        .Select(book => new Book(book.Id, book.Title, book.Authors, Pages: book.TotalPages, Language: book.Language, Hash: "manual:" + book.Id, Source: "Manual", CoverUrl: book.CoverUrl)).ToArray();

    public IReadOnlyList<ReadingEvent> AsReadingEvents() => _data.Sessions
        .Where(session => session.DurationSeconds > 0 && _data.Books.Any(book => book.Id == session.BookId))
        .SelectMany(session =>
        {
            var book = _data.Books.First(item => item.Id == session.BookId);
            var segments = session.Segments.Count > 0
                ? session.Segments
                : [new ManualReadingSegment { StartedAtUtc = session.StartedAtUtc, EndedAtUtc = session.StartedAtUtc.AddSeconds(session.DurationSeconds) }];
            return segments.Select(segment => new ReadingEvent(session.BookId, session.EndPage ?? 0, segment.StartedAtUtc.ToUnixTimeSeconds(), segment.DurationSeconds, book.TotalPages, "Manual"));
        }).OrderBy(item => item.StartTime).ToArray();

    public IReadOnlyList<ReadestNote> AsNotes() => _data.Sessions
        .Where(session => !string.IsNullOrWhiteSpace(session.Note))
        .Select(session =>
        {
            var book = _data.Books.FirstOrDefault(item => item.Id == session.BookId);
            return new ReadestNote(
                "manual:" + session.Id,
                "manual:" + session.BookId,
                book?.Title ?? "Unknown physical book",
                book?.Authors ?? "",
                null,
                book?.CoverUrl,
                "",
                session.Note,
                "Manual note",
                "Default",
                session.EndPage,
                session.CreatedAtUtc,
                session.EndedAtUtc,
                null,
                null,
                null);
        }).OrderByDescending(note => note.CreatedAt).ToArray();

    public ManualReadingSession? FindSession(DateTimeOffset start, long bookId) => _data.Sessions.FirstOrDefault(item => item.BookId == bookId && start >= item.StartedAtUtc.AddSeconds(-2) && start <= item.EndedAtUtc.AddSeconds(2));
    public ManualReadingSession? FindSession(string id) => _data.Sessions.FirstOrDefault(item => item.Id == id);
    public ManualBook? FindBook(long id) => _data.Books.FirstOrDefault(book => book.Id == id);

    public async Task ExportLibraryAsync(string path)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(CapturePortableLibrary(), new JsonSerializerOptions { WriteIndented = true }));

    public async Task FlushDraftAsync()
    {
        EnsureCanMutate();
        _draftCheckpointTimer.Stop();
        await AwaitPendingStorageAsync();
        EnsureCanMutate();
        if (_data.ActiveSession is null) return;
        _data.ActiveSession.LastUpdatedUtc = DateTimeOffset.UtcNow;
        var snapshot = JsonSerializer.Serialize(_data.ActiveSession);
        await SaveAndPublishAsync(false);
        _flushedActiveSnapshot = snapshot;
    }

    public async Task SuspendForRestoreAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (HasActiveSession) throw new InvalidOperationException("Finish or discard the active manual session before restoring a backup.");
        if (IsCorrectingSession || _mutationOperations > 0 || IsAddingBook || IsSearching)
            throw new InvalidOperationException("Wait for the manual-library operation to finish and close the book editor before restoring a backup.");
        _suspendedForRestore = true;
        _storageEpoch++;
        _timer.Stop();
        _draftCheckpointTimer.Stop();
        _coverSearchCancellation?.Cancel();
        StartSessionCommand.Refresh();
        try
        {
            await AwaitPendingStorageAsync();
            await _store.WaitForWritesAsync();
        }
        catch { ResumeAfterRestore(); throw; }
    }

    public void ResumeAfterRestore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _suspendedForRestore = false;
        _storageEpoch++;
        _lastTimerHeartbeatUtc = DateTimeOffset.UtcNow;
        if (HasActiveSession && !IsFinishing) _timer.Start();
        RaiseTimerState();
    }

    private void RebuildCollections()
    {
        if (_disposed) return;
        var selectedId = SelectedBook?.Id;
        Replace(ArchivedBooks, _data.Books.Where(book => book.Archived).OrderBy(book => book.Title));
        SelectedArchivedBook = ArchivedBooks.FirstOrDefault();
        Raise(nameof(HasArchivedBooks));
        Replace(Books, _data.Books.Where(book => !book.Archived).OrderByDescending(book => _data.Sessions.Where(item => item.BookId == book.Id).Select(item => item.StartedAtUtc).DefaultIfEmpty(book.CreatedAtUtc).Max()).ThenBy(book => book.Title));
        BuildDuplicateGroups();
        var journalBookId = SelectedJournalBook?.BookId;
        Replace(JournalBooks, new[] { new ManualJournalBookOption(null, Text("All physical books", "Tất cả sách giấy")) }
            .Concat(_data.Books.OrderBy(book => book.Title).Select(book => new ManualJournalBookOption(book.Id, book.Title))));
        SelectedJournalBook = JournalBooks.FirstOrDefault(book => book.BookId == journalBookId) ?? JournalBooks.First();
        RebuildJournal();
        SelectedBook = Books.FirstOrDefault(book => book.Id == selectedId) ?? Books.FirstOrDefault();
        Raise(nameof(HasBooks));
        Raise(nameof(HasSessions));
        Raise(nameof(LibrarySummary));
        Raise(nameof(SelectedBookProgress));
        Raise(nameof(SelectedBookProgressLabel));
        StartSessionCommand.Refresh();
        ArchiveSelectedBookCommand.Refresh();
        MergeDuplicateBooksCommand.Refresh();
    }

    private async Task SaveAndPublishAsync(bool publish = true)
    {
        EnsureCanMutate();
        var epoch = _storageEpoch;
        await SaveStorageAsync();
        if (_disposed || _suspendedForRestore || epoch != _storageEpoch) return;
        if (_store.LastMirrorError is { } mirrorError) _log("Manual library was saved to SQLite, but its JSON backup mirror could not be updated: " + mirrorError);
        RaiseStorageState();
        if (publish) DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task SaveStorageAsync(bool allowWhileSuspended = false, bool correctionOwned = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspendedForRestore && !allowWhileSuspended) throw new InvalidOperationException("Manual-library writes are suspended while a backup is being restored.");
        if (IsCorrectingSession && !correctionOwned) throw new InvalidOperationException("Wait for the current session correction to finish before saving another manual-library change.");
        _pendingStorageTasks.RemoveWhere(task => task.IsCompleted);
        var task = _store.SaveAsync(_data);
        _pendingStorageTasks.Add(task);
        return task;
    }

    private async Task AwaitPendingStorageAsync()
    {
        var pending = _pendingStorageTasks.ToArray();
        try { await Task.WhenAll(pending); }
        catch
        {
            foreach (var task in pending.Where(task => task.IsCompleted && (task.IsFaulted || task.IsCanceled)))
            {
                if (task.IsFaulted) _ = task.Exception;
                _pendingStorageTasks.Remove(task);
            }
            throw;
        }
    }

    private async Task RetryMirrorAsync()
    {
        try { await SaveAndPublishAsync(false); }
        catch (Exception ex) { _log("Local recovery mirror retry failed: " + ex); }
        RaiseStorageState();
    }

    private void RaiseStorageState()
    {
        Raise(nameof(HasMirrorWarning));
        Raise(nameof(StorageStatus));
        RetryMirrorCommand.Refresh();
    }

    private void EnsureCanMutate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_suspendedForRestore) throw new InvalidOperationException("Manual-library changes are suspended while a backup is being restored.");
        if (IsCorrectingSession) throw new InvalidOperationException("Wait for the current session correction to finish before changing or closing the manual library.");
    }

    private IDisposable BeginMutationOperation()
    {
        EnsureCanMutate();
        _mutationOperations++;
        return new MutationScope(() => _mutationOperations--);
    }

    private sealed class MutationScope(Action end) : IDisposable
    {
        private bool _ended;
        public void Dispose() { if (_ended) return; _ended = true; end(); }
    }

    private void RefreshCorrectionCommands()
    {
        AddBookCommand.Refresh(); CancelAddBookCommand.Refresh(); SearchBooksCommand.Refresh(); UseManualEntryCommand.Refresh(); SaveBookCommand.Refresh();
        StartSessionCommand.Refresh(); PauseResumeCommand.Refresh(); BeginFinishCommand.Refresh(); CancelFinishCommand.Refresh();
        SaveFinishedSessionCommand.Refresh(); DiscardSessionCommand.Refresh(); DeleteSessionCommand.Refresh(); UndoDeleteSessionCommand.Refresh();
        DeleteBookCommand.Refresh(); EditBookCommand.Refresh(); UndoDeleteBookCommand.Refresh(); UndoLastImportCommand.Refresh(); UndoRecentActionCommand.Refresh();
        ArchiveSelectedBookCommand.Refresh(); MergeDuplicateBooksCommand.Refresh(); RestoreArchivedBookCommand.Refresh(); RetryMirrorCommand.Refresh();
    }

    private static string Text(string english, string vietnamese) => Localization.Localizer.Instance.IsVietnamese ? vietnamese : english;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        if (target is RangeObservableCollection<T> range)
        {
            range.ReplaceRange(source);
            return;
        }
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (IsCorrectingSession) throw new InvalidOperationException("Wait for the current session correction to finish before closing the manual library.");
        _disposed = true;
        _coverSearchCancellation?.Cancel();
        _coverSearchCancellation?.Dispose();
        _timer.Stop();
        _draftCheckpointTimer.Stop();
        Exception? pendingFailure = null;
        try { Task.WhenAll(_pendingStorageTasks.ToArray()).GetAwaiter().GetResult(); }
        catch (Exception ex) { pendingFailure = ex; _log("Manual persistence while closing failed: " + ex); }
        if (_data.ActiveSession is not null && JsonSerializer.Serialize(_data.ActiveSession) != _flushedActiveSnapshot)
        {
            _data.ActiveSession.LastUpdatedUtc = DateTimeOffset.UtcNow;
            try { _store.SaveAsync(_data).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log("Manual draft could not be saved while closing: " + ex); throw; }
            pendingFailure = null;
        }
        if (pendingFailure is not null) throw new InvalidOperationException("Manual library changes could not be saved while closing.", pendingFailure);
    }
}
