using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed record NavItem(string Name, string Icon);

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ReadestDatabaseLocator _locator = new();
    private readonly SettingsStore _store;
    private readonly StatisticsEngine _statistics = new();
    private readonly AnalyticsEngine _analytics = new();
    private readonly DateRangeService _ranges = new();
    private readonly GoalEngine _goalEngine = new();
    private readonly InsightEngine _insightEngine = new();
    private readonly ExportService _export = new();
    private readonly Func<string?> _chooseDatabase;
    private readonly Func<string, string, string?> _chooseExport;
    private readonly Action<string> _log;
    private readonly Action<string> _applyTheme;
    private IReadestRepository? _repository;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshDebounce;
    private CancellationTokenSource? _activeRefresh;
    private readonly DispatcherTimer _refreshTimer = new();
    private IReadOnlyList<ReadingEvent> _events = [];
    private IReadOnlyList<ReadingEvent> _periodEvents = [];
    private IReadOnlyList<Book> _bookModels = [];
    private IReadOnlyList<ReadingSession> _periodSessions = [];
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
    private BookDetail? _bookDetail;
    private string _sessionBook = "All books";
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
    private int _allSessionCount;

    public MainViewModel(Func<string?> chooseDatabase, Func<string, string, string?> chooseExport, Action<string>? log = null, Action<string>? applyTheme = null)
    {
        _chooseDatabase = chooseDatabase; _chooseExport = chooseExport; _log = log ?? (_ => { }); _applyTheme = applyTheme ?? (_ => { }); _store = new(_locator.SettingsPath);
        NavigateCommand = new ParameterCommand<string>(page => { if (!string.IsNullOrWhiteSpace(page)) SelectedPage = page; });
        OpenInsightCommand = new ParameterCommand<InsightItem>(insight => { if (insight is not null) { SelectedInsight = insight; SelectedPage = "Insights"; } });
        RefreshCommand = new AsyncCommand(() => RefreshAsync()); ChangeDatabaseCommand = new AsyncCommand(ChangeDatabaseAsync); RedetectCommand = new AsyncCommand(RedetectAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Diagnostics is not null); ExportCsvCommand = new AsyncCommand(ExportCsvAsync, () => Period is not null); ExportJsonCommand = new AsyncCommand(ExportJsonAsync, () => Period is not null);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync); SaveGoalsCommand = new AsyncCommand(SaveGoalsAsync); BackupSettingsCommand = new AsyncCommand(BackupSettingsAsync); ResetSettingsCommand = new AsyncCommand(ResetSettingsAsync);
        PreviousMonthCommand = new RelayCommand(() => { Month = Month.AddMonths(-1); BuildActivity(); }); NextMonthCommand = new RelayCommand(() => { if (Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) { Month = Month.AddMonths(1); BuildActivity(); } }); TodayCommand = new RelayCommand(() => { Month = new(DateTime.Today.Year, DateTime.Today.Month, 1); SelectedDate = DateOnly.FromDateTime(DateTime.Today); BuildActivity(); });
        PreviousYearCommand = new RelayCommand(() => { Year--; BuildYear(); }); NextYearCommand = new RelayCommand(() => { if (Year < DateTime.Today.Year) { Year++; BuildYear(); } });
        SelectDayCommand = new ParameterCommand<CalendarDayItem>(day => { if (day is not null) { SelectedDate = day.Date; BuildSelectedDay(); } });
        SelectHeatmapDayCommand = new ParameterCommand<ChartPoint>(point => { if (point is not null && DateOnly.TryParse(point.Label, out var date)) { Month = new(date.Year, date.Month, 1); SelectedDate = date; BuildActivity(); SelectedPage = "Activity"; } });
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(incremental: true);
    }

    public string[] Pages { get; } = ["Overview", "Activity", "Sessions", "Books", "Goals", "Insights", "Year in Reading", "Settings"];
    public NavItem[] Navigation { get; } =
    [
        new("Overview", "M3,3 H17 V7 H3 Z M3,10 H9 V17 H3 Z M12,10 H17 V17 H12 Z"),
        new("Activity", "M3,4 H17 V17 H3 Z M3,8 H17 M7,2 V6 M13,2 V6"),
        new("Sessions", "M10,3 A7,7 0 1 1 9.9,3 M10,6 V10 L13,12"),
        new("Books", "M3,3 H9 A2,2 0 0 1 11,5 V17 A3,3 0 0 0 8,14 H3 Z M17,3 H11 A2,2 0 0 0 9,5 V17 A3,3 0 0 1 12,14 H17 Z"),
        new("Goals", "M10,2 A8,8 0 1 1 9.9,2 M10,6 A4,4 0 1 1 9.9,6 M10,9 A1,1 0 1 1 9.9,9"),
        new("Insights", "M10,2 A6,6 0 0 1 14,12 L13,15 H7 L6,12 A6,6 0 0 1 10,2 M8,18 H12"),
        new("Year in Reading", "M10,2 L12,7 L18,7 L13,11 L15,17 L10,13 L5,17 L7,11 L2,7 L8,7 Z"),
        new("Settings", "M10,6 A4,4 0 1 1 9.9,6 M10,1 V4 M10,16 V19 M1,10 H4 M16,10 H19 M3.6,3.6 L5.7,5.7 M14.3,14.3 L16.4,16.4 M16.4,3.6 L14.3,5.7 M5.7,14.3 L3.6,16.4")
    ];
    public string[] RangeOptions => DateRangePresets.All;
    public string[] TrendMetrics { get; } = ["Reading time", "Sessions", "Active books"];
    public string[] Granularities { get; } = ["Auto", "Day", "Week", "Month"];
    public string[] PatternMetrics { get; } = ["Time", "Sessions"];
    public string[] SessionDurations { get; } = ["All durations", "<5m", "5–15m", "15–30m", "30–60m", "60m+"];
    public string[] SessionSorts { get; } = ["Newest", "Oldest", "Longest", "Shortest"];
    public string[] BookSorts { get; } = ["Reading time", "Sessions", "Active days", "Recently read", "Title"];
    public string[] BookFilters { get; } = ["All", "Active in range", "Recently read", "Most read", "Least read"];
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
    public ObservableCollection<SessionDisplay> Sessions { get; } = [];
    public ObservableCollection<string> SessionBookOptions { get; } = ["All books"];
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

    public ParameterCommand<string> NavigateCommand { get; }
    public ParameterCommand<InsightItem> OpenInsightCommand { get; }
    public ParameterCommand<CalendarDayItem> SelectDayCommand { get; }
    public ParameterCommand<ChartPoint> SelectHeatmapDayCommand { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ChangeDatabaseCommand { get; }
    public AsyncCommand RedetectCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }
    public AsyncCommand SaveGoalsCommand { get; }
    public AsyncCommand BackupSettingsCommand { get; }
    public AsyncCommand ResetSettingsCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }

    public string SelectedPage { get => _selectedPage; set { if (Set(ref _selectedPage, value)) { Raise(nameof(PageSubtitle)); _applyTheme(Theme); _ = Application.Current.Dispatcher.InvokeAsync(() => _applyTheme(Theme), DispatcherPriority.Loaded); } } }
    public string PageSubtitle => SelectedPage switch { "Overview" => "Your reading activity", "Activity" => "Calendar analytics", "Sessions" => "Continuous reading periods", "Books" => "Reading by title", "Goals" => "Targets, pace and history", "Insights" => "Patterns derived locally", "Year in Reading" => "Your annual reading story", _ => "Data, refresh and privacy" };
    public string SelectedRange { get => _selectedRange; set { if (Set(ref _selectedRange, value)) RecalculateAll(); } }
    public bool IsCustomRange => SelectedRange == "Custom";
    public DateTime? CustomStart { get => _customStart; set { if (Set(ref _customStart, value) && IsCustomRange) RecalculateAll(); } }
    public DateTime? CustomEnd { get => _customEnd; set { if (Set(ref _customEnd, value) && IsCustomRange) RecalculateAll(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Error { get => _error; private set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public DateTimeOffset? LastSync { get => _lastSync; private set { Set(ref _lastSync, value); Raise(nameof(LastSyncLabel)); } }
    public string LastSyncLabel => LastSync is null ? "Not synced" : $"Last sync {LastSync.Value:HH:mm}";
    public bool IsConnected => _repository is not null;
    public bool HasData => _events.Count > 0;
    public bool NoPeriodData => _periodEvents.Count == 0 && IsConnected;
    public DatabaseDiagnostics? Diagnostics { get => _diagnostics; private set => Set(ref _diagnostics, value); }
    public PeriodMetrics? Period { get => _period; private set => Set(ref _period, value); }
    public ComparisonResult? Comparison { get => _comparison; private set => Set(ref _comparison, value); }
    public SessionProfile? SessionProfile { get => _sessionProfile; private set => Set(ref _sessionProfile, value); }
    public WeekdayWeekendStats? WeekdayWeekend { get => _weekdayWeekend; private set => Set(ref _weekdayWeekend, value); }
    public ReadingWindow? CommonWindow { get => _commonWindow; private set => Set(ref _commonWindow, value); }
    public string CommonWindowLabel => CommonWindow is null ? "—" : $"{CommonWindow.StartHour:00}:00–{(CommonWindow.StartHour + CommonWindow.Hours) % 24:00}:00";
    public string ConsistencyDetail => Period is null ? "No eligible days" : $"{Formatters.Count(Period.ActiveDays, "active day")} of {Formatters.Count(Period.AvailableDays, "eligible day")}";
    public string ComparisonText => Comparison is null || !Comparison.HasBaseline ? "No previous-period baseline" : $"{Comparison.PercentDelta:+0.#;-0.#;0}% vs previous period";
    public string AbsoluteDeltaText => Comparison is null ? "—" : $"{(Comparison.AbsoluteDelta >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(Comparison.AbsoluteDelta))}";
    public double SessionsPerActiveDay => Period is null || Period.ActiveDays == 0 ? 0 : (double)Period.SessionCount / Period.ActiveDays;
    public int CurrentStreak { get; private set; }
    public int LongestStreak { get; private set; }
    public string CurrentStreakLabel => Formatters.Count(CurrentStreak, "day");
    public string LongestStreakLabel => $"Best: {Formatters.Count(LongestStreak, "day")}";
    public string StreakContinuation => CurrentStreak > 0 && !_statistics.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)).Any(d => d.Date == DateOnly.FromDateTime(DateTime.Today)) ? $"Read today to continue your {Formatters.Count(CurrentStreak, "day")} streak." : "";
    public string TopBookLabel { get; private set; } = "No active book";
    public string TrendMetric { get => _trendMetric; set { if (Set(ref _trendMetric, value)) BuildTrend(); } }
    public string TrendGranularity { get => _trendGranularity; set { if (Set(ref _trendGranularity, value)) BuildTrend(); } }
    public string TrendSummary => TrendMetric == "Reading time" ? $"{Formatters.Duration(Period?.TotalSeconds ?? 0)} total · {Formatters.Duration(Period?.AveragePerCalendarDay ?? 0)}/day" : $"{Trend.Sum(x => x.Value):0} total";
    public string PatternMetric { get => _patternMetric; set { if (Set(ref _patternMetric, value)) BuildPatterns(); } }
    public DateTime Month { get => _month; set { if (Set(ref _month, new DateTime(value.Year, value.Month, 1))) Raise(nameof(MonthLabel)); } }
    public string MonthLabel => Month.ToString("MMMM yyyy");
    public DateOnly SelectedDate { get => _selectedDate; private set => Set(ref _selectedDate, value); }
    public DayDetails? SelectedDay { get => _selectedDay; private set => Set(ref _selectedDay, value); }
    public SessionDisplay? SelectedSession { get => _selectedSession; set { if (Set(ref _selectedSession, value)) BuildSessionDetail(); } }
    public BookRow? SelectedBook { get => _selectedBook; set { if (Set(ref _selectedBook, value)) BuildBookDetail(); } }
    public BookDetail? BookDetail { get => _bookDetail; private set => Set(ref _bookDetail, value); }
    public InsightItem? SelectedInsight { get => _selectedInsight; set => Set(ref _selectedInsight, value); }
    public string SessionBook { get => _sessionBook; set { var normalized = string.IsNullOrWhiteSpace(value) ? "All books" : value; if (Set(ref _sessionBook, normalized)) ApplySessionFilters(); } }
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
    public int AllSessionCount { get => _allSessionCount; private set => Set(ref _allSessionCount, value); }
    public string PageMetricReliability => ExperimentalPageMetrics ? "Approximate · enabled" : "Approximate · hidden";

    public int SessionGapMinutes { get => _settings.SessionGapMinutes; set { _settings.SessionGapMinutes = value; Raise(); RecalculateAll(); } }
    public int MinimumSessionSeconds { get => _settings.MinimumSessionSeconds; set { _settings.MinimumSessionSeconds = Math.Max(0, value); Raise(); RecalculateAll(); } }
    public string FirstDayOfWeek { get => _settings.WeekStartsMonday ? "Monday" : "Sunday"; set { _settings.WeekStartsMonday = value != "Sunday"; Raise(); RecalculateAll(); } }
    public bool ExperimentalPageMetrics { get => _settings.ExperimentalPageMetrics; set { _settings.ExperimentalPageMetrics = value; Raise(); Raise(nameof(PageMetricReliability)); } }
    public bool CompactMode { get => _settings.CompactMode; set { _settings.CompactMode = value; Raise(); } }
    public bool Use24HourTime { get => _settings.Use24HourTime; set { _settings.Use24HourTime = value; Raise(); } }
    public bool ReduceMotion { get => _settings.ReduceMotion; set { _settings.ReduceMotion = value; Raise(); } }
    public string Theme { get => ThemeManager.Normalize(_settings.Theme); set { var normalized = ThemeManager.Normalize(value); if (_settings.Theme == normalized) return; _settings.Theme = normalized; Raise(); _applyTheme(normalized); } }
    public string RefreshMode
    {
        get => _settings.RefreshIntervalSeconds switch { 30 => "30 sec", 60 => "1 min", 300 => "5 min", _ => _settings.AutoRefresh ? "On Readest changes" : "Manual" };
        set { _settings.RefreshIntervalSeconds = value switch { "30 sec" => 30, "1 min" => 60, "5 min" => 300, _ => 0 }; _settings.AutoRefresh = value != "Manual"; Raise(); ConfigureRefresh(); }
    }

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync(); GoalEngine.Migrate(_settings); _settings.Theme = ThemeManager.Normalize(_settings.Theme); _applyTheme(_settings.Theme); _selectedRange = _settings.DefaultRangeDays switch { 7 => "7 days", 90 => "90 days", 365 => "This year", -1 => "All time", _ => "30 days" }; RaiseSettings();
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
            if (!validation.IsValid) { _repository = null; Diagnostics = null; Error = validation.Message; Status = "Incompatible database"; return; }
            _repository = repository; Diagnostics = validation.Diagnostics; _settings.DatabasePath = path; await _store.SaveAsync(_settings); Status = "Loading Readest activity…"; await RefreshAsync(force: true); ConfigureRefresh();
        }
        catch (Exception ex) { _repository = null; Error = Friendly(ex); Status = "Connection failed"; }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private async Task RefreshAsync(bool incremental = false, bool force = false)
    {
        if (_repository is null || (IsBusy && !force)) return; _activeRefresh?.Cancel(); _activeRefresh?.Dispose(); _activeRefresh = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = _activeRefresh.Token; IsBusy = true; Error = null;
        try
        {
            var booksTask = _repository.GetBooksAsync(token); var since = incremental && _events.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(_events.Max(e => e.StartTime)) : (DateTimeOffset?)null; var eventsTask = _repository.GetEventsAsync(since, cancellationToken: token); await Task.WhenAll(booksTask, eventsTask); _bookModels = await booksTask; var fresh = await eventsTask; _events = since is null ? fresh : _events.Concat(fresh).GroupBy(e => (e.BookId, e.Page, e.StartTime)).Select(g => g.Last()).OrderBy(e => e.StartTime).ToArray(); Diagnostics = (await _repository.ValidateAsync(token)).Diagnostics; if (_goalEngine.SyncYearArchives(_settings, _events)) await _store.SaveAsync(_settings, token); RecalculateAll(); LastSync = DateTimeOffset.Now; Status = "Readest connected"; _log($"Refresh complete · {_events.Count} events · {_bookModels.Count} books · {_repository.DatabasePath}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error = "Could not refresh Readest data. " + Friendly(ex); Status = "Last good data retained"; _log("Refresh error: " + ex); }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private void RecalculateAll()
    {
        Raise(nameof(IsCustomRange)); if (_events.Count == 0) { ClearAnalytics(); return; }
        var first = _events.Min(e => e.Start); _resolvedRange = _ranges.Resolve(SelectedRange, DateTimeOffset.Now, first, _settings.WeekStartsMonday, CustomStart is null ? null : DateOnly.FromDateTime(CustomStart.Value), CustomEnd is null ? null : DateOnly.FromDateTime(CustomEnd.Value)); _periodEvents = _analytics.Filter(_events, _resolvedRange); _periodSessions = _statistics.BuildSessions(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes)).Where(s => s.DurationSeconds >= MinimumSessionSeconds).ToArray();
        Period = _analytics.Metrics(_events, _resolvedRange, SessionGapMinutes); Comparison = _analytics.Compare(_events, _resolvedRange); SessionProfile = _analytics.Sessions(_periodSessions); WeekdayWeekend = _analytics.WeekdayWeekend(_events, _resolvedRange); CommonWindow = _analytics.CommonWindow(_events, _resolvedRange); Raise(nameof(CommonWindowLabel)); Raise(nameof(ConsistencyDetail));
        var allDaily = _statistics.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)); AllSessionCount = _statistics.BuildSessions(_events, TimeSpan.FromMinutes(SessionGapMinutes)).Count; var streaks = _statistics.Streaks(allDaily.Select(d => d.Date), DateOnly.FromDateTime(DateTime.Today)); CurrentStreak = streaks.Current; LongestStreak = streaks.Longest; Raise(nameof(CurrentStreak)); Raise(nameof(LongestStreak)); Raise(nameof(CurrentStreakLabel)); Raise(nameof(LongestStreakLabel)); Raise(nameof(StreakContinuation)); Raise(nameof(ComparisonText)); Raise(nameof(AbsoluteDeltaText));
        BuildTrend(); BuildHeatmap(allDaily); BuildPatterns(); ApplySessionFilters(); ApplyBookFilters(); BuildActivity(); BuildGoals(); BuildRecordsAndInsights(); BuildYear(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(SessionsPerActiveDay)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData));
    }

    private void BuildTrend() { if (_resolvedRange is null) return; Replace(Trend, _analytics.AggregateTrend(_events, _resolvedRange, TrendMetric, TrendGranularity, SessionGapMinutes)); Raise(nameof(TrendSummary)); }
    private void BuildHeatmap(IReadOnlyList<DailyStat> daily)
    {
        var map = daily.ToDictionary(d => d.Date); var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-364)); Replace(Heatmap, Enumerable.Range(0, 365).Select(i => { var date = start.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : $"{date:MMM d, yyyy}\n{Formatters.Duration(d.Seconds)} · {Formatters.Count(d.Sessions, "session")} · {Formatters.Count(d.Books, "book")}"); }));
    }

    private void BuildPatterns()
    {
        if (_resolvedRange is null) return; var sessions = _periodSessions; var total = _periodEvents.Sum(e => e.DurationSeconds);
        Replace(Hourly, Enumerable.Range(0, 24).Select(h => { double value = PatternMetric == "Sessions" ? sessions.Count(s => _statistics.ToLocal(s.Start).Hour == h) : _periodEvents.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds) / 60; return new ChartPoint($"{h:00}", value, PatternMetric == "Time" ? Formatters.Duration(value * 60) : Formatters.Count((int)value, "session")); }));
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }; Replace(Weekdays, order.Select(day => { double value = PatternMetric == "Sessions" ? sessions.Count(s => _statistics.ToLocal(s.Start).DayOfWeek == day) : _periodEvents.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds) / 60; return new ChartPoint(day.ToString()[..3], value, PatternMetric == "Time" ? Formatters.Duration(value * 60) : Formatters.Count((int)value, "session")); }));
    }

    private void BuildRecordsAndInsights()
    {
        var span = _resolvedRange is null ? 30 : Math.Max(1, _resolvedRange.EndDate.DayNumber - _resolvedRange.StartDate.DayNumber + 1); var overview = _statistics.Overview(_periodEvents, _bookModels, span, TimeSpan.FromMinutes(SessionGapMinutes)); Replace(Records, overview.Records); Replace(SessionHistogram, SessionProfile?.Histogram ?? []); var goalProgress = Goals.Select(g => g.Progress).ToArray(); var insights = _resolvedRange is null ? [] : _insightEngine.Generate(_events, _bookModels, _resolvedRange, SessionGapMinutes, goalProgress); var selectedId = SelectedInsight?.Id; Replace(AllInsights, insights); Replace(QuickInsights, insights.Take(5)); SelectedInsight = AllInsights.FirstOrDefault(i => i.Id == selectedId) ?? AllInsights.FirstOrDefault();
    }

    private IEnumerable<SessionDisplay> SessionRows(IEnumerable<ReadingSession> sessions)
    {
        var titles = _bookModels.ToDictionary(b => b.Id, b => string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title); return sessions.Select(s => new SessionDisplay(_statistics.ToLocal(s.Start), _statistics.ToLocal(s.End), s.DurationSeconds, s.BookIds.Count == 1 ? titles.GetValueOrDefault(s.BookIds[0], "Unknown book") : $"{s.BookIds.Count} books", s.EventCount, s));
    }

    private void ApplySessionFilters()
    {
        var rows = SessionRows(_periodSessions); if (SessionBook != "All books") rows = rows.Where(s => s.Books.Contains(SessionBook, StringComparison.CurrentCultureIgnoreCase)); if (!string.IsNullOrWhiteSpace(SessionSearch)) rows = rows.Where(s => s.Books.Contains(SessionSearch, StringComparison.CurrentCultureIgnoreCase)); rows = SessionDuration switch { "<5m" => rows.Where(s => s.DurationSeconds < 300), "5–15m" => rows.Where(s => s.DurationSeconds >= 300 && s.DurationSeconds < 900), "15–30m" => rows.Where(s => s.DurationSeconds >= 900 && s.DurationSeconds < 1800), "30–60m" => rows.Where(s => s.DurationSeconds >= 1800 && s.DurationSeconds < 3600), "60m+" => rows.Where(s => s.DurationSeconds >= 3600), _ => rows }; rows = SessionSort switch { "Oldest" => rows.OrderBy(s => s.Start), "Longest" => rows.OrderByDescending(s => s.DurationSeconds), "Shortest" => rows.OrderBy(s => s.DurationSeconds), _ => rows.OrderByDescending(s => s.Start) }; Replace(Sessions, rows); var selectedBook = SessionBook; var bookOptions = new[] { "All books" }.Concat(_bookModels.Select(b => b.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Order()).ToArray(); SyncSessionBookOptions(bookOptions); _sessionBook = bookOptions.Contains(selectedBook) ? selectedBook : "All books"; Raise(nameof(SessionBook)); if (SelectedSession is null || !Sessions.Any(s => s.Source == SelectedSession.Source)) SelectedSession = Sessions.FirstOrDefault();
    }

    private void SyncSessionBookOptions(IReadOnlyCollection<string> desired)
    {
        for (var i = SessionBookOptions.Count - 1; i >= 0; i--) if (!desired.Contains(SessionBookOptions[i])) SessionBookOptions.RemoveAt(i);
        foreach (var item in desired) if (!SessionBookOptions.Contains(item)) SessionBookOptions.Add(item);
    }

    private void BuildSessionDetail()
    {
        if (SelectedSession is null) { SessionTimeline.Clear(); return; } var start = SelectedSession.Source.Start; var end = SelectedSession.Source.End; var items = _periodEvents.Where(e => e.Start >= start && e.Start <= end).OrderBy(e => e.Start).Select(e => new ChartPoint(_statistics.ToLocal(e.Start).ToString("HH:mm:ss"), e.DurationSeconds / 60, Formatters.Duration(e.DurationSeconds))); Replace(SessionTimeline, items);
    }

    private void ApplyBookFilters()
    {
        var sourceEvents = BookFilter == "All" ? _events : _periodEvents; var summaries = _statistics.RankBooks(sourceEvents, _bookModels); var rows = summaries.Select(summary => { var sessions = _statistics.BuildSessions(sourceEvents.Where(e => e.BookId == summary.Id), TimeSpan.FromMinutes(SessionGapMinutes), summary.Id); var hour = sourceEvents.Where(e => e.BookId == summary.Id).GroupBy(e => _statistics.ToLocal(e.Start).Hour).MaxBy(g => g.Sum(e => e.DurationSeconds))?.Key; return new BookRow(summary, sessions.Count, sessions.Count == 0 ? 0 : sessions.Average(s => s.DurationSeconds), hour is null ? "—" : $"{hour:00}:00"); }); if (BookFilter == "Recently read") rows = rows.Where(b => b.LastRead >= DateTimeOffset.Now.AddDays(-30)); if (!string.IsNullOrWhiteSpace(BookSearch)) rows = rows.Where(b => b.Title.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase) || b.Authors.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase)); rows = BookSort switch { "Sessions" => rows.OrderByDescending(b => b.Sessions), "Active days" => rows.OrderByDescending(b => b.ActiveDays), "Recently read" => rows.OrderByDescending(b => b.LastRead), "Title" => rows.OrderBy(b => b.Title), _ => BookFilter == "Least read" ? rows.OrderBy(b => b.Seconds) : rows.OrderByDescending(b => b.Seconds) }; Replace(Books, rows); TopBookLabel = Books.FirstOrDefault()?.Title ?? "No active book"; Raise(nameof(TopBookLabel)); if (SelectedBook is null || !Books.Any(b => b.Id == SelectedBook.Id)) SelectedBook = Books.FirstOrDefault();
    }

    private void BuildBookDetail()
    {
        if (SelectedBook is null) { BookDetail = null; BookTrend.Clear(); BookHourly.Clear(); BookWeekdays.Clear(); BookSessions.Clear(); return; } var events = _periodEvents.Where(e => e.BookId == SelectedBook.Id).ToArray(); BookDetail = _statistics.BookDetails(SelectedBook.Summary, events, SessionGapMinutes); var daily = BookDetail.Daily; Replace(BookTrend, daily.Select(d => new ChartPoint(d.Date.ToString("MMM d"), d.Seconds / 60, Formatters.Duration(d.Seconds)))); Replace(BookHourly, Enumerable.Range(0, 24).Select(h => new ChartPoint($"{h:00}", events.Where(e => _statistics.ToLocal(e.Start).Hour == h).Sum(e => e.DurationSeconds) / 60))); var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }; Replace(BookWeekdays, order.Select(day => new ChartPoint(day.ToString()[..3], events.Where(e => _statistics.ToLocal(e.Start).DayOfWeek == day).Sum(e => e.DurationSeconds) / 60))); Replace(BookSessions, SessionRows(BookDetail.SessionHistory));
    }

    private void BuildActivity()
    {
        var monthStart = new DateOnly(Month.Year, Month.Month, 1); var gridStart = monthStart.AddDays(-((_settings.WeekStartsMonday ? (int)monthStart.DayOfWeek + 6 : (int)monthStart.DayOfWeek) % 7)); Replace(WeekdayHeaders, _settings.WeekStartsMonday ? new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" } : new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }); var daily = _statistics.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)).ToDictionary(d => d.Date); var sessions = _statistics.BuildSessions(_events, TimeSpan.FromMinutes(SessionGapMinutes)); var max = daily.Where(x => x.Key.Year == Month.Year && x.Key.Month == Month.Month).Select(x => x.Value.Seconds).DefaultIfEmpty().Max(); Replace(CalendarDays, Enumerable.Range(0, 42).Select(i => { var date = gridStart.AddDays(i); var d = daily.GetValueOrDefault(date); var value = d?.Seconds ?? 0; var level = value <= 0 || max <= 0 ? "None" : (value / max) switch { < .25 => "Low", < .5 => "Medium", < .75 => "High", _ => "Peak" }; return new CalendarDayItem(date, date.Month == Month.Month, value, sessions.Count(s => DateOnly.FromDateTime(_statistics.ToLocal(s.Start).DateTime) == date), d?.Books ?? 0, level, value <= 0 ? "" : Formatters.Duration(value)); })); Replace(ActivityMonthTrend, Enumerable.Range(0, DateTime.DaysInMonth(Month.Year, Month.Month)).Select(i => { var date = monthStart.AddDays(i); var d = daily.GetValueOrDefault(date); return new ChartPoint(date.Day.ToString(), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : $"{date:MMM d}\n{Formatters.Duration(d.Seconds)} · {Formatters.Count(d.Sessions, "session")} · {Formatters.Count(d.Books, "book")}"); })); if (SelectedDate.Month != Month.Month || SelectedDate.Year != Month.Year) SelectedDate = monthStart; BuildSelectedDay(); Raise(nameof(MonthSummary));
    }

    public string MonthSummary { get { var items = CalendarDays.Where(d => d.IsInMonth && d.Seconds > 0).ToArray(); return items.Length == 0 ? "No reading activity this month." : $"{Formatters.Duration(items.Sum(d => d.Seconds))} · {Formatters.Count(items.Length, "active day")} · {Formatters.Count(items.Sum(d => d.Sessions), "session")} · {Formatters.Count(_events.Where(e => { var d = _statistics.ToLocal(e.Start); return d.Year == Month.Year && d.Month == Month.Month; }).Select(e => e.BookId).Distinct().Count(), "book active", "books active")}"; } }
    private void BuildSelectedDay()
    {
        var sessions = _statistics.BuildSessions(_events, TimeSpan.FromMinutes(SessionGapMinutes)).Where(s => DateOnly.FromDateTime(_statistics.ToLocal(s.Start).DateTime) == SelectedDate).ToArray(); var events = _events.Where(e => DateOnly.FromDateTime(_statistics.ToLocal(e.Start).DateTime) == SelectedDate).ToArray(); var rows = SessionRows(sessions).ToArray(); var titles = _bookModels.ToDictionary(b => b.Id, b => b.Title); var books = events.GroupBy(e => e.BookId).Select(g => new ChartPoint(titles.GetValueOrDefault(g.Key, "Unknown book"), g.Sum(e => e.DurationSeconds), Formatters.Duration(g.Sum(e => e.DurationSeconds)))).OrderByDescending(x => x.Value).ToArray(); SelectedDay = new(SelectedDate, events.Sum(e => e.DurationSeconds), sessions.Length, events.Select(e => e.BookId).Distinct().Count(), rows.FirstOrDefault()?.Start, rows.LastOrDefault()?.End, rows, books); Replace(DaySessions, rows); Replace(DayBooks, books);
    }

    private void BuildGoals()
    {
        foreach (var editor in Goals) editor.Commit(); var definitions = GoalEngine.Migrate(_settings); Replace(Goals, definitions.Select(d => new GoalEditor(d, _goalEngine.Progress(d, _events, SessionGapMinutes), _goalEngine.History(d, _events, SessionGapMinutes)))); Replace(GoalArchives, _settings.GoalArchives.OrderByDescending(a => a.Year)); CurrentYearBookGoal = CreateYearBookGoal(DateTime.Today.Year);
    }

    private void BuildYear()
    {
        YearSummary = _analytics.Year(_events, _bookModels, Year, SessionGapMinutes); Replace(YearMonths, YearSummary.Months); Replace(YearHeatmap, YearSummary.Heatmap); Replace(YearTopBooks, YearSummary.TopBooks); YearBookGoal = CreateYearBookGoal(Year); Raise(nameof(YearBestMonth)); Raise(nameof(YearBestDay)); Raise(nameof(YearTopBook)); Raise(nameof(YearLongestStreakLabel));
    }

    private YearBookGoalSummary CreateYearBookGoal(int year)
    {
        var books = _events.Where(e => _statistics.ToLocal(e.Start).Year == year).Select(e => e.BookId).Distinct().Count();
        var archive = _settings.GoalArchives.FirstOrDefault(a => a.Year == year);
        var target = archive?.TargetBooks ?? GoalEngine.Migrate(_settings).First(g => g.Period == GoalPeriod.Yearly && g.Metric == GoalMetric.Books).TargetValue;
        var progress = target <= 0 ? 0 : Math.Min(100, books * 100d / target);
        var status = target <= 0 ? "No goal set" : books >= target ? "Goal reached" : year < DateTime.Today.Year ? "Year complete" : "In progress";
        var detail = archive is not null ? "Archived yearly result" : year == DateTime.Today.Year ? "Current yearly goal" : "No saved goal snapshot for this year";
        return new(year, books, target, progress, status, detail);
    }

    private async Task ChangeDatabaseAsync() { var path = _chooseDatabase(); if (path is not null) await ConnectAsync(path); }
    private async Task RedetectAsync() { _settings.DatabasePath = null; var path = await _locator.LocateAsync(); if (path is null) { Error = "Readest data was not found."; RaiseConnectionState(); } else await ConnectAsync(path); }
    private void OpenFolder() { if (Diagnostics is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Diagnostics.Path}\"") { UseShellExecute = true }); }
    private async Task ExportCsvAsync() { var path = _chooseExport("csv", "CSV file|*.csv"); if (path is not null) await _export.ExportCsvAsync(path, _statistics.Daily(_periodEvents, TimeSpan.FromMinutes(SessionGapMinutes))); }
    private async Task ExportJsonAsync() { var path = _chooseExport("json", "JSON file|*.json"); if (path is not null && _resolvedRange is not null) await _export.ExportJsonAsync(path, _statistics.Overview(_periodEvents, _bookModels, Math.Max(1, _resolvedRange.EndDate.DayNumber - _resolvedRange.StartDate.DayNumber + 1), TimeSpan.FromMinutes(SessionGapMinutes)), Books.Select(b => b.Summary)); }
    private async Task SaveSettingsAsync() { _settings.DefaultRangeDays = SelectedRange switch { "7 days" => 7, "90 days" => 90, "This year" => 365, "All time" => -1, _ => 30 }; await _store.SaveAsync(_settings); Status = "Settings saved"; ConfigureRefresh(); }
    private async Task SaveGoalsAsync() { foreach (var goal in Goals) goal.Commit(); _settings.Goals = Goals.Select(g => g.Definition).ToList(); await _store.SaveAsync(_settings); BuildGoals(); BuildRecordsAndInsights(); BuildYear(); Status = "Goals saved"; }
    private async Task BackupSettingsAsync() { var path = _chooseExport("json", "JSON file|*.json"); if (path is not null) { await _store.SaveAsync(_settings); _store.Backup(path); Status = "Settings backup created"; } }
    private async Task ResetSettingsAsync() { if (MessageBox.Show("Reset Readest Stats settings and goals? Readest data will not be changed.", "Readest Stats", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return; var database = _settings.DatabasePath; _settings = new() { DatabasePath = database }; GoalEngine.Migrate(_settings); _applyTheme(_settings.Theme); await _store.SaveAsync(_settings); RaiseSettings(); RecalculateAll(); Status = "App-owned settings reset"; }

    private void ConfigureRefresh()
    {
        _watcher?.Dispose(); _watcher = null; _refreshTimer.Stop(); if (!_settings.AutoRefresh || _repository is null) return;
        if (_settings.RefreshIntervalSeconds > 0) { _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds); _refreshTimer.Start(); return; }
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_repository.DatabasePath)!) { Filter = "statistics.db*", NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true }; _watcher.Changed += WatcherChanged; _watcher.Created += WatcherChanged; _watcher.Renamed += WatcherChanged; _watcher.Deleted += WatcherChanged;
    }
    private void WatcherChanged(object sender, FileSystemEventArgs e) { _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _refreshDebounce = new(); var token = _refreshDebounce.Token; _ = Application.Current.Dispatcher.InvokeAsync(async () => { try { await Task.Delay(1500, token); await RefreshAsync(incremental: true); } catch (OperationCanceledException) { } }); }
    private void ClearAnalytics() { Period = null; Comparison = null; SelectedInsight = null; Trend.Clear(); Heatmap.Clear(); Hourly.Clear(); Weekdays.Clear(); Records.Clear(); QuickInsights.Clear(); AllInsights.Clear(); Sessions.Clear(); Books.Clear(); Goals.Clear(); CalendarDays.Clear(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); }
    private static string Friendly(Exception ex) => ex switch { UnauthorizedAccessException => "Permission denied while reading the database.", IOException => ex.Message, Microsoft.Data.Sqlite.SqliteException => "The database is locked, corrupted, or incompatible.", _ => ex.Message };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private void RaiseConnectionState() { Raise(nameof(IsConnected)); Raise(nameof(NoPeriodData)); Raise(nameof(HasData)); OpenFolderCommand.Refresh(); }
    private void RaiseSettings() { Raise(nameof(SessionGapMinutes)); Raise(nameof(MinimumSessionSeconds)); Raise(nameof(FirstDayOfWeek)); Raise(nameof(ExperimentalPageMetrics)); Raise(nameof(CompactMode)); Raise(nameof(Use24HourTime)); Raise(nameof(ReduceMotion)); Raise(nameof(Theme)); Raise(nameof(RefreshMode)); Raise(nameof(SelectedRange)); }
    public void Dispose() { _watcher?.Dispose(); _refreshTimer.Stop(); _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _activeRefresh?.Cancel(); _activeRefresh?.Dispose(); }
}
