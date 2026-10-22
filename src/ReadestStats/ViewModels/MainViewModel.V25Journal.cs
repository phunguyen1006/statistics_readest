using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class MainViewModel
{
    private void OpenJournalBook(long manualBookId)
    {
        if (!CanInteract || Manual.FindBook(manualBookId) is null) return;
        // A digital-only filter would hide the target physical edition.
        if (SelectedSource == "Readest") SelectedSource = "Manual";
        _bookFilter = "All";
        _bookSearch = "";
        Raise(nameof(BookFilter)); Raise(nameof(BookSearch));
        ApplyBookFilters();
        var match = Books.FirstOrDefault(book => book.Id == manualBookId)
            ?? Books.FirstOrDefault(book => ManualBookFor(book.Id)?.Id == manualBookId);
        if (match is null) return;
        SelectedBook = match;
        SelectedPage = "Books";
    }

    private void OpenJournalDay(DateOnly date)
    {
        if (!CanInteract) return;
        if (SelectedSource == "Readest") SelectedSource = "Manual";
        Month = new(date.Year, date.Month, 1);
        SelectedDate = date;
        BuildActivity();
        SelectedPage = "Activity";
    }
}
