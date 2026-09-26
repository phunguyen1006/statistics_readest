using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace ReadestStats.Localization;

public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();
    private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _keys = [];
    private readonly List<(Regex Pattern, string Translation)> _patterns = [];
    private readonly CultureInfo _systemCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _systemUiCulture = CultureInfo.CurrentUICulture;
    public bool IsVietnamese { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private Localizer()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("ReadestStats.Localization.UiStrings.tsv")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var columns = line.Split('\t', 2); if (columns.Length != 2) continue;
            var source = columns[0].Replace("\\n", "\n"); var translation = columns[1].Replace("\\n", "\n");
            _translations[source] = translation; _keys[Key(source)] = source;
        }
        foreach (var pair in _translations.Where(p => Regex.IsMatch(p.Key, @"\{\d+\}")).OrderByDescending(p => Regex.Replace(p.Key, @"\{\d+\}", "").Length))
        {
            var pattern = string.Concat(Regex.Split(pair.Key, @"(\{\d+\})").Select(part => Regex.IsMatch(part, @"^\{\d+\}$") ? "(?<p" + part[1..^1] + ">.*?)" : Regex.Escape(part)));
            _patterns.Add((new Regex("^" + pattern + "$", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50)), pair.Value));
        }
    }
    public static string Key(string source) => "s" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.ToLowerInvariant())))[..16];
    public string this[string key] => Translate(_keys.GetValueOrDefault(key, key));
    public bool HasTranslation(string source) => _translations.ContainsKey(source);
    public string Translate(string? text)
    {
        if (string.IsNullOrEmpty(text) || !IsVietnamese) return text ?? "";
        if (_translations.TryGetValue(text, out var translated)) return text.Any(char.IsLetter) && text == text.ToUpperInvariant() ? translated.ToUpper(CultureInfo.GetCultureInfo("vi-VN")) : translated;
        foreach (var (pattern, translation) in _patterns)
        {
            var match = pattern.Match(text); if (!match.Success) continue;
            return Regex.Replace(translation, @"\{(\d+)\}", m => match.Groups["p" + m.Groups[1].Value].Value);
        }
        return text;
    }
    public void SetLanguage(string language)
    {
        IsVietnamese = language == "Tiếng Việt" || language == "System" && _systemUiCulture.TwoLetterISOLanguageName == "vi";
        var culture = language == "System" ? _systemCulture : CultureInfo.GetCultureInfo(IsVietnamese ? "vi-VN" : "en-US");
        CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = language == "System" ? _systemUiCulture : culture;
        CultureInfo.DefaultThreadCurrentCulture = culture; CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        if (Application.Current is { } app)
            foreach (Window window in app.Windows) { window.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag); RefreshBindings(window); }
    }
    public static void RefreshBindings(DependencyObject element)
    {
        var values = element.GetLocalValueEnumerator();
        while (values.MoveNext()) if (values.Current.Value is BindingExpression binding) binding.UpdateTarget();
        if (element is UIElement ui) ui.InvalidateVisual();
        if (element is Visual)
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) RefreshBindings(VisualTreeHelper.GetChild(element, i));
    }
}

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

public sealed class UiTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is string text ? Localizer.Instance.Translate(text) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public static class UiDialog
{
    public static MessageBoxResult Show(string text, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None) =>
        MessageBox.Show(Localizer.Instance.Translate(text), Localizer.Instance.Translate(caption), button, image);
}
