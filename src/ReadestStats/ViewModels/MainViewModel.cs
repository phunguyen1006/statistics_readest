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

public sealed record NavItem(string Name, string Icon);
public sealed record BookFilterOption(long? Id, string Label);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ReadestDatabaseLocator _locator = new();
    private readonly SettingsStore _store;
    private readonly StatisticsEngine _statistics = new();
    private readonly AnalyticsEngine _analytics = new();
    private readonly DateRangeService _ranges = new();
    private readonly GoalEngine _goalEngine = new();
    private readonly InsightEngine _insightEngine = new();
    private readonly DataQualityEngine _dataQualityEngine = new();
    private readonly VisualizationEngine _visualization = new();
    private readonly InfographicEngine _infographics = new();
    private readonly ExportService _export = new();
    private readonly UpdateChecker _updateChecker = new();
    private readonly NoteDiscoveryEngine _noteDiscovery = new();
    private readonly Func<string?> _chooseDatabase;
    private readonly Func<string, string, string?> _chooseExport;
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
    private IReadOnlyList<ReadingEvent> _periodEvents = [];
    private IReadOnlyList<Book> _bookModels = [];
    private IReadOnlyList<ReadingSession> _periodSessions = [];
    private IReadOnlyList<DailyStat> _allDaily = [];
    private IReadOnlyList<ReadingSession> _allSessions = [];
    private AppSettings _settings = new();
    private ResolvedDateRange? _resolvedRange;
    private string _selectedPage = "Overview";
    private string _selectedRange = "30 days";
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
    private int _allSessionCount;
    private readonly Stack<string> _navigationHistory = new();
    private bool _isGoingBack;

    public MainViewModel(Func<string?> chooseDatabase, Func<string, string, string?> chooseExport, Action<string>? log = null, Action<string>? applyTheme = null)
    {
        _chooseDatabase = chooseDatabase; _chooseExport = chooseExport; _log = log ?? (_ => { }); _applyTheme = applyTheme ?? (_ => { }); _store = new(_locator.SettingsPath);
        NavigateCommand = new ParameterCommand<string>(page => { if (!string.IsNullOrWhiteSpace(page)) SelectedPage = page; });
        OpenInsightCommand = new ParameterCommand<InsightItem>(insight => { if (insight is not null) { SelectedInsight = insight; SelectedPage = "Statistics"; } });
        OpenSessionBookCommand = new ParameterCommand<SessionDisplay>(OpenSessionBook, session => session?.Source.BookIds.Count > 0);
        OpenBookInReadestCommand = new RelayCommand(OpenBookInReadest, CanOpenSelectedBook);
        SelectNoteCommand = new ParameterCommand<NoteRow>(note => SelectedNote = note);
        ToggleFavoriteNoteCommand = new ParameterCommand<NoteRow>(ToggleFavoriteNote, note => note is not null);
        OpenSelectedNoteCommand = new RelayCommand(OpenSelectedNote, CanOpenSelectedNote);
        RandomNoteCommand = new RelayCommand(SelectRandomNote, () => Notes.Count > 0);
        PreviousRandomNoteCommand = new RelayCommand(SelectPreviousRandomNote, () => _noteDiscovery.CanGoBack);
        CopySelectedNoteCommand = new RelayCommand(CopySelectedNote, () => SelectedNote is not null);
        DailyNoteCommand = new RelayCommand(SelectDailyNote, () => _allNoteRows.Count > 0);
        SaveSelectedNoteCommand = new AsyncCommand(SaveSelectedNoteAsync, () => SelectedNote is not null);
        RefreshNotesCommand = new AsyncCommand(RefreshNotesAsync, () => _notesRepository is not null);
        ExportNotesMarkdownCommand = new AsyncCommand(() => ExportNotesAsync("md"), () => Notes.Count > 0);
        ExportNotesJsonCommand = new AsyncCommand(() => ExportNotesAsync("json"), () => Notes.Count > 0);
        ExportNotesCsvCommand = new AsyncCommand(() => ExportNotesAsync("csv"), () => Notes.Count > 0);
        TogglePinnedBookCommand = new AsyncCommand(TogglePinnedBookAsync, () => SelectedBook is not null);
        CheckForUpdatesCommand = new AsyncCommand(CheckForUpdatesAsync);
        OpenUpdateCommand = new RelayCommand(OpenUpdate, () => !string.IsNullOrWhiteSpace(_updateUrl));
        BackCommand = new RelayCommand(GoBack, () => _navigationHistory.Count > 0);
        RefreshCommand = new AsyncCommand(() => RefreshAsync()); ChangeDatabaseCommand = new AsyncCommand(ChangeDatabaseAsync); RedetectCommand = new AsyncCommand(RedetectAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Diagnostics is not null); ExportCsvCommand = new AsyncCommand(ExportCsvAsync, () => Period is not null); ExportJsonCommand = new AsyncCommand(ExportJsonAsync, () => Period is not null); ExportMonthlyReportCommand = new AsyncCommand(ExportMonthlyReportAsync, () => IsConnected);
        OpenLogsCommand = new RelayCommand(OpenLogs);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync); SaveGoalsCommand = new AsyncCommand(SaveGoalsAsync); BackupSettingsCommand = new AsyncCommand(BackupSettingsAsync); ResetSettingsCommand = new AsyncCommand(ResetSettingsAsync);
        PreviousMonthCommand = new RelayCommand(() => { Month = Month.AddMonths(-1); BuildActivity(); }); NextMonthCommand = new RelayCommand(() => { if (Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) { Month = Month.AddMonths(1); BuildActivity(); } }); TodayCommand = new RelayCommand(() => { Month = new(DateTime.Today.Year, DateTime.Today.Month, 1); SelectedDate = DateOnly.FromDateTime(DateTime.Today); BuildActivity(); });
        PreviousYearCommand = new RelayCommand(() => { Year--; BuildYear(); }); NextYearCommand = new RelayCommand(() => { if (Year < DateTime.Today.Year) { Year++; BuildYear(); } });
        SelectDayCommand = new ParameterCommand<CalendarDayItem>(day => { if (day is not null) { SelectedDate = day.Date; BuildSelectedDay(); } });
        SelectHeatmapDayCommand = new ParameterCommand<ChartPoint>(point => { if (point is not null && DateOnly.TryParse(point.Label, out var date)) { Month = new(date.Year, date.Month, 1); SelectedDate = date; BuildActivity(); SelectedPage = "Activity"; } });
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(incremental: true);
    }

    public string[] Pages { get; } = ["Overview", "Activity", "Sessions", "Books", "Notes", "Goals", "Statistics", "Year in Reading", "Settings"];
    public NavItem[] Navigation { get; } =
    [
        new("Overview", "M3,3 H17 V7 H3 Z M3,10 H9 V17 H3 Z M12,10 H17 V17 H12 Z"),
        new("Activity", "M3,4 H17 V17 H3 Z M3,8 H17 M7,2 V6 M13,2 V6"),
        new("Sessions", "M10,3 A7,7 0 1 1 9.9,3 M10,6 V10 L13,12"),
        new("Books", "M3,3 H9 A2,2 0 0 1 11,5 V17 A3,3 0 0 0 8,14 H3 Z M17,3 H11 A2,2 0 0 0 9,5 V17 A3,3 0 0 1 12,14 H17 Z"),
        new("Notes", "M4,3 H16 A2,2 0 0 1 18,5 V15 A2,2 0 0 1 16,17 H4 A2,2 0 0 1 2,15 V5 A2,2 0 0 1 4,3 Z M5,7 H15 M5,10 H13 M5,13 H10"),
        new("Goals", "M10,2 A8,8 0 1 1 9.9,2 M10,6 A4,4 0 1 1 9.9,6 M10,9 A1,1 0 1 1 9.9,9"),
        new("Statistics", "M3,17 V10 H6 V17 Z M8,17 V5 H11 V17 Z M13,17 V8 H16 V17 Z"),
        new("Year in Reading", "M10,2 L12,7 L18,7 L13,11 L15,17 L10,13 L5,17 L7,11 L2,7 L8,7 Z"),
        new("Settings", "M12.22,2 H11.78 A2,2 0 0 0 9.78,4 V4.18 A2,2 0 0 1 8.78,5.91 L8.35,6.16 A2,2 0 0 1 6.35,6.16 L6.2,6.08 A2,2 0 0 0 3.47,6.81 L3.25,7.19 A2,2 0 0 0 3.98,9.92 L4.13,10.02 A2,2 0 0 1 5.13,11.74 V12.25 A2,2 0 0 1 4.13,13.99 L3.98,14.08 A2,2 0 0 0 3.25,16.81 L3.47,17.19 A2,2 0 0 0 6.2,17.92 L6.35,17.84 A2,2 0 0 1 8.35,17.84 L8.78,18.09 A2,2 0 0 1 9.78,19.82 V20 A2,2 0 0 0 11.78,22 H12.22 A2,2 0 0 0 14.22,20 V19.82 A2,2 0 0 1 15.22,18.09 L15.65,17.84 A2,2 0 0 1 17.65,17.84 L17.8,17.92 A2,2 0 0 0 20.53,17.19 L20.75,16.81 A2,2 0 0 0 20.02,14.08 L19.87,13.99 A2,2 0 0 1 18.87,12.25 V11.74 A2,2 0 0 1 19.87,10 L20.02,9.91 A2,2 0 0 0 20.75,7.18 L20.53,6.8 A2,2 0 0 0 17.8,6.07 L17.65,6.15 A2,2 0 0 1 15.65,6.15 L15.22,5.9 A2,2 0 0 1 14.22,4.17 V4 A2,2 0 0 0 12.22,2 Z M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12 Z")
    ];
    public string[] RangeOptions => DateRangePresets.All;
    public string[] TrendMetrics { get; } = ["Reading time", "Sessions", "Active books"];
    public string[] Granularities { get; } = ["Auto", "Day", "Week", "Month"];
    public string[] PatternMetrics { get; } = ["Time", "Sessions"];
    public string[] SessionDurations { get; } = ["All durations", "<5m", "5–15m", "15–30m", "30–60m", "60m+"];
    public string[] SessionSorts { get; } = ["Newest", "Oldest", "Longest", "Shortest"];
    public string[] BookSorts { get; } = ["Reading time", "Sessions", "Active days", "Recently read", "Title"];
    public string[] BookFilters { get; } = ["All", "Active in range", "Pinned", "Recently read", "Status: Want to read", "Status: Reading", "Status: Finished", "Status: Paused", "Status: Dropped"];
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
    public ObservableCollection<CompositionPart> YearBookAttention { get; } = [];
    public ObservableCollection<MatrixCell> BookDayMatrix { get; } = [];
    public ObservableCollection<ChartPoint> ReadingFingerprint { get; } = [];
    public ObservableCollection<ChartPoint> RollingMomentum { get; } = [];
    public ObservableCollection<DumbbellDatum> PeriodDumbbells { get; } = [];
    public ObservableCollection<ChartPoint> SessionStaircase { get; } = [];
    public ObservableCollection<PaceDatum> GoalPace { get; } = [];
    public ObservableCollection<ChartPoint> BestReadingDays { get; } = [];
    public ObservableCollection<NoteRow> Notes { get; } = [];
    public ObservableCollection<ChartPoint> NotesByBook { get; } = [];
    public ObservableCollection<ChartPoint> NotesByMonth { get; } = [];
    public ObservableCollection<ChartPoint> NotesByType { get; } = [];

    public ParameterCommand<string> NavigateCommand { get; }
    public ParameterCommand<InsightItem> OpenInsightCommand { get; }
    public ParameterCommand<SessionDisplay> OpenSessionBookCommand { get; }
    public RelayCommand OpenBookInReadestCommand { get; }
    public ParameterCommand<NoteRow> SelectNoteCommand { get; }
    public ParameterCommand<NoteRow> ToggleFavoriteNoteCommand { get; }
    public RelayCommand OpenSelectedNoteCommand { get; }
    public RelayCommand RandomNoteCommand { get; }
    public RelayCommand PreviousRandomNoteCommand { get; }
    public RelayCommand CopySelectedNoteCommand { get; }
    public RelayCommand DailyNoteCommand { get; }
    public AsyncCommand SaveSelectedNoteCommand { get; }
    public AsyncCommand RefreshNotesCommand { get; }
    public AsyncCommand ExportNotesMarkdownCommand { get; }
    public AsyncCommand ExportNotesJsonCommand { get; }
    public AsyncCommand ExportNotesCsvCommand { get; }
    public AsyncCommand TogglePinnedBookCommand { get; }
    public AsyncCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenUpdateCommand { get; }
    public RelayCommand BackCommand { get; }
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
    public AsyncCommand ResetSettingsCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }

    public string SelectedPage { get => _selectedPage; set { if (string.IsNullOrWhiteSpace(value) || value == _selectedPage) return; var previous = _selectedPage; if (Set(ref _selectedPage, value)) { if (!_isGoingBack) _navigationHistory.Push(previous); Raise(nameof(PageSubtitle)); Raise(nameof(Breadcrumb)); Raise(nameof(CanGoBack)); Raise(nameof(IsGlobalRangeVisible)); BackCommand.Refresh(); RefreshSelectedPage(); _applyTheme(Theme); _ = Application.Current.Dispatcher.InvokeAsync(() => _applyTheme(Theme), DispatcherPriority.Loaded); } } }
    public string PageSubtitle => SelectedPage switch { "Overview" => "Your reading activity", "Activity" => "Calendar analytics", "Sessions" => "Continuous reading periods", "Books" => "Your reading library", "Notes" => "Your personal knowledge library", "Goals" => "Targets, pace and history", "Statistics" => "Your personal reading story", "Year in Reading" => "Your annual reading story", _ => "Data, quality and preferences" };
    public string Breadcrumb => $"Readest Stats  /  {SelectedPage}";
    public bool CanGoBack => _navigationHistory.Count > 0;
    public bool IsGlobalRangeVisible => SelectedPage is "Overview" or "Sessions" or "Books" or "Statistics";
    public string SelectedRange { get => _selectedRange; set { if (Set(ref _selectedRange, value)) { _settings.DefaultRangePreset = value; Raise(nameof(IsCustomRange)); RecalculateAll(); SchedulePreferenceSave(); } } }
    public bool IsCustomRange => SelectedRange == "Custom";
    public DateTime? CustomStart { get => _customStart; set { if (Set(ref _customStart, value) && IsCustomRange) { _settings.CustomRangeStart = value is null ? null : DateOnly.FromDateTime(value.Value); RecalculateAll(); SchedulePreferenceSave(); } } }
    public DateTime? CustomEnd { get => _customEnd; set { if (Set(ref _customEnd, value) && IsCustomRange) { _settings.CustomRangeEnd = value is null ? null : DateOnly.FromDateTime(value.Value); RecalculateAll(); SchedulePreferenceSave(); } } }
    public string Status { get => _status; private set => Set(ref _status, value); }
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
    public BookRow? SelectedBook { get => _selectedBook; set { if (Set(ref _selectedBook, value)) { BuildBookDetail(); UpdateOpenBookState(); Raise(nameof(IsSelectedBookPinned)); Raise(nameof(PinBookLabel)); TogglePinnedBookCommand.Refresh(); } } }
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
    public ReadingStory? Story { get => _story; private set => Set(ref _story, value); }
    public string OpenBookStatus { get => _openBookStatus; private set => Set(ref _openBookStatus, value); }
    public string NotesStatus { get => _notesStatus; private set => Set(ref _notesStatus, value); }
    public string NotesCountLabel => Formatters.Count(Notes.Count, "note");
    public bool HasNoVisibleNotes => Notes.Count == 0;
    public string NotesEmptyMessage => "No local Readest notes found.";
    public int TotalNotes => _readestNotes.Count;
    public int FavoriteNotesCount => _allNoteRows.Count(note => note.IsFavorite);
    public int NotesReadCount => _allNoteRows.Count(note => note.TimesSeen > 0);
    public string NoteSearch { get => _noteSearch; set { if (Set(ref _noteSearch, value ?? "")) ApplyNoteFilters(); } }
    public string NotesSummary => _readestNotes.Count == 0 ? "No notes found in the local Readest library." : $"{Formatters.Count(_readestNotes.Count, "note")} across {Formatters.Count(_readestNotes.Select(note => note.BookHash).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "book")}";
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
                tracking.CompletedAtUtc = normalized == "Finished" ? tracking.CompletedAtUtc ?? DateTimeOffset.UtcNow : null;
            }
            Raise(nameof(SelectedBookStatusDetail));
            Raise(nameof(SelectedBookCompletedDate));
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
            Raise(); Raise(nameof(SelectedBookStatusDetail)); _ = SaveBookTrackingAsync();
        }
    }
    public bool IsSelectedBookPinned
    {
        get { var key = SelectedBook is null ? null : BookTrackingKey(SelectedBook.Id); return key is not null && _settings.PinnedBookKeys.Contains(key); }
    }
    public string PinBookLabel => IsSelectedBookPinned ? "Unpin book" : "Pin book";
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
    public string Theme { get => ThemeManager.Normalize(_settings.Theme); set { var normalized = ThemeManager.Normalize(value); if (_settings.Theme == normalized) return; _settings.Theme = normalized; Raise(); _applyTheme(normalized); } }
    public string RefreshMode
    {
        get => _settings.RefreshIntervalSeconds switch { 30 => "30 sec", 60 => "1 min", 300 => "5 min", _ => _settings.AutoRefresh ? "On Readest changes" : "Manual" };
        set { _settings.RefreshIntervalSeconds = value switch { "30 sec" => 30, "1 min" => 60, "5 min" => 300, _ => 0 }; _settings.AutoRefresh = value != "Manual"; Raise(); ConfigureRefresh(); }
    }

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync(); _settings.BookTracking ??= []; _settings.PinnedBookKeys ??= []; _settings.NoteStates ??= []; if (_settings.AutomaticBackups) { try { _store.CreateAutomaticBackup(); } catch (Exception ex) { _log("Automatic backup error: " + ex); } } GoalEngine.Migrate(_settings); _settings.Theme = ThemeManager.Normalize(_settings.Theme); _applyTheme(_settings.Theme); _selectedRange = DateRangePresets.All.Contains(_settings.DefaultRangePreset) ? _settings.DefaultRangePreset : _settings.DefaultRangeDays switch { 1 => "Today", 7 => "7 days", 90 => "90 days", 183 => "6 months", 366 => "1 year", 365 => "This year", -1 => "All time", _ => "30 days" }; _customStart = _settings.CustomRangeStart?.ToDateTime(TimeOnly.MinValue) ?? _customStart; _customEnd = _settings.CustomRangeEnd?.ToDateTime(TimeOnly.MinValue) ?? _customEnd; _trendMetric = TrendMetrics.Contains(_settings.TrendMetric) ? _settings.TrendMetric : "Reading time"; _trendGranularity = Granularities.Contains(_settings.TrendGranularity) ? _settings.TrendGranularity : "Auto"; RaiseSettings();
        var path = await _locator.LocateAsync(_settings.DatabasePath);
        if (path is null) { Status = "No Readest data found"; Error = "Readest data was not found. Choose statistics.db to connect."; RaiseConnectionState(); return; }
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
            var since = incremental && _events.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(_events.Max(e => e.StartTime)) : (DateTimeOffset?)null;
            var eventsTask = _repository.GetEventsAsync(since, cancellationToken: token);
            var notesTask = _notesRepository?.LoadAsync(token) ?? Task.FromResult<IReadOnlyList<ReadestNote>>([]);
            await Task.WhenAll(booksTask, eventsTask, notesTask);
            _bookModels = await booksTask;
            var fresh = await eventsTask;
            _readestNotes = await notesTask;
            _events = since is null ? fresh : _events.Concat(fresh).GroupBy(e => (e.BookId, e.Page, e.StartTime)).Select(g => g.Last()).OrderBy(e => e.StartTime).ToArray();
            Diagnostics = (await _repository.ValidateAsync(token)).Diagnostics;
            if (_goalEngine.SyncYearArchives(_settings, _events, completedBooks: CompletedBookDates())) await _store.SaveAsync(_settings, token);
            BuildNotes();
            RecalculateAll();
            LastSync = DateTimeOffset.Now;
            Status = "Readest connected";
            _log($"Refresh complete · {_events.Count} events · {_bookModels.Count} books · {_readestNotes.Count} notes · {_repository.DatabasePath}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "Could not refresh Readest data. " + Friendly(ex); Status = "Last good data retained"; _log("Refresh error: " + ex); }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private void RecalculateAll()
    {
        Raise(nameof(IsCustomRange)); if (_events.Count == 0) { ClearAnalytics(); BuildDataQuality(); return; }
        var first = _events.Min(e => e.Start); _resolvedRange = _ranges.Resolve(SelectedRange, DateTimeOffset.Now, first, _settings.WeekStartsMonday, CustomStart is null ? null : DateOnly.FromDateTime(CustomStart.Value), CustomEnd is null ? null : DateOnly.FromDateTime(CustomEnd.Value)); _periodEvents = _analytics.Filter(_events, _resolvedRange); _periodSessions = _statistics.BuildSessions(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes)).Where(s => s.DurationSeconds >= MinimumSessionSeconds).ToArray();
        Period = _analytics.Metrics(_events, _resolvedRange, SessionGapMinutes); Comparison = _analytics.Compare(_events, _resolvedRange); SessionProfile = _analytics.Sessions(_periodSessions); WeekdayWeekend = _analytics.WeekdayWeekend(_events, _resolvedRange); CommonWindow = _analytics.CommonWindow(_events, _resolvedRange); Raise(nameof(CommonWindowLabel)); Raise(nameof(ConsistencyDetail));
        _allDaily = _statistics.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)); _allSessions = _statistics.BuildSessions(_events, TimeSpan.FromMinutes(SessionGapMinutes)); AllSessionCount = _allSessions.Count; var streaks = _statistics.Streaks(_allDaily.Select(d => d.Date), DateOnly.FromDateTime(DateTime.Today)); CurrentStreak = streaks.Current; LongestStreak = streaks.Longest; Raise(nameof(CurrentStreak)); Raise(nameof(LongestStreak)); Raise(nameof(CurrentStreakLabel)); Raise(nameof(LongestStreakLabel)); Raise(nameof(StreakContinuation)); Raise(nameof(ComparisonText)); Raise(nameof(AbsoluteDeltaText));
        TopBookLabel = _statistics.RankBooks(_periodEvents, _bookModels).FirstOrDefault()?.Title ?? "No active book"; Raise(nameof(TopBookLabel));
        BuildTrend(); BuildHeatmap(_allDaily); BuildPatterns(); BuildStory(_allDaily); BuildGoals(); BuildRecordsAndInsights(); BuildInfographics(_allDaily); BuildDataQuality(); RefreshSelectedPage(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(ReadingPeriodSummary)); Raise(nameof(SessionsPerActiveDay)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData));
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
            return new BookRow(summary, sessions.Count, sessions.Count == 0 ? 0 : sessions.Average(item => item.DurationSeconds), "—", ReadestLibraryLocator.FindCoverFile(Diagnostics?.Path, model?.Hash), BookStatus(summary.Id));
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
        var titles = _bookModels.ToDictionary(b => b.Id, b => string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title); return sessions.Select(s => new SessionDisplay(_statistics.ToLocal(s.Start), _statistics.ToLocal(s.End), s.DurationSeconds, s.BookIds.Count == 1 ? titles.GetValueOrDefault(s.BookIds[0], "Unknown book") : $"{s.BookIds.Count} books", s.EventCount, s));
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
        var isStatusFilter = BookFilter.StartsWith("Status: ", StringComparison.Ordinal);
        var sourceEvents = _periodEvents;
        var summaries = _statistics.RankBooks(sourceEvents, _bookModels).ToList();
        if (BookFilter == "All" || BookFilter == "Pinned" || isStatusFilter)
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
            return new BookRow(
                summary,
                sessions.Count,
                sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds),
                hour is null ? "—" : FormatHour(hour.Value),
                ReadestLibraryLocator.FindCoverFile(Diagnostics?.Path, model?.Hash),
                BookStatus(summary.Id),
                key is not null && _settings.PinnedBookKeys.Contains(key));
        });
        if (BookFilter == "Recently read") rows = rows.Where(b => b.LastRead >= DateTimeOffset.Now.AddDays(-30));
        if (BookFilter == "Pinned") rows = rows.Where(book => book.IsPinned);
        if (isStatusFilter) rows = rows.Where(book => book.Status == BookFilter[8..]);
        if (!string.IsNullOrWhiteSpace(BookSearch)) rows = rows.Where(b => b.Title.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase) || b.Authors.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase));
        rows = BookSort switch { "Sessions" => rows.OrderByDescending(b => b.Sessions), "Active days" => rows.OrderByDescending(b => b.ActiveDays), "Recently read" => rows.OrderByDescending(b => b.LastRead), "Title" => rows.OrderBy(b => b.Title), _ => rows.OrderByDescending(b => b.Seconds) };
        Replace(Books, rows);
        TopBookLabel = Books.FirstOrDefault()?.Title ?? "No active book";
        Raise(nameof(TopBookLabel));
        SelectedBook = SelectedBook is null ? Books.FirstOrDefault() : Books.FirstOrDefault(book => book.Id == SelectedBook.Id) ?? Books.FirstOrDefault();
    }

    private void BuildBookDetail()
    {
        if (SelectedBook is null) { BookDetail = null; BookTrend.Clear(); BookHourly.Clear(); BookWeekdays.Clear(); BookSessions.Clear(); _selectedBookStatus = "Unspecified"; Raise(nameof(SelectedBookStatus)); Raise(nameof(SelectedBookStatusDetail)); return; } var events = _periodEvents.Where(e => e.BookId == SelectedBook.Id).ToArray(); BookDetail = _statistics.BookDetails(SelectedBook.Summary, events, SessionGapMinutes); var daily = BookDetail.Daily; Replace(BookTrend, daily.Select(d => new ChartPoint(d.Date.ToString("MMM d"), d.Seconds / 60, Formatters.Duration(d.Seconds), "min"))); Replace(BookHourly, Enumerable.Range(0, 24).Select(h => new ChartPoint(FormatHour(h), events.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds) / 60, Unit: "min"))); var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }; Replace(BookWeekdays, order.Select(day => new ChartPoint(day.ToString()[..3], events.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds) / 60, Unit: "min"))); Replace(BookSessions, SessionRows(BookDetail.SessionHistory)); _selectedBookStatus = BookStatus(SelectedBook.Id); Raise(nameof(SelectedBookStatus)); Raise(nameof(SelectedBookStatusDetail)); Raise(nameof(SelectedBookCompletedDate));
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
        Replace(BookDayMatrix, _infographics.BookPeriodMatrix(_events, _bookModels, _resolvedRange));
        Replace(ReadingFingerprint, _infographics.Fingerprint(allDaily, DateOnly.FromDateTime(DateTime.Today)));
        Replace(RollingMomentum, _infographics.RollingMomentum(_events, _resolvedRange, SessionGapMinutes));
        Replace(PeriodDumbbells, _infographics.PeriodDumbbells(_events, _resolvedRange, SessionGapMinutes));
        Replace(SessionStaircase, _infographics.SessionStaircase(_periodSessions));
    }

    private void BuildDataQuality()
    {
        var report = _dataQualityEngine.Analyze(_bookModels, _events, Diagnostics, DateTimeOffset.Now);
        if (_notesRepository?.LastDiagnostics is { } notes)
        {
            var noteCheck = new DataQualityCheck("Readest note files", notes.FilesFailed == 0 ? "Pass" : "Attention", notes.Summary, "Read-only Books/**/config.json scan", notes.FilesFailed > 0);
            var checks = report.Checks.Append(noteCheck).ToArray(); var attention = checks.Count(check => check.NeedsAttention); var passed = checks.Length - attention; var score = Math.Max(0, report.Score - (noteCheck.NeedsAttention ? 10 : 0));
            report = new(score, attention == 0 ? report.Status : "Needs attention", passed, attention, noteCheck.NeedsAttention ? $"{report.Summary} Some Readest note files could not be parsed." : report.Summary, checks);
        }
        DataQuality = report; Replace(DataQualityChecks, report.Checks);
    }

    private void BuildNotes()
    {
        var selectedId = _selectedNote?.Id;
        var randomId = _randomNote?.Id;
        _noteDiscovery.Update(_readestNotes.Select(note => KeyValuePair.Create(note.Id, note.BookHash))); PreviousRandomNoteCommand.Refresh();
        _allNoteRows.Clear();
        foreach (var source in _readestNotes)
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

        NotesStatus = _readestNotes.Count == 0
            ? "No local Readest notes found."
            : $"{Formatters.Count(_readestNotes.Count, "note")} indexed · source remains read-only. {_notesRepository?.LastDiagnostics.Summary}";
        Raise(nameof(NotesCountLabel));
        Raise(nameof(HasNoVisibleNotes));
        Raise(nameof(NotesEmptyMessage));
        Raise(nameof(TotalNotes));
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
        var id = _noteDiscovery.Next(_lastRandomNoteId);
        return id is null ? null : _allNoteRows.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
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
        await SaveNoteStateAsync(SelectedNote);
        NotesStatus = "Personal note settings saved locally.";
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

    private bool CanOpenSelectedNote() => SelectedNote?.BookPath is { } path && File.Exists(path);

    private void OpenSelectedNote()
    {
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
        var path = ReadestLibraryLocator.FindBookFile(Diagnostics?.Path, model?.Hash);
        OpenBookStatus = SelectedBook is null
            ? "Select a book to open it in Readest."
            : path is null
                ? "This book file is not available in the local Readest library."
                : "";
        OpenBookInReadestCommand.Refresh();
    }

    private bool CanOpenSelectedBook()
    {
        var model = SelectedBook is null ? null : _bookModels.FirstOrDefault(book => book.Id == SelectedBook.Id);
        return ReadestLibraryLocator.FindBookFile(Diagnostics?.Path, model?.Hash) is not null;
    }

    private void OpenBookInReadest()
    {
        var model = SelectedBook is null ? null : _bookModels.FirstOrDefault(book => book.Id == SelectedBook.Id);
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

    private string BookStatus(long bookId)
    {
        var key = BookTrackingKey(bookId);
        return key is not null && _settings.BookTracking.TryGetValue(key, out var tracking) ? tracking.Status : "Unspecified";
    }

    private IReadOnlyList<DateTimeOffset> CompletedBookDates() => _settings.BookTracking.Values.Where(item => item.CompletedAtUtc is not null).Select(item => item.CompletedAtUtc!.Value).ToArray();

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
    private async Task BackupSettingsAsync() { var path = _chooseExport("json", "JSON file|*.json"); if (path is not null) { await _store.SaveAsync(_settings); _store.Backup(path); Status = "Settings backup created"; } }
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
    private void ClearAnalytics() { Period = null; Comparison = null; Story = null; SelectedInsight = null; Trend.Clear(); Heatmap.Clear(); Hourly.Clear(); Weekdays.Clear(); TimeBlocks.Clear(); CompletionTimeline.Clear(); StreakTimeline.Clear(); CumulativeJourney.Clear(); MonthlyJourney.Clear(); ComparisonBars.Clear(); StatisticsTopBooks.Clear(); Records.Clear(); QuickInsights.Clear(); AllInsights.Clear(); Sessions.Clear(); Books.Clear(); Goals.Clear(); CalendarDays.Clear(); WeekHourMatrix.Clear(); DayTimeline.Clear(); SessionDots.Clear(); ReadingStyleDays.Clear(); BookAttention.Clear(); YearBookAttention.Clear(); BookDayMatrix.Clear(); ReadingFingerprint.Clear(); RollingMomentum.Clear(); PeriodDumbbells.Clear(); SessionStaircase.Clear(); GoalPace.Clear(); BestReadingDays.Clear(); UpdateOpenBookState(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); }
    private static string Friendly(Exception ex) => ex switch { UnauthorizedAccessException => "Permission denied while reading the database.", IOException => ex.Message, Microsoft.Data.Sqlite.SqliteException => "The database is locked, corrupted, or incompatible.", _ => ex.Message };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private void RaiseConnectionState() { Raise(nameof(IsConnected)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); OpenFolderCommand.Refresh(); }
    private void RaiseSettings() { Raise(nameof(SessionGapMinutes)); Raise(nameof(MinimumSessionSeconds)); Raise(nameof(FirstDayOfWeek)); Raise(nameof(ExperimentalPageMetrics)); Raise(nameof(CompactMode)); Raise(nameof(Use24HourTime)); Raise(nameof(ReduceMotion)); Raise(nameof(AutomaticBackups)); Raise(nameof(Theme)); Raise(nameof(RefreshMode)); Raise(nameof(SelectedRange)); }
    public void Dispose() { _watcher?.Dispose(); _refreshTimer.Stop(); _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _activeRefresh?.Cancel(); _activeRefresh?.Dispose(); _noteSaveDebounce?.Cancel(); _noteSaveDebounce?.Dispose(); _preferenceSaveDebounce?.Cancel(); _preferenceSaveDebounce?.Dispose(); }
}
