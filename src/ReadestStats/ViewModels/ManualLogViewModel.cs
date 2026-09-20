using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class ManualLogViewModel : ObservableObject, IDisposable
{
    public sealed record DuplicateBookGroup(string Key, string Label, IReadOnlyList<ManualBook> Books)
    {
        public string Detail => $"{Books.Count} editions · {string.Join(" · ", Books.Select(book => book.Title))}";
    }
    private readonly ManualReadingStore _store;
    private readonly BookMetadataService _metadata;
    private readonly Action<string> _log;
    private readonly CoverCacheService _coverCache;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ManualReadingData _data = new();
    private ManualBook? _selectedBook;
    private BookMetadataResult? _selectedSearchResult;
    private string _searchQuery = "";
    private string _searchStatus = "Search by title, author, or ISBN.";
    private string _status = "Your physical-reading data is stored locally.";
    private bool _isAddingBook;
    private bool _isSearching;
    private bool _isFinishing;
    private string _draftTitle = "";
    private string _draftAuthors = "";
    private string _draftTotalPages = "";
    private string _draftPublisher = "";
    private string _draftPublishedDate = "";
    private string _draftLanguage = "";
    private string _draftGoodreadsUrl = "";
    private string _draftError = "";
    private string _finishStartPage = "";
    private string _finishEndPage = "";
    private string _finishError = "";
    private string _finishNote = "";
    private int _ticksSinceSave;
    private DateTimeOffset _lastTimerHeartbeatUtc;
    private long? _editingBookId;
    private ManualReadingSession? _lastDeletedSession;
    private ManualBook? _lastDeletedBook;
    private List<ManualReadingSession> _lastDeletedBookSessions = [];
    private readonly List<long> _lastImportedBookIds = [];
    private readonly List<string> _lastImportedSessionIds = [];
    private DuplicateBookGroup? _selectedDuplicateGroup;

    public ManualLogViewModel(ManualReadingStore store, BookMetadataService metadata, Action<string>? log = null, CoverCacheService? coverCache = null)
    {
        _store = store;
        _metadata = metadata;
        _log = log ?? (_ => { });
        _coverCache = coverCache ?? new(Path.Combine(Path.GetDirectoryName(store.Path)!, "covers"));
        _timer.Tick += TimerTick;
        AddBookCommand = new RelayCommand(BeginAddBook);
        CancelAddBookCommand = new RelayCommand(CancelAddBook);
        SearchBooksCommand = new AsyncCommand(SearchBooksAsync, () => SearchQuery.Trim().Length >= 2);
        UseManualEntryCommand = new RelayCommand(UseManualEntry);
        SaveBookCommand = new AsyncCommand(SaveBookAsync, () => IsAddingBook);
        StartSessionCommand = new AsyncCommand(StartSessionAsync, () => SelectedBook is not null && !HasActiveSession);
        PauseResumeCommand = new AsyncCommand(PauseResumeAsync, () => HasActiveSession);
        BeginFinishCommand = new RelayCommand(BeginFinish, () => HasActiveSession);
        CancelFinishCommand = new RelayCommand(CancelFinish);
        SaveFinishedSessionCommand = new AsyncCommand(SaveFinishedSessionAsync, () => HasActiveSession && IsFinishing);
        DiscardSessionCommand = new AsyncCommand(DiscardSessionAsync, () => HasActiveSession);
        DeleteSessionCommand = new ParameterCommand<ManualSessionRow>(row => { if (row is not null) _ = DeleteSessionAsync(row.Id); });
        UndoDeleteSessionCommand = new AsyncCommand(UndoDeleteSessionAsync, () => _lastDeletedSession is not null);
        UndoDeleteBookCommand = new AsyncCommand(UndoDeleteBookAsync, () => _lastDeletedBook is not null);
        UndoLastImportCommand = new AsyncCommand(UndoLastImportAsync, () => _lastImportedBookIds.Count > 0 || _lastImportedSessionIds.Count > 0);
        UndoRecentActionCommand = new AsyncCommand(UndoRecentActionAsync, () => ActivityLog.Count > 0);
        DeleteBookCommand = new ParameterCommand<ManualBook>(book => { if (book is not null) _ = DeleteBookAsync(book.Id); });
        EditBookCommand = new ParameterCommand<ManualBook>(BeginEditBook, book => book is not null && !HasActiveSession);
        OpenMetadataCommand = new RelayCommand(OpenMetadata, () => SelectedSearchResult?.InfoUrl is not null);
        ArchiveSelectedBookCommand = new AsyncCommand(ArchiveSelectedBookAsync, () => SelectedBook is not null && !HasActiveSession);
        MergeDuplicateBooksCommand = new AsyncCommand(MergeDuplicateBooksAsync, () => SelectedDuplicateGroup is not null && !HasActiveSession);
    }

    public event EventHandler? DataChanged;
    public ObservableCollection<ManualBook> Books { get; } = [];
    public ObservableCollection<ManualSessionRow> RecentSessions { get; } = [];
    public ObservableCollection<BookMetadataResult> SearchResults { get; } = [];
    public ObservableCollection<OperationJournalEntry> ActivityLog { get; } = [];
    public ObservableCollection<DuplicateBookGroup> DuplicateGroups { get; } = [];

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

    public ManualBook? SelectedBook
    {
        get => _selectedBook;
        set
        {
            if (!Set(ref _selectedBook, value)) return;
            Raise(nameof(SelectedBookProgress));
            Raise(nameof(SelectedBookProgressLabel));
            StartSessionCommand.Refresh();
            ArchiveSelectedBookCommand.Refresh();
        }
    }

    public BookMetadataResult? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set
        {
            if (!Set(ref _selectedSearchResult, value) || value is null) return;
            DraftTitle = value.Title;
            DraftAuthors = value.Authors;
            DraftTotalPages = value.PageCount?.ToString() ?? "";
            DraftPublisher = value.Publisher ?? "";
            DraftPublishedDate = value.PublishedDate ?? "";
            DraftLanguage = value.Language ?? "";
            OpenMetadataCommand.Refresh();
        }
    }
    public DuplicateBookGroup? SelectedDuplicateGroup { get => _selectedDuplicateGroup; set { if (Set(ref _selectedDuplicateGroup, value)) MergeDuplicateBooksCommand.Refresh(); } }
    public bool HasDuplicateGroups => DuplicateGroups.Count > 0;

    public string SearchQuery { get => _searchQuery; set { if (Set(ref _searchQuery, value)) SearchBooksCommand.Refresh(); } }
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsAddingBook
    {
        get => _isAddingBook;
        private set
        {
            if (!Set(ref _isAddingBook, value)) return;
            Raise(nameof(IsNotAddingBook));
            SaveBookCommand.Refresh();
        }
    }
    public bool IsNotAddingBook => !IsAddingBook;
    public bool IsEditingBook => _editingBookId is not null;
    public string BookEditorTitle => IsEditingBook ? "Edit physical book" : "Add a physical book";
    public bool IsSearching { get => _isSearching; private set => Set(ref _isSearching, value); }
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool IsFinishing
    {
        get => _isFinishing;
        private set
        {
            if (!Set(ref _isFinishing, value)) return;
            Raise(nameof(IsNotFinishing));
            SaveFinishedSessionCommand.Refresh();
        }
    }
    public bool IsNotFinishing => !IsFinishing;
    public string DraftTitle { get => _draftTitle; set => Set(ref _draftTitle, value); }
    public string DraftAuthors { get => _draftAuthors; set => Set(ref _draftAuthors, value); }
    public string DraftTotalPages { get => _draftTotalPages; set => Set(ref _draftTotalPages, value); }
    public string DraftPublisher { get => _draftPublisher; set => Set(ref _draftPublisher, value); }
    public string DraftPublishedDate { get => _draftPublishedDate; set => Set(ref _draftPublishedDate, value); }
    public string DraftLanguage { get => _draftLanguage; set => Set(ref _draftLanguage, value); }
    public string DraftGoodreadsUrl { get => _draftGoodreadsUrl; set => Set(ref _draftGoodreadsUrl, value); }
    public string DraftError { get => _draftError; private set => Set(ref _draftError, value); }
    public string FinishStartPage { get => _finishStartPage; set => Set(ref _finishStartPage, value); }
    public string FinishEndPage { get => _finishEndPage; set => Set(ref _finishEndPage, value); }
    public string FinishError { get => _finishError; private set => Set(ref _finishError, value); }
    public string FinishNote { get => _finishNote; set => Set(ref _finishNote, value); }

    public bool HasBooks => Books.Count > 0;
    public bool HasSessions => RecentSessions.Count > 0;
    public bool CanUndoDeleteSession => _lastDeletedSession is not null;
    public bool CanUndoDeleteBook => _lastDeletedBook is not null;
    public bool CanUndoLastImport => _lastImportedBookIds.Count > 0 || _lastImportedSessionIds.Count > 0;
    public bool HasActiveSession => _data.ActiveSession is not null;
    public bool CanSelectBook => !HasActiveSession;
    public bool IsSessionPaused => _data.ActiveSession?.IsPaused == true;
    public string PauseResumeLabel => IsSessionPaused ? "Resume" : "Pause";
    public string ActiveBookTitle => ActiveBook?.Title ?? "No active session";
    public string ActiveBookAuthors => ActiveBook?.Authors ?? "Choose a physical book to begin.";
    public string ActiveBookCover => ActiveBook?.CoverUrl ?? "";
    public string ActiveSessionState => !HasActiveSession ? "READY" : IsSessionPaused ? "PAUSED" : "READING NOW";
    public string ElapsedLabel => FormatTimer(_data.ActiveSession?.ElapsedSeconds(DateTimeOffset.UtcNow) ?? 0);
    public double SelectedBookProgress => SelectedBook?.ProgressPercent ?? 0;
    public string SelectedBookProgressLabel => SelectedBook is null ? "Select a book" : SelectedBook.TotalPages is > 0 ? $"{SelectedBook.CurrentPage ?? 0} / {SelectedBook.TotalPages} pages · {SelectedBook.ProgressPercent:0}%" : $"Page {SelectedBook.CurrentPage ?? 0} · total pages unknown";
    public string LibrarySummary => $"{Books.Count} physical {(Books.Count == 1 ? "book" : "books")} · {RecentSessions.Count} manual {(RecentSessions.Count == 1 ? "session" : "sessions")}";
    public string DatabasePath => _store.DatabasePath;
    public AppDatabaseHealth DatabaseHealth => _store.InspectDatabase();

    public async Task InitializeAsync()
    {
        _data = await _store.LoadAsync();
        RebuildCollections();
        RefreshActivityLog();
        if (_data.ActiveSession is not null)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_data.ActiveSession.IsPaused && now - _data.ActiveSession.LastUpdatedUtc > TimeSpan.FromSeconds(90))
            {
                CloseRunningSegment(_data.ActiveSession, _data.ActiveSession.LastUpdatedUtc);
                _data.ActiveSession.IsPaused = true;
                _data.ActiveSession.LastResumedAtUtc = null;
                Status = "Recovered and paused the timer; time while the app was closed was excluded.";
                await _store.SaveAsync(_data);
            }
            SelectedBook = Books.FirstOrDefault(book => book.Id == _data.ActiveSession.BookId);
            if (!Status.StartsWith("Recovered and paused", StringComparison.Ordinal)) Status = _data.ActiveSession.IsPaused ? "Recovered a paused manual session." : "Recovered a running manual session.";
            _lastTimerHeartbeatUtc = now;
            _timer.Start();
        }
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<Book> AsBooks() => _data.Books.Where(book => !book.Archived)
        .Select(book => new Book(book.Id, book.Title, book.Authors, Pages: book.TotalPages, Language: book.Language, Hash: "manual:" + book.Id, Source: "Manual", CoverUrl: book.CoverUrl)).ToArray();

    public IReadOnlyList<ReadingEvent> AsReadingEvents() => _data.Sessions
        .Where(session => session.DurationSeconds > 0 && _data.Books.Any(book => book.Id == session.BookId && !book.Archived))
        .SelectMany(session =>
        {
            var book = _data.Books.First(item => item.Id == session.BookId);
            var segments = session.Segments.Count > 0
                ? session.Segments
                : [new ManualReadingSegment { StartedAtUtc = session.StartedAtUtc, EndedAtUtc = session.StartedAtUtc.AddSeconds(session.DurationSeconds) }];
            return segments.Select(segment => new ReadingEvent(session.BookId, session.EndPage ?? 0, segment.StartedAtUtc.ToUnixTimeSeconds(), segment.DurationSeconds, book.TotalPages, "Manual"));
        }).OrderBy(item => item.StartTime).ToArray();

    public IReadOnlyList<ManualReadingSession> Sessions => _data.Sessions;

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

    public async Task<bool> UpdateSessionNoteAsync(string noteId, string note)
    {
        var sessionId = noteId.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) ? noteId[7..] : noteId;
        var session = FindSession(sessionId);
        if (session is null) return false;
        session.Note = note?.Trim() ?? "";
        await SaveAndPublishAsync();
        return true;
    }

    public async Task<string> RefreshBookMetadataAsync(long id)
    {
        var book = FindBook(id);
        if (book is null) return "Physical edition not found.";
        var query = book.Isbn13 ?? book.Isbn10 ?? string.Join(" ", new[] { book.Title, book.Authors }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var results = await _metadata.SearchAsync(query);
        var match = results.FirstOrDefault(result => book.Isbn13 is not null && result.Isbn13 == book.Isbn13)
            ?? results.FirstOrDefault(result => book.Isbn10 is not null && result.Isbn10 == book.Isbn10)
            ?? results.OrderByDescending(result => BookMetadataService.Relevance(result, book.Title + " " + book.Authors)).FirstOrDefault();
        if (match is null) return "No matching metadata was found.";
        book.Authors = string.IsNullOrWhiteSpace(book.Authors) ? match.Authors : book.Authors;
        book.TotalPages ??= match.PageCount;
        book.Publisher ??= match.Publisher;
        book.PublishedDate ??= match.PublishedDate;
        book.Language ??= match.Language;
        book.Isbn10 ??= match.Isbn10;
        book.Isbn13 ??= match.Isbn13;
        book.InfoUrl ??= match.InfoUrl;
        book.ExternalSource ??= match.Source;
        book.ExternalId ??= match.ExternalId;
        book.CoverUrl = await _coverCache.CacheAsync(match.CoverUrl ?? book.CoverUrl);
        await SaveAndPublishAsync();
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == id);
        return "Metadata refreshed; your existing edits were preserved.";
    }

    public int CleanupCoverCache() => _coverCache.RemoveUnreferenced(_data.Books.Select(book => book.CoverUrl));

    public async Task ExportLibraryAsync(string path)
    {
        var export = new ManualReadingData { SchemaVersion = _data.SchemaVersion, Books = _data.Books, Sessions = _data.Sessions, ActiveSession = null };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<(int Books, int Sessions)> ImportLibraryAsync(string path)
    {
        CaptureUndo("Imported physical library");
        _lastImportedBookIds.Clear(); _lastImportedSessionIds.Clear();
        var incoming = await ManualReadingStore.LoadPortableFileAsync(path);
        var idMap = new Dictionary<long, long>();
        var booksAdded = 0;
        foreach (var source in incoming.Books)
        {
            var existing = _data.Books.FirstOrDefault(book => source.Isbn13 is not null && book.Isbn13 == source.Isbn13)
                ?? _data.Books.FirstOrDefault(book => book.Title.Equals(source.Title, StringComparison.CurrentCultureIgnoreCase) && book.Authors.Equals(source.Authors, StringComparison.CurrentCultureIgnoreCase));
            if (existing is null)
            {
                var originalId = source.Id;
                source.Id = ManualReadingStore.NextBookId(_data.Books.Select(book => book.Id));
                _data.Books.Add(source);
                _lastImportedBookIds.Add(source.Id);
                idMap[originalId] = source.Id;
                booksAdded++;
            }
            else idMap[source.Id] = existing.Id;
        }
        var sessionsAdded = 0;
        foreach (var session in incoming.Sessions.Where(session => !_data.Sessions.Any(existing => existing.Id == session.Id)))
        {
            if (!idMap.TryGetValue(session.BookId, out var mappedId)) continue;
            session.BookId = mappedId;
            _data.Sessions.Add(session);
            _lastImportedSessionIds.Add(session.Id);
            sessionsAdded++;
        }
        foreach (var id in idMap.Values.Distinct()) RecalculateBookProgress(id);
        await SaveAndPublishAsync();
        RebuildCollections();
        RaiseImportUndo();
        return (booksAdded, sessionsAdded);
    }

    public async Task<int> ImportCatalogAsync(IEnumerable<CatalogImportBook> incoming)
    {
        CaptureUndo("Imported book catalog");
        _lastImportedBookIds.Clear(); _lastImportedSessionIds.Clear();
        var added = 0;
        foreach (var source in incoming)
        {
            var existing = _data.Books.FirstOrDefault(book => source.Isbn13 is not null && book.Isbn13 == source.Isbn13)
                ?? _data.Books.FirstOrDefault(book => book.Title.Equals(source.Title, StringComparison.CurrentCultureIgnoreCase) && book.Authors.Equals(source.Authors, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null) continue;
            var imported = new ManualBook
            {
                Id = ManualReadingStore.NextBookId(_data.Books.Select(book => book.Id)), Title = source.Title, Authors = source.Authors,
                Isbn13 = source.Isbn13, TotalPages = source.Pages, CompletedAtUtc = source.FinishedAtUtc,
                CurrentPage = source.FinishedAtUtc is not null ? source.Pages : null, ExternalSource = "CSV import"
            };
            _data.Books.Add(imported); _lastImportedBookIds.Add(imported.Id);
            added++;
        }
        await SaveAndPublishAsync();
        RebuildCollections();
        RaiseImportUndo();
        return added;
    }

    private async Task UndoLastImportAsync()
    {
        _data.Sessions.RemoveAll(session => _lastImportedSessionIds.Contains(session.Id));
        _data.Books.RemoveAll(book => _lastImportedBookIds.Contains(book.Id) && !_data.Sessions.Any(session => session.BookId == book.Id));
        _lastImportedBookIds.Clear(); _lastImportedSessionIds.Clear();
        await SaveAndPublishAsync(); RebuildCollections(); RaiseImportUndo(); Status = "The last import was undone.";
    }

    public async Task<string?> SaveHistoricalSessionAsync(string? id, long bookId, DateTimeOffset startedAt, double durationMinutes, int startPage, int endPage, string note)
    {
        var book = _data.Books.FirstOrDefault(item => item.Id == bookId);
        if (book is null) return "Choose a physical book.";
        if (durationMinutes <= 0 || durationMinutes > 24 * 60) return "Duration must be between 1 minute and 24 hours.";
        if (startPage <= 0 || endPage < startPage) return "Enter a valid start and end page.";
        if (book.TotalPages is > 0 && endPage > book.TotalPages) return $"End page cannot exceed {book.TotalPages}.";
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var proposedEnd = startedAt.ToUniversalTime().Add(duration);
        var overlap = _data.Sessions.FirstOrDefault(item => item.Id != id && item.StartedAtUtc < proposedEnd && item.EndedAtUtc > startedAt.ToUniversalTime());
        if (overlap is not null) return $"This overlaps another manual session for {FindBook(overlap.BookId)?.Title ?? "a book"}. Edit the times before saving.";
        var session = id is null ? null : _data.Sessions.FirstOrDefault(item => item.Id == id);
        var previousBookId = session?.BookId;
        if (session is null)
        {
            session = new ManualReadingSession { BookId = bookId };
            _data.Sessions.Add(session);
        }
        session.BookId = bookId;
        session.StartedAtUtc = startedAt.ToUniversalTime();
        session.EndedAtUtc = session.StartedAtUtc.Add(duration);
        session.DurationSeconds = duration.TotalSeconds;
        session.StartPage = startPage;
        session.EndPage = endPage;
        session.PagesRead = ManualReadingRules.CalculatePagesRead(startPage, endPage);
        session.Note = note.Trim();
        session.Segments = [new ManualReadingSegment { StartedAtUtc = session.StartedAtUtc, EndedAtUtc = session.EndedAtUtc }];
        foreach (var affectedBookId in new long?[] { bookId, previousBookId }.Where(value => value is not null).Select(value => value!.Value).Distinct()) RecalculateBookProgress(affectedBookId);
        await SaveAndPublishAsync();
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == bookId);
        Status = id is null ? "Past reading session added." : "Manual session updated.";
        return null;
    }

    private ManualBook? ActiveBook => _data.ActiveSession is null ? null : _data.Books.FirstOrDefault(book => book.Id == _data.ActiveSession.BookId);

    private void BeginAddBook()
    {
        ClearDraft();
        IsAddingBook = true;
        SearchStatus = "Search by title, author, or ISBN, or enter details manually.";
    }

    private void CancelAddBook()
    {
        _editingBookId = null;
        IsAddingBook = false;
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        ClearDraft();
    }

    public void BeginEditBook(ManualBook? book)
    {
        if (book is null || HasActiveSession) return;
        _editingBookId = book.Id;
        IsAddingBook = true;
        SelectedSearchResult = null;
        DraftTitle = book.Title;
        DraftAuthors = book.Authors;
        DraftTotalPages = book.TotalPages?.ToString() ?? "";
        DraftPublisher = book.Publisher ?? "";
        DraftPublishedDate = book.PublishedDate ?? "";
        DraftLanguage = book.Language ?? "";
        DraftGoodreadsUrl = book.GoodreadsUrl ?? "";
        SearchStatus = "Update the metadata for this physical edition.";
        Raise(nameof(IsEditingBook));
        Raise(nameof(BookEditorTitle));
    }

    private async Task SearchBooksAsync()
    {
        IsSearching = true;
        SearchStatus = "Searching Google Books and Open Library…";
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        try
        {
            var results = await _metadata.SearchAsync(SearchQuery);
            foreach (var result in results) SearchResults.Add(result);
            Raise(nameof(HasSearchResults));
            SearchStatus = results.Count == 0 ? "No matching edition found. You can enter the book manually." : $"{results.Count} matching editions · choose the one you own.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SearchStatus = "Book search is unavailable. Check your connection or enter the book manually.";
            _log("Book metadata search error: " + ex);
        }
        finally { IsSearching = false; }
    }

    // The parent supplies the key to the metadata service; this text only controls status wording.
    public string? GoogleApiKeyState { get; set; }

    private void UseManualEntry()
    {
        SelectedSearchResult = null;
        DraftTitle = SearchQuery.Trim();
        DraftAuthors = "";
        DraftTotalPages = "";
        DraftPublisher = "";
        DraftPublishedDate = "";
        DraftLanguage = "";
        DraftError = "";
    }

    private async Task SaveBookAsync()
    {
        DraftError = "";
        var title = DraftTitle.Trim();
        if (title.Length == 0) { DraftError = "Enter a book title."; return; }
        int? pages = null;
        if (!string.IsNullOrWhiteSpace(DraftTotalPages))
        {
            if (!int.TryParse(DraftTotalPages, out var parsed) || parsed <= 0) { DraftError = "Total pages must be a positive whole number."; return; }
            pages = parsed;
        }
        if (!string.IsNullOrWhiteSpace(DraftGoodreadsUrl) && !Uri.TryCreate(DraftGoodreadsUrl, UriKind.Absolute, out _)) { DraftError = "Goodreads link must be a complete web address."; return; }
        var selected = SelectedSearchResult;
        if (_editingBookId is { } editingId)
        {
            var existing = _data.Books.FirstOrDefault(item => item.Id == editingId);
            if (existing is null) { DraftError = "This book is no longer available."; return; }
            existing.Title = title;
            existing.Authors = DraftAuthors.Trim();
            existing.TotalPages = pages;
            existing.Publisher = NullIfEmpty(DraftPublisher);
            existing.PublishedDate = NullIfEmpty(DraftPublishedDate);
            existing.Language = NullIfEmpty(DraftLanguage);
            existing.GoodreadsUrl = NullIfEmpty(DraftGoodreadsUrl);
            if (selected is not null)
            {
                existing.Isbn10 = selected.Isbn10;
                existing.Isbn13 = selected.Isbn13;
                existing.CoverUrl = await _coverCache.CacheAsync(selected.CoverUrl);
                existing.InfoUrl = selected.InfoUrl;
                existing.ExternalSource = selected.Source;
                existing.ExternalId = selected.ExternalId;
            }
            _editingBookId = null;
            await SaveAndPublishAsync();
            RebuildCollections();
            SelectedBook = Books.FirstOrDefault(item => item.Id == existing.Id);
            IsAddingBook = false;
            Raise(nameof(IsEditingBook));
            Raise(nameof(BookEditorTitle));
            Status = $"Updated {existing.Title}.";
            return;
        }
        var duplicate = _data.Books.FirstOrDefault(book =>
            selected is not null && book.ExternalSource == selected.Source && book.ExternalId == selected.ExternalId ||
            selected?.Isbn13 is not null && book.Isbn13 == selected.Isbn13);
        if (duplicate is not null)
        {
            SelectedBook = duplicate;
            IsAddingBook = false;
            Status = "That edition is already in your physical library.";
            return;
        }
        var book = new ManualBook
        {
            Id = ManualReadingStore.NextBookId(_data.Books.Select(item => item.Id)),
            Title = title,
            Authors = DraftAuthors.Trim(),
            TotalPages = pages,
            Publisher = NullIfEmpty(DraftPublisher),
            PublishedDate = NullIfEmpty(DraftPublishedDate),
            Language = NullIfEmpty(DraftLanguage),
            GoodreadsUrl = NullIfEmpty(DraftGoodreadsUrl),
            Isbn10 = selected?.Isbn10,
            Isbn13 = selected?.Isbn13,
            CoverUrl = await _coverCache.CacheAsync(selected?.CoverUrl),
            InfoUrl = selected?.InfoUrl,
            ExternalSource = selected?.Source ?? "Manual entry",
            ExternalId = selected?.ExternalId
        };
        _data.Books.Add(book);
        await SaveAndPublishAsync();
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == book.Id);
        IsAddingBook = false;
        Status = $"Added {book.Title} to your physical library.";
    }

    private async Task StartSessionAsync()
    {
        if (SelectedBook is null || HasActiveSession) return;
        var now = DateTimeOffset.UtcNow;
        _data.ActiveSession = new ActiveManualSession { BookId = SelectedBook.Id, StartedAtUtc = now, LastResumedAtUtc = now, LastUpdatedUtc = now };
        _lastTimerHeartbeatUtc = now;
        await SaveAndPublishAsync(false);
        _timer.Start();
        RaiseTimerState();
        Status = $"Reading {SelectedBook.Title}.";
    }

    private async Task PauseResumeAsync()
    {
        var active = _data.ActiveSession;
        if (active is null) return;
        var now = DateTimeOffset.UtcNow;
        if (active.IsPaused)
        {
            active.IsPaused = false;
            active.LastResumedAtUtc = now;
            _lastTimerHeartbeatUtc = now;
            Status = "Session resumed.";
        }
        else
        {
            CloseRunningSegment(active, now);
            active.IsPaused = true;
            active.LastResumedAtUtc = null;
            Status = "Session paused.";
        }
        active.LastUpdatedUtc = now;
        await SaveAndPublishAsync(false);
        RaiseTimerState();
    }

    private void BeginFinish()
    {
        var book = ActiveBook;
        var active = _data.ActiveSession;
        if (book is null || active is null) return;
        var now = DateTimeOffset.UtcNow;
        if (!active.IsPaused)
        {
            CloseRunningSegment(active, now);
            active.IsPaused = true;
            active.LastResumedAtUtc = null;
            active.LastUpdatedUtc = now;
        }
        _timer.Stop();
        FinishStartPage = ((book.CurrentPage ?? 0) + 1).ToString();
        FinishEndPage = "";
        FinishNote = "";
        FinishError = "";
        IsFinishing = true;
        RaiseTimerState();
        Status = "Timer stopped · confirm the last page you reached.";
        _ = SaveCheckpointAsync();
    }

    private void CancelFinish()
    {
        IsFinishing = false;
        Status = "Session remains paused. Resume when you are ready.";
        RaiseTimerState();
    }

    private async Task SaveFinishedSessionAsync()
    {
        var active = _data.ActiveSession;
        var book = ActiveBook;
        if (active is null || book is null) return;
        FinishError = "";
        if (!int.TryParse(FinishStartPage, out var startPage) || startPage <= 0) { FinishError = "Start page must be a positive whole number."; return; }
        if (!int.TryParse(FinishEndPage, out var endPage) || endPage <= 0) { FinishError = "Enter the last page you reached."; return; }
        if (endPage < startPage) { FinishError = "End page cannot be before the start page."; return; }
        if (book.TotalPages is > 0 && endPage > book.TotalPages) { FinishError = $"End page cannot exceed {book.TotalPages}."; return; }
        var pagesRead = ManualReadingRules.CalculatePagesRead(startPage, endPage);
        var now = DateTimeOffset.UtcNow;
        var elapsed = active.ElapsedSeconds(now);
        if (elapsed < 1) { FinishError = "The session is too short to save."; return; }
        _data.Sessions.Add(new ManualReadingSession
        {
            BookId = book.Id,
            StartedAtUtc = active.StartedAtUtc,
            EndedAtUtc = now,
            DurationSeconds = elapsed,
            StartPage = startPage,
            EndPage = endPage,
            PagesRead = pagesRead,
            Note = FinishNote.Trim(),
            Segments = active.Segments.Select(segment => new ManualReadingSegment { StartedAtUtc = segment.StartedAtUtc, EndedAtUtc = segment.EndedAtUtc }).ToList()
        });
        book.CurrentPage = Math.Max(book.CurrentPage ?? 0, endPage);
        if (book.TotalPages is > 0 && book.CurrentPage >= book.TotalPages && book.CompletedAtUtc is null) book.CompletedAtUtc = now;
        _data.ActiveSession = null;
        _timer.Stop();
        IsFinishing = false;
        await SaveAndPublishAsync();
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == book.Id);
        RaiseTimerState();
        Status = $"Saved {Formatters.Duration(elapsed)} · {pagesRead} pages.";
    }

    private async Task DiscardSessionAsync()
    {
        _data.ActiveSession = null;
        _timer.Stop();
        IsFinishing = false;
        await SaveAndPublishAsync(false);
        RaiseTimerState();
        Status = "Active session discarded.";
    }

    private async Task DeleteSessionAsync(string id)
    {
        var session = _data.Sessions.FirstOrDefault(item => item.Id == id);
        if (session is null) return;
        CaptureUndo("Deleted manual session");
        _lastDeletedSession = CloneSession(session);
        _data.Sessions.Remove(session);
        RecalculateBookProgress(session.BookId);
        await SaveAndPublishAsync();
        RebuildCollections();
        Status = "Manual session deleted.";
        Raise(nameof(CanUndoDeleteSession));
        UndoDeleteSessionCommand.Refresh();
    }

    private async Task UndoDeleteSessionAsync()
    {
        if (_lastDeletedSession is not { } session) return;
        if (_data.Sessions.Any(item => item.Id == session.Id)) { _lastDeletedSession = null; return; }
        _data.Sessions.Add(CloneSession(session));
        _lastDeletedSession = null;
        RecalculateBookProgress(session.BookId);
        await SaveAndPublishAsync();
        RebuildCollections();
        Raise(nameof(CanUndoDeleteSession));
        UndoDeleteSessionCommand.Refresh();
        Status = "Deleted session restored.";
    }

    private async Task DeleteBookAsync(long id)
    {
        if (_data.ActiveSession?.BookId == id) { Status = "Finish or discard the active session before deleting this book."; return; }
        var book = _data.Books.FirstOrDefault(item => item.Id == id);
        if (book is null) return;
        CaptureUndo($"Deleted {book.Title}");
        _lastDeletedBook = CloneBook(book);
        _lastDeletedBookSessions = _data.Sessions.Where(session => session.BookId == id).Select(CloneSession).ToList();
        _data.Sessions.RemoveAll(session => session.BookId == id);
        _data.Books.Remove(book);
        await SaveAndPublishAsync();
        RebuildCollections();
        Status = $"Deleted {book.Title} and its manual sessions.";
        Raise(nameof(CanUndoDeleteBook)); UndoDeleteBookCommand.Refresh();
    }

    private async Task UndoDeleteBookAsync()
    {
        if (_lastDeletedBook is not { } book || _data.Books.Any(item => item.Id == book.Id)) return;
        _data.Books.Add(CloneBook(book));
        foreach (var session in _lastDeletedBookSessions.Where(session => !_data.Sessions.Any(item => item.Id == session.Id))) _data.Sessions.Add(CloneSession(session));
        _lastDeletedBook = null; _lastDeletedBookSessions = [];
        await SaveAndPublishAsync(); RebuildCollections(); Raise(nameof(CanUndoDeleteBook)); UndoDeleteBookCommand.Refresh(); Status = "Deleted book and sessions restored.";
    }

    private void RaiseImportUndo() { Raise(nameof(CanUndoLastImport)); UndoLastImportCommand.Refresh(); }

    private void CaptureUndo(string action)
    {
        try { _store.CaptureUndo(action, _data); RefreshActivityLog(); }
        catch (Exception ex) { _log("Undo journal error: " + ex); }
    }

    private void RefreshActivityLog() { Replace(ActivityLog, _store.ListOperations()); UndoRecentActionCommand.Refresh(); Raise(nameof(HasActivityLog)); }
    public bool HasActivityLog => ActivityLog.Count > 0;

    private async Task UndoRecentActionAsync()
    {
        var restored = _store.RestoreLatestUndo(); if (restored is null) return;
        _data = restored; await _store.SaveAsync(_data); RebuildCollections(); RefreshActivityLog(); DataChanged?.Invoke(this, EventArgs.Empty); Status = "The most recent library change was restored.";
    }

    private static ManualBook CloneBook(ManualBook source) => JsonSerializer.Deserialize<ManualBook>(JsonSerializer.Serialize(source))!;

    private void RecalculateBookProgress(long bookId)
    {
        var book = _data.Books.FirstOrDefault(item => item.Id == bookId);
        if (book is null) return;
        book.CurrentPage = _data.Sessions.Where(item => item.BookId == bookId && item.EndPage is not null).Select(item => item.EndPage).DefaultIfEmpty().Max();
        if (book.CurrentPage is null or 0) book.CurrentPage = null;
        if (book.TotalPages is null || book.CurrentPage < book.TotalPages) book.CompletedAtUtc = null;
    }

    private void BuildDuplicateGroups()
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var groups = _data.Books.Where(book => !book.Archived).GroupBy(book => !string.IsNullOrWhiteSpace(book.Isbn13) ? "isbn:" + Normalize(book.Isbn13) : "book:" + Normalize(book.Title) + ":" + Normalize(book.Authors), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => new DuplicateBookGroup(group.Key, group.First().Title, group.ToArray())).ToArray();
        Replace(DuplicateGroups, groups); SelectedDuplicateGroup = DuplicateGroups.FirstOrDefault(); Raise(nameof(HasDuplicateGroups));
    }

    private async Task ArchiveSelectedBookAsync()
    {
        if (SelectedBook is null) return; CaptureUndo($"Archived {SelectedBook.Title}"); SelectedBook.Archived = true; await SaveAndPublishAsync(); RebuildCollections(); Status = "Book archived; its sessions remain in statistics.";
    }

    private async Task MergeDuplicateBooksAsync()
    {
        if (SelectedDuplicateGroup?.Books.Count < 2) return;
        var group = SelectedDuplicateGroup!; var primary = group.Books.OrderByDescending(book => _data.Sessions.Count(session => session.BookId == book.Id)).ThenBy(book => book.CreatedAtUtc).First();
        CaptureUndo($"Merged duplicate editions of {primary.Title}");
        foreach (var duplicate in group.Books.Where(book => book.Id != primary.Id))
        {
            foreach (var session in _data.Sessions.Where(session => session.BookId == duplicate.Id)) session.BookId = primary.Id;
            primary.TotalPages ??= duplicate.TotalPages; primary.CoverUrl ??= duplicate.CoverUrl; primary.Isbn13 ??= duplicate.Isbn13; primary.Publisher ??= duplicate.Publisher; primary.PublishedDate ??= duplicate.PublishedDate;
            _data.Books.Remove(duplicate);
        }
        RecalculateBookProgress(primary.Id); await SaveAndPublishAsync(); RebuildCollections(); SelectedBook = Books.FirstOrDefault(book => book.Id == primary.Id); Status = "Duplicate editions merged; all sessions were preserved.";
    }

    private void RebuildCollections()
    {
        var selectedId = SelectedBook?.Id;
        Replace(Books, _data.Books.Where(book => !book.Archived).OrderByDescending(book => _data.Sessions.Where(item => item.BookId == book.Id).Select(item => item.StartedAtUtc).DefaultIfEmpty(book.CreatedAtUtc).Max()).ThenBy(book => book.Title));
        BuildDuplicateGroups();
        Replace(RecentSessions, _data.Sessions.OrderByDescending(item => item.StartedAtUtc).Take(250).Select(item =>
        {
            var book = _data.Books.FirstOrDefault(entry => entry.Id == item.BookId);
            return new ManualSessionRow(item.Id, item.BookId, book?.Title ?? "Unknown book", item.StartedAtUtc.ToLocalTime(), item.DurationSeconds, item.StartPage, item.EndPage, item.PagesRead);
        }));
        SelectedBook = Books.FirstOrDefault(book => book.Id == selectedId) ?? Books.FirstOrDefault();
        Raise(nameof(HasBooks));
        Raise(nameof(HasSessions));
        Raise(nameof(LibrarySummary));
        Raise(nameof(SelectedBookProgress));
        Raise(nameof(SelectedBookProgressLabel));
        StartSessionCommand.Refresh();
        ArchiveSelectedBookCommand.Refresh(); MergeDuplicateBooksCommand.Refresh();
    }

    private async Task SaveAndPublishAsync(bool publish = true)
    {
        await _store.SaveAsync(_data);
        if (publish) DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void TimerTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (_data.ActiveSession is { IsPaused: false } active && _lastTimerHeartbeatUtc != default && now - _lastTimerHeartbeatUtc > TimeSpan.FromSeconds(90))
        {
            CloseRunningSegment(active, _lastTimerHeartbeatUtc.AddSeconds(2));
            active.IsPaused = true;
            active.LastResumedAtUtc = null;
            active.LastUpdatedUtc = now;
            Status = "Timer paused after sleep or a long system interruption; inactive time was excluded.";
            RaiseTimerState();
            try { await _store.SaveAsync(_data); } catch (Exception ex) { _log("Manual timer recovery error: " + ex); }
            return;
        }
        _lastTimerHeartbeatUtc = now;
        Raise(nameof(ElapsedLabel));
        if (++_ticksSinceSave < 15 || _data.ActiveSession is null) return;
        _ticksSinceSave = 0;
        _data.ActiveSession.LastUpdatedUtc = now;
        try { await _store.SaveAsync(_data); } catch (Exception ex) { _log("Manual timer checkpoint error: " + ex); }
    }

    private async Task SaveCheckpointAsync()
    {
        try { await _store.SaveAsync(_data); }
        catch (Exception ex) { _log("Manual finish checkpoint error: " + ex); }
    }

    private void RaiseTimerState()
    {
        Raise(nameof(HasActiveSession));
        Raise(nameof(CanSelectBook));
        Raise(nameof(IsSessionPaused));
        Raise(nameof(PauseResumeLabel));
        Raise(nameof(ActiveBookTitle));
        Raise(nameof(ActiveBookAuthors));
        Raise(nameof(ActiveBookCover));
        Raise(nameof(ActiveSessionState));
        Raise(nameof(ElapsedLabel));
        StartSessionCommand.Refresh();
        PauseResumeCommand.Refresh();
        BeginFinishCommand.Refresh();
        SaveFinishedSessionCommand.Refresh();
        DiscardSessionCommand.Refresh();
        EditBookCommand.Refresh();
    }

    private void OpenMetadata()
    {
        if (SelectedSearchResult?.InfoUrl is not { } url) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Could not open the metadata page."; _log("Open metadata error: " + ex); }
    }

    private void ClearDraft()
    {
        SearchQuery = "";
        SelectedSearchResult = null;
        DraftTitle = "";
        DraftAuthors = "";
        DraftTotalPages = "";
        DraftPublisher = "";
        DraftPublishedDate = "";
        DraftLanguage = "";
        DraftGoodreadsUrl = "";
        DraftError = "";
        _editingBookId = null;
        Raise(nameof(IsEditingBook));
        Raise(nameof(BookEditorTitle));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ManualReadingSession CloneSession(ManualReadingSession session) => new()
    {
        Id = session.Id,
        BookId = session.BookId,
        StartedAtUtc = session.StartedAtUtc,
        EndedAtUtc = session.EndedAtUtc,
        DurationSeconds = session.DurationSeconds,
        StartPage = session.StartPage,
        EndPage = session.EndPage,
        PagesRead = session.PagesRead,
        Note = session.Note,
        CreatedAtUtc = session.CreatedAtUtc,
        Segments = session.Segments.Select(segment => new ManualReadingSegment { StartedAtUtc = segment.StartedAtUtc, EndedAtUtc = segment.EndedAtUtc }).ToList()
    };
    private static void CloseRunningSegment(ActiveManualSession active, DateTimeOffset endUtc)
    {
        if (active.LastResumedAtUtc is not { } start || endUtc <= start) return;
        active.Segments.Add(new ManualReadingSegment { StartedAtUtc = start, EndedAtUtc = endUtc });
    }
    private static string FormatTimer(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1 ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    }
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source) { target.Clear(); foreach (var item in source) target.Add(item); }

    public void Dispose()
    {
        _timer.Stop();
        if (_data.ActiveSession is not null)
        {
            _data.ActiveSession.LastUpdatedUtc = DateTimeOffset.UtcNow;
            try { _store.SaveAsync(_data).GetAwaiter().GetResult(); } catch { }
        }
    }
}
