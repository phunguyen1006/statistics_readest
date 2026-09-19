using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ReadestStats.ViewModels;
using ReadestStats.Views;

namespace ReadestStats.UiTests;

public sealed class WpfSmokeTests
{
    [Fact]
    public void All_pages_construct_measure_and_arrange_on_sta()
    {
        RunOnSta(() =>
        {
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            using var viewModel = new MainViewModel(() => null, (_, _) => null);
            FrameworkElement[] views =
            [
                new OverviewView(), new ActivityView(), new SessionsView(), new BooksView(),
                new NotesView(), new GoalsView(), new StatisticsView(), new YearView(), new SettingsView()
            ];
            foreach (var view in views)
            {
                view.DataContext = viewModel;
                Layout(view);
            }
            var manual = new ManualLogView { DataContext = viewModel.Manual };
            Layout(manual);
            var addBook = new AddBookWindow { DataContext = viewModel.Manual };
            Layout(addBook);
            var sessionEditor = new SessionEditorWindow(viewModel.Manual);
            Layout(sessionEditor);
            var window = new MainWindow { DataContext = viewModel };
            foreach (var page in viewModel.Pages)
            {
                viewModel.SelectedPage = page;
                Layout(window);
            }
        });
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(1280, 800));
        element.Arrange(new Rect(0, 0, 1280, 800));
        element.UpdateLayout();
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
