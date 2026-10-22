using System.Text.Json;
using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class V25SessionEditingTests
{
    private static readonly ManualBook Edition = new() { Id = -2, Title = "A different physical edition", TotalPages = 500 };

    [Fact]
    public void NotePagesAndBookCorrection_PreservesAllRecordedTimeExactly()
    {
        var original = PausedSession();
        var before = JsonSerializer.Serialize(original);
        var edited = ManualSessionEditing.Prepare(original, Edition, original.StartedAtUtc.AddDays(1), 999, 21, 30, "  Corrected note  ").Session!;
        Assert.Equal(-2, edited.BookId);
        Assert.Equal(21, edited.StartPage);
        Assert.Equal(30, edited.EndPage);
        Assert.Equal(10, edited.PagesRead);
        Assert.Equal("Corrected note", edited.Note);
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.CreatedAtUtc, edited.CreatedAtUtc);
        Assert.Equal(original.StartedAtUtc, edited.StartedAtUtc);
        Assert.Equal(original.EndedAtUtc, edited.EndedAtUtc);
        Assert.Equal(original.DurationSeconds, edited.DurationSeconds);
        Assert.Equal(JsonSerializer.Serialize(original.Segments), JsonSerializer.Serialize(edited.Segments));
        Assert.NotSame(original.Segments, edited.Segments);
        Assert.NotSame(original.Segments[0], edited.Segments[0]);
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void ExplicitTimingEdit_ShiftsStartAndScalesReadingAndPauseGaps()
    {
        var original = PausedSession();
        var changedStart = original.StartedAtUtc.AddDays(2).AddMinutes(3);
        var edited = ManualSessionEditing.Prepare(original, Edition, changedStart, original.DurationSeconds * 2 / 60, 1, 10, "", editTiming: true).Session!;
        Assert.Equal(changedStart, edited.StartedAtUtc);
        Assert.Equal(original.DurationSeconds * 2, edited.DurationSeconds, 6);
        Assert.Equal(2, edited.Segments.Count);
        Assert.Equal(original.Segments[0].DurationSeconds * 2, edited.Segments[0].DurationSeconds, 6);
        Assert.Equal(original.Segments[1].DurationSeconds * 2, edited.Segments[1].DurationSeconds, 6);
        Assert.Equal((original.Segments[1].StartedAtUtc - original.Segments[0].EndedAtUtc).TotalSeconds * 2,
            (edited.Segments[1].StartedAtUtc - edited.Segments[0].EndedAtUtc).TotalSeconds, 6);
        Assert.Equal((original.EndedAtUtc - original.StartedAtUtc).TotalSeconds * 2,
            (edited.EndedAtUtc - edited.StartedAtUtc).TotalSeconds, 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void InvalidTimingEdit_DoesNotTouchOriginal(double minutes)
    {
        var original = PausedSession();
        var before = JsonSerializer.Serialize(original);
        var result = ManualSessionEditing.Prepare(original, Edition, original.StartedAtUtc, minutes, 1, 10, "", editTiming: true);
        Assert.Null(result.Session);
        Assert.NotNull(result.Error);
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void StartOnlyTimingEdit_PreservesExactDurationAndEverySegmentOffset()
    {
        var original = PausedSession();
        original.Segments[0].EndedAtUtc = original.Segments[0].EndedAtUtc.AddTicks(3);
        original.DurationSeconds = original.Segments.Sum(segment => segment.DurationSeconds);
        var before = JsonSerializer.Serialize(original);
        var changedStart = original.StartedAtUtc.AddDays(-7).AddTicks(9);
        var delta = changedStart - original.StartedAtUtc;
        var edited = ManualSessionEditing.Prepare(original, Edition, changedStart, original.DurationSeconds / 60,
            1, 10, "Start only", editTiming: true).Session!;
        Assert.Equal(changedStart, edited.StartedAtUtc);
        Assert.Equal(original.DurationSeconds, edited.DurationSeconds);
        Assert.Equal(original.EndedAtUtc.AddTicks(delta.Ticks), edited.EndedAtUtc);
        Assert.Equal(original.Segments.Count, edited.Segments.Count);
        for (var i = 0; i < original.Segments.Count; i++)
        {
            Assert.Equal(original.Segments[i].StartedAtUtc.AddTicks(delta.Ticks), edited.Segments[i].StartedAtUtc);
            Assert.Equal(original.Segments[i].EndedAtUtc.AddTicks(delta.Ticks), edited.Segments[i].EndedAtUtc);
        }
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void Overlap_UsesActualReadingSegmentsNotPauseGap()
    {
        var original = PausedSession();
        var inPause = new ManualReadingSession { Id = "other", StartedAtUtc = original.StartedAtUtc.AddMinutes(2), DurationSeconds = 60 };
        Assert.False(ManualSessionEditing.Overlaps(original, inPause));
        inPause.StartedAtUtc = original.StartedAtUtc.AddSeconds(30);
        Assert.True(ManualSessionEditing.Overlaps(original, inPause));
        inPause.StartedAtUtc = original.Segments[0].EndedAtUtc;
        Assert.False(ManualSessionEditing.Overlaps(original, inPause));
        Assert.False(ManualSessionEditing.Overlaps(original, original));
    }

    [Fact]
    public void NewHistoricalSession_GetsFractionalSecondsWithoutMinuteRounding()
    {
        var start = new DateTimeOffset(2026, 10, 4, 23, 59, 59, TimeSpan.Zero).AddTicks(1234567);
        var edited = ManualSessionEditing.Prepare(null, Edition, start, 1.025, 1, 5, "New").Session!;
        Assert.Equal(start, edited.StartedAtUtc);
        Assert.Equal(61.5, edited.DurationSeconds, 6);
        Assert.Equal(start.AddSeconds(61.5), edited.EndedAtUtc);
        Assert.Single(edited.Segments);
    }

    [Fact]
    public void ExplicitTimingEdit_RoundsFractionalOffsetsAtTickBoundary()
    {
        var start = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        var original = new ManualReadingSession
        {
            Id = "fractional", StartedAtUtc = start, EndedAtUtc = start.AddSeconds(150), DurationSeconds = 120,
            Segments = [new() { StartedAtUtc = start, EndedAtUtc = start.AddSeconds(60) },
                new() { StartedAtUtc = start.AddSeconds(90), EndedAtUtc = start.AddSeconds(150) }]
        };
        var edited = ManualSessionEditing.Prepare(original, Edition, start, 2.05, 1, 5, "", editTiming: true).Session!;
        Assert.Equal(123, edited.DurationSeconds);
        Assert.Equal(start.AddSeconds(61.5), edited.Segments[0].EndedAtUtc);
        Assert.Equal(start.AddSeconds(92.25), edited.Segments[1].StartedAtUtc);
        Assert.Equal(start.AddSeconds(153.75), edited.Segments[1].EndedAtUtc);
    }

    [Fact]
    public void PageOnlyCorrection_KeepsMidnightAndHourBucketsUnchanged()
    {
        var original = PausedSession();
        var corrected = ManualSessionEditing.Prepare(original, Edition, original.StartedAtUtc.AddHours(-1), 2, 10, 20, "Page correction").Session!;
        var engine = new StatisticsEngine(TimeZoneInfo.Utc);
        static ReadingEvent[] Events(ManualReadingSession session) => session.Segments.Select(segment =>
            new ReadingEvent(-1, 0, segment.StartedAtUtc.ToUnixTimeSeconds(), segment.DurationSeconds, 500, "Manual")).ToArray();
        var before = engine.Daily(Events(original), TimeSpan.FromMinutes(5));
        var after = engine.Daily(Events(corrected), TimeSpan.FromMinutes(5));
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
    }

    [Fact]
    public void InvalidPages_DoNotCreateOrMutateSession()
    {
        var original = PausedSession();
        var before = JsonSerializer.Serialize(original);
        Assert.Null(ManualSessionEditing.Prepare(original, Edition, original.StartedAtUtc, 2, 1, 501, "").Session);
        Assert.Null(ManualSessionEditing.Prepare(original, Edition, original.StartedAtUtc, 2, 30, 20, "").Session);
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    private static ManualReadingSession PausedSession()
    {
        var start = new DateTimeOffset(2026, 10, 4, 23, 58, 1, TimeSpan.Zero).AddTicks(1234567);
        return new()
        {
            Id = "paused", BookId = -1, StartedAtUtc = start, EndedAtUtc = start.AddSeconds(421.5),
            DurationSeconds = 122.75, StartPage = 1, EndPage = 10, Note = "Original", CreatedAtUtc = start.AddDays(-2),
            Segments = [new() { StartedAtUtc = start, EndedAtUtc = start.AddSeconds(61.25) },
                new() { StartedAtUtc = start.AddSeconds(360), EndedAtUtc = start.AddSeconds(421.5) }]
        };
    }
}
