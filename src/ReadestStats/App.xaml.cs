using System.Windows;
using System.IO;
using Microsoft.Win32;
using ReadestStats.ViewModels;
using ReadestStats.Views;

namespace ReadestStats;

public partial class App : Application
{
    private static readonly HashSet<string> ReportedDispatcherErrors = [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log("Application starting");
        DispatcherUnhandledException += (_, args) =>
        {
            Log("Unhandled UI error: " + args.Exception);
            var fatal = args.Exception is OutOfMemoryException or AccessViolationException;
            var errorKey = $"{args.Exception.GetType().FullName}:{args.Exception.Message}";
            if (ReportedDispatcherErrors.Add(errorKey))
            {
                MessageBox.Show(fatal ? "Readest Stats encountered a fatal error and must restart. Your Readest library was not modified.\n\n" + args.Exception.Message : args.Exception.Message, "Readest Stats", MessageBoxButton.OK, fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);
            }
            args.Handled = !fatal;
            if (fatal) Shutdown(1);
        };
        try
        {
            var viewModel = new MainViewModel(
                () => { var dialog = new OpenFileDialog { Title = "Select Readest statistics.db", Filter = "Readest statistics database|statistics.db|SQLite database|*.db" }; return dialog.ShowDialog() == true ? dialog.FileName : null; },
                (extension, filter) => { var dialog = new SaveFileDialog { DefaultExt = extension, Filter = filter, FileName = $"readest-stats-{DateTime.Now:yyyy-MM-dd}.{extension}" }; return dialog.ShowDialog() == true ? dialog.FileName : null; },
                Log,
                ThemeManager.Apply,
                () => { var dialog = new OpenFileDialog { Title = "Restore Readest Stats backup", Filter = "Readest Stats backup|*.zip" }; return dialog.ShowDialog() == true ? dialog.FileName : null; },
                (title, filter) => { var dialog = new OpenFileDialog { Title = title, Filter = filter }; return dialog.ShowDialog() == true ? dialog.FileName : null; },
                preview => new ImportPreviewWindow { Owner = Current.MainWindow, DataContext = preview }.ShowDialog() == true);
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
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReadestStats", "logs"); Directory.CreateDirectory(directory); var path = Path.Combine(directory, "readest-stats.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000)
            {
                for (var index = 2; index >= 1; index--) { var source = Path.Combine(directory, $"readest-stats.{index}.log"); var destination = Path.Combine(directory, $"readest-stats.{index + 1}.log"); if (File.Exists(source)) File.Move(source, destination, true); }
                File.Move(path, Path.Combine(directory, "readest-stats.1.log"), true);
            }
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
