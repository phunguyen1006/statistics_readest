using System.Windows;

namespace ReadestStats.Views;

public partial class ImportPreviewWindow : Window
{
    public ImportPreviewWindow() => InitializeComponent();
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
