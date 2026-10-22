namespace ReadestStats.Core;

public sealed record LocalClockChange(DateOnly Date, bool DateChanged, bool EligibilityExpired);

/// <summary>Local UI boundaries only; never polls or writes a source database.</summary>
public static class LocalReadingClock
{
    public static LocalClockChange Observe(DateOnly previousDate, DateTimeOffset now,
        TimeZoneInfo timeZone, IEnumerable<DateTimeOffset> eligibilityBoundaries)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        return new(date, date != previousDate, eligibilityBoundaries.Any(boundary => boundary <= now));
    }
}
