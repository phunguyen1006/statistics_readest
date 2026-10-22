using System.Windows.Threading;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed partial class MainViewModel
{
    private readonly DispatcherTimer _localTimeTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateOnly _lastObservedLocalDate = DateOnly.FromDateTime(DateTime.Today);
    private DateTimeOffset? _lastObservedNoteClockUtc;
    private bool _localClockAttached, _refreshingNoteClock;

    private void StartLocalClock()
    {
        if (_disposed || !_settingsLoaded || !CanInteract) return;
        if (!_localClockAttached)
        {
            _localClockAttached = true;
            _localTimeTimer.Tick += (_, _) => RefreshLocalTimeState();
        }
        RefreshLocalTimeState();
        _localTimeTimer.Start();
    }

    private void RefreshLocalTimeState() => RefreshLocalTimeStateAt(DateTimeOffset.UtcNow);

    // An explicit clock instant keeps boundary tests independent of dispatcher timing.
    private void RefreshLocalTimeStateAt(DateTimeOffset now)
    {
        if (_disposed || !_settingsLoaded || !CanInteract || IsBusy || _rediscoveryInitializationPending) return;
        try
        {
            var previousUtc = _lastObservedNoteClockUtc;
            var clockMovedBackward = previousUtc is { } previous && now < previous;
            // A command's CanExecute query may already have rebuilt an expired pool cache.
            // Observe actual note timestamps as well, so the visible labels/scopes cannot miss expiry.
            var crossedBoundaries = _allNoteRows.SelectMany(note => NoteClockBoundaries(note))
                .Where(boundary => previousUtc is { } observed && boundary > observed && boundary <= now);
            var change = LocalReadingClock.Observe(_lastObservedLocalDate, now, TimeZoneInfo.Local,
                _rediscoveryPoolCache.Values.Select(pool => pool.ValidUntilUtc).Concat(crossedBoundaries));
            if (!change.DateChanged && !change.EligibilityExpired && !clockMovedBackward)
            {
                _lastObservedNoteClockUtc = now;
                return;
            }
            _rediscoveryPoolCache.Clear();
            _refreshingNoteClock = true;
            try
            {
                foreach (var note in _allNoteRows.Where(note => note.State.SnoozedUntilUtc is { } until && (until <= now || clockMovedBackward)))
                    note.RefreshTemporalState();
            }
            finally { _refreshingNoteClock = false; }
            // Update availability, not discovery: don't advance a shuffle,
            // replace a reading preview, or increment seen/history counters.
            RefreshRediscoveryCommands();
            RefreshReaderCommands();
            if (!IsNoteReaderOpen && (NoteScope is "Snoozed" or "Not seen recently")) ApplyNoteFilters();
            if (change.DateChanged)
            {
                DailyNote = ChooseDailyNote();
            Manual.RefreshJournalDatePreset(change.Date.ToDateTime(TimeOnly.MinValue));
                BuildNoteAnalytics();
                RecalculateAll();
                NextMonthCommand.Refresh(); TodayCommand.Refresh();
                Raise(nameof(LatestReportDate));
            }
            // Commit the observation only after all derived UI refreshes succeed.
            // A failed tick can then retry a crossed boundary instead of losing it forever.
            _lastObservedLocalDate = change.Date;
            _lastObservedNoteClockUtc = now;
        }
        catch (Exception ex) { _log("Local date/availability refresh failed: " + ex); }
    }

    private static IEnumerable<DateTimeOffset> NoteClockBoundaries(NoteRow note)
    {
        if (note.State.SnoozedUntilUtc is { } snooze) yield return snooze;
        if (note.LastSeenUtc is { } seen && seen <= DateTimeOffset.MaxValue.AddDays(-30)) yield return seen.AddDays(30);
    }
}
