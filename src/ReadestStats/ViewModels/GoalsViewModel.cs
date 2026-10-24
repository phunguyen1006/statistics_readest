using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class GoalsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public GoalsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentYearBookGoal)) Raise(nameof(CurrentYearBookGoal));
            if (e.PropertyName == nameof(MainViewModel.GoalPace)) Raise(nameof(GoalPace));
            if (e.PropertyName == nameof(MainViewModel.HasActiveReadingPlans)) Raise(nameof(HasActiveReadingPlans));
        };
    }

    public MainViewModel Main => _main;
    public ICommand SaveGoalsCommand => _main.SaveGoalsCommand;
    public YearBookGoalSummary? CurrentYearBookGoal => _main.CurrentYearBookGoal;
    public ObservableCollection<PaceDatum> GoalPace => _main.GoalPace;
    public bool HasActiveReadingPlans => _main.HasActiveReadingPlans;
    public ObservableCollection<BookFocusItem> ActiveReadingPlans => _main.ActiveReadingPlans;
    public ICommand OpenFocusBookCommand => _main.OpenFocusBookCommand;
    public ObservableCollection<GoalEditor> Goals => _main.Goals;
    public ObservableCollection<GoalArchive> GoalArchives => _main.GoalArchives;
}
