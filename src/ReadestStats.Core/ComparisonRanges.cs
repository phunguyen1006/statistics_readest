namespace ReadestStats.Core;

public static class ComparisonRanges
{
    public static ResolvedDateRange? Resolve(ResolvedDateRange current, string mode, DateOnly? customStart = null, DateOnly? customEnd = null, TimeZoneInfo? timeZone = null)
    {
        if (mode == "Off") return null;
        var zone = timeZone ?? TimeZoneInfo.Local;
        DateTimeOffset start, end;
        if (mode == "Custom")
        {
            if (customStart is null || customEnd is null || customEnd < customStart) return null;
            var ranges = new DateRangeService(zone);
            start = ranges.ToUtc(customStart.Value); end = ranges.ToUtc(customEnd.Value.AddDays(1));
        }
        else if (mode == "Same period last year")
        {
            DateTimeOffset Shift(DateTimeOffset date) { var local = TimeZoneInfo.ConvertTime(date, zone).DateTime.AddYears(-1); return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone)); }
            start = Shift(current.Start); end = Shift(current.End);
        }
        else { start = current.PreviousStart; end = current.PreviousEnd; }
        if (end <= start) return null;
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end.AddTicks(-1), zone).DateTime);
        return new(mode, start, end, start, end, first, last, last.DayNumber - first.DayNumber + 1);
    }
}
