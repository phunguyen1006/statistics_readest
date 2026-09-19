namespace ReadestStats.Core;

public sealed class ReadingPlanEngine
{
    public ReadingPlanProgress Evaluate(
        ReadingPlan? plan,
        int? currentPage,
        int? totalPages,
        double recentDailySeconds,
        DateOnly today)
    {
        if (plan is not { Enabled: true }) return new(false, "No plan", "Set a target date or daily pace for this book.", 0, 0, 0, 0, 0, null, "");
        var pageBased = totalPages is > 0;
        var actual = pageBased ? Math.Clamp(currentPage ?? 0, 0, totalPages!.Value) : recentDailySeconds / 60d;
        var target = pageBased ? totalPages!.Value : Math.Max(plan.DailyMinutes, 1);
        var remaining = Math.Max(0, target - actual);
        var readingDays = plan.TargetDate is { } date ? CountReadingDays(today, date, plan.IncludeWeekends) : 0;
        var requiredPerDay = readingDays > 0 ? remaining / readingDays : pageBased ? Math.Max(0, plan.DailyPages) : Math.Max(0, plan.DailyMinutes);
        var plannedDaily = pageBased ? Math.Max(0, plan.DailyPages) : Math.Max(0, plan.DailyMinutes);
        var status = remaining <= 0 ? "Complete" : plan.TargetDate is { } deadline && deadline < today ? "Behind" : plannedDaily <= 0 || requiredPerDay <= plannedDaily ? "On track" : "Behind";
        var projected = plannedDaily <= 0 || remaining <= 0 ? (DateOnly?)null : AddReadingDays(today, (int)Math.Ceiling(remaining / plannedDaily), plan.IncludeWeekends);
        var unit = pageBased ? "pages" : "minutes";
        var summary = remaining <= 0
            ? "Target reached."
            : $"{remaining:0.#} {unit} remaining · {requiredPerDay:0.#} per reading day" + (projected is null ? "" : $" · projected {projected:MMM d}");
        return new(true, status, summary, actual, Math.Max(0, target - requiredPerDay * Math.Max(0, readingDays - 1)), target, remaining, requiredPerDay, projected, unit);
    }

    public static int CountReadingDays(DateOnly start, DateOnly end, bool includeWeekends)
    {
        if (end < start) return 0;
        var count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
            if (includeWeekends || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        return count;
    }

    private static DateOnly AddReadingDays(DateOnly start, int days, bool includeWeekends)
    {
        var date = start;
        var remaining = Math.Max(0, days);
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (includeWeekends || date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) remaining--;
        }
        return date;
    }
}
