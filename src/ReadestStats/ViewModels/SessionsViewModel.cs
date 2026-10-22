using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class SessionsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SessionsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Period)) Raise(nameof(Period));
            if (e.PropertyName == nameof(MainViewModel.SessionProfile)) Raise(nameof(SessionProfile));
            if (e.PropertyName == nameof(MainViewModel.SessionsPerActiveDay)) Raise(nameof(SessionsPerActiveDay));
            if (e.PropertyName == nameof(MainViewModel.SelectedSession)) Raise(nameof(SelectedSession));
        };
    }

    public MainViewModel Main => _main;
    public ReadingPeriod? Period => _main.Period;
    public SessionDistribution SessionProfile => _main.SessionProfile;
    public string SessionsPerActiveDay => _main.SessionsPerActiveDay;
    public ObservableCollection<string> SessionBookOptions => _main.SessionBookOptions;
    public string SessionBook { get => _main.SessionBook; set => _main.SessionBook = value; }
    public string[] SessionDurations => _main.SessionDurations;
    public string SessionDuration { get => _main.SessionDuration; set => _main.SessionDuration = value; }
    public string SessionSearch { get => _main.SessionSearch; set => _main.SessionSearch = value; }
    public string[] SessionSorts => _main.SessionSorts;
    public string SessionSort { get => _main.SessionSort; set => _main.SessionSort = value; }
    public ObservableCollection<ChartPoint> SessionDots => _main.SessionDots;
    public ObservableCollection<ChartPoint> ReadingStyleDays => _main.ReadingStyleDays;
    public ObservableCollection<SessionDisplay> Sessions => _main.Sessions;
    public SessionDisplay? SelectedSession { get => _main.SelectedSession; set => _main.SelectedSession = value; }
    public ObservableCollection<SessionTimelineSegment> SessionTimeline => _main.SessionTimeline;
    public ICommand OpenSessionBookCommand => _main.OpenSessionBookCommand;
}
