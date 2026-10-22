using System.Collections.ObjectModel;
using System.IO;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed record AuditEventRow(ReadingEvent Event, string BookTitle, bool IsExcluded, string? Reason)
{
    public string TimeLabel => Event.Start.ToLocalTime().ToString("MMM d, yyyy · HH:mm");
    public string DurationLabel => Formatters.Duration(Event.DurationSeconds);
    public string StateLabel => IsExcluded ? "Excluded locally" : "Included";
    public string ActionLabel => IsExcluded ? "Restore" : "Exclude";
}

public sealed partial class MainViewModel
{
    private AuditEventRow? _selectedAuditEvent;
    private string _auditReason = "Abnormal event";
    private WeeklyRecap? _weeklyRecap;
    private MetadataAnalytics? _metadataAnalytics;
    private DateTimeOffset? _manualLastUpdated;
    private static string V24Text(string english, string vietnamese) => Localization.Localizer.Instance.IsVietnamese ? vietnamese : english;

    public ObservableCollection<AuditEventRow> AuditEvents { get; } = [];
    public ObservableCollection<AuditEventRow> DayAuditEvents { get; } = [];
    public ObservableCollection<ChartPoint> WeeklyRecapDays { get; } = [];
    public ObservableCollection<MetadataBreakdown> AuthorBreakdown { get; } = [];
    public ObservableCollection<MetadataBreakdown> LanguageBreakdown { get; } = [];
    public AsyncCommand ToggleAuditEventCommand { get; private set; } = null!;
    public AsyncCommand ExportWeeklyRecapCommand { get; private set; } = null!;
    public AuditEventRow? SelectedAuditEvent
    {
        get => _selectedAuditEvent;
        set { if (Set(ref _selectedAuditEvent, value)) { Raise(nameof(AuditPreview)); ToggleAuditEventCommand.Refresh(); } }
    }
    public string AuditReason { get => _auditReason; set => Set(ref _auditReason, value); }
    public string AuditPreview
    {
        get
        {
            if (SelectedAuditEvent is null) return V24Text("Select a Readest event to preview its effect.", "Chọn một sự kiện Readest để xem trước ảnh hưởng.");
            var row = SelectedAuditEvent;
            var current = _events.Sum(item => item.DurationSeconds);
            var exclusions = _settings.ReadingEventExclusions.ToList();
            var key = ReadingEventAudit.Key(row.Event);
            if (row.IsExcluded) exclusions.RemoveAll(item => item.Key == key);
            else exclusions.Add(new ReadingEventExclusion(key, AuditReason, DateTimeOffset.UtcNow));
            var readest = ReadingEventAudit.Apply(_readestEvents, exclusions);
            var projected = SelectedSource switch
            {
                "Manual" => Manual.AsReadingEvents().Sum(item => item.DurationSeconds),
                "Readest" => readest.Sum(item => item.DurationSeconds),
                _ => readest.Sum(item => item.DurationSeconds) +
                    ReadingEventMerger.ExcludeOverlaps(Manual.AsReadingEvents(), readest).Sum(item => item.DurationSeconds)
            };
            return V24Text(
                $"All-time reading ({SelectedSource}): {Formatters.Duration(current)} → {Formatters.Duration(projected)} · {row.BookTitle} · {row.DurationLabel}. Only Statistics changes; Readest stays untouched.",
                $"Tổng thời gian đọc ({SelectedSource}): {Formatters.Duration(current)} → {Formatters.Duration(projected)} · {row.BookTitle} · {row.DurationLabel}. Chỉ Statistics thay đổi; Readest giữ nguyên.");
        }
    }
    public WeeklyRecap? WeeklyRecap { get => _weeklyRecap; private set => Set(ref _weeklyRecap, value); }
    public string WeeklyRecapHeading => WeeklyRecap is null ? V24Text("This week", "Tuần này") : $"{WeeklyRecap.Start:MMM d} – {WeeklyRecap.End:MMM d}";
    public string WeeklyRecapSummary => WeeklyRecap is null ? V24Text("No reading yet.", "Chưa có hoạt động đọc.") :
        V24Text($"{Formatters.Duration(WeeklyRecap.TotalSeconds)} · {WeeklyRecap.ActiveDays}/7 active days · {(WeeklyRecap.DeltaSeconds >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(WeeklyRecap.DeltaSeconds))} vs same days last week",
            $"{Formatters.Duration(WeeklyRecap.TotalSeconds)} · {WeeklyRecap.ActiveDays}/7 ngày đọc · {(WeeklyRecap.DeltaSeconds >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(WeeklyRecap.DeltaSeconds))} so với cùng số ngày tuần trước");
    public string WeeklyRecapDetail => WeeklyRecap is null ? "" : V24Text(
        $"Top book: {WeeklyRecap.TopBook} · Goal {WeeklyRecap.GoalPercent:0}% · Readest {Formatters.Duration(WeeklyRecap.ReadestSeconds)} · manual {Formatters.Duration(WeeklyRecap.ManualSeconds)}",
        $"Sách đọc nhiều nhất: {WeeklyRecap.TopBook} · Mục tiêu {WeeklyRecap.GoalPercent:0}% · Readest {Formatters.Duration(WeeklyRecap.ReadestSeconds)} · đọc sách giấy {Formatters.Duration(WeeklyRecap.ManualSeconds)}");
    public string ConsistencyLabel => V24Text($"{Period?.ConsistencyPercent ?? 0:0}% consistency", $"Đều đặn {Period?.ConsistencyPercent ?? 0:0}%");
    public string AverageSessionLabel => V24Text($"Avg {Formatters.Duration(Period?.AverageSessionSeconds ?? 0)}", $"Trung bình {Formatters.Duration(Period?.AverageSessionSeconds ?? 0)}");
    public MetadataAnalytics? MetadataAnalytics { get => _metadataAnalytics; private set => Set(ref _metadataAnalytics, value); }
    public string AuthorCoverage => MetadataAnalytics is null ? V24Text("0/0 books", "0/0 sách") : V24Text(
        $"{MetadataAnalytics.BooksWithAuthors}/{MetadataAnalytics.ActiveBooks} books · {Formatters.Duration(MetadataAnalytics.SecondsWithAuthors)} of {Formatters.Duration(MetadataAnalytics.TotalSeconds)} attributed",
        $"{MetadataAnalytics.BooksWithAuthors}/{MetadataAnalytics.ActiveBooks} sách · đã phân loại {Formatters.Duration(MetadataAnalytics.SecondsWithAuthors)}/{Formatters.Duration(MetadataAnalytics.TotalSeconds)}");
    public string LanguageCoverage => MetadataAnalytics is null ? V24Text("0/0 books", "0/0 sách") : V24Text(
        $"{MetadataAnalytics.BooksWithLanguages}/{MetadataAnalytics.ActiveBooks} books · {Formatters.Duration(MetadataAnalytics.SecondsWithLanguages)} of {Formatters.Duration(MetadataAnalytics.TotalSeconds)} attributed",
        $"{MetadataAnalytics.BooksWithLanguages}/{MetadataAnalytics.ActiveBooks} sách · đã phân loại {Formatters.Duration(MetadataAnalytics.SecondsWithLanguages)}/{Formatters.Duration(MetadataAnalytics.TotalSeconds)}");
    public string ReadestSourceStatus => IsConnected
        ? V24Text($"Readest · {(HasError ? "last good data" : "connected")} · {(LastSync is null ? "not refreshed" : "updated " + FormatTime(LastSync.Value))} · {_readestEvents.Count} events",
            $"Readest · {(HasError ? "dữ liệu gần nhất" : "đã kết nối")} · {(LastSync is null ? "chưa làm mới" : "cập nhật " + FormatTime(LastSync.Value))} · {_readestEvents.Count} sự kiện")
        : _readestEvents.Count > 0
            ? V24Text("Readest · offline · showing last known digital data", "Readest · ngoại tuyến · đang hiển thị dữ liệu sách điện tử gần nhất")
            : V24Text("Readest · not connected · digital data unavailable", "Readest · chưa kết nối · không có dữ liệu sách điện tử");
    public string ManualSourceStatus
    {
        get
        {
            return V24Text($"Physical · local · {Manual.Books.Count} books · {Manual.Sessions.Count} sessions" +
                (_manualLastUpdated is null ? "" : $" · updated {FormatTime(_manualLastUpdated.Value)}"),
                $"Sách giấy · trên máy · {Manual.Books.Count} sách · {Manual.Sessions.Count} phiên đọc" +
                (_manualLastUpdated is null ? "" : $" · cập nhật {FormatTime(_manualLastUpdated.Value)}"));
        }
    }

