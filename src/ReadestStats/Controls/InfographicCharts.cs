using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ReadestStats.Core;

namespace ReadestStats.Controls;

public abstract class InfographicElement : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(InfographicElement),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    protected InfographicElement() => Focusable = false;
    protected T[] Items<T>() => ItemsSource?.Cast<T>().ToArray() ?? [];

    private static void SourceChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is not InfographicElement chart) return;
        if (args.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= chart.CollectionChanged;
        if (args.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += chart.CollectionChanged;
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();
}

public sealed class MatrixChart : InfographicElement
{
    private readonly List<(Rect Rect, MatrixCell Cell)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear();
        var items = Items<MatrixCell>(); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this);
        if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No matrix data in this period")); return; }
        var rows = items.Max(item => item.Row) + 1; var columns = items.Max(item => item.Column) + 1;
        var left = Math.Clamp(ActualWidth * .13, 56, 128); var top = 24d; var gap = columns > 40 ? 1d : 2d;
        var cellWidth = Math.Max(2, (ActualWidth - left - 8 - gap * (columns - 1)) / columns);
        var cellHeight = Math.Clamp((ActualHeight - top - 20 - gap * (rows - 1)) / rows, 8, 19);
        var positive = items.Where(item => item.Value > 0).Select(item => item.Value).Order().ToArray();
        foreach (var row in items.GroupBy(item => item.Row).OrderBy(group => group.Key))
            Viz.Text(dc, Viz.Trim(row.First().RowLabel, 17), new(0, top + row.Key * (cellHeight + gap) + 1), 9, muted, dpi);
        var labelStep = columns >= 24 ? Math.Max(1, columns / 8) : Math.Max(1, columns / 6);
        foreach (var item in items.OrderBy(item => item.Row).ThenBy(item => item.Column))
        {
            var rect = new Rect(left + item.Column * (cellWidth + gap), top + item.Row * (cellHeight + gap), cellWidth, cellHeight);
            var level = Viz.Bucket(item.Value, positive); var fill = (Brush)FindResource($"Heatmap{level}");
            dc.DrawRoundedRectangle(fill, level == 0 ? new Pen((Brush)FindResource("ChartGrid"), .7) : null, rect, 1.5, 1.5);
            if (item.Count > 1 && cellWidth >= 8) dc.DrawEllipse(level >= 3 ? (Brush)FindResource("HeatmapText4") : (Brush)FindResource("TextMuted"), null, new(rect.Right - 2.5, rect.Top + 2.5), 1.2, 1.2);
            _hits.Add((rect, item));
            if (item.Row == rows - 1 && (item.Column == 0 || item.Column == columns - 1 || item.Column % labelStep == 0))
                Viz.Text(dc, item.ColumnLabel, new(rect.Left, top + rows * (cellHeight + gap) + 3), 8, muted, dpi);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this)));
        ToolTip = hit.Cell is null ? null : hit.Cell.Detail; Cursor = hit.Cell is null ? Cursors.Arrow : Cursors.Hand;
    }
}

public sealed class TimelineBandChart : InfographicElement
{
    private readonly List<(Rect Rect, TimelineSpan Span)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = Items<TimelineSpan>();
        if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No sessions on this day")); return; }
        var muted = Viz.Muted(this); var grid = (Brush)FindResource("ChartGrid"); var primary = (Brush)FindResource("Primary"); var dpi = Viz.Dpi(this);
        var plot = new Rect(8, 24, Math.Max(1, ActualWidth - 16), Math.Max(24, ActualHeight - 38));
        foreach (var hour in new[] { 0, 6, 12, 18, 24 })
        {
            var x = plot.Left + plot.Width * hour / 24d; dc.DrawLine(new Pen(grid, 1), new(x, plot.Top - 4), new(x, plot.Bottom));
            Viz.Text(dc, Localization.Localizer.Instance.Translate($"{hour % 24:00}:00"), new(Math.Min(x, plot.Right - 28), 2), 9, muted, dpi);
        }
        var laneHeight = Math.Clamp((plot.Height - Math.Max(0, items.Length - 1) * 4) / items.Length, 6, 15);
        foreach (var item in items)
        {
            var x = plot.Left + plot.Width * item.StartHour / 24d; var right = plot.Left + plot.Width * item.EndHour / 24d;
            var rect = new Rect(x, plot.Top + item.Lane * (laneHeight + 4), Math.Max(3, right - x), laneHeight);
            dc.DrawRoundedRectangle(primary, null, rect, 2, 2); _hits.Add((new Rect(rect.X - 3, rect.Y - 2, rect.Width + 6, rect.Height + 4), item));
            if (rect.Width > 70) Viz.Text(dc, Viz.Trim(item.Label, 18), new(rect.X + 5, rect.Y - 1), 9, (Brush)FindResource("AppBackground"), dpi);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this)));
        ToolTip = hit.Span is null ? null : hit.Span.Detail;
    }
}

