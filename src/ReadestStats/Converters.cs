using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReadestStats.Core;

namespace ReadestStats;

public sealed class PageWidthConverter : IValueConverter
{
    // Keep editorial grids fluid at normal sizes; scroll rather than clip at high DPI.
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Math.Max(parameter as string == "Responsive" ? 320 : 900, (value is double width && double.IsFinite(width) ? width : 0) - 52);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class AdaptiveColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value is double actual && double.IsFinite(actual) ? Math.Max(0, actual) : 0;
        var kind = parameter as string;
        if (kind is "Four" or "Five" or "Six")
        {
            var desired = kind switch { "Four" => 4, "Five" => 5, _ => 6 };
            return Math.Clamp((int)(Math.Max(0, width - 52) / 160), 1, desired);
        }
        return width < 620 ? kind == "Metrics" ? 2 : 1 : kind == "Two" ? 2 : 3;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SameDateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is DateOnly first && values[1] is DateOnly second && first == second;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}

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

public sealed class OneBasedIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int index ? (index + 1).ToString("00", culture) : "—";
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

public sealed class CoverImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            if (!uri.IsFile || uri.IsUnc || Path.GetPathRoot(uri.LocalPath) is not { Length: >= 2 } root || root[1] != ':') return null;
            path = uri.LocalPath;
            if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > ReadestStats.Core.CoverCacheService.MaxCoverBytes) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (int.TryParse(parameter?.ToString(), out var width)) image.DecodePixelWidth = width;
            image.UriSource = uri;
            image.EndInit();
            if (image.CanFreeze) image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FormatException or ArgumentException or System.Net.WebException) { return null; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
