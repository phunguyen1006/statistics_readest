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
    private int _selectedIndex;
    public HeatmapChart() => Focusable = true;
    private static void SourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d is not HeatmapChart chart) return; if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.CollectionChanged; if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.CollectionChanged; }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        _hits.Clear(); var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var positive = items.Where(x => x.Value > 0).Select(x => x.Value).Order().ToArray(); var levels = Enumerable.Range(0, 5).Select(i => (Brush)FindResource($"Heatmap{i}")).ToArray(); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; var muted = (Brush)FindResource("TextMuted"); var first = DateOnly.TryParse(items[0].Label, out var parsed) ? parsed : new DateOnly(2026, 1, 5); var shift = ((int)first.DayOfWeek + 6) % 7; var columns = (int)Math.Ceiling((shift + items.Length) / 7d); var left = 31d; var top = 20d; var gap = 3d; var cell = Math.Clamp((ActualWidth - left - 10 - gap * columns) / Math.Max(1, columns), 6, 13);
        DrawText(dc, "Mon", new(0, top + 0 * (cell + gap) - 1), 9, muted, dpi); DrawText(dc, "Wed", new(0, top + 2 * (cell + gap) - 1), 9, muted, dpi); DrawText(dc, "Fri", new(0, top + 4 * (cell + gap) - 1), 9, muted, dpi);
        string? lastMonth = null;
        for (var i = 0; i < items.Length; i++)
        {
            var index = shift + i; var col = index / 7; var row = index % 7; var level = Bucket(items[i].Value, positive); var rect = new Rect(left + col * (cell + gap), top + row * (cell + gap), cell, cell); dc.DrawRoundedRectangle(levels[level], IsKeyboardFocused && i == _selectedIndex ? new Pen((Brush)FindResource("Focus"), 2) : null, rect, 2, 2); _hits.Add((rect, items[i]));
            if (DateOnly.TryParse(items[i].Label, out var date) && date.Day <= 7 && date.ToString("MMM") != lastMonth) { lastMonth = date.ToString("MMM"); DrawText(dc, lastMonth, new(rect.X, 1), 9, muted, dpi); }
        }
        var legendY = top + 7 * (cell + gap) + 5; DrawText(dc, "Less", new(left, legendY), 9, muted, dpi); var legendX = left + 27; for (var i = 0; i < levels.Length; i++) dc.DrawRoundedRectangle(levels[i], null, new(legendX + i * 14, legendY, 10, 10), 2, 2); DrawText(dc, "More", new(legendX + 73, legendY), 9, muted, dpi);
    }
    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(x => x.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : hit.Item.Detail ?? $"{hit.Item.Label}\n{hit.Item.Value:0.#} min"; Cursor = hit.Item is null ? Cursors.Arrow : Cursors.Hand; }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { var hit = _hits.Select((value, index) => (value, index)).FirstOrDefault(x => x.value.Rect.Contains(e.GetPosition(this))); if (hit.value.Item is null) return; _selectedIndex = hit.index; Focus(); InvalidateVisual(); if (ItemClickCommand?.CanExecute(hit.value.Item) == true) ItemClickCommand.Execute(hit.value.Item); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var previous = _selectedIndex;
        _selectedIndex = e.Key switch { Key.Left => Math.Max(0, _selectedIndex - 7), Key.Right => Math.Min(items.Length - 1, _selectedIndex + 7), Key.Up => Math.Max(0, _selectedIndex - 1), Key.Down => Math.Min(items.Length - 1, _selectedIndex + 1), _ => _selectedIndex };
        if (e.Key is Key.Enter or Key.Space) { var item = items[Math.Clamp(_selectedIndex, 0, items.Length - 1)]; if (ItemClickCommand?.CanExecute(item) == true) ItemClickCommand.Execute(item); e.Handled = true; }
        else if (_selectedIndex != previous) { ToolTip = items[_selectedIndex].Detail; InvalidateVisual(); e.Handled = true; }
    }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); var items = ItemsSource?.ToArray() ?? []; if (items.Length > 0) _selectedIndex = Math.Clamp(_selectedIndex, 0, items.Length - 1); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
    private static int Bucket(double value, double[] positive) { if (value <= 0 || positive.Length == 0) return 0; var rank = Array.BinarySearch(positive, value); if (rank < 0) rank = ~rank; var percentile = (rank + 1d) / positive.Length; return percentile switch { <= .25 => 1, <= .5 => 2, <= .75 => 3, _ => 4 }; }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
}

