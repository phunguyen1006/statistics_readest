using System.Globalization;
using System.Windows;
using ReadestStats.Core;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class SessionEditorWindow : Window
{
    private readonly ManualLogViewModel _viewModel;
    private readonly ManualSessionRow? _row;
    private readonly ManualReadingSession? _original;
    private readonly DateTime _initialLocalStart;
    private readonly double _initialMinutes;
    private readonly double _initialDisplayedMinutes;

    public SessionEditorWindow(ManualLogViewModel viewModel, ManualSessionRow? row = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _row = row;
        _original = row is null ? null : viewModel.FindSession(row.Id);
        _initialLocalStart = _original?.StartedAtUtc.LocalDateTime ?? DateTime.Now.AddMinutes(-30);
        _initialMinutes = (_original?.DurationSeconds ?? 1800) / 60;
        DataContext = this;
        var choices = viewModel.Books.ToList();
        var selectedBook = row is null ? viewModel.SelectedBook ?? choices.FirstOrDefault() : viewModel.FindBook(row.BookId);
        if (selectedBook is not null && choices.All(book => book.Id != selectedBook.Id)) choices.Add(selectedBook);
        BookInput.ItemsSource = choices;
        BookInput.SelectedItem = selectedBook;
        DateInput.SelectedDate = _initialLocalStart.Date;
        RecordedDateInput.Text = _initialLocalStart.ToString("d", CultureInfo.CurrentCulture);
        TimeInput.Text = _initialLocalStart.ToString("HH:mm:ss.FFFFFFF", CultureInfo.CurrentCulture);
        DurationInput.Text = _initialMinutes.ToString("0.########", CultureInfo.CurrentCulture);
        _initialDisplayedMinutes = double.Parse(DurationInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture);
        StartPageInput.Text = row?.StartPage?.ToString() ?? ((viewModel.SelectedBook?.CurrentPage ?? 0) + 1).ToString();
        EndPageInput.Text = row?.EndPage?.ToString() ?? "";
        NoteInput.Text = row is null ? "" : viewModel.FindSession(row.Id)?.Note ?? "";
        if (row is not null) Heading.Text = ReadestStats.Localization.Localizer.Instance.Translate("Edit manual session");
        EditTimingInput.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateTimingControls();
    }

    public string EditTimingLabel => Text("Edit recorded time explicitly", "Chủ động sửa thời gian đã ghi");
    public string TimingEditHint => _row is null
        ? Text("Enter actual reading time. Start time accepts HH:mm or HH:mm:ss.", "Nhập thời gian đọc thực. Giờ bắt đầu nhận HH:mm hoặc HH:mm:ss.")
        : Text("Pages, book and note edits keep exact seconds and pause gaps. Opt in above to shift the start and scale reading segments and pauses proportionally.",
            "Sửa trang, sách hoặc ghi chú giữ nguyên giây chính xác và khoảng tạm dừng. Chọn mục trên để đổi giờ bắt đầu và co giãn các đoạn đọc cùng khoảng nghỉ theo tỷ lệ.");
    public string StartTimeHint => Text("24-hour time: HH:mm:ss (fractional seconds are optional)", "Giờ 24 tiếng: HH:mm:ss (phần lẻ của giây là tùy chọn)");
    private static string Text(string english, string vietnamese) => ReadestStats.Localization.Localizer.Instance.IsVietnamese ? vietnamese : english;

    private void EditTiming_Changed(object sender, RoutedEventArgs e) => UpdateTimingControls();
    private void UpdateTimingControls()
    {
        if (DateInput is null || RecordedDateInput is null || TimeInput is null || DurationInput is null) return;
        var editable = _row is null || EditTimingInput.IsChecked == true;
        DateInput.IsEnabled = editable;
        DateInput.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        RecordedDateInput.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        TimeInput.IsReadOnly = !editable;
        DurationInput.IsReadOnly = !editable;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
        => await SaveAsync();

    private async Task SaveAsync()
    {
        ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("");
        if (BookInput.SelectedItem is not ManualBook book) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Choose a physical book."); return; }
        if (!int.TryParse(StartPageInput.Text, out var startPage) || !int.TryParse(EndPageInput.Text, out var endPage))
        { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Duration and pages must be numbers."); StartPageInput.Focus(); return; }
        var editTiming = _row is null || EditTimingInput.IsChecked == true;
        var startedAt = _original?.StartedAtUtc ?? DateTimeOffset.Now;
        var minutes = _initialMinutes;
        if (editTiming)
        {
            if (DateInput.SelectedDate is not { } date || !TimeSpan.TryParse(TimeInput.Text, CultureInfo.CurrentCulture, out var time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Enter a valid date and start time."); TimeInput.Focus(); return; }
            if (!double.TryParse(DurationInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out minutes))
            { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate("Duration and pages must be numbers."); DurationInput.Focus(); return; }
            // A tidy display is not a time correction. Preserve the canonical
            // seconds when the numeric input still matches its initial display,
            // even if the user opts in or shifts only the start time.
            if (_original is not null && minutes == _initialDisplayedMinutes) minutes = _initialMinutes;
            var local = date.Date.Add(time);
            if (_original is not null && local == _initialLocalStart && minutes == _initialMinutes) editTiming = false;
            else
            {
                if (TimeZoneInfo.Local.IsInvalidTime(local) || TimeZoneInfo.Local.IsAmbiguousTime(local))
                { ErrorText.Text = Text("This local time is skipped or repeated by daylight saving. Choose an unambiguous start time.", "Giờ địa phương này bị bỏ qua hoặc lặp lại do đổi giờ mùa hè. Chọn giờ bắt đầu không bị trùng."); TimeInput.Focus(); return; }
                startedAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }
        }
        SaveButton.IsEnabled = false;
        try
        {
            var error = await _viewModel.SaveHistoricalSessionAsync(_row?.Id, book.Id, startedAt, minutes, startPage, endPage, NoteInput.Text, editTiming);
            if (error is not null) { ErrorText.Text = ReadestStats.Localization.Localizer.Instance.Translate(error); return; }
            if (IsVisible) DialogResult = true;
        }
        catch (Exception)
        {
            ErrorText.Text = Text("The session could not be saved. Keep this editor open and retry after the current app operation finishes.", "Chưa lưu được phiên. Giữ cửa sổ này và thử lại sau khi thao tác hiện tại kết thúc.");
        }
        finally { SaveButton.IsEnabled = true; }
    }
}
