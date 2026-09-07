namespace ReadestStats.Core;

public static class DateRangePresets
{
    public static readonly string[] All = ["7 days", "30 days", "90 days", "This week", "This month", "This year", "Last year", "All time", "Custom"];
}

public sealed record ResolvedDateRange(string Label, DateTimeOffset Start, DateTimeOffset End, DateTimeOffset PreviousStart, DateTimeOffset PreviousEnd, DateOnly StartDate, DateOnly EndDate, int AvailableDays);

public sealed class DateRangeService
{
    private readonly TimeZoneInfo _zone;
    public DateRangeService(TimeZoneInfo? zone = null) => _zone = zone ?? TimeZoneInfo.Local;

    public ResolvedDateRange Resolve(string preset, DateTimeOffset now, DateTimeOffset? firstEvent = null, bool weekStartsMonday = true, DateOnly? customStart = null, DateOnly? customEnd = null)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, _zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        DateOnly startDate;
        DateOnly endDate = today;
        DateOnly previousStart;
        DateOnly previousEnd;
        var rolling = preset is "7 days" or "30 days" or "90 days";
        switch (preset)
        {
            case "7 days": startDate = today.AddDays(-6); previousEnd = startDate.AddDays(-1); previousStart = previousEnd.AddDays(-6); break;
            case "90 days": startDate = today.AddDays(-89); previousEnd = startDate.AddDays(-1); previousStart = previousEnd.AddDays(-89); break;
            case "This week":
                var offset = weekStartsMonday ? ((int)today.DayOfWeek + 6) % 7 : (int)today.DayOfWeek;
                startDate = today.AddDays(-offset); previousStart = startDate.AddDays(-7); previousEnd = previousStart.AddDays(offset); break;
            case "This month":
                startDate = new(today.Year, today.Month, 1); var previousMonth = startDate.AddMonths(-1); previousStart = previousMonth; previousEnd = previousMonth.AddDays(Math.Min(today.Day - 1, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month) - 1)); break;
            case "This year":
                startDate = new(today.Year, 1, 1); previousStart = new(today.Year - 1, 1, 1); previousEnd = previousStart.AddDays(today.DayOfYear - 1); break;
            case "Last year": startDate = new(today.Year - 1, 1, 1); endDate = new(today.Year - 1, 12, 31); previousStart = new(today.Year - 2, 1, 1); previousEnd = new(today.Year - 2, 12, 31); break;
            case "All time":
                startDate = firstEvent is null ? today : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstEvent.Value, _zone).DateTime); previousStart = startDate; previousEnd = startDate.AddDays(-1); break;
            case "Custom":
                startDate = customStart is null || customStart > today ? today : customStart.Value; endDate = customEnd is null || customEnd > today ? today : customEnd.Value; if (endDate < startDate) (startDate, endDate) = (endDate, startDate); var length = endDate.DayNumber - startDate.DayNumber + 1; previousEnd = startDate.AddDays(-1); previousStart = previousEnd.AddDays(-(length - 1)); break;
            default: startDate = today.AddDays(-29); previousEnd = startDate.AddDays(-1); previousStart = previousEnd.AddDays(-29); break;
        }
        var firstDate = firstEvent is null ? startDate : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstEvent.Value, _zone).DateTime);
        var eligibleStart = firstDate > startDate ? firstDate : startDate;
        var available = eligibleStart > endDate ? 0 : endDate.DayNumber - eligibleStart.DayNumber + 1;
        var currentStart = ToUtc(startDate); var currentEnd = preset is "Last year" or "Custom" ? ToUtc(endDate.AddDays(1)) : now.AddMilliseconds(1); var priorEnd = rolling ? currentStart : ToUtc(previousEnd.AddDays(1)); var priorStart = rolling ? currentStart - (currentEnd - currentStart) : ToUtc(previousStart);
        return new(preset, currentStart, currentEnd, priorStart, priorEnd, startDate, endDate, available);
    }

    public DateTimeOffset ToUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new(TimeZoneInfo.ConvertTimeToUtc(local, _zone), TimeSpan.Zero);
    }
}
