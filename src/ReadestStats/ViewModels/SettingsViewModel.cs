using System.Collections.ObjectModel;
using System.Windows.Input;
using ReadestStats.Core;

namespace ReadestStats.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Status)) Raise(nameof(Status));
            if (e.PropertyName == nameof(MainViewModel.Theme)) Raise(nameof(Theme));
            if (e.PropertyName == nameof(MainViewModel.Language)) Raise(nameof(Language));
            if (e.PropertyName == nameof(MainViewModel.AutomaticBackups)) Raise(nameof(AutomaticBackups));
            if (e.PropertyName == nameof(MainViewModel.SelectedBackup)) Raise(nameof(SelectedBackup));
            if (e.PropertyName == nameof(MainViewModel.SelectedAuditEvent)) Raise(nameof(SelectedAuditEvent));
            if (e.PropertyName == nameof(MainViewModel.AuditPreview)) Raise(nameof(AuditPreview));
            if (e.PropertyName == nameof(MainViewModel.AuditReason)) Raise(nameof(AuditReason));
            if (e.PropertyName == nameof(MainViewModel.UpdateStatus)) Raise(nameof(UpdateStatus));
            if (e.PropertyName == nameof(MainViewModel.DataQuality)) Raise(nameof(DataQuality));
            if (e.PropertyName == nameof(MainViewModel.Diagnostics)) Raise(nameof(Diagnostics));
            if (e.PropertyName == nameof(MainViewModel.HasSelectedHealthCheck)) Raise(nameof(HasSelectedHealthCheck));
            if (e.PropertyName == nameof(MainViewModel.SelectedHealthCheck)) Raise(nameof(SelectedHealthCheck));
        };
    }

    public MainViewModel Main => _main;
    public ManualLogViewModel Manual => _main.Manual;

    public double ViewportWidth { get => _main.ViewportWidth; set => _main.ViewportWidth = value; }
    public string StorageHealthHeading => _main.StorageHealthHeading;
    public string RefreshHealthLabel => _main.RefreshHealthLabel;
    public string BackupHealthLabel => _main.BackupHealthLabel;
    public string RetryRefreshLabel => _main.RetryRefreshLabel;
    public string RetryMirrorLabel => _main.RetryMirrorLabel;
    public string CreateBackupLabel => _main.CreateBackupLabel;
    public ICommand RefreshCommand => _main.RefreshCommand;
    public ICommand CreateAutomaticBackupCommand => _main.CreateAutomaticBackupCommand;
    public ICommand ChangeDatabaseCommand => _main.ChangeDatabaseCommand;
    public ICommand RedetectCommand => _main.RedetectCommand;
    public ICommand OpenFolderCommand => _main.OpenFolderCommand;
    public string Status => _main.Status;
    public DiagnosticsInfo Diagnostics => _main.Diagnostics;
    public int AllSessionCount => _main.AllSessionCount;
    public string PageMetricReliability => _main.PageMetricReliability;
    public DataQualityReport DataQuality => _main.DataQuality;
    public ObservableCollection<DataQualityCheck> DataQualityChecks => _main.DataQualityChecks;
    public bool HasSelectedHealthCheck => _main.HasSelectedHealthCheck;
    public DataQualityCheck? SelectedHealthCheck { get => _main.SelectedHealthCheck; set => _main.SelectedHealthCheck = value; }
    public ObservableCollection<DataHealthIssue> HealthIssues => _main.HealthIssues;
    public ICommand ReviewDataQualityCommand => _main.ReviewDataQualityCommand;
    public ICommand OpenHealthIssueCommand => _main.OpenHealthIssueCommand;
    public string[] RefreshModes => _main.RefreshModes;
    public string RefreshMode { get => _main.RefreshMode; set => _main.RefreshMode = value; }
    public string GoogleBooksApiKey { get => _main.GoogleBooksApiKey; set => _main.GoogleBooksApiKey = value; }
    public int SessionGapMinutes { get => _main.SessionGapMinutes; set => _main.SessionGapMinutes = value; }
    public int MinimumSessionSeconds { get => _main.MinimumSessionSeconds; set => _main.MinimumSessionSeconds = value; }
    public string FirstDayOfWeek { get => _main.FirstDayOfWeek; set => _main.FirstDayOfWeek = value; }
    public bool ExperimentalPageMetrics { get => _main.ExperimentalPageMetrics; set => _main.ExperimentalPageMetrics = value; }
    public string[] ThemeOptions => _main.ThemeOptions;
    public string Theme { get => _main.Theme; set => _main.Theme = value; }
    public bool CompactMode { get => _main.CompactMode; set => _main.CompactMode = value; }
    public bool Use24HourTime { get => _main.Use24HourTime; set => _main.Use24HourTime = value; }
    public bool ReduceMotion { get => _main.ReduceMotion; set => _main.ReduceMotion = value; }
    public ICommand ExportCsvCommand => _main.ExportCsvCommand;
    public ICommand ExportJsonCommand => _main.ExportJsonCommand;
    public ICommand ExportMonthlyReportCommand => _main.ExportMonthlyReportCommand;
    public string MonthlyReviewExportHint => _main.MonthlyReviewExportHint;
    public ICommand ExportPhysicalLibraryCommand => _main.ExportPhysicalLibraryCommand;
    public ICommand ImportPhysicalLibraryCommand => _main.ImportPhysicalLibraryCommand;
    public ICommand BackupSettingsCommand => _main.BackupSettingsCommand;
    public ICommand ResetSettingsCommand => _main.ResetSettingsCommand;
    public string AppVersion => _main.AppVersion;
    public string BuildDate => _main.BuildDate;
    public string BuildCommit => _main.BuildCommit;
    public string UpdateStatus => _main.UpdateStatus;
    public ICommand CheckForUpdatesCommand => _main.CheckForUpdatesCommand;
    public ICommand OpenUpdateCommand => _main.OpenUpdateCommand;
    public string AppDatabaseSummary => _main.AppDatabaseSummary;
    public string AppDatabasePath => _main.AppDatabasePath;
    public bool AutomaticBackups { get => _main.AutomaticBackups; set => _main.AutomaticBackups = value; }
    public ICommand OpenLogsCommand => _main.OpenLogsCommand;
    public int BackupRetentionDays { get => _main.BackupRetentionDays; set => _main.BackupRetentionDays = value; }
    public ObservableCollection<BackupSnapshot> BackupHistory => _main.BackupHistory;
    public BackupSnapshot? SelectedBackup { get => _main.SelectedBackup; set => _main.SelectedBackup = value; }
    public ICommand RestoreSelectedBackupCommand => _main.RestoreSelectedBackupCommand;
    public ICommand DeleteSelectedBackupCommand => _main.DeleteSelectedBackupCommand;
    public ICommand OpenBackupFolderCommand => _main.OpenBackupFolderCommand;
    public string[] LanguageOptions => _main.LanguageOptions;
    public string Language { get => _main.Language; set => _main.Language = value; }
    public ObservableCollection<AuditEventRow> AuditEvents => _main.AuditEvents;
    public AuditEventRow? SelectedAuditEvent { get => _main.SelectedAuditEvent; set => _main.SelectedAuditEvent = value; }
    public string AuditPreview => _main.AuditPreview;
    public string AuditReason { get => _main.AuditReason; set => _main.AuditReason = value; }
    public ICommand ToggleAuditEventCommand => _main.ToggleAuditEventCommand;
    public ICommand RestoreBackupCommand => _main.RestoreBackupCommand;
    public ICommand CleanupCoverCacheCommand => _main.CleanupCoverCacheCommand;
    public ICommand SaveSettingsCommand => _main.SaveSettingsCommand;
}