public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    private readonly List<(Rect Rect, ChartPoint Item)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear();
        var items = ItemsSource?.ToArray() ?? [];
        var muted = (Brush)FindResource("TextMuted"); var grid = (Brush)FindResource("ChartGrid"); var primary = (Brush)FindResource("Primary"); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (items.Length == 0 || items.All(item => item.Value <= 0)) { DrawText(dc, "Not enough reading history yet", new(8, Math.Max(8, ActualHeight / 2 - 8)), 12, muted, dpi); return; }
        var plot = new Rect(42, 13, Math.Max(1, ActualWidth - 54), Math.Max(1, ActualHeight - 44)); var max = Math.Max(1, items.Max(item => item.Value));
        for (var i = 0; i <= 2; i++) { var y = plot.Top + plot.Height * i / 2; dc.DrawLine(new Pen(grid, 1), new(plot.Left, y), new(plot.Right, y)); DrawText(dc, Compact(max * (2 - i) / 2), new(1, y - 7), 10, muted, dpi); }
        var points = new List<Point>(); var slot = items.Length == 1 ? 0d : plot.Width / (items.Length - 1);
        for (var i = 0; i < items.Length; i++)
        {
            var point = new Point(items.Length == 1 ? plot.Left + plot.Width / 2 : plot.Left + slot * i, plot.Bottom - plot.Height * Math.Max(0, items[i].Value) / max);
            points.Add(point); var hitWidth = items.Length == 1 ? plot.Width : Math.Max(12, slot); _hits.Add((new Rect(point.X - hitWidth / 2, plot.Top, hitWidth, plot.Height), items[i]));
        }
        if (points.Count > 1) dc.DrawGeometry(null, new Pen(primary, 2) { LineJoin = PenLineJoin.Round }, new StreamGeometryBuilder(points).Geometry);
        var bestIndex = Array.FindIndex(items, item => item == items.MaxBy(value => value.Value));
        for (var i = 0; i < points.Count; i++) dc.DrawEllipse(i == bestIndex ? primary : (Brush)FindResource("Surface"), new Pen(primary, i == bestIndex ? 2 : 1), points[i], i == bestIndex ? 4 : 2.5, i == bestIndex ? 4 : 2.5);
        var labelStep = Math.Max(1, (int)Math.Ceiling(items.Length / 6d));
        for (var i = 0; i < items.Length; i++) if (i == 0 || i == items.Length - 1 || i % labelStep == 0) DrawText(dc, items[i].Label, new(points[i].X - 10, plot.Bottom + 8), 10, muted, dpi);
        if (bestIndex >= 0) DrawText(dc, $"Peak · {items[bestIndex].Label}", new(Math.Min(plot.Right - 70, points[bestIndex].X + 7), Math.Max(0, points[bestIndex].Y - 20)), 10, primary, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : $"{hit.Item.Label}\n{hit.Item.Detail ?? Compact(hit.Item.Value)}"; }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
    private static string Compact(double value) => value >= 1000 ? $"{value / 1000:0.#}k" : value >= 100 ? $"{value:0}" : $"{value:0.#}";
}

public sealed class RadialClockChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(RadialClockChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    private readonly List<(Point Center, ChartPoint Item)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = ItemsSource?.Take(24).ToArray() ?? [];
        var muted = (Brush)FindResource("TextMuted"); var grid = (Brush)FindResource("ChartGrid"); var primary = (Brush)FindResource("Primary"); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var center = new Point(ActualWidth / 2, ActualHeight / 2); var outer = Math.Max(20, Math.Min(ActualWidth, ActualHeight) / 2 - 24); var inner = outer * .56;
        dc.DrawEllipse(null, new Pen(grid, 1), center, inner, inner); dc.DrawEllipse(null, new Pen(grid, 1), center, outer, outer);
        if (items.Length == 0 || items.All(item => item.Value <= 0)) { DrawCentered(dc, "NO ACTIVITY", center, 11, muted, dpi); return; }
        var max = items.Max(item => item.Value);
        for (var i = 0; i < items.Length; i++)
        {
            var angle = (i * 15 - 90) * Math.PI / 180; var normalized = max <= 0 ? 0 : items[i].Value / max; var endRadius = inner + (outer - inner) * (.18 + .82 * normalized);
            var start = new Point(center.X + Math.Cos(angle) * inner, center.Y + Math.Sin(angle) * inner); var end = new Point(center.X + Math.Cos(angle) * endRadius, center.Y + Math.Sin(angle) * endRadius);
            var brush = primary.Clone(); brush.Opacity = .22 + .78 * normalized; brush.Freeze(); dc.DrawLine(new Pen(brush, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end); _hits.Add((end, items[i]));
        }
        DrawCentered(dc, "24H", new(center.X, center.Y - 7), 17, primary, dpi); DrawCentered(dc, "READING", new(center.X, center.Y + 12), 9, muted, dpi);
        foreach (var (label, hour) in new[] { ("00", 0), ("06", 6), ("12", 12), ("18", 18) }) { var angle = (hour * 15 - 90) * Math.PI / 180; DrawCentered(dc, label, new(center.X + Math.Cos(angle) * (outer + 13), center.Y + Math.Sin(angle) * (outer + 13)), 9, muted, dpi); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this); var hit = _hits.OrderBy(item => (item.Center - point).Length).FirstOrDefault();
        ToolTip = hit.Item is null || (hit.Center - point).Length > 18 ? null : $"{hit.Item.Label}\n{hit.Item.Detail ?? $"{hit.Item.Value:0.#}"}";
    }
    private static void DrawCentered(DrawingContext dc, string text, Point center, double size, Brush brush, double dpi) { var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi); dc.DrawText(formatted, new(center.X - formatted.Width / 2, center.Y - formatted.Height / 2)); }
}

