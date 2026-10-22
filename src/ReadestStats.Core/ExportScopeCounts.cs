namespace ReadestStats.Core;

/// <summary>Counts of the records actually written, not just the records before privacy filtering.</summary>
public sealed record ExportScopeCounts(int Books = 0, int Sessions = 0, int Notes = 0, int Omitted = 0)
{
    public int ExportedItems => Books + Sessions + Notes;
    public bool CanExport => ExportedItems > 0;
}

public readonly record struct NoteExportScopeItem(bool IsManual, bool HasPublicHighlight);

public static class ExportScopeCounter
{
    public static bool IncludesNote(bool isManual, bool hasPublicHighlight, bool includePrivate) =>
        includePrivate || !isManual && hasPublicHighlight;

    public static ExportScopeCounts Notes(IEnumerable<NoteExportScopeItem> items, bool includePrivate)
    {
        var written = 0;
        var omitted = 0;
        foreach (var item in items)
        {
            if (IncludesNote(item.IsManual, item.HasPublicHighlight, includePrivate)) written++;
            else omitted++;
        }
        return new(Notes: written, Omitted: omitted);
    }

    /// <param name="sessions">The sessions in the chosen scope; pass every session for all-items/archive.</param>
    /// <param name="allBooks">Includes unlogged and archived books, as a full library export does.</param>
    public static ExportScopeCounts Physical(IEnumerable<ManualBook> allBooks,
        IEnumerable<ManualReadingSession> sessions, bool entireLibrary)
    {
        var scopedSessions = sessions.ToArray();
        var ids = scopedSessions.Select(session => session.BookId).ToHashSet();
        return new(Books: allBooks.Count(book => entireLibrary || ids.Contains(book.Id)), Sessions: scopedSessions.Length);
    }
}
