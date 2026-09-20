namespace ReadestStats.Core;

public sealed class PlanAdherenceEngine
{
    public IReadOnlyList<PlanAdherenceDay> Build(ReadingPlan? plan, DateOnly start, DateOnly end, IReadOnlyDictionary<DateOnly, double> actualByDay)
    {
        if (plan is not { Enabled: true } || end < start) return [];
        var unit = plan.DailyPages > 0 ? "pages" : "minutes";
        var dailyTarget = plan.DailyPages > 0 ? plan.DailyPages : plan.DailyMinutes;
        var result = new List<PlanAdherenceDay>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var scheduled = IsReadingDay(date, plan);
            var skipped = plan.SkippedDates.Contains(date);
            var planned = scheduled && !skipped ? Math.Max(0, dailyTarget) : 0;
            var actual = actualByDay.GetValueOrDefault(date);
            var status = skipped ? "Skipped" : !scheduled ? "Rest day" : planned <= 0 ? "No target" : actual >= planned ? "Complete" : actual > 0 ? "Partial" : "Missed";
            result.Add(new(date, planned, actual, status, unit));
        }
        return result;
    }

    public static bool IsReadingDay(DateOnly date, ReadingPlan plan)
    {
        if (plan.StartDate is { } start && date < start) return false;
        if (plan.TargetDate is { } end && date > end) return false;
        return plan.ReadingDays.Count > 0 ? plan.ReadingDays.Contains(date.DayOfWeek) : plan.IncludeWeekends || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}