public sealed class SessionDistributionChart : InfographicElement
{
    private readonly List<(Rect Rect, DotDatum Point)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = Items<DotDatum>();
        if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No sessions in this period")); return; }
        if (items.Length <= 15) DrawDots(dc, items); else DrawHistogram(dc, items);
    }

    private void DrawDots(DrawingContext dc, DotDatum[] items)
    {
        var muted = Viz.Muted(this); var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var dpi = Viz.Dpi(this);
        var plot = new Rect(8, 18, Math.Max(1, ActualWidth - 16), Math.Max(1, ActualHeight - 42)); var max = Math.Max(1, items.Max(item => item.Value));
        dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom));
        for (var i = 0; i <= 4; i++) { var x = plot.Left + plot.Width * i / 4; Viz.Text(dc, Localization.Localizer.Instance.Translate($"{max * i / 4:0.#}m"), new(x - 7, plot.Bottom + 7), 9, muted, dpi); }
        foreach (var (item, index) in items.Select((value, index) => (value, index)))
        {
            var center = new Point(plot.Left + plot.Width * item.Value / max, plot.Bottom - 9 - index % 3 * 13);
            dc.DrawEllipse(index == items.Length - 1 ? primary : (Brush)FindResource("Surface"), new Pen(primary, 1.5), center, index == items.Length - 1 ? 5 : 4, index == items.Length - 1 ? 5 : 4);
            _hits.Add((new Rect(center.X - 8, center.Y - 8, 16, 16), item));
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate($"{items.Length} individual sessions"), new(plot.Left, 0), 9, muted, dpi);
    }

    private void DrawHistogram(DrawingContext dc, DotDatum[] items)
    {
        var muted = Viz.Muted(this); var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var dpi = Viz.Dpi(this);
        var limits = new[] { 5d, 10d, 20d, 30d, 60d, double.MaxValue }; var labels = new[] { "<5m", "5–10", "10–20", "20–30", "30–60", "60m+" };
        var counts = limits.Select((limit, index) => items.Count(item => item.Value >= (index == 0 ? 0 : limits[index - 1]) && item.Value < limit)).ToArray();
        var plot = new Rect(8, 18, Math.Max(1, ActualWidth - 16), Math.Max(1, ActualHeight - 42)); var max = Math.Max(1, counts.Max()); var slot = plot.Width / counts.Length;
        dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom));
        for (var i = 0; i < counts.Length; i++)
        {
            var height = plot.Height * counts[i] / max; var rect = new Rect(plot.Left + i * slot + slot * .22, plot.Bottom - height, slot * .56, height);
            dc.DrawRoundedRectangle(primary, null, rect, 2, 2); Viz.Text(dc, labels[i], new(plot.Left + i * slot, plot.Bottom + 7), 9, muted, dpi);
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate($"{items.Length} sessions · grouped distribution"), new(plot.Left, 0), 9, muted, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this)));
        ToolTip = hit.Point is null ? null : hit.Point.Detail;
    }
}

