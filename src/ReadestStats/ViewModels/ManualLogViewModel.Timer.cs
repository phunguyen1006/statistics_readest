using System.Windows.Threading;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class ManualLogViewModel
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _draftCheckpointTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool _isFinishing;
    private string _finishStartPage = "";
    private string _finishEndPage = "";
    private string _finishError = "";
    private string _finishNote = "";
    private int _ticksSinceSave;
    private DateTimeOffset _lastTimerHeartbeatUtc;
    private string _targetMinutes = "";
    private string _targetError = "";
    private bool _restoringFinishDraft;
    private string _draftCheckpointError = "";
    private bool _checkpointDeferredForCorrection;
    private bool _finishDraftDeferredForCorrection;
    private DateTimeOffset? _deferredTimerPauseAtUtc;

    public bool HasActiveSession => _data.ActiveSession is not null;
    public bool CanSelectBook => !HasActiveSession;
    public bool IsSessionPaused => _data.ActiveSession?.IsPaused == true;
    public string PauseResumeLabel => IsSessionPaused ? "Resume" : "Pause";
    public ManualBook? ActiveBook => _data.ActiveSession is null ? null : FindBook(_data.ActiveSession.BookId);
    public string ActiveBookTitle => ActiveBook?.Title ?? Localization.Localizer.Instance.Translate("No active session");
    public string ActiveBookAuthors => ActiveBook?.Authors ?? Localization.Localizer.Instance.Translate("Choose a physical book to begin.");
    public string ActiveBookCover => ActiveBook?.CoverUrl ?? "";
    public string ActiveSessionState => !HasActiveSession ? "READY" : IsSessionPaused ? "PAUSED" : "READING NOW";
    public string ElapsedLabel => FormatTimer(_data.ActiveSession?.ElapsedSeconds(DateTimeOffset.UtcNow) ?? 0);

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
        UndoRecentActionCommand.Refresh();
        ArchiveSelectedBookCommand.Refresh();
        MergeDuplicateBooksCommand.Refresh();
        RestoreArchivedBookCommand.Refresh();
    }

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
}
