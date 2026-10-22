using System.Windows;
using Microsoft.Win32;
using ReadestStats.Core;
using ReadestStats.Views;
using ReadestStats.Localization;

namespace ReadestStats.Services;

public sealed class WpfDialogService : IDialogService
{
    public string? ChooseDatabase()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Readest statistics.db",
            Filter = "Readest statistics database|statistics.db|SQLite database|*.db"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseExport(string extension, string filter)
    {
        var dialog = new SaveFileDialog
        {
            DefaultExt = extension,
            Filter = filter,
            FileName = $"readest-stats-{DateTime.Now:yyyy-MM-dd}.{extension}"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseBackupImport()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Restore Readest Stats backup",
            Filter = "Readest Stats backup|*.zip"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseImport(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool ConfirmImport(CatalogImportPreview preview)
    {
        var window = new ImportPreviewWindow
        {
            Owner = Application.Current?.MainWindow,
            DataContext = preview
        };
        return window.ShowDialog() == true;
    }

    public bool ConfirmMerge(string message)
    {
        return UiDialog.Show(message, "Merge physical editions", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }

    public void ShowMessage(string message, string title = "Readest Stats", bool isError = false)
    {
        UiDialog.Show(message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Warning);
    }
}
