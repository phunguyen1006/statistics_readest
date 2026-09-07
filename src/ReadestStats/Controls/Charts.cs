using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ReadestStats.Core;

namespace ReadestStats.Controls;

public sealed class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(BarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));
    public static readonly DependencyProperty MaxBarWidthProperty = DependencyProperty.Register(nameof(MaxBarWidth), typeof(double), typeof(BarChart), new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public double MaxBarWidth { get => (double)GetValue(MaxBarWidthProperty); set => SetValue(MaxBarWidthProperty, value); }
    private readonly List<(Rect Rect, ChartPoint Item)> _hits = [];
    private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d is not BarChart chart) return; if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.CollectionChanged; if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.CollectionChanged; }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = ItemsSource?.ToArray() ?? []; var textBrush = (Brush)FindResource("TextMuted"); var gridBrush = (Brush)FindResource("ChartGrid"); var primary = (Brush)FindResource("Primary"); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (items.Length == 0 || items.All(x => x.Value <= 0)) { DrawText(dc, "No activity in this period", new Point(12, Math.Max(12, ActualHeight / 2 - 8)), 12, textBrush, dpi); return; }
        var plot = new Rect(43, 12, Math.Max(1, ActualWidth - 51), Math.Max(1, ActualHeight - 42)); var max = Math.Max(1, items.Max(x => x.Value));
        for (var i = 0; i <= 3; i++) { var y = plot.Top + plot.Height * i / 3; dc.DrawLine(new Pen(gridBrush, 1), new(plot.Left, y), new(plot.Right, y)); DrawText(dc, Compact(max * (3 - i) / 3), new(1, y - 7), 10, textBrush, dpi); }
        var slot = plot.Width / items.Length; var width = Math.Max(2, Math.Min(MaxBarWidth, slot * .62));
        for (var i = 0; i < items.Length; i++)
        {
            var h = plot.Height * Math.Max(0, items[i].Value) / max; var rect = new Rect(plot.Left + slot * i + (slot - width) / 2, plot.Bottom - h, width, h); dc.DrawRoundedRectangle(primary, null, rect, Math.Min(2, width / 2), Math.Min(2, width / 2)); _hits.Add((new Rect(plot.Left + slot * i, plot.Top, slot, plot.Height), items[i]));
            var step = Math.Max(1, (int)Math.Ceiling(items.Length / 7d)); if (i == 0 || i == items.Length - 1 || i % step == 0) DrawText(dc, items[i].Label, new(plot.Left + slot * i, plot.Bottom + 7), 10, textBrush, dpi);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(x => x.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : $"{hit.Item.Label}\n{hit.Item.Detail ?? Compact(hit.Item.Value)}"; }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
    private static string Compact(double value) => value >= 1000 ? $"{value / 1000:0.#}k" : value >= 100 ? $"{value:0}" : $"{value:0.#}";
}

public sealed class HeatmapChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(HeatmapChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));
    public static readonly DependencyProperty ItemClickCommandProperty = DependencyProperty.Register(nameof(ItemClickCommand), typeof(ICommand), typeof(HeatmapChart));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ICommand? ItemClickCommand { get => (ICommand?)GetValue(ItemClickCommandProperty); set => SetValue(ItemClickCommandProperty, value); }
    private readonly List<(Rect Rect, ChartPoint Item)> _hits = [];
    private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d is not HeatmapChart chart) return; if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.CollectionChanged; if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.CollectionChanged; }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        _hits.Clear(); var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var positive = items.Where(x => x.Value > 0).Select(x => x.Value).Order().ToArray(); var levels = Enumerable.Range(0, 5).Select(i => (Brush)FindResource($"Heatmap{i}")).ToArray(); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; var muted = (Brush)FindResource("TextMuted"); var first = DateOnly.TryParse(items[0].Label, out var parsed) ? parsed : new DateOnly(2026, 1, 5); var shift = ((int)first.DayOfWeek + 6) % 7; var columns = (int)Math.Ceiling((shift + items.Length) / 7d); var left = 31d; var top = 20d; var gap = 3d; var cell = Math.Clamp((ActualWidth - left - 10 - gap * columns) / Math.Max(1, columns), 6, 13);
        DrawText(dc, "Mon", new(0, top + 0 * (cell + gap) - 1), 9, muted, dpi); DrawText(dc, "Wed", new(0, top + 2 * (cell + gap) - 1), 9, muted, dpi); DrawText(dc, "Fri", new(0, top + 4 * (cell + gap) - 1), 9, muted, dpi);
        string? lastMonth = null;
        for (var i = 0; i < items.Length; i++)
        {
            var index = shift + i; var col = index / 7; var row = index % 7; var level = Bucket(items[i].Value, positive); var rect = new Rect(left + col * (cell + gap), top + row * (cell + gap), cell, cell); dc.DrawRoundedRectangle(levels[level], null, rect, 2, 2); _hits.Add((rect, items[i]));
            if (DateOnly.TryParse(items[i].Label, out var date) && date.Day <= 7 && date.ToString("MMM") != lastMonth) { lastMonth = date.ToString("MMM"); DrawText(dc, lastMonth, new(rect.X, 1), 9, muted, dpi); }
        }
        var legendY = top + 7 * (cell + gap) + 5; DrawText(dc, "Less", new(left, legendY), 9, muted, dpi); var legendX = left + 27; for (var i = 0; i < levels.Length; i++) dc.DrawRoundedRectangle(levels[i], null, new(legendX + i * 14, legendY, 10, 10), 2, 2); DrawText(dc, "More", new(legendX + 73, legendY), 9, muted, dpi);
    }
    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(x => x.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : hit.Item.Detail ?? $"{hit.Item.Label}\n{hit.Item.Value:0.#} min"; Cursor = hit.Item is null ? Cursors.Arrow : Cursors.Hand; }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { var hit = _hits.FirstOrDefault(x => x.Rect.Contains(e.GetPosition(this))); if (hit.Item is not null && ItemClickCommand?.CanExecute(hit.Item) == true) ItemClickCommand.Execute(hit.Item); }
    private static int Bucket(double value, double[] positive) { if (value <= 0 || positive.Length == 0) return 0; var rank = Array.BinarySearch(positive, value); if (rank < 0) rank = ~rank; var percentile = (rank + 1d) / positive.Length; return percentile switch { <= .25 => 1, <= .5 => 2, <= .75 => 3, _ => 4 }; }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
}
