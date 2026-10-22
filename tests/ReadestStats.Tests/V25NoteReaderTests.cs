using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25NoteReaderTests
{
    [Fact]
    public void UndoRestoresExactCurationWithoutOverwritingLaterWritingHeartOrSeenChanges()
    {
        var until = DateTimeOffset.Parse("2026-10-06T08:00:00Z");
        var state = new NoteUserState { SnoozedUntilUtc = until, ExcludeFromRandom = true };
        var undo = NoteCurationUndoSnapshot.Capture("manual:session:42", state);
        state.SnoozedUntilUtc = null;
        state.ExcludeFromRandom = false;
        state.IsFavorite = true;
        state.PersonalNote = "Written afterwards";
        state.TimesSeen = 8;
        state.IsHidden = true;

        Assert.True(undo.TryRestore("MANUAL:SESSION:42", state));
        Assert.Equal(until, state.SnoozedUntilUtc);
        Assert.True(state.ExcludeFromRandom);
        Assert.True(state.IsFavorite);
        Assert.Equal("Written afterwards", state.PersonalNote);
        Assert.Equal(8, state.TimesSeen);
        Assert.True(state.IsHidden);
    }

    [Fact]
    public void UndoCannotTargetWhateverNoteWasSelectedAfterTheAction()
    {
        var undo = NoteCurationUndoSnapshot.Capture("first", new());
        var next = new NoteUserState { ExcludeFromRandom = true, SnoozedUntilUtc = DateTimeOffset.UtcNow.AddDays(7) };
        var until = next.SnoozedUntilUtc;
        Assert.False(undo.TryRestore("next", next));
        Assert.True(next.ExcludeFromRandom);
        Assert.Equal(until, next.SnoozedUntilUtc);
    }

    [Fact]
    public void UndoSnapshotDoesNotChangeWhenOriginalStateChanges()
    {
        var state = new NoteUserState();
        var undo = NoteCurationUndoSnapshot.Capture("first", state);
        state.SnoozedUntilUtc = DateTimeOffset.UtcNow.AddDays(30);
        state.ExcludeFromRandom = true;
        Assert.True(undo.TryRestore("first", state));
        Assert.Null(state.SnoozedUntilUtc);
        Assert.False(state.ExcludeFromRandom);
    }

    [Fact]
    public void ManualPreviewUsesSessionWritingIncludingUnsavedEdits()
    {
        var note = Note("manual:session:42", "", "Private session writing");
        Assert.Equal("Private session writing", NoteReaderContent.Preview(note));
        Assert.Equal("Edited session writing", NoteReaderContent.Preview(note, "Edited session writing"));
        Assert.Equal("", NoteReaderContent.Preview(note, ""));
    }

    [Fact]
    public void ReadestPreviewPrefersHighlightWithoutCombiningPrivateComment()
    {
        var note = Note("digital", "A highlighted quotation", "PRIVATE annotation");
        Assert.Equal("A highlighted quotation", NoteReaderContent.Preview(note));
        Assert.DoesNotContain("PRIVATE", NoteReaderContent.QuoteWithCitation(note));
    }

    [Fact]
    public void DefaultManualCopyIsCitationOnlyAndCannotLeakSessionWriting()
    {
        var note = Note("manual:session:42", "", "PRIVATE session writing");
        var copied = NoteReaderContent.QuoteWithCitation(note);
        Assert.Equal("Book title — Author, Page 42", copied);
        Assert.DoesNotContain("PRIVATE", copied);
    }

    [Fact]
    public void ExplicitPrivateCopyIncludesOnlyNonemptyPrivateWriting()
    {
        Assert.Equal("Session writing" + Environment.NewLine + Environment.NewLine + "Personal writing",
            NoteReaderContent.PrivateWriting("Session writing", "Personal writing"));
        Assert.Equal("Personal writing", NoteReaderContent.PrivateWriting(" ", "Personal writing"));
        Assert.Empty(NoteReaderContent.PrivateWriting(null, ""));
    }

    private static ReadestNote Note(string id, string text, string annotation) =>
        new(id, "book", "Book title", "Author", null, null, text, annotation, "Note", "", 42,
            null, null, null, null, null);
}
