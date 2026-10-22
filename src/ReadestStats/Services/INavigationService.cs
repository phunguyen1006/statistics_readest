namespace ReadestStats.Services;

public interface INavigationService
{
    string CurrentPage { get; }
    void NavigateTo(string page);
    void OpenBook(long manualBookId);
    void OpenCalendarDay(DateOnly date);
    event EventHandler<string>? Navigated;
    event EventHandler<long>? OpenBookRequested;
    event EventHandler<DateOnly>? OpenCalendarDayRequested;
}
