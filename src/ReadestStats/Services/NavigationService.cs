namespace ReadestStats.Services;

public sealed class NavigationService : INavigationService
{
    private string _currentPage = "Overview";

    public string CurrentPage => _currentPage;

    public event EventHandler<string>? Navigated;
    public event EventHandler<long>? OpenBookRequested;
    public event EventHandler<DateOnly>? OpenCalendarDayRequested;

    public void NavigateTo(string page)
    {
        if (string.IsNullOrWhiteSpace(page) || _currentPage == page) return;
        _currentPage = page;
        Navigated?.Invoke(this, page);
    }

    public void OpenBook(long manualBookId)
    {
        OpenBookRequested?.Invoke(this, manualBookId);
    }

    public void OpenCalendarDay(DateOnly date)
    {
        OpenCalendarDayRequested?.Invoke(this, date);
    }
}
