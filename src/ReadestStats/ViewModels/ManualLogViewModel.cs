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
    private CancellationTokenSource? _coverSearchCancellation;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _draftCheckpointTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
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
    private string _draftCoverPath = "";
    private readonly HashSet<string> _editedDraftFields = [];
    private bool _applyingMetadata;
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
    private ManualBook? _mergePrimary;
    private ManualBook? _selectedArchivedBook;
    private string _targetMinutes = "";
    private string _targetError = "";
    private string _journalSearch = "";
    private DateTime? _journalFrom;
    private DateTime? _journalThrough;
    private ManualJournalBookOption? _selectedJournalBook;
    private bool _restoringFinishDraft;
    private string _draftCheckpointError = "";
    private readonly HashSet<Task> _pendingStorageTasks = [];
    private bool _disposed;
    private string? _flushedActiveSnapshot;
    private bool _suspendedForRestore;
    private long _storageEpoch;
    private int _mutationOperations;
    private ManualSessionRow? _selectedJournalSession;
    private JournalDatePreset? _selectedJournalPreset;
    private bool _applyingJournalPreset;
    private bool _isCorrectingSession;
    private bool _checkpointDeferredForCorrection;
    private bool _finishDraftDeferredForCorrection;
    private DateTimeOffset? _deferredTimerPauseAtUtc;
    public Func<LibraryReferenceSnapshot>? CaptureReferences { get; set; }
    public Func<LibraryReferenceSnapshot, Task>? RestoreReferences { get; set; }
    public Func<long, IReadOnlyList<long>, Task>? MergeReferences { get; set; }

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

    public event EventHandler? DataChanged;
    public event Action<long>? OpenBookRequested;
    public event Action<DateOnly>? OpenCalendarDayRequested;
    public IReadOnlyList<JournalDatePreset> JournalDatePresets { get; } =
    [
        new("all", "All time", "Toàn bộ"), new("today", "Today", "Hôm nay"),
        new("seven", "Last 7 days", "7 ngày gần đây"), new("month", "This month", "Tháng này"),
        new("custom", "Custom dates", "Ngày tùy chọn")
    ];
    public ObservableCollection<SessionSegmentRow> SelectedSessionSegments { get; } = new RangeObservableCollection<SessionSegmentRow>();
    public bool IsCorrectingSession => _isCorrectingSession;
    public ObservableCollection<ManualBook> Books { get; } = new RangeObservableCollection<ManualBook>();
    public IReadOnlyList<ManualBook> AllBooks => _data.Books;
    public ObservableCollection<ManualSessionRow> RecentSessions { get; } = new RangeObservableCollection<ManualSessionRow>();
    public IReadOnlyList<ManualReadingSession> FilteredJournalSessions
    {
        get
        {
            var sessionsById = _data.Sessions.GroupBy(session => session.Id).ToDictionary(group => group.Key, group => group.First());
            return RecentSessions.Where(row => sessionsById.ContainsKey(row.Id)).Select(row => sessionsById[row.Id]).ToArray();
        }
    }
    public ObservableCollection<ManualJournalBookOption> JournalBooks { get; } = new RangeObservableCollection<ManualJournalBookOption>();
    public ObservableCollection<BookMetadataResult> SearchResults { get; } = new RangeObservableCollection<BookMetadataResult>();
    public ObservableCollection<OperationJournalEntry> ActivityLog { get; } = new RangeObservableCollection<OperationJournalEntry>();
    public ObservableCollection<DuplicateBookGroup> DuplicateGroups { get; } = new RangeObservableCollection<DuplicateBookGroup>();
    public ObservableCollection<ManualBook> ArchivedBooks { get; } = new RangeObservableCollection<ManualBook>();
    public AsyncCommand RestoreArchivedBookCommand { get; }
    public ManualBook? SelectedArchivedBook { get => _selectedArchivedBook; set { if (Set(ref _selectedArchivedBook, value)) RestoreArchivedBookCommand.Refresh(); } }
    public bool HasArchivedBooks => ArchivedBooks.Count > 0;
    public ManualBook? MergePrimary { get => _mergePrimary; set { if (Set(ref _mergePrimary, value)) Raise(nameof(MergePreview)); } }
    public string MergePreview => SelectedDuplicateGroup is null || MergePrimary is null ? "Choose duplicate editions to review." :
        $"Keep: {MergePrimary.Title} · {MergePrimary.Authors}\nMove {_data.Sessions.Count(s => SelectedDuplicateGroup.Books.Any(b => b.Id == s.BookId && b.Id != MergePrimary.Id))} sessions from {SelectedDuplicateGroup.Books.Count - 1} other edition(s). Existing metadata is kept; missing page count, cover, ISBN, publisher and publication date are filled. Plans, pins and edition links follow the retained book. Undo is available in Activity log.";

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
    public RelayCommand ClearJournalFiltersCommand { get; }
    public AsyncCommand RetryMirrorCommand { get; }
    public RelayCommand OpenJournalBookCommand { get; }
    public RelayCommand OpenJournalDayCommand { get; }

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
    public DuplicateBookGroup? SelectedDuplicateGroup { get => _selectedDuplicateGroup; set { if (Set(ref _selectedDuplicateGroup, value)) { MergePrimary = value?.Books.OrderByDescending(book => _data.Sessions.Count(s => s.BookId == book.Id)).FirstOrDefault(); Raise(nameof(MergePreview)); MergeDuplicateBooksCommand.Refresh(); } } }
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
            PauseResumeCommand.Refresh();
            BeginFinishCommand.Refresh();
        }
    }
    public bool IsNotFinishing => !IsFinishing;
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
    public string FinishStartPage { get => _finishStartPage; set { if (Set(ref _finishStartPage, value)) PersistFinishDraft(); } }
    public string FinishEndPage { get => _finishEndPage; set { if (Set(ref _finishEndPage, value)) PersistFinishDraft(); } }
    public string FinishError { get => _finishError; private set => Set(ref _finishError, value); }
    public string FinishNote { get => _finishNote; set { if (Set(ref _finishNote, value)) PersistFinishDraft(); } }
    public string TargetMinutes { get => _targetMinutes; set { if (Set(ref _targetMinutes, value)) TargetError = ""; } }
    public string TargetError { get => _targetError; private set => Set(ref _targetError, value); }
    public string TargetMinutesLabel => Text("FOCUS TARGET · MINUTES (OPTIONAL)", "MỤC TIÊU TẬP TRUNG · PHÚT (TÙY CHỌN)");
    public string FocusTargetHint => Text("Blank means no target. The timer never stops automatically.", "Để trống nếu không đặt mục tiêu. Đồng hồ không tự dừng.");
    public bool HasFocusTarget => _data.ActiveSession?.TargetMinutes is > 0 and <= 720;
    public string FocusTargetLabel => _data.ActiveSession?.TargetRemainingSeconds(DateTimeOffset.UtcNow) is not { } remaining ? "" :
        remaining <= 0 ? Text("Focus target reached · keep reading or finish", "Đã đạt mục tiêu · đọc tiếp hoặc kết thúc") :
        Text($"{FormatTimer(remaining)} left · {_data.ActiveSession.TargetMinutes} min target", $"Còn {FormatTimer(remaining)} · mục tiêu {_data.ActiveSession.TargetMinutes} phút");
    public double FocusProgressPercent => !HasFocusTarget ? 0 : Math.Clamp(_data.ActiveSession!.ElapsedSeconds(DateTimeOffset.UtcNow) * 100 / (_data.ActiveSession.TargetMinutes!.Value * 60), 0, 100);
    public string FinishDraftHint => string.IsNullOrEmpty(_draftCheckpointError)
        ? Text("Pages and note are saved locally as a draft, including when you cancel or close the app.", "Số trang và ghi chú được lưu nháp trên máy, kể cả khi hủy hoặc đóng ứng dụng.")
        : Text("The draft could not be saved locally. Keep this window open and try Save session again.", "Chưa lưu được bản nháp trên máy. Giữ cửa sổ này và thử Lưu phiên lần nữa.");
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
    public bool HasMirrorWarning => _store.LastMirrorError is not null;
    public string StorageStatus => HasMirrorWarning
        ? Text("Local sessions are safe in SQLite. The recovery JSON mirror could not be updated; retry to repair it.", "Các phiên đã lưu an toàn trong SQLite. Bản JSON phục hồi chưa cập nhật được; hãy thử lại để sửa.")
        : Text("SQLite and the local recovery mirror are up to date.", "SQLite và bản phục hồi trên máy đã được cập nhật.");

    public bool HasBooks => Books.Count > 0;
    public bool HasSessions => RecentSessions.Count > 0;
    public bool CanUndoDeleteSession => _lastDeletedSession is not null;
    public bool CanUndoDeleteBook => _lastDeletedBook is not null;
    public bool CanUndoLastImport => _lastImportedBookIds.Count > 0 || _lastImportedSessionIds.Count > 0;
    public bool HasActiveSession => _data.ActiveSession is not null;
    public bool CanSelectBook => !HasActiveSession;
    public bool IsSessionPaused => _data.ActiveSession?.IsPaused == true;
    public string PauseResumeLabel => IsSessionPaused ? "Resume" : "Pause";
    public string ActiveBookTitle => ActiveBook?.Title ?? Localization.Localizer.Instance.Translate("No active session");
    public string ActiveBookAuthors => ActiveBook?.Authors ?? Localization.Localizer.Instance.Translate("Choose a physical book to begin.");
    public string ActiveBookCover => ActiveBook?.CoverUrl ?? "";
    public string ActiveSessionState => !HasActiveSession ? "READY" : IsSessionPaused ? "PAUSED" : "READING NOW";
    public string ElapsedLabel => FormatTimer(_data.ActiveSession?.ElapsedSeconds(DateTimeOffset.UtcNow) ?? 0);
    public double SelectedBookProgress => SelectedBook?.ProgressPercent ?? 0;
    public string SelectedBookProgressLabel => SelectedBook is null ? "Select a book" : SelectedBook.TotalPages is > 0 ? $"{SelectedBook.CurrentPage ?? 0} / {SelectedBook.TotalPages} pages · {SelectedBook.ProgressPercent:0}%" : $"Page {SelectedBook.CurrentPage ?? 0} · total pages unknown";
    public string LibrarySummary => $"{Books.Count} physical {(Books.Count == 1 ? "book" : "books")} · {_data.Sessions.Count} manual {(_data.Sessions.Count == 1 ? "session" : "sessions")}";
    public string DatabasePath => _store.DatabasePath;
    public AppDatabaseHealth DatabaseHealth => _store.InspectDatabase();

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
        RefreshActivityLog();
        if (_data.ActiveSession is not null)
        {
            var now = DateTimeOffset.UtcNow;
            if (!_data.ActiveSession.IsPaused && (_suspendedForRestore || now - _data.ActiveSession.LastUpdatedUtc > TimeSpan.FromSeconds(90)))
            {
                CloseRunningSegment(_data.ActiveSession, _data.ActiveSession.LastUpdatedUtc);
                _data.ActiveSession.IsPaused = true;
                _data.ActiveSession.LastResumedAtUtc = null;
                Status = "Recovered and paused the timer; time while the app was closed was excluded.";
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
    public IReadOnlyList<ManualSessionRow> SearchSessions(string query, int maxResults = 12) =>
        ManualSessionJournal.Query(_data.Sessions, _data.Books, query).Take(Math.Clamp(maxResults, 1, 100)).ToArray();
    public async Task FlushDraftAsync()
    {
        EnsureCanMutate();
        _draftCheckpointTimer.Stop();
        // Also wait for an in-flight save that has already cleared the active
        // session in memory. A failed write must keep normal closing cancellable.
        await AwaitPendingStorageAsync();
        EnsureCanMutate();
        if (_data.ActiveSession is null) return;
        _data.ActiveSession.LastUpdatedUtc = DateTimeOffset.UtcNow;
        var snapshot = JsonSerializer.Serialize(_data.ActiveSession);
        await SaveAndPublishAsync(false);
        // An edit arriving while the disk write is pending must still be flushed
        // by the direct-disposal fallback, rather than marked as already saved.
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

    public ManualReadingData CapturePortableLibrary()
    {
        EnsureCanMutate();
        var export = new ManualReadingData { SchemaVersion = _data.SchemaVersion, Books = _data.Books, Sessions = _data.Sessions, ActiveSession = null };
        return JsonSerializer.Deserialize<ManualReadingData>(JsonSerializer.Serialize(export))!;
    }

    public async Task ExportLibraryAsync(string path)
        => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(CapturePortableLibrary(), new JsonSerializerOptions { WriteIndented = true }));

    public async Task<(int Books, int Sessions)> ImportLibraryAsync(string path)
    {
        using var mutation = BeginMutationOperation();
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
        using var mutation = BeginMutationOperation();
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
        using var mutation = BeginMutationOperation();
        _data.Sessions.RemoveAll(session => _lastImportedSessionIds.Contains(session.Id));
        _data.Books.RemoveAll(book => _lastImportedBookIds.Contains(book.Id) && !_data.Sessions.Any(session => session.BookId == book.Id));
        _lastImportedBookIds.Clear(); _lastImportedSessionIds.Clear();
        await SaveAndPublishAsync(); RebuildCollections(); RaiseImportUndo(); Status = "The last import was undone.";
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
            // A previous mirror may still be enumerating this data. Finish all
            // actual Core writes before introducing a proposed session, and
            // let only this correction enqueue persistence until rollback or
            // commit is complete. Timer display still advances during the wait.
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

    private ManualBook? ActiveBook => _data.ActiveSession is null ? null : _data.Books.FirstOrDefault(book => book.Id == _data.ActiveSession.BookId);

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

    // The parent supplies the key to the metadata service; this text only controls status wording.
    public string? GoogleApiKeyState { get; set; }

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

    private async Task StartSessionAsync()
    {
        using var mutation = BeginMutationOperation();
        if (SelectedBook is null || HasActiveSession) return;
        int? target = null;
        if (!string.IsNullOrWhiteSpace(TargetMinutes))
        {
            if (!int.TryParse(TargetMinutes, out var parsed) || parsed is < 1 or > 720)
            {
                TargetError = Text("Enter a target from 1 to 720 minutes, or leave it blank.", "Nhập mục tiêu từ 1 đến 720 phút, hoặc để trống.");
                return;
            }
            target = parsed;
        }
        var now = DateTimeOffset.UtcNow;
        _data.ActiveSession = new ActiveManualSession { BookId = SelectedBook.Id, StartedAtUtc = now, LastResumedAtUtc = now, LastUpdatedUtc = now, TargetMinutes = target };
        RestoreFinishDraft();
        _lastTimerHeartbeatUtc = now;
        try { await SaveAndPublishAsync(false); }
        catch (Exception ex)
        {
            _data.ActiveSession = null;
            RaiseTimerState();
            Status = Text("The session could not be started safely. Check the local data folder and try again.", "Chưa thể bắt đầu phiên an toàn. Kiểm tra thư mục dữ liệu trên máy rồi thử lại.");
            _log("Manual session start failed: " + ex);
            return;
        }
        if (_disposed) return;
        _timer.Start();
        RaiseTimerState();
        Status = $"Reading {SelectedBook.Title}.";
    }

    private async Task PauseResumeAsync()
    {
        using var mutation = BeginMutationOperation();
        var active = _data.ActiveSession;
        if (active is null) return;
        var now = DateTimeOffset.UtcNow;
        if (active.IsPaused)
        {
            active.Resume(now);
            _lastTimerHeartbeatUtc = now;
            // Finish stops the dispatcher. Resuming the same session must restart it.
            _timer.Start();
            Status = "Session resumed.";
        }
        else
        {
            active.Pause(now);
            Status = "Session paused.";
        }
        active.LastUpdatedUtc = now;
        try { await SaveAndPublishAsync(false); }
        catch (Exception ex)
        {
            Status = Text("The timer changed, but its checkpoint could not be saved. Keep the app open and try again.", "Đồng hồ đã đổi trạng thái nhưng chưa lưu được. Giữ ứng dụng mở rồi thử lại.");
            _log("Manual pause/resume checkpoint failed: " + ex);
        }
        RaiseTimerState();
    }

    private void BeginFinish()
    {
        EnsureCanMutate();
        var book = ActiveBook;
        var active = _data.ActiveSession;
        if (book is null || active is null) return;
        var now = DateTimeOffset.UtcNow;
        active.BeginFinish(now, (book.CurrentPage ?? 0) + 1);
        _timer.Stop();
        RestoreFinishDraft();
        FinishError = "";
        IsFinishing = true;
        RaiseTimerState();
        Status = "Timer stopped · confirm the last page you reached.";
        _ = SaveCheckpointAsync();
    }

    private void CancelFinish()
    {
        EnsureCanMutate();
        if (_data.ActiveSession is { } active) active.IsFinishing = false;
        IsFinishing = false;
        Status = "Session remains paused. Resume when you are ready.";
        RaiseTimerState();
        _ = SaveCheckpointAsync();
    }

    private async Task SaveFinishedSessionAsync()
    {
        using var mutation = BeginMutationOperation();
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
        var session = new ManualReadingSession
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
        };
        var previousPage = book.CurrentPage;
        var previousCompletion = book.CompletedAtUtc;
        _data.Sessions.Add(session);
        book.CurrentPage = Math.Max(book.CurrentPage ?? 0, endPage);
        if (book.TotalPages is > 0 && book.CurrentPage >= book.TotalPages && book.CompletedAtUtc is null) book.CompletedAtUtc = now;
        _data.ActiveSession = null;
        _draftCheckpointTimer.Stop();
        _timer.Stop();
        IsFinishing = false;
        try { await SaveAndPublishAsync(); }
        catch (Exception ex)
        {
            _data.Sessions.Remove(session);
            book.CurrentPage = previousPage;
            book.CompletedAtUtc = previousCompletion;
            _data.ActiveSession = active;
            IsFinishing = true;
            FinishError = Text("The session could not be saved. Your timer and draft are still here; check the local data folder and retry.", "Chưa lưu được phiên. Đồng hồ và bản nháp vẫn còn; kiểm tra thư mục dữ liệu trên máy rồi thử lại.");
            RaiseTimerState();
            _log("Manual session save failed: " + ex);
            return;
        }
        RebuildCollections();
        SelectedBook = Books.FirstOrDefault(item => item.Id == book.Id);
        RaiseTimerState();
        Status = $"Saved {Formatters.Duration(elapsed)} · {pagesRead} pages.";
    }

    private async Task DiscardSessionAsync()
    {
        using var mutation = BeginMutationOperation();
        _data.ActiveSession = null;
        _draftCheckpointTimer.Stop();
        _timer.Stop();
        IsFinishing = false;
        await SaveAndPublishAsync(false);
        RaiseTimerState();
        Status = "Active session discarded.";
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

    private void RaiseImportUndo() { Raise(nameof(CanUndoLastImport)); UndoLastImportCommand.Refresh(); }

    private void CaptureUndo(string action)
    {
        EnsureCanMutate();
        var snapshot = JsonSerializer.Deserialize<ManualReadingData>(JsonSerializer.Serialize(_data))!;
        snapshot.RecoveryReferences = CaptureReferences?.Invoke();
        _store.CaptureUndo(action, snapshot); RefreshActivityLog();
    }

    private void RefreshActivityLog() { Replace(ActivityLog, _store.ListOperations()); UndoRecentActionCommand.Refresh(); Raise(nameof(HasActivityLog)); }
    public bool HasActivityLog => ActivityLog.Count > 0;

    private async Task UndoRecentActionAsync()
    {
        using var mutation = BeginMutationOperation();
        var restored = _store.RestoreLatestUndo(); if (restored is null) return;
        _data = restored; await RecoverReferencesAsync(); await SaveStorageAsync(); RebuildCollections(); RefreshActivityLog(); DataChanged?.Invoke(this, EventArgs.Empty); Status = "The most recent library change was restored.";
    }

    private async Task RecoverReferencesAsync()
    {
        if (_data.RecoveryReferences is not { } references || RestoreReferences is null) return;
        await RestoreReferences(references); _data.RecoveryReferences = null;
    }

    private async Task RestoreArchivedBookAsync()
    {
        using var mutation = BeginMutationOperation();
        if (SelectedArchivedBook is not { } book || HasActiveSession) return;
        CaptureUndo($"Restored {book.Title}"); book.Archived = false;
        await SaveAndPublishAsync(); RebuildCollections(); SelectedBook = Books.FirstOrDefault(b => b.Id == book.Id);
        Status = "Book restored to the physical library.";
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
        using var mutation = BeginMutationOperation();
        if (SelectedBook is null || HasActiveSession) return; CaptureUndo($"Archived {SelectedBook.Title}"); SelectedBook.Archived = true; await SaveAndPublishAsync(); RebuildCollections(); Status = "Book archived; its sessions remain in statistics.";
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

    private void RebuildCollections()
    {
        if (_disposed) return;
        var selectedId = SelectedBook?.Id;
        Replace(ArchivedBooks, _data.Books.Where(book => book.Archived).OrderBy(book => book.Title));
        SelectedArchivedBook = ArchivedBooks.FirstOrDefault(); Raise(nameof(HasArchivedBooks));
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
        ArchiveSelectedBookCommand.Refresh(); MergeDuplicateBooksCommand.Refresh();
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
        // Track the Core task, not this view model's dispatcher continuation.
        // Core persistence must not capture the UI synchronization context.
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
            // Report this failed attempt once. A later close/restore attempt
            // must be able to persist the preserved draft after the disk issue
            // is repaired, rather than re-awaiting the same historical failure.
            foreach (var task in pending.Where(task => task.IsCompleted && (task.IsFaulted || task.IsCanceled)))
            {
                if (task.IsFaulted) _ = task.Exception;
                _pendingStorageTasks.Remove(task);
            }
            throw;
        }
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

    private void RestoreFinishDraft()
    {
        _restoringFinishDraft = true;
        try
        {
            FinishStartPage = _data.ActiveSession?.FinishStartPage ?? "";
            FinishEndPage = _data.ActiveSession?.FinishEndPage ?? "";
            FinishNote = _data.ActiveSession?.FinishNote ?? "";
        }
        finally { _restoringFinishDraft = false; }
    }

    private void PersistFinishDraft()
    {
        if (_disposed || _suspendedForRestore || _restoringFinishDraft || _data.ActiveSession is not { } active) return;
        if (IsCorrectingSession)
        {
            _finishDraftDeferredForCorrection = true;
            _checkpointDeferredForCorrection = true;
            return;
        }
        active.FinishStartPage = FinishStartPage;
        active.FinishEndPage = FinishEndPage;
        active.FinishNote = FinishNote;
        _draftCheckpointTimer.Stop();
        _draftCheckpointTimer.Start();
    }

    private async void TimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _suspendedForRestore) return;
        var now = DateTimeOffset.UtcNow;
        if (IsCorrectingSession)
        {
            // Do not mutate an active session while the correction's mirror is
            // enumerating it. The clock remains live; sleep recovery and writes
            // are applied only after the proposed row is committed or removed.
            if (_data.ActiveSession is { IsPaused: false } && _lastTimerHeartbeatUtc != default && now - _lastTimerHeartbeatUtc > TimeSpan.FromSeconds(90))
                _deferredTimerPauseAtUtc ??= _lastTimerHeartbeatUtc.AddSeconds(2);
            _lastTimerHeartbeatUtc = now;
            Raise(nameof(ElapsedLabel)); Raise(nameof(FocusTargetLabel)); Raise(nameof(FocusProgressPercent));
            if (_data.ActiveSession is not null && ++_ticksSinceSave >= 15)
            { _ticksSinceSave = 0; _checkpointDeferredForCorrection = true; }
            return;
        }
        if (_data.ActiveSession is { IsPaused: false } active && _lastTimerHeartbeatUtc != default && now - _lastTimerHeartbeatUtc > TimeSpan.FromSeconds(90))
        {
            CloseRunningSegment(active, _lastTimerHeartbeatUtc.AddSeconds(2));
            active.IsPaused = true;
            active.LastResumedAtUtc = null;
            active.LastUpdatedUtc = now;
            Status = "Timer paused after sleep or a long system interruption; inactive time was excluded.";
            RaiseTimerState();
            try { await SaveStorageAsync(); } catch (Exception ex) { _log("Manual timer recovery error: " + ex); }
            return;
        }
        _lastTimerHeartbeatUtc = now;
        Raise(nameof(ElapsedLabel));
        Raise(nameof(FocusTargetLabel));
        Raise(nameof(FocusProgressPercent));
        if (++_ticksSinceSave < 15 || _data.ActiveSession is null) return;
        _ticksSinceSave = 0;
        _data.ActiveSession.LastUpdatedUtc = now;
        try { await SaveAndPublishAsync(false); } catch (Exception ex) { _log("Manual timer checkpoint error: " + ex); }
    }

    private async Task SaveCheckpointAsync()
    {
        if (_disposed || _suspendedForRestore) return;
        if (IsCorrectingSession) { _checkpointDeferredForCorrection = true; return; }
        try { await SaveAndPublishAsync(false); _draftCheckpointError = ""; }
        catch (Exception ex) { _draftCheckpointError = ex.Message; _log("Manual finish checkpoint error: " + ex); }
        if (!_disposed) Raise(nameof(FinishDraftHint));
    }

    private void RefreshCorrectionCommands()
    {
        AddBookCommand.Refresh(); CancelAddBookCommand.Refresh(); SearchBooksCommand.Refresh(); UseManualEntryCommand.Refresh(); SaveBookCommand.Refresh();
        StartSessionCommand.Refresh(); PauseResumeCommand.Refresh(); BeginFinishCommand.Refresh(); CancelFinishCommand.Refresh();
        SaveFinishedSessionCommand.Refresh(); DiscardSessionCommand.Refresh(); DeleteSessionCommand.Refresh(); UndoDeleteSessionCommand.Refresh();
        DeleteBookCommand.Refresh(); EditBookCommand.Refresh(); UndoDeleteBookCommand.Refresh(); UndoLastImportCommand.Refresh(); UndoRecentActionCommand.Refresh();
        ArchiveSelectedBookCommand.Refresh(); MergeDuplicateBooksCommand.Refresh(); RestoreArchivedBookCommand.Refresh(); RetryMirrorCommand.Refresh();
    }

    private void ResumeDeferredCheckpoints()
    {
        var checkpoint = _checkpointDeferredForCorrection;
        var finishDraft = _finishDraftDeferredForCorrection;
        var pauseAt = _deferredTimerPauseAtUtc;
        _checkpointDeferredForCorrection = false;
        _finishDraftDeferredForCorrection = false;
        _deferredTimerPauseAtUtc = null;
        if (_disposed || _suspendedForRestore) return;
        if (pauseAt is { } cutoff && _data.ActiveSession is { IsPaused: false } active)
        {
            CloseRunningSegment(active, cutoff);
            active.IsPaused = true;
            active.LastResumedAtUtc = null;
            active.LastUpdatedUtc = DateTimeOffset.UtcNow;
            Status = "Timer paused after sleep or a long system interruption; inactive time was excluded.";
            RaiseTimerState();
            checkpoint = true;
        }
        if (finishDraft) PersistFinishDraft();
        if (checkpoint && HasActiveSession) { _draftCheckpointTimer.Stop(); _draftCheckpointTimer.Start(); }
    }

    private void RaiseTimerState()
    {
        if (_disposed) return;
        Raise(nameof(HasActiveSession));
        Raise(nameof(CanSelectBook));
        Raise(nameof(IsSessionPaused));
        Raise(nameof(PauseResumeLabel));
        Raise(nameof(ActiveBookTitle));
        Raise(nameof(ActiveBookAuthors));
        Raise(nameof(ActiveBookCover));
        Raise(nameof(ActiveSessionState));
        Raise(nameof(ElapsedLabel));
        Raise(nameof(HasFocusTarget));
        Raise(nameof(FocusTargetLabel));
        Raise(nameof(FocusProgressPercent));
        StartSessionCommand.Refresh();
        PauseResumeCommand.Refresh();
        BeginFinishCommand.Refresh();
        SaveFinishedSessionCommand.Refresh();
        DiscardSessionCommand.Refresh();
        EditBookCommand.Refresh();
        UndoRecentActionCommand.Refresh(); ArchiveSelectedBookCommand.Refresh(); MergeDuplicateBooksCommand.Refresh(); RestoreArchivedBookCommand.Refresh();
    }

    private void OpenMetadata()
    {
        if (!ExternalLinkPolicy.TryGetWebUri(SelectedSearchResult?.InfoUrl, out var url)) return;
        try { Process.Start(new ProcessStartInfo(url!.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Status = "Could not open the metadata page."; _log("Open metadata error: " + ex); }
    }

    private void ClearDraft()
    {
        _applyingMetadata = true;
        SearchQuery = "";
        SelectedSearchResult = null;
        DraftTitle = "";
        DraftAuthors = "";
        DraftTotalPages = "";
        DraftPublisher = "";
        DraftPublishedDate = "";
        DraftLanguage = "";
        DraftGoodreadsUrl = "";
        DraftCoverPath = "";
        DraftError = "";
        _applyingMetadata = false;
        _editedDraftFields.Clear();
        _editingBookId = null;
        Raise(nameof(IsEditingBook));
        Raise(nameof(BookEditorTitle));
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Text(string english, string vietnamese) => Localization.Localizer.Instance.IsVietnamese ? vietnamese : english;
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
        // Drain only Core I/O tasks. UI continuations can finish after this
        // returns, but cannot enqueue another write or restart a timer.
        Exception? pendingFailure = null;
        try { Task.WhenAll(_pendingStorageTasks.ToArray()).GetAwaiter().GetResult(); }
        catch (Exception ex) { pendingFailure = ex; _log("Manual persistence while closing failed: " + ex); }
        if (_data.ActiveSession is not null && JsonSerializer.Serialize(_data.ActiveSession) != _flushedActiveSnapshot)
        {
            _data.ActiveSession.LastUpdatedUtc = DateTimeOffset.UtcNow;
            // Normal closing has already awaited FlushDraftAsync. Direct
            // disposal still preserves sub-debounce edits as a safe fallback.
            // Store SaveAsync uses ConfigureAwait(false), including lock wait.
            try { _store.SaveAsync(_data).GetAwaiter().GetResult(); }
            catch (Exception ex) { _log("Manual draft could not be saved while closing: " + ex); throw; }
            pendingFailure = null;
        }
        _pendingStorageTasks.Clear();
        if (pendingFailure is not null) throw new IOException("A manual reading write failed while closing; the previous saved data was preserved.", pendingFailure);
    }
}
