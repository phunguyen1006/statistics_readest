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
    public double ViewportWidth { get => _main.ViewportWidth; set => _main.ViewportWidth = value; }
    public ICommand SaveGoalsCommand => _main.SaveGoalsCommand;
    public YearBookGoal CurrentYearBookGoal => _main.CurrentYearBookGoal;
    public string GoalPace => _main.GoalPace;
    public bool HasActiveReadingPlans => _main.HasActiveReadingPlans;
    public ObservableCollection<ActiveReadingPlanItem> ActiveReadingPlans => _main.ActiveReadingPlans;
    public ICommand OpenFocusBookCommand => _main.OpenFocusBookCommand;
    public ObservableCollection<GoalEditorItem> Goals => _main.Goals;
    public ObservableCollection<GoalArchiveItem> GoalArchives => _main.GoalArchives;
}
