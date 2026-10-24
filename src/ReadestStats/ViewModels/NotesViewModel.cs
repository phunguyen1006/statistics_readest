using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class NotesViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public NotesViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedNote)) Raise(nameof(SelectedNote));
            if (e.PropertyName == nameof(MainViewModel.NoteListSelection)) Raise(nameof(NoteListSelection));
            if (e.PropertyName == nameof(MainViewModel.DailyNote)) Raise(nameof(DailyNote));
            if (e.PropertyName == nameof(MainViewModel.RandomNote)) Raise(nameof(RandomNote));
            if (e.PropertyName == nameof(MainViewModel.RandomNotePool)) Raise(nameof(RandomNotePool));
            if (e.PropertyName == nameof(MainViewModel.RandomPoolStatus)) Raise(nameof(RandomPoolStatus));
            if (e.PropertyName == nameof(MainViewModel.NotesSummary)) Raise(nameof(NotesSummary));
            if (e.PropertyName == nameof(MainViewModel.NotesStatus)) Raise(nameof(NotesStatus));
            if (e.PropertyName == nameof(MainViewModel.HasNoteCurationUndo)) Raise(nameof(HasNoteCurationUndo));
            if (e.PropertyName == nameof(MainViewModel.NoteCurationUndoLabel)) Raise(nameof(NoteCurationUndoLabel));
            if (e.PropertyName == nameof(MainViewModel.IsNoteReaderOpen)) Raise(nameof(IsNoteReaderOpen));
        };
    }

    public MainViewModel Main => _main;
    public ICommand RefreshNotesCommand => _main.RefreshNotesCommand;
    public string NotesSummary => _main.NotesSummary;
    public NoteRow? DailyNote => _main.DailyNote;
    public ICommand DailyNoteCommand => _main.DailyNoteCommand;
    public string OpenNoteReaderLabel => _main.OpenNoteReaderLabel;
    public ICommand OpenDailyNoteReaderCommand => _main.OpenDailyNoteReaderCommand;
    public string[] RandomNotePools => _main.RandomNotePools;
    public string RandomNotePool { get => _main.RandomNotePool; set => _main.RandomNotePool = value; }
    public NoteRow? RandomNote => _main.RandomNote;
    public string RandomPoolStatus => _main.RandomPoolStatus;
    public ICommand RandomNoteCommand => _main.RandomNoteCommand;
    public ICommand PreviousRandomNoteCommand => _main.PreviousRandomNoteCommand;
    public ICommand OpenRandomNoteReaderCommand => _main.OpenRandomNoteReaderCommand;
    public ObservableCollection<NoteRow> RandomNoteHistory => _main.RandomNoteHistory;
    public ICommand OpenRediscoveryHistoryCommand => _main.OpenRediscoveryHistoryCommand;
    public int TotalNotes => _main.TotalNotes;
    public int FavoriteNotesCount => _main.FavoriteNotesCount;
    public int NotesReadCount => _main.NotesReadCount;
    public bool HasNoteCurationUndo => _main.HasNoteCurationUndo;
    public string UndoNoteCurationLabel => _main.UndoNoteCurationLabel;
    public ICommand UndoNoteCurationCommand => _main.UndoNoteCurationCommand;
    public string NoteCurationUndoLabel => _main.NoteCurationUndoLabel;
    public string NotesReaderShortcutsLabel => _main.NotesReaderShortcutsLabel;
    public string NoteSearch { get => _main.NoteSearch; set => _main.NoteSearch = value; }
    public string[] NoteScopeOptions => _main.NoteScopeOptions;
    public string NoteScope { get => _main.NoteScope; set => _main.NoteScope = value; }
    public ObservableCollection<NoteRow> Notes => _main.Notes;
    public NoteRow? NoteListSelection => _main.NoteListSelection;
    public ICommand ToggleFavoriteNoteCommand => _main.ToggleFavoriteNoteCommand;
    public bool HasNoVisibleNotes => _main.HasNoVisibleNotes;
    public string NotesEmptyMessage => _main.NotesEmptyMessage;
    public NoteRow? SelectedNote { get => _main.SelectedNote; set => _main.SelectedNote = value; }
    public ICommand OpenNoteReaderCommand => _main.OpenNoteReaderCommand;
    public ICommand OpenSelectedNoteCommand => _main.OpenSelectedNoteCommand;
    public ICommand RandomSameBookCommand => _main.RandomSameBookCommand;
    public string CopyReaderQuoteLabel => _main.CopyReaderQuoteLabel;
    public ICommand CopyReaderQuoteCommand => _main.CopyReaderQuoteCommand;
    public string CopyReaderPrivateWritingLabel => _main.CopyReaderPrivateWritingLabel;
    public ICommand CopyReaderPrivateWritingCommand => _main.CopyReaderPrivateWritingCommand;
    public string CopyReaderPrivateWritingHint => _main.CopyReaderPrivateWritingHint;
    public bool ReaderHasPrivateWriting => _main.ReaderHasPrivateWriting;
    public ICommand SaveSelectedNoteCommand => _main.SaveSelectedNoteCommand;
    public ICommand SnoozeNoteForCommand => _main.SnoozeNoteForCommand;
    public ICommand ToggleRandomEligibilityCommand => _main.ToggleRandomEligibilityCommand;
    public string ClearNoteSnoozeLabel => _main.ClearNoteSnoozeLabel;
    public ICommand ClearSelectedNoteSnoozeCommand => _main.ClearSelectedNoteSnoozeCommand;
    public string NotesStatus => _main.NotesStatus;
    public ICommand ExportNotesMarkdownCommand => _main.ExportNotesMarkdownCommand;
    public ICommand ExportNotesJsonCommand => _main.ExportNotesJsonCommand;
    public ICommand ExportNotesCsvCommand => _main.ExportNotesCsvCommand;
    public ObservableCollection<ChartPoint> NotesByBook => _main.NotesByBook;
    public ObservableCollection<ChartPoint> NotesByMonth => _main.NotesByMonth;
    public ObservableCollection<ChartPoint> NotesByType => _main.NotesByType;
    public bool IsNoteReaderOpen => _main.IsNoteReaderOpen;
    public ICommand ToggleReaderFavoriteCommand => _main.ToggleReaderFavoriteCommand;
}
