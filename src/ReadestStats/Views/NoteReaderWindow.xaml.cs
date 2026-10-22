using System.ComponentModel;
using System.Windows;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class NoteReaderWindow : Window
{
    private readonly MainViewModel _viewModel;

    public NoteReaderWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        var workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(320, workArea.Width - 32);
        MaxHeight = Math.Max(320, workArea.Height - 32);
        MinWidth = Math.Min(MinWidth, MaxWidth);
        MinHeight = Math.Min(MinHeight, MaxHeight);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
        Loaded += Reader_Loaded;
        Closed += Reader_Closed;
        PropertyChangedEventManager.AddHandler(viewModel, ViewModel_Changed, nameof(MainViewModel.SelectedNote));
    }

    private void Reader_Loaded(object sender, RoutedEventArgs e) => CloseButton.Focus();

    private void ViewModel_Changed(object? sender, PropertyChangedEventArgs e)
    {
        // A new note starts at its beginning without moving keyboard focus away from Next/Previous.
        ReaderScroll.ScrollToTop();
    }

    private void Reader_Closed(object? sender, EventArgs e)
    {
        PropertyChangedEventManager.RemoveHandler(_viewModel, ViewModel_Changed, nameof(MainViewModel.SelectedNote));
        Loaded -= Reader_Loaded;
        Closed -= Reader_Closed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
