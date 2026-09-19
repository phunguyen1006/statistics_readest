using System.Windows.Controls;
using ReadestStats.Core;
using ReadestStats.ViewModels;
namespace ReadestStats.Views;
public partial class BooksView : UserControl
{
    public BooksView() => InitializeComponent();

    private void BookList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is ListBox { SelectedItem: BookRow book })
            viewModel.SelectedBook = book;
    }
}
