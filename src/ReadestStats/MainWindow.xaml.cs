using System.Windows;
using System.Windows.Input;

namespace ReadestStats;

public partial class MainWindow : Window
{
    private bool _isFullScreen;
    private bool _wasMaximized;
    private Rect _boundsBeforeFullScreen;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        PreviewKeyDown += HandlePreviewKeyDown;
        Closing += (_, _) => SavePlacement();
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
