namespace ReadestStats.Core;

/// <summary>Keep source highlights separate from private annotations and session writing.</summary>
public static class NoteReaderContent
{
    public static string Preview(ReadestNote note, string? editedSourceNote = null) =>
        !string.IsNullOrWhiteSpace(note.Text) ? note.Text : editedSourceNote ?? note.Note;

    public static string Citation(ReadestNote note)
    {
        var author = string.IsNullOrWhiteSpace(note.Authors) ? "" : $" — {note.Authors}";
        var location = note.Page is > 0 ? $", {note.PageLabel}" : "";
        return $"{note.BookTitle}{author}{location}";
    }

    // Deliberately accepts no personal-writing argument. A manual note yields a citation only.
    public static string QuoteWithCitation(ReadestNote note) => string.IsNullOrWhiteSpace(note.Text)
        ? Citation(note)
        : $"{note.Text.Trim()}{Environment.NewLine}{Environment.NewLine}{Citation(note)}";

    // Private writing is copied only through a separately labelled, explicit action.
    public static string PrivateWriting(string? sourceNote, string? personalNote) =>
        string.Join(Environment.NewLine + Environment.NewLine,
            new[] { sourceNote, personalNote }.Where(text => !string.IsNullOrWhiteSpace(text)));
}
