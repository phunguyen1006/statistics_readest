namespace ReadestStats.Core;

public static class ReadingEventMerger
{
    /// <summary>Removes time covered by authoritative events from candidate events without changing either source.</summary>
    public static IReadOnlyList<ReadingEvent> ExcludeOverlaps(IEnumerable<ReadingEvent> candidates, IEnumerable<ReadingEvent> blockers)
    {
        var occupied = blockers.Where(item => item.DurationSeconds > 0).Select(item => (item.Start, item.End)).OrderBy(item => item.Start).ToArray();
        var result = new List<ReadingEvent>();
        foreach (var candidate in candidates.Where(item => item.DurationSeconds > 0))
        {
            var remaining = new List<(DateTimeOffset Start, DateTimeOffset End)> { (candidate.Start, candidate.End) };
            foreach (var block in occupied)
            {
                if (block.End <= candidate.Start) continue;
                if (block.Start >= candidate.End) break;
                var next = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                foreach (var segment in remaining)
                {
                    if (block.End <= segment.Start || block.Start >= segment.End) { next.Add(segment); continue; }
                    if (block.Start > segment.Start) next.Add((segment.Start, block.Start));
                    if (block.End < segment.End) next.Add((block.End, segment.End));
                }
                remaining = next;
                if (remaining.Count == 0) break;
            }
            result.AddRange(remaining.Where(segment => segment.End > segment.Start).Select(segment => new ReadingEvent(candidate.BookId, candidate.Page, segment.Start.ToUnixTimeSeconds(), (segment.End - segment.Start).TotalSeconds, candidate.TotalPages, candidate.Source)));
        }
        return result;
    }
}