public sealed class ScatterPlotChart : InfographicElement
{
    private readonly List<(Point Point, DotDatum Item)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = Items<DotDatum>();
        if (items.Length < 4) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("At least 4 active days are needed for a style map")); return; }
        var muted = Viz.Muted(this); var grid = (Brush)FindResource("ChartGrid"); var primary = (Brush)FindResource("Primary"); var dpi = Viz.Dpi(this);
        var plot = new Rect(38, 10, Math.Max(1, ActualWidth - 48), Math.Max(1, ActualHeight - 38)); var maxX = Math.Max(1, items.Max(item => item.Value)); var maxY = Math.Max(1, items.Max(item => item.Secondary));
        dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom)); dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Top), new(plot.Left, plot.Bottom));
        dc.DrawLine(new Pen(grid, .7), new(plot.Left + plot.Width / 2, plot.Top), new(plot.Left + plot.Width / 2, plot.Bottom)); dc.DrawLine(new Pen(grid, .7), new(plot.Left, plot.Top + plot.Height / 2), new(plot.Right, plot.Top + plot.Height / 2));
        foreach (var item in items)
        {
            var point = new Point(plot.Left + plot.Width * item.Value / maxX, plot.Bottom - plot.Height * item.Secondary / maxY);
            dc.DrawEllipse((Brush)FindResource("Surface"), new Pen(primary, 1.5), point, 4.5, 4.5); _hits.Add((point, item));
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate("sessions →"), new(plot.Right - 54, plot.Bottom + 8), 9, muted, dpi); Viz.Text(dc, Localization.Localizer.Instance.Translate("longer ↑"), new(0, plot.Top), 9, muted, dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var point = e.GetPosition(this); var hit = _hits.OrderBy(item => (item.Point - point).Length).FirstOrDefault();
        ToolTip = hit.Item is null || (hit.Point - point).Length > 14 ? null : hit.Item.Detail;
    }
}

public sealed class SegmentedBandChart : InfographicElement
{
    private readonly List<(Rect Rect, CompositionPart Item)> _hits = [];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = Items<CompositionPart>();
        if (items.Length == 0 || items.Sum(item => item.Value) <= 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No book attention to divide yet")); return; }
        var total = items.Sum(item => item.Value); var dpi = Viz.Dpi(this); var left = 0d; var band = new Rect(0, 22, ActualWidth, 30);
        foreach (var item in items)
        {
            var width = band.Width * item.Value / total; var rect = new Rect(left, band.Top, width, band.Height);
            var level = 4 - item.Index % 4; var fill = (Brush)FindResource($"Heatmap{Math.Max(1, level)}"); dc.DrawRectangle(fill, new Pen((Brush)FindResource("AppBackground"), 1), rect); _hits.Add((rect, item));
            if (width > 72) Viz.Text(dc, Viz.Trim(item.Label, 15), new(rect.Left + 6, rect.Top + 7), 10, (Brush)FindResource(level >= 3 ? "HeatmapText4" : "HeatmapText1"), dpi);
            left += width;
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate("0%"), new(0, 58), 9, Viz.Muted(this), dpi); Viz.Text(dc, Localization.Localizer.Instance.Translate("100% of reading time"), new(Math.Max(0, ActualWidth - 102), 58), 9, Viz.Muted(this), dpi);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : hit.Item.Detail;
    }
}

public sealed class DumbbellChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<DumbbellDatum>(); if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No comparison available")); return; }
        var muted = Viz.Muted(this); var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var surface = (Brush)FindResource("Surface"); var dpi = Viz.Dpi(this);
        var labelWidth = 96d; var valueWidth = 106d; var plotLeft = labelWidth; var plotRight = Math.Max(plotLeft + 40, ActualWidth - valueWidth); var rowHeight = Math.Max(30, ActualHeight / items.Length);
        foreach (var (item, index) in items.Select((value, index) => (value, index)))
        {
            var y = rowHeight * index + rowHeight / 2; var max = Math.Max(1, Math.Max(item.Current, item.Previous)); var currentX = plotLeft + (plotRight - plotLeft) * item.Current / max; var previousX = plotLeft + (plotRight - plotLeft) * item.Previous / max;
            Viz.Text(dc, item.Label, new(0, y - 8), 10, muted, dpi); dc.DrawLine(new Pen(grid, 2), new(plotLeft, y), new(plotRight, y)); dc.DrawLine(new Pen(primary, 1.5), new(previousX, y), new(currentX, y));
            dc.DrawEllipse(surface, new Pen(primary, 1.5), new(previousX, y), 4, 4); dc.DrawEllipse(primary, null, new(currentX, y), 4.5, 4.5);
            Viz.Text(dc, Localization.Localizer.Instance.Translate($"{item.PreviousLabel} → {item.CurrentLabel}"), new(plotRight + 8, y - 8), 10, primary, dpi);
        }
    }
}

