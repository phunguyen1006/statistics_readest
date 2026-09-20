namespace ReadestStats.Core;

public sealed record Book(long Id, string Title, string Authors, long? LastOpen = null, int? Pages = null, string? Series = null, string? Language = null, string? Hash = null, string Source = "Readest", string? CoverUrl = null);
public sealed record ReadingEvent(long BookId, int Page, long StartTime, double DurationSeconds, int? TotalPages, string Source = "Readest")
{
    public DateTimeOffset Start => DateTimeOffset.FromUnixTimeSeconds(StartTime);
    public DateTimeOffset End => Start.AddSeconds(Math.Max(0, DurationSeconds));
}

public sealed record DailyStat(DateOnly Date, double Seconds, int Books, int Sessions);
public sealed record ChartPoint(string Label, double Value, string? Detail = null, string? Unit = null);
public sealed record ReadingSession(DateTimeOffset Start, DateTimeOffset End, double DurationSeconds, IReadOnlyList<long> BookIds, int EventCount, IReadOnlyList<string>? Sources = null)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
}
public sealed record SessionDisplay(DateTimeOffset Start, DateTimeOffset End, double DurationSeconds, string Books, int EventCount, ReadingSession Source, string SourceLabel = "Readest", string PageRangeLabel = "—")
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
    public string DefaultRangePreset { get; set; } = "30 days";
    public DateOnly? CustomRangeStart { get; set; }
    public DateOnly? CustomRangeEnd { get; set; }
    public string TrendMetric { get; set; } = "Reading time";
    public string TrendGranularity { get; set; } = "Auto";
    public string CompareMode { get; set; } = "Previous period";
    public DateOnly? CompareCustomStart { get; set; }
    public DateOnly? CompareCustomEnd { get; set; }
    public string DefaultSourceFilter { get; set; } = "All sources";
    public string? GoogleBooksApiKey { get; set; }
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
    public List<BookEditionLink> BookLinks { get; set; } = [];
    public int LibrarySchemaVersion { get; set; } = 2;
    public List<string> PinnedBookKeys { get; set; } = [];
    public bool AutomaticBackups { get; set; } = true;
    public int BackupRetentionDays { get; set; } = 7;
    public string Language { get; set; } = "System";
    /// <summary>Statistics-owned note curation. Readest source files remain read-only.</summary>
    public Dictionary<string, NoteUserState> NoteStates { get; set; } = [];
    public int NoteStateSchemaVersion { get; set; } = 3;
}

public sealed class BookTrackingState
{
    public string Status { get; set; } = "Unspecified";
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public ReadingPlan? Plan { get; set; }
    public List<ReadingCycle> Cycles { get; set; } = [];
    public string? CurrentCycleId { get; set; }
}

public sealed class ReadingCycle
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Number { get; set; } = 1;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string Label => Number == 1 ? "First read" : $"Re-read {Number - 1}";
}

public sealed class ReadingPlan
{
    public bool Enabled { get; set; }
    public bool IsPaused { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetDate { get; set; }
    public double DailyMinutes { get; set; }
    public int DailyPages { get; set; }
    public bool IncludeWeekends { get; set; } = true;
    public List<DayOfWeek> ReadingDays { get; set; } = [];
    public List<DateOnly> SkippedDates { get; set; } = [];
    public int Priority { get; set; } = 2;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BookEditionLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ReadestKey { get; set; } = "";
    public string ManualKey { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ReadingPlanProgress(
    bool Enabled,
    string Status,
    string Summary,
    double Actual,
    double Required,
    double Target,
    double Remaining,
    double RequiredPerReadingDay,
    DateOnly? ProjectedFinish,
    string Unit);

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
