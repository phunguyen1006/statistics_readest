using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;
using ReadestStats.Core;

namespace ReadestStats;

public sealed class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double seconds ? Formatters.Duration(seconds) : "—";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ClockConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not DateTimeOffset value) return "—";
        var use24HourTime = values.Length < 2 || values[1] is not bool setting || setting;
        return parameter?.ToString() switch
        {
            "date-time" => value.ToString(use24HourTime ? "MMM d, yyyy · HH:mm" : "MMM d, yyyy · h:mm tt", culture),
            "short-date-time" => value.ToString(use24HourTime ? "MMM d · HH:mm" : "MMM d · h:mm tt", culture),
            "full-date-time" => value.ToString(use24HourTime ? "MMM d, yyyy HH:mm" : "MMM d, yyyy h:mm tt", culture),
            "seconds" => value.ToString(use24HourTime ? "HH:mm:ss" : "h:mm:ss tt", culture),
            _ => value.ToString(use24HourTime ? "HH:mm" : "h:mm tt", culture)
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

public sealed class PageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 1d : .32d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class IntensityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CurrentBrush((value?.ToString()) switch { "Low" => "Heatmap1", "Medium" => "Heatmap2", "High" => "Heatmap3", "Peak" => "Heatmap4", _ => "Heatmap0" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush CurrentBrush(string key) => Application.Current.TryFindResource(key) is SolidColorBrush brush ? new SolidColorBrush(brush.Color) : Brushes.Transparent;
}

public sealed class IntensityForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        CurrentBrush((value?.ToString()) switch { "Low" => "HeatmapText1", "Medium" => "HeatmapText2", "High" => "HeatmapText3", "Peak" => "HeatmapText4", _ => "HeatmapText0" });
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush CurrentBrush(string key) => Application.Current.TryFindResource(key) is SolidColorBrush brush ? new SolidColorBrush(brush.Color) : Brushes.Transparent;
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is long bytes ? bytes < 1024 * 1024 ? $"{bytes / 1024d:0.0} KB" : $"{bytes / 1024d / 1024d:0.0} MB" : "—";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PercentChangeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double percent ? $"{percent:+0;-0;0}% vs previous period" : "No previous-period baseline";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
