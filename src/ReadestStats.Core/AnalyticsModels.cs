namespace ReadestStats.Core;

public sealed record PeriodMetrics(double TotalSeconds, int ActiveDays, int AvailableDays, int SessionCount, int ActiveBooks, double AveragePerActiveDay, double AveragePerCalendarDay, double AverageSessionSeconds, double ConsistencyPercent);
public sealed record SessionProfile(double Average, double Median, double Longest, double Shortest, double P25, double P75, IReadOnlyList<ChartPoint> Histogram);
public sealed record WeekdayWeekendStats(double WeekdaySeconds, double WeekendSeconds, double AverageWeekdaySeconds, double AverageWeekendSeconds, string Summary);
public sealed record ReadingWindow(int StartHour, int Hours, double Seconds, double Share);
public sealed record InsightItem(string Id, int Priority, string Category, string Title, string Description, string SampleStatus = "Strong sample");
public sealed record CalendarDayItem(DateOnly Date, bool IsInMonth, double Seconds, int Sessions, int Books, string Intensity, string DurationLabel);
public sealed record DayDetails(DateOnly Date, double Seconds, int Sessions, int Books, DateTimeOffset? FirstSession, DateTimeOffset? LastSession, IReadOnlyList<SessionDisplay> SessionItems, IReadOnlyList<ChartPoint> BookItems);
public sealed record YearInReading(int Year, double TotalSeconds, int ActiveDays, int Sessions, int ActiveBooks, int LongestStreak, DailyStat? BestDay, ChartPoint? BestMonth, BookSummary? TopBook, ChartPoint? FavoriteHour, ChartPoint? FavoriteWeekday, IReadOnlyList<ChartPoint> Months, IReadOnlyList<ChartPoint> Heatmap, IReadOnlyList<BookSummary> TopBooks);

public enum GoalPeriod { Daily, Weekly, Monthly, Yearly }
public enum GoalMetric { ReadingTime, ActiveDays, Sessions, Books }
public sealed class GoalDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public GoalPeriod Period { get; set; }
    public GoalMetric Metric { get; set; } = GoalMetric.ReadingTime;
    public double TargetValue { get; set; }
    public bool Enabled { get; set; } = true;
}
public sealed record GoalProgress(GoalDefinition Definition, double Current, double Target, double ProgressPercent, double ElapsedPercent, double Remaining, double RequiredPerDay, double? Projected, string Status, string CurrentLabel, string TargetLabel, string RemainingLabel, string PaceLabel, string ProjectionLabel);
public sealed record GoalHistory(string Label, int Hit, int Total, double HitRate, int BestStreak, IReadOnlyList<ChartPoint> Points);
public sealed class GoalArchive
{
    public int Year { get; set; }
    public double TargetBooks { get; set; }
    public int BooksRead { get; set; }
    public DateTimeOffset ArchivedAtUtc { get; set; }
    public double ProgressPercent => TargetBooks <= 0 ? 0 : Math.Min(100, BooksRead * 100d / TargetBooks);
    public string ResultLabel => $"{Formatters.Count(BooksRead, "book")} / {TargetBooks:0.#} goal";
    public string Status => TargetBooks > 0 && BooksRead >= TargetBooks ? "Goal reached" : "Goal not reached";
}
public sealed record YearBookGoalSummary(int Year, int BooksRead, double TargetBooks, double ProgressPercent, string Status, string Detail);
public sealed record BookRow(BookSummary Summary, int Sessions, double AverageSessionSeconds, string TypicalHour)
{
    public long Id => Summary.Id;
    public string Title => Summary.Title;
    public string Authors => Summary.Authors;
    public double Seconds => Summary.Seconds;
    public int ActiveDays => Summary.ActiveDays;
    public DateTimeOffset? LastRead => Summary.LastRead;
    public double Share => Summary.Share;
}
