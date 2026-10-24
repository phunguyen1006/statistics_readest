using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class StatisticsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public StatisticsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Period)) Raise(nameof(Period));
            if (e.PropertyName == nameof(MainViewModel.Comparison)) Raise(nameof(Comparison));
            if (e.PropertyName == nameof(MainViewModel.MonthlyReview)) Raise(nameof(MonthlyReview));
            if (e.PropertyName == nameof(MainViewModel.MonthlyReviewHeading)) Raise(nameof(MonthlyReviewHeading));
            if (e.PropertyName == nameof(MainViewModel.MonthlyReviewSummary)) Raise(nameof(MonthlyReviewSummary));
            if (e.PropertyName == nameof(MainViewModel.MonthlyReviewExactLabel)) Raise(nameof(MonthlyReviewExactLabel));
            if (e.PropertyName == nameof(MainViewModel.ReportMonth)) Raise(nameof(ReportMonth));
            if (e.PropertyName == nameof(MainViewModel.TrendSummary)) Raise(nameof(TrendSummary));
            if (e.PropertyName == nameof(MainViewModel.PatternSummary)) Raise(nameof(PatternSummary));
            if (e.PropertyName == nameof(MainViewModel.Story)) Raise(nameof(Story));
            if (e.PropertyName == nameof(MainViewModel.NoPeriodData)) Raise(nameof(NoPeriodData));
        };
    }

    public MainViewModel Main => _main;
    public bool NoPeriodData => _main.NoPeriodData;
    public PeriodMetrics? Period => _main.Period;
    public ComparisonResult? Comparison => _main.Comparison;
    public MonthlyReview? MonthlyReview => _main.MonthlyReview;
    public string ReadingPeriodSummary => _main.ReadingPeriodSummary;
    public string ComparisonText => _main.ComparisonText;
    public string EligibleDaysLabel => _main.EligibleDaysLabel;
    public string CurrentStreakLabel => _main.CurrentStreakLabel;
    public string LongestStreakLabel => _main.LongestStreakLabel;
    public string MedianSessionLabel => _main.MedianSessionLabel;
    public string TopBookLabel => _main.TopBookLabel;
    public YearBookGoalSummary? CurrentYearBookGoal => _main.CurrentYearBookGoal;
    public string MonthlyReviewEyebrow => _main.MonthlyReviewEyebrow;
    public string MonthlyReviewHeading => _main.MonthlyReviewHeading;
    public string MonthlyPreviousLabel => _main.MonthlyPreviousLabel;
    public ICommand PreviousReviewMonthCommand => _main.PreviousReviewMonthCommand;
    public DateTime? ReportMonth { get => _main.ReportMonth; set => _main.ReportMonth = value; }
    public DateTime EarliestReportDate => _main.EarliestReportDate;
    public DateTime LatestReportDate => _main.LatestReportDate;
    public string MonthlyReviewPickerHint => _main.MonthlyReviewPickerHint;
    public string MonthlyNextLabel => _main.MonthlyNextLabel;
    public ICommand NextReviewMonthCommand => _main.NextReviewMonthCommand;
    public string MonthlyCurrentLabel => _main.MonthlyCurrentLabel;
    public ICommand CurrentReviewMonthCommand => _main.CurrentReviewMonthCommand;
    public string MonthlyReviewExportLabel => _main.MonthlyReviewExportLabel;
    public ICommand ExportMonthlyReportCommand => _main.ExportMonthlyReportCommand;
    public string MonthlyReviewSummary => _main.MonthlyReviewSummary;
    public string MonthlyReviewComparison => _main.MonthlyReviewComparison;
    public string MonthlyReviewContext => _main.MonthlyReviewContext;
    public string MonthlyReviewCoverage => _main.MonthlyReviewCoverage;
    public ObservableCollection<ChartPoint> MonthlyReviewDays => _main.MonthlyReviewDays;
    public string MonthlyReviewExactLabel => _main.MonthlyReviewExactLabel;
    public string MonthlyDayHint => _main.MonthlyDayHint;
    public ObservableCollection<DailyStat> MonthlyReviewDaily => _main.MonthlyReviewDaily;
    public string MonthlyOpenDayLabel => _main.MonthlyOpenDayLabel;
    public ICommand OpenMonthlyDayCommand => _main.OpenMonthlyDayCommand;
    public ReadingStory? Story => _main.Story;
    public ObservableCollection<ChartPoint> Heatmap => _main.Heatmap;
    public ICommand SelectHeatmapDayCommand => _main.SelectHeatmapDayCommand;
    public ObservableCollection<ChartPoint> StreakTimeline => _main.StreakTimeline;
    public string[] PatternMetrics => _main.PatternMetrics;
    public string PatternMetric { get => _main.PatternMetric; set => _main.PatternMetric = value; }
    public string PatternSummary => _main.PatternSummary;
    public ObservableCollection<MatrixCell> WeekHourMatrix => _main.WeekHourMatrix;
    public string[] TrendMetrics => _main.TrendMetrics;
    public string TrendMetric { get => _main.TrendMetric; set => _main.TrendMetric = value; }
    public string[] Granularities => _main.Granularities;
    public string TrendGranularity { get => _main.TrendGranularity; set => _main.TrendGranularity = value; }
    public string[] CompareModes => _main.CompareModes;
    public string CompareMode { get => _main.CompareMode; set => _main.CompareMode = value; }
    public string TrendSummary => _main.TrendSummary;
    public string ComparisonLegend => _main.ComparisonLegend;
    public bool IsCustomComparison => _main.IsCustomComparison;
    public DateTime? CompareCustomStart { get => _main.CompareCustomStart; set => _main.CompareCustomStart = value; }
    public DateTime? CompareCustomEnd { get => _main.CompareCustomEnd; set => _main.CompareCustomEnd = value; }
    public ObservableCollection<ChartPoint> Trend => _main.Trend;
    public ObservableCollection<ChartPoint> ComparisonTrend => _main.ComparisonTrend;
    public ObservableCollection<ChartPoint> ComparisonData => _main.ComparisonData;
    public ObservableCollection<ChartPoint> RollingMomentum => _main.RollingMomentum;
    public ObservableCollection<ChartPoint> SessionStaircase => _main.SessionStaircase;
    public ObservableCollection<ChartPoint> MonthlyJourney => _main.MonthlyJourney;
    public string ComparisonPeriodLabel => _main.ComparisonPeriodLabel;
    public ObservableCollection<DumbbellDatum> PeriodDumbbells => _main.PeriodDumbbells;
    public ObservableCollection<DotDatum> SessionDots => _main.SessionDots;
    public SessionProfile? SessionProfile => _main.SessionProfile;
    public ObservableCollection<BookRow> StatisticsTopBooks => _main.StatisticsTopBooks;
    public ObservableCollection<DotDatum> ReadingStyleDays => _main.ReadingStyleDays;
    public ObservableCollection<CompositionPart> BookAttention => _main.BookAttention;
    public string AuthorCoverage => _main.AuthorCoverage;
    public ObservableCollection<MetadataBreakdown> AuthorBreakdown => _main.AuthorBreakdown;
    public string LanguageCoverage => _main.LanguageCoverage;
    public ObservableCollection<MetadataBreakdown> LanguageBreakdown => _main.LanguageBreakdown;
    public ObservableCollection<PersonalRecord> Records => _main.Records;
}
