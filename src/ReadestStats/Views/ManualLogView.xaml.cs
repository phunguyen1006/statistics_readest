using System.Windows;
using System.Windows.Controls;
using ReadestStats.Core;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class ManualLogView : UserControl
{
    public ManualLogView() => InitializeComponent();

    private void AddBook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualLogViewModel viewModel) return;
        viewModel.AddBookCommand.Execute(null);
        var dialog = new AddBookWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = viewModel
        };
        dialog.ShowDialog();
        if (viewModel.IsAddingBook) viewModel.CancelAddBookCommand.Execute(null);
    }

    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualLogViewModel viewModel || sender is not Button { DataContext: ManualSessionRow row }) return;
        if (ReadestStats.Localization.UiDialog.Show($"Delete the {row.DurationLabel} session for {row.BookTitle}?", "Delete manual session", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            viewModel.DeleteSessionCommand.Execute(row);
    }

    private void AddPastSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ManualLogViewModel viewModel) new SessionEditorWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void EditSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ManualLogViewModel viewModel && sender is Button { DataContext: ManualSessionRow row }) new SessionEditorWindow(viewModel, row) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void DeleteBook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualLogViewModel viewModel || sender is not Button { DataContext: ManualBook book }) return;
        if (ReadestStats.Localization.UiDialog.Show($"Delete {book.Title} and all of its manual sessions?", "Delete physical book", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            viewModel.DeleteBookCommand.Execute(book);
    }

    private void EditBook_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualLogViewModel viewModel || sender is not Button { DataContext: ManualBook book }) return;
        viewModel.BeginEditBook(book);
        var dialog = new AddBookWindow { Owner = Window.GetWindow(this), DataContext = viewModel };
        dialog.ShowDialog();
        if (viewModel.IsAddingBook) viewModel.CancelAddBookCommand.Execute(null);
    }

    private void DiscardSession_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ManualLogViewModel viewModel) return;
        if (ReadestStats.Localization.UiDialog.Show("Discard the active manual session? Its elapsed time will not be saved.", "Discard session", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            viewModel.DiscardSessionCommand.Execute(null);
    }
}
