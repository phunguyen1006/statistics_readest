namespace ReadestStats.Core;

/// <summary>A single exact-note rediscovery change; never rolls back writing, hearts or seen counts.</summary>
public sealed record NoteCurationUndoSnapshot(string NoteId, DateTimeOffset? SnoozedUntilUtc, bool ExcludeFromRandom)
{
    public static NoteCurationUndoSnapshot Capture(string noteId, NoteUserState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        ArgumentNullException.ThrowIfNull(state);
        return new(noteId, state.SnoozedUntilUtc, state.ExcludeFromRandom);
    }

    public bool TryRestore(string noteId, NoteUserState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!NoteId.Equals(noteId, StringComparison.OrdinalIgnoreCase)) return false;
        state.SnoozedUntilUtc = SnoozedUntilUtc;
        state.ExcludeFromRandom = ExcludeFromRandom;
        return true;
    }
}
