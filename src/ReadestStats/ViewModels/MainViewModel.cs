using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed record NavItem(string Name, string Icon, string Group);
public sealed record BookFilterOption(long? Id, string Label);
public sealed record GlobalSearchResult(string Kind, string Title, string Detail, string Target, string? Key = null);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ReadestDatabaseLocator _locator = new();
    private readonly SettingsStore _store;
    private readonly StatisticsEngine _statistics = new();
    private readonly AnalyticsEngine _analytics = new();
    private readonly DateRangeService _ranges = new();
    private readonly GoalEngine _goalEngine = new();
    private readonly ReadingPlanEngine _readingPlanEngine = new();
    private readonly LibraryImportService _libraryImport = new();
    private readonly InsightEngine _insightEngine = new();
    private readonly DataQualityEngine _dataQualityEngine = new();
    private readonly VisualizationEngine _visualization = new();
    private readonly InfographicEngine _infographics = new();
    private readonly ExportService _export = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly NoteDiscoveryEngine _noteDiscovery = new();
    private readonly Func<string?> _chooseDatabase;
    private readonly Func<string, string, string?> _chooseExport;
    private readonly Func<string?> _chooseBackupImport;
    private readonly Func<string, string, string?> _chooseImport;
    private readonly AppDataBackupService _backup;
    private readonly Action<string> _log;
    private readonly Action<string> _applyTheme;
    private IReadestRepository? _repository;
    private ReadestNotesRepository? _notesRepository;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshDebounce;
    private CancellationTokenSource? _activeRefresh;
    private CancellationTokenSource? _noteSaveDebounce;
    private CancellationTokenSource? _preferenceSaveDebounce;
    private readonly DispatcherTimer _refreshTimer = new();
    private IReadOnlyList<ReadingEvent> _events = [];
    private IReadOnlyList<ReadingEvent> _readestEvents = [];
    private IReadOnlyList<ReadingEvent> _periodEvents = [];
    private IReadOnlyList<Book> _bookModels = [];
    private IReadOnlyList<Book> _readestBookModels = [];
    private IReadOnlyList<ReadingSession> _periodSessions = [];
    private IReadOnlyList<DailyStat> _allDaily = [];
    private IReadOnlyList<ReadingSession> _allSessions = [];
    private AppSettings _settings = new();
    private ResolvedDateRange? _resolvedRange;
    private string _selectedPage = "Today";
    private string _selectedRange = "30 days";
    private string _selectedSource = "All sources";
    private DateTime? _customStart = DateTime.Today.AddDays(-29);
    private DateTime? _customEnd = DateTime.Today;
    private string _status = "Finding Readest data…";
    private string? _error;
    private bool _isBusy;
    private DateTimeOffset? _lastSync;
    private DatabaseDiagnostics? _diagnostics;
    private PeriodMetrics? _period;
    private ComparisonResult? _comparison;
    private SessionProfile? _sessionProfile;
    private WeekdayWeekendStats? _weekdayWeekend;
    private ReadingWindow? _commonWindow;
    private string _trendMetric = "Reading time";
    private string _trendGranularity = "Auto";
    private string _patternMetric = "Time";
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DayDetails? _selectedDay;
    private SessionDisplay? _selectedSession;
    private BookRow? _selectedBook;
    private IReadOnlyList<ReadestNote> _readestNotes = [];
    private readonly List<NoteRow> _allNoteRows = [];
    private NoteRow? _selectedNote;
    private NoteRow? _dailyNote;
    private NoteRow? _randomNote;
    private string? _lastRandomNoteId;
    private string _noteSearch = "";
    private string _noteScope = "All notes";
    private string _notesStatus = "Readest notes are stored locally and never modified.";
    private BookDetail? _bookDetail;
    private BookFilterOption _sessionBook = new(null, "All books");
    private string _sessionDuration = "All durations";
    private string _sessionSearch = "";
    private string _sessionSort = "Newest";
    private string _bookSearch = "";
    private string _bookSort = "Reading time";
    private string _bookFilter = "Active in range";
    private int _year = DateTime.Today.Year;
    private YearInReading? _yearSummary;
    private YearBookGoalSummary? _yearBookGoal;
    private YearBookGoalSummary? _currentYearBookGoal;
    private InsightItem? _selectedInsight;
    private DataQualityReport? _dataQuality;
    private ReadingStory? _story;
    private string _openBookStatus = "Select a book to open it in Readest.";
    private string _updateStatus = "Not checked yet";
    private string? _updateUrl;
    private string _selectedBookStatus = "Unspecified";
    private ReadingPlanProgress _selectedBookPlan = new(false, "No plan", "Set a target date or daily pace for this book.", 0, 0, 0, 0, 0, null, "");
    private int _allSessionCount;
    private readonly Stack<string> _navigationHistory = new();
    private bool _isGoingBack;
    private bool _isCommandPaletteOpen;
    private string _globalSearch = "";
    private GlobalSearchResult? _selectedGlobalSearchResult;

    public MainViewModel(Func<string?> chooseDatabase, Func<string, string, string?> chooseExport, Action<string>? log = null, Action<string>? applyTheme = null, Func<string?>? chooseBackupImport = null, Func<string, string, string?>? chooseImport = null)
    {
        _chooseDatabase = chooseDatabase; _chooseExport = chooseExport; _chooseBackupImport = chooseBackupImport ?? (() => null); _chooseImport = chooseImport ?? ((_, _) => null); _log = log ?? (_ => { }); _applyTheme = applyTheme ?? (_ => { }); _store = new(_locator.SettingsPath);
        var manualPath = Path.Combine(Path.GetDirectoryName(_locator.SettingsPath)!, "manual-reading.json");
        _backup = new(_locator.SettingsPath, manualPath);
        Manual = new(new ManualReadingStore(manualPath), new BookMetadataService(googleApiKey: () => _settings.GoogleBooksApiKey), _log);
        Manual.DataChanged += (_, _) => { ApplySourceSelection(); BuildNotes(); BuildDataQuality(); };
        NavigateCommand = new ParameterCommand<string>(page => { if (!string.IsNullOrWhiteSpace(page)) SelectedPage = page; });
        OpenInsightCommand = new ParameterCommand<InsightItem>(insight => { if (insight is not null) { SelectedInsight = insight; SelectedPage = "Statistics"; } });
        OpenSessionBookCommand = new ParameterCommand<SessionDisplay>(OpenSessionBook, session => session?.Source.BookIds.Count > 0);
        OpenBookInReadestCommand = new RelayCommand(OpenBookInReadest, CanOpenSelectedBook);
        SelectNoteCommand = new ParameterCommand<NoteRow>(note => SelectedNote = note);
        ToggleFavoriteNoteCommand = new ParameterCommand<NoteRow>(ToggleFavoriteNote, note => note is not null);
        OpenSelectedNoteCommand = new RelayCommand(OpenSelectedNote, CanOpenSelectedNote);
        RandomNoteCommand = new RelayCommand(SelectRandomNote, () => Notes.Count > 0);
        RandomSameBookCommand = new RelayCommand(SelectRandomFromSameBook, () => SelectedNote is not null && Notes.Count(note => note.BookHash == SelectedNote.BookHash) > 1);
        PreviousRandomNoteCommand = new RelayCommand(SelectPreviousRandomNote, () => _noteDiscovery.CanGoBack);
        CopySelectedNoteCommand = new RelayCommand(CopySelectedNote, () => SelectedNote is not null);
        DailyNoteCommand = new RelayCommand(SelectDailyNote, () => _allNoteRows.Count > 0);
        SaveSelectedNoteCommand = new AsyncCommand(SaveSelectedNoteAsync, () => SelectedNote is not null);
        RefreshNotesCommand = new AsyncCommand(RefreshNotesAsync, () => _notesRepository is not null);
        ExportNotesMarkdownCommand = new AsyncCommand(() => ExportNotesAsync("md"), () => Notes.Count > 0);
        ExportNotesJsonCommand = new AsyncCommand(() => ExportNotesAsync("json"), () => Notes.Count > 0);
        ExportNotesCsvCommand = new AsyncCommand(() => ExportNotesAsync("csv"), () => Notes.Count > 0);
        TogglePinnedBookCommand = new AsyncCommand(TogglePinnedBookAsync, () => SelectedBook is not null);
        SaveReadingPlanCommand = new AsyncCommand(SaveReadingPlanAsync, () => SelectedBook is not null);
        RemoveReadingPlanCommand = new AsyncCommand(RemoveReadingPlanAsync, () => SelectedBookPlan.Enabled);
        ToggleReadingPlanPauseCommand = new AsyncCommand(ToggleReadingPlanPauseAsync, () => SelectedTracking?.Plan?.Enabled == true);
        StartRereadCommand = new AsyncCommand(StartRereadAsync, () => SelectedBook is not null && SelectedBookStatus == "Finished");
        OpenFocusBookCommand = new ParameterCommand<BookFocusItem>(OpenFocusBook, item => item is not null);
        StartSelectedManualSessionCommand = new AsyncCommand(StartSelectedManualSessionAsync, () => SelectedManualBook is not null && !Manual.HasActiveSession);
        LinkSelectedEditionCommand = new AsyncCommand(LinkSelectedEditionAsync, () => SelectedBook is not null && LinkCandidate is not null && !IsSelectedBookLinked);
        UnlinkSelectedEditionCommand = new AsyncCommand(UnlinkSelectedEditionAsync, () => IsSelectedBookLinked);
        RefreshSelectedMetadataCommand = new AsyncCommand(RefreshSelectedMetadataAsync, () => SelectedManualBook is not null);
        CleanupCoverCacheCommand = new RelayCommand(CleanupCoverCache);
        ExportPhysicalLibraryCommand = new AsyncCommand(ExportPhysicalLibraryAsync);
        ImportPhysicalLibraryCommand = new AsyncCommand(ImportPhysicalLibraryAsync);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync);
        OpenUpdateCommand = new RelayCommand(OpenUpdate, () => !string.IsNullOrWhiteSpace(_updateUrl));
        BackCommand = new RelayCommand(GoBack, () => _navigationHistory.Count > 0);
        OpenCommandPaletteCommand = new RelayCommand(OpenCommandPalette);
        CloseCommandPaletteCommand = new RelayCommand(CloseCommandPalette, () => IsCommandPaletteOpen);
        OpenGlobalSearchResultCommand = new ParameterCommand<GlobalSearchResult>(OpenGlobalSearchResult, result => result is not null);
        RefreshCommand = new AsyncCommand(() => RefreshAsync()); ChangeDatabaseCommand = new AsyncCommand(ChangeDatabaseAsync); RedetectCommand = new AsyncCommand(RedetectAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Diagnostics is not null); ExportCsvCommand = new AsyncCommand(ExportCsvAsync, () => Period is not null); ExportJsonCommand = new AsyncCommand(ExportJsonAsync, () => Period is not null); ExportMonthlyReportCommand = new AsyncCommand(ExportMonthlyReportAsync, () => IsConnected);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync); SaveGoalsCommand = new AsyncCommand(SaveGoalsAsync); BackupSettingsCommand = new AsyncCommand(BackupSettingsAsync); RestoreBackupCommand = new AsyncCommand(RestoreBackupAsync); ResetSettingsCommand = new AsyncCommand(ResetSettingsAsync);
        PreviousMonthCommand = new RelayCommand(() => { Month = Month.AddMonths(-1); BuildActivity(); }); NextMonthCommand = new RelayCommand(() => { if (Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) { Month = Month.AddMonths(1); BuildActivity(); } }); TodayCommand = new RelayCommand(() => { Month = new(DateTime.Today.Year, DateTime.Today.Month, 1); SelectedDate = DateOnly.FromDateTime(DateTime.Today); BuildActivity(); });
        PreviousYearCommand = new RelayCommand(() => { Year--; BuildYear(); }); NextYearCommand = new RelayCommand(() => { if (Year < DateTime.Today.Year) { Year++; BuildYear(); } });
        SelectDayCommand = new ParameterCommand<CalendarDayItem>(day => { if (day is not null) { SelectedDate = day.Date; BuildSelectedDay(); } });
        SelectHeatmapDayCommand = new ParameterCommand<ChartPoint>(point => { if (point is not null && DateOnly.TryParse(point.Label, out var date)) { Month = new(date.Year, date.Month, 1); SelectedDate = date; BuildActivity(); SelectedPage = "Activity"; } });
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(incremental: true);
    }

    public string[] Pages { get; } = ["Today", "Activity", "Sessions", "Manual log", "Books", "Notes", "Goals", "Statistics", "Year in Reading", "Settings"];
    public NavItem[] Navigation { get; } =
    [
        new("Today", "M3,3 H17 V7 H3 Z M3,10 H9 V17 H3 Z M12,10 H17 V17 H12 Z", "READING"),
        new("Activity", "M3,4 H17 V17 H3 Z M3,8 H17 M7,2 V6 M13,2 V6", "READING"),
        new("Sessions", "M10,3 A7,7 0 1 1 9.9,3 M10,6 V10 L13,12", "READING"),
        new("Manual log", "M4,3 H16 V17 H4 Z M7,7 H13 M7,10 H13 M7,13 H10 M15,2 V6 M13,4 H17", "READING"),
        new("Books", "M3,3 H9 A2,2 0 0 1 11,5 V17 A3,3 0 0 0 8,14 H3 Z M17,3 H11 A2,2 0 0 0 9,5 V17 A3,3 0 0 1 12,14 H17 Z", "LIBRARY"),
        new("Notes", "M4,3 H16 A2,2 0 0 1 18,5 V15 A2,2 0 0 1 16,17 H4 A2,2 0 0 1 2,15 V5 A2,2 0 0 1 4,3 Z M5,7 H15 M5,10 H13 M5,13 H10", "LIBRARY"),
        new("Goals", "M10,2 A8,8 0 1 1 9.9,2 M10,6 A4,4 0 1 1 9.9,6 M10,9 A1,1 0 1 1 9.9,9", "INSIGHTS"),
        new("Statistics", "M3,17 V10 H6 V17 Z M8,17 V5 H11 V17 Z M13,17 V8 H16 V17 Z", "INSIGHTS"),
        new("Year in Reading", "M10,2 L12,7 L18,7 L13,11 L15,17 L10,13 L5,17 L7,11 L2,7 L8,7 Z", "INSIGHTS"),
        new("Settings", "M12.22,2 H11.78 A2,2 0 0 0 9.78,4 V4.18 A2,2 0 0 1 8.78,5.91 L8.35,6.16 A2,2 0 0 1 6.35,6.16 L6.2,6.08 A2,2 0 0 0 3.47,6.81 L3.25,7.19 A2,2 0 0 0 3.98,9.92 L4.13,10.02 A2,2 0 0 1 5.13,11.74 V12.25 A2,2 0 0 1 4.13,13.99 L3.98,14.08 A2,2 0 0 0 3.25,16.81 L3.47,17.19 A2,2 0 0 0 6.2,17.92 L6.35,17.84 A2,2 0 0 1 8.35,17.84 L8.78,18.09 A2,2 0 0 1 9.78,19.82 V20 A2,2 0 0 0 11.78,22 H12.22 A2,2 0 0 0 14.22,20 V19.82 A2,2 0 0 1 15.22,18.09 L15.65,17.84 A2,2 0 0 1 17.65,17.84 L17.8,17.92 A2,2 0 0 0 20.53,17.19 L20.75,16.81 A2,2 0 0 0 20.02,14.08 L19.87,13.99 A2,2 0 0 1 18.87,12.25 V11.74 A2,2 0 0 1 19.87,10 L20.02,9.91 A2,2 0 0 0 20.75,7.18 L20.53,6.8 A2,2 0 0 0 17.8,6.07 L17.65,6.15 A2,2 0 0 1 15.65,6.15 L15.22,5.9 A2,2 0 0 1 14.22,4.17 V4 A2,2 0 0 0 12.22,2 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z", "SYSTEM")
    ];
    public string[] RangeOptions => DateRangePresets.All;
    public string[] SourceOptions { get; } = ["All sources", "Readest", "Manual"];
    public string[] TrendMetrics { get; } = ["Reading time", "Sessions", "Active books"];
    public string[] Granularities { get; } = ["Auto", "Day", "Week", "Month"];
    public string[] PatternMetrics { get; } = ["Time", "Sessions"];
    public string[] SessionDurations { get; } = ["All durations", "<5m", "5–15m", "15–30m", "30–60m", "60m+"];
    public string[] SessionSorts { get; } = ["Newest", "Oldest", "Longest", "Shortest"];
    public string[] BookSorts { get; } = ["Reading time", "Sessions", "Active days", "Recently read", "Title"];
    public string[] BookFilters { get; } = ["All", "Active in range", "Currently reading", "Want to read", "Finished this year", "Paused", "Physical books", "Readest books", "Without recent activity", "Incomplete metadata", "Pinned"];
    public string[] BookStatusOptions { get; } = ["Unspecified", "Want to read", "Reading", "Finished", "Paused", "Dropped"];
    public string[] RefreshModes { get; } = ["Manual", "On Readest changes", "30 sec", "1 min", "5 min"];
    public string[] ThemeOptions { get; } = ["Dark", "Light", "System"];

    public ObservableCollection<ChartPoint> Trend { get; } = [];
    public ObservableCollection<ChartPoint> Heatmap { get; } = [];
    public ObservableCollection<ChartPoint> Hourly { get; } = [];
    public ObservableCollection<ChartPoint> Weekdays { get; } = [];
    public ObservableCollection<PersonalRecord> Records { get; } = [];
    public ObservableCollection<InsightItem> QuickInsights { get; } = [];
    public ObservableCollection<InsightItem> AllInsights { get; } = [];
    public ObservableCollection<ChartPoint> SessionHistogram { get; } = [];
    public ObservableCollection<ChartPoint> TimeBlocks { get; } = [];
    public ObservableCollection<ChartPoint> CompletionTimeline { get; } = [];
    public ObservableCollection<ChartPoint> StreakTimeline { get; } = [];
    public ObservableCollection<ChartPoint> CumulativeJourney { get; } = [];
    public ObservableCollection<ChartPoint> MonthlyJourney { get; } = [];
    public ObservableCollection<ChartPoint> ComparisonBars { get; } = [];
    public ObservableCollection<BookRow> StatisticsTopBooks { get; } = [];
    public ObservableCollection<SessionDisplay> Sessions { get; } = [];
    public ObservableCollection<BookFilterOption> SessionBookOptions { get; } = [new(null, "All books")];
    public ObservableCollection<ChartPoint> SessionTimeline { get; } = [];
    public ObservableCollection<BookRow> Books { get; } = [];
    public ObservableCollection<BookFocusItem> ContinueReading { get; } = [];
    public ObservableCollection<BookFocusItem> ActiveReadingPlans { get; } = [];
    public ObservableCollection<ChartPoint> BookTrend { get; } = [];
    public ObservableCollection<ChartPoint> BookHourly { get; } = [];
    public ObservableCollection<ChartPoint> BookWeekdays { get; } = [];
    public ObservableCollection<SessionDisplay> BookSessions { get; } = [];
    public ObservableCollection<CalendarDayItem> CalendarDays { get; } = [];
    public ObservableCollection<string> WeekdayHeaders { get; } = [];
    public ObservableCollection<ChartPoint> ActivityMonthTrend { get; } = [];
    public ObservableCollection<SessionDisplay> DaySessions { get; } = [];
    public ObservableCollection<ChartPoint> DayBooks { get; } = [];
    public ObservableCollection<GoalEditor> Goals { get; } = [];
    public ObservableCollection<GoalArchive> GoalArchives { get; } = [];
    public ObservableCollection<ChartPoint> YearMonths { get; } = [];
    public ObservableCollection<ChartPoint> YearHeatmap { get; } = [];
    public ObservableCollection<BookSummary> YearTopBooks { get; } = [];
    public ObservableCollection<DataQualityCheck> DataQualityChecks { get; } = [];
    public ObservableCollection<MatrixCell> WeekHourMatrix { get; } = [];
    public ObservableCollection<TimelineSpan> DayTimeline { get; } = [];
    public ObservableCollection<DotDatum> SessionDots { get; } = [];
    public ObservableCollection<DotDatum> ReadingStyleDays { get; } = [];
    public ObservableCollection<CompositionPart> BookAttention { get; } = [];
    public ObservableCollection<CompositionPart> SourceComposition { get; } = [];
    public ObservableCollection<ChartPoint> PhysicalPageProgress { get; } = [];
    public ObservableCollection<CompositionPart> YearBookAttention { get; } = [];
    public bool HasPhysicalPageProgress => PhysicalPageProgress.Count > 0;
    public string SourceCompositionSummary => SourceComposition.Count == 0 ? "No reading in this period." : string.Join(" · ", SourceComposition.Select(item => item.Detail));
    public ObservableCollection<MatrixCell> BookDayMatrix { get; } = [];
    public ObservableCollection<ChartPoint> ReadingFingerprint { get; } = [];
    public ObservableCollection<ChartPoint> RollingMomentum { get; } = [];
    public ObservableCollection<DumbbellDatum> PeriodDumbbells { get; } = [];
    public ObservableCollection<ChartPoint> SessionStaircase { get; } = [];
    public ObservableCollection<PaceDatum> GoalPace { get; } = [];
    public ObservableCollection<ChartPoint> BestReadingDays { get; } = [];
    public ObservableCollection<NoteRow> Notes { get; } = [];
    public ObservableCollection<GlobalSearchResult> GlobalSearchResults { get; } = [];
    public ObservableCollection<ChartPoint> NotesByBook { get; } = [];
    public ObservableCollection<ChartPoint> NotesByMonth { get; } = [];
    public ObservableCollection<ChartPoint> NotesByType { get; } = [];
    public ManualLogViewModel Manual { get; }

    public ParameterCommand<string> NavigateCommand { get; }
    public ParameterCommand<InsightItem> OpenInsightCommand { get; }
    public ParameterCommand<SessionDisplay> OpenSessionBookCommand { get; }
    public RelayCommand OpenBookInReadestCommand { get; }
    public ParameterCommand<NoteRow> SelectNoteCommand { get; }
    public ParameterCommand<NoteRow> ToggleFavoriteNoteCommand { get; }
    public RelayCommand OpenSelectedNoteCommand { get; }
    public RelayCommand RandomNoteCommand { get; }
    public RelayCommand RandomSameBookCommand { get; }
    public RelayCommand PreviousRandomNoteCommand { get; }
    public RelayCommand CopySelectedNoteCommand { get; }
    public RelayCommand DailyNoteCommand { get; }
    public AsyncCommand SaveSelectedNoteCommand { get; }
    public AsyncCommand RefreshNotesCommand { get; }
    public AsyncCommand ExportNotesMarkdownCommand { get; }
    public AsyncCommand ExportNotesJsonCommand { get; }
    public AsyncCommand ExportNotesCsvCommand { get; }
    public AsyncCommand TogglePinnedBookCommand { get; }
    public AsyncCommand SaveReadingPlanCommand { get; }
    public AsyncCommand RemoveReadingPlanCommand { get; }
    public AsyncCommand ToggleReadingPlanPauseCommand { get; }
    public AsyncCommand StartRereadCommand { get; }
    public ParameterCommand<BookFocusItem> OpenFocusBookCommand { get; }
    public AsyncCommand StartSelectedManualSessionCommand { get; }
    public AsyncCommand LinkSelectedEditionCommand { get; }
    public AsyncCommand UnlinkSelectedEditionCommand { get; }
    public AsyncCommand RefreshSelectedMetadataCommand { get; }
    public RelayCommand CleanupCoverCacheCommand { get; }
    public AsyncCommand ExportPhysicalLibraryCommand { get; }
    public AsyncCommand ImportPhysicalLibraryCommand { get; }
    public AsyncCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenUpdateCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand OpenCommandPaletteCommand { get; }
    public RelayCommand CloseCommandPaletteCommand { get; }
    public ParameterCommand<GlobalSearchResult> OpenGlobalSearchResultCommand { get; }
    public ParameterCommand<CalendarDayItem> SelectDayCommand { get; }
    public ParameterCommand<ChartPoint> SelectHeatmapDayCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ChangeDatabaseCommand { get; }
    public AsyncCommand RedetectCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public AsyncCommand ExportMonthlyReportCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand SaveGoalsCommand { get; }
    public AsyncCommand BackupSettingsCommand { get; }
    public AsyncCommand RestoreBackupCommand { get; }
    public AsyncCommand ResetSettingsCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }

    public string SelectedPage { get => _selectedPage; set { if (string.IsNullOrWhiteSpace(value) || value == _selectedPage) return; var previous = _selectedPage; if (Set(ref _selectedPage, value)) { if (!_isGoingBack) _navigationHistory.Push(previous); Raise(nameof(PageSubtitle)); Raise(nameof(Breadcrumb)); Raise(nameof(CanGoBack)); Raise(nameof(IsGlobalRangeVisible)); Raise(nameof(IsSourceFilterVisible)); BackCommand.Refresh(); RefreshSelectedPage(); _applyTheme(Theme); _ = Application.Current.Dispatcher.InvokeAsync(() => _applyTheme(Theme), DispatcherPriority.Loaded); } } }
    public string PageSubtitle => SelectedPage switch { "Today" => "Your reading focus for today", "Activity" => "Calendar analytics", "Sessions" => "Continuous reading periods", "Manual log" => "Physical books and timed sessions", "Books" => "Your reading library", "Notes" => "Your personal knowledge library", "Goals" => "Targets, pace and history", "Statistics" => "Your personal reading story", "Year in Reading" => "Your annual reading story", _ => "Data, quality and preferences" };
    public string Breadcrumb => $"Readest Stats  /  {SelectedPage}";
    public bool CanGoBack => _navigationHistory.Count > 0;
    public bool IsGlobalRangeVisible => SelectedPage is "Today" or "Sessions" or "Books" or "Statistics";
    public bool IsSourceFilterVisible => SelectedPage is "Today" or "Activity" or "Sessions" or "Books" or "Goals" or "Statistics" or "Year in Reading";
    public string SelectedSource { get => _selectedSource; set { var normalized = SourceOptions.Contains(value) ? value : "All sources"; if (Set(ref _selectedSource, normalized)) { _settings.DefaultSourceFilter = normalized; ApplySourceSelection(); SchedulePreferenceSave(); } } }
    public string SelectedRange { get => _selectedRange; set { if (Set(ref _selectedRange, value)) { _settings.DefaultRangePreset = value; Raise(nameof(IsCustomRange)); RecalculateAll(); SchedulePreferenceSave(); } } }
    public bool IsCustomRange => SelectedRange == "Custom";
    public DateTime? CustomStart { get => _customStart; set { if (Set(ref _customStart, value) && IsCustomRange) { _settings.CustomRangeStart = value is null ? null : DateOnly.FromDateTime(value.Value); RecalculateAll(); SchedulePreferenceSave(); } } }
    public DateTime? CustomEnd { get => _customEnd; set { if (Set(ref _customEnd, value) && IsCustomRange) { _settings.CustomRangeEnd = value is null ? null : DateOnly.FromDateTime(value.Value); RecalculateAll(); SchedulePreferenceSave(); } } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsCommandPaletteOpen { get => _isCommandPaletteOpen; private set { if (Set(ref _isCommandPaletteOpen, value)) CloseCommandPaletteCommand.Refresh(); } }
    public string GlobalSearch { get => _globalSearch; set { if (Set(ref _globalSearch, value ?? "")) RebuildGlobalSearch(); } }
    public GlobalSearchResult? SelectedGlobalSearchResult { get => _selectedGlobalSearchResult; set => Set(ref _selectedGlobalSearchResult, value); }
    public string? Error { get => _error; private set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public DateTimeOffset? LastSync { get => _lastSync; private set { Set(ref _lastSync, value); Raise(nameof(LastSyncLabel)); } }
    public string LastSyncLabel => LastSync is null ? "Not synced" : $"Last sync {FormatTime(LastSync.Value)}";
    public bool IsConnected => _repository is not null;
    public bool HasData => _events.Count > 0;
    public bool NoPeriodData => _periodEvents.Count == 0 && IsConnected;
    public DatabaseDiagnostics? Diagnostics { get => _diagnostics; private set => Set(ref _diagnostics, value); }
    public PeriodMetrics? Period { get => _period; private set => Set(ref _period, value); }
    public ComparisonResult? Comparison { get => _comparison; private set => Set(ref _comparison, value); }
    public SessionProfile? SessionProfile { get => _sessionProfile; private set => Set(ref _sessionProfile, value); }
    public WeekdayWeekendStats? WeekdayWeekend { get => _weekdayWeekend; private set => Set(ref _weekdayWeekend, value); }
    public ReadingWindow? CommonWindow { get => _commonWindow; private set => Set(ref _commonWindow, value); }
    public string CommonWindowLabel => CommonWindow is null ? "—" : FormatHourRange(CommonWindow.StartHour, CommonWindow.Hours);
    public string ConsistencyDetail => Period is null ? "No eligible days" : $"{Formatters.Count(Period.ActiveDays, "active day")} of {Formatters.Count(Period.AvailableDays, "eligible day")}";
    public string ComparisonText => Comparison is null || !Comparison.HasBaseline ? "No previous-period baseline" : $"{Comparison.PercentDelta:+0.#;-0.#;0}% vs previous period";
    public string AbsoluteDeltaText => Comparison is null ? "—" : $"{(Comparison.AbsoluteDelta >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(Comparison.AbsoluteDelta))}";
    public double SessionsPerActiveDay => Period is null || Period.ActiveDays == 0 ? 0 : (double)Period.SessionCount / Period.ActiveDays;
    public int FinishedInRange => _resolvedRange is null ? 0 : CompletedBookDates().Count(date => date >= _resolvedRange.Start && date < _resolvedRange.End);
    public int FinishedThisYear => CompletedBookDates().Count(date => _statistics.ToLocal(date).Year == DateTime.Today.Year);
    public int FinishedAllTime => CompletedBookDates().Count;
    public int CurrentStreak { get; private set; }
    public int LongestStreak { get; private set; }
    public string CurrentStreakLabel => Formatters.Count(CurrentStreak, "day");
    public string LongestStreakLabel => $"Best: {Formatters.Count(LongestStreak, "day")}";
    public string StreakContinuation => CurrentStreak > 0 && !_allDaily.Any(d => d.Date == DateOnly.FromDateTime(DateTime.Today)) ? $"Read today to continue your {Formatters.Count(CurrentStreak, "day")} streak." : "";
    public string TopBookLabel { get; private set; } = "No active book";
    public string TrendMetric { get => _trendMetric; set { if (Set(ref _trendMetric, value)) { _settings.TrendMetric = value; BuildTrend(); SchedulePreferenceSave(); } } }
    public string TrendGranularity { get => _trendGranularity; set { if (Set(ref _trendGranularity, value)) { _settings.TrendGranularity = value; BuildTrend(); SchedulePreferenceSave(); } } }
    public string ReadingPeriodSummary => $"{Formatters.Duration(Period?.TotalSeconds ?? 0)} total · {Formatters.Duration(Period?.AveragePerCalendarDay ?? 0)}/day";
    public string TrendSummary
    {
        get
        {
            var days = _resolvedRange is null ? 1 : Math.Max(1, _resolvedRange.EndDate.DayNumber - _resolvedRange.StartDate.DayNumber + 1);
            var resolution = AnalyticsEngine.ResolveTrendGranularity(days, TrendGranularity).ToLowerInvariant();
            return TrendMetric switch
            {
                "Sessions" => $"{Formatters.Count(Period?.SessionCount ?? 0, "session")} · {resolution} view",
                "Active books" => $"{Formatters.Count(Period?.ActiveBooks ?? 0, "active book")} · {resolution} view",
                _ => $"{Formatters.Duration(Period?.TotalSeconds ?? 0)} total · {Formatters.Duration(Period?.AveragePerCalendarDay ?? 0)}/day · {resolution} view"
            };
        }
    }
    public string PatternMetric { get => _patternMetric; set { if (Set(ref _patternMetric, value)) { BuildPatterns(); if (_resolvedRange is not null) Replace(WeekHourMatrix, _infographics.WeekHourMatrix(_events, _resolvedRange, SessionGapMinutes, PatternMetric == "Sessions")); } } }
    public string PatternWindowLabel { get; private set; } = "—";
    public string PatternSummary { get; private set; } = "No activity in this period.";
    public string PatternWeekdaySummary { get; private set; } = "No activity in this period.";
    public DateTime Month { get => _month; set { if (Set(ref _month, new DateTime(value.Year, value.Month, 1))) Raise(nameof(MonthLabel)); } }
    public string MonthLabel => Month.ToString("MMMM yyyy");
    public DateOnly SelectedDate { get => _selectedDate; private set => Set(ref _selectedDate, value); }
    public DayDetails? SelectedDay { get => _selectedDay; private set => Set(ref _selectedDay, value); }
    public SessionDisplay? SelectedSession { get => _selectedSession; set { if (Set(ref _selectedSession, value)) BuildSessionDetail(); } }
    public BookRow? SelectedBook { get => _selectedBook; set { if (Set(ref _selectedBook, value)) { BuildBookDetail(); UpdateOpenBookState(); Raise(nameof(IsSelectedBookPinned)); Raise(nameof(PinBookLabel)); Raise(nameof(SelectedBookOpenLabel)); Raise(nameof(SelectedManualBook)); Raise(nameof(LinkCandidate)); Raise(nameof(IsSelectedBookLinked)); Raise(nameof(LinkEditionLabel)); Raise(nameof(SelectedBookCycleSummary)); TogglePinnedBookCommand.Refresh(); SaveReadingPlanCommand.Refresh(); StartSelectedManualSessionCommand.Refresh(); StartRereadCommand.Refresh(); LinkSelectedEditionCommand.Refresh(); UnlinkSelectedEditionCommand.Refresh(); RefreshSelectedMetadataCommand.Refresh(); } } }
    public NoteRow? SelectedNote
    {
        get => _selectedNote;
        set
        {
            if (!Set(ref _selectedNote, value)) return;
            if (value is not null)
            {
                value.MarkSeen();
                Raise(nameof(NotesReadCount));
                ScheduleNoteSave(value);
            }
            SaveSelectedNoteCommand.Refresh();
            OpenSelectedNoteCommand.Refresh();
            CopySelectedNoteCommand.Refresh();
            RandomSameBookCommand.Refresh();
        }
    }
    public NoteRow? DailyNote { get => _dailyNote; private set => Set(ref _dailyNote, value); }
    public NoteRow? RandomNote { get => _randomNote; private set => Set(ref _randomNote, value); }
    public BookDetail? BookDetail { get => _bookDetail; private set => Set(ref _bookDetail, value); }
    public InsightItem? SelectedInsight { get => _selectedInsight; set => Set(ref _selectedInsight, value); }
    public BookFilterOption SessionBook { get => _sessionBook; set { var normalized = value ?? SessionBookOptions.First(); if (Set(ref _sessionBook, normalized)) ApplySessionFilters(); } }
    public string SessionDuration { get => _sessionDuration; set { if (Set(ref _sessionDuration, value)) ApplySessionFilters(); } }
    public string SessionSearch { get => _sessionSearch; set { if (Set(ref _sessionSearch, value)) ApplySessionFilters(); } }
    public string SessionSort { get => _sessionSort; set { if (Set(ref _sessionSort, value)) ApplySessionFilters(); } }
    public string BookSearch { get => _bookSearch; set { if (Set(ref _bookSearch, value)) ApplyBookFilters(); } }
    public string BookSort { get => _bookSort; set { if (Set(ref _bookSort, value)) ApplyBookFilters(); } }
    public string BookFilter { get => _bookFilter; set { if (Set(ref _bookFilter, value)) ApplyBookFilters(); } }
    public int Year { get => _year; set { if (Set(ref _year, value)) BuildYear(); } }
    public YearInReading? YearSummary { get => _yearSummary; private set => Set(ref _yearSummary, value); }
    public YearBookGoalSummary? YearBookGoal { get => _yearBookGoal; private set => Set(ref _yearBookGoal, value); }
    public YearBookGoalSummary? CurrentYearBookGoal { get => _currentYearBookGoal; private set => Set(ref _currentYearBookGoal, value); }
    public string YearBestMonth => YearSummary?.BestMonth is null ? "—" : $"{YearSummary.BestMonth.Label} · {Formatters.Duration(YearSummary.BestMonth.Value * 3600)}";
    public string YearBestDay => YearSummary?.BestDay is null ? "—" : $"{YearSummary.BestDay.Date:MMM d} · {Formatters.Duration(YearSummary.BestDay.Seconds)}";
    public string YearTopBook => YearSummary?.TopBook is null ? "—" : $"{YearSummary.TopBook.Title} · {Formatters.Duration(YearSummary.TopBook.Seconds)}";
    public string YearLongestStreakLabel => YearSummary is null ? "—" : Formatters.Count(YearSummary.LongestStreak, "day");
    public string YearFavoriteHour { get { var label = YearSummary?.FavoriteHour?.Label; return label is not null && label.Length >= 2 && int.TryParse(label[..2], out var hour) ? FormatHourRange(hour, 1) : "—"; } }
    public int AllSessionCount { get => _allSessionCount; private set => Set(ref _allSessionCount, value); }
    public DataQualityReport? DataQuality { get => _dataQuality; private set => Set(ref _dataQuality, value); }
    public string AppDatabaseSummary { get; private set; } = "App database has not been checked yet.";
    public string AppDatabasePath => Manual.DatabasePath;
    public ReadingStory? Story { get => _story; private set => Set(ref _story, value); }
    public string OpenBookStatus { get => _openBookStatus; private set => Set(ref _openBookStatus, value); }
    public string NotesStatus { get => _notesStatus; private set => Set(ref _notesStatus, value); }
    public string NotesCountLabel => Formatters.Count(Notes.Count, "note");
    public bool HasNoVisibleNotes => Notes.Count == 0;
    public string NotesEmptyMessage => "No local Readest notes found.";
    public int TotalNotes => _readestNotes.Count + Manual.AsNotes().Count;
    public int FavoriteNotesCount => _allNoteRows.Count(note => note.IsFavorite);
    public int NotesReadCount => _allNoteRows.Count(note => note.TimesSeen > 0);
    public string NoteSearch { get => _noteSearch; set { if (Set(ref _noteSearch, value ?? "")) ApplyNoteFilters(); } }
    public string[] NoteScopeOptions { get; } = ["All notes", "Unseen", "Not seen recently", "Readest only", "Physical books only", "Favorites only", "Selected book"];
    public string NoteScope { get => _noteScope; set { var normalized = NoteScopeOptions.Contains(value) ? value : "All notes"; if (Set(ref _noteScope, normalized)) ApplyNoteFilters(); } }
    public string NotesSummary
    {
        get
        {
            var sources = _readestNotes.Concat(Manual.AsNotes()).ToArray();
            return sources.Length == 0 ? "No notes found yet." : $"{Formatters.Count(sources.Length, "note")} across {Formatters.Count(sources.Select(note => note.BookHash).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "book")}";
        }
    }
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }
    public string SelectedBookStatus
    {
        get => _selectedBookStatus;
        set
        {
            var normalized = BookStatusOptions.Contains(value) ? value : "Unspecified";
            if (!Set(ref _selectedBookStatus, normalized) || SelectedBook is null) return;
            var key = BookTrackingKey(SelectedBook.Id);
            if (key is null) return;
            if (normalized == "Unspecified") _settings.BookTracking.Remove(key);
            else
            {
                if (!_settings.BookTracking.TryGetValue(key, out var tracking)) _settings.BookTracking[key] = tracking = new();
                tracking.Status = normalized;
                tracking.Cycles ??= [];
                if (normalized == "Reading")
                {
                    tracking.StartedAtUtc ??= DateTimeOffset.UtcNow;
                    EnsureOpenCycle(tracking);
                }
                tracking.CompletedAtUtc = normalized == "Finished" ? tracking.CompletedAtUtc ?? DateTimeOffset.UtcNow : null;
                if (normalized == "Finished" && tracking.CompletedAtUtc is { } completed) CompleteOpenCycle(tracking, completed);
            }
            Raise(nameof(SelectedBookStatusDetail));
            Raise(nameof(SelectedBookCompletedDate));
            Raise(nameof(SelectedBookCycleSummary));
            StartRereadCommand.Refresh();
            _ = SaveBookTrackingAsync();
        }
    }
    public string SelectedBookStatusDetail
    {
        get
        {
            if (SelectedBook is null) return "Select a book to manage its reading status.";
            var key = BookTrackingKey(SelectedBook.Id);
            var completed = key is not null && _settings.BookTracking.TryGetValue(key, out var tracking) ? tracking.CompletedAtUtc : null;
            return completed is null ? "Stored locally by Readest Stats." : $"Finished {completed.Value.ToLocalTime():MMM d, yyyy}. Used for book goals.";
        }
    }
    public DateTime? SelectedBookCompletedDate
    {
        get
        {
            var key = SelectedBook is null ? null : BookTrackingKey(SelectedBook.Id);
            return key is not null && _settings.BookTracking.TryGetValue(key, out var tracking) && tracking.CompletedAtUtc is { } completed ? completed.ToLocalTime().Date : null;
        }
        set
        {
            var key = SelectedBook is null ? null : BookTrackingKey(SelectedBook.Id);
            if (key is null || !_settings.BookTracking.TryGetValue(key, out var tracking) || tracking.Status != "Finished" || value is null) return;
            tracking.CompletedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Local)).ToUniversalTime();
            CompleteOpenCycle(tracking, tracking.CompletedAtUtc.Value);
            Raise(); Raise(nameof(SelectedBookStatusDetail)); Raise(nameof(SelectedBookCycleSummary)); _ = SaveBookTrackingAsync();
        }
    }
    public string SelectedBookCycleSummary
    {
        get
        {
            var cycles = SelectedTracking?.Cycles ?? [];
            if (cycles.Count == 0) return "No reading cycle recorded yet.";
            var current = cycles.FirstOrDefault(cycle => cycle.Id == SelectedTracking?.CurrentCycleId) ?? cycles.OrderByDescending(cycle => cycle.Number).First();
            var state = current.CompletedAtUtc is { } completed ? $"finished {completed.ToLocalTime():MMM d, yyyy}" : $"started {current.StartedAtUtc.ToLocalTime():MMM d, yyyy}";
            return $"{current.Label} · {state} · {Formatters.Count(cycles.Count, "cycle")}";
        }
    }
    public bool IsSelectedBookPinned
    {
        get { var key = SelectedBook is null ? null : BookTrackingKey(SelectedBook.Id); return key is not null && _settings.PinnedBookKeys.Contains(key); }
    }
    public string PinBookLabel => IsSelectedBookPinned ? "Unpin book" : "Pin book";
    public string SelectedBookOpenLabel => SelectedBook?.Source == "Manual" ? "View book info" : "Open in Readest";
    public ManualBook? SelectedManualBook => SelectedBook is null ? null : ManualBookFor(SelectedBook.Id);
    public bool IsSelectedBookLinked => SelectedBook is not null && LinkForBook(SelectedBook.Id) is not null;
    public BookRow? LinkCandidate => SelectedBook is null || IsSelectedBookLinked ? null : FindLinkCandidate(SelectedBook);
    public string LinkEditionLabel => IsSelectedBookLinked ? "Linked Readest + physical edition" : LinkCandidate is null ? "No matching edition found" : $"Link with {LinkCandidate.Title} ({LinkCandidate.Source})";
    public ReadingPlanProgress SelectedBookPlan { get => _selectedBookPlan; private set { if (Set(ref _selectedBookPlan, value)) { RemoveReadingPlanCommand.Refresh(); ToggleReadingPlanPauseCommand.Refresh(); } } }
    public bool HasContinueReading => ContinueReading.Count > 0;
    public bool HasActiveReadingPlans => ActiveReadingPlans.Count > 0;
    public DateTime? SelectedPlanTargetDate
    {
        get => SelectedTracking?.Plan?.TargetDate?.ToDateTime(TimeOnly.MinValue);
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.TargetDate = value is null ? null : DateOnly.FromDateTime(value.Value); Raise(); BuildSelectedPlan(); }
    }
    public DateTime? SelectedPlanStartDate
    {
        get => SelectedTracking?.Plan?.StartDate?.ToDateTime(TimeOnly.MinValue);
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.StartDate = value is null ? null : DateOnly.FromDateTime(value.Value); Raise(); BuildSelectedPlan(); }
    }
    public double SelectedPlanDailyMinutes
    {
        get => SelectedTracking?.Plan?.DailyMinutes ?? 0;
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.DailyMinutes = Math.Max(0, value); Raise(); BuildSelectedPlan(); }
    }
    public int SelectedPlanDailyPages
    {
        get => SelectedTracking?.Plan?.DailyPages ?? 0;
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.DailyPages = Math.Max(0, value); Raise(); BuildSelectedPlan(); }
    }
    public bool SelectedPlanIncludeWeekends
    {
        get => SelectedTracking?.Plan?.IncludeWeekends ?? true;
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.IncludeWeekends = value; Raise(); BuildSelectedPlan(); }
    }
    public bool SelectedPlanIsPaused => SelectedTracking?.Plan?.IsPaused == true;
    public string ReadingPlanPauseLabel => SelectedPlanIsPaused ? "Resume plan" : "Pause plan";
    public bool SelectedPlanMonday { get => IsPlanDay(DayOfWeek.Monday); set => SetPlanDay(DayOfWeek.Monday, value); }
    public bool SelectedPlanTuesday { get => IsPlanDay(DayOfWeek.Tuesday); set => SetPlanDay(DayOfWeek.Tuesday, value); }
    public bool SelectedPlanWednesday { get => IsPlanDay(DayOfWeek.Wednesday); set => SetPlanDay(DayOfWeek.Wednesday, value); }
    public bool SelectedPlanThursday { get => IsPlanDay(DayOfWeek.Thursday); set => SetPlanDay(DayOfWeek.Thursday, value); }
    public bool SelectedPlanFriday { get => IsPlanDay(DayOfWeek.Friday); set => SetPlanDay(DayOfWeek.Friday, value); }
    public bool SelectedPlanSaturday { get => IsPlanDay(DayOfWeek.Saturday); set => SetPlanDay(DayOfWeek.Saturday, value); }
    public bool SelectedPlanSunday { get => IsPlanDay(DayOfWeek.Sunday); set => SetPlanDay(DayOfWeek.Sunday, value); }
    public int SelectedPlanPriority
    {
        get => SelectedTracking?.Plan?.Priority ?? 2;
        set { var plan = EnsureSelectedPlan(); if (plan is null) return; plan.Priority = Math.Clamp(value, 1, 3); Raise(); }
    }
    public bool SelectedPlanUsesPages => SelectedManualBook?.TotalPages is > 0;
    public GoalProgress? SelectedBookPlanGoal => !SelectedBookPlan.Enabled ? null : new(
        new GoalDefinition { Metric = SelectedPlanUsesPages ? GoalMetric.Books : GoalMetric.ReadingTime, Period = GoalPeriod.Daily, TargetValue = SelectedBookPlan.Target },
        SelectedBookPlan.Actual,
        SelectedBookPlan.Target,
        SelectedBookPlan.Target <= 0 ? 0 : SelectedBookPlan.Actual * 100 / SelectedBookPlan.Target,
        SelectedBookPlan.Target <= 0 ? 0 : SelectedBookPlan.Required * 100 / SelectedBookPlan.Target,
        SelectedBookPlan.Remaining,
        SelectedBookPlan.RequiredPerReadingDay,
        null,
        SelectedBookPlan.Status,
        $"{SelectedBookPlan.Actual:0.#} {SelectedBookPlan.Unit}",
        $"{SelectedBookPlan.Target:0.#} {SelectedBookPlan.Unit}",
        $"{SelectedBookPlan.Remaining:0.#} remaining",
        $"{SelectedBookPlan.RequiredPerReadingDay:0.#} per reading day",
        SelectedBookPlan.ProjectedFinish is { } date ? $"Projected {date:MMM d}" : "Projection unavailable");
    public bool AutomaticBackups { get => _settings.AutomaticBackups; set { _settings.AutomaticBackups = value; Raise(); } }
    public string PageMetricReliability => ExperimentalPageMetrics ? "Approximate · enabled" : "Approximate · hidden";
    public string AppVersion => Assembly.GetEntryAssembly()?.GetName().Version is { } version ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.3.0";
    public string BuildCommit { get { var value = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion; var commit = value?.Split('+').ElementAtOrDefault(1); return string.IsNullOrWhiteSpace(commit) ? "Development build" : commit[..Math.Min(7, commit.Length)]; } }
    public string BuildDate { get { try { return Environment.ProcessPath is { } path ? File.GetLastWriteTime(path).ToString(Use24HourTime ? "MMM d, yyyy · HH:mm" : "MMM d, yyyy · h:mm tt") : "Unknown"; } catch { return "Unknown"; } } }

    public int SessionGapMinutes { get => _settings.SessionGapMinutes; set { _settings.SessionGapMinutes = value; Raise(); RecalculateAll(); } }
    public int MinimumSessionSeconds { get => _settings.MinimumSessionSeconds; set { _settings.MinimumSessionSeconds = Math.Max(0, value); Raise(); RecalculateAll(); } }
    public string FirstDayOfWeek { get => _settings.WeekStartsMonday ? "Monday" : "Sunday"; set { _settings.WeekStartsMonday = value != "Sunday"; Raise(); RecalculateAll(); } }
    public bool ExperimentalPageMetrics { get => _settings.ExperimentalPageMetrics; set { _settings.ExperimentalPageMetrics = value; Raise(); Raise(nameof(PageMetricReliability)); } }
    public bool CompactMode { get => _settings.CompactMode; set { _settings.CompactMode = value; Raise(); } }
    public bool Use24HourTime { get => _settings.Use24HourTime; set { if (_settings.Use24HourTime == value) return; _settings.Use24HourTime = value; Raise(); Raise(nameof(LastSyncLabel)); Raise(nameof(BuildDate)); RecalculateAll(); } }
    public bool ReduceMotion { get => _settings.ReduceMotion; set { _settings.ReduceMotion = value; Raise(); } }
    public string GoogleBooksApiKey { get => _settings.GoogleBooksApiKey ?? ""; set { _settings.GoogleBooksApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Manual.GoogleApiKeyState = _settings.GoogleBooksApiKey; Raise(); } }
    public string Theme { get => ThemeManager.Normalize(_settings.Theme); set { var normalized = ThemeManager.Normalize(value); if (_settings.Theme == normalized) return; _settings.Theme = normalized; Raise(); _applyTheme(normalized); } }
    public string RefreshMode
    {
        get => _settings.RefreshIntervalSeconds switch { 30 => "30 sec", 60 => "1 min", 300 => "5 min", _ => _settings.AutoRefresh ? "On Readest changes" : "Manual" };
        set { _settings.RefreshIntervalSeconds = value switch { "30 sec" => 30, "1 min" => 60, "5 min" => 300, _ => 0 }; _settings.AutoRefresh = value != "Manual"; Raise(); ConfigureRefresh(); }
    }

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync(); _settings.BookTracking ??= []; _settings.BookLinks ??= []; _settings.PinnedBookKeys ??= []; _settings.NoteStates ??= []; if (_settings.AutomaticBackups) { try { _store.CreateAutomaticBackup(); } catch (Exception ex) { _log("Automatic backup error: " + ex); } } GoalEngine.Migrate(_settings); _settings.Theme = ThemeManager.Normalize(_settings.Theme); _applyTheme(_settings.Theme); _selectedRange = DateRangePresets.All.Contains(_settings.DefaultRangePreset) ? _settings.DefaultRangePreset : _settings.DefaultRangeDays switch { 1 => "Today", 7 => "7 days", 90 => "90 days", 183 => "6 months", 366 => "1 year", 365 => "This year", -1 => "All time", _ => "30 days" }; _selectedSource = SourceOptions.Contains(_settings.DefaultSourceFilter) ? _settings.DefaultSourceFilter : "All sources"; _customStart = _settings.CustomRangeStart?.ToDateTime(TimeOnly.MinValue) ?? _customStart; _customEnd = _settings.CustomRangeEnd?.ToDateTime(TimeOnly.MinValue) ?? _customEnd; _trendMetric = TrendMetrics.Contains(_settings.TrendMetric) ? _settings.TrendMetric : "Reading time"; _trendGranularity = Granularities.Contains(_settings.TrendGranularity) ? _settings.TrendGranularity : "Auto"; Manual.GoogleApiKeyState = _settings.GoogleBooksApiKey; await Manual.InitializeAsync(); if (_settings.AutomaticBackups) { try { _backup.CreateAutomatic(AppVersion); } catch (Exception ex) { _log("Complete automatic backup error: " + ex); } } RaiseSettings(); Raise(nameof(SelectedSource));
        var path = await _locator.LocateAsync(_settings.DatabasePath);
        if (path is null) { Status = Manual.HasBooks ? "Manual library ready" : "No Readest data found"; Error = "Readest data was not found. Manual log remains available; choose statistics.db to connect digital reading."; RaiseConnectionState(); return; }
        await ConnectAsync(path);
    }

    private async Task ConnectAsync(string path)
    {
        IsBusy = true; Error = null;
        try
        {
            var repository = new SqliteReadestRepository(path); var validation = await repository.ValidateAsync();
            if (!validation.IsValid) { _repository = null; _notesRepository = null; Diagnostics = null; Error = validation.Message; Status = "Incompatible database"; return; }
            _repository = repository; _notesRepository = new ReadestNotesRepository(path); Diagnostics = validation.Diagnostics; _settings.DatabasePath = path; await _store.SaveAsync(_settings); Status = "Loading Readest activity…"; await RefreshAsync(force: true); ConfigureRefresh();
        }
        catch (Exception ex) { _repository = null; _notesRepository = null; Error = Friendly(ex); Status = "Connection failed"; }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private async Task RefreshAsync(bool incremental = false, bool force = false)
    {
        if (_repository is null || (IsBusy && !force)) return; _activeRefresh?.Cancel(); _activeRefresh?.Dispose(); _activeRefresh = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = _activeRefresh.Token; IsBusy = true; Error = null;
        try
        {
            var booksTask = _repository.GetBooksAsync(token);
            var since = incremental && _readestEvents.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(_readestEvents.Max(e => e.StartTime)) : (DateTimeOffset?)null;
            var eventsTask = _repository.GetEventsAsync(since, cancellationToken: token);
            var notesTask = _notesRepository?.LoadAsync(token) ?? Task.FromResult<IReadOnlyList<ReadestNote>>([]);
            await Task.WhenAll(booksTask, eventsTask, notesTask);
            _readestBookModels = await booksTask;
            var fresh = await eventsTask;
            _readestNotes = await notesTask;
            _readestEvents = since is null ? fresh : _readestEvents.Concat(fresh).GroupBy(e => (e.BookId, e.Page, e.StartTime)).Select(g => g.Last()).OrderBy(e => e.StartTime).ToArray();
            ApplySourceSelection(false);
            Diagnostics = (await _repository.ValidateAsync(token)).Diagnostics;
            if (_goalEngine.SyncYearArchives(_settings, _events, completedBooks: CompletedBookDates())) await _store.SaveAsync(_settings, token);
            BuildNotes();
            RecalculateAll();
            LastSync = DateTimeOffset.Now;
            Status = "Readest connected";
            _log($"Refresh complete · {_readestEvents.Count} Readest events · {Manual.AsReadingEvents().Count} manual sessions · {_bookModels.Count} books · {_readestNotes.Count} notes · {_repository.DatabasePath}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "Could not refresh Readest data. " + Friendly(ex); Status = "Last good data retained"; _log("Refresh error: " + ex); }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private void RecalculateAll()
    {
        Raise(nameof(IsCustomRange)); if (_events.Count == 0) { ClearAnalytics(); if (_bookModels.Count > 0) ApplyBookFilters(); BuildDataQuality(); return; }
        var first = _events.Min(e => e.Start); _resolvedRange = _ranges.Resolve(SelectedRange, DateTimeOffset.Now, first, _settings.WeekStartsMonday, CustomStart is null ? null : DateOnly.FromDateTime(CustomStart.Value), CustomEnd is null ? null : DateOnly.FromDateTime(CustomEnd.Value)); _periodEvents = _analytics.Filter(_events, _resolvedRange); _periodSessions = _statistics.BuildSessions(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes)).Where(s => s.DurationSeconds >= MinimumSessionSeconds).ToArray();
        Period = _analytics.Metrics(_events, _resolvedRange, SessionGapMinutes); Comparison = _analytics.Compare(_events, _resolvedRange); SessionProfile = _analytics.Sessions(_periodSessions); WeekdayWeekend = _analytics.WeekdayWeekend(_events, _resolvedRange); CommonWindow = _analytics.CommonWindow(_events, _resolvedRange); Raise(nameof(CommonWindowLabel)); Raise(nameof(ConsistencyDetail));
        _allDaily = _statistics.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)); _allSessions = _statistics.BuildSessions(_events, TimeSpan.FromMinutes(SessionGapMinutes)); AllSessionCount = _allSessions.Count; var streaks = _statistics.Streaks(_allDaily.Select(d => d.Date), DateOnly.FromDateTime(DateTime.Today)); CurrentStreak = streaks.Current; LongestStreak = streaks.Longest; Raise(nameof(CurrentStreak)); Raise(nameof(LongestStreak)); Raise(nameof(CurrentStreakLabel)); Raise(nameof(LongestStreakLabel)); Raise(nameof(StreakContinuation)); Raise(nameof(ComparisonText)); Raise(nameof(AbsoluteDeltaText));
        TopBookLabel = _statistics.RankBooks(_periodEvents, _bookModels).FirstOrDefault()?.Title ?? "No active book"; Raise(nameof(TopBookLabel));
        BuildTrend(); BuildHeatmap(_allDaily); BuildPatterns(); BuildStory(_allDaily); BuildGoals(); BuildRecordsAndInsights(); BuildInfographics(_allDaily); BuildDataQuality(); RefreshSelectedPage(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(ReadingPeriodSummary)); Raise(nameof(SessionsPerActiveDay)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData));
    }

    private void ApplySourceSelection(bool recalculate = true)
    {
        var manualEvents = Manual.AsReadingEvents();
        var manualBooks = Manual.AsBooks();
        var manualWithoutReadestOverlap = ReadingEventMerger.ExcludeOverlaps(manualEvents, _readestEvents);
        var linkedManualToReadest = _settings.BookLinks
            .Select(link => (ManualId: ParseManualKey(link.ManualKey), Readest: _readestBookModels.FirstOrDefault(book => BookKey(book).Equals(link.ReadestKey, StringComparison.OrdinalIgnoreCase))))
            .Where(item => item.ManualId is not null && item.Readest is not null)
            .ToDictionary(item => item.ManualId!.Value, item => item.Readest!);
        var combinedManualEvents = manualWithoutReadestOverlap.Select(item => linkedManualToReadest.TryGetValue(item.BookId, out var readest) ? item with { BookId = readest.Id } : item).ToArray();
        var linkedReadestIds = linkedManualToReadest.Values.Select(book => book.Id).ToHashSet();
        var linkedManualIds = linkedManualToReadest.Keys.ToHashSet();
        var combinedBooks = _readestBookModels.Select(book => linkedReadestIds.Contains(book.Id) ? book with { Source = "Linked" } : book)
            .Concat(manualBooks.Where(book => !linkedManualIds.Contains(book.Id))).ToArray();
        _events = SelectedSource switch
        {
            "Readest" => _readestEvents,
            "Manual" => manualEvents,
            _ => _readestEvents.Concat(combinedManualEvents).OrderBy(item => item.StartTime).ToArray()
        };
        _bookModels = SelectedSource switch
        {
            "Readest" => _readestBookModels,
            "Manual" => manualBooks,
            _ => combinedBooks
        };
        if (recalculate) RecalculateAll();
    }

    private void RefreshSelectedPage()
    {
        if (_resolvedRange is null) return;
        switch (SelectedPage)
        {
            case "Activity": BuildActivity(); break;
            case "Sessions": ApplySessionFilters(); break;
            case "Books": ApplyBookFilters(); break;
            case "Statistics": BuildStatistics(); break;
            case "Year in Reading": BuildYear(); break;
        }
    }

    private void BuildTrend() { if (_resolvedRange is null) return; Replace(Trend, _analytics.AggregateTrend(_events, _resolvedRange, TrendMetric, TrendGranularity, SessionGapMinutes)); Raise(nameof(TrendSummary)); }
    private void BuildHeatmap(IReadOnlyList<DailyStat> daily)
    {
        var map = daily.ToDictionary(d => d.Date); var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-364)); Replace(Heatmap, Enumerable.Range(0, 365).Select(i => { var date = start.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : $"{date:MMM d, yyyy}\n{Formatters.Duration(d.Seconds)} · {Formatters.Count(d.Sessions, "session")} · {Formatters.Count(d.Books, "book")}", "min"); }));
    }

    private void BuildPatterns()
    {
        if (_resolvedRange is null) return; var sessions = _periodSessions; var total = _periodEvents.Sum(e => e.DurationSeconds);
        var hourly = Enumerable.Range(0, 24).Select(h => { double value = PatternMetric == "Sessions" ? sessions.Count(s => _statistics.ToLocal(s.Start).Hour == h) : _periodEvents.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds) / 60; return new ChartPoint(FormatHour(h), value, PatternMetric == "Time" ? Formatters.Duration(value * 60) : Formatters.Count((int)value, "session"), PatternMetric == "Time" ? "min" : "sessions"); }).ToArray();
        Replace(Hourly, hourly);
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var weekdays = order.Select(day => { double value = PatternMetric == "Sessions" ? sessions.Count(s => _statistics.ToLocal(s.Start).DayOfWeek == day) : _periodEvents.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds) / 60; return new ChartPoint(day.ToString()[..3], value, PatternMetric == "Time" ? Formatters.Duration(value * 60) : Formatters.Count((int)value, "session")); }).ToArray();
        Replace(Weekdays, weekdays);
        var windows = Enumerable.Range(0, 24).Select(hour => new { Hour = hour, Value = hourly[hour].Value + hourly[(hour + 1) % 24].Value }).ToArray();
        var best = windows.MaxBy(item => item.Value)!;
        var measuredTotal = hourly.Sum(item => item.Value);
        PatternWindowLabel = measuredTotal <= 0 ? "—" : FormatHourRange(best.Hour, 2);
        PatternSummary = measuredTotal <= 0
            ? $"No {(PatternMetric == "Sessions" ? "sessions" : "reading activity")} in this period."
            : PatternMetric == "Sessions"
                ? $"{Formatters.Count((int)best.Value, "session")} started here · {best.Value / measuredTotal:P0} of selected-period sessions"
                : $"{Formatters.Duration(best.Value * 60)} here · {best.Value / measuredTotal:P0} of selected-period reading";
        var bestWeekday = weekdays.MaxBy(item => item.Value)!;
        PatternWeekdaySummary = bestWeekday.Value <= 0 ? "No activity in this period." : PatternMetric == "Sessions" ? $"Most sessions on {bestWeekday.Label} · {bestWeekday.Detail}" : $"Most reading on {bestWeekday.Label} · {bestWeekday.Detail}";
        Raise(nameof(PatternWindowLabel)); Raise(nameof(PatternSummary)); Raise(nameof(PatternWeekdaySummary));
    }

    private void BuildStory(IReadOnlyList<DailyStat> allDaily)
    {
        if (_resolvedRange is null || Comparison is null) return;
        Story = _visualization.Build(_events, _resolvedRange, Comparison, SessionGapMinutes, precomputedDaily: allDaily);
        Replace(StreakTimeline, Story.StreakTimeline);
        Replace(CumulativeJourney, Story.CumulativeJourney);
        Replace(MonthlyJourney, Story.MonthlyJourney);
        Replace(ComparisonBars, Story.ComparisonBars);
    }

    private void BuildRecordsAndInsights()
    {
        if (_resolvedRange is null) return; var overview = _statistics.OverviewForRange(_periodEvents, _bookModels, _resolvedRange, TimeSpan.FromMinutes(SessionGapMinutes)); Replace(Records, overview.Records); Replace(SessionHistogram, SessionProfile?.Histogram ?? []); var goalProgress = Goals.Select(g => g.Progress).ToArray(); var insights = _insightEngine.Generate(_events, _bookModels, _resolvedRange, SessionGapMinutes, goalProgress, Use24HourTime); var selectedId = SelectedInsight?.Id; Replace(AllInsights, insights); Replace(QuickInsights, insights.Take(5)); SelectedInsight = AllInsights.FirstOrDefault(i => i.Id == selectedId) ?? AllInsights.FirstOrDefault();
    }

    private void BuildStatistics()
    {
        if (_resolvedRange is null) return;
        var ranked = _statistics.RankBooks(_periodEvents, _bookModels);
        Replace(StatisticsTopBooks, ranked.Take(10).Select(summary =>
        {
            var sessions = _statistics.BuildSessions(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes), summary.Id);
            var model = _bookModels.FirstOrDefault(book => book.Id == summary.Id);
            var manualBook = model?.Source is "Manual" or "Linked" ? ManualBookFor(summary.Id) : null;
            return new BookRow(summary, sessions.Count, sessions.Count == 0 ? 0 : sessions.Average(item => item.DurationSeconds), "—", model?.Source == "Manual" ? model.CoverUrl : ReadestLibraryLocator.FindCoverFile(Diagnostics?.Path, model?.Hash), BookStatus(summary.Id), false, model?.Source ?? "Readest", manualBook?.ProgressPercent ?? 0, manualBook is null ? "" : manualBook.TotalPages is > 0 ? $"{manualBook.CurrentPage ?? 0} / {manualBook.TotalPages} pages" : $"Page {manualBook.CurrentPage ?? 0}");
        }));

        double SecondsFor(params int[] hours) => _periodEvents.Where(item => hours.Contains(_statistics.ToLocal(item.Start).Hour)).Sum(item => item.DurationSeconds);
        Replace(TimeBlocks, new[]
        {
            new ChartPoint("Morning", SecondsFor(5, 6, 7, 8, 9, 10, 11) / 60, Formatters.Duration(SecondsFor(5, 6, 7, 8, 9, 10, 11))),
            new ChartPoint("Afternoon", SecondsFor(12, 13, 14, 15, 16) / 60, Formatters.Duration(SecondsFor(12, 13, 14, 15, 16))),
            new ChartPoint("Evening", SecondsFor(17, 18, 19, 20, 21) / 60, Formatters.Duration(SecondsFor(17, 18, 19, 20, 21))),
            new ChartPoint("Night", SecondsFor(22, 23, 0, 1, 2, 3, 4) / 60, Formatters.Duration(SecondsFor(22, 23, 0, 1, 2, 3, 4)))
        });

        var completed = CompletedBookDates().Select(_statistics.ToLocal).ToArray();
        var currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        Replace(CompletionTimeline, Enumerable.Range(0, 12).Select(offset => currentMonth.AddMonths(offset - 11)).Select(month =>
            new ChartPoint(month.ToString("MMM yy"), completed.Count(date => date.Year == month.Year && date.Month == month.Month), Formatters.Count(completed.Count(date => date.Year == month.Year && date.Month == month.Month), "book"))));
        Raise(nameof(FinishedInRange)); Raise(nameof(FinishedThisYear)); Raise(nameof(FinishedAllTime));
    }

    private IEnumerable<SessionDisplay> SessionRows(IEnumerable<ReadingSession> sessions)
    {
        var titles = _bookModels.ToDictionary(b => b.Id, b => string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title);
        return sessions.Select(s =>
        {
            var sourceLabel = s.Sources is { Count: > 0 } ? string.Join(" + ", s.Sources) : "Readest";
            var manual = s.BookIds.Count == 1 && sourceLabel.Equals("Manual", StringComparison.OrdinalIgnoreCase) ? Manual.FindSession(s.Start, s.BookIds[0]) : null;
            var pageRange = manual?.StartPage is not null && manual.EndPage is not null ? $"pp. {manual.StartPage}–{manual.EndPage}" : "—";
            return new SessionDisplay(_statistics.ToLocal(s.Start), _statistics.ToLocal(s.End), s.DurationSeconds, s.BookIds.Count == 1 ? titles.GetValueOrDefault(s.BookIds[0], "Unknown book") : $"{s.BookIds.Count} books", s.EventCount, s, sourceLabel, pageRange);
        });
    }

    private void ApplySessionFilters()
    {
        var rows = SessionRows(_periodSessions); if (SessionBook.Id is { } selectedBookId) rows = rows.Where(s => s.Source.BookIds.Contains(selectedBookId)); if (!string.IsNullOrWhiteSpace(SessionSearch)) rows = rows.Where(s => s.Books.Contains(SessionSearch, StringComparison.CurrentCultureIgnoreCase)); rows = SessionDuration switch { "<5m" => rows.Where(s => s.DurationSeconds < 300), "5–15m" => rows.Where(s => s.DurationSeconds >= 300 && s.DurationSeconds < 900), "15–30m" => rows.Where(s => s.DurationSeconds >= 900 && s.DurationSeconds < 1800), "30–60m" => rows.Where(s => s.DurationSeconds >= 1800 && s.DurationSeconds < 3600), "60m+" => rows.Where(s => s.DurationSeconds >= 3600), _ => rows }; rows = SessionSort switch { "Oldest" => rows.OrderBy(s => s.Start), "Longest" => rows.OrderByDescending(s => s.DurationSeconds), "Shortest" => rows.OrderBy(s => s.DurationSeconds), _ => rows.OrderByDescending(s => s.Start) }; Replace(Sessions, rows); var selectedBook = SessionBook; var bookOptions = new[] { new BookFilterOption(null, "All books") }.Concat(_bookModels.Where(book => !string.IsNullOrWhiteSpace(book.Title)).OrderBy(book => book.Title).ThenBy(book => book.Authors).Select(book => new BookFilterOption(book.Id, string.IsNullOrWhiteSpace(book.Authors) ? book.Title : $"{book.Title} — {book.Authors}"))).ToArray(); SyncSessionBookOptions(bookOptions); _sessionBook = bookOptions.FirstOrDefault(option => option.Id == selectedBook.Id) ?? bookOptions[0]; Raise(nameof(SessionBook)); if (SelectedSession is null || !Sessions.Any(s => s.Source == SelectedSession.Source)) SelectedSession = Sessions.FirstOrDefault();
    }

    private void SyncSessionBookOptions(IReadOnlyCollection<BookFilterOption> desired)
    {
        for (var i = SessionBookOptions.Count - 1; i >= 0; i--) if (!desired.Contains(SessionBookOptions[i])) SessionBookOptions.RemoveAt(i);
        foreach (var item in desired) if (!SessionBookOptions.Contains(item)) SessionBookOptions.Add(item);
    }

    private void BuildSessionDetail()
    {
        if (SelectedSession is null) { SessionTimeline.Clear(); return; } var start = SelectedSession.Source.Start; var end = SelectedSession.Source.End; var items = _periodEvents.Where(e => e.Start >= start && e.Start <= end).OrderBy(e => e.Start).Select(e => new ChartPoint(FormatTime(_statistics.ToLocal(e.Start), true), e.DurationSeconds / 60, Formatters.Duration(e.DurationSeconds), "min")); Replace(SessionTimeline, items);
    }

    private void ApplyBookFilters()
    {
        var statusMap = new Dictionary<string, string>(StringComparer.Ordinal) { ["Currently reading"] = "Reading", ["Want to read"] = "Want to read", ["Paused"] = "Paused" };
        var isStatusFilter = statusMap.TryGetValue(BookFilter, out var requestedStatus);
        var sourceEvents = _periodEvents;
        var summaries = _statistics.RankBooks(sourceEvents, _bookModels).ToList();
        if (BookFilter != "Active in range")
        {
            var activeIds = summaries.Select(summary => summary.Id).ToHashSet();
            summaries.AddRange(_bookModels
                .Where(book => !activeIds.Contains(book.Id))
                .Select(book => new BookSummary(
                    book.Id,
                    string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title,
                    string.IsNullOrWhiteSpace(book.Authors) ? "Unknown author" : book.Authors,
                    0, 0, null, null, 0, 0, null, null, 0)));
        }
        var rows = summaries.Select(summary =>
        {
            var sessions = _statistics.BuildSessions(sourceEvents.Where(e => e.BookId == summary.Id), TimeSpan.FromMinutes(SessionGapMinutes), summary.Id);
            var hour = sourceEvents.Where(e => e.BookId == summary.Id).GroupBy(e => _statistics.ToLocal(e.Start).Hour).MaxBy(g => g.Sum(e => e.DurationSeconds))?.Key;
            var model = _bookModels.FirstOrDefault(book => book.Id == summary.Id);
            var key = BookTrackingKey(summary.Id);
            var manualBook = model?.Source is "Manual" or "Linked" ? ManualBookFor(summary.Id) : null;
            return new BookRow(
                summary,
                sessions.Count,
                sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds),
                hour is null ? "—" : FormatHour(hour.Value),
                model?.Source == "Manual" ? model.CoverUrl : ReadestLibraryLocator.FindCoverFile(Diagnostics?.Path, model?.Hash),
                BookStatus(summary.Id),
                key is not null && _settings.PinnedBookKeys.Contains(key),
                model?.Source ?? "Readest",
                manualBook?.ProgressPercent ?? 0,
                manualBook is null ? "" : manualBook.TotalPages is > 0 ? $"{manualBook.CurrentPage ?? 0} / {manualBook.TotalPages} pages" : $"Page {manualBook.CurrentPage ?? 0}");
        });
        if (BookFilter == "Recently read") rows = rows.Where(b => b.LastRead >= DateTimeOffset.Now.AddDays(-30));
        if (BookFilter == "Pinned") rows = rows.Where(book => book.IsPinned);
        if (isStatusFilter) rows = rows.Where(book => book.Status == requestedStatus);
        if (BookFilter == "Finished this year") rows = rows.Where(book => book.Status == "Finished" && _settings.BookTracking.TryGetValue(BookTrackingKey(book.Id) ?? "", out var state) && state.CompletedAtUtc?.ToLocalTime().Year == DateTime.Today.Year);
        if (BookFilter == "Physical books") rows = rows.Where(book => book.Source == "Manual");
        if (BookFilter == "Readest books") rows = rows.Where(book => book.Source == "Readest");
        if (BookFilter == "Without recent activity") rows = rows.Where(book => book.LastRead is null || book.LastRead < DateTimeOffset.Now.AddDays(-30));
        if (BookFilter == "Incomplete metadata") rows = rows.Where(book => string.IsNullOrWhiteSpace(book.Authors) || book.Authors == "Unknown author" || _bookModels.FirstOrDefault(model => model.Id == book.Id)?.Pages is null);
        if (!string.IsNullOrWhiteSpace(BookSearch)) rows = rows.Where(b => b.Title.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase) || b.Authors.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase));
        rows = BookSort switch { "Sessions" => rows.OrderByDescending(b => b.Sessions), "Active days" => rows.OrderByDescending(b => b.ActiveDays), "Recently read" => rows.OrderByDescending(b => b.LastRead), "Title" => rows.OrderBy(b => b.Title), _ => rows.OrderByDescending(b => b.Seconds) };
        Replace(Books, rows);
        TopBookLabel = Books.FirstOrDefault()?.Title ?? "No active book";
        Raise(nameof(TopBookLabel));
        SelectedBook = SelectedBook is null ? Books.FirstOrDefault() : Books.FirstOrDefault(book => book.Id == SelectedBook.Id) ?? Books.FirstOrDefault();
        BuildContinueReading();
    }

    private void BuildBookDetail()
    {
        if (SelectedBook is null) { BookDetail = null; BookTrend.Clear(); BookHourly.Clear(); BookWeekdays.Clear(); BookSessions.Clear(); _selectedBookStatus = "Unspecified"; Raise(nameof(SelectedBookStatus)); Raise(nameof(SelectedBookStatusDetail)); BuildSelectedPlan(); return; } var events = _periodEvents.Where(e => e.BookId == SelectedBook.Id).ToArray(); BookDetail = _statistics.BookDetails(SelectedBook.Summary, events, SessionGapMinutes); var daily = BookDetail.Daily; Replace(BookTrend, daily.Select(d => new ChartPoint(d.Date.ToString("MMM d"), d.Seconds / 60, Formatters.Duration(d.Seconds), "min"))); Replace(BookHourly, Enumerable.Range(0, 24).Select(h => new ChartPoint(FormatHour(h), events.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds) / 60, Unit: "min"))); var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }; Replace(BookWeekdays, order.Select(day => new ChartPoint(day.ToString()[..3], events.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds) / 60, Unit: "min"))); Replace(BookSessions, SessionRows(BookDetail.SessionHistory)); _selectedBookStatus = BookStatus(SelectedBook.Id); Raise(nameof(SelectedBookStatus)); Raise(nameof(SelectedBookStatusDetail)); Raise(nameof(SelectedBookCompletedDate)); BuildSelectedPlan();
    }

    private void BuildActivity()
    {
        var monthStart = new DateOnly(Month.Year, Month.Month, 1); var gridStart = monthStart.AddDays(-((_settings.WeekStartsMonday ? (int)monthStart.DayOfWeek + 6 : (int)monthStart.DayOfWeek) % 7)); Replace(WeekdayHeaders, _settings.WeekStartsMonday ? new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" } : new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }); var daily = _allDaily.ToDictionary(d => d.Date); var sessions = _allSessions; var max = daily.Where(x => x.Key.Year == Month.Year && x.Key.Month == Month.Month).Select(x => x.Value.Seconds).DefaultIfEmpty().Max(); Replace(CalendarDays, Enumerable.Range(0, 42).Select(i => { var date = gridStart.AddDays(i); var d = daily.GetValueOrDefault(date); var value = d?.Seconds ?? 0; var level = value <= 0 || max <= 0 ? "None" : (value / max) switch { < .25 => "Low", < .5 => "Medium", < .75 => "High", _ => "Peak" }; return new CalendarDayItem(date, date.Month == Month.Month, value, sessions.Count(s => DateOnly.FromDateTime(_statistics.ToLocal(s.Start).DateTime) == date), d?.Books ?? 0, level, value <= 0 ? "" : Formatters.Duration(value)); })); Replace(ActivityMonthTrend, Enumerable.Range(0, DateTime.DaysInMonth(Month.Year, Month.Month)).Select(index => { var date = monthStart.AddDays(index); var stat = daily.GetValueOrDefault(date); return new ChartPoint(date.Day.ToString(), (stat?.Seconds ?? 0) / 60d, stat is null ? $"{date:MMM d} · No activity" : $"{date:MMM d} · {Formatters.Duration(stat.Seconds)}", "min"); })); if (SelectedDate.Month != Month.Month || SelectedDate.Year != Month.Year) SelectedDate = monthStart; BuildSelectedDay(); Raise(nameof(MonthSummary));
    }

    public string MonthSummary { get { var items = CalendarDays.Where(d => d.IsInMonth && d.Seconds > 0).ToArray(); return items.Length == 0 ? "No reading activity this month." : $"{Formatters.Duration(items.Sum(d => d.Seconds))} · {Formatters.Count(items.Length, "active day")} · {Formatters.Count(items.Sum(d => d.Sessions), "session")} · {Formatters.Count(_events.Where(e => { var d = _statistics.ToLocal(e.Start); return d.Year == Month.Year && d.Month == Month.Month; }).Select(e => e.BookId).Distinct().Count(), "book active", "books active")}"; } }
    private void BuildSelectedDay()
    {
        var sessions = _allSessions.Where(s => DateOnly.FromDateTime(_statistics.ToLocal(s.Start).DateTime) == SelectedDate).ToArray(); var events = _events.Where(e => DateOnly.FromDateTime(_statistics.ToLocal(e.Start).DateTime) == SelectedDate).ToArray(); var rows = SessionRows(sessions).ToArray(); var titles = _bookModels.ToDictionary(b => b.Id, b => b.Title); var books = events.GroupBy(e => e.BookId).Select(g => new ChartPoint(titles.GetValueOrDefault(g.Key, "Unknown book"), g.Sum(e => e.DurationSeconds), Formatters.Duration(g.Sum(e => e.DurationSeconds)))).OrderByDescending(x => x.Value).ToArray(); SelectedDay = new(SelectedDate, events.Sum(e => e.DurationSeconds), sessions.Length, events.Select(e => e.BookId).Distinct().Count(), rows.FirstOrDefault()?.Start, rows.LastOrDefault()?.End, rows, books); Replace(DaySessions, rows); Replace(DayBooks, books); Replace(DayTimeline, _infographics.DayTimeline(_events, _bookModels, SelectedDate, SessionGapMinutes));
    }

    private void BuildGoals()
    {
        foreach (var editor in Goals) editor.Commit(); var definitions = GoalEngine.Migrate(_settings); var completed = CompletedBookDates(); Replace(Goals, definitions.Select(d => new GoalEditor(d, _goalEngine.Progress(d, _events, SessionGapMinutes, completedBooks: completed), _goalEngine.History(d, _events, SessionGapMinutes, completedBooks: completed)))); Replace(GoalArchives, _settings.GoalArchives.OrderByDescending(a => a.Year)); CurrentYearBookGoal = CreateYearBookGoal(DateTime.Today.Year);
        var yearlyGoal = definitions.First(goal => goal.Period == GoalPeriod.Yearly && goal.Metric == GoalMetric.Books);
        Replace(GoalPace, _infographics.YearGoalPace(completed, DateTime.Today.Year, yearlyGoal.TargetValue, DateOnly.FromDateTime(DateTime.Today)));
    }

    private void BuildYear()
    {
        YearSummary = _analytics.Year(_events, _bookModels, Year, SessionGapMinutes); Replace(YearMonths, YearSummary.Months); Replace(YearHeatmap, YearSummary.Heatmap); Replace(YearTopBooks, YearSummary.TopBooks); YearBookGoal = CreateYearBookGoal(Year); Raise(nameof(YearBestMonth)); Raise(nameof(YearBestDay)); Raise(nameof(YearTopBook)); Raise(nameof(YearLongestStreakLabel)); Raise(nameof(YearFavoriteHour));
        Replace(BestReadingDays, _infographics.BestDays(_events, Year, SessionGapMinutes));
        var yearEvents = _events.Where(item => _statistics.ToLocal(item.Start).Year == Year).ToArray();
        Replace(YearBookAttention, _infographics.BookAttention(yearEvents, _bookModels));
    }

    private void BuildInfographics(IReadOnlyList<DailyStat> allDaily)
    {
        if (_resolvedRange is null) return;
        Replace(WeekHourMatrix, _infographics.WeekHourMatrix(_events, _resolvedRange, SessionGapMinutes, PatternMetric == "Sessions"));
        Replace(SessionDots, _infographics.SessionDots(_periodSessions, _bookModels));
        Replace(ReadingStyleDays, _infographics.ReadingStyleDays(_events, _resolvedRange, SessionGapMinutes));
        Replace(BookAttention, _infographics.BookAttention(_periodEvents, _bookModels));
        var sourceTotals = _periodEvents.GroupBy(item => item.Source).OrderByDescending(group => group.Sum(item => item.DurationSeconds)).ToArray();
        Replace(SourceComposition, sourceTotals.Select((group, index) => new CompositionPart(group.Key, group.Sum(item => item.DurationSeconds), $"{group.Key}: {Formatters.Duration(group.Sum(item => item.DurationSeconds))}", index)));
        var periodStart = _resolvedRange.Start;
        var periodEnd = _resolvedRange.End;
        Replace(PhysicalPageProgress, Manual.Sessions
            .Where(session => session.EndPage is not null && session.EndedAtUtc >= periodStart && session.StartedAtUtc <= periodEnd)
            .OrderBy(session => session.EndedAtUtc)
            .Select(session => new ChartPoint(_statistics.ToLocal(session.EndedAtUtc).ToString("MMM d"), session.EndPage!.Value, $"{Manual.FindBook(session.BookId)?.Title ?? "Physical book"} · page {session.EndPage}", "page")));
        Raise(nameof(HasPhysicalPageProgress));
        Raise(nameof(SourceCompositionSummary));
        Replace(BookDayMatrix, _infographics.BookPeriodMatrix(_events, _bookModels, _resolvedRange));
        Replace(ReadingFingerprint, _infographics.Fingerprint(allDaily, DateOnly.FromDateTime(DateTime.Today)));
        Replace(RollingMomentum, _infographics.RollingMomentum(_events, _resolvedRange, SessionGapMinutes));
        Replace(PeriodDumbbells, _infographics.PeriodDumbbells(_events, _resolvedRange, SessionGapMinutes));
        Replace(SessionStaircase, _infographics.SessionStaircase(_periodSessions));
    }

    private void BuildDataQuality()
    {
        var report = _dataQualityEngine.Analyze(_bookModels, _events, Diagnostics, DateTimeOffset.Now);
        var extraChecks = new List<DataQualityCheck>();
        try
        {
            var database = Manual.DatabaseHealth;
            var healthy = database.IntegrityStatus.Equals("ok", StringComparison.OrdinalIgnoreCase);
            AppDatabaseSummary = $"Schema {database.SchemaVersion} · {Formatters.Count(database.Books, "book")} · {Formatters.Count(database.Sessions, "session")} · integrity {database.IntegrityStatus}";
            extraChecks.Add(new("Readest Stats database", healthy ? "Pass" : "Attention", AppDatabaseSummary, database.Path, !healthy));
            Raise(nameof(AppDatabaseSummary)); Raise(nameof(AppDatabasePath));
        }
        catch (Exception ex)
        {
            AppDatabaseSummary = "App database check failed.";
            extraChecks.Add(new("Readest Stats database", "Attention", AppDatabaseSummary, ex.Message, true));
            Raise(nameof(AppDatabaseSummary)); Raise(nameof(AppDatabasePath));
        }
        if (_notesRepository?.LastDiagnostics is { } notes)
        {
            var noteCheck = new DataQualityCheck("Readest note files", notes.FilesFailed == 0 ? "Pass" : "Attention", notes.Summary, "Read-only Books/**/config.json scan", notes.FilesFailed > 0);
            extraChecks.Add(noteCheck);
        }
        var invalidLinks = _settings.BookLinks.Count(link => ParseManualKey(link.ManualKey) is not { } manualId || Manual.FindBook(manualId) is null || !_readestBookModels.Any(book => BookKey(book).Equals(link.ReadestKey, StringComparison.OrdinalIgnoreCase)));
        extraChecks.Add(new("Edition links", invalidLinks == 0 ? "Pass" : "Attention", invalidLinks == 0 ? "All linked editions resolve to both local books." : $"{invalidLinks} edition link(s) no longer resolve.", "App-owned edition map only; Readest remains read-only", invalidLinks > 0));
        var overlapping = Manual.Sessions.GroupBy(session => session.BookId).Sum(group => group.OrderBy(session => session.StartedAtUtc).Zip(group.OrderBy(session => session.StartedAtUtc).Skip(1), (left, right) => left.EndedAtUtc > right.StartedAtUtc ? 1 : 0).Sum());
        extraChecks.Add(new("Manual session timeline", overlapping == 0 ? "Pass" : "Attention", overlapping == 0 ? "No overlapping physical-reading sessions." : $"{overlapping} overlapping session pair(s) need review.", "Manual session start and end timestamps", overlapping > 0));
        if (extraChecks.Count > 0)
        {
            var checks = report.Checks.Concat(extraChecks).ToArray();
            var attention = checks.Count(check => check.NeedsAttention);
            var passed = checks.Length - attention;
            var penalty = extraChecks.Count(check => check.NeedsAttention) * 8;
            report = new(Math.Max(0, report.Score - penalty), attention == 0 ? report.Status : "Needs attention", passed, attention, attention == 0 ? report.Summary : $"{report.Summary} App-owned library data also needs attention.", checks);
        }
        DataQuality = report; Replace(DataQualityChecks, report.Checks);
    }

    private void BuildNotes()
    {
        var selectedId = _selectedNote?.Id;
        var randomId = _randomNote?.Id;
        var noteSources = _readestNotes.Concat(Manual.AsNotes()).ToArray();
        _noteDiscovery.Update(noteSources.Select(note => KeyValuePair.Create(note.Id, note.BookHash))); PreviousRandomNoteCommand.Refresh();
        _allNoteRows.Clear();
        foreach (var source in noteSources)
        {
            if (!_settings.NoteStates.TryGetValue(source.Id, out var state))
            {
                state = new();
                _settings.NoteStates[source.Id] = state;
            }
            var row = new NoteRow(source, state);
            row.PropertyChanged += NoteRowChanged;
            _allNoteRows.Add(row);
        }

        ApplyNoteFilters();
        BuildNoteAnalytics();
        DailyNote = ChooseDailyNote();
        RandomNote = randomId is null
            ? ChooseRandomNote()
            : _allNoteRows.FirstOrDefault(note => note.Id.Equals(randomId, StringComparison.OrdinalIgnoreCase)) ?? ChooseRandomNote();
        _lastRandomNoteId = RandomNote?.Id;

        _selectedNote = selectedId is null
            ? DailyNote ?? RandomNote ?? Notes.FirstOrDefault()
            : _allNoteRows.FirstOrDefault(note => note.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? DailyNote ?? RandomNote ?? Notes.FirstOrDefault();
        Raise(nameof(SelectedNote));

        NotesStatus = noteSources.Length == 0
            ? "No Readest or physical-book notes found."
            : $"{Formatters.Count(noteSources.Length, "note")} indexed · Readest source remains read-only.";
        Raise(nameof(NotesCountLabel));
        Raise(nameof(HasNoVisibleNotes));
        Raise(nameof(NotesEmptyMessage));
        Raise(nameof(TotalNotes));
        Raise(nameof(NotesSummary));
        Raise(nameof(FavoriteNotesCount));
        RandomNoteCommand.Refresh();
        DailyNoteCommand.Refresh();
        RefreshNotesCommand.Refresh();
        OpenSelectedNoteCommand.Refresh();
        SaveSelectedNoteCommand.Refresh();
        ExportNotesMarkdownCommand.Refresh();
        ExportNotesJsonCommand.Refresh();
        ExportNotesCsvCommand.Refresh();
    }

    private void ApplyNoteFilters()
    {
        var rows = _allNoteRows.AsEnumerable();
        rows = NoteScope switch
        {
            "Readest only" => rows.Where(note => !note.Id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase)),
            "Physical books only" => rows.Where(note => note.Id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase)),
            "Unseen" => rows.Where(note => note.TimesSeen == 0),
            "Not seen recently" => rows.Where(note => note.LastSeenUtc is null || note.LastSeenUtc < DateTimeOffset.UtcNow.AddDays(-30)),
            "Favorites only" => rows.Where(note => note.IsFavorite),
            "Selected book" when SelectedNote is not null => rows.Where(note => note.BookHash.Equals(SelectedNote.BookHash, StringComparison.OrdinalIgnoreCase)),
            _ => rows
        };
        if (!string.IsNullOrWhiteSpace(NoteSearch)) rows = rows.Where(note => note.BookTitle.Contains(NoteSearch, StringComparison.CurrentCultureIgnoreCase) || note.Authors.Contains(NoteSearch, StringComparison.CurrentCultureIgnoreCase) || note.Text.Contains(NoteSearch, StringComparison.CurrentCultureIgnoreCase) || note.Note.Contains(NoteSearch, StringComparison.CurrentCultureIgnoreCase) || note.PersonalNote.Contains(NoteSearch, StringComparison.CurrentCultureIgnoreCase));
        Replace(Notes, rows.OrderByDescending(note => note.SortDate));
        if (_selectedNote is not null && !Notes.Contains(_selectedNote))
        {
            _selectedNote = Notes.FirstOrDefault();
            Raise(nameof(SelectedNote));
        }
        Raise(nameof(NotesCountLabel));
        Raise(nameof(HasNoVisibleNotes));
        Raise(nameof(NotesEmptyMessage));
        Raise(nameof(FavoriteNotesCount)); Raise(nameof(NotesReadCount));
        RandomNoteCommand.Refresh();
        RandomSameBookCommand.Refresh();
        ExportNotesMarkdownCommand.Refresh();
        ExportNotesJsonCommand.Refresh();
        ExportNotesCsvCommand.Refresh();
    }

    private void BuildNoteAnalytics()
    {
        Replace(NotesByBook, _allNoteRows
            .GroupBy(note => note.BookTitle)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(8)
            .Select(group => new ChartPoint(group.Key, group.Count(), $"{Formatters.Count(group.Count(), "note")}", "notes")));
        var now = DateTimeOffset.Now;
        Replace(NotesByMonth, Enumerable.Range(0, 12).Select(offset =>
        {
            var month = new DateTime(now.Year, now.Month, 1).AddMonths(offset - 11);
            var count = _allNoteRows.Count(note =>
            {
                var date = note.SortDate.ToLocalTime();
                return date.Year == month.Year && date.Month == month.Month;
            });
            return new ChartPoint(month.ToString("MMM yy"), count, Formatters.Count(count, "note"), "notes");
        }));
        Replace(NotesByType, _allNoteRows.GroupBy(note => note.TypeLabel).OrderByDescending(group => group.Count()).Select(group => new ChartPoint(group.Key, group.Count(), Formatters.Count(group.Count(), "note"), "notes")));
    }

    private void NoteRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (sender is not NoteRow note) return;
        Raise(nameof(FavoriteNotesCount));
        ScheduleNoteSave(note);
    }

    private void ScheduleNoteSave(NoteRow note)
    {
        _noteSaveDebounce?.Cancel(); _noteSaveDebounce?.Dispose(); _noteSaveDebounce = new(); var token = _noteSaveDebounce.Token;
        _ = Application.Current.Dispatcher.InvokeAsync(async () => { try { await Task.Delay(350, token); await SaveNoteStateAsync(note); NotesStatus = "Note settings saved locally."; } catch (OperationCanceledException) { } });
    }

    private void SchedulePreferenceSave()
    {
        _preferenceSaveDebounce?.Cancel(); _preferenceSaveDebounce?.Dispose(); _preferenceSaveDebounce = new(); var token = _preferenceSaveDebounce.Token;
        _ = Application.Current.Dispatcher.InvokeAsync(async () => { try { await Task.Delay(500, token); await _store.SaveAsync(_settings, token); } catch (OperationCanceledException) { } catch (Exception ex) { _log("Preference save error: " + ex); } });
    }

    private NoteRow? ChooseDailyNote()
    {
        var id = _noteDiscovery.Daily(DateOnly.FromDateTime(DateTime.Today));
        return id is null ? null : _allNoteRows.FirstOrDefault(note => note.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private NoteRow? ChooseRandomNote()
    {
        var candidates = Notes.Where(note => note.Id != _lastRandomNoteId).ToArray();
        if (candidates.Length == 0) candidates = Notes.ToArray();
        if (candidates.Length == 0) return null;
        var poolSize = Math.Max(1, (int)Math.Ceiling(candidates.Length * 0.4));
        var rediscoveryPool = candidates.OrderBy(note => note.TimesSeen).ThenBy(note => note.LastSeenUtc ?? DateTimeOffset.MinValue).Take(poolSize).ToArray();
        return rediscoveryPool[Random.Shared.Next(rediscoveryPool.Length)];
    }

    private void SelectDailyNote()
    {
        if (DailyNote is not null) SelectedNote = DailyNote;
    }

    private void SelectRandomNote()
    {
        var selected = ChooseRandomNote();
        if (selected is null) return;
        RandomNote = selected;
        _lastRandomNoteId = selected.Id;
        SelectedNote = selected;
        PreviousRandomNoteCommand.Refresh();
    }

    private void SelectPreviousRandomNote()
    {
        var id = _noteDiscovery.Previous();
        var note = id is null ? null : _allNoteRows.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (note is not null) { RandomNote = note; _lastRandomNoteId = note.Id; SelectedNote = note; }
        PreviousRandomNoteCommand.Refresh();
    }

    private void SelectRandomFromSameBook()
    {
        if (SelectedNote is null) return;
        var candidates = Notes.Where(note => note.BookHash.Equals(SelectedNote.BookHash, StringComparison.OrdinalIgnoreCase) && note.Id != SelectedNote.Id).ToArray();
        if (candidates.Length == 0) return;
        RandomNote = candidates[Random.Shared.Next(candidates.Length)];
        _lastRandomNoteId = RandomNote.Id;
        SelectedNote = RandomNote;
        RandomSameBookCommand.Refresh();
    }

    private void CopySelectedNote()
    {
        if (SelectedNote is null) return;
        var content = string.Join(Environment.NewLine + Environment.NewLine, new[] { SelectedNote.Text, SelectedNote.Note, SelectedNote.PersonalNote }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(content)) return;
        Clipboard.SetText(content); NotesStatus = "Note copied to clipboard.";
    }

    private void ToggleFavoriteNote(NoteRow? note)
    {
        if (note is null) return;
        note.IsFavorite = !note.IsFavorite;
        Raise(nameof(FavoriteNotesCount));
    }

    private async Task SaveSelectedNoteAsync()
    {
        if (SelectedNote is null) return;
        if (SelectedNote.IsManual && !await Manual.UpdateSessionNoteAsync(SelectedNote.Id, SelectedNote.Note))
        {
            NotesStatus = "That physical-session note no longer exists.";
            return;
        }
        await SaveNoteStateAsync(SelectedNote);
        NotesStatus = SelectedNote.IsManual ? "Physical-session note saved locally." : "Personal note settings saved locally.";
    }

    private async Task SaveNoteStateAsync(NoteRow note)
    {
        try
        {
            _settings.NoteStates[note.Id] = note.State;
            await _store.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            NotesStatus = "Could not save note settings. " + Friendly(ex);
            _log("Note state save error: " + ex);
        }
    }

    private async Task RefreshNotesAsync()
    {
        if (_notesRepository is null) return;
        try
        {
            NotesStatus = "Refreshing local Readest notes…";
            _readestNotes = await _notesRepository.LoadAsync();
            BuildNotes();
        }
        catch (Exception ex)
        {
            NotesStatus = "Could not refresh notes. " + Friendly(ex);
            _log("Note refresh error: " + ex);
        }
    }

    private bool CanOpenSelectedNote() => SelectedNote?.Id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) == true || SelectedNote?.BookPath is { } path && File.Exists(path);

    private void OpenSelectedNote()
    {
        if (SelectedNote?.BookHash.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) == true && long.TryParse(SelectedNote.BookHash[7..], out var manualId))
        {
            _bookFilter = "All"; Raise(nameof(BookFilter)); ApplyBookFilters();
            SelectedBook = Books.FirstOrDefault(book => book.Id == manualId);
            SelectedPage = "Books";
            NotesStatus = "Opened the physical book details.";
            return;
        }
        if (SelectedNote?.BookPath is not { } bookPath || !File.Exists(bookPath))
        {
            NotesStatus = "The original book file is not available in the Readest library.";
            return;
        }
        try
        {
            var executable = ReadestLibraryLocator.FindReadestExecutable();
            var startInfo = executable is null ? new ProcessStartInfo(bookPath) { UseShellExecute = true } : new ProcessStartInfo(executable) { UseShellExecute = true };
            if (executable is not null) startInfo.ArgumentList.Add(bookPath);
            Process.Start(startInfo);
            NotesStatus = $"Opening {SelectedNote.BookTitle} in Readest…";
        }
        catch (Exception ex)
        {
            NotesStatus = "Readest could not be opened. " + ex.Message;
            _log("Open note in Readest error: " + ex);
        }
    }

    private async Task ExportNotesAsync(string format)
    {
        var rows = Notes.ToArray();
        if (rows.Length == 0) return;
        if (MessageBox.Show($"Export {Formatters.Count(rows.Length, "note")}? The file includes highlights, Readest notes, personal notes and favorite status in plain text.", "Export private notes", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        var extension = format switch { "json" => "json", "csv" => "csv", _ => "md" };
        var filter = extension switch { "json" => "JSON file|*.json", "csv" => "CSV file|*.csv", _ => "Markdown file|*.md" };
        var path = _chooseExport(extension, filter);
        if (path is null) return;
        var content = format switch
        {
            "json" => JsonSerializer.Serialize(rows.Select(ToExportObject), new JsonSerializerOptions { WriteIndented = true }),
            "csv" => BuildNotesCsv(rows),
            _ => BuildNotesMarkdown(rows)
        };
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        NotesStatus = $"{Formatters.Count(rows.Length, "note")} exported.";
    }

    private object ToExportObject(NoteRow note) => new
    {
        id = note.Id,
        book = note.BookTitle,
        authors = note.Authors,
        page = note.Page,
        type = note.TypeLabel,
        color = note.ColorLabel,
        highlight = note.Text,
        note = note.Note,
        personalNote = note.PersonalNote,
        favorite = note.IsFavorite,
        createdAt = note.Source.CreatedAt,
        updatedAt = note.Source.UpdatedAt
    };

    private static string BuildNotesMarkdown(IEnumerable<NoteRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Readest Notes");
        builder.AppendLine();
        foreach (var note in rows)
        {
            builder.AppendLine($"## {note.BookTitle}");
            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(note.Text))
            {
                var quotedText = note.Text.Replace(Environment.NewLine, Environment.NewLine + "> ");
                builder.AppendLine($"> {quotedText}");
            }
            if (!string.IsNullOrWhiteSpace(note.Note)) { builder.AppendLine(); builder.AppendLine(note.Note); }
            if (!string.IsNullOrWhiteSpace(note.PersonalNote)) { builder.AppendLine(); builder.AppendLine($"**My note:** {note.PersonalNote}"); }
            builder.AppendLine();
            builder.AppendLine($"- {note.PageLabel} · {note.TypeLabel} · {note.ColorLabel}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string BuildNotesCsv(IEnumerable<NoteRow> rows)
    {
        static string Csv(string? value)
        {
            var normalized = (value ?? string.Empty).Replace("\"", "\"\"").Replace(Environment.NewLine, " ");
            return "\"" + normalized + "\"";
        }
        var builder = new StringBuilder();
        builder.AppendLine("Book,Authors,Page,Type,Color,Highlight,Note,Personal note,Favorite,Created");
        foreach (var note in rows)
        {
            builder.AppendLine(string.Join(",", Csv(note.BookTitle), Csv(note.Authors), Csv(note.Page?.ToString()), Csv(note.TypeLabel), Csv(note.ColorLabel), Csv(note.Text), Csv(note.Note), Csv(note.PersonalNote), Csv(note.IsFavorite.ToString()), Csv(note.Source.CreatedAt?.ToString("O"))));
        }
        return builder.ToString();
    }

    private YearBookGoalSummary CreateYearBookGoal(int year)
    {
        var books = CompletedBookDates().Count(date => date.ToLocalTime().Year == year);
        var archive = _settings.GoalArchives.FirstOrDefault(a => a.Year == year);
        var target = archive?.TargetBooks ?? GoalEngine.Migrate(_settings).First(g => g.Period == GoalPeriod.Yearly && g.Metric == GoalMetric.Books).TargetValue;
        var progress = target <= 0 ? 0 : Math.Min(100, books * 100d / target);
        var status = target <= 0 ? "No goal set" : books >= target ? "Goal reached" : year < DateTime.Today.Year ? "Year complete" : "In progress";
        var detail = archive is not null ? "Archived yearly result" : year == DateTime.Today.Year ? "Current yearly goal" : "No saved goal snapshot for this year";
        return new(year, books, target, progress, status, detail);
    }

    private void GoBack()
    {
        if (_navigationHistory.Count == 0) return;
        var page = _navigationHistory.Pop();
        _isGoingBack = true;
        try { SelectedPage = page; }
        finally { _isGoingBack = false; }
        Raise(nameof(CanGoBack));
        BackCommand.Refresh();
    }

    private void OpenCommandPalette()
    {
        IsCommandPaletteOpen = true;
        GlobalSearch = "";
        RebuildGlobalSearch();
    }

    private void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
        GlobalSearch = "";
    }

    private void RebuildGlobalSearch()
    {
        var query = GlobalSearch.Trim();
        var results = new List<GlobalSearchResult>();
        results.AddRange(Pages.Select(page => new GlobalSearchResult("PAGE", page, PageSubtitleFor(page), page)));
        results.AddRange(_bookModels.Select(book => new GlobalSearchResult("BOOK", book.Title, book.Authors, "Books", book.Id.ToString())));
        results.AddRange(_allNoteRows.Select(note => new GlobalSearchResult("NOTE", string.IsNullOrWhiteSpace(note.Text) ? note.Note : note.Text, note.BookTitle, "Notes", note.Id)));
        results.AddRange(Manual.RecentSessions.Select(session => new GlobalSearchResult("SESSION", session.BookTitle, $"{session.DateLabel} · {session.DurationLabel} · {session.PageRangeLabel}", "Manual log", session.Id)));
        if (!string.IsNullOrWhiteSpace(query)) results = results.Where(item => item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase) || item.Kind.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Replace(GlobalSearchResults, results.Take(40));
        SelectedGlobalSearchResult = GlobalSearchResults.FirstOrDefault();
    }

    private void OpenGlobalSearchResult(GlobalSearchResult? result)
    {
        if (result is null) return;
        if (result.Kind == "BOOK" && long.TryParse(result.Key, out var bookId))
        {
            _bookFilter = "All"; Raise(nameof(BookFilter)); ApplyBookFilters();
            SelectedBook = Books.FirstOrDefault(book => book.Id == bookId);
        }
        else if (result.Kind == "NOTE" && result.Key is not null)
        {
            SelectedNote = _allNoteRows.FirstOrDefault(note => note.Id.Equals(result.Key, StringComparison.OrdinalIgnoreCase));
        }
        SelectedPage = result.Target;
        CloseCommandPalette();
    }

    private string PageSubtitleFor(string page) => page switch { "Today" => "Today and reading focus", "Activity" => "Calendar analytics", "Sessions" => "Reading history", "Manual log" => "Physical books and timer", "Books" => "Unified library", "Notes" => "Daily and random rediscovery", "Goals" => "Plans and targets", "Statistics" => "Reading insights", "Year in Reading" => "Annual story", _ => "Preferences and data health" };

    private void OpenSessionBook(SessionDisplay? session)
    {
        var bookId = session?.Source.BookIds.FirstOrDefault();
        if (bookId is null or 0) return;
        var match = Books.FirstOrDefault(book => book.Id == bookId);
        if (match is null)
        {
            _bookFilter = "All";
            _bookSearch = "";
            Raise(nameof(BookFilter));
            Raise(nameof(BookSearch));
            ApplyBookFilters();
            match = Books.FirstOrDefault(book => book.Id == bookId);
        }
        if (match is null) return;
        SelectedBook = match;
        SelectedPage = "Books";
    }

    private void UpdateOpenBookState()
    {
        var model = SelectedBook is null ? null : _bookModels.FirstOrDefault(book => book.Id == SelectedBook.Id);
        var path = model?.Source == "Manual" ? null : ReadestLibraryLocator.FindBookFile(Diagnostics?.Path, model?.Hash);
        var manual = model?.Source == "Manual" ? Manual.FindBook(model.Id) : null;
        OpenBookStatus = SelectedBook is null
            ? "Select a book to open it in Readest."
            : model?.Source == "Manual"
                ? manual?.InfoUrl is not null || manual?.GoodreadsUrl is not null ? "Open the saved metadata page for this physical edition." : "No metadata page was saved for this physical edition."
            : path is null
                ? "This book file is not available in the local Readest library."
                : "";
        OpenBookInReadestCommand.Refresh();
    }

    private bool CanOpenSelectedBook()
    {
        var model = SelectedBook is null ? null : _bookModels.FirstOrDefault(book => book.Id == SelectedBook.Id);
        if (model?.Source == "Manual")
        {
            var manual = Manual.FindBook(model.Id);
            return manual?.InfoUrl is not null || manual?.GoodreadsUrl is not null;
        }
        return ReadestLibraryLocator.FindBookFile(Diagnostics?.Path, model?.Hash) is not null;
    }

    private void OpenBookInReadest()
    {
        var model = SelectedBook is null ? null : _bookModels.FirstOrDefault(book => book.Id == SelectedBook.Id);
        if (model?.Source == "Manual")
        {
            var manual = Manual.FindBook(model.Id);
            var url = manual?.GoodreadsUrl ?? manual?.InfoUrl;
            if (url is null) { OpenBookStatus = "No metadata page was saved for this physical edition."; return; }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                OpenBookStatus = $"Opening information for {SelectedBook!.Title}…";
            }
            catch (Exception ex)
            {
                OpenBookStatus = "The book information page could not be opened. " + ex.Message;
                _log("Open manual book metadata error: " + ex);
            }
            return;
        }
        var bookPath = ReadestLibraryLocator.FindBookFile(Diagnostics?.Path, model?.Hash);
        if (bookPath is null) { OpenBookStatus = "Could not find this book in the local Readest library."; return; }
        try
        {
            var executable = ReadestLibraryLocator.FindReadestExecutable();
            var startInfo = executable is null ? new ProcessStartInfo(bookPath) { UseShellExecute = true } : new ProcessStartInfo(executable) { UseShellExecute = true };
            if (executable is not null) startInfo.ArgumentList.Add(bookPath);
            Process.Start(startInfo);
            OpenBookStatus = $"Opening {SelectedBook!.Title} in Readest…";
        }
        catch (Exception ex)
        {
            OpenBookStatus = "Readest could not be opened. " + ex.Message;
            _log("Open in Readest error: " + ex);
        }
    }

    private string? BookTrackingKey(long bookId)
    {
        var model = _bookModels.FirstOrDefault(book => book.Id == bookId);
        return model is null ? null : string.IsNullOrWhiteSpace(model.Hash) ? $"id:{bookId}" : model.Hash;
    }

    private static string BookKey(Book book) => string.IsNullOrWhiteSpace(book.Hash) ? $"id:{book.Id}" : book.Hash;
    private static long? ParseManualKey(string key) => key.StartsWith("manual:", StringComparison.OrdinalIgnoreCase) && long.TryParse(key[7..], out var id) ? id : null;

    private BookEditionLink? LinkForBook(long bookId)
    {
        var directManual = "manual:" + bookId;
        var model = _bookModels.FirstOrDefault(book => book.Id == bookId) ?? _readestBookModels.FirstOrDefault(book => book.Id == bookId);
        var key = model is null ? "" : BookKey(model);
        return _settings.BookLinks.FirstOrDefault(link => link.ManualKey.Equals(directManual, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(key) && link.ReadestKey.Equals(key, StringComparison.OrdinalIgnoreCase)));
    }

    private ManualBook? ManualBookFor(long bookId)
    {
        var direct = Manual.FindBook(bookId);
        if (direct is not null) return direct;
        var link = LinkForBook(bookId);
        return link is null || ParseManualKey(link.ManualKey) is not { } manualId ? null : Manual.FindBook(manualId);
    }

    private BookRow? FindLinkCandidate(BookRow selected)
    {
        var selectedIsManual = Manual.FindBook(selected.Id) is not null;
        var title = NormalizeMatch(selected.Title);
        var authors = NormalizeMatch(selected.Authors);
        if (string.IsNullOrWhiteSpace(title)) return null;
        return Books.Where(book => book.Id != selected.Id && (Manual.FindBook(book.Id) is not null) != selectedIsManual && LinkForBook(book.Id) is null)
            .Select(book => new { Book = book, Title = NormalizeMatch(book.Title), Authors = NormalizeMatch(book.Authors) })
            .Where(item => item.Title == title || item.Title.Contains(title, StringComparison.Ordinal) || title.Contains(item.Title, StringComparison.Ordinal))
            .OrderByDescending(item => item.Title == title)
            .ThenByDescending(item => item.Authors == authors)
            .Select(item => item.Book)
            .FirstOrDefault();
    }

    private static string NormalizeMatch(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
        .Select(char.ToLowerInvariant).ToArray());

    private async Task LinkSelectedEditionAsync()
    {
        if (SelectedBook is null || LinkCandidate is not { } candidate) return;
        var manual = Manual.FindBook(SelectedBook.Id) ?? Manual.FindBook(candidate.Id);
        var readestRow = manual?.Id == SelectedBook.Id ? candidate : SelectedBook;
        var readest = _readestBookModels.FirstOrDefault(book => book.Id == readestRow.Id);
        if (manual is null || readest is null) return;
        _settings.BookLinks.RemoveAll(link => link.ManualKey == "manual:" + manual.Id || link.ReadestKey == BookKey(readest));
        _settings.BookLinks.Add(new BookEditionLink { ManualKey = "manual:" + manual.Id, ReadestKey = BookKey(readest) });
        await _store.SaveAsync(_settings);
        ApplySourceSelection();
        SelectedBook = Books.FirstOrDefault(book => book.Id == readest.Id);
        Status = $"Linked the Readest and physical editions of {readest.Title}.";
    }

    private async Task UnlinkSelectedEditionAsync()
    {
        if (SelectedBook is null || LinkForBook(SelectedBook.Id) is not { } link) return;
        _settings.BookLinks.Remove(link);
        await _store.SaveAsync(_settings);
        ApplySourceSelection();
        SelectedBook = Books.FirstOrDefault(book => book.Id == SelectedBook.Id) ?? Books.FirstOrDefault();
        Status = "Book editions unlinked.";
    }

    private async Task RefreshSelectedMetadataAsync()
    {
        var manual = SelectedManualBook;
        if (manual is null) return;
        Status = "Refreshing book metadata…";
        try { Status = await Manual.RefreshBookMetadataAsync(manual.Id); }
        catch (Exception ex) { Status = "Metadata refresh failed: " + Friendly(ex); _log("Metadata refresh error: " + ex); }
    }

    private void CleanupCoverCache()
    {
        var removed = Manual.CleanupCoverCache();
        Status = removed == 0 ? "No unused cached covers found" : $"Removed {Formatters.Count(removed, "unused cover")}";
    }

    private async Task ExportPhysicalLibraryAsync()
    {
        var path = _chooseExport("json", "Readest Stats physical library|*.json");
        if (path is null) return;
        await Manual.ExportLibraryAsync(path);
        Status = $"Exported {Formatters.Count(Manual.Books.Count, "physical book")} with manual sessions";
    }

    private async Task ImportPhysicalLibraryAsync()
    {
        var path = _chooseImport("Import Center", "Supported library files|*.json;*.csv|Readest Stats physical library|*.json|Goodreads, StoryGraph, or generic CSV|*.csv");
        if (path is null) return;
        try
        {
            if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var preview = await _libraryImport.PreviewCsvAsync(path);
                var added = await Manual.ImportCatalogAsync(preview.Books);
                Status = $"Import Center · {preview.Provider}: imported {Formatters.Count(added, "new book")}; {preview.SkippedRows} row(s) skipped.";
            }
            else
            {
                var result = await Manual.ImportLibraryAsync(path);
                Status = $"Import Center · Readest Stats: imported {Formatters.Count(result.Books, "new book")} and {Formatters.Count(result.Sessions, "new session")}";
            }
        }
        catch (Exception ex) { Status = "Library import failed: " + Friendly(ex); _log("Library import error: " + ex); }
    }

    private BookTrackingState? SelectedTracking
    {
        get
        {
            var key = SelectedBook is null ? null : BookTrackingKey(SelectedBook.Id);
            return key is not null && _settings.BookTracking.TryGetValue(key, out var state) ? state : null;
        }
    }

    private ReadingPlan? EnsureSelectedPlan()
    {
        if (SelectedBook is null) return null;
        var key = BookTrackingKey(SelectedBook.Id);
        if (key is null) return null;
        if (!_settings.BookTracking.TryGetValue(key, out var tracking)) _settings.BookTracking[key] = tracking = new() { Status = BookStatus(SelectedBook.Id) };
        tracking.Plan ??= new ReadingPlan { Enabled = true };
        tracking.Plan.Enabled = true;
        return tracking.Plan;
    }

    private ReadingPlanProgress PlanFor(BookRow book)
    {
        var key = BookTrackingKey(book.Id);
        var plan = key is not null && _settings.BookTracking.TryGetValue(key, out var tracking) ? tracking.Plan : null;
        var manual = ManualBookFor(book.Id);
        var recent = _events.Where(item => item.BookId == book.Id && item.Start >= DateTimeOffset.Now.AddDays(-30)).ToArray();
        var activeDays = Math.Max(1, recent.Select(item => DateOnly.FromDateTime(_statistics.ToLocal(item.Start).DateTime)).Distinct().Count());
        return _readingPlanEngine.Evaluate(plan, manual?.CurrentPage, manual?.TotalPages, recent.Sum(item => item.DurationSeconds) / activeDays, DateOnly.FromDateTime(DateTime.Today));
    }

    private void BuildSelectedPlan()
    {
        SelectedBookPlan = SelectedBook is null ? _readingPlanEngine.Evaluate(null, null, null, 0, DateOnly.FromDateTime(DateTime.Today)) : PlanFor(SelectedBook);
        Raise(nameof(SelectedPlanStartDate)); Raise(nameof(SelectedPlanTargetDate)); Raise(nameof(SelectedPlanDailyMinutes)); Raise(nameof(SelectedPlanDailyPages)); Raise(nameof(SelectedPlanIncludeWeekends)); Raise(nameof(SelectedPlanPriority)); Raise(nameof(SelectedPlanUsesPages)); Raise(nameof(SelectedBookPlanGoal)); Raise(nameof(SelectedPlanIsPaused)); Raise(nameof(ReadingPlanPauseLabel));
        Raise(nameof(SelectedPlanMonday)); Raise(nameof(SelectedPlanTuesday)); Raise(nameof(SelectedPlanWednesday)); Raise(nameof(SelectedPlanThursday)); Raise(nameof(SelectedPlanFriday)); Raise(nameof(SelectedPlanSaturday)); Raise(nameof(SelectedPlanSunday));
    }

    private void BuildContinueReading()
    {
        var candidates = Books.Where(book => book.Status is "Reading" or "Paused" || PlanFor(book).Enabled)
            .Select(book =>
            {
                var plan = PlanFor(book);
                var reason = plan.Status == "Behind" ? "Plan needs attention" : book.LastRead is null ? "Ready to begin" : $"Last read {book.LastRead.Value.ToLocalTime():MMM d}";
                return new BookFocusItem(book, reason, "View book", plan);
            })
            .OrderByDescending(item => item.Plan.Status == "Behind")
            .ThenByDescending(item => SelectedTrackingPriority(item.Book.Id))
            .ThenBy(item => item.Book.LastRead ?? DateTimeOffset.MinValue)
            .ToArray();
        Replace(ContinueReading, candidates.Take(3));
        Replace(ActiveReadingPlans, candidates.Where(item => item.Plan.Enabled));
        Raise(nameof(HasContinueReading));
        Raise(nameof(HasActiveReadingPlans));
    }

    private int SelectedTrackingPriority(long bookId)
    {
        var key = BookTrackingKey(bookId);
        return key is not null && _settings.BookTracking.TryGetValue(key, out var tracking) ? tracking.Plan?.Priority ?? 0 : 0;
    }

    private void OpenFocusBook(BookFocusItem? item)
    {
        if (item is null) return;
        SelectedBook = Books.FirstOrDefault(book => book.Id == item.Book.Id) ?? item.Book;
        SelectedPage = "Books";
    }

    private async Task SaveReadingPlanAsync()
    {
        var plan = EnsureSelectedPlan();
        if (plan is null) return;
        plan.Enabled = true;
        await _store.SaveAsync(_settings);
        BuildSelectedPlan(); BuildContinueReading();
        Status = "Reading plan saved";
    }

    private async Task RemoveReadingPlanAsync()
    {
        if (SelectedTracking?.Plan is not { } plan) return;
        plan.Enabled = false;
        await _store.SaveAsync(_settings);
        BuildSelectedPlan(); BuildContinueReading();
        Status = "Reading plan removed";
    }

    private async Task ToggleReadingPlanPauseAsync()
    {
        if (SelectedTracking?.Plan is not { Enabled: true } plan) return;
        plan.IsPaused = !plan.IsPaused;
        await _store.SaveAsync(_settings);
        BuildSelectedPlan(); BuildContinueReading();
        Status = plan.IsPaused ? "Reading plan paused" : "Reading plan resumed";
    }

    private bool IsPlanDay(DayOfWeek day)
    {
        var plan = SelectedTracking?.Plan;
        if (plan is null) return true;
        if (plan.ReadingDays.Count == 0) return day is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || plan.IncludeWeekends;
        return plan.ReadingDays.Contains(day);
    }

    private void SetPlanDay(DayOfWeek day, bool enabled)
    {
        var plan = EnsureSelectedPlan();
        if (plan is null) return;
        if (enabled && !plan.ReadingDays.Contains(day)) plan.ReadingDays.Add(day);
        if (!enabled) plan.ReadingDays.Remove(day);
        plan.IncludeWeekends = plan.ReadingDays.Contains(DayOfWeek.Saturday) || plan.ReadingDays.Contains(DayOfWeek.Sunday);
        Raise(nameof(SelectedPlanIncludeWeekends));
        BuildSelectedPlan();
    }

    private async Task StartRereadAsync()
    {
        if (SelectedBook is null) return;
        var key = BookTrackingKey(SelectedBook.Id);
        if (key is null) return;
        if (!_settings.BookTracking.TryGetValue(key, out var tracking)) _settings.BookTracking[key] = tracking = new();
        tracking.Cycles ??= [];
        var cycle = new ReadingCycle { Number = tracking.Cycles.Select(item => item.Number).DefaultIfEmpty(0).Max() + 1 };
        tracking.Cycles.Add(cycle);
        tracking.CurrentCycleId = cycle.Id;
        tracking.Status = "Reading";
        tracking.StartedAtUtc = cycle.StartedAtUtc;
        tracking.CompletedAtUtc = null;
        _selectedBookStatus = "Reading";
        Raise(nameof(SelectedBookStatus)); Raise(nameof(SelectedBookStatusDetail)); Raise(nameof(SelectedBookCompletedDate)); Raise(nameof(SelectedBookCycleSummary));
        StartRereadCommand.Refresh();
        await SaveBookTrackingAsync();
        Status = $"Started {cycle.Label.ToLowerInvariant()} for {SelectedBook.Title}";
    }

    private static ReadingCycle EnsureOpenCycle(BookTrackingState tracking)
    {
        tracking.Cycles ??= [];
        var current = tracking.Cycles.FirstOrDefault(cycle => cycle.Id == tracking.CurrentCycleId && cycle.CompletedAtUtc is null);
        if (current is not null) return current;
        current = new ReadingCycle { Number = tracking.Cycles.Select(cycle => cycle.Number).DefaultIfEmpty(0).Max() + 1, StartedAtUtc = tracking.StartedAtUtc ?? DateTimeOffset.UtcNow };
        tracking.Cycles.Add(current);
        tracking.CurrentCycleId = current.Id;
        return current;
    }

    private static void CompleteOpenCycle(BookTrackingState tracking, DateTimeOffset completed)
    {
        var cycle = EnsureOpenCycle(tracking);
        cycle.CompletedAtUtc = completed;
        tracking.CurrentCycleId = cycle.Id;
    }

    private Task StartSelectedManualSessionAsync()
    {
        var manualBook = SelectedManualBook;
        if (manualBook is null) return Task.CompletedTask;
        Manual.SelectedBook = Manual.Books.FirstOrDefault(book => book.Id == manualBook.Id);
        if (Manual.StartSessionCommand.CanExecute(null)) Manual.StartSessionCommand.Execute(null);
        SelectedPage = "Manual log";
        return Task.CompletedTask;
    }

    private string BookStatus(long bookId)
    {
        var key = BookTrackingKey(bookId);
        if (key is not null && _settings.BookTracking.TryGetValue(key, out var tracking)) return tracking.Status;
        var manual = ManualBookFor(bookId);
        return manual?.CompletedAtUtc is not null ? "Finished" : manual?.CurrentPage is > 0 ? "Reading" : manual is not null ? "Want to read" : "Unspecified";
    }

    private IReadOnlyList<DateTimeOffset> CompletedBookDates()
    {
        var manualKeys = Manual.Books.Select(book => "manual:" + book.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tracked = _settings.BookTracking.Where(item => SelectedSource switch
        {
            "Manual" => manualKeys.Contains(item.Key),
            "Readest" => !manualKeys.Contains(item.Key),
            _ => true
        }).SelectMany(item => item.Value.Cycles.Count > 0
            ? item.Value.Cycles.Where(cycle => cycle.CompletedAtUtc is not null).Select(cycle => cycle.CompletedAtUtc!.Value)
            : item.Value.CompletedAtUtc is { } completed ? [completed] : []);
        var manual = SelectedSource == "Readest" ? [] : Manual.Books.Where(book => book.CompletedAtUtc is not null).Select(book => book.CompletedAtUtc!.Value);
        return tracked.Concat(manual).Distinct().ToArray();
    }

    private async Task SaveBookTrackingAsync()
    {
        try
        {
            await _store.SaveAsync(_settings);
            BuildGoals(); BuildYear(); ApplyBookFilters();
            Status = $"Book status saved · {SelectedBookStatus}";
        }
        catch (Exception ex) { Error = "Could not save the book status. " + Friendly(ex); }
    }

    private async Task TogglePinnedBookAsync()
    {
        if (SelectedBook is null) return;
        var key = BookTrackingKey(SelectedBook.Id);
        if (key is null) return;
        if (!_settings.PinnedBookKeys.Remove(key)) _settings.PinnedBookKeys.Add(key);
        await _store.SaveAsync(_settings);
        Raise(nameof(IsSelectedBookPinned)); Raise(nameof(PinBookLabel));
        Status = IsSelectedBookPinned ? "Book pinned" : "Book unpinned";
        ApplyBookFilters();
    }

    private string FormatTime(DateTimeOffset value, bool includeSeconds = false) => value.ToString(Use24HourTime ? includeSeconds ? "HH:mm:ss" : "HH:mm" : includeSeconds ? "h:mm:ss tt" : "h:mm tt");
    private string FormatHour(int hour) => DateTime.Today.AddHours(hour).ToString(Use24HourTime ? "HH:mm" : "h tt");
    private string FormatHourRange(int startHour, int hours) => $"{FormatHour(startHour)}–{FormatHour((startHour + hours) % 24)}";

    private async Task ChangeDatabaseAsync() { var path = _chooseDatabase(); if (path is not null) await ConnectAsync(path); }
    private async Task RedetectAsync() { _settings.DatabasePath = null; var path = await _locator.LocateAsync(); if (path is null) { Error = "Readest data was not found."; RaiseConnectionState(); } else await ConnectAsync(path); }
    private void OpenFolder() { if (Diagnostics is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Diagnostics.Path}\"") { UseShellExecute = true }); }
    private void OpenLogs() { var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadestStats", "logs"); Directory.CreateDirectory(directory); Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true }); }
    private async Task ExportCsvAsync() { var path = _chooseExport("csv", "CSV file|*.csv"); if (path is not null) await _export.ExportCsvAsync(path, _statistics.Daily(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes))); }
    private async Task ExportJsonAsync() { var path = _chooseExport("json", "JSON file|*.json"); if (path is not null && _resolvedRange is not null) await _export.ExportJsonAsync(path, _statistics.Overview(_periodEvents, _bookModels, Math.Max(1, _resolvedRange.EndDate.DayNumber - _resolvedRange.StartDate.DayNumber + 1), TimeSpan.FromMinutes(SessionGapMinutes)), Books.Select(b => b.Summary)); }
    private async Task ExportMonthlyReportAsync() { var path = _chooseExport("md", "Markdown report|*.md"); if (path is not null) { await _export.ExportMonthlyMarkdownAsync(path, Month.Year, Month.Month, _events, _bookModels, SessionGapMinutes); Status = $"{Month:MMMM yyyy} report exported"; } }
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = "Checking GitHub releases…";
        try
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 1, 0);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var update = await _updateChecker.CheckAsync(current, timeout.Token);
            _updateUrl = update.DownloadUrl;
            UpdateStatus = update.IsNewer ? $"{update.Tag} is available" : $"Up to date · latest {update.Tag}";
            OpenUpdateCommand.Refresh();
        }
        catch (Exception ex)
        {
            UpdateStatus = "Could not check for updates. Check your internet connection.";
            _log("Update check error: " + ex);
        }
    }
    private void OpenUpdate() { if (!string.IsNullOrWhiteSpace(_updateUrl)) Process.Start(new ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
    private async Task SaveSettingsAsync() { _settings.DefaultRangePreset = SelectedRange; _settings.CustomRangeStart = CustomStart is null ? null : DateOnly.FromDateTime(CustomStart.Value); _settings.CustomRangeEnd = CustomEnd is null ? null : DateOnly.FromDateTime(CustomEnd.Value); _settings.TrendMetric = TrendMetric; _settings.TrendGranularity = TrendGranularity; _settings.DefaultRangeDays = SelectedRange switch { "Today" => 1, "7 days" => 7, "90 days" => 90, "6 months" => 183, "1 year" => 366, "This year" => 365, "All time" => -1, _ => 30 }; await _store.SaveAsync(_settings); Status = "Settings saved"; ConfigureRefresh(); }
    private async Task SaveGoalsAsync() { foreach (var goal in Goals) goal.Commit(); _settings.Goals = Goals.Select(g => g.Definition).ToList(); await _store.SaveAsync(_settings); BuildGoals(); BuildRecordsAndInsights(); BuildStatistics(); BuildYear(); Status = "Goals saved"; }
    private async Task BackupSettingsAsync()
    {
        var path = _chooseExport("zip", "Readest Stats backup|*.zip");
        if (path is null) return;
        await _store.SaveAsync(_settings);
        _backup.Create(path, AppVersion);
        Status = "Complete backup created · settings, goals, physical books, sessions and notes included";
    }

    private async Task RestoreBackupAsync()
    {
        var path = _chooseBackupImport();
        if (path is null) return;
        BackupManifest manifest;
        try { manifest = _backup.Inspect(path); }
        catch (Exception ex) { Status = "Backup could not be opened: " + Friendly(ex); return; }
        if (MessageBox.Show($"Restore backup from {manifest.CreatedAtUtc.ToLocalTime():g}?\n\n{Formatters.Count(manifest.Books, "physical book")} · {Formatters.Count(manifest.Sessions, "manual session")} · {Formatters.Count(manifest.Notes, "session note")} · {Formatters.Count(manifest.Covers, "cached cover")}\n\nCurrent app-owned data will be kept as recovery copies.", "Restore Readest Stats backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _backup.Restore(path);
            _settings = await _store.LoadAsync();
            GoalEngine.Migrate(_settings);
            await Manual.InitializeAsync();
            RaiseSettings();
            await RefreshAsync(force: true);
            Status = "Backup restored successfully";
        }
        catch (Exception ex) { Status = "Backup restore failed: " + Friendly(ex); _log("Backup restore error: " + ex); }
    }
    private async Task ResetSettingsAsync() { if (MessageBox.Show("Reset Readest Stats settings and goals? Readest data will not be changed.", "Readest Stats", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; var database = _settings.DatabasePath; _settings = new() { DatabasePath = database }; GoalEngine.Migrate(_settings); _applyTheme(_settings.Theme); await _store.SaveAsync(_settings); RaiseSettings(); RecalculateAll(); Status = "App-owned settings reset"; }

    private void ConfigureRefresh()
    {
        _watcher?.Dispose(); _watcher = null; _refreshTimer.Stop(); if (!_settings.AutoRefresh || _repository is null) return;
        if (_settings.RefreshIntervalSeconds > 0) { _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds); _refreshTimer.Start(); return; }
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_repository.DatabasePath)!) { Filter = "*", IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true }; _watcher.Changed += WatcherChanged; _watcher.Created += WatcherChanged; _watcher.Renamed += WatcherChanged; _watcher.Deleted += WatcherChanged;
    }
    private void WatcherChanged(object sender, FileSystemEventArgs e)
    {
        var file = Path.GetFileName(e.FullPath);
        if (!file.StartsWith("statistics.db", StringComparison.OrdinalIgnoreCase) &&
            !file.Equals("config.json", StringComparison.OrdinalIgnoreCase) &&
            !file.Equals("library.json", StringComparison.OrdinalIgnoreCase)) return;
        _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _refreshDebounce = new(); var token = _refreshDebounce.Token;
        _ = Application.Current.Dispatcher.InvokeAsync(async () => { try { await Task.Delay(900, token); await RefreshAsync(incremental: true); } catch (OperationCanceledException) { } });
    }
    private void ClearAnalytics() { Period = null; Comparison = null; Story = null; SelectedInsight = null; Trend.Clear(); Heatmap.Clear(); Hourly.Clear(); Weekdays.Clear(); TimeBlocks.Clear(); CompletionTimeline.Clear(); StreakTimeline.Clear(); CumulativeJourney.Clear(); MonthlyJourney.Clear(); ComparisonBars.Clear(); StatisticsTopBooks.Clear(); Records.Clear(); QuickInsights.Clear(); AllInsights.Clear(); Sessions.Clear(); Books.Clear(); Goals.Clear(); CalendarDays.Clear(); WeekHourMatrix.Clear(); DayTimeline.Clear(); SessionDots.Clear(); ReadingStyleDays.Clear(); BookAttention.Clear(); SourceComposition.Clear(); PhysicalPageProgress.Clear(); YearBookAttention.Clear(); BookDayMatrix.Clear(); ReadingFingerprint.Clear(); RollingMomentum.Clear(); PeriodDumbbells.Clear(); SessionStaircase.Clear(); GoalPace.Clear(); BestReadingDays.Clear(); UpdateOpenBookState(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); Raise(nameof(HasPhysicalPageProgress)); Raise(nameof(SourceCompositionSummary)); }
    private static string Friendly(Exception ex) => ex switch { UnauthorizedAccessException => "Permission denied while reading the database.", IOException => ex.Message, Microsoft.Data.Sqlite.SqliteException => "The database is locked, corrupted, or incompatible.", _ => ex.Message };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private void RaiseConnectionState() { Raise(nameof(IsConnected)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); OpenFolderCommand.Refresh(); }
    private void RaiseSettings() { Raise(nameof(SessionGapMinutes)); Raise(nameof(MinimumSessionSeconds)); Raise(nameof(FirstDayOfWeek)); Raise(nameof(ExperimentalPageMetrics)); Raise(nameof(CompactMode)); Raise(nameof(Use24HourTime)); Raise(nameof(ReduceMotion)); Raise(nameof(AutomaticBackups)); Raise(nameof(Theme)); Raise(nameof(RefreshMode)); Raise(nameof(SelectedRange)); Raise(nameof(SelectedSource)); Raise(nameof(GoogleBooksApiKey)); }
    public void Dispose() { Manual.Dispose(); _watcher?.Dispose(); _refreshTimer.Stop(); _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _activeRefresh?.Cancel(); _activeRefresh?.Dispose(); _noteSaveDebounce?.Cancel(); _noteSaveDebounce?.Dispose(); _preferenceSaveDebounce?.Cancel(); _preferenceSaveDebounce?.Dispose(); }
}
