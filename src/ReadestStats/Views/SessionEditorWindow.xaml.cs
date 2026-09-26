using System.Globalization;
using System.Windows;
using ReadestStats.Core;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class SessionEditorWindow : Window
{
    private readonly ManualLogViewModel _viewModel;
    private readonly ManualSessionRow? _row;

    public SessionEditorWindow(ManualLogViewModel viewModel, ManualSessionRow? row = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _row = row;
        BookInput.ItemsSource = viewModel.Books;
        BookInput.SelectedItem = row is null ? viewModel.SelectedBook ?? viewModel.Books.FirstOrDefault() : viewModel.FindBook(row.BookId);
        DateInput.SelectedDate = row?.StartedAt.LocalDateTime.Date ?? DateTime.Today;
        TimeInput.Text = (row?.StartedAt.LocalDateTime ?? DateTime.Now).ToString("HH:mm");
        DurationInput.Text = row is null ? "30" : Math.Max(1, Math.Round(row.DurationSeconds / 60)).ToString(CultureInfo.InvariantCulture);
        StartPageInput.Text = row?.StartPage?.ToString() ?? ((viewModel.SelectedBook?.CurrentPage ?? 0) + 1).ToString();
        EndPageInput.Text = row?.EndPage?.ToString() ?? "";
        NoteInput.Text = row is null ? "" : viewModel.FindSession(row.Id)?.Note ?? "";
        if (row is not null) Heading.Text = ReadestStats.Localization.Localizer.Instance.Translate("Edit manual session");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("");
        if (BookInput.SelectedItem is not ManualBook book) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Choose a physical book."); return; }
        if (DateInput.SelectedDate is not { } date || !TimeSpan.TryParse(TimeInput.Text, CultureInfo.CurrentCulture, out var time)) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Enter a valid date and start time."); return; }
        if (!double.TryParse(DurationInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes) || !int.TryParse(StartPageInput.Text, out var startPage) || !int.TryParse(EndPageInput.Text, out var endPage)) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Duration and pages must be numbers."); return; }
        SaveButton.IsEnabled = false;
        var startedAt = new DateTimeOffset(date.Date.Add(time), TimeZoneInfo.Local.GetUtcOffset(date.Date.Add(time)));
        var error = await _viewModel.SaveHistoricalSessionAsync(_row?.Id, book.Id, startedAt, minutes, startPage, endPage, NoteInput.Text);
        SaveButton.IsEnabled = true;
        if (error is not null) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate(error); return; }
        DialogResult = true;
    }
}
