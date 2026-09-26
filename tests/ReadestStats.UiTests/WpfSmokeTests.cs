using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ReadestStats.ViewModels;
using ReadestStats.Views;
using ReadestStats.Core;
using ReadestStats.Localization;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReadestStats.UiTests;

public sealed class WpfSmokeTests
{
    private static string _stage = "starting";
    [Fact]
    public void All_pages_construct_measure_and_arrange_on_sta()
    {
        RunOnSta(async () =>
        {
            var app = Application.Current as App ?? new App(startWorkspace: false);
            app.InitializeComponent();
            var root = Path.Combine(Path.GetTempPath(), "readest-v23-ui-" + Guid.NewGuid().ToString("N"));
            var dataRoot = Path.Combine(root, "ReadestStats"); Directory.CreateDirectory(dataRoot);
            var now = DateTimeOffset.Now.AddHours(-2);
            var data = new ManualReadingData
            {
                Books = [new() { Id = -1, Title = "Bitter Harvest", Authors = "Ann Rule", Isbn13 = "123", TotalPages = 574 }, new() { Id = -2, Title = "Bitter Harvest", Authors = "Ann Rule", Isbn13 = "123", TotalPages = 574, CompletedAtUtc = now }],
                Sessions = [new() { Id = "s1", BookId = -1, StartedAtUtc = now, EndedAtUtc = now.AddMinutes(30), DurationSeconds = 1800, StartPage = 1, EndPage = 30, Note = "Keep this user note in English." }, new() { Id = "s2", BookId = -2, StartedAtUtc = now.AddDays(-40), EndedAtUtc = now.AddDays(-40).AddMinutes(10), DurationSeconds = 600, StartPage = 1, EndPage = 10 }]
            };
            await new ManualReadingStore(Path.Combine(dataRoot, "manual-reading.json")).SaveAsync(data);
            await new SettingsStore(Path.Combine(dataRoot, "settings.json")).SaveAsync(new() { AutomaticBackups = false, Language = "English", PinnedBookKeys = ["manual:-2"], BookTracking = new() { ["manual:-2"] = new() { Status = "Reading", Plan = new() { Enabled = true, DailyMinutes = 20 } } } });
            using var viewModel = new MainViewModel(() => null, (_, _) => null, dataDirectory: root, confirmMerge: _ => true);
            _stage = "initialize";
            await viewModel.InitializeAsync();
            _stage = "construct pages";
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
            var importPreview = new ImportPreviewWindow { DataContext = new ReadestStats.Core.CatalogImportPreview("Goodreads", 1, 1, 0, [new("Book", "Author", null, 100, null)]) };
            Layout(importPreview);
            var window = new MainWindow { DataContext = viewModel };
            _stage = "navigate pages";
            Assert.Equal(1, window.LoadedPageCount);
            foreach (var page in viewModel.Pages)
            {
                viewModel.SelectedPage = page;
                Layout(window);
            }
            Assert.Equal(viewModel.Pages.Length, window.LoadedPageCount);
            viewModel.SelectedPage = "Today"; Assert.Equal(viewModel.Pages.Length, window.LoadedPageCount);

            _stage = "archive";
            viewModel.SelectedSource = "Manual"; viewModel.SelectedRange = "All time";
            var total = viewModel.Period!.TotalSeconds; var finished = viewModel.FinishedAllTime;
            viewModel.Manual.SelectedBook = viewModel.Manual.Books.Single(b => b.Id == -2);
            await InvokeAsync(viewModel.Manual, "ArchiveSelectedBookAsync");
            Assert.Equal(total, viewModel.Period!.TotalSeconds); Assert.Equal(finished, viewModel.FinishedAllTime);
            Assert.DoesNotContain(viewModel.Manual.Books, b => b.Id == -2); Assert.Single(viewModel.Manual.ArchivedBooks);
            await InvokeAsync(viewModel.Manual, "RestoreArchivedBookAsync");
            Assert.Equal(2, viewModel.Manual.Books.Count);
            _stage = "merge";
            viewModel.Manual.MergePrimary = viewModel.Manual.Books.Single(b => b.Id == -1);
            await InvokeAsync(viewModel.Manual, "MergeDuplicateBooksAsync");
            Assert.Single(viewModel.Manual.Books); Assert.Equal(2, viewModel.Manual.Sessions.Count); Assert.All(viewModel.Manual.Sessions, s => Assert.Equal(-1, s.BookId));
            var mergedSettings = await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync();
            Assert.Contains("manual:-1", mergedSettings.PinnedBookKeys); Assert.True(mergedSettings.BookTracking["manual:-1"].Plan!.Enabled);
            await InvokeAsync(viewModel.Manual, "UndoRecentActionAsync");
            Assert.Equal(2, viewModel.Manual.Books.Count); Assert.Equal(total, viewModel.Period!.TotalSeconds);
            var undoneSettings = await new SettingsStore(Path.Combine(dataRoot, "settings.json")).LoadAsync();
            Assert.Contains("manual:-2", undoneSettings.PinnedBookKeys); Assert.True(undoneSettings.BookTracking.ContainsKey("manual:-2"));

            _stage = "comparison";
            viewModel.SelectedRange = "30 days"; viewModel.CompareMode = "Custom";
            viewModel.CompareCustomStart = now.AddDays(-40).Date; viewModel.CompareCustomEnd = now.AddDays(-40).Date;
            Assert.Equal(600, viewModel.Comparison!.PreviousValue); Assert.Equal(1800, viewModel.Comparison.CurrentValue);
            Assert.Empty(viewModel.ComparisonTrend); Assert.Single(viewModel.ComparisonData); Assert.NotEmpty(viewModel.PeriodDumbbells);
            viewModel.CompareMode = "Off"; Assert.Null(viewModel.Comparison); Assert.Empty(viewModel.PeriodDumbbells);
            viewModel.CompareMode = "Previous period"; viewModel.TrendGranularity = "Month";
            Assert.All(viewModel.Trend.Concat(viewModel.ComparisonData), p => Assert.Equal("h", p.Unit));

            _stage = "restore workspace";
            await InvokeAsync(viewModel, "SaveSettingsAsync");
            var backupPath = Path.Combine(root, "workspace.zip");
            new AppDataBackupService(Path.Combine(dataRoot, "settings.json"), Path.Combine(dataRoot, "manual-reading.json")).Create(backupPath, "2.3.0");
            viewModel.SelectedRange = "Today";
            await InvokeAsync(viewModel, "RestoreAppBackupAsync", backupPath);
            Assert.Equal("30 days", viewModel.SelectedRange);
            Assert.Equal(2, viewModel.Manual.Books.Count);
            Assert.Equal(1800, viewModel.Period!.TotalSeconds);

            _stage = "localization";
            viewModel.Language = "Tiếng Việt";
            Assert.Equal("Lưu sách", Localizer.Instance.Translate("Save book"));
            Assert.Equal("Trang kết thúc không được vượt quá 574.", Localizer.Instance.Translate("End page cannot exceed 574."));
            Assert.Equal("Ghi chú giữ nguyên", Localizer.Instance.Translate("Ghi chú giữ nguyên"));
            Assert.Equal("Bitter Harvest", viewModel.Manual.Books[0].Title);
            foreach (var page in viewModel.Pages) { viewModel.SelectedPage = page; Layout(window); }
            Assert.Equal("Cài đặt", viewModel.PageTitle);
            viewModel.SelectedPage = "Manual log"; Layout(window);
            if (Environment.GetEnvironmentVariable("READEST_QA_OUTPUT") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output); ThemeManager.Apply("Light"); Layout(window);
                Render(window, Path.Combine(output, "manual-vi-light.png"));
                viewModel.SelectedPage = "Statistics"; Layout(window); Render(window, Path.Combine(output, "statistics-vi-light.png"));
                ThemeManager.Apply("Dark"); Layout(window); Render(window, Path.Combine(output, "statistics-vi-dark.png"));
            }
            _stage = "virtualization";
            viewModel.Language = "English"; Assert.Equal("Save book", Localizer.Instance.Translate("Save book"));
            viewModel.SelectedPage = "Books"; viewModel.BookFilter = "All";
            var sample = viewModel.Books.First();
            for (var i = 0; i < 2000; i++) viewModel.Books.Add(sample);
            Layout(window);
            var list = Descendants(window).OfType<ListBox>().First(l => ReferenceEquals(l.ItemsSource, viewModel.Books));
            Assert.InRange(Descendants(list).OfType<ListBoxItem>().Count(), 1, 99);
            viewModel.Dispose(); window.DataContext = null;
            try { Directory.Delete(root, true); } catch { }
        });
    }


    private static void Layout(FrameworkElement element)
    {
        if (element is Window window) element = (FrameworkElement)window.Content;
        Localizer.RefreshBindings(element);
        element.Measure(new Size(1280, 800));
        element.Arrange(new Rect(0, 0, 1280, 800));
        element.UpdateLayout();
    }

    private static Task InvokeAsync(object target, string method, params object[] args) => (Task)target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Window window) root = (DependencyObject)window.Content;
        if (root is not Visual) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    private static void Render(FrameworkElement element, string path)
    {
        if (element is Window window) element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }

    private static void RunOnSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () => { try { await action(); } catch (Exception ex) { failure = ex; } finally { dispatcher.InvokeShutdown(); } });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "WPF verification timed out at: " + _stage);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
