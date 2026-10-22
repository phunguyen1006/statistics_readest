using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class BooksViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public BooksViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedBook)) Raise(nameof(SelectedBook));
            if (e.PropertyName == nameof(MainViewModel.SelectedBookStatus)) Raise(nameof(SelectedBookStatus));
            if (e.PropertyName == nameof(MainViewModel.SelectedBookPlan)) Raise(nameof(SelectedBookPlan));
            if (e.PropertyName == nameof(MainViewModel.BookDetail)) Raise(nameof(BookDetail));
            if (e.PropertyName == nameof(MainViewModel.SelectedBookCycles)) Raise(nameof(SelectedBookCycles));
            if (e.PropertyName == nameof(MainViewModel.SelectedBookCycleSummary)) Raise(nameof(SelectedBookCycleSummary));
            if (e.PropertyName == nameof(MainViewModel.LinkEditionLabel)) Raise(nameof(LinkEditionLabel));
            if (e.PropertyName == nameof(MainViewModel.PinBookLabel)) Raise(nameof(PinBookLabel));
            if (e.PropertyName == nameof(MainViewModel.ReadingPlanPauseLabel)) Raise(nameof(ReadingPlanPauseLabel));
            if (e.PropertyName == nameof(MainViewModel.SelectedPlanTodayLabel)) Raise(nameof(SelectedPlanTodayLabel));
            if (e.PropertyName == nameof(MainViewModel.SelectedPlanAdherence)) Raise(nameof(SelectedPlanAdherence));
        };
    }

    public MainViewModel Main => _main;
    public string BookSearch { get => _main.BookSearch; set => _main.BookSearch = value; }
    public string[] BookFilters => _main.BookFilters;
    public string BookFilter { get => _main.BookFilter; set => _main.BookFilter = value; }
    public string[] BookSorts => _main.BookSorts;
    public string BookSort { get => _main.BookSort; set => _main.BookSort = value; }
    public ObservableCollection<ChartPoint> BookAttention => _main.BookAttention;
    public ObservableCollection<ChartPoint> BookDayMatrix => _main.BookDayMatrix;
    public ObservableCollection<BookSlice> Books => _main.Books;
    public BookSlice? SelectedBook { get => _main.SelectedBook; set => _main.SelectedBook = value; }
    public string SelectedBookOpenLabel => _main.SelectedBookOpenLabel;
    public ICommand OpenBookInReadestCommand => _main.OpenBookInReadestCommand;
    public ICommand StartSelectedManualSessionCommand => _main.StartSelectedManualSessionCommand;
    public string[] BookStatusOptions => _main.BookStatusOptions;
    public string SelectedBookStatus { get => _main.SelectedBookStatus; set => _main.SelectedBookStatus = value; }
    public string PinBookLabel => _main.PinBookLabel;
    public ICommand TogglePinnedBookCommand => _main.TogglePinnedBookCommand;
    public ICommand StartRereadCommand => _main.StartRereadCommand;
    public string LinkEditionLabel => _main.LinkEditionLabel;
    public ICommand LinkSelectedEditionCommand => _main.LinkSelectedEditionCommand;
    public BookMetadataResult? LinkCandidate => _main.LinkCandidate;
    public ICommand UnlinkSelectedEditionCommand => _main.UnlinkSelectedEditionCommand;
    public bool IsSelectedBookLinked => _main.IsSelectedBookLinked;
    public ICommand RefreshSelectedMetadataCommand => _main.RefreshSelectedMetadataCommand;
    public DateTime? SelectedBookCompletedDate { get => _main.SelectedBookCompletedDate; set => _main.SelectedBookCompletedDate = value; }
    public string OpenBookStatus => _main.OpenBookStatus;
    public string SelectedBookCycleSummary => _main.SelectedBookCycleSummary;
    public ObservableCollection<ReadingCycleItem> SelectedBookCycles => _main.SelectedBookCycles;
    public ICommand SkipTodayPlanCommand => _main.SkipTodayPlanCommand;
    public string SelectedPlanTodayLabel => _main.SelectedPlanTodayLabel;
    public string SelectedPlanAdherence => _main.SelectedPlanAdherence;
    public ReadingPlanProgress SelectedBookPlan => _main.SelectedBookPlan;
    public string SelectedBookPlanGoal => _main.SelectedBookPlanGoal;
    public DateTime? SelectedPlanStartDate { get => _main.SelectedPlanStartDate; set => _main.SelectedPlanStartDate = value; }
    public DateTime? SelectedPlanTargetDate { get => _main.SelectedPlanTargetDate; set => _main.SelectedPlanTargetDate = value; }
    public int SelectedPlanDailyMinutes { get => _main.SelectedPlanDailyMinutes; set => _main.SelectedPlanDailyMinutes = value; }
    public int SelectedPlanDailyPages { get => _main.SelectedPlanDailyPages; set => _main.SelectedPlanDailyPages = value; }
    public bool SelectedPlanUsesPages { get => _main.SelectedPlanUsesPages; set => _main.SelectedPlanUsesPages = value; }
    public bool SelectedPlanMonday { get => _main.SelectedPlanMonday; set => _main.SelectedPlanMonday = value; }
    public bool SelectedPlanTuesday { get => _main.SelectedPlanTuesday; set => _main.SelectedPlanTuesday = value; }
    public bool SelectedPlanWednesday { get => _main.SelectedPlanWednesday; set => _main.SelectedPlanWednesday = value; }
    public bool SelectedPlanThursday { get => _main.SelectedPlanThursday; set => _main.SelectedPlanThursday = value; }
    public bool SelectedPlanFriday { get => _main.SelectedPlanFriday; set => _main.SelectedPlanFriday = value; }
    public bool SelectedPlanSaturday { get => _main.SelectedPlanSaturday; set => _main.SelectedPlanSaturday = value; }
    public bool SelectedPlanSunday { get => _main.SelectedPlanSunday; set => _main.SelectedPlanSunday = value; }
    public string SelectedPlanPriority { get => _main.SelectedPlanPriority; set => _main.SelectedPlanPriority = value; }
    public string ReadingPlanPauseLabel => _main.ReadingPlanPauseLabel;
    public ICommand ToggleReadingPlanPauseCommand => _main.ToggleReadingPlanPauseCommand;
    public ICommand RemoveReadingPlanCommand => _main.RemoveReadingPlanCommand;
    public ICommand SaveReadingPlanCommand => _main.SaveReadingPlanCommand;
    public BookAnalyticsDetail BookDetail => _main.BookDetail;
    public ObservableCollection<ChartPoint> BookTrend => _main.BookTrend;
    public ObservableCollection<ChartPoint> BookHourly => _main.BookHourly;
    public ObservableCollection<ChartPoint> BookWeekdays => _main.BookWeekdays;
    public ObservableCollection<SessionDisplay> BookSessions => _main.BookSessions;
}