    private void MarkManualUpdated()
    {
        _manualLastUpdated = File.Exists(Manual.DatabasePath) ? new DateTimeOffset(File.GetLastWriteTime(Manual.DatabasePath)) : DateTimeOffset.Now;
        Raise(nameof(ManualSourceStatus));
    }

    private void RaiseV24SourceStatus()
    {
        Raise(nameof(ReadestSourceStatus)); Raise(nameof(ManualSourceStatus));
    }

    private void BuildV24()
    {
        BuildMonthlyReview(); RaiseV25Status();
        WeeklyRecap = WeeklyRecapEngine.Build(_events, _bookModels, DateOnly.FromDateTime(DateTime.Today),
            _settings.WeekStartsMonday, _settings.WeeklyGoalMinutes);
        Replace(WeeklyRecapDays, WeeklyRecap.Days);
        Raise(nameof(WeeklyRecapHeading)); Raise(nameof(WeeklyRecapSummary)); Raise(nameof(WeeklyRecapDetail));
        Raise(nameof(ConsistencyLabel)); Raise(nameof(AverageSessionLabel));
        MetadataAnalytics = MetadataAnalyticsEngine.Build(_periodEvents, _bookModels);
        Replace(AuthorBreakdown, MetadataAnalytics.Authors);
        Replace(LanguageBreakdown, MetadataAnalytics.Languages);
        Raise(nameof(AuthorCoverage)); Raise(nameof(LanguageCoverage));
        BuildAuditEvents(); RaiseV24SourceStatus();
    }

