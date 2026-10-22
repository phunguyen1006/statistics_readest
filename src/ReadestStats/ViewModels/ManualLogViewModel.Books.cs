using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Collections.ObjectModel;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class ManualLogViewModel
{
    private ManualBook? _selectedBook;
    private BookMetadataResult? _selectedSearchResult;
    private DuplicateBookGroup? _selectedDuplicateGroup;
    private ManualBook? _selectedArchivedBook;
    private ManualBook? _mergePrimary;
    private string _searchQuery = "";
    private string _searchStatus = "Search Open Library for book details, or enter them manually.";
    private bool _isAddingBook;
    private bool _isSearching;
    private long? _editingBookId;
    private string _draftTitle = "";
    private string _draftAuthors = "";
    private string _draftTotalPages = "";
    private string _draftPublisher = "";
    private string _draftPublishedDate = "";
    private string _draftLanguage = "";
    private string _draftCoverPath = "";
    private string _draftGoodreadsUrl = "";
    private string _draftError = "";
    private CancellationTokenSource? _coverSearchCancellation;
    private bool _applyingMetadata;
    private readonly HashSet<string> _editedDraftFields = [];

    public ObservableCollection<ManualBook> Books { get; } = new RangeObservableCollection<ManualBook>();
    public ObservableCollection<ManualBook> ArchivedBooks { get; } = new RangeObservableCollection<ManualBook>();
    public ObservableCollection<DuplicateBookGroup> DuplicateGroups { get; } = new RangeObservableCollection<DuplicateBookGroup>();
    public ObservableCollection<BookMetadataResult> SearchResults { get; } = new RangeObservableCollection<BookMetadataResult>();

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
            if (!Set(ref _selectedSearchResult, value)) return;
            Raise(nameof(EditionComparison));
            if (value is null) return;
            _applyingMetadata = true;
            try
            {
                if (!_editedDraftFields.Contains(nameof(DraftTitle))) DraftTitle = value.Title;
                if (!_editedDraftFields.Contains(nameof(DraftAuthors))) DraftAuthors = value.Authors;
                if (!_editedDraftFields.Contains(nameof(DraftTotalPages))) DraftTotalPages = value.PageCount?.ToString() ?? "";
                if (!_editedDraftFields.Contains(nameof(DraftPublisher))) DraftPublisher = value.Publisher ?? "";
                if (!_editedDraftFields.Contains(nameof(DraftPublishedDate))) DraftPublishedDate = value.PublishedDate ?? "";
                if (!_editedDraftFields.Contains(nameof(DraftLanguage))) DraftLanguage = value.Language ?? "";
            }
            finally { _applyingMetadata = false; }
            OpenMetadataCommand.Refresh();
        }
    }

    public DuplicateBookGroup? SelectedDuplicateGroup
    {
        get => _selectedDuplicateGroup;
        set
        {
            if (!Set(ref _selectedDuplicateGroup, value)) return;
            MergePrimary = value?.Books.OrderByDescending(book => _data.Sessions.Count(s => s.BookId == book.Id)).FirstOrDefault();
            Raise(nameof(MergePreview));
            MergeDuplicateBooksCommand.Refresh();
        }
    }

    public ManualBook? SelectedArchivedBook
    {
        get => _selectedArchivedBook;
        set
        {
            if (Set(ref _selectedArchivedBook, value))
                RestoreArchivedBookCommand.Refresh();
        }
    }

    public bool HasArchivedBooks => ArchivedBooks.Count > 0;
    public bool HasBooks => Books.Count > 0;
    public double SelectedBookProgress => SelectedBook?.ProgressPercent ?? 0;
    public string SelectedBookProgressLabel => SelectedBook is null ? "Select a book" : SelectedBook.TotalPages is > 0 ? $"{SelectedBook.CurrentPage ?? 0} / {SelectedBook.TotalPages} pages · {SelectedBook.ProgressPercent:0}%" : $"Page {SelectedBook.CurrentPage ?? 0} · total pages unknown";

    public ManualBook? MergePrimary { get => _mergePrimary; set { if (Set(ref _mergePrimary, value)) Raise(nameof(MergePreview)); } }
    public string MergePreview => SelectedDuplicateGroup is null || MergePrimary is null ? "Choose duplicate editions to review." :
        $"Keep: {MergePrimary.Title} · {MergePrimary.Authors}\nMove {_data.Sessions.Count(s => SelectedDuplicateGroup.Books.Any(b => b.Id == s.BookId && b.Id != MergePrimary.Id))} sessions from {SelectedDuplicateGroup.Books.Count - 1} other edition(s). Existing metadata is kept; missing page count, cover, ISBN, publisher and publication date are filled. Plans, pins and edition links follow the retained book. Undo is available in Activity log.";
    public bool HasDuplicateGroups => DuplicateGroups.Count > 0;

    public string SearchQuery { get => _searchQuery; set { if (Set(ref _searchQuery, value)) SearchBooksCommand.Refresh(); } }
    public string SearchStatus { get => _searchStatus; private set => Set(ref _searchStatus, value); }
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

    public string DraftTitle { get => _draftTitle; set { if (Set(ref _draftTitle, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftTitle)); } }
    public string DraftAuthors { get => _draftAuthors; set { if (Set(ref _draftAuthors, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftAuthors)); } }
    public string DraftTotalPages { get => _draftTotalPages; set { if (Set(ref _draftTotalPages, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftTotalPages)); } }
    public string DraftPublisher { get => _draftPublisher; set { if (Set(ref _draftPublisher, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftPublisher)); } }
    public string DraftPublishedDate { get => _draftPublishedDate; set { if (Set(ref _draftPublishedDate, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftPublishedDate)); } }
    public string DraftLanguage { get => _draftLanguage; set { if (Set(ref _draftLanguage, value) && !_applyingMetadata) _editedDraftFields.Add(nameof(DraftLanguage)); } }
    public string DraftCoverPath { get => _draftCoverPath; set => Set(ref _draftCoverPath, value); }
    public string EditionComparison => SelectedSearchResult is null ? Localization.Localizer.Instance.Translate("Select an edition to compare ISBN, publisher, date and pages.") :
        Localization.Localizer.Instance.IsVietnamese
            ? $"{SelectedSearchResult.Source} · ISBN {SelectedSearchResult.Isbn13 ?? SelectedSearchResult.Isbn10 ?? "không rõ"} · {SelectedSearchResult.Publisher ?? "không rõ nhà xuất bản"} · {SelectedSearchResult.PublishedDate ?? "không rõ năm"} · {SelectedSearchResult.PageCount?.ToString() ?? "?"} trang"
            : $"{SelectedSearchResult.Source} · ISBN {SelectedSearchResult.Isbn13 ?? SelectedSearchResult.Isbn10 ?? "unknown"} · {SelectedSearchResult.Publisher ?? "publisher unknown"} · {SelectedSearchResult.PublishedDate ?? "date unknown"} · {SelectedSearchResult.PageCount?.ToString() ?? "?"} pages";
    public string DraftGoodreadsUrl { get => _draftGoodreadsUrl; set => Set(ref _draftGoodreadsUrl, value); }
    public string DraftError { get => _draftError; private set => Set(ref _draftError, value); }

    public string? GoogleApiKeyState { get; set; }

    private void BeginAddBook()
    {
        EnsureCanMutate();
        ClearDraft();
        IsAddingBook = true;
        SearchStatus = "Search by title, author, or ISBN, or enter details manually.";
    }

    private void CancelAddBook()
    {
        EnsureCanMutate();
        _editingBookId = null;
        IsAddingBook = false;
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        ClearDraft();
    }

    public void BeginEditBook(ManualBook? book)
    {
        EnsureCanMutate();
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
        DraftCoverPath = book.CoverUrl ?? "";
        SearchStatus = "Update the metadata for this physical edition.";
        Raise(nameof(IsEditingBook));
        Raise(nameof(BookEditorTitle));
    }

    private void UseManualEntry()
    {
        EnsureCanMutate();
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
        using var mutation = BeginMutationOperation();
        DraftError = "";
        var title = DraftTitle.Trim();
        if (title.Length == 0) { DraftError = "Enter a book title."; return; }
        int? pages = null;
        if (!string.IsNullOrWhiteSpace(DraftTotalPages))
        {
            if (!int.TryParse(DraftTotalPages, out var parsed) || parsed <= 0) { DraftError = "Total pages must be a positive whole number."; return; }
            pages = parsed;
        }
        if (!string.IsNullOrWhiteSpace(DraftGoodreadsUrl) && !ExternalLinkPolicy.TryGetWebUri(DraftGoodreadsUrl, out _)) { DraftError = "Goodreads link must be an HTTP or HTTPS web address."; return; }
        var selected = SelectedSearchResult;
        string? customCover = null;
        if (!string.IsNullOrWhiteSpace(DraftCoverPath) && (_editingBookId is null || DraftCoverPath != _data.Books.FirstOrDefault(book => book.Id == _editingBookId)?.CoverUrl))
        {
            try { customCover = await _coverCache.ImportLocalAsync(DraftCoverPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { DraftError = "Could not use that cover. Choose a valid JPG, PNG or WebP under 15 MB."; _log("Cover import error: " + ex); return; }
        }
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
                if (string.IsNullOrWhiteSpace(existing.CoverUrl)) existing.CoverUrl = await _coverCache.CacheAsync(selected.CoverUrl);
                existing.InfoUrl = selected.InfoUrl;
                existing.ExternalSource = selected.Source;
                existing.ExternalId = selected.ExternalId;
            }
            if (customCover is not null) existing.CoverUrl = customCover;
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
            CoverUrl = customCover ?? await _coverCache.CacheAsync(selected?.CoverUrl),
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

    private void ClearDraft()
    {
        _editedDraftFields.Clear();
        DraftTitle = "";
        DraftAuthors = "";
        DraftTotalPages = "";
        DraftPublisher = "";
        DraftPublishedDate = "";
        DraftLanguage = "";
        DraftGoodreadsUrl = "";
        DraftCoverPath = "";
        DraftError = "";
        SelectedSearchResult = null;
    }

    private async Task SearchBooksAsync()
    {
        EnsureCanMutate();
        var epoch = _storageEpoch;
        _coverSearchCancellation?.Cancel();
        _coverSearchCancellation?.Dispose();
        var searchCancellation = new CancellationTokenSource();
        _coverSearchCancellation = searchCancellation;
        IsSearching = true;
        SearchStatus = "Searching Google Books and Open Library…";
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        try
        {
            var results = await _metadata.SearchAsync(SearchQuery, searchCancellation.Token);
            if (searchCancellation.IsCancellationRequested || _suspendedForRestore || epoch != _storageEpoch) return;
            foreach (var result in results) SearchResults.Add(result);
            Raise(nameof(HasSearchResults));
            SearchStatus = results.Count == 0 ? "No matching edition found. You can enter the book manually." : $"{results.Count} matching editions · choose the one you own.";
            _ = LoadSearchCoversAsync(results, searchCancellation.Token);
        }
        catch (OperationCanceledException) when (searchCancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SearchStatus = "Book search is unavailable. Check your connection or enter the book manually.";
            _log("Book metadata search error: " + ex);
        }
        finally { if (ReferenceEquals(_coverSearchCancellation, searchCancellation)) IsSearching = false; }
    }

    private async Task LoadSearchCoversAsync(IReadOnlyList<BookMetadataResult> results, CancellationToken token)
    {
        var epoch = _storageEpoch;
        using var slots = new SemaphoreSlim(4);
        try
        {
            await Task.WhenAll(results.Where(result => result.CoverUrl is not null).Select(async result =>
            {
                await slots.WaitAsync(token);
                try
                {
                    var path = await _coverCache.CacheAsync(result.CoverUrl, token);
                    if (path is null || token.IsCancellationRequested || _suspendedForRestore || epoch != _storageEpoch) return;
                    var index = SearchResults.IndexOf(result);
                    if (index >= 0)
                    {
                        var wasSelected = SelectedSearchResult == result;
                        var cached = result with { CachedCoverPath = path };
                        SearchResults[index] = cached;
                        if (wasSelected) SelectedSearchResult = cached;
                    }
                }
                finally { slots.Release(); }
            }));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log("Book cover preview error: " + ex); }
    }

    private void OpenMetadata()
    {
        if (SelectedSearchResult is not { InfoUrl: { } url } || !ExternalLinkPolicy.TryGetWebUri(url, out var uri)) return;
        ExternalLinkPolicy.Open(uri);
    }

    public async Task<string> RefreshBookMetadataAsync(long id)
    {
        using var mutation = BeginMutationOperation();
        var book = FindBook(id);
        if (book is null) return "Physical edition not found.";
        var query = book.Isbn13 ?? book.Isbn10 ?? string.Join(" ", new[] { book.Title, book.Authors }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var results = await _metadata.SearchAsync(query);
        var match = results.FirstOrDefault(result => book.Isbn13 is not null && BookMetadataService.NormalizeIsbn(result.Isbn13) == BookMetadataService.NormalizeIsbn(book.Isbn13))
            ?? results.FirstOrDefault(result => book.Isbn10 is not null && BookMetadataService.NormalizeIsbn(result.Isbn10) == BookMetadataService.NormalizeIsbn(book.Isbn10))
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
        if (string.IsNullOrWhiteSpace(book.CoverUrl) || !File.Exists(book.CoverUrl))
            book.CoverUrl = await _coverCache.CacheAsync(match.CoverUrl);
        await SaveAndPublishAsync();
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == id);
        return "Metadata refreshed; your existing edits were preserved.";
    }

    public int CleanupCoverCache() { EnsureCanMutate(); return _coverCache.RemoveUnreferenced(_data.Books.Select(book => book.CoverUrl)); }

    private async Task DeleteBookAsync(long id)
    {
        using var mutation = BeginMutationOperation();
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
        using var mutation = BeginMutationOperation();
        if (_lastDeletedBook is not { } book || _data.Books.Any(item => item.Id == book.Id)) return;
        _data.Books.Add(CloneBook(book));
        foreach (var session in _lastDeletedBookSessions.Where(session => !_data.Sessions.Any(item => item.Id == session.Id))) _data.Sessions.Add(CloneSession(session));
        _lastDeletedBook = null; _lastDeletedBookSessions = [];
        await SaveAndPublishAsync(); RebuildCollections(); Raise(nameof(CanUndoDeleteBook)); UndoDeleteBookCommand.Refresh(); Status = "Deleted book and sessions restored.";
    }

    private async Task ArchiveSelectedBookAsync()
    {
        using var mutation = BeginMutationOperation();
        if (SelectedBook is null || HasActiveSession) return; CaptureUndo($"Archived {SelectedBook.Title}"); SelectedBook.Archived = true; await SaveAndPublishAsync(); RebuildCollections(); Status = "Book archived; its sessions remain in statistics.";
    }

    private async Task RestoreArchivedBookAsync()
    {
        using var mutation = BeginMutationOperation();
        if (SelectedArchivedBook is not { } book || HasActiveSession) return;
        CaptureUndo($"Restored {book.Title}"); book.Archived = false;
        await SaveAndPublishAsync(); RebuildCollections(); SelectedBook = Books.FirstOrDefault(b => b.Id == book.Id);
        Status = "Book restored to the physical library.";
    }

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

    private async Task MergeDuplicateBooksAsync()
    {
        using var mutation = BeginMutationOperation();
        if (SelectedDuplicateGroup is not { Books.Count: >= 2 } group || MergePrimary is not { } primary || HasActiveSession || !group.Books.Contains(primary)) return;
        if (!_confirmMerge(MergePreview)) return;
        CaptureUndo($"Merged duplicate editions of {primary.Title}");
        foreach (var duplicate in group.Books.Where(book => book.Id != primary.Id))
        {
            foreach (var session in _data.Sessions.Where(session => session.BookId == duplicate.Id)) session.BookId = primary.Id;
            primary.TotalPages ??= duplicate.TotalPages; primary.CoverUrl ??= duplicate.CoverUrl; primary.Isbn13 ??= duplicate.Isbn13; primary.Publisher ??= duplicate.Publisher; primary.PublishedDate ??= duplicate.PublishedDate;
            _data.Books.Remove(duplicate);
        }
        try
        {
            if (MergeReferences is not null) await MergeReferences(primary.Id, group.Books.Where(b => b.Id != primary.Id).Select(b => b.Id).ToArray());
            RecalculateBookProgress(primary.Id); await SaveAndPublishAsync(); RebuildCollections(); SelectedBook = Books.FirstOrDefault(book => book.Id == primary.Id); Status = "Duplicate editions merged; all sessions were preserved.";
        }
        catch { await UndoRecentActionAsync(); throw; }
    }

    private static ManualBook CloneBook(ManualBook source) => JsonSerializer.Deserialize<ManualBook>(JsonSerializer.Serialize(source))!;
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
