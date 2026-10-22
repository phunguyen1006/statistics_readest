using System.Collections.ObjectModel;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class ManualLogViewModel
{
    private string _journalSearch = "";
    private DateTime? _journalFrom;
    private DateTime? _journalThrough;
    private JournalDatePreset? _selectedJournalPreset;
    private ManualSessionRow? _selectedJournalSession;
    private ManualJournalBookOption? _selectedJournalBook;
    private ManualReadingSession? _lastDeletedSession;
    private bool _applyingJournalPreset;

    public IReadOnlyList<JournalDatePreset> JournalDatePresets { get; } =
    [
        new("all", "All time", "Toàn bộ"), new("today", "Today", "Hôm nay"),
        new("seven", "Last 7 days", "7 ngày gần đây"), new("month", "This month", "Tháng này"),
        new("custom", "Custom dates", "Ngày tùy chọn")
    ];

    public ObservableCollection<SessionSegmentRow> SelectedSessionSegments { get; } = new RangeObservableCollection<SessionSegmentRow>();
    public ObservableCollection<ManualSessionRow> RecentSessions { get; } = new RangeObservableCollection<ManualSessionRow>();
    public ObservableCollection<ManualJournalBookOption> JournalBooks { get; } = new RangeObservableCollection<ManualJournalBookOption>();

    public IReadOnlyList<ManualReadingSession> FilteredJournalSessions
    {
        get
        {
            var sessionsById = _data.Sessions.GroupBy(session => session.Id).ToDictionary(group => group.Key, group => group.First());
            return RecentSessions.Where(row => sessionsById.ContainsKey(row.Id)).Select(row => sessionsById[row.Id]).ToArray();
        }
    }

    public string JournalSearch { get => _journalSearch; set { if (Set(ref _journalSearch, value)) RebuildJournal(); } }
    public DateTime? JournalFrom { get => _journalFrom; set { if (Set(ref _journalFrom, value)) { MarkJournalDatesCustom(); RebuildJournal(); } } }
    public DateTime? JournalThrough { get => _journalThrough; set { if (Set(ref _journalThrough, value)) { MarkJournalDatesCustom(); RebuildJournal(); } } }
    public JournalDatePreset? SelectedJournalPreset { get => _selectedJournalPreset; set { if (Set(ref _selectedJournalPreset, value) && value is not null) ApplyJournalPreset(value.Key); } }
    public ManualSessionRow? SelectedJournalSession { get => _selectedJournalSession; set { if (Set(ref _selectedJournalSession, value)) RefreshSelectedSession(); } }
    public ManualJournalBookOption? SelectedJournalBook { get => _selectedJournalBook; set { if (Set(ref _selectedJournalBook, value)) RebuildJournal(); } }

    public string JournalTitle => Text("Your full reading journal", "Nhật ký đọc đầy đủ");
    public string JournalEyebrow => Text("MANUAL SESSION JOURNAL", "NHẬT KÝ PHIÊN ĐỌC THỦ CÔNG");
    public string JournalSearchLabel => Text("FIND TITLE, AUTHOR, OR SESSION NOTE", "TÌM TÊN SÁCH, TÁC GIẢ HOẶC GHI CHÚ");
    public string JournalBookLabel => Text("BOOK", "SÁCH");
    public string JournalFromLabel => Text("FROM DATE", "TỪ NGÀY");
    public string JournalThroughLabel => Text("THROUGH DATE", "ĐẾN NGÀY");
    public string JournalPresetLabel => Text("QUICK DATE RANGE", "KHOẢNG NGÀY NHANH");
    public string ClearJournalFiltersLabel => Text("Clear filters", "Xóa bộ lọc");
    public string JournalSummary => JournalFrom?.Date > JournalThrough?.Date
        ? Text("From date must be on or before the end date.", "Ngày bắt đầu phải bằng hoặc trước ngày kết thúc.")
        : Text($"{RecentSessions.Count} of {_data.Sessions.Count} sessions · {Formatters.Duration(RecentSessions.Sum(row => row.DurationSeconds))} shown",
            $"{RecentSessions.Count} / {_data.Sessions.Count} phiên · hiển thị {Formatters.Duration(RecentSessions.Sum(row => row.DurationSeconds))}");
    public string JournalEmptyMessage => _data.Sessions.Count == 0
        ? Text("Start the timer or add a past session to begin your journal.", "Bắt đầu bấm giờ hoặc thêm phiên trước đây để tạo nhật ký.")
        : Text("No session matches these filters. Clear the filters to see the full journal.", "Không có phiên phù hợp. Xóa bộ lọc để xem toàn bộ nhật ký.");

    private ManualReadingSession? SelectedSavedSession => SelectedJournalSession is { } row ? FindSession(row.Id) : null;
    public bool HasSelectedJournalSession => SelectedSavedSession is not null;
    public string SelectedSessionHeading => SelectedJournalSession?.BookTitle ?? "";
    public string SelectedSessionDetailLabel => Text("SESSION DETAILS · READ ONLY", "CHI TIẾT PHIÊN · CHỈ ĐỌC");
    public string SessionStartLabel => Text("STARTED", "BẮT ĐẦU");
    public string SessionEndLabel => Text("ENDED", "KẾT THÚC");
    public string SessionActiveLabel => Text("ACTUAL READING", "THỜI GIAN ĐỌC THỰC");
    public string SessionElapsedLabel => Text("ELAPSED SPAN", "KHOẢNG THỜI GIAN");
    public string SessionPauseLabel => Text("PAUSE GAPS", "THỜI GIAN TẠM DỪNG");
    public string SessionSegmentsLabel => Text("RECORDED READING SEGMENTS", "CÁC ĐOẠN ĐỌC ĐÃ GHI");
    public string SessionNoteLabel => Text("SESSION NOTE", "GHI CHÚ PHIÊN");
    public string EditJournalSessionLabel => Text("Edit session", "Sửa phiên");
    public string OpenJournalBookLabel => Text("Open book", "Mở sách");
    public string OpenJournalDayLabel => Text("Open calendar day", "Mở ngày trên lịch");
    public string SelectedSessionStart => SelectedSavedSession is { } session ? ExactTimestamp(session.StartedAtUtc) : "";
    public string SelectedSessionEnd => SelectedSavedSession is { } session ? ExactTimestamp(session.EndedAtUtc) : "";
    public string SelectedSessionActive => SelectedSavedSession is { } session ? ExactDuration(session.DurationSeconds) : "";
    public string SelectedSessionElapsed => SelectedSavedSession is { } session ? ExactDuration(Math.Max(0, (session.EndedAtUtc - session.StartedAtUtc).TotalSeconds)) : "";
    public string SelectedSessionPauses => SelectedSavedSession is { } session ? ExactDuration(Math.Max(0, (session.EndedAtUtc - session.StartedAtUtc).TotalSeconds - session.DurationSeconds)) : "";
    public string SelectedSessionPages => SelectedJournalSession?.PageRangeLabel ?? "";
    public string SelectedSessionNote => SelectedSavedSession is { } session && !string.IsNullOrWhiteSpace(session.Note) ? session.Note : Text("No session note.", "Chưa có ghi chú cho phiên này.");
    public bool HasSessions => RecentSessions.Count > 0;

    private void RebuildJournal()
    {
        var selectedId = SelectedJournalSession?.Id;
        Replace(RecentSessions, ManualSessionJournal.Query(_data.Sessions, _data.Books, JournalSearch, SelectedJournalBook?.BookId,
            JournalFrom is { } from ? DateOnly.FromDateTime(from) : null, JournalThrough is { } through ? DateOnly.FromDateTime(through) : null));
        SelectedJournalSession = selectedId is null ? null : RecentSessions.FirstOrDefault(row => row.Id == selectedId);
        Raise(nameof(HasSessions));
        Raise(nameof(JournalSummary));
        Raise(nameof(JournalEmptyMessage));
        Raise(nameof(FilteredJournalSessions));
    }

    private void ClearJournalFilters()
    {
        JournalSearch = "";
        SelectedJournalPreset = JournalDatePresets.First(preset => preset.Key == "all");
        ApplyJournalPreset("all");
        SelectedJournalBook = JournalBooks.FirstOrDefault();
    }

    private void MarkJournalDatesCustom()
    {
        if (!_applyingJournalPreset) Set(ref _selectedJournalPreset, JournalDatePresets.First(preset => preset.Key == "custom"), nameof(SelectedJournalPreset));
    }

    private void ApplyJournalPreset(string key)
        => ApplyJournalPreset(key, DateTime.Today);

    public void RefreshJournalDatePreset(DateTime localToday)
    {
        if (SelectedJournalPreset is { Key: not ("custom" or "all") } preset)
            ApplyJournalPreset(preset.Key, localToday.Date);
    }

    private void ApplyJournalPreset(string key, DateTime localToday)
    {
        if (key == "custom") return;
        _applyingJournalPreset = true;
        try
        {
            var today = localToday.Date;
            JournalFrom = key switch { "today" => today, "seven" => today.AddDays(-6), "month" => new DateTime(today.Year, today.Month, 1), _ => null };
            JournalThrough = key == "all" ? null : today;
        }
        finally { _applyingJournalPreset = false; }
    }

    private void RefreshSelectedSession()
    {
        var session = SelectedSavedSession;
        Replace(SelectedSessionSegments, session is null ? [] : ManualSessionEditing.Intervals(session).Select((segment, index) =>
            new SessionSegmentRow(index + 1, segment.StartedAtUtc.ToLocalTime(), segment.EndedAtUtc.ToLocalTime(), segment.DurationSeconds)));
        foreach (var property in new[] { nameof(HasSelectedJournalSession), nameof(SelectedSessionHeading), nameof(SelectedSessionStart), nameof(SelectedSessionEnd),
            nameof(SelectedSessionActive), nameof(SelectedSessionElapsed), nameof(SelectedSessionPauses), nameof(SelectedSessionPages), nameof(SelectedSessionNote) }) Raise(property);
        OpenJournalBookCommand.Refresh(); OpenJournalDayCommand.Refresh();
    }

    private async Task DeleteSessionAsync(string id)
    {
        using var mutation = BeginMutationOperation();
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
        using var mutation = BeginMutationOperation();
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

    public async Task<string?> SaveHistoricalSessionAsync(string? id, long bookId, DateTimeOffset startedAt, double durationMinutes, int startPage, int endPage, string note, bool editTiming = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCorrectingSession || _mutationOperations > 0 || IsAddingBook || IsSearching)
            return Text("Wait for the current manual-library operation to finish, then save again.", "Chờ thao tác thư viện sách giấy hiện tại kết thúc rồi lưu lại.");
        using var mutation = BeginMutationOperation();
        _isCorrectingSession = true;
        Raise(nameof(IsCorrectingSession));
        RefreshCorrectionCommands();
        if (_draftCheckpointTimer.IsEnabled) _checkpointDeferredForCorrection = true;
        _draftCheckpointTimer.Stop();
        try
        {
            try { await AwaitPendingStorageAsync(); await _store.WaitForWritesAsync(); }
            catch (Exception ex)
            {
                _log("Manual session correction waiting for storage failed: " + ex);
                return Text("The previous local write failed. Your session is unchanged; keep this editor open and retry.", "Lần lưu trên máy trước đó bị lỗi. Phiên không thay đổi; giữ cửa sổ này rồi thử lại.");
            }
            var book = _data.Books.FirstOrDefault(item => item.Id == bookId);
            if (book is null) return "Choose a physical book.";
            var original = id is null ? null : FindSession(id);
            if (id is not null && original is null) return Text("This session no longer exists. Close the editor and refresh the journal.", "Phiên này không còn tồn tại. Đóng cửa sổ và làm mới nhật ký.");
            var prepared = ManualSessionEditing.Prepare(original, book, startedAt, durationMinutes, startPage, endPage, note, editTiming);
            if (prepared.Session is not { } candidate) return EditingError(prepared.Error ?? "Enter a valid start and end page.");
            if (original is null || editTiming)
            {
                var overlap = _data.Sessions.FirstOrDefault(other => ManualSessionEditing.Overlaps(candidate, other));
                if (overlap is not null) return Text($"This overlaps another manual session for {FindBook(overlap.BookId)?.Title ?? "a book"}. Edit the times before saving.",
                    $"Thời gian trùng với một phiên của {FindBook(overlap.BookId)?.Title ?? "sách khác"}. Sửa thời gian trước khi lưu.");
            }
            var affected = _data.Books.Where(item => item.Id == bookId || item.Id == original?.BookId)
                .Select(item => (Book: item, Page: item.CurrentPage, Completed: item.CompletedAtUtc)).ToArray();
            var index = original is null ? _data.Sessions.Count : _data.Sessions.IndexOf(original);
            if (original is null) _data.Sessions.Add(candidate); else _data.Sessions[index] = candidate;
            foreach (var state in affected) RecalculateBookProgress(state.Book.Id);
            try { await SaveStorageAsync(correctionOwned: true); }
            catch (Exception ex)
            {
                if (original is null) _data.Sessions.Remove(candidate); else _data.Sessions[index] = original;
                foreach (var state in affected) { state.Book.CurrentPage = state.Page; state.Book.CompletedAtUtc = state.Completed; }
                _log("Manual session correction failed: " + ex);
                return Text("The session could not be saved. Your original session and progress are unchanged; keep the editor open and retry.", "Chưa lưu được phiên. Phiên gốc và tiến độ không thay đổi; giữ cửa sổ mở rồi thử lại.");
            }
            if (_disposed) return null;
            if (_store.LastMirrorError is { } mirrorError) _log("Manual session saved to SQLite; recovery mirror failed: " + mirrorError);
            RaiseStorageState();
            RebuildCollections();
            SelectedBook = Books.FirstOrDefault(item => item.Id == bookId);
            Status = id is null ? "Past reading session added." : "Manual session updated.";
            DataChanged?.Invoke(this, EventArgs.Empty);
            return null;
        }
        finally
        {
            _isCorrectingSession = false;
            Raise(nameof(IsCorrectingSession));
            RefreshCorrectionCommands();
            ResumeDeferredCheckpoints();
        }
    }

    public async Task<bool> UpdateSessionNoteAsync(string noteId, string note)
    {
        using var mutation = BeginMutationOperation();
        var sessionId = noteId.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) ? noteId[7..] : noteId;
        var session = FindSession(sessionId);
        if (session is null) return false;
        session.Note = note?.Trim() ?? "";
        await SaveAndPublishAsync();
        RebuildJournal();
        return true;
    }

    public IReadOnlyList<ManualSessionRow> SearchSessions(string query, int maxResults = 12) =>
        ManualSessionJournal.Query(_data.Sessions, _data.Books, query).Take(Math.Clamp(maxResults, 1, 100)).ToArray();

    private static string ExactTimestamp(DateTimeOffset utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz");
    private static string ExactDuration(double seconds) => $"{Formatters.Duration(seconds)} · {seconds:0.#######} {Text("seconds", "giây")}";
    private static string EditingError(string error) => error switch
    {
        "Duration must be from 1 second to 24 hours." => Text(error, "Thời lượng phải từ 1 giây đến 24 giờ."),
        "This duration is too short to preserve the recorded reading segments." => Text(error, "Thời lượng quá ngắn để giữ các đoạn đọc đã ghi."),
        "The corrected time is outside the supported date range." => Text(error, "Thời gian đã sửa nằm ngoài khoảng ngày được hỗ trợ."),
        _ => Localization.Localizer.Instance.Translate(error)
    };

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
}
