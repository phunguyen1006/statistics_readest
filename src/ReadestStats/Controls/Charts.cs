using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ReadestStats.Core;

namespace ReadestStats.Controls;

public class BarChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(BarChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BarChart chart) return;
        if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.CollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.CollectionChanged;
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return;
        var primary = (Brush)FindResource("Primary"); var muted = (Brush)FindResource("Border"); var text = (Brush)FindResource("TextSecondary");
        var plot = new Rect(8, 8, Math.Max(1, ActualWidth - 16), Math.Max(1, ActualHeight - 34)); var max = Math.Max(1, items.Max(x => x.Value)); var gap = items.Length <= 31 ? 3 : 1; var width = Math.Max(1, plot.Width / items.Length - gap);
        dc.DrawLine(new Pen(muted, 1), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        for (var i = 0; i < items.Length; i++)
        {
            var h = plot.Height * items[i].Value / max; var rect = new Rect(plot.Left + i * plot.Width / items.Length, plot.Bottom - h, width, h); dc.DrawRoundedRectangle(primary, null, rect, 2, 2);
            if (items.Length <= 31 && (i == 0 || i == items.Length - 1 || i % Math.Max(1, items.Length / 6) == 0))
            {
                var label = new FormattedText(items[i].Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(rect.X, plot.Bottom + 5));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var index = Math.Clamp((int)(e.GetPosition(this).X / Math.Max(1, ActualWidth) * items.Length), 0, items.Length - 1); var item = items[index]; ToolTip = $"{item.Label}\n{item.Detail ?? $"{item.Value:0.#}"}";
    }
}

public class HeatmapChart : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ChartPoint>), typeof(HeatmapChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public IEnumerable<ChartPoint>? ItemsSource { get => (IEnumerable<ChartPoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HeatmapChart chart) return; if (e.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.Changed; if (e.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.Changed;
    }
    private void Changed(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var values = items.Where(x => x.Value > 0).Select(x => Math.Log(1 + x.Value)).Order().ToArray(); var cap = values.Length == 0 ? 1 : values[(int)Math.Floor((values.Length - 1) * .9)];
        var empty = (Brush)FindResource("Border"); var primary = ((SolidColorBrush)FindResource("Primary")).Color; var cell = Math.Min(14, Math.Max(5, (ActualWidth - 8) / 53 - 2)); var gap = 2d;
        for (var i = 0; i < items.Length; i++)
        {
            var column = i / 7; var row = i % 7; var intensity = items[i].Value <= 0 ? 0 : Math.Clamp(Math.Log(1 + items[i].Value) / Math.Max(.1, cap), .18, 1); var brush = intensity == 0 ? empty : new SolidColorBrush(Color.FromArgb((byte)(70 + 185 * intensity), primary.R, primary.G, primary.B));
            dc.DrawRoundedRectangle(brush, null, new Rect(4 + column * (cell + gap), 4 + row * (cell + gap), cell, cell), 2, 2);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var items = ItemsSource?.ToArray() ?? []; if (items.Length == 0) return; var cell = Math.Min(14, Math.Max(5, (ActualWidth - 8) / 53 - 2)); var p = e.GetPosition(this); var index = Math.Clamp((int)((p.X - 4) / (cell + 2)) * 7 + (int)((p.Y - 4) / (cell + 2)), 0, items.Length - 1); ToolTip = $"{items[index].Label}\n{items[index].Detail}";
    }
}
