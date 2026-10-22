using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class MainViewModel
{
    private DateTimeOffset? _lastRefreshFailure;
    private DateTimeOffset? _lastBackupFailure;
    private bool _watcherFallback;
    private bool _settingsLoaded;
    private bool _isRestoring, _restoreReloading, _isClosing;
    private readonly HashSet<Task> _settingsWrites = [];
    public bool IsRestoring => _isRestoring;
    public bool CanInteract => !_isRestoring && !_isClosing;
    public void SetClosingInteraction(bool closing) { _isClosing = closing; Raise(nameof(CanInteract)); RefreshMonthlyNavigation(); }

    private bool HasRunningCommand() => GetType().GetProperties()
        .Where(property => property.PropertyType == typeof(AsyncCommand)
            && property.Name is not nameof(RestoreBackupCommand) and not nameof(RestoreSelectedBackupCommand))
        .Any(property => ((AsyncCommand?)property.GetValue(this))?.IsRunning == true)
        || Manual.GetType().GetProperties().Where(property => property.PropertyType == typeof(AsyncCommand))
            .Any(property => ((AsyncCommand?)property.GetValue(Manual))?.IsRunning == true);

    private Task SaveSettingsDataAsync(CancellationToken cancellationToken = default)
    {
        // Queued pre-restore continuations must never save an old settings object
        // after replacement. Restore persists its newly loaded state explicitly.
        if (_isRestoring || _disposed) return Task.CompletedTask;
        _settingsWrites.RemoveWhere(task => task.IsCompletedSuccessfully || task.IsCanceled);
        var task = _store.SaveAsync(_settings, cancellationToken);
        _settingsWrites.Add(task);
        return task;
    }

    private async Task DrainSettingsWritesAsync()
    {
        try { await Task.WhenAll(_settingsWrites.ToArray()); }
        finally
        {
            // A failure is reported to this caller once. Keeping an observed
            // failed task forever would prevent every future close/restore retry.
            _settingsWrites.RemoveWhere(task => task.IsCompleted);
        }
    }
    public string StorageHealthHeading => V24Text("STORAGE & REFRESH", "LƯU DỮ LIỆU & LÀM MỚI");
    public string RetryRefreshLabel => V24Text("Retry Readest refresh", "Thử làm mới Readest");
    public string RetryMirrorLabel => V24Text("Retry recovery mirror", "Thử ghi bản phục hồi");
    public string CreateBackupLabel => V24Text("Create backup now", "Sao lưu ngay");
    public string MonthlyDateLabel => V24Text("Date", "Ngày");
    public string MonthlyTimeLabel => V24Text("Reading time", "Thời gian đọc");
    public string MonthlyBooksLabel => V24Text("Books", "Sách");
    public string MonthlySessionsLabel => V24Text("Sessions", "Phiên đọc");
    public string ExportJournalLabel => V24Text("Export physical journal", "Xuất nhật ký sách giấy");
    private DateTime? _reportMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private MonthlyReview? _monthlyReview;
    private RelayCommand? _previousReviewMonthCommand, _nextReviewMonthCommand, _currentReviewMonthCommand;
    private ParameterCommand<DailyStat>? _openMonthlyDayCommand;
    public ObservableCollection<ChartPoint> MonthlyReviewDays { get; } = [];
    public ObservableCollection<DailyStat> MonthlyReviewDaily { get; } = [];
    public DateTime LatestReportDate => DateTime.Today;
    public DateTime EarliestReportDate => new(2, 1, 1);
    private static DateTime CurrentReportMonth => new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public string MonthlyPreviousLabel => V24Text("Previous month", "Tháng trước");
    public string MonthlyNextLabel => V24Text("Next month", "Tháng sau");
    public string MonthlyCurrentLabel => V24Text("Current month", "Tháng hiện tại");
    public string MonthlyOpenDayLabel => V24Text("Open day", "Xem ngày");
    public string MonthlyDayHint => V24Text("Open a row to see its reading timeline and books in Activity. Your source and top date filters stay unchanged.", "Mở một ngày để xem dòng thời gian đọc và sách ở tab Hoạt động. Giữ nguyên bộ lọc nguồn và khoảng ngày ở trên.");
    public RelayCommand PreviousReviewMonthCommand => _previousReviewMonthCommand ??= new(
        () => { if (CanInteract && (ReportMonth ?? CurrentReportMonth) > EarliestReportDate) ReportMonth = (ReportMonth ?? CurrentReportMonth).AddMonths(-1); },
        () => CanInteract && (ReportMonth ?? CurrentReportMonth) > EarliestReportDate);
    public RelayCommand NextReviewMonthCommand => _nextReviewMonthCommand ??= new(
        () => { if (CanInteract && (ReportMonth ?? CurrentReportMonth) < CurrentReportMonth) ReportMonth = (ReportMonth ?? CurrentReportMonth).AddMonths(1); },
        () => CanInteract && (ReportMonth ?? CurrentReportMonth) < CurrentReportMonth);
    public RelayCommand CurrentReviewMonthCommand => _currentReviewMonthCommand ??= new(
        () => { if (CanInteract) ReportMonth = CurrentReportMonth; },
        () => CanInteract && (ReportMonth ?? CurrentReportMonth) != CurrentReportMonth);
    public ParameterCommand<DailyStat> OpenMonthlyDayCommand => _openMonthlyDayCommand ??= new(day =>
    {
        if (!CanInteract || day is null || !MonthlyReviewDaily.Contains(day)) return;
        Month = new(day.Date.Year, day.Date.Month, 1);
        SelectedDate = day.Date;
        BuildActivity();
        SelectedPage = "Activity";
    }, day => CanInteract && day is not null && MonthlyReviewDaily.Contains(day));
    public DateTime? ReportMonth
    {
        get => _reportMonth;
        set
        {
            var date = value ?? DateTime.Today;
            if (date.Year < 2 || date > DateTime.Today) date = DateTime.Today;
            var month = new DateTime(date.Year, date.Month, 1);
            if (Set(ref _reportMonth, month)) BuildMonthlyReview();
        }
    }
    public MonthlyReview? MonthlyReview { get => _monthlyReview; private set => Set(ref _monthlyReview, value); }
    public string MonthlyReviewEyebrow => V24Text("MONTHLY REVIEW", "TỔNG KẾT THÁNG");
    public string MonthlyReviewHeading => ReportMonth?.ToString("MMMM yyyy") ?? "";
    public string MonthlyReviewPickerHint => V24Text("Choose a date in the month", "Chọn một ngày trong tháng");
    public string MonthlyReviewExportHint => V24Text("Exports the month selected in Statistics → Monthly review, using the current source filter.", "Xuất tháng đã chọn trong Thống kê → Tổng kết tháng, theo bộ lọc nguồn hiện tại.");
    public string MonthlyReviewExportLabel => V24Text("Export this month", "Xuất tháng này");
    public string MonthlyReviewExactLabel => V24Text("Exact daily values", "Số liệu từng ngày");
    public string MonthlyReviewSummary => MonthlyReview is null ? "" : V24Text(
        $"{Formatters.Duration(MonthlyReview.TotalSeconds)} · {MonthlyReview.ActiveDays}/{MonthlyReview.CoveredDays} elapsed days active · {MonthlyReview.Sessions} sessions · {MonthlyReview.ActiveBooks} books",
        $"{Formatters.Duration(MonthlyReview.TotalSeconds)} · {MonthlyReview.ActiveDays}/{MonthlyReview.CoveredDays} ngày đã trôi qua có đọc · {MonthlyReview.Sessions} phiên · {MonthlyReview.ActiveBooks} sách");
    public string MonthlyReviewComparison => MonthlyReview is null ? "" :
        V24Text(MonthlyReview.ComparisonLabel, MonthlyReview.IsComplete ? "Toàn bộ tháng trước" : "Cùng thời gian đã trôi qua trong tháng trước") +
        $" · {Formatters.Duration(MonthlyReview.PreviousSeconds)} · " + (MonthlyReview.HasBaseline
            ? $"{(MonthlyReview.DeltaSeconds >= 0 ? "+" : "−")}{Formatters.Duration(Math.Abs(MonthlyReview.DeltaSeconds))} ({MonthlyReview.PercentChange:+0.0;-0.0;0.0}%)"
            : V24Text("No previous-month baseline", "Không có dữ liệu nền tháng trước"));
    public string MonthlyReviewContext => MonthlyReview is null ? "" : V24Text(
        $"{SelectedSource} · {MonthlyReview.TimeZoneId} · independent of the top date filter. Local exclusions and overlap rules apply. Current month stops at now; future days are omitted. Chart: minutes per day, zero baseline.",
        $"{TranslateSource(SelectedSource)} · {MonthlyReview.TimeZoneId} · không phụ thuộc bộ lọc ngày ở trên. Áp dụng loại trừ cục bộ và quy tắc trùng phiên. Tháng hiện tại tính đến bây giờ; bỏ các ngày tương lai. Biểu đồ: phút/ngày, gốc 0.");
    public string MonthlyReviewCoverage => MonthlyReview?.SourceCoverage?.ToText(IsVietnamese) ?? "";
    private string TranslateSource(string value) => V24Text(value, value switch { "All sources" => "Mọi nguồn", "Manual" => "Sách giấy", _ => value });

    private MonthlySourceCoverage BuildMonthlyCoverage() => MonthlySourceCoverage.Create(SelectedSource,
        IsConnected, HasError || _lastRefreshFailure is not null, _readestEvents.Count > 0 || LastSync is not null, LastSync);

    private void RefreshMonthlyNavigation()
    {
        _previousReviewMonthCommand?.Refresh(); _nextReviewMonthCommand?.Refresh();
        _currentReviewMonthCommand?.Refresh(); _openMonthlyDayCommand?.Refresh();
    }

    private void BuildMonthlyReview()
    {
        var month = ReportMonth ?? DateTime.Today;
        MonthlyReview = MonthlyReviewEngine.Build(_events, _bookModels, month.Year, month.Month, SessionGapMinutes, SelectedSource)
            with { SourceCoverage = BuildMonthlyCoverage() };
        Replace(MonthlyReviewDays, MonthlyReview.Days.Take(MonthlyReview.CoveredDays));
        Replace(MonthlyReviewDaily, MonthlyReview.Daily.Take(MonthlyReview.CoveredDays));
        foreach (var property in new[] { nameof(MonthlyReviewHeading), nameof(MonthlyReviewSummary), nameof(MonthlyReviewComparison), nameof(MonthlyReviewContext), nameof(MonthlyReviewCoverage), nameof(MonthlyReviewEyebrow), nameof(MonthlyReviewPickerHint), nameof(MonthlyReviewExportHint), nameof(MonthlyReviewExportLabel), nameof(MonthlyReviewExactLabel), nameof(MonthlyPreviousLabel), nameof(MonthlyNextLabel), nameof(MonthlyCurrentLabel), nameof(MonthlyOpenDayLabel), nameof(MonthlyDayHint) }) Raise(property);
        RefreshMonthlyNavigation();
    }

    public string RefreshHealthLabel
    {
        get
        {
            var mode = _watcherFallback ? V24Text("Checking every minute (file monitoring unavailable)", "Kiểm tra mỗi phút (không theo dõi được tệp)") :
                _repository is null ? V24Text("Readest not connected", "Chưa kết nối Readest") : V24Text(RefreshMode, RefreshMode switch { "Manual" => "Thủ công", "On Readest changes" => "Khi Readest thay đổi", "30 sec" => "30 giây", "1 min" => "1 phút", "5 min" => "5 phút", _ => RefreshMode });
            var success = LastSync is null ? V24Text("No successful refresh yet", "Chưa làm mới thành công") : V24Text($"Last success {LastSync:g}", $"Thành công gần nhất {LastSync:g}");
            var failure = _lastRefreshFailure is null ? "" : V24Text($" · failed attempt {_lastRefreshFailure:g}; last good data retained", $" · lần thử lỗi {_lastRefreshFailure:g}; vẫn giữ dữ liệu gần nhất");
            return (IsBusy ? V24Text("Refreshing… · ", "Đang làm mới… · ") : "") + mode + " · " + success + failure;
        }
    }

    public string BackupHealthLabel
    {
        get
        {
            var latest = BackupHistory.Where(item => item.Status == "Verified").OrderByDescending(item => item.CreatedAt).FirstOrDefault();
            var state = AutomaticBackups ? V24Text("Automatic backups on", "Bật sao lưu tự động") : V24Text("Automatic backups off", "Tắt sao lưu tự động");
            var saved = latest is null ? V24Text("No verified automatic backup", "Chưa có bản sao lưu tự động đã kiểm tra") : V24Text($"Latest verified backup {latest.CreatedAt.ToLocalTime():g}", $"Bản sao lưu đã kiểm tra gần nhất {latest.CreatedAt.ToLocalTime():g}");
            return state + " · " + saved + (_lastBackupFailure is null ? "" : V24Text($" · last attempt failed {_lastBackupFailure:g}; retry backup", $" · lần thử lỗi {_lastBackupFailure:g}; hãy thử sao lưu lại"));
        }
    }

    private void RecordRefreshFailure() { _lastRefreshFailure = DateTimeOffset.Now; RaiseV25Status(); }
    private void RecordRefreshSuccess() { _lastRefreshFailure = null; RaiseV25Status(); }
    private void RecordBackupAttempt(bool success) { _lastBackupFailure = success ? null : DateTimeOffset.Now; RaiseV25Status(); }
    private void RaiseV25Status()
    {
        if (MonthlyReview is { } review) MonthlyReview = review with { SourceCoverage = BuildMonthlyCoverage() };
        foreach (var property in new[] { nameof(RefreshHealthLabel), nameof(BackupHealthLabel), nameof(StorageHealthHeading), nameof(RetryRefreshLabel), nameof(RetryMirrorLabel), nameof(CreateBackupLabel), nameof(MonthlyDateLabel), nameof(MonthlyTimeLabel), nameof(MonthlyBooksLabel), nameof(MonthlySessionsLabel), nameof(MonthlyReviewCoverage), nameof(MonthlyPreviousLabel), nameof(MonthlyNextLabel), nameof(MonthlyCurrentLabel), nameof(MonthlyOpenDayLabel), nameof(MonthlyDayHint), nameof(ExportJournalLabel) }) Raise(property);
        RefreshMonthlyNavigation();
    }

    public async Task FlushPreferencesAsync()
    {
        if (!_settingsLoaded || _isRestoring) return;
        _preferenceSaveDebounce?.Cancel(); _noteSaveDebounce?.Cancel();
        await DrainSettingsWritesAsync();
        await Manual.FlushDraftAsync();
        await SaveSettingsDataAsync();
    }

    private Views.ExportOptions? ChooseExportOptions(bool physical, int filteredCount, int totalCount)
    {
        var options = new Views.ExportOptions(physical, filteredCount, totalCount, selection => physical
            ? ExportScopeCounter.Physical(Manual.AllBooks,
                selection.AllItems || selection.ArchiveMode ? Manual.Sessions : Manual.FilteredJournalSessions,
                selection.AllItems || selection.ArchiveMode)
            : ExportScopeCounter.Notes((selection.AllItems ? _allNoteRows.AsEnumerable() : Notes.AsEnumerable())
                .Select(note => new NoteExportScopeItem(note.IsManual, !string.IsNullOrWhiteSpace(note.Text))), selection.IncludePrivate));
        var dialog = new Views.ExportOptionsWindow(options);
        if (Application.Current?.MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
        // Refresh can finish while a modal is open. Keep the displayed totals
        // aligned with that new snapshot, and always detach when the dialog closes.
        void OnCountsChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(NotesCountLabel) or nameof(TotalNotes) or nameof(ManualSourceStatus)) options.RefreshCounts();
        }
        void OnManualCountsChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ManualLogViewModel.IsCorrectingSession))
                options.UnavailableReason = Manual.IsCorrectingSession ? V24Text("Wait for the session correction to finish before exporting.", "Chờ chỉnh sửa phiên đọc hoàn tất trước khi xuất.") : "";
            if (args.PropertyName is nameof(ManualLogViewModel.FilteredJournalSessions) or nameof(ManualLogViewModel.IsCorrectingSession)) options.RefreshCounts();
        }
        PropertyChanged += OnCountsChanged;
        if (physical) Manual.PropertyChanged += OnManualCountsChanged;
        try { return dialog.ShowDialog() == true ? options : null; }
        finally
        {
            PropertyChanged -= OnCountsChanged;
            if (physical) Manual.PropertyChanged -= OnManualCountsChanged;
        }
    }

    private async Task<bool> PreviewRestoreAsync(string path)
    {
        BackupRestorePreview preview;
        IsBusy = true; Status = V24Text("Checking backup contents…", "Đang kiểm tra nội dung sao lưu…");
        try { preview = await Task.Run(() => _backup.Preview(path)); }
        finally { IsBusy = false; }
        if (Manual.HasActiveSession || HasRunningCommand()) { Status = V24Text("Finish the active session and wait for other operations before restoring.", "Kết thúc phiên đang đọc và chờ các thao tác khác xong trước khi khôi phục."); return false; }
        var dialog = new Views.BackupPreviewWindow(preview);
        if (Application.Current?.MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    private async Task ExportPhysicalLibraryV25Async()
    {
        if (!CanInteract) return;
        if (Manual.IsCorrectingSession)
        {
            Status = V24Text("Wait for the current physical-library operation to finish, then retry export.", "Chờ thao tác thư viện sách giấy hiện tại hoàn tất, rồi thử xuất lại.");
            return;
        }
        var options = ChooseExportOptions(true, Manual.FilteredJournalSessions.Count, Manual.Sessions.Count);
        if (options is null) return;

        // The modal counts the live journal, which may roll over or receive a
        // completed save while it is open. Capture that exact chosen scope now,
        // before the file picker can dispatch any more background UI updates.
        ManualReadingData snapshot;
        try { snapshot = Manual.CapturePortableLibrary(); }
        catch (InvalidOperationException)
        {
            Status = V24Text("Wait for the current physical-library operation to finish, then retry export.", "Chờ thao tác thư viện sách giấy hiện tại hoàn tất, rồi thử xuất lại.");
            return;
        }
        if (!options.ArchiveMode && !options.AllItems)
        {
            var sessionIds = Manual.FilteredJournalSessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
            snapshot.Sessions = snapshot.Sessions.Where(session => sessionIds.Contains(session.Id)).ToList();
            var bookIds = snapshot.Sessions.Select(session => session.BookId).ToHashSet();
            snapshot.Books = snapshot.Books.Where(book => bookIds.Contains(book.Id)).ToList();
        }
        if (snapshot.Books.Count == 0 && snapshot.Sessions.Count == 0)
        {
            Status = V24Text("Nothing to export in this scope. Choose another scope or add a book/session first.", "Phạm vi này không có dữ liệu để xuất. Đổi phạm vi hoặc thêm sách/phiên đọc trước.");
            return;
        }
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string content;
        if (options.ArchiveMode)
        {
            // Preserve the portable archive format, including its schema,
            // private writing and local metadata, but not the active draft.
            content = JsonSerializer.Serialize(snapshot, jsonOptions);
        }
        else
        {
            var shared = ShareExportPolicy.ShareLibrary(snapshot, options.IncludePrivate);
            content = JsonSerializer.Serialize(new
            {
                shared.SchemaVersion, shared.Books, shared.Sessions,
                Export = new { Kind = "Share summary", Scope = options.AllItems ? "Entire physical library" : "Current physical journal filters", IncludesPrivateWriting = options.IncludePrivate, ExportedAtUtc = DateTimeOffset.UtcNow, TimeZone = TimeZoneInfo.Local.Id }
            }, jsonOptions);
        }
        var path = _chooseExport("json", "Readest Stats physical library|*.json");
        if (path is null) return;
        await File.WriteAllTextAsync(path, content);
        Status = V24Text("Physical library exported with the selected privacy options", "Đã xuất thư viện sách giấy theo tùy chọn riêng tư");
    }
}
