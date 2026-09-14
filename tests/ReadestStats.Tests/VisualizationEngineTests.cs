using ReadestStats.Core;

namespace ReadestStats.Tests;

public sealed class VisualizationEngineTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Viz UTC", TimeSpan.Zero, "Viz UTC", "Viz UTC");
    private readonly VisualizationEngine _visualization = new(Zone);
    private readonly DateRangeService _ranges = new(Zone);
    private static DateTimeOffset At(int year, int month, int day, int hour = 12) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);
    private static ReadingEvent Event(DateTimeOffset at, double seconds = 60) => new(1, 1, at.ToUnixTimeSeconds(), seconds, null);

    [Fact]
    public void StoryUsesOnlyObservedActivity()
    {
        var now = At(2026, 9, 13, 23);
        var events = new[] { Event(At(2026, 9, 12, 21), 600), Event(At(2026, 9, 13, 22), 1200) };
        var range = _ranges.Resolve("30 days", now, events[0].Start);
        var story = _visualization.Build(events, range, new(1800, 0, 1800, null, false), 5, now);

        Assert.Equal(30, story.StreakTimeline.Count);
        Assert.Equal(2, story.ActiveLast30);
        Assert.Equal(2, story.StreakTimeline.Count(point => point.Value > 0));
        Assert.Equal(0.5, story.CumulativeJourney[^1].Value, 6);
        Assert.Contains("21:00", story.PeakWindow);
    }

    [Fact]
    public void EmptyStoryDoesNotInventAnnotations()
    {
        var now = At(2026, 9, 13, 18);
        var range = _ranges.Resolve("30 days", now);
        var story = _visualization.Build([], range, new(0, 0, 0, null, false), 5, now);

        Assert.Empty(story.CumulativeJourney);
        Assert.Equal("No monthly activity yet", story.BestMonth);
        Assert.Contains("Not enough activity", story.PeakWindow);
        Assert.All(story.MonthlyJourney, point => Assert.Equal(0, point.Value));
    }

    [Fact]
    public void ComparisonBarsRemainFiniteWithoutBaseline()
    {
        var now = At(2026, 9, 13, 18);
        var range = _ranges.Resolve("7 days", now);
        var story = _visualization.Build([Event(now, 90)], range, new(90, 0, 90, null, false), 5, now);

        Assert.Equal(2, story.ComparisonBars.Count);
        Assert.All(story.ComparisonBars, point => Assert.True(double.IsFinite(point.Value)));
    }
}
