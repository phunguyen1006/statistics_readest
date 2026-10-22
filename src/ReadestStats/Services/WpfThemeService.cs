namespace ReadestStats.Services;

public sealed class WpfThemeService : IThemeService
{
    public void ApplyTheme(string theme) => ThemeManager.Apply(theme);
}
