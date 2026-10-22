using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class AddBookWindow : Window
{
    private ManualLogViewModel? _viewModel;

    public AddBookWindow()
    {
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width * .96);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height * .96);
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Detach();
        _viewModel = e.NewValue as ManualLogViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManualLogViewModel.IsAddingBook) && _viewModel?.IsAddingBook == false) Close();
    }

    private void Detach()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _viewModel = null;
    }

    private void ChooseCover_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var dialog = new OpenFileDialog { Filter = "Cover images|*.jpg;*.jpeg;*.png;*.webp", Title = "Choose a book cover" };
        if (dialog.ShowDialog(this) == true) _viewModel.DraftCoverPath = dialog.FileName;
    }
}
