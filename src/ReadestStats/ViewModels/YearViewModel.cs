using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class YearViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public YearViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Year)) Raise(nameof(Year));
            if (e.PropertyName == nameof(MainViewModel.YearSummary)) Raise(nameof(YearSummary));
            if (e.PropertyName == nameof(MainViewModel.YearBookGoal)) Raise(nameof(YearBookGoal));
            if (e.PropertyName == nameof(MainViewModel.YearLongestStreakLabel)) Raise(nameof(YearLongestStreakLabel));
            if (e.PropertyName == nameof(MainViewModel.YearTopBook)) Raise(nameof(YearTopBook));
            if (e.PropertyName == nameof(MainViewModel.YearBestMonth)) Raise(nameof(YearBestMonth));
            if (e.PropertyName == nameof(MainViewModel.YearFavoriteHour)) Raise(nameof(YearFavoriteHour));
        };
    }

    public MainViewModel Main => _main;
    public double ViewportWidth { get => _main.ViewportWidth; set => _main.ViewportWidth = value; }
    public ICommand PreviousYearCommand => _main.PreviousYearCommand;
    public int Year { get => _main.Year; set => _main.Year = value; }
    public ICommand NextYearCommand => _main.NextYearCommand;
    public YearSummary YearSummary => _main.YearSummary;
    public string YearLongestStreakLabel => _main.YearLongestStreakLabel;
    public YearBookGoal YearBookGoal => _main.YearBookGoal;
    public ObservableCollection<ChartPoint> YearHeatmap => _main.YearHeatmap;
    public ObservableCollection<ChartPoint> YearMonths => _main.YearMonths;
    public ObservableCollection<ChartPoint> BestReadingDays => _main.BestReadingDays;
    public ObservableCollection<ChartPoint> YearBookAttention => _main.YearBookAttention;
    public string YearTopBook => _main.YearTopBook;
    public string YearBestMonth => _main.YearBestMonth;
    public string YearFavoriteHour => _main.YearFavoriteHour;
}
