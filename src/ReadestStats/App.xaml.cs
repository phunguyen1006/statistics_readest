using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ReadestStats.Services;
using ReadestStats.ViewModels;

namespace ReadestStats;

public partial class App : Application
{
    private readonly bool _startWorkspace;
    public App() : this(true) { }
    public App(bool startWorkspace) => _startWorkspace = startWorkspace;
    private static readonly HashSet<string> ReportedDispatcherErrors = [];

    public static IServiceProvider Services { get; private set; } = null!;

    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Register Core Services & Abstractions
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<IThemeService, WpfThemeService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // Register ViewModels
        services.AddSingleton<MainViewModel>();

        // Register Views
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services = ConfigureServices();
        var logger = Services.GetRequiredService<IAppLogger>();
        if (!_startWorkspace) return;
        logger.Log("Application starting");
        DispatcherUnhandledException += (_, args) =>
        {
            logger.Log("Unhandled UI error: " + args.Exception);
            var fatal = args.Exception is OutOfMemoryException or AccessViolationException;
            var errorKey = $"{args.Exception.GetType().FullName}:{args.Exception.Message}";
            if (ReportedDispatcherErrors.Add(errorKey))
            {
                ReadestStats.Localization.UiDialog.Show(fatal ? "Readest Stats encountered a fatal error and must restart. Your Readest library was not modified.\n\n" + args.Exception.Message : args.Exception.Message, "Readest Stats", MessageBoxButton.OK, fatal ? MessageBoxImage.Error : MessageBoxImage.Warning);
            }
            args.Handled = !fatal;
            if (fatal) Shutdown(1);
        };
        try
        {
            var viewModel = Services.GetRequiredService<MainViewModel>();
            var window = Services.GetRequiredService<MainWindow>();
            window.DataContext = viewModel;
            MainWindow = window;
            window.Closed += (_, _) => viewModel.Dispose();
            window.Show(); logger.Log("Main window shown");
            await viewModel.InitializeAsync(); logger.Log("Initialization complete");
        }
        catch (Exception ex)
        {
            logger.Log("Startup error: " + ex);
            ReadestStats.Localization.UiDialog.Show("Readest Stats could not start. See the diagnostic log for details.\n\n" + ex.Message, "Readest Stats", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    public static void Log(string message)
    {
        if (Services?.GetService<IAppLogger>() is { } logger)
        {
            logger.Log(message);
            return;
        }
        new FileAppLogger().Log(message);
    }
}