public sealed class FingerprintChart : InfographicElement
{
    private readonly List<(Rect Rect, ChartPoint Item)> _hits = [];
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); _hits.Clear(); var items = Items<ChartPoint>(); if (items.Length == 0) return;
        var max = Math.Max(1, items.Max(item => item.Value)); var slot = ActualWidth / items.Length; var baseline = ActualHeight - 16; var primary = (Brush)FindResource("Primary"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this);
        foreach (var (item, index) in items.Select((value, index) => (value, index)))
        {
            var ratio = item.Value / max; var height = item.Value <= 0 ? 2 : 5 + ratio * Math.Max(8, ActualHeight - 28); var brush = primary.Clone(); brush.Opacity = item.Value <= 0 ? .12 : .28 + .72 * ratio; brush.Freeze(); var rect = new Rect(index * slot, baseline - height, Math.Max(1, slot * .56), height); dc.DrawRectangle(brush, null, rect); _hits.Add((new Rect(index * slot, 0, Math.Max(3, slot), ActualHeight), item));
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate("earlier"), new(0, ActualHeight - 13), 9, muted, dpi); Viz.Text(dc, Localization.Localizer.Instance.Translate("today"), new(Math.Max(0, ActualWidth - 28), ActualHeight - 13), 9, muted, dpi);
    }
    protected override void OnMouseMove(MouseEventArgs e) { var hit = _hits.FirstOrDefault(item => item.Rect.Contains(e.GetPosition(this))); ToolTip = hit.Item is null ? null : hit.Item.Detail; }
}

public sealed class MomentumChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<ChartPoint>(); if (items.Length < 4 || items.All(item => item.Value <= 0)) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("More calendar observations are needed for momentum")); return; }
        var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this); var plot = new Rect(8, 12, ActualWidth - 16, ActualHeight - 34); var max = Math.Max(1, items.Max(item => item.Value)); var points = items.Select((item, index) => new Point(plot.Left + plot.Width * index / Math.Max(1, items.Length - 1), plot.Bottom - plot.Height * item.Value / max)).ToArray();
        dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom)); dc.DrawGeometry(null, new Pen(primary, 2) { LineJoin = PenLineJoin.Round }, new StreamGeometryBuilder(points).Geometry); foreach (var point in points.Where((_, index) => index == points.Length - 1 || index % Math.Max(1, points.Length / 12) == 0)) dc.DrawEllipse(primary, null, point, 2.5, 2.5);
        Viz.Text(dc, items[0].Label, new(plot.Left, plot.Bottom + 7), 9, muted, dpi); Viz.Text(dc, items[^1].Label, new(plot.Right - 30, plot.Bottom + 7), 9, muted, dpi);
    }
}

public sealed class StaircaseChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<ChartPoint>(); if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No sessions to accumulate")); return; }
        var primary = (Brush)FindResource("Primary"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this); var plot = new Rect(8, 10, ActualWidth - 16, ActualHeight - 32); var max = Math.Max(1, items.Max(item => item.Value)); var pen = new Pen(primary, 2); Point? previous = null;
        for (var i = 0; i < items.Length; i++)
        {
            var point = new Point(plot.Left + plot.Width * i / Math.Max(1, items.Length - 1), plot.Bottom - plot.Height * items[i].Value / max);
            if (previous is not null) { dc.DrawLine(pen, previous.Value, new(point.X, previous.Value.Y)); dc.DrawLine(pen, new(point.X, previous.Value.Y), point); }
            dc.DrawEllipse(primary, null, point, 2.8, 2.8); previous = point;
        }
        Viz.Text(dc, items[0].Label, new(plot.Left, plot.Bottom + 7), 9, muted, dpi); Viz.Text(dc, Localization.Localizer.Instance.Translate($"{items[^1].Value:0.#}h total"), new(Math.Max(plot.Left, plot.Right - 48), 0), 9, primary, dpi);
    }
}

