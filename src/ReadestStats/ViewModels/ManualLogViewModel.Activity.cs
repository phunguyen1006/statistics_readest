using System.Collections.ObjectModel;
using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class ManualLogViewModel
{
    private readonly List<long> _lastImportedBookIds = [];
    private readonly List<string> _lastImportedSessionIds = [];
    private ManualBook? _lastDeletedBook;
    private List<ManualReadingSession> _lastDeletedBookSessions = [];

    public ObservableCollection<OperationJournalEntry> ActivityLog { get; } = new RangeObservableCollection<OperationJournalEntry>();
    public bool HasActivityLog => ActivityLog.Count > 0;
    public bool CanUndoLastImport => _lastImportedBookIds.Count > 0 || _lastImportedSessionIds.Count > 0;
    public bool CanUndoDeleteSession => _lastDeletedSession is not null;
    public bool CanUndoDeleteBook => _lastDeletedBook is not null;

    private void RaiseImportUndo()
    {
        Raise(nameof(CanUndoLastImport));
        UndoLastImportCommand.Refresh();
    }

    private void CaptureUndo(string action)
    {
        EnsureCanMutate();
        var snapshot = JsonSerializer.Deserialize<ManualReadingData>(JsonSerializer.Serialize(_data))!;
        snapshot.RecoveryReferences = CaptureReferences?.Invoke();
        _store.CaptureUndo(action, snapshot);
        RefreshActivityLog();
    }

    private void RefreshActivityLog()
    {
        Replace(ActivityLog, _store.ListOperations());
        UndoRecentActionCommand.Refresh();
        Raise(nameof(HasActivityLog));
    }

    private async Task UndoRecentActionAsync()
    {
        using var mutation = BeginMutationOperation();
        var restored = _store.RestoreLatestUndo();
        if (restored is null) return;
        _data = restored;
        await RecoverReferencesAsync();
        await SaveStorageAsync();
        RebuildCollections();
        RefreshActivityLog();
        DataChanged?.Invoke(this, EventArgs.Empty);
        Status = "The most recent library change was restored.";
    }

    private async Task RecoverReferencesAsync()
    {
        if (_data.RecoveryReferences is not { } references || RestoreReferences is null) return;
        await RestoreReferences(references);
        _data.RecoveryReferences = null;
    }

    private async Task UndoLastImportAsync()
    {
        using var mutation = BeginMutationOperation();
        _data.Sessions.RemoveAll(session => _lastImportedSessionIds.Contains(session.Id));
        _data.Books.RemoveAll(book => _lastImportedBookIds.Contains(book.Id) && !_data.Sessions.Any(session => session.BookId == book.Id));
        _lastImportedBookIds.Clear();
        _lastImportedSessionIds.Clear();
        await SaveAndPublishAsync();
        RebuildCollections();
        RaiseImportUndo();
        Status = "The last import was undone.";
    }

    public ManualReadingData CapturePortableLibrary()
    {
        EnsureCanMutate();
        var export = new ManualReadingData { SchemaVersion = _data.SchemaVersion, Books = _data.Books, Sessions = _data.Sessions, ActiveSession = null };
        return JsonSerializer.Deserialize<ManualReadingData>(JsonSerializer.Serialize(export))!;
    }

    public async Task<(int Books, int Sessions)> ImportLibraryAsync(string path)
    {
        using var mutation = BeginMutationOperation();
        CaptureUndo("Imported physical library");
        _lastImportedBookIds.Clear();
        _lastImportedSessionIds.Clear();
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
        using var mutation = BeginMutationOperation();
        CaptureUndo("Imported book catalog");
        _lastImportedBookIds.Clear();
        _lastImportedSessionIds.Clear();
        var added = 0;
        foreach (var source in incoming)
        {
            var existing = _data.Books.FirstOrDefault(book => source.Isbn13 is not null && book.Isbn13 == source.Isbn13)
                ?? _data.Books.FirstOrDefault(book => book.Title.Equals(source.Title, StringComparison.CurrentCultureIgnoreCase) && book.Authors.Equals(source.Authors, StringComparison.CurrentCultureIgnoreCase));
            if (existing is not null) continue;
            var imported = new ManualBook
            {
                Id = ManualReadingStore.NextBookId(_data.Books.Select(book => book.Id)),
                Title = source.Title,
                Authors = source.Authors,
                Isbn13 = source.Isbn13,
                TotalPages = source.Pages,
                CompletedAtUtc = source.FinishedAtUtc,
                CurrentPage = source.FinishedAtUtc is not null ? source.Pages : null,
                ExternalSource = "CSV import"
            };
            _data.Books.Add(imported);
            _lastImportedBookIds.Add(imported.Id);
            added++;
        }
        await SaveAndPublishAsync();
        RebuildCollections();
        RaiseImportUndo();
        return added;
    }
}
