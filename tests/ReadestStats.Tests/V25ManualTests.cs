using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25ManualTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ReadestV25Manual-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FinishCancelResumeFinish_PreservesDraftAndExcludesPausedTime()
    {
        var active = new ActiveManualSession { StartedAtUtc = Start, LastResumedAtUtc = Start, TargetMinutes = 20 };
        active.BeginFinish(Start.AddMinutes(5), 11);
        active.FinishEndPage = "22";
        active.FinishNote = "A useful passage";
        active.IsFinishing = false;
        active.Resume(Start.AddHours(1));
        active.BeginFinish(Start.AddHours(1).AddMinutes(2), 99);

        Assert.True(active.IsPaused);
        Assert.True(active.IsFinishing);
        Assert.Equal("11", active.FinishStartPage);
        Assert.Equal("22", active.FinishEndPage);
        Assert.Equal("A useful passage", active.FinishNote);
        Assert.Equal(420, active.ElapsedSeconds(Start.AddHours(2)));
        Assert.Equal(2, active.Segments.Count);
    }

    [Fact]
    public void FocusTarget_FreezesWhilePausedAndDoesNotAutoFinish()
    {
        var active = new ActiveManualSession { StartedAtUtc = Start, LastResumedAtUtc = Start, TargetMinutes = 10 };
        active.Pause(Start.AddMinutes(4));
        Assert.Equal(360, active.TargetRemainingSeconds(Start.AddHours(2)));
        active.Resume(Start.AddHours(2));
        Assert.Equal(60, active.TargetRemainingSeconds(Start.AddHours(2).AddMinutes(5)));
        Assert.Equal(0, active.TargetRemainingSeconds(Start.AddHours(2).AddMinutes(7)));
        Assert.False(active.IsPaused);
        Assert.False(active.IsFinishing);
        Assert.Equal(660, active.ElapsedSeconds(Start.AddHours(2).AddMinutes(7)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    public void FocusTarget_AbsentOrInvalidHasNoCountdown(int? target)
    {
        Assert.Null(new ActiveManualSession { TargetMinutes = target }.TargetRemainingSeconds(Start));
    }

    [Fact]
    public async Task Store_RestoresDurableFinishDraftAndFocusTargetFromSqlite()
    {
        var store = new ManualReadingStore(Path.Combine(_directory, "manual-reading.json"));
        var active = new ActiveManualSession { BookId = -1, StartedAtUtc = Start, LastResumedAtUtc = Start, TargetMinutes = 25 };
        active.BeginFinish(Start.AddMinutes(8), 3);
        active.FinishEndPage = "16";
        active.FinishNote = "Ghi chú tiếng Việt";
        await store.SaveAsync(new ManualReadingData { Books = [new ManualBook { Id = -1, Title = "Book" }], ActiveSession = active });
        File.Delete(store.Path);

        var restored = (await new ManualReadingStore(store.Path).LoadAsync()).ActiveSession!;
        Assert.True(restored.IsFinishing);
        Assert.True(restored.IsPaused);
        Assert.Null(restored.LastResumedAtUtc);
        Assert.Equal("3", restored.FinishStartPage);
        Assert.Equal("16", restored.FinishEndPage);
        Assert.Equal("Ghi chú tiếng Việt", restored.FinishNote);
        Assert.Equal(25, restored.TargetMinutes);
        Assert.Equal(480, restored.ElapsedSeconds(Start.AddDays(1)));
    }

    [Fact]
    public async Task Store_OlderActiveSessionGetsSafeOptionalDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "manual-reading.json");
        await File.WriteAllTextAsync(path, """
            {"SchemaVersion":3,"Books":[{"Id":-1,"Title":"Book"}],"Sessions":[],
             "ActiveSession":{"BookId":-1,"StartedAtUtc":"2026-10-01T18:00:00Z","IsPaused":true,"Segments":[]}}
            """);

        var active = (await new ManualReadingStore(path).LoadAsync()).ActiveSession!;
        Assert.False(active.IsFinishing);
        Assert.Null(active.TargetMinutes);
        Assert.Equal("", active.FinishStartPage);
        Assert.Equal("", active.FinishEndPage);
        Assert.Equal("", active.FinishNote);
    }

    [Fact]
    public void Journal_ContainsWholeHistoryAndSearchesBeyondOld250Limit()
    {
        var sessions = Enumerable.Range(0, 400).Select(index => Session(index.ToString(), Start.AddDays(index), index == 0 ? "needle in the oldest note" : "")).ToArray();
        var books = new[] { new ManualBook { Id = -1, Title = "A book" } };
        Assert.Equal(400, ManualSessionJournal.Query(sessions, books).Count);
        Assert.Equal("0", Assert.Single(ManualSessionJournal.Query(sessions, books, "needle oldest")).Id);
    }

    [Fact]
    public void Journal_CombinesBookDateAndAccentInsensitiveTextFilters()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC plus seven journal", TimeSpan.FromHours(7), "UTC plus seven journal", "UTC plus seven journal");
        var sessions = new[]
        {
            Session("a", Start, "Tình yêu và lòng tốt"),
            Session("b", Start.AddDays(1), "Tình yêu và lòng tốt"),
            new ManualReadingSession { Id = "c", BookId = -2, StartedAtUtc = Start, DurationSeconds = 100, Note = "Tình yêu" }
        };
        var books = new[] { new ManualBook { Id = -1, Title = "Cây cam ngọt", Authors = "José Mauro" }, new ManualBook { Id = -2, Title = "Other" } };
        var result = Assert.Single(ManualSessionJournal.Query(sessions, books, "cay JOSE tinh", -1,
            new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), zone));
        Assert.Equal("a", result.Id);
        Assert.Equal(2, result.StartedAt.Day);
        Assert.Equal("Tình yêu và lòng tốt", result.Note);
    }

    [Fact]
    public void Journal_InvertedDateRangeHasNoRowsAndUnknownBooksRemainReadable()
    {
        var sessions = new[] { Session("a", Start) };
        Assert.Empty(ManualSessionJournal.Query(sessions, [], from: new DateOnly(2026, 10, 5), through: new DateOnly(2026, 10, 1)));
        Assert.Equal("Unknown book", Assert.Single(ManualSessionJournal.Query(sessions, [])).BookTitle);
    }

    [Fact]
    public void RepeatedPauseOrResume_DoesNotDuplicateReadingSegments()
    {
        var active = new ActiveManualSession { LastResumedAtUtc = Start };
        active.Pause(Start.AddMinutes(1));
        active.Pause(Start.AddMinutes(2));
        active.Resume(Start.AddMinutes(3));
        active.Resume(Start.AddMinutes(4));
        active.Pause(Start.AddMinutes(5));
        Assert.Equal(180, active.ElapsedSeconds(Start.AddDays(1)));
        Assert.Equal(2, active.Segments.Count);
    }

    private static ManualReadingSession Session(string id, DateTimeOffset start, string note = "") => new()
    {
        Id = id, BookId = -1, StartedAtUtc = start, EndedAtUtc = start.AddMinutes(2), DurationSeconds = 120, Note = note
    };

    public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
}