public sealed class GoalBulletChart : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(GoalProgress), typeof(GoalBulletChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public GoalProgress? Progress { get => (GoalProgress?)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (Progress is null) return; var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this);
        var track = new Rect(0, 19, Math.Max(1, ActualWidth), 11); dc.DrawRoundedRectangle(grid, null, track, 2, 2); var ratio = Math.Clamp(Progress.ProgressPercent / 100, 0, 1); dc.DrawRoundedRectangle(primary, null, new Rect(track.X, track.Y, track.Width * ratio, track.Height), 2, 2);
        var requiredX = track.Left + track.Width * Math.Clamp(Progress.ElapsedPercent / 100, 0, 1); dc.DrawLine(new Pen(muted, 1.5), new(requiredX, 12), new(requiredX, 36));
        if (Progress.Projected is not null && Progress.Target > 0)
        {
            var forecastX = track.Left + track.Width * Math.Clamp(Progress.Projected.Value / Progress.Target, 0, 1); var triangle = new StreamGeometry(); using var context = triangle.Open(); context.BeginFigure(new(forecastX, 7), true, true); context.LineTo(new(forecastX - 4, 1), true, false); context.LineTo(new(forecastX + 4, 1), true, false); triangle.Freeze(); dc.DrawGeometry(primary, null, triangle);
        }
        Viz.Text(dc, Localization.Localizer.Instance.Translate($"Actual {Progress.CurrentLabel}"), new(0, 38), 9, primary, dpi); var target = Viz.Format($"Target {Progress.TargetLabel}", 9, muted, dpi); dc.DrawText(target, new(Math.Max(0, ActualWidth - target.Width), 38));
    }
}

public sealed class GoalHistoryStrip : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<ChartPoint>(); if (items.Length == 0) return; var primary = (Brush)FindResource("Primary"); var border = (Brush)FindResource("BorderStrong"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this); var gap = 4d; var width = Math.Clamp((ActualWidth - gap * (items.Length - 1)) / items.Length, 5, 16); var left = Math.Max(0, (ActualWidth - (width + gap) * items.Length + gap) / 2);
        for (var i = 0; i < items.Length; i++) { var rect = new Rect(left + i * (width + gap), 7, width, 18); dc.DrawRoundedRectangle(items[i].Value > 0 ? primary : null, new Pen(border, 1), rect, 1.5, 1.5); }
        Viz.Text(dc, items[0].Label, new(left, 31), 8, muted, dpi); var last = Viz.Format(items[^1].Label, 8, muted, dpi); dc.DrawText(last, new(Math.Max(left, ActualWidth - last.Width), 31));
    }
}

public sealed class PaceChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<PaceDatum>(); if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("Set a yearly target to see pace")); return; }
        var primary = (Brush)FindResource("Primary"); var muted = Viz.Muted(this); var grid = (Brush)FindResource("ChartGrid"); var dpi = Viz.Dpi(this); var plot = new Rect(8, 10, ActualWidth - 16, ActualHeight - 32); var max = Math.Max(1, items.Max(item => Math.Max(item.Actual, item.Required))); var actual = new List<Point>(); var required = new List<Point>();
        for (var i = 0; i < items.Length; i++) { var x = plot.Left + plot.Width * i / Math.Max(1, items.Length - 1); actual.Add(new(x, plot.Bottom - plot.Height * items[i].Actual / max)); required.Add(new(x, plot.Bottom - plot.Height * items[i].Required / max)); }
        dc.DrawLine(new Pen(grid, 1), new(plot.Left, plot.Bottom), new(plot.Right, plot.Bottom)); dc.DrawGeometry(null, new Pen(primary, 2), new StreamGeometryBuilder(actual).Geometry); dc.DrawGeometry(null, new Pen(muted, 1.5) { DashStyle = DashStyles.Dash }, new StreamGeometryBuilder(required).Geometry);
        foreach (var point in actual) dc.DrawEllipse(primary, null, point, 2.5, 2.5); Viz.Text(dc, Localization.Localizer.Instance.Translate("actual —  required - -"), new(plot.Left, 0), 9, muted, dpi); Viz.Text(dc, items[0].Label, new(plot.Left, plot.Bottom + 7), 9, muted, dpi); Viz.Text(dc, items[^1].Label, new(plot.Right - 22, plot.Bottom + 7), 9, muted, dpi);
    }
}

