using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class OverviewViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Period)) Raise(nameof(Period));
            if (e.PropertyName == nameof(MainViewModel.WeeklyRecap)) Raise(nameof(WeeklyRecap));
            if (e.PropertyName == nameof(MainViewModel.Comparison)) Raise(nameof(Comparison));
            if (e.PropertyName == nameof(MainViewModel.ComparisonTrend)) Raise(nameof(ComparisonTrend));
            if (e.PropertyName == nameof(MainViewModel.PeriodDumbbells)) Raise(nameof(PeriodDumbbells));
            if (e.PropertyName == nameof(MainViewModel.NoPeriodData)) Raise(nameof(NoPeriodData));
            if (e.PropertyName == nameof(MainViewModel.ReadingPeriodSummary)) Raise(nameof(ReadingPeriodSummary));
            if (e.PropertyName == nameof(MainViewModel.ComparisonText)) Raise(nameof(ComparisonText));
            if (e.PropertyName == nameof(MainViewModel.CurrentStreakLabel)) Raise(nameof(CurrentStreakLabel));
            if (e.PropertyName == nameof(MainViewModel.LongestStreakLabel)) Raise(nameof(LongestStreakLabel));
            if (e.PropertyName == nameof(MainViewModel.TopBookLabel)) Raise(nameof(TopBookLabel));
            if (e.PropertyName == nameof(MainViewModel.MedianSessionLabel)) Raise(nameof(MedianSessionLabel));
        };
    }

    public MainViewModel Main => _main;
    public PeriodMetrics? Period => _main.Period;
    public bool NoPeriodData => _main.NoPeriodData;
    public string ReadingPeriodSummary => _main.ReadingPeriodSummary;
    public string ComparisonText => _main.ComparisonText;
    public string CurrentStreakLabel => _main.CurrentStreakLabel;
    public string LongestStreakLabel => _main.LongestStreakLabel;
    public string TopBookLabel => _main.TopBookLabel;
    public string MedianSessionLabel => _main.MedianSessionLabel;
    public ObservableCollection<ChartPoint> WeeklyRecapDays => _main.WeeklyRecapDays;
    public WeeklyRecap? WeeklyRecap => _main.WeeklyRecap;
    public ICommand ExportWeeklyRecapCommand => _main.ExportWeeklyRecapCommand;
    public ObservableCollection<ChartPoint> Trend => _main.Trend;
    public ComparisonResult? Comparison => _main.Comparison;
    public ObservableCollection<ChartPoint> ComparisonTrend => _main.ComparisonTrend;
    public ObservableCollection<ChartPoint> ComparisonData => _main.ComparisonData;
    public ObservableCollection<DumbbellDatum> PeriodDumbbells => _main.PeriodDumbbells;
    public ObservableCollection<InsightItem> QuickInsights => _main.QuickInsights;
    public ObservableCollection<BookFocusItem> ContinueReading => _main.ContinueReading;
    public ICommand OpenInsightCommand => _main.OpenInsightCommand;
    public ICommand NavigateCommand => _main.NavigateCommand;
}
