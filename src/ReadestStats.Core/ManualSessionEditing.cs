namespace ReadestStats.Core;

public sealed record ManualSessionEditResult(ManualReadingSession? Session, string? Error);

/// <summary>Creates a correction without changing the saved session. Time edits are explicit.</summary>
public static class ManualSessionEditing
{
    public static ManualSessionEditResult Prepare(ManualReadingSession? original, ManualBook book,
        DateTimeOffset startedAt, double durationMinutes, int startPage, int endPage, string note, bool editTiming = false)
    {
        if (startPage <= 0 || endPage < startPage) return Invalid("Enter a valid start and end page.");
        if (book.TotalPages is > 0 && endPage > book.TotalPages) return Invalid($"End page cannot exceed {book.TotalPages}.");
        var candidate = new ManualReadingSession
        {
            Id = original?.Id ?? Guid.NewGuid().ToString("N"),
            BookId = book.Id,
            StartedAtUtc = original?.StartedAtUtc ?? startedAt.ToUniversalTime(),
            EndedAtUtc = original?.EndedAtUtc ?? startedAt.ToUniversalTime(),
            DurationSeconds = original?.DurationSeconds ?? 0,
            CreatedAtUtc = original?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            StartPage = startPage, EndPage = endPage,
            PagesRead = ManualReadingRules.CalculatePagesRead(startPage, endPage),
            Note = note?.Trim() ?? "",
            Segments = original?.Segments.Select(Clone).ToList() ?? []
        };
        if (original is not null && !editTiming) return new(candidate, null);
        var preserveDuration = original is not null && durationMinutes == original.DurationSeconds / 60;
        var seconds = preserveDuration ? original!.DurationSeconds : durationMinutes * 60;
        if (!double.IsFinite(seconds) || seconds is < 1 or > 86400)
            return Invalid("Duration must be from 1 second to 24 hours.");
        try
        {
            var start = startedAt.ToUniversalTime();
            if (preserveDuration)
            {
                // Moving only the start translates the exact recorded timeline.
                // Do not pass integer ticks through a floating seconds ratio.
                var shiftTicks = (start - original!.StartedAtUtc).Ticks;
                candidate.StartedAtUtc = start;
                candidate.EndedAtUtc = original.EndedAtUtc.AddTicks(shiftTicks);
                foreach (var segment in candidate.Segments)
                {
                    segment.StartedAtUtc = segment.StartedAtUtc.AddTicks(shiftTicks);
                    segment.EndedAtUtc = segment.EndedAtUtc.AddTicks(shiftTicks);
                }
                return new(candidate, null);
            }
            var source = original?.Segments.Where(segment => segment.EndedAtUtc > segment.StartedAtUtc)
                .OrderBy(segment => segment.StartedAtUtc).ToArray() ?? [];
            if (source.Length > 0)
            {
                // A deliberate time correction retains the rhythm: both reading
                // segments and pause gaps scale proportionally around a new start.
                var factor = seconds / source.Sum(segment => segment.DurationSeconds);
                var origin = source[0].StartedAtUtc;
                candidate.Segments = source.Select(segment => new ManualReadingSegment
                {
                    StartedAtUtc = AddSecondsPrecisely(start, (segment.StartedAtUtc - origin).TotalSeconds * factor),
                    EndedAtUtc = AddSecondsPrecisely(start, (segment.EndedAtUtc - origin).TotalSeconds * factor)
                }).ToList();
                if (candidate.Segments.Any(segment => segment.EndedAtUtc <= segment.StartedAtUtc))
                    return Invalid("This duration is too short to preserve the recorded reading segments.");
            }
            else candidate.Segments = [new() { StartedAtUtc = start, EndedAtUtc = AddSecondsPrecisely(start, seconds) }];
            candidate.StartedAtUtc = candidate.Segments.Min(segment => segment.StartedAtUtc);
            candidate.EndedAtUtc = candidate.Segments.Max(segment => segment.EndedAtUtc);
            candidate.DurationSeconds = candidate.Segments.Sum(segment => segment.DurationSeconds);
            return new(candidate, null);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        { return Invalid("The corrected time is outside the supported date range."); }
    }

    public static bool Overlaps(ManualReadingSession candidate, ManualReadingSession other)
    {
        if (candidate.Id == other.Id) return false;
        var otherIntervals = Intervals(other);
        return Intervals(candidate).Any(left => otherIntervals.Any(right =>
            left.StartedAtUtc < right.EndedAtUtc && left.EndedAtUtc > right.StartedAtUtc));
    }

    public static IReadOnlyList<ManualReadingSegment> Intervals(ManualReadingSession session) => session.Segments.Count > 0
        ? session.Segments.Where(segment => segment.EndedAtUtc > segment.StartedAtUtc).OrderBy(segment => segment.StartedAtUtc).ToArray()
        : session.DurationSeconds > 0 && double.IsFinite(session.DurationSeconds)
            ? [new() { StartedAtUtc = session.StartedAtUtc, EndedAtUtc = AddSecondsPrecisely(session.StartedAtUtc, session.DurationSeconds) }] : [];

    // AddSeconds truncates fractional double ticks. Round once at the canonical
    // 100-nanosecond boundary so a minutes input does not shift an exact stamp.
    private static DateTimeOffset AddSecondsPrecisely(DateTimeOffset start, double seconds)
        => start.AddTicks(checked((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero)));
    private static ManualReadingSegment Clone(ManualReadingSegment segment) => new() { StartedAtUtc = segment.StartedAtUtc, EndedAtUtc = segment.EndedAtUtc };
    private static ManualSessionEditResult Invalid(string error) => new(null, error);
}
