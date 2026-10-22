using ReadestStats.Core;

namespace ReadestStats.Services;

public interface IReadingDataCoordinator
{
    string SelectedRange { get; set; }
    string SelectedSource { get; set; }
    DateTime? CustomStart { get; set; }
    DateTime? CustomEnd { get; set; }
    int SessionGapMinutes { get; set; }
    int MinimumSessionSeconds { get; set; }

    IReadOnlyList<ReadingEvent> Events { get; }
    IReadOnlyList<BookModel> BookModels { get; }
    IReadOnlyList<ReadingEvent> PeriodEvents { get; }
    IReadOnlyList<DailyReading> AllDaily { get; }
    ReadingPeriod? Period { get; }
    ComparisonMetrics? Comparison { get; }
    AppSettings Settings { get; }
    bool IsConnected { get; }
    bool IsBusy { get; }
    string Status { get; }
    string Error { get; }

    event EventHandler? DataRefreshed;
    event EventHandler? FilterChanged;
    event EventHandler? SettingsChanged;

    Task InitializeAsync();
    Task RefreshAsync(bool showErrors = true, CancellationToken cancellationToken = default);
    Task ApplySourceSelectionAsync(bool recalculate = true);
}
