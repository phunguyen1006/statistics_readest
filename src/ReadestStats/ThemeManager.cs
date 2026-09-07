using System.Windows;
using System.Windows.Media;

namespace ReadestStats;

public static class ThemeManager
{
    public static void Apply(string theme)
    {
        var dark = theme == "Dark" || (theme == "System" && Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0);
        var colors = dark
            ? new Dictionary<string, string> { ["AppBackground"] = "#0F172A", ["Surface"] = "#172033", ["SurfaceAlt"] = "#1E293B", ["Primary"] = "#60A5FA", ["Accent"] = "#F59E0B", ["TextPrimary"] = "#F8FAFC", ["TextSecondary"] = "#CBD5E1", ["Border"] = "#334155", ["Success"] = "#4ADE80" }
            : new Dictionary<string, string> { ["AppBackground"] = "#F8FAFC", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#EFF6FF", ["Primary"] = "#1E40AF", ["Accent"] = "#D97706", ["TextPrimary"] = "#172554", ["TextSecondary"] = "#475569", ["Border"] = "#DBEAFE", ["Success"] = "#15803D" };
        foreach (var item in colors) Application.Current.Resources[item.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Value));
    }
}
