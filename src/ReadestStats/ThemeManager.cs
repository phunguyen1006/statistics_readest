using System.Windows;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;

namespace ReadestStats;

public static class ThemeManager
{
    public static void Apply(string? requestedTheme)
    {
        var theme = Normalize(requestedTheme);
        var light = theme == "Light" || theme == "System" && SystemUsesLightApps();
        foreach (var (key, value) in light ? Light : Dark)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen) brush.Color = color;
            else Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        foreach (Window window in Application.Current.Windows) InvalidateTree(window);
    }

    public static string Normalize(string? theme) => theme is "Dark" or "Light" or "System" ? theme : "System";

    private static bool SystemUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch { return false; }
    }

    private static void InvalidateTree(DependencyObject node)
    {
        var values = node.GetLocalValueEnumerator();
        while (values.MoveNext()) if (values.Current.Value is BindingExpression binding) binding.UpdateTarget();
        if (node is UIElement element) element.InvalidateVisual();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) InvalidateTree(VisualTreeHelper.GetChild(node, i));
    }

    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["AppBackground"] = "#0A0A0A", ["SidebarBackground"] = "#0D0D0D", ["Surface"] = "#111111", ["SurfaceAlt"] = "#151515", ["SurfaceHover"] = "#1A1A1A", ["SurfaceActive"] = "#242424", ["ErrorSurface"] = "#171717",
        ["Border"] = "#2B2B2B", ["BorderStrong"] = "#595959", ["TextPrimary"] = "#F5F5F5", ["TextSecondary"] = "#D0D0D0", ["TextMuted"] = "#A3A3A3", ["TextDisabled"] = "#737373",
        ["Primary"] = "#F5F5F5", ["OnPrimary"] = "#0A0A0A", ["PrimaryHover"] = "#DADADA", ["Focus"] = "#FFFFFF", ["ChartSecondary"] = "#B8B8B8", ["ChartTertiary"] = "#777777", ["ChartGrid"] = "#2B2B2B",
        ["ScrollThumbBrush"] = "#666666", ["ScrollThumbHover"] = "#949494", ["TooltipBackground"] = "#F5F5F5", ["TooltipForeground"] = "#0A0A0A",
        ["Heatmap0"] = "#171717", ["Heatmap1"] = "#303030", ["Heatmap2"] = "#575757", ["Heatmap3"] = "#909090", ["Heatmap4"] = "#F5F5F5", ["HeatmapText0"] = "#F5F5F5", ["HeatmapText1"] = "#F5F5F5", ["HeatmapText2"] = "#F5F5F5", ["HeatmapText3"] = "#0A0A0A", ["HeatmapText4"] = "#0A0A0A"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["AppBackground"] = "#F4F4F4", ["SidebarBackground"] = "#FFFFFF", ["Surface"] = "#FFFFFF", ["SurfaceAlt"] = "#F0F0F0", ["SurfaceHover"] = "#E7E7E7", ["SurfaceActive"] = "#DCDCDC", ["ErrorSurface"] = "#EEEEEE",
        ["Border"] = "#D4D4D4", ["BorderStrong"] = "#8A8A8A", ["TextPrimary"] = "#111111", ["TextSecondary"] = "#333333", ["TextMuted"] = "#5F5F5F", ["TextDisabled"] = "#8A8A8A",
        ["Primary"] = "#111111", ["OnPrimary"] = "#FFFFFF", ["PrimaryHover"] = "#303030", ["Focus"] = "#111111", ["ChartSecondary"] = "#4A4A4A", ["ChartTertiary"] = "#777777", ["ChartGrid"] = "#D8D8D8",
        ["ScrollThumbBrush"] = "#9A9A9A", ["ScrollThumbHover"] = "#686868", ["TooltipBackground"] = "#111111", ["TooltipForeground"] = "#FFFFFF",
        ["Heatmap0"] = "#ECECEC", ["Heatmap1"] = "#CFCFCF", ["Heatmap2"] = "#999999", ["Heatmap3"] = "#555555", ["Heatmap4"] = "#111111", ["HeatmapText0"] = "#111111", ["HeatmapText1"] = "#111111", ["HeatmapText2"] = "#111111", ["HeatmapText3"] = "#FFFFFF", ["HeatmapText4"] = "#FFFFFF"
    };
}
