using System.Windows;
using System.Windows.Controls;

namespace ReadestStats.Controls;

/// <summary>Arranges related sections in columns, stacking them when the viewport is narrow.</summary>
public sealed class AdaptiveColumnsPanel : Panel
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(AdaptiveColumnsPanel), new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty BreakpointProperty = DependencyProperty.Register(
        nameof(Breakpoint), typeof(double), typeof(AdaptiveColumnsPanel), new FrameworkPropertyMetadata(620d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveColumnsPanel), new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double Breakpoint { get => (double)GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private int ColumnCount(double width) => width < Breakpoint ? 1 : Math.Max(1, Columns);
    private UIElement[] VisibleChildren() => InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed).ToArray();

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : Math.Max(0, availableSize.Width);
        var children = VisibleChildren(); var columns = ColumnCount(width); var gap = Math.Max(0, Spacing);
        var itemWidth = Math.Max(0, (width - gap * (columns - 1)) / columns); var height = 0d;
        for (var i = 0; i < children.Length; i += columns)
        {
            var rowHeight = 0d;
            for (var j = i; j < Math.Min(i + columns, children.Length); j++)
            {
                children[j].Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, children[j].DesiredSize.Height);
            }
            if (i > 0) height += gap;
            height += rowHeight;
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = VisibleChildren(); var columns = ColumnCount(finalSize.Width); var gap = Math.Max(0, Spacing);
        var itemWidth = Math.Max(0, (finalSize.Width - gap * (columns - 1)) / columns); var top = 0d;
        for (var i = 0; i < children.Length; i += columns)
        {
            var rowHeight = children.Skip(i).Take(columns).Max(child => child.DesiredSize.Height);
            for (var j = i; j < Math.Min(i + columns, children.Length); j++)
                children[j].Arrange(new Rect((j - i) * (itemWidth + gap), top, itemWidth, rowHeight));
            top += rowHeight + gap;
        }
        return finalSize;
    }
}
