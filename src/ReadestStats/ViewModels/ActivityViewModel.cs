using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class ActivityViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ActivityViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.MonthLabel)) Raise(nameof(MonthLabel));
            if (e.PropertyName == nameof(MainViewModel.MonthSummary)) Raise(nameof(MonthSummary));
            if (e.PropertyName == nameof(MainViewModel.SelectedDay)) Raise(nameof(SelectedDay));
            if (e.PropertyName == nameof(MainViewModel.SelectedAuditEvent)) Raise(nameof(SelectedAuditEvent));
            if (e.PropertyName == nameof(MainViewModel.AuditPreview)) Raise(nameof(AuditPreview));
            if (e.PropertyName == nameof(MainViewModel.AuditReason)) Raise(nameof(AuditReason));
        };
    }

    public MainViewModel Main => _main;
    public double ViewportWidth { get => _main.ViewportWidth; set => _main.ViewportWidth = value; }
    public ICommand PreviousMonthCommand => _main.PreviousMonthCommand;
    public string MonthLabel => _main.MonthLabel;
    public ICommand NextMonthCommand => _main.NextMonthCommand;
    public ICommand TodayCommand => _main.TodayCommand;
    public string MonthSummary => _main.MonthSummary;
    public ObservableCollection<ChartPoint> ActivityMonthTrend => _main.ActivityMonthTrend;
    public string[] WeekdayHeaders => _main.WeekdayHeaders;
    public ObservableCollection<CalendarDayDisplay> CalendarDays => _main.CalendarDays;
    public ICommand SelectDayCommand => _main.SelectDayCommand;
    public CalendarDayDisplay? SelectedDay => _main.SelectedDay;
    public ObservableCollection<ActivitySessionDisplay> DayTimeline => _main.DayTimeline;
    public ObservableCollection<BookSlice> DayBooks => _main.DayBooks;
    public ICommand NavigateCommand => _main.NavigateCommand;
    public ObservableCollection<AuditEventRow> DayAuditEvents => _main.DayAuditEvents;
    public AuditEventRow? SelectedAuditEvent { get => _main.SelectedAuditEvent; set => _main.SelectedAuditEvent = value; }
    public string AuditPreview => _main.AuditPreview;
    public string AuditReason { get => _main.AuditReason; set => _main.AuditReason = value; }
    public ICommand ToggleAuditEventCommand => _main.ToggleAuditEventCommand;
}
