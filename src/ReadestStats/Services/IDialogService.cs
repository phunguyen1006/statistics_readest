using ReadestStats.Core;

namespace ReadestStats.Services;

public interface IDialogService
{
    string? ChooseDatabase();
    string? ChooseExport(string extension, string filter);
    string? ChooseBackupImport();
    string? ChooseImport(string title, string filter);
    bool ConfirmImport(CatalogImportPreview preview);
    bool ConfirmMerge(string message);
    void ShowMessage(string message, string title = "Readest Stats", bool isError = false);
}
