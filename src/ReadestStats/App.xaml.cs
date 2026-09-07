using System.Windows;
using System.IO;
using Microsoft.Win32;
using ReadestStats.ViewModels;

namespace ReadestStats;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("Application starting");
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Readest Stats", MessageBoxButton.OK, MessageBoxImage.Warning); args.Handled = true; };
        try
        {
            var viewModel = new MainViewModel(
                () => { var dialog = new OpenFileDialog { Title = "Select Readest statistics.db", Filter = "Readest statistics database|statistics.db|SQLite database|*.db" }; return dialog.ShowDialog() == true ? dialog.FileName : null; },
                (extension, filter) => { var dialog = new SaveFileDialog { DefaultExt = extension, Filter = filter, FileName = $"readest-stats-{DateTime.Now:yyyy-MM-dd}.{extension}" }; return dialog.ShowDialog() == true ? dialog.FileName : null; });
            ThemeManager.Apply("System");
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Closed += (_, _) => viewModel.Dispose();
            window.Show(); Log("Main window shown");
            await viewModel.InitializeAsync(); Log("Initialization complete");
        }
        catch (Exception ex)
        {
            Log("Startup error: " + ex);
            MessageBox.Show("Readest Stats could not start. See the diagnostic log for details.\n\n" + ex.Message, "Readest Stats", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void Log(string message)
    {
        try { var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadestStats", "logs"); Directory.CreateDirectory(directory); File.AppendAllText(Path.Combine(directory, "readest-stats.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }
}
