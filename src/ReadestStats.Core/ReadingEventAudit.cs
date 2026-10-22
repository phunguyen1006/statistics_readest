namespace ReadestStats.Core;

public sealed record ReadingEventExclusion(string Key, string Reason, DateTimeOffset ExcludedAtUtc);

public static class ReadingEventAudit
{
    public static string Key(ReadingEvent item) => $"{item.Source}:{item.BookId}:{item.Page}:{item.StartTime}";

    public static IReadOnlyList<ReadingEvent> Apply(
        IReadOnlyList<ReadingEvent> source,
        IEnumerable<ReadingEventExclusion> exclusions)
    {
        var keys = exclusions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        return source.Where(item => !keys.Contains(Key(item))).ToArray();
    }

    public static (double BeforeSeconds, double AfterSeconds, int BeforeEvents, int AfterEvents) Preview(
        IReadOnlyList<ReadingEvent> source,
        IEnumerable<ReadingEventExclusion> exclusions,
        ReadingEvent candidate)
    {
        var before = Apply(source, exclusions);
        var key = Key(candidate);
        var after = before.Where(item => Key(item) != key).ToArray();
        return (before.Sum(item => item.DurationSeconds), after.Sum(item => item.DurationSeconds), before.Count, after.Length);
    }
}
