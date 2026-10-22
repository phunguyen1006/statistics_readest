namespace ReadestStats.Core;

public static class ReadingEventWindow
{
    public static IReadOnlyList<ReadingEvent> Clip(IEnumerable<ReadingEvent> events, DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return [];
        return events.Select(item =>
        {
            var clippedStart = item.Start > start ? item.Start : start;
            var clippedEnd = item.End < end ? item.End : end;
            return clippedEnd > clippedStart
                ? item with { StartTime = clippedStart.ToUnixTimeSeconds(), DurationSeconds = (clippedEnd - clippedStart).TotalSeconds }
                : null;
        }).Where(item => item is not null).Select(item => item!).ToArray();
    }
}
