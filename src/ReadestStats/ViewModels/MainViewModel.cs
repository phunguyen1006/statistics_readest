using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ReadestDatabaseLocator _locator = new();
    private readonly SettingsStore _store;
    private readonly StatisticsEngine _engine = new();
    private readonly ExportService _export = new();
    private readonly Func<string?> _chooseDatabase;
    private readonly Func<string, string, string?> _chooseExport;
    private IReadestRepository? _repository;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _refreshDebounce;
    private IReadOnlyList<ReadingEvent> _events = [];
    private IReadOnlyList<Book> _bookModels = [];
    private AppSettings _settings = new();
    private string _status = "Finding Readest data…";
    private string? _error;
    private bool _isBusy;
    private OverviewStats? _overview;
    private SessionSummary? _sessionSummary;
    private DatabaseDiagnostics? _diagnostics;
    private BookSummary? _selectedBook;
    private BookDetail? _bookDetail;
    private SessionDisplay? _selectedSession;
    private string _bookSearch = "";
    private string _bookSort = "Most read";
    private string _range = "30 days";
    private string _weekdayMode = "Total time";
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private int _year = DateTime.Today.Year;

    public MainViewModel(Func<string?> chooseDatabase, Func<string, string, string?> chooseExport)
    {
        _chooseDatabase = chooseDatabase; _chooseExport = chooseExport; _store = new(_locator.SettingsPath);
        RefreshCommand = new(() => RefreshAsync()); ChangeDatabaseCommand = new(ChangeDatabaseAsync); RedetectCommand = new(RedetectAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Diagnostics is not null); ExportCsvCommand = new(ExportCsvAsync, () => Overview is not null); ExportJsonCommand = new(ExportJsonAsync, () => Overview is not null);
        PreviousMonthCommand = new RelayCommand(() => { Month = Month.AddMonths(-1); RecalculatePeriods(); });
        NextMonthCommand = new RelayCommand(() => { Month = Month.AddMonths(1); RecalculatePeriods(); }, () => Month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1));
        PreviousYearCommand = new RelayCommand(() => { Year--; RecalculatePeriods(); }); NextYearCommand = new RelayCommand(() => { if (Year < DateTime.Today.Year) Year++; RecalculatePeriods(); }, () => Year < DateTime.Today.Year);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync);
    }

    public ObservableCollection<DailyStat> Daily { get; } = [];
    public ObservableCollection<ChartPoint> Trend { get; } = [];
    public ObservableCollection<ChartPoint> Heatmap { get; } = [];
    public ObservableCollection<ChartPoint> Hourly { get; } = [];
    public ObservableCollection<ChartPoint> Weekdays { get; } = [];
    public ObservableCollection<ChartPoint> WeekdaysDisplay { get; } = [];
    public ObservableCollection<ChartPoint> Monthly { get; } = [];
    public ObservableCollection<ChartPoint> Yearly { get; } = [];
    public ObservableCollection<ChartPoint> BookTrend { get; } = [];
    public ObservableCollection<ChartPoint> MonthlyHeatmap { get; } = [];
    public ObservableCollection<ChartPoint> YearlyHeatmap { get; } = [];
    public ObservableCollection<ChartPoint> SessionTimeline { get; } = [];
    public ObservableCollection<ChartPoint> BookHeatmap { get; } = [];
    public ObservableCollection<ChartPoint> BookHourly { get; } = [];
    public ObservableCollection<ChartPoint> BookWeekdays { get; } = [];
    public ObservableCollection<SessionDisplay> BookSessions { get; } = [];
    public ObservableCollection<BookSummary> Books { get; } = [];
    public ObservableCollection<SessionDisplay> Sessions { get; } = [];
    public ObservableCollection<string> Insights { get; } = [];
    public ObservableCollection<PersonalRecord> Records { get; } = [];

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ChangeDatabaseCommand { get; }
    public AsyncCommand RedetectCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncCommand ExportCsvCommand { get; }
    public AsyncCommand ExportJsonCommand { get; }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand PreviousYearCommand { get; }
    public RelayCommand NextYearCommand { get; }
    public AsyncCommand SaveSettingsCommand { get; }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string? Error { get => _error; private set { Set(ref _error, value); Raise(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool HasData => _events.Count > 0;
    public bool NoData => _repository is not null && _events.Count == 0 && !HasError;
    public bool NotConnected => _repository is null;
    public OverviewStats? Overview { get => _overview; private set => Set(ref _overview, value); }
    public SessionSummary? SessionSummary { get => _sessionSummary; private set => Set(ref _sessionSummary, value); }
    public DatabaseDiagnostics? Diagnostics { get => _diagnostics; private set => Set(ref _diagnostics, value); }
    public BookSummary? SelectedBook { get => _selectedBook; set { if (Set(ref _selectedBook, value)) UpdateBookDetail(value); } }
    public BookDetail? BookDetail { get => _bookDetail; private set => Set(ref _bookDetail, value); }
    public SessionDisplay? SelectedSession { get => _selectedSession; set { if (Set(ref _selectedSession, value)) { var events = value is null ? [] : _events.Where(e => e.Start >= value.Source.Start && e.Start <= value.Source.End).ToArray(); Replace(SessionTimeline, events.Select(e => new ChartPoint(_engine.ToLocal(e.Start).ToString("HH:mm"), e.DurationSeconds / 60, Formatters.Duration(e.DurationSeconds)))); } } }
    public string BookSearch { get => _bookSearch; set { if (Set(ref _bookSearch, value)) FilterBooks(); } }
    public string BookSort { get => _bookSort; set { if (Set(ref _bookSort, value)) FilterBooks(); } }
    public string Range { get => _range; set { if (Set(ref _range, value)) Recalculate(); } }
    public string WeekdayMode { get => _weekdayMode; set { if (Set(ref _weekdayMode, value)) UpdateWeekdayDisplay(); } }
    public DateTime Month { get => _month; set => Set(ref _month, new(value.Year, value.Month, 1)); }
    public int Year { get => _year; set => Set(ref _year, value); }
    public string MonthLabel => Month.ToString("MMMM yyyy");
    public string MonthlySummary
    {
        get { var days = Daily.Where(d => d.Date.Year == Month.Year && d.Date.Month == Month.Month).ToArray(); var events = _events.Where(e => { var d = _engine.ToLocal(e.Start); return d.Year == Month.Year && d.Month == Month.Month; }).ToArray(); var sessions = _engine.BuildSessions(events, TimeSpan.FromMinutes(SessionGapMinutes)); return $"{Formatters.Duration(days.Sum(d => d.Seconds))} · {days.Length} active days · {events.Select(e => e.BookId).Distinct().Count()} books · {sessions.Count} sessions" + (days.Length == 0 ? "" : $" · best day {days.MaxBy(d => d.Seconds)!.Date:MMM d}"); }
    }
    public string YearlySummary
    {
        get { var days = Daily.Where(d => d.Date.Year == Year).ToArray(); var events = _events.Where(e => _engine.ToLocal(e.Start).Year == Year).ToArray(); var best = days.GroupBy(d => d.Date.Month).Select(g => new { Month = g.Key, Seconds = g.Sum(x => x.Seconds) }).MaxBy(x => x.Seconds); return $"{Formatters.Duration(days.Sum(d => d.Seconds))} · {days.Length} active days · {events.Select(e => e.BookId).Distinct().Count()} books" + (best is null ? "" : $" · best month {new DateTime(Year, best.Month, 1):MMMM}"); }
    }
    public int SessionGapMinutes { get => _settings.SessionGapMinutes; set { _settings.SessionGapMinutes = value; Raise(); Recalculate(); } }
    public string FirstDayOfWeek { get => _settings.WeekStartsMonday ? "Monday" : "Sunday"; set { _settings.WeekStartsMonday = value != "Sunday"; Raise(); Recalculate(); } }
    public bool AutoRefresh { get => _settings.AutoRefresh; set { _settings.AutoRefresh = value; Raise(); ConfigureWatcher(); } }
    public string Theme { get => _settings.Theme; set { _settings.Theme = value; Raise(); ThemeManager.Apply(value); } }
    public double DailyGoalMinutes { get => _settings.DailyGoalMinutes; set { _settings.DailyGoalMinutes = value; Raise(); Raise(nameof(DailyGoalProgress)); } }
    public double WeeklyGoalMinutes { get => _settings.WeeklyGoalMinutes; set { _settings.WeeklyGoalMinutes = value; Raise(); Raise(nameof(WeeklyGoalProgress)); } }
    public double MonthlyGoalHours { get => _settings.MonthlyGoalHours; set { _settings.MonthlyGoalHours = value; Raise(); Raise(nameof(MonthlyGoalProgress)); } }
    public double DailyGoalProgress => Math.Min(100, (Overview?.TodaySeconds ?? 0) / 60 / Math.Max(1, DailyGoalMinutes) * 100);
    public double WeeklyGoalProgress => Math.Min(100, _events.Where(e => _engine.ToLocal(e.Start).Date >= StartOfWeek(DateTime.Today)).Sum(e => e.DurationSeconds) / 60 / Math.Max(1, WeeklyGoalMinutes) * 100);
    public double MonthlyGoalProgress => Math.Min(100, _events.Where(e => { var d = _engine.ToLocal(e.Start); return d.Year == DateTime.Today.Year && d.Month == DateTime.Today.Month; }).Sum(e => e.DurationSeconds) / 3600 / Math.Max(1, MonthlyGoalHours) * 100);

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync();
        _range = _settings.DefaultRangeDays switch { 7 => "7 days", 90 => "90 days", 365 => "1 year", -1 => "All time", _ => "30 days" };
        ThemeManager.Apply(_settings.Theme); RaiseAllSettings();
        var path = await _locator.LocateAsync(_settings.DatabasePath);
        if (path is null) { Status = "No Readest data found"; Error = "Không tìm thấy dữ liệu Readest. Chọn statistics.db để kết nối."; RaiseConnectionState(); return; }
        await ConnectAsync(path);
    }

    private async Task ConnectAsync(string path)
    {
        IsBusy = true; Error = null;
        try
        {
            var repository = new SqliteReadestRepository(path);
            var validation = await repository.ValidateAsync();
            if (!validation.IsValid) { _repository = null; Diagnostics = null; Error = validation.Message; Status = "Incompatible database"; return; }
            _repository = repository; Diagnostics = validation.Diagnostics; _settings.DatabasePath = path; await _store.SaveAsync(_settings);
            await RefreshAsync(); ConfigureWatcher();
        }
        catch (Exception ex) { _repository = null; Error = Friendly(ex); Status = "Connection failed"; }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private async Task RefreshAsync(bool incremental = false)
    {
        if (_repository is null) return;
        IsBusy = true; Error = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var booksTask = _repository.GetBooksAsync(cts.Token);
            var since = incremental && _events.Count > 0 ? DateTimeOffset.FromUnixTimeSeconds(_events.Max(e => e.StartTime)) : (DateTimeOffset?)null;
            var eventsTask = _repository.GetEventsAsync(since, cancellationToken: cts.Token);
            await Task.WhenAll(booksTask, eventsTask); _bookModels = await booksTask;
            var fresh = await eventsTask;
            _events = since is null ? fresh : _events.Concat(fresh).GroupBy(e => (e.BookId, e.Page, e.StartTime)).Select(g => g.Last()).OrderBy(e => e.StartTime).ToArray();
            Diagnostics = (await _repository.ValidateAsync(cts.Token)).Diagnostics;
            Recalculate(); Status = $"Connected to Readest · updated {DateTime.Now:t}";
        }
        catch (Exception ex) { Error = Friendly(ex); Status = "Refresh failed"; }
        finally { IsBusy = false; RaiseConnectionState(); }
    }

    private void Recalculate()
    {
        var days = Range switch { "7 days" => 7, "90 days" => 90, "1 year" => 365, "All time" => Math.Max(1, (int)((DateTimeOffset.Now - (_events.Count == 0 ? DateTimeOffset.Now : _events.Min(e => e.Start))).TotalDays + 1)), _ => 30 };
        Overview = _engine.Overview(_events, _bookModels, days, TimeSpan.FromMinutes(SessionGapMinutes));
        _settings.DefaultRangeDays = Range switch { "7 days" => 7, "90 days" => 90, "1 year" => 365, "All time" => -1, _ => 30 };
        SessionSummary = _engine.SessionStatistics(_events, SessionGapMinutes, _settings.WeekStartsMonday);
        Replace(Daily, _engine.Daily(_events, TimeSpan.FromMinutes(SessionGapMinutes)));
        Replace(Trend, Overview.Daily.Select(d => new ChartPoint(d.Date.ToString(days > 90 ? "MMM d" : "d MMM"), d.Seconds / 60, $"{Formatters.Duration(d.Seconds)} · {d.Books} books · {d.Sessions} sessions")));
        var map = Daily.ToDictionary(d => d.Date); var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-364));
        Replace(Heatmap, Enumerable.Range(0, 365).Select(i => { var date = start.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : $"{Formatters.Duration(d.Seconds)} · {d.Books} books"); }));
        Replace(Hourly, Overview.Hourly); Replace(Weekdays, Overview.Weekdays); UpdateWeekdayDisplay();
        var titleById = _bookModels.ToDictionary(b => b.Id, b => b.Title);
        Replace(Sessions, SessionSummary.Recent.Select(s => new SessionDisplay(_engine.ToLocal(s.Start), _engine.ToLocal(s.End), s.DurationSeconds, s.BookIds.Count == 1 ? titleById.GetValueOrDefault(s.BookIds[0], "Unknown book") : $"{s.BookIds.Count} books", s.EventCount, s)));
        if (SelectedSession is null || !Sessions.Any(s => s.Source == SelectedSession.Source)) SelectedSession = Sessions.FirstOrDefault();
        Replace(Insights, Overview.Insights); Replace(Records, Overview.Records);
        FilterBooks(); RecalculatePeriods();
        Raise(nameof(HasData)); Raise(nameof(NoData)); Raise(nameof(DailyGoalProgress)); Raise(nameof(WeeklyGoalProgress)); Raise(nameof(MonthlyGoalProgress));
    }

    private void RecalculatePeriods()
    {
        Replace(Monthly, _engine.Monthly(Daily, Month.Year, Month.Month)); Replace(Yearly, _engine.Yearly(Daily, Year));
        var map = Daily.ToDictionary(d => d.Date);
        var monthStart = new DateOnly(Month.Year, Month.Month, 1); Replace(MonthlyHeatmap, Enumerable.Range(0, DateTime.DaysInMonth(Month.Year, Month.Month)).Select(i => { var date = monthStart.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : Formatters.Duration(d.Seconds)); }));
        var yearStart = new DateOnly(Year, 1, 1); var yearDays = DateTime.IsLeapYear(Year) ? 366 : 365; Replace(YearlyHeatmap, Enumerable.Range(0, yearDays).Select(i => { var date = yearStart.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : Formatters.Duration(d.Seconds)); }));
        Raise(nameof(MonthLabel)); Raise(nameof(MonthlySummary)); Raise(nameof(YearlySummary));
    }

    private void UpdateWeekdayDisplay()
    {
        if (WeekdayMode == "Total time") { Replace(WeekdaysDisplay, Weekdays); return; }
        var grouped = Overview?.Daily.GroupBy(d => d.Date.DayOfWeek).ToDictionary(g => g.Key, g => g.Average(x => x.Seconds) / 60d) ?? [];
        var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        Replace(WeekdaysDisplay, order.Select(day => new ChartPoint(day.ToString()[..3], grouped.GetValueOrDefault(day), "Average per active occurrence")));
    }

    private void UpdateBookDetail(BookSummary? value)
    {
        BookDetail = value is null ? null : _engine.BookDetails(value, _events, SessionGapMinutes);
        Replace(BookTrend, BookDetail?.Daily.Select(d => new ChartPoint(d.Date.ToString("MMM d"), d.Seconds / 60, Formatters.Duration(d.Seconds))) ?? []);
        if (value is null) { BookHeatmap.Clear(); BookHourly.Clear(); BookWeekdays.Clear(); BookSessions.Clear(); return; }
        var bookEvents = _events.Where(e => e.BookId == value.Id).ToArray(); var map = BookDetail!.Daily.ToDictionary(d => d.Date); var start = DateOnly.FromDateTime(DateTime.Today.AddDays(-364));
        Replace(BookHeatmap, Enumerable.Range(0, 365).Select(i => { var date = start.AddDays(i); var d = map.GetValueOrDefault(date); return new ChartPoint(date.ToString("yyyy-MM-dd"), (d?.Seconds ?? 0) / 60, d is null ? "No activity" : Formatters.Duration(d.Seconds)); }));
        var bookOverview = _engine.Overview(bookEvents, _bookModels.Where(b => b.Id == value.Id).ToArray(), Math.Max(1, (int)((DateTimeOffset.Now - (bookEvents.Length == 0 ? DateTimeOffset.Now : bookEvents.Min(e => e.Start))).TotalDays + 1)), TimeSpan.FromMinutes(SessionGapMinutes));
        Replace(BookHourly, bookOverview.Hourly); Replace(BookWeekdays, bookOverview.Weekdays);
        Replace(BookSessions, BookDetail.SessionHistory.Select(s => new SessionDisplay(_engine.ToLocal(s.Start), _engine.ToLocal(s.End), s.DurationSeconds, value.Title, s.EventCount, s)));
    }
    private void FilterBooks()
    {
        var items = _engine.RankBooks(_events, _bookModels).Where(b => string.IsNullOrWhiteSpace(BookSearch) || b.Title.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase) || b.Authors.Contains(BookSearch, StringComparison.CurrentCultureIgnoreCase));
        items = BookSort switch { "Recently read" => items.OrderByDescending(x => x.LastRead), "Title" => items.OrderBy(x => x.Title), "Reading time ascending" => items.OrderBy(x => x.Seconds), _ => items.OrderByDescending(x => x.Seconds) };
        Replace(Books, items); if (SelectedBook is null || !Books.Any(x => x.Id == SelectedBook.Id)) SelectedBook = Books.FirstOrDefault();
    }

    private async Task ChangeDatabaseAsync() { var path = _chooseDatabase(); if (path is not null) await ConnectAsync(path); }
    private async Task RedetectAsync() { _settings.DatabasePath = null; var path = await _locator.LocateAsync(); if (path is null) { Error = "Không tìm thấy dữ liệu Readest."; RaiseConnectionState(); } else await ConnectAsync(path); }
    private void OpenFolder() { if (Diagnostics is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Diagnostics.Path}\"") { UseShellExecute = true }); }
    private async Task ExportCsvAsync() { var path = _chooseExport("csv", "CSV file|*.csv"); if (path is not null) await _export.ExportCsvAsync(path, Daily); }
    private async Task ExportJsonAsync() { var path = _chooseExport("json", "JSON file|*.json"); if (path is not null && Overview is not null) await _export.ExportJsonAsync(path, Overview, Books); }
    private async Task SaveSettingsAsync() { await _store.SaveAsync(_settings); Status = "Settings saved"; }

    private void ConfigureWatcher()
    {
        _watcher?.Dispose(); _watcher = null;
        if (!AutoRefresh || _repository is null) return;
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_repository.DatabasePath)!) { Filter = "statistics.db*", NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
        _watcher.Changed += WatcherChanged; _watcher.Created += WatcherChanged; _watcher.Renamed += WatcherChanged; _watcher.Deleted += WatcherChanged;
    }

    private void WatcherChanged(object sender, FileSystemEventArgs e)
    {
        _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); _refreshDebounce = new(); var token = _refreshDebounce.Token;
        _ = Application.Current.Dispatcher.InvokeAsync(async () => { try { await Task.Delay(1500, token); await RefreshAsync(incremental: true); } catch (OperationCanceledException) { } });
    }

    private static DateTime StartOfWeek(DateTime date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    private static string Friendly(Exception ex) => ex switch { UnauthorizedAccessException => "Permission denied while reading the database.", IOException => ex.Message, Microsoft.Data.Sqlite.SqliteException => "The database is locked, corrupted, or incompatible.", _ => "Could not read Readest statistics: " + ex.Message };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    private void RaiseConnectionState() { Raise(nameof(NotConnected)); Raise(nameof(NoData)); Raise(nameof(HasData)); OpenFolderCommand.Refresh(); ExportCsvCommand.Refresh(); ExportJsonCommand.Refresh(); }
    private void RaiseAllSettings() { Raise(nameof(SessionGapMinutes)); Raise(nameof(FirstDayOfWeek)); Raise(nameof(Range)); Raise(nameof(AutoRefresh)); Raise(nameof(Theme)); Raise(nameof(DailyGoalMinutes)); Raise(nameof(WeeklyGoalMinutes)); Raise(nameof(MonthlyGoalHours)); }
    public void Dispose() { _watcher?.Dispose(); _refreshDebounce?.Cancel(); _refreshDebounce?.Dispose(); }
}
