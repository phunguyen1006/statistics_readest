using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class MainViewModel
{
    // SelectedNote's normal setter marks notes read. Explicit rediscovery navigation does not.
    private bool _rediscoveryNavigating;
    private bool _rediscoveryInitializationPending;
    private NoteDiscoveryState? _rediscoveryStateToRestore;
    private NoteDiscoveryState? _unresolvedReadestDiscovery;
    private NoteDiscoveryState? _readestDiscoveryAwaitingReconciliation;
    private bool _readestNotesSnapshotAuthoritative;
    private string? _rediscoveryBookHash;
    private ParameterCommand<NoteRow>? _openRediscoveryHistoryCommand;
    private readonly Dictionary<string, RediscoveryPoolCache> _rediscoveryPoolCache = new(StringComparer.Ordinal);
    private sealed record RediscoveryPoolCache(NoteRow[] Rows, NoteDiscoveryPoolSnapshot Snapshot, DateTimeOffset ValidUntilUtc);

    public int RandomEligibleCount => RediscoveryRows().Length;
    public bool HasRandomNote => RandomNote is not null;
    public string RandomPoolStatus => RandomEligibleCount == 0
        ? RandomNotePool == "Current book" && string.IsNullOrWhiteSpace(_rediscoveryBookHash)
            ? "Choose a note to set the current book, or switch to another pool."
            : "No eligible notes in this pool. Change the pool or allow a snoozed/excluded note."
        : $"{RandomEligibleCount} eligible notes · every note appears once per shuffle cycle.";
    public ParameterCommand<NoteRow> OpenRediscoveryHistoryCommand => _openRediscoveryHistoryCommand ??= new(note =>
    {
        if (note is null || !_noteDiscovery.NavigateTo(note.Id)) return;
        ShowRediscovery(note, markSeen: false);
        PersistRediscoveryState();
        RefreshRediscoveryCommands();
    }, note => note is not null);

    private void RestoreRediscoveryPreferences()
    {
        _settings.NoteDiscovery = NotesDiscoveryPersistence.Normalize(_settings.NoteDiscovery);
        _randomNotePool = _settings.NoteDiscovery.SelectedPool;
        _rediscoveryBookHash = _settings.NoteDiscovery.CurrentBookHash;
        _rediscoveryStateToRestore = _settings.NoteDiscovery;
        _unresolvedReadestDiscovery = NotesDiscoveryPersistence.MissingDigital(_settings.NoteDiscovery, []);
        _readestDiscoveryAwaitingReconciliation = _settings.NoteDiscovery;
        _readestNotesSnapshotAuthoritative = false;
        _rediscoveryPoolCache.Clear();
        _rediscoveryInitializationPending = true;
        Raise(nameof(RandomNotePool));
    }

    private void CompleteRediscoveryInitialization()
    {
        if (!_rediscoveryInitializationPending) return;
        _noteDiscovery.RestoreState(_rediscoveryStateToRestore);
        _rediscoveryStateToRestore = null;
        _rediscoveryInitializationPending = false;
        BuildRediscoveryState();
    }

    /// <summary>Call after a Readest notes load, before rebuilding the combined note rows.</summary>
    private void SetReadestNotesSnapshotAuthority(bool authoritative)
    {
        if (!_rediscoveryInitializationPending)
        {
            // Rehydrate previously unresolved IDs before the next library Update can prune them.
            var combined = CaptureRediscoveryState();
            _noteDiscovery.RestoreState(combined);
            _readestDiscoveryAwaitingReconciliation = authoritative ? null : combined;
            _unresolvedReadestDiscovery = authoritative ? null : NotesDiscoveryPersistence.MissingDigital(combined, []);
        }
        _readestNotesSnapshotAuthoritative = authoritative;
    }

    private void SetRandomPoolV25(string? value)
    {
        var normalized = NoteDiscoveryPools.Normalize(value);
        if (!Set(ref _randomNotePool, normalized, nameof(RandomNotePool))) return;
        _rediscoveryBookHash = normalized == "Current book" ? SelectedNote?.BookHash ?? RandomNote?.BookHash : null;
        _rediscoveryPoolCache.Clear();
        PickRediscovery(markSeen: true);
    }

    private NoteRow[] RediscoveryRows(string? sameBookHash = null) => RediscoveryPool(sameBookHash).Rows;

    private RediscoveryPoolCache RediscoveryPool(string? sameBookHash = null)
    {
        var bookHash = sameBookHash ?? (RandomNotePool == "Current book" ? _rediscoveryBookHash : null);
        var key = RandomNotePool + "|" + (bookHash ?? "");
        var now = DateTimeOffset.UtcNow;
        if (_rediscoveryPoolCache.TryGetValue(key, out var cached) && now < cached.ValidUntilUtc) return cached;
        var eligible = NoteDiscoveryEligibility.Select(_allNoteRows.Select(note => new NoteDiscoveryCandidate(
            note.Id, note.BookHash, note.IsFavorite, note.TimesSeen, note.LastSeenUtc,
            note.State.SnoozedUntilUtc, note.ExcludeFromRandom, note.IsManual, note.State.IsHidden)),
            RandomNotePool, now, bookHash).Select(note => note.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = _allNoteRows.Where(note => eligible.Contains(note.Id)).ToArray();
        var until = DateTimeOffset.MaxValue;
        foreach (var note in _allNoteRows)
        {
            if (note.State.SnoozedUntilUtc is { } snoozed && snoozed > now && snoozed < until) until = snoozed;
            if (RandomNotePool == "Not seen recently" && note.LastSeenUtc is { } seen && seen <= DateTimeOffset.MaxValue.AddDays(-30))
            {
                var boundary = seen.AddDays(30);
                if (boundary >= now && boundary < until) until = boundary;
            }
        }
        cached = new(rows, new NoteDiscoveryPoolSnapshot(rows.Select(note => note.Id)), until);
        // Keep small same-book caches without retaining every book visited in a long app session.
        if (_rediscoveryPoolCache.Count >= 16) _rediscoveryPoolCache.Clear();
        _rediscoveryPoolCache[key] = cached;
        return cached;
    }

    private void OnRediscoveryNoteStateChanged(string? propertyName)
    {
        if (propertyName is nameof(NoteRow.ExcludeFromRandom) or nameof(NoteRow.IsSnoozed)
            || propertyName == nameof(NoteRow.IsFavorite) && RandomNotePool == "Favorites"
            || propertyName == nameof(NoteRow.TimesSeen) && RandomNotePool == "Unseen"
            || propertyName == nameof(NoteRow.LastSeenUtc) && RandomNotePool == "Not seen recently")
            _rediscoveryPoolCache.Clear();
        RefreshRediscoveryCommands();
    }

    /// <summary>Reconcile source refreshes without marking a restored preview as newly read.</summary>
    private void BuildRediscoveryState()
    {
        _rediscoveryPoolCache.Clear();
        if (_rediscoveryInitializationPending)
        {
            // Manual-library initialization may arrive before Readest's first note snapshot.
            RandomNote = _allNoteRows.FirstOrDefault(note => note.Id.Equals(_rediscoveryStateToRestore?.CurrentId, StringComparison.OrdinalIgnoreCase));
            _lastRandomNoteId = RandomNote?.Id;
            RefreshRediscoveryCommands();
            return;
        }
        if (_readestNotesSnapshotAuthoritative)
        {
            _unresolvedReadestDiscovery = null;
            _readestDiscoveryAwaitingReconciliation = null;
        }
        else
        {
            _unresolvedReadestDiscovery = NotesDiscoveryPersistence.MissingDigital(
                _readestDiscoveryAwaitingReconciliation ?? _unresolvedReadestDiscovery, _allNoteRows.Select(note => note.Id));
            _readestDiscoveryAwaitingReconciliation = null;
        }
        _noteDiscovery.Update(_allNoteRows.Select(note => KeyValuePair.Create(note.Id, note.BookHash)));
        var currentId = _noteDiscovery.CurrentId;
        var current = RediscoveryRows().FirstOrDefault(note => note.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase));
        if (current is null) PickRediscovery(markSeen: false, selectNote: false);
        else { RandomNote = current; _lastRandomNoteId = current.Id; }
        RebuildRediscoveryHistory();
        PersistRediscoveryState();
        RefreshRediscoveryCommands();
    }

    private void SelectRandomNoteV25() => PickRediscovery(markSeen: true);

    private void SelectRandomSameBookV25()
    {
        if (SelectedNote is null) return;
        PickRediscovery(markSeen: true, sameBookHash: SelectedNote.BookHash);
    }

    private void PickRediscovery(bool markSeen, string? sameBookHash = null, bool selectNote = true)
    {
        var pool = RediscoveryPool(sameBookHash);
        var eligible = pool.Rows;
        var bookHash = sameBookHash ?? (RandomNotePool == "Current book" ? _rediscoveryBookHash : null);
        var scopeKey = RandomNotePool + "|" + (bookHash ?? "");
        var id = _noteDiscovery.NextFromSnapshot(_lastRandomNoteId, pool.Snapshot, scopeKey);
        var note = id is null ? null : eligible.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (note is null)
        {
            RandomNote = null;
            _lastRandomNoteId = null;
            NotesStatus = "No eligible notes in this pool. Change the pool or allow a snoozed/excluded note.";
        }
        else if (selectNote) ShowRediscovery(note, markSeen);
        else { RandomNote = note; _lastRandomNoteId = note.Id; }
        if (note is not null && NotesStatus.StartsWith("No eligible notes in this pool.", StringComparison.Ordinal))
            NotesStatus = V24Text("Random note ready. Back/history navigation leaves seen counts unchanged.", "Ghi chú ngẫu nhiên đã sẵn sàng. Xem lại lịch sử không tăng số lần đã xem.");
        RebuildRediscoveryHistory();
        PersistRediscoveryState();
        RefreshRediscoveryCommands();
    }

    private void SelectPreviousRandomNoteV25()
    {
        var id = _noteDiscovery.Previous();
        var note = id is null ? null : _allNoteRows.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (note is not null) ShowRediscovery(note, markSeen: false);
        PersistRediscoveryState();
        RefreshRediscoveryCommands();
    }

    private void ShowRediscovery(NoteRow note, bool markSeen)
    {
        RandomNote = note;
        _lastRandomNoteId = note.Id;
        if (markSeen && ReferenceEquals(SelectedNote, note))
        {
            note.MarkSeen();
            Raise(nameof(NotesReadCount));
            ScheduleNoteSave(note);
        }
        else
        {
            _rediscoveryNavigating = !markSeen;
            try { SelectedNote = note; }
            finally { _rediscoveryNavigating = false; }
        }
    }

    private void RebuildRediscoveryHistory()
    {
        var notesById = _allNoteRows.GroupBy(note => note.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Replace(RandomNoteHistory, _noteDiscovery.RecentIds.Where(notesById.ContainsKey).Select(id => notesById[id]));
    }

    private void PersistRediscoveryState()
    {
        _settings.NoteDiscovery = CaptureRediscoveryState();
        SchedulePreferenceSave();
    }

    private NoteDiscoveryState CaptureRediscoveryState()
    {
        var live = _noteDiscovery.CaptureState(RandomNotePool);
        live.CurrentBookHash = _rediscoveryBookHash;
        return _readestNotesSnapshotAuthoritative ? live : NotesDiscoveryPersistence.MergeUnresolvedDigital(live, _unresolvedReadestDiscovery);
    }

    private bool CanPickRandomNoteV25() => !_rediscoveryInitializationPending && RandomEligibleCount > 0;
    private bool CanPickRandomSameBookV25() => !_rediscoveryInitializationPending && SelectedNote is not null && RediscoveryRows(SelectedNote.BookHash).Any(note => !note.Id.Equals(SelectedNote.Id, StringComparison.OrdinalIgnoreCase));
    private void RefreshRediscoveryCommands()
    {
        Raise(nameof(RandomEligibleCount));
        Raise(nameof(RandomPoolStatus));
        Raise(nameof(HasRandomNote));
        RandomNoteCommand.Refresh();
        RandomSameBookCommand.Refresh();
        PreviousRandomNoteCommand.Refresh();
    }
}
