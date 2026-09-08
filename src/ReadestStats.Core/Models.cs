namespace ReadestStats.Core;

public sealed record Book(long Id, string Title, string Authors, long? LastOpen = null, int? Pages = null, string? Series = null, string? Language = null, string? Hash = null);
public sealed record ReadingEvent(long BookId, int Page, long StartTime, double DurationSeconds, int? TotalPages)
{
    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(StartTime);
    public DateTimeOffset End => Start.AddSeconds(Math.Max(0, DurationSeconds));
}

public sealed record DailyStat(DateOnly Date, double Seconds, int Books, int Sessions);
public sealed record ChartPoint(string Label, double Value, string? Detail = null);
public sealed record ReadingSession(DateTimeOffset Start, DateTimeOffset End, double DurationSeconds, IReadOnlyList<long> BookIds, int EventCount)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}
public sealed record SessionDisplay(DateTimeOffset Start, DateTimeOffset End, double DurationSeconds, string Books, int EventCount, ReadingSession Source)
{
    public double ElapsedSeconds => Math.Max(0, (End - Start).TotalSeconds);
    public string DateLabel => Start.ToString("MMM d, yyyy");
}

public sealed record BookSummary(long Id, string Title, string Authors, double Seconds, int ActiveDays, DateTimeOffset? FirstRead, DateTimeOffset? LastRead, int EventCount, int DistinctPages, int? LatestPage, int? TotalPages, double Share);
public sealed record BookDetail(BookSummary Summary, int CurrentStreak, int LongestStreak, int Sessions, double AverageSessionSeconds, double LongestSessionSeconds, double MedianEventSeconds, IReadOnlyList<DailyStat> Daily, IReadOnlyList<ReadingSession> SessionHistory);
public sealed record PeriodComparison(double CurrentSeconds, double PreviousSeconds, double? PercentChange);
public sealed record ComparisonResult(double CurrentValue, double PreviousValue, double AbsoluteDelta, double? PercentDelta, bool HasBaseline);
public sealed record PersonalRecord(string Name, string Value, string Detail);
public sealed record OverviewStats(double TotalSeconds, double TodaySeconds, int CurrentStreak, int LongestStreak, int ActiveDays, int BooksRead, PeriodComparison Comparison, IReadOnlyList<DailyStat> Daily, IReadOnlyList<ChartPoint> Hourly, IReadOnlyList<ChartPoint> Weekdays, IReadOnlyList<PersonalRecord> Records, IReadOnlyList<string> Insights);
public sealed record SessionSummary(int Total, double AverageSeconds, double MedianSeconds, double LongestSeconds, int ThisWeek, double AveragePerActiveDay, IReadOnlyList<ReadingSession> Recent);
public sealed record DatabaseDiagnostics(string Path, long SizeBytes, int Books, int Events, DateTimeOffset? FirstEvent, DateTimeOffset? LatestEvent, string ConnectionState, int SchemaVersion, IReadOnlyList<string> Tables);
public sealed record SchemaValidation(bool IsValid, string Message, DatabaseDiagnostics? Diagnostics = null);

public sealed class AppSettings
{
    public string? DatabasePath { get; set; }
    public int SessionGapMinutes { get; set; } = 5;
    public bool WeekStartsMonday { get; set; } = true;
    public int DefaultRangeDays { get; set; } = 30;
    public bool AutoRefresh { get; set; } = true;
    public string Theme { get; set; } = "System";
    public double DailyGoalMinutes { get; set; } = 30;
    public double WeeklyGoalMinutes { get; set; } = 210;
    public double MonthlyGoalHours { get; set; } = 15;
    public List<GoalDefinition> Goals { get; set; } = [];
    public int GoalSchemaVersion { get; set; }
    public List<GoalArchive> GoalArchives { get; set; } = [];
    public int RefreshIntervalSeconds { get; set; }
    public int MinimumSessionSeconds { get; set; } = 1;
    public bool ExperimentalPageMetrics { get; set; }
    public bool CompactMode { get; set; }
    public bool Use24HourTime { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public Dictionary<string, BookTrackingState> BookTracking { get; set; } = [];
    public List<string> PinnedBookKeys { get; set; } = [];
    public bool AutomaticBackups { get; set; } = true;
}

public sealed class BookTrackingState
{
    public string Status { get; set; } = "Unspecified";
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public static class Formatters
{
    public static string Duration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (value.TotalSeconds < 60) return $"{Math.Round(value.TotalSeconds)}s";
        if (value.TotalMinutes < 10 && value.Seconds > 0) return $"{(int)value.TotalMinutes}m {value.Seconds}s";
        if (value.TotalMinutes < 60) return $"{Math.Round(value.TotalMinutes)}m";
        if (value.Minutes == 0) return $"{(int)value.TotalHours}h";
        return $"{(int)value.TotalHours}h {value.Minutes}m";
    }

    public static string Count(int value, string singular, string? plural = null) => $"{value} {(value == 1 ? singular : plural ?? singular + "s")}";
}
