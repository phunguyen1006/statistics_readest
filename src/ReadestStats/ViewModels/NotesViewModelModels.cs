using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class NoteRow : ObservableObject
{
    public NoteRow(ReadestNote source, NoteUserState state)
    {
        Source = source;
        State = state;
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
    public string Note => Source.Note;
    public string TypeLabel => Source.TypeLabel;
    public string ColorLabel => Source.ColorLabel;
    public int? Page => Source.Page;
    public string PageLabel => Source.PageLabel;
    public string DisplayDate => Source.DisplayDate;
    public DateTimeOffset SortDate => Source.CreatedAt ?? Source.UpdatedAt ?? DateTimeOffset.MinValue;
    public bool HasHighlight => Source.HasHighlight;
    public bool HasComment => Source.HasComment;
    public string? Cfi => Source.Cfi;
    public string? XpointerStart => Source.XpointerStart;
    public string? XpointerEnd => Source.XpointerEnd;

    public bool IsFavorite
    {
        get => State.IsFavorite;
        set { if (State.IsFavorite == value) return; State.IsFavorite = value; Raise(); Raise(nameof(FavoriteLabel)); }
    }

    public string FavoriteLabel => IsFavorite ? "Remove from favorites" : "Add to favorites";

    public bool IsHidden
    {
        get => State.IsHidden;
        set { if (State.IsHidden == value) return; State.IsHidden = value; Raise(); }
    }

    public string TagsText
    {
        get => string.Join(", ", State.Tags);
        set { State.Tags = ParseList(value); Raise(); Raise(nameof(HasTags)); }
    }

    public string CollectionsText
    {
        get => string.Join(", ", State.Collections);
        set { State.Collections = ParseList(value); Raise(); Raise(nameof(HasCollections)); }
    }

    public string PersonalNote
    {
        get => State.PersonalNote;
        set { if (State.PersonalNote == value) return; State.PersonalNote = value ?? ""; Raise(); Raise(nameof(HasPersonalNote)); }
    }

    public bool HasTags => State.Tags.Count > 0;
    public bool HasCollections => State.Collections.Count > 0;
    public bool HasPersonalNote => !string.IsNullOrWhiteSpace(State.PersonalNote);

    public string ReviewStatus
    {
        get => string.IsNullOrWhiteSpace(State.ReviewStatus) ? "New" : State.ReviewStatus;
        set { var normalized = string.IsNullOrWhiteSpace(value) ? "New" : value; if (State.ReviewStatus == normalized) return; State.ReviewStatus = normalized; Raise(); Raise(nameof(ReviewLabel)); }
    }

    public DateTimeOffset? NextReviewUtc => State.NextReviewUtc;
    public bool IsDue => State.NextReviewUtc is not null && State.NextReviewUtc <= DateTimeOffset.UtcNow;
    public string ReviewLabel => IsDue ? "Due for review" : State.NextReviewUtc is null ? "Not scheduled" : $"Review {State.NextReviewUtc.Value.ToLocalTime():MMM d}";
    public int TimesSeen => State.TimesSeen;

    public void MarkSeen()
    {
        State.TimesSeen++;
        Raise(nameof(TimesSeen));
    }

    public void SetReview(string status, int intervalDays)
    {
        State.ReviewStatus = status;
        State.ReviewIntervalDays = Math.Max(1, intervalDays);
        State.LastReviewedUtc = DateTimeOffset.UtcNow;
        State.NextReviewUtc = DateTimeOffset.UtcNow.AddDays(State.ReviewIntervalDays);
        Raise(nameof(ReviewStatus));
        Raise(nameof(NextReviewUtc));
        Raise(nameof(IsDue));
        Raise(nameof(ReviewLabel));
    }

    private static List<string> ParseList(string? value) => (value ?? "")
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(item => item.Length > 0)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .Take(20)
        .ToList();
}
