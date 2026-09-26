using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using ReadestStats.Views;
using ReadestStats.ViewModels;

namespace ReadestStats;

public partial class MainWindow : Window
{
    private bool _isFullScreen;
    private bool _wasMaximized;
    private Rect _boundsBeforeFullScreen;
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    public int LoadedPageCount => _pages.Count;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        PreviewKeyDown += HandlePreviewKeyDown;
        Closing += (_, _) => SavePlacement();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is MainViewModel old) old.PropertyChanged -= ViewModelChanged;
            _pages.Clear();
            if (args.NewValue is MainViewModel current) { current.PropertyChanged += ViewModelChanged; ShowPage(current); }
        };
        Closed += (_, _) => { if (DataContext is MainViewModel vm) vm.PropertyChanged -= ViewModelChanged; };
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainViewModel vm && e.PropertyName == nameof(MainViewModel.SelectedPage)) ShowPage(vm);
    }

    private void ShowPage(MainViewModel vm)
    {
        if (!_pages.TryGetValue(vm.SelectedPage, out var page))
        {
            page = vm.SelectedPage switch
            {
                "Activity" => new ActivityView(), "Sessions" => new SessionsView(), "Manual log" => new ManualLogView { DataContext = vm.Manual },
                "Books" => new BooksView(), "Notes" => new NotesView(), "Goals" => new GoalsView(), "Statistics" => new StatisticsView(),
                "Year in Reading" => new YearView(), "Settings" => new SettingsView(), _ => new OverviewView()
            };
            _pages.Add(vm.SelectedPage, page);
        }
        PageHost.Content = page;
        page.Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        Localization.Localizer.RefreshBindings(page);
    }

    private void RestorePlacement()
    {
        var placement = WindowPlacementStore.Load();
        if (placement is null) return;
        var visible = placement.Width >= MinWidth && placement.Height >= MinHeight &&
                      placement.Left + placement.Width > SystemParameters.VirtualScreenLeft && placement.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                      placement.Top + placement.Height > SystemParameters.VirtualScreenTop && placement.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        if (!visible) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var bounds = _isFullScreen ? _boundsBeforeFullScreen : RestoreBounds;
        if (bounds.IsEmpty || bounds.Width < MinWidth || bounds.Height < MinHeight) bounds = new Rect(Left, Top, Width, Height);
        WindowPlacementStore.Save(new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, _isFullScreen ? _wasMaximized : WindowState == WindowState.Maximized));
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11 && !(e.Key == Key.Escape && _isFullScreen)) return;
        ToggleFullScreen();
        e.Handled = true;
    }

    private void CommandSearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        Dispatcher.BeginInvoke(() => { CommandSearchBox.Focus(); CommandSearchBox.SelectAll(); });
    }

    private void CommandSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var resultList = (CommandSearchBox.Parent as Panel)?.Children.OfType<ListBox>().FirstOrDefault();
        if (resultList is not null && e.Key is Key.Down or Key.Up or Key.Home or Key.End)
        {
            var count = resultList.Items.Count;
            if (count == 0) return;
            resultList.SelectedIndex = e.Key switch
            {
                Key.Home => 0,
                Key.End => count - 1,
                Key.Down => Math.Min(count - 1, Math.Max(0, resultList.SelectedIndex + 1)),
                _ => Math.Max(0, resultList.SelectedIndex - 1)
            };
            resultList.ScrollIntoView(resultList.SelectedItem);
            e.Handled = true;
            return;
        }
        if (e.Key != Key.Enter || viewModel.SelectedGlobalSearchResult is null) return;
        viewModel.OpenGlobalSearchResultCommand.Execute(viewModel.SelectedGlobalSearchResult);
        e.Handled = true;
    }

    private void CommandPaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.CloseCommandPaletteCommand.Execute(null);
    }

    private void CommandPalettePanel_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _wasMaximized = WindowState == WindowState.Maximized;
            _boundsBeforeFullScreen = RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
            return;
        }

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        if (_wasMaximized) WindowState = WindowState.Maximized;
        else
        {
            Left = _boundsBeforeFullScreen.Left;
            Top = _boundsBeforeFullScreen.Top;
            Width = _boundsBeforeFullScreen.Width;
            Height = _boundsBeforeFullScreen.Height;
        }
        _isFullScreen = false;
    }
}