public sealed class MonthlyGlyphChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<ChartPoint>().Take(12).ToArray(); if (items.Length == 0) return; var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this); var max = Math.Max(1, items.Max(item => item.Value)); var slot = ActualWidth / items.Length; var baseline = ActualHeight - 22;
        for (var i = 0; i < items.Length; i++) { var x = slot * i + slot / 2; var y = baseline - (baseline - 12) * items[i].Value / max; dc.DrawLine(new Pen(grid, 2), new(x, baseline), new(x, y)); dc.DrawEllipse(items[i].Value > 0 ? primary : (Brush)FindResource("Surface"), new Pen(primary, 1), new(x, y), 4, 4); Viz.Text(dc, items[i].Label, new(x - 10, baseline + 6), 9, muted, dpi); }
    }
}

public sealed class LollipopChart : InfographicElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var items = Items<ChartPoint>(); if (items.Length == 0) { Viz.Empty(dc, this, Localization.Localizer.Instance.Translate("No active days this year")); return; }
        var primary = (Brush)FindResource("Primary"); var grid = (Brush)FindResource("ChartGrid"); var muted = Viz.Muted(this); var dpi = Viz.Dpi(this); var max = Math.Max(1, items.Max(item => item.Value)); var rowHeight = ActualHeight / items.Length;
        for (var i = 0; i < items.Length; i++) { var y = rowHeight * i + rowHeight / 2; var left = 58d; var right = left + (ActualWidth - 132) * items[i].Value / max; Viz.Text(dc, items[i].Label, new(0, y - 8), 10, muted, dpi); dc.DrawLine(new Pen(grid, 2), new(left, y), new(right, y)); dc.DrawEllipse(primary, null, new(right, y), 4.5, 4.5); Viz.Text(dc, Formatters.Duration(items[i].Value * 60), new(ActualWidth - 66, y - 8), 10, primary, dpi); }
    }
}

public sealed class SessionGlyphs : FrameworkElement
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(nameof(Count), typeof(int), typeof(SessionGlyphs), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var brush = (Brush)FindResource("TextMuted"); for (var i = 0; i < Math.Min(Count, 4); i++) dc.DrawRectangle(brush, null, new Rect(i * 5, 1, 3, 8)); if (Count > 4) dc.DrawEllipse(brush, null, new(22, 5), 1.5, 1.5);
    }
}

internal static class Viz
{
    public static Brush Muted(FrameworkElement element) => (Brush)element.FindResource("TextMuted");
    public static double Dpi(Visual visual) => VisualTreeHelper.GetDpi(visual).PixelsPerDip;
    public static FormattedText Format(string text, double size, Brush brush, double dpi) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dpi);
    public static void Text(DrawingContext dc, string text, Point point, double size, Brush brush, double dpi) => dc.DrawText(Format(text, size, brush, dpi), point);
    public static void Empty(DrawingContext dc, FrameworkElement element, string message) => Text(dc, message, new(8, Math.Max(8, element.ActualHeight / 2 - 8)), 11, Muted(element), Dpi(element));
    public static int Bucket(double value, double[] positive) { if (value <= 0 || positive.Length == 0) return 0; var rank = Array.BinarySearch(positive, value); if (rank < 0) rank = ~rank; var percentile = (rank + 1d) / positive.Length; return percentile switch { <= .25 => 1, <= .5 => 2, <= .75 => 3, _ => 4 }; }
    public static string Trim(string text, int length) => text.Length <= length ? text : text[..Math.Max(1, length - 1)] + "…";
}
