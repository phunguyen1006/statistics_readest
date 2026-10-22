using System.Runtime.InteropServices;
using System.Windows;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isNoteReaderOpen;
    private NoteCurationUndoSnapshot? _noteCurationUndo;
    private string _noteCurationUndoBookTitle = "";
    private RelayCommand? _openNoteReaderCommand, _openDailyNoteReaderCommand, _openRandomNoteReaderCommand;
    private RelayCommand? _toggleReaderFavoriteCommand, _copyReaderQuoteCommand, _copyReaderPrivateWritingCommand;
    private AsyncCommand? _undoNoteCurationCommand, _clearSelectedNoteSnoozeCommand;

    public bool IsNoteReaderOpen { get => _isNoteReaderOpen; private set => Set(ref _isNoteReaderOpen, value); }
    // The reader can legitimately show a random note outside the filtered list.
    // Project an explicit null visual selection instead of handing WPF an invalid item.
    public NoteRow? NoteListSelection => SelectedNote is { } note && Notes.Contains(note) ? note : null;
    public bool HasNoteCurationUndo => _noteCurationUndo is not null;
    public bool ReaderHasSourceContent => !string.IsNullOrWhiteSpace(SelectedNote?.PreviewContent);
    public bool ReaderHasPrivateWriting => SelectedNote?.HasComment == true || SelectedNote?.HasPersonalNote == true;
    public bool ReaderContentIsPrivate => SelectedNote is not null && !SelectedNote.HasHighlight;
    public string ReaderContentLabel => ReaderContentIsPrivate
        ? V24Text("PRIVATE SOURCE / SESSION NOTE", "GHI CHÚ NGUỒN / PHIÊN ĐỌC RIÊNG TƯ")
        : V24Text("HIGHLIGHT", "ĐOẠN TRÍCH");
    public string NoteCurationUndoLabel => V24Text(
        $"Last rediscovery change: {_noteCurationUndoBookTitle}",
        $"Thay đổi Random gần nhất: {_noteCurationUndoBookTitle}");
    public string NoteReaderTitle => V24Text("Focused note", "Đọc ghi chú");
    public string OpenNoteReaderLabel => V24Text("Read in focus", "Đọc tập trung");
    public string ReaderPreviousLabel => V24Text("Previous", "Trước");
    public string ReaderNextLabel => V24Text("Next random note", "Note ngẫu nhiên tiếp");
    public string ReaderCloseLabel => V24Text("Close", "Đóng");
    public string ReaderPrivateCommentLabel => V24Text("PRIVATE SOURCE ANNOTATION", "CHÚ THÍCH NGUỒN RIÊNG TƯ");
    public string ReaderPersonalWritingLabel => V24Text("YOUR PRIVATE WRITING", "NỘI DUNG RIÊNG CỦA BẠN");
    public string ReaderEmptyContentLabel => V24Text("This note has no source text.", "Ghi chú này không có nội dung nguồn.");
    public string ReaderPrivacyLabel => V24Text(
        "Highlight + citation never includes private writing. Copy private writing only with its separate button.",
        "Sao chép trích dẫn không kèm nội dung riêng. Chỉ nút sao chép riêng mới chứa ghi chú riêng tư.");
    public string ReaderShortcutsLabel => V24Text(
        "Alt + ← / → previous / next · Ctrl + Shift + H heart · Ctrl + Shift + C citation · Esc close",
        "Alt + ← / → trước / tiếp · Ctrl + Shift + H yêu thích · Ctrl + Shift + C trích dẫn · Esc đóng");
    public string NotesReaderShortcutsLabel => V24Text(
        "Ctrl + Shift + F read in focus · Alt + ← / → previous / random · Ctrl + Alt + Z undo rediscovery change",
        "Ctrl + Shift + F đọc tập trung · Alt + ← / → trước / ngẫu nhiên · Ctrl + Alt + Z hoàn tác thay đổi Random");
    public string CopyReaderQuoteLabel => SelectedNote?.HasHighlight == true
        ? V24Text("Copy highlight + citation", "Sao chép đoạn trích + nguồn")
        : V24Text("Copy citation only", "Chỉ sao chép nguồn sách");
    public string CopyReaderPrivateWritingLabel => V24Text("Copy private writing", "Sao chép nội dung riêng");
    public string CopyReaderPrivateWritingHint => V24Text(
        "Copies source annotations, physical-session notes and personal writing to the clipboard. Keep them private.",
        "Sao chép chú thích nguồn, ghi chú phiên đọc sách giấy và nội dung riêng vào bộ nhớ tạm. Hãy giữ riêng tư.");
    public string ClearNoteSnoozeLabel => V24Text("End snooze", "Bỏ tạm ẩn");
    public string UndoNoteCurationLabel => V24Text("Undo", "Hoàn tác");

    public RelayCommand OpenNoteReaderCommand => _openNoteReaderCommand ??= new(
        () => OpenFocusedNote(SelectedNote), () => CanInteract && SelectedNote is not null && !IsNoteReaderOpen);
    public RelayCommand OpenDailyNoteReaderCommand => _openDailyNoteReaderCommand ??= new(
        () => OpenFocusedNote(DailyNote), () => CanInteract && DailyNote is not null && !IsNoteReaderOpen);
    public RelayCommand OpenRandomNoteReaderCommand => _openRandomNoteReaderCommand ??= new(
        () => OpenFocusedNote(RandomNote), () => CanInteract && RandomNote is not null && !IsNoteReaderOpen);
    public RelayCommand ToggleReaderFavoriteCommand => _toggleReaderFavoriteCommand ??= new(
        () => { if (CanInteract) ToggleFavoriteNote(SelectedNote); }, () => CanInteract && SelectedNote is not null);
    public RelayCommand CopyReaderQuoteCommand => _copyReaderQuoteCommand ??= new(CopyReaderQuote, () => SelectedNote is not null);
    public RelayCommand CopyReaderPrivateWritingCommand => _copyReaderPrivateWritingCommand ??= new(
        CopyReaderPrivateWriting, () => ReaderHasPrivateWriting);
    public AsyncCommand UndoNoteCurationCommand => _undoNoteCurationCommand ??= new(UndoNoteCurationAsync, () => CanInteract && !_noteCurationBusy && HasNoteCurationUndo);
    public AsyncCommand ClearSelectedNoteSnoozeCommand => _clearSelectedNoteSnoozeCommand ??= new(
        ClearSelectedNoteSnoozeAsync, () => CanInteract && !_noteCurationBusy && SelectedNote?.State.SnoozedUntilUtc is not null);

    private void OpenFocusedNote(NoteRow? note)
    {
        if (note is null || IsNoteReaderOpen || _disposed || !CanInteract) return;
        SelectedNote = note;
        IsNoteReaderOpen = true;
        RefreshReaderCommands();
        try
        {
            var dialog = new Views.NoteReaderWindow(this);
            if (Application.Current?.MainWindow is { IsVisible: true } owner) dialog.Owner = owner;
            dialog.ShowDialog();
        }
        finally
        {
            IsNoteReaderOpen = false;
            ApplyNoteFilters();
            RefreshReaderCommands();
        }
    }

    // Root curation handlers must capture their local note before changing it or awaiting a save.
    private void RememberNoteCurationChange(NoteRow note)
    {
        _noteCurationUndo = NoteCurationUndoSnapshot.Capture(note.Id, note.State);
        _noteCurationUndoBookTitle = note.BookTitle;
        RefreshReaderCommands();
    }

    // A restore/reset establishes a new settings baseline; old Undo must not overwrite it.
    private void ClearNoteCurationUndo()
    {
        _noteCurationUndo = null;
        _noteCurationUndoBookTitle = "";
        RefreshReaderCommands();
    }

    private async Task ClearSelectedNoteSnoozeAsync()
    {
        var note = SelectedNote;
        if (!CanInteract || _noteCurationBusy || note?.State.SnoozedUntilUtc is null) return;
        RememberNoteCurationChange(note);
        note.ClearSnooze();
        SetReaderCurationBusy(true);
        try
        {
            if (!await SaveNoteStateAsync(note)) return;
            ReconcileReaderCuration();
            NotesStatus = V24Text("Snooze ended; this note may appear randomly again.", "Đã bỏ tạm ẩn; ghi chú có thể xuất hiện ngẫu nhiên trở lại.");
        }
        catch (Exception ex)
        {
            NotesStatus = V24Text("Could not save note curation. ", "Không thể lưu thiết lập ghi chú. ") + Friendly(ex);
            _log("Note snooze save error: " + ex);
        }
        finally { SetReaderCurationBusy(false); }
    }

    private async Task UndoNoteCurationAsync()
    {
        var undo = _noteCurationUndo;
        if (undo is null || !CanInteract || _noteCurationBusy) return;
        // Never restore whichever note became selected while the preceding save was in progress.
        var note = _allNoteRows.FirstOrDefault(row => row.Id.Equals(undo.NoteId, StringComparison.OrdinalIgnoreCase));
        var state = note?.State ?? _settings.NoteStates.FirstOrDefault(pair => pair.Key.Equals(undo.NoteId, StringComparison.OrdinalIgnoreCase)).Value;
        if (state is null || !undo.TryRestore(undo.NoteId, state))
        {
            _noteCurationUndo = null;
            NotesStatus = V24Text("That note no longer has local curation to restore.", "Ghi chú đó không còn thiết lập cục bộ để hoàn tác.");
            RefreshReaderCommands();
            return;
        }
        note?.RestoreRandomCuration(undo.SnoozedUntilUtc, undo.ExcludeFromRandom);
        _settings.NoteStates[undo.NoteId] = state;
        SetReaderCurationBusy(true);
        try
        {
            if (note is not null)
            {
                if (!await SaveNoteStateAsync(note)) return;
            }
            else await SaveSettingsDataAsync();
            ReconcileReaderCuration();
            // A later action may replace the one-step undo while this write awaits disk access.
            if (ReferenceEquals(_noteCurationUndo, undo)) _noteCurationUndo = null;
            NotesStatus = V24Text("Last rediscovery change undone. Writing, hearts and reading history are unchanged.",
                "Đã hoàn tác thay đổi Random. Nội dung riêng, yêu thích và lịch sử đọc không đổi.");
        }
        catch (Exception ex)
        {
            // Keep the snapshot available so a failed disk write can be retried.
            NotesStatus = V24Text("Could not save the undo. Try again. ", "Không thể lưu hoàn tác. Hãy thử lại. ") + Friendly(ex);
            _log("Note curation undo save error: " + ex);
        }
        finally { SetReaderCurationBusy(false); }
    }

    private void SetReaderCurationBusy(bool busy)
    {
        _noteCurationBusy = busy;
        Raise(nameof(IsNoteCurationBusy));
        RefreshReaderCommands();
    }

    private void ReconcileReaderCuration()
    {
        // Reconcile without selecting or marking the replacement preview as seen.
        BuildRediscoveryState();
        if (!IsNoteReaderOpen) ApplyNoteFilters();
        RefreshReaderCommands();
    }

    private void CopyReaderQuote()
    {
        if (SelectedNote is not { } note) return;
        CopyReaderText(NoteReaderContent.QuoteWithCitation(note.Source), V24Text(
            "Highlight/citation copied without private writing.", "Đã sao chép trích dẫn/nguồn, không kèm nội dung riêng."));
    }

    private void CopyReaderPrivateWriting()
    {
        if (SelectedNote is not { } note) return;
        var content = NoteReaderContent.PrivateWriting(note.Note, note.PersonalNote);
        if (string.IsNullOrWhiteSpace(content)) return;
        CopyReaderText(content, V24Text("Private writing copied. Keep the clipboard private.", "Đã sao chép nội dung riêng. Hãy bảo vệ bộ nhớ tạm."));
    }

    private void CopyReaderText(string content, string success)
    {
        try { Clipboard.SetText(content); NotesStatus = success; }
        catch (ExternalException ex)
        {
            NotesStatus = V24Text("The clipboard is busy. Try copying again.", "Bộ nhớ tạm đang bận. Hãy thử sao chép lại.");
            _log("Note clipboard error: " + ex.Message);
        }
    }

    // Call after SelectedNote/NoteRow changes, a direct BuildNotes selection, and clock refreshes.
    private void RefreshReaderCommands()
    {
        SnoozeSelectedNoteCommand.Refresh();
        ToggleRandomEligibilityCommand.Refresh();
        SnoozeNoteForCommand.Refresh();
        _openNoteReaderCommand?.Refresh();
        _openDailyNoteReaderCommand?.Refresh();
        _openRandomNoteReaderCommand?.Refresh();
        _toggleReaderFavoriteCommand?.Refresh();
        _copyReaderQuoteCommand?.Refresh();
        _copyReaderPrivateWritingCommand?.Refresh();
        _undoNoteCurationCommand?.Refresh();
        _clearSelectedNoteSnoozeCommand?.Refresh();
        foreach (var property in new[]
        {
            nameof(NoteListSelection), nameof(HasNoteCurationUndo), nameof(NoteCurationUndoLabel), nameof(ReaderHasSourceContent),
            nameof(ReaderHasPrivateWriting), nameof(ReaderContentIsPrivate), nameof(ReaderContentLabel),
            nameof(CopyReaderQuoteLabel), nameof(NoteReaderTitle), nameof(OpenNoteReaderLabel), nameof(ReaderPreviousLabel),
            nameof(ReaderNextLabel), nameof(ReaderCloseLabel), nameof(ReaderPrivateCommentLabel), nameof(ReaderPersonalWritingLabel),
            nameof(ReaderEmptyContentLabel), nameof(ReaderPrivacyLabel), nameof(ReaderShortcutsLabel), nameof(NotesReaderShortcutsLabel),
            nameof(CopyReaderPrivateWritingLabel), nameof(CopyReaderPrivateWritingHint), nameof(ClearNoteSnoozeLabel), nameof(UndoNoteCurationLabel)
        }) Raise(property);
    }
}
