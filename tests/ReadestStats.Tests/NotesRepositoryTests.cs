using System.Text;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class NotesRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReadestStatsNotes-" + Guid.NewGuid().ToString("N"));

    public NotesRepositoryTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Books", "hash-a"));
        File.WriteAllText(Path.Combine(_root, "statistics.db"), "");
        File.WriteAllText(Path.Combine(_root, "Books", "library.json"), """
            [{"hash":"hash-a","title":"Fixture Book","author":"Fixture Author","filePath":"Books/hash-a/fixture.epub"}]
            """, Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "Books", "hash-a", "fixture.epub"), "fixture", Encoding.UTF8);
        File.WriteAllText(Path.Combine(_root, "Books", "hash-a", "config.json"), """
            {
              "booknotes": [
                {"id":"n1","bookHash":"hash-a","text":"A highlighted passage","note":"My thought","type":"annotation","color":"yellow","page":12,"createdAt":"2026-09-01T10:00:00Z"},
                {"id":"n2","bookHash":"hash-a","text":"Deleted passage","deletedAt":"2026-09-02T10:00:00Z"}
              ]
            }
            """, Encoding.UTF8);
    }

    [Fact]
    public async Task LoadsNotesAndSkipsDeletedItems()
    {
        var repository = new ReadestNotesRepository(Path.Combine(_root, "statistics.db"));
        var notes = await repository.LoadAsync();
        var note = Assert.Single(notes);
        Assert.Equal("n1", note.Id);
        Assert.Equal("Fixture Book", note.BookTitle);
        Assert.Equal("Fixture Author", note.Authors);
        Assert.Equal(12, note.Page);
        Assert.Equal("A highlighted passage", note.Text);
        Assert.Equal("My thought", note.Note);
        Assert.EndsWith(Path.Combine("Books", "hash-a", "fixture.epub"), note.BookPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, repository.LastDiagnostics.FilesScanned);
        Assert.Equal(1, repository.LastDiagnostics.NotesLoaded);
        Assert.Equal(1, repository.LastDiagnostics.NotesSkipped);
        Assert.Equal(0, repository.LastDiagnostics.FilesFailed);
    }

    [Fact]
    public async Task ReportsMalformedNoteFilesWithoutFailingTheLibrary()
    {
        var broken = Path.Combine(_root, "Books", "hash-b"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "config.json"), "{broken", Encoding.UTF8);
        var repository = new ReadestNotesRepository(Path.Combine(_root, "statistics.db")); var notes = await repository.LoadAsync();
        Assert.Single(notes);
        Assert.Equal(2, repository.LastDiagnostics.FilesScanned);
        Assert.Equal(1, repository.LastDiagnostics.FilesFailed);
        Assert.Equal(1, repository.LastDiagnostics.UnmappedBooks);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
