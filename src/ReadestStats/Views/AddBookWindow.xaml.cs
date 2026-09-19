using System.ComponentModel;
using System.Windows;
using ReadestStats.ViewModels;

namespace ReadestStats.Views;

public partial class AddBookWindow : Window
{
    private ManualLogViewModel? _viewModel;

    public AddBookWindow()
    {
        InitializeComponent();
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
}
