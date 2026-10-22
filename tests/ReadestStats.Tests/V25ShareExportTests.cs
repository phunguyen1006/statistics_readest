using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25ShareExportTests
{
    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData(" +SUM(1,2)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,2)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    [InlineData("\n=1+1")]
    [InlineData("\uFEFF=1+1")]
    [InlineData("\u0008=1+1")]
    public void CsvFormulaLikeText_IsQuotedAndForcedToText(string value) =>
        Assert.StartsWith("\"'", ShareExportPolicy.CsvCell(value));

    [Fact]
    public void CsvOrdinaryText_RetainsContentAndEscapesQuotes()
    {
        Assert.Equal("\"Book, \"\"edition\"\"\"", ShareExportPolicy.CsvCell("Book, \"edition\""));
        Assert.Equal("\"A normal title\"", ShareExportPolicy.CsvCell("A normal title"));
        Assert.Equal("\"\"", ShareExportPolicy.CsvCell(null));
    }

    [Theory]
    [InlineData("C:\\Users\\Reader\\covers\\a.jpg")]
    [InlineData("\\\\server\\covers\\a.jpg")]
    [InlineData("file:///C:/covers/a.jpg")]
    [InlineData("ms-settings:test")]
    [InlineData("https://user:password@example.test/a.jpg")]
    public void ShareCoverLocation_DropsLocalPathsAndNonWebValues(string value) =>
        Assert.Null(ShareExportPolicy.PublicCoverUrl(value));

    [Fact]
    public void ShareCopy_DefaultsToNoPrivateNotes_AndNeverMutatesOriginal()
    {
        var source = new ManualReadingData
        {
            Books = [new() { Id = -1, Title = "Book", CoverUrl = "C:\\private\\cover.jpg", InfoUrl = "file:///C:/private/a.html", GoodreadsUrl = "https://www.goodreads.com/book/show/1" }],
            Sessions = [new() { Id = "session", BookId = -1, Note = "Private session note", Segments = [new() { StartedAtUtc = DateTimeOffset.UtcNow, EndedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1) }] }],
            ActiveSession = new() { BookId = -1 },
            RecoveryReferences = new([], [], ["manual:-1"])
        };
        var copy = ShareExportPolicy.ShareLibrary(source);

        Assert.Null(copy.ActiveSession);
        Assert.Null(copy.RecoveryReferences);
        Assert.Null(copy.Books[0].CoverUrl);
        Assert.Null(copy.Books[0].InfoUrl);
        Assert.Equal(source.Books[0].GoodreadsUrl, copy.Books[0].GoodreadsUrl);
        Assert.Equal("", copy.Sessions[0].Note);
        copy.Sessions[0].Segments.Clear();
        Assert.Equal("Private session note", source.Sessions[0].Note);
        Assert.Single(source.Sessions[0].Segments);
        Assert.Equal("C:\\private\\cover.jpg", source.Books[0].CoverUrl);
        Assert.NotNull(source.ActiveSession);
    }

    [Fact]
    public void ShareCopy_OnlyIncludesPrivateSessionNotesWhenExplicitlyRequested()
    {
        var source = new ManualReadingData { Sessions = [new() { Note = "Personal" }] };
        Assert.Equal("Personal", ShareExportPolicy.ShareLibrary(source, includeSessionNotes: true).Sessions[0].Note);
        Assert.Equal("https://example.test/cover.jpg", ShareExportPolicy.PublicCoverUrl("https://example.test/cover.jpg"));
    }
}
