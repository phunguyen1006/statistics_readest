using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class NoteRow : ObservableObject
{
    private string _sourceNote;

    public NoteRow(ReadestNote source, NoteUserState state)
    {
        Source = source;
        State = state;
        _sourceNote = source.Note;
    }

    public ReadestNote Source { get; }
    public NoteUserState State { get; }
    public string Id => Source.Id;
    public string BookHash => Source.BookHash;
    public string BookTitle => Source.BookTitle;
    public string Authors => Source.Authors;
    public string? BookPath => Source.BookPath;
    public string? CoverPath => Source.CoverPath;
    public string Text => Source.Text;
    public string Note
    {
        get => _sourceNote;
        set { if (Set(ref _sourceNote, value ?? "")) Raise(nameof(HasComment)); }
    }
    public bool IsManual => Id.StartsWith("manual:", StringComparison.OrdinalIgnoreCase);
    public bool IsReadest => !IsManual;
    public bool HasComment => !string.IsNullOrWhiteSpace(Note);
    public string TypeLabel => Source.TypeLabel;
    public string ColorLabel => Source.ColorLabel;
    public int? Page => Source.Page;
    public string PageLabel => Source.PageLabel;
    public string DisplayDate => Source.DisplayDate;
    public DateTimeOffset SortDate => Source.CreatedAt ?? Source.UpdatedAt ?? DateTimeOffset.MinValue;
    public bool HasHighlight => Source.HasHighlight;
    public string? Cfi => Source.Cfi;
    public string? XpointerStart => Source.XpointerStart;
    public string? XpointerEnd => Source.XpointerEnd;

    public bool IsFavorite
    {
        get => State.IsFavorite;
        set { if (State.IsFavorite == value) return; State.IsFavorite = value; Raise(); Raise(nameof(FavoriteLabel)); }
    }

    public string FavoriteLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public string PersonalNote
    {
        get => State.PersonalNote;
        set { if (State.PersonalNote == value) return; State.PersonalNote = value ?? ""; Raise(); Raise(nameof(HasPersonalNote)); }
    }

    public bool HasPersonalNote => !string.IsNullOrWhiteSpace(State.PersonalNote);
    public int TimesSeen => State.TimesSeen;
    public DateTimeOffset? LastSeenUtc => State.LastSeenUtc;
    public string RediscoveryLabel => State.LastSeenUtc is null ? "Not seen yet" : $"Last seen {State.LastSeenUtc.Value.ToLocalTime():MMM d, yyyy}";

    public void MarkSeen()
    {
        State.TimesSeen++;
        State.LastSeenUtc = DateTimeOffset.UtcNow;
        Raise(nameof(TimesSeen));
        Raise(nameof(LastSeenUtc));
        Raise(nameof(RediscoveryLabel));
    }
}