    private void BuildAuditEvents()
    {
        var selectedKey = SelectedAuditEvent is null ? null : ReadingEventAudit.Key(SelectedAuditEvent.Event);
        var titles = _readestBookModels.ToDictionary(item => item.Id, item => item.Title);
        var exclusions = _settings.ReadingEventExclusions.ToDictionary(item => item.Key, item => item.Reason, StringComparer.Ordinal);
        Replace(AuditEvents, _readestEvents.OrderByDescending(item => item.StartTime)
            .Where((item, index) => index < 250 || exclusions.ContainsKey(ReadingEventAudit.Key(item)))
            .Select(item => new AuditEventRow(item, titles.GetValueOrDefault(item.BookId, "Unknown book"),
                exclusions.ContainsKey(ReadingEventAudit.Key(item)), exclusions.GetValueOrDefault(ReadingEventAudit.Key(item)))));
        if (selectedKey is not null)
            SelectedAuditEvent = AuditEvents.FirstOrDefault(item => ReadingEventAudit.Key(item.Event) == selectedKey);
        Raise(nameof(AuditPreview));
    }

    private void BuildDayAudit()
    {
        if (SelectedSource == "Manual") { DayAuditEvents.Clear(); return; }
        var selectedKey = SelectedAuditEvent is null ? null : ReadingEventAudit.Key(SelectedAuditEvent.Event);
        var titles = _readestBookModels.ToDictionary(item => item.Id, item => item.Title);
        var exclusions = _settings.ReadingEventExclusions.ToDictionary(item => item.Key, item => item.Reason, StringComparer.Ordinal);
        Replace(DayAuditEvents, _readestEvents.Where(item => DateOnly.FromDateTime(_statistics.ToLocal(item.Start).DateTime) == SelectedDate)
            .OrderBy(item => item.StartTime).Select(item => new AuditEventRow(item,
                titles.GetValueOrDefault(item.BookId, "Unknown book"), exclusions.ContainsKey(ReadingEventAudit.Key(item)),
                exclusions.GetValueOrDefault(ReadingEventAudit.Key(item)))));
        if (selectedKey is not null && DayAuditEvents.FirstOrDefault(item => ReadingEventAudit.Key(item.Event) == selectedKey) is { } match)
            SelectedAuditEvent = match;
    }

    private async Task ToggleAuditEventAsync()
    {
        if (SelectedAuditEvent is not { } row) return;
        var key = ReadingEventAudit.Key(row.Event);
        var old = _settings.ReadingEventExclusions.FirstOrDefault(item => item.Key == key);
        if (old is null)
            _settings.ReadingEventExclusions.Add(new ReadingEventExclusion(key, string.IsNullOrWhiteSpace(AuditReason) ? "Abnormal event" : AuditReason.Trim(), DateTimeOffset.UtcNow));
        else _settings.ReadingEventExclusions.Remove(old);
        await _store.SaveAsync(_settings);
        ApplySourceSelection();
        BuildAuditEvents(); BuildDayAudit();
        Status = old is null ? "Readest event excluded locally; restore is available" : "Readest event restored";
    }

    private async Task ExportWeeklyRecapAsync()
    {
        if (WeeklyRecap is null) return;
        var path = _chooseExport("md", "Markdown report|*.md");
        if (path is null) return;
        await File.WriteAllTextAsync(path, WeeklyRecapEngine.ToMarkdown(WeeklyRecap));
        Status = "Weekly recap exported";
    }
}