public sealed class StreakStripChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(StreakStripChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    private readonly List<(Rect Rect, ChartPoint Item)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return;
        var primary = (Brush)FindResource("Primary"); var border = (Brush)FindResource("BorderStrong"); var muted = (Brush)FindResource("TextMuted"); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var gap = 4d; var width = Math.Clamp((ActualWidth - gap * (items.Length - 1)) / items.Length, 5, 16); var total = width * items.Length + gap * (items.Length - 1); var left = Math.Max(0, (ActualWidth - total) / 2);
        for (var i = 0; i < items.Length; i++) { var rect = new Rect(left + i * (width + gap), 8, width, width); dc.DrawRoundedRectangle(items[i].Value > 0 ? primary : null, new Pen(border, 1), rect, 2, 2); _hits.Add((rect, items[i])); }
        DrawText(dc, "30 days ago", new(left, Math.Min(ActualHeight - 15, 31)), 9, muted, dpi); var today = new FormattedText("Today", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, muted, dpi); dc.DrawText(today, new(left + total - today.Width, Math.Min(ActualHeight - 15, 31)));
    }
    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : hit.Item.Detail; }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
}

public sealed class ComparisonBarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(ComparisonBarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = ItemsSource?.Take(2).ToArray() ?? []; var muted = (Brush)FindResource("TextMuted"); var primary = (Brush)FindResource("Primary"); var secondary = (Brush)FindResource("ChartTertiary"); var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (items.Length == 0) return; var max = Math.Max(1, items.Max(item => item.Value));
        for (var i = 0; i < items.Length; i++)
        {
            var y = 8 + i * 38; DrawText(dc, items[i].Label, new(0, y), 10, muted, dpi); var valueText = new FormattedText(items[i].Detail ?? $"{items[i].Value:0.#}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, primary, dpi); dc.DrawText(valueText, new(ActualWidth - valueText.Width, y));
            var track = new Rect(0, y + 19, Math.Max(1, ActualWidth), 7); dc.DrawRoundedRectangle(secondary, null, track, 3.5, 3.5); var fill = new Rect(0, y + 19, Math.Max(items[i].Value > 0 ? 3 : 0, track.Width * items[i].Value / max), 7); dc.DrawRoundedRectangle(primary, null, fill, 3.5, 3.5);
        }
    }
    private static void DrawText(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi), point);
}

public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var primary = (Brush)FindResource("Primary"); var track = (Brush)FindResource("ChartGrid"); var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 7); var thickness = Math.Clamp(radius * .16, 4, 8); dc.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        var ratio = Maximum <= 0 ? 0 : Math.Clamp(Value / Maximum, 0, 1); if (ratio > 0) { var geometry = new StreamGeometry(); using var context = geometry.Open(); var start = new Point(center.X, center.Y - radius); var angle = ratio * Math.PI * 2; var end = new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius); context.BeginFigure(start, false, false); context.ArcTo(end, new Size(radius, radius), 0, ratio > .5, SweepDirection.Clockwise, true, false); geometry.Freeze(); dc.DrawGeometry(null, new Pen(primary, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry); }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; var text = new FormattedText($"{ratio * 100:0}%", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 15, primary, dpi); dc.DrawText(text, new(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}

internal sealed class StreamGeometryBuilder
{
    public StreamGeometry Geometry { get; } = new();
    public StreamGeometryBuilder(IReadOnlyList<Point> points)
    {
        using var context = Geometry.Open(); context.BeginFigure(points[0], false, false); context.PolyLineTo(points.Skip(1).ToArray(), true, false); Geometry.Freeze();
    }
}
